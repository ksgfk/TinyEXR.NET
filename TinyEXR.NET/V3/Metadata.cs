using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Text;

namespace TinyEXR.V3
{
    /// <summary>
    /// Describes one native OpenEXR channel.
    /// </summary>
    public sealed class Channel
    {
        public Channel(string name, PixelType pixelType, int xSampling = 1, int ySampling = 1, bool perceptuallyLinear = false)
        {
            ModelValidation.ValidateName(name, nameof(name), allowEmpty: false);
            ModelValidation.ValidateEnum(pixelType, nameof(pixelType));
            if (xSampling <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(xSampling), xSampling, "Channel sampling must be positive.");
            }

            if (ySampling <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ySampling), ySampling, "Channel sampling must be positive.");
            }

            Name = name;
            PixelType = pixelType;
            XSampling = xSampling;
            YSampling = ySampling;
            PerceptuallyLinear = perceptuallyLinear;
        }

        public string Name { get; }

        public PixelType PixelType { get; }

        public int XSampling { get; }

        public int YSampling { get; }

        public bool PerceptuallyLinear { get; }
    }

    /// <summary>
    /// Raw on-disk payload for one parsed or user-supplied header attribute.
    /// </summary>
    public sealed class HeaderAttribute
    {
        private readonly byte[] _data;

        public HeaderAttribute(string name, string typeName, ReadOnlySpan<byte> data)
        {
            ModelValidation.ValidateName(name, nameof(name), allowEmpty: false);
            ModelValidation.ValidateName(typeName, nameof(typeName), allowEmpty: false);

            Name = name;
            TypeName = typeName;
            _data = data.ToArray();
        }

        private HeaderAttribute(string name, string typeName, byte[] data)
        {
            ModelValidation.ValidateName(name, nameof(name), allowEmpty: false);
            ModelValidation.ValidateName(typeName, nameof(typeName), allowEmpty: false);

            Name = name;
            TypeName = typeName;
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>
        /// Adopts parser-owned storage without a second copy. The caller must transfer exclusive ownership.
        /// </summary>
        internal static HeaderAttribute Adopt(string name, string typeName, byte[] data)
        {
            return new HeaderAttribute(name, typeName, data);
        }

        public string Name { get; }

        public string TypeName { get; }

        public ReadOnlySpan<byte> Data => _data;

        public int ByteLength => _data.Length;
    }

    /// <summary>
    /// Tile geometry for tiled and deep-tiled parts.
    /// </summary>
    public sealed class TileDescription
    {
        public TileDescription(
            uint tileSizeX,
            uint tileSizeY,
            TileLevelMode levelMode = TileLevelMode.OneLevel,
            TileRoundingMode roundingMode = TileRoundingMode.RoundDown)
        {
            if (tileSizeX == 0 || tileSizeX > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileSizeX),
                    tileSizeX,
                    $"Tile width must be between 1 and {int.MaxValue}.");
            }

            if (tileSizeY == 0 || tileSizeY > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileSizeY),
                    tileSizeY,
                    $"Tile height must be between 1 and {int.MaxValue}.");
            }

            ModelValidation.ValidateEnum(levelMode, nameof(levelMode));
            ModelValidation.ValidateEnum(roundingMode, nameof(roundingMode));

            TileSizeX = tileSizeX;
            TileSizeY = tileSizeY;
            LevelMode = levelMode;
            RoundingMode = roundingMode;
        }

        public uint TileSizeX { get; }

        public uint TileSizeY { get; }

        public TileLevelMode LevelMode { get; }

        public TileRoundingMode RoundingMode { get; }
    }

    /// <summary>
    /// Immutable description of one image part. Pixel ownership lives in the part levels.
    /// </summary>
    public sealed class Header
    {
        private readonly ReadOnlyCollection<Channel> _channels;
        private readonly ReadOnlyCollection<HeaderAttribute> _attributes;

        public Header(
            PartType partType,
            Box2i dataWindow,
            IEnumerable<Channel> channels,
            Compression compression = Compression.None,
            LineOrder lineOrder = LineOrder.IncreasingY,
            Box2i? displayWindow = null,
            float pixelAspectRatio = 1.0f,
            Vector2? screenWindowCenter = null,
            float screenWindowWidth = 1.0f,
            TileDescription? tiles = null,
            string? name = null,
            Chromaticities? chromaticities = null,
            IEnumerable<HeaderAttribute>? attributes = null)
        {
            ModelValidation.ValidateEnum(partType, nameof(partType));
            ModelValidation.ValidateEnum(compression, nameof(compression));
            ModelValidation.ValidateEnum(lineOrder, nameof(lineOrder));
            ModelValidation.ThrowIfNotPositiveFinite(pixelAspectRatio, nameof(pixelAspectRatio));
            ModelValidation.ThrowIfNotPositiveFinite(screenWindowWidth, nameof(screenWindowWidth));

            Vector2 center = screenWindowCenter ?? Vector2.Zero;
            ModelValidation.ThrowIfNotFinite(center.X, nameof(screenWindowCenter));
            ModelValidation.ThrowIfNotFinite(center.Y, nameof(screenWindowCenter));

            bool tiledPart = partType == PartType.Tiled || partType == PartType.DeepTiled;
            if (tiledPart != (tiles != null))
            {
                throw new ArgumentException(
                    tiledPart
                        ? "Tiled parts require a tile description."
                        : "Scanline parts must not have a tile description.",
                    nameof(tiles));
            }

            string effectiveName = name ?? string.Empty;
            ModelValidation.ValidateName(effectiveName, nameof(name), allowEmpty: true);

            List<Channel> channelList = ModelValidation.CopySortedUnique(
                channels,
                static channel => channel.Name,
                nameof(channels),
                "channel");
            if (channelList.Count == 0)
            {
                throw new ArgumentException("At least one channel is required.", nameof(channels));
            }

            List<HeaderAttribute> attributeList = attributes == null
                ? new List<HeaderAttribute>()
                : ModelValidation.CopyUnique(
                    attributes,
                    static attribute => attribute.Name,
                    nameof(attributes),
                    "attribute");

            PartType = partType;
            Compression = compression;
            LineOrder = lineOrder;
            DataWindow = dataWindow;
            DisplayWindow = displayWindow ?? dataWindow;
            PixelAspectRatio = pixelAspectRatio;
            ScreenWindowCenter = center;
            ScreenWindowWidth = screenWindowWidth;
            Tiles = tiles;
            Name = effectiveName;
            Chromaticities = chromaticities;
            _channels = channelList.AsReadOnly();
            _attributes = attributeList.AsReadOnly();
        }

        public PartType PartType { get; }

        public Compression Compression { get; }

        public LineOrder LineOrder { get; }

        public Box2i DataWindow { get; }

        public Box2i DisplayWindow { get; }

        public float PixelAspectRatio { get; }

        public Vector2 ScreenWindowCenter { get; }

        public float ScreenWindowWidth { get; }

        public TileDescription? Tiles { get; }

        public string Name { get; }

        public Chromaticities? Chromaticities { get; }

        public IReadOnlyList<Channel> Channels => _channels;

        public IReadOnlyList<HeaderAttribute> Attributes => _attributes;

        public bool IsDeep => PartType == PartType.DeepScanline || PartType == PartType.DeepTiled;

        public bool IsTiled => PartType == PartType.Tiled || PartType == PartType.DeepTiled;
    }

    internal static class ModelValidation
    {
        private const int MaxNameByteCount = 255;

        internal static UTF8Encoding StrictUtf8 { get; } = new UTF8Encoding(false, true);

        internal static IComparer<string> Utf8ByteComparer { get; } = new Utf8ByteLexicographicComparer();

        public static void ValidateEnum<T>(T value, string parameterName)
            where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "The value is not defined by the TinyEXR v3 API.");
            }
        }

        public static void ValidateName(string value, string parameterName, bool allowEmpty)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!allowEmpty && value.Length == 0)
            {
                throw new ArgumentException("The name must not be empty.", parameterName);
            }

            if (value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("Names must not contain a NUL character.", parameterName);
            }

            int byteCount;
            try
            {
                byteCount = StrictUtf8.GetByteCount(value);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "Names must contain only valid Unicode scalar values.",
                    parameterName,
                    exception);
            }

            if (byteCount > MaxNameByteCount)
            {
                throw new ArgumentException($"Names must fit in {MaxNameByteCount} UTF-8 bytes.", parameterName);
            }
        }

        public static void ThrowIfNotFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");
            }
        }

        public static void ThrowIfNotPositiveFinite(float value, string parameterName)
        {
            ThrowIfNotFinite(value, parameterName);
            if (value <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "The value must be positive.");
            }
        }

        public static List<T> CopySortedUnique<T>(
            IEnumerable<T> values,
            Func<T, string> getName,
            string parameterName,
            string elementDescription)
            where T : class
        {
            List<T> result = CopyUnique(values, getName, parameterName, elementDescription);
            result.Sort((left, right) => Utf8ByteComparer.Compare(getName(left), getName(right)));
            return result;
        }

        public static List<T> CopyUnique<T>(
            IEnumerable<T> values,
            Func<T, string> getName,
            string parameterName,
            string elementDescription)
            where T : class
        {
            if (values == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            List<T> result = new List<T>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (T value in values)
            {
                if (value == null)
                {
                    throw new ArgumentException($"The {elementDescription} collection must not contain null elements.", parameterName);
                }

                string name = getName(value);
                if (!names.Add(name))
                {
                    throw new ArgumentException($"Duplicate {elementDescription} name '{name}'.", parameterName);
                }

                result.Add(value);
            }

            return result;
        }

        public static int PixelTypeSize(PixelType pixelType)
        {
            ValidateEnum(pixelType, nameof(pixelType));
            switch (pixelType)
            {
                case PixelType.Half:
                    return 2;
                case PixelType.UInt:
                case PixelType.Float:
                    return 4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pixelType));
            }
        }

        public static long CountSampleLocations(int minimum, int maximum, int sampling)
        {
            if (sampling <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampling));
            }

            return checked(FloorDivide(maximum, sampling) - FloorDivide((long)minimum - 1L, sampling));
        }

        public static ulong CountSamples(Box2i region, int xSampling, int ySampling)
        {
            long xCount = CountSampleLocations(region.MinX, region.MaxX, xSampling);
            long yCount = CountSampleLocations(region.MinY, region.MaxY, ySampling);
            return checked((ulong)xCount * (ulong)yCount);
        }

        public static ulong CountPixels(Box2i region)
        {
            return checked((ulong)region.Width * (ulong)region.Height);
        }

        private static long FloorDivide(long value, int divisor)
        {
            long quotient = value / divisor;
            if (value % divisor < 0)
            {
                quotient--;
            }

            return quotient;
        }

        private sealed class Utf8ByteLexicographicComparer : IComparer<string>
        {
            public int Compare(string? left, string? right)
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }

                if (left == null)
                {
                    return -1;
                }

                if (right == null)
                {
                    return 1;
                }

                byte[] leftBytes = StrictUtf8.GetBytes(left);
                byte[] rightBytes = StrictUtf8.GetBytes(right);
                int commonLength = Math.Min(leftBytes.Length, rightBytes.Length);
                for (int i = 0; i < commonLength; i++)
                {
                    int comparison = leftBytes[i].CompareTo(rightBytes[i]);
                    if (comparison != 0)
                    {
                        return comparison;
                    }
                }

                return leftBytes.Length.CompareTo(rightBytes.Length);
            }
        }
    }
}
