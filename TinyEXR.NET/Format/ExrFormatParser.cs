using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace TinyEXR.Format
{
    /// <summary>
    /// Shared OpenEXR version and header semantics for the incremental reader and writer.
    /// </summary>
    internal static class ExrFormatParser
    {
        internal const uint Magic = 20_000_630U;
        internal const int SupportedFileVersion = 2;

        private const uint TiledFlag = 1U << 9;
        private const uint LongNamesFlag = 1U << 10;
        private const uint NonImageFlag = 1U << 11;
        private const uint MultipartFlag = 1U << 12;
        private const uint KnownVersionBits = 0xffU | TiledFlag | LongNamesFlag | NonImageFlag | MultipartFlag;

        private const int ShortNameByteLimit = 31;
        private const int LongNameByteLimit = 255;
        private const int WindowCoordinateLimit = int.MaxValue / 2;

        private static readonly HashSet<string> ModeledAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "channels",
            "compression",
            "dataWindow",
            "displayWindow",
            "lineOrder",
            "pixelAspectRatio",
            "screenWindowCenter",
            "screenWindowWidth",
            "tiles",
            "name",
            "type",
            "chunkCount",
            "chromaticities",
        };

        /// <summary>
        /// Interprets a completed attribute list for the incremental reader without requiring
        /// a contiguous file image. Attribute arrays are already reader-owned and are not copied.
        /// </summary>
        internal static ExrResult InterpretHeaderAttributes(
            int partIndex,
            IReadOnlyList<HeaderAttribute> attributes,
            ParsedFileFlags flags,
            out Header? header,
            out int chunkCount)
        {
            header = null;
            chunkCount = 0;
            try
            {
                (header, chunkCount) = InterpretHeader(attributes, flags);
                return ExrResult.Success;
            }
            catch (FormatParseException exception)
            {
                return exception.Result;
            }
            catch (OutOfMemoryException)
            {
                return ExrResult.OutOfMemory;
            }
            catch (OverflowException)
            {
                return ExrResult.InvalidFile;
            }
            catch (ArgumentException)
            {
                return ExrResult.InvalidFile;
            }
        }

        internal static ExrResult ValidateHeaders(
            IReadOnlyList<Header> headers,
            ParsedFileFlags flags)
        {
            try
            {
                ValidateFileFlagsAndPartNames(headers, flags);
                return ExrResult.Success;
            }
            catch (FormatParseException exception)
            {
                return exception.Result;
            }
            catch (ArgumentException)
            {
                return ExrResult.InvalidFile;
            }
        }

        internal static ExrResult InterpretVersionField(
            uint versionField,
            out ParsedFileFlags flags,
            out int nameByteLimit)
        {
            flags = default;
            nameByteLimit = 0;
            int fileVersion = (int)(versionField & 0xffU);
            if (fileVersion > SupportedFileVersion)
            {
                return ExrResult.Unsupported;
            }

            if (fileVersion != SupportedFileVersion)
            {
                return ExrResult.InvalidFile;
            }

            if ((versionField & ~KnownVersionBits) != 0)
            {
                return ExrResult.InvalidFile;
            }

            flags = new ParsedFileFlags(
                (versionField & TiledFlag) != 0,
                (versionField & LongNamesFlag) != 0,
                (versionField & NonImageFlag) != 0,
                (versionField & MultipartFlag) != 0);
            if (flags.Multipart && flags.Tiled)
            {
                return ExrResult.InvalidFile;
            }

            nameByteLimit = flags.LongNames ? LongNameByteLimit : ShortNameByteLimit;
            return ExrResult.Success;
        }

        private static (Header Header, int ChunkCount) InterpretHeader(
            IReadOnlyList<HeaderAttribute> rawAttributes,
            ParsedFileFlags flags)
        {
            Dictionary<string, HeaderAttribute> attributes = new Dictionary<string, HeaderAttribute>(rawAttributes.Count, StringComparer.Ordinal);
            for (int i = 0; i < rawAttributes.Count; i++)
            {
                HeaderAttribute attribute = rawAttributes[i];
                if (attribute.TypeName == "preview" && !PreviewImage.IsValid(attribute.Data))
                    throw Failure(ExrResult.InvalidFile);
                attributes.Add(attribute.Name, attribute);
            }

            List<Channel> channels = ParseChannels(
                Required(attributes, "channels", "chlist").Data,
                flags.LongNames ? LongNameByteLimit : ShortNameByteLimit);
            Compression compression = ParseCompression(Required(attributes, "compression", "compression", 1));
            Box2i dataWindow = ParseBox(Required(attributes, "dataWindow", "box2i", 16));
            Box2i displayWindow = ParseBox(Required(attributes, "displayWindow", "box2i", 16));
            ValidateWindowEndpoints(dataWindow);
            ValidateWindowEndpoints(displayWindow);

            LineOrder lineOrder = ParseLineOrder(Required(attributes, "lineOrder", "lineOrder", 1));
            float pixelAspectRatio = ParseSingle(Required(attributes, "pixelAspectRatio", "float", 4));
            Vector2 screenWindowCenter = ParseVector2(Required(attributes, "screenWindowCenter", "v2f", 8));
            float screenWindowWidth = ParseSingle(Required(attributes, "screenWindowWidth", "float", 4));

            PartType partType = ParsePartType(attributes, flags);
            bool tiled = partType == PartType.Tiled || partType == PartType.DeepTiled;
            bool deep = partType == PartType.DeepScanline || partType == PartType.DeepTiled;
            if (deep && !FormatCapabilities.IsValidDeepCompression(compression))
                throw Failure(ExrResult.InvalidFile);
            if (!FormatCapabilities.IsImplemented(compression))
                throw Failure(ExrResult.Unsupported);
            TileDescription? tiles = null;
            if (tiled)
            {
                tiles = ParseTileDescription(Required(attributes, "tiles", "tiledesc", 9));
            }

            string name = string.Empty;
            HeaderAttribute? nameAttribute = Optional(attributes, "name", "string");
            if (nameAttribute != null)
            {
                name = DecodeStringValue(nameAttribute, allowEmpty: true);
            }

            if (lineOrder == LineOrder.RandomY && !tiled)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            uint? declaredChunkCount = null;
            HeaderAttribute? chunkCountAttribute = Optional(attributes, "chunkCount", "int", 4);
            if (chunkCountAttribute != null)
            {
                int signedCount = ReadInt32(chunkCountAttribute.Data);
                if (signedCount < 0)
                {
                    throw Failure(ExrResult.InvalidFile);
                }

                declaredChunkCount = (uint)signedCount;
            }

            if (flags.Multipart && declaredChunkCount == null)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            if (flags.Multipart && name.Length == 0)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            if (deep)
            {
                HeaderAttribute version = Required(attributes, "version", "int", 4);
                if (ReadInt32(version.Data) != 1)
                {
                    throw Failure(ExrResult.Unsupported);
                }

                HeaderAttribute? maxSamples = Optional(attributes, "maxSamplesPerPixel", "int", 4);
                if (maxSamples != null && ReadInt32(maxSamples.Data) < -1)
                {
                    throw Failure(ExrResult.InvalidFile);
                }
            }

            Chromaticities? chromaticities = null;
            HeaderAttribute? chromaticitiesAttribute = Optional(attributes, "chromaticities", "chromaticities", 32);
            if (chromaticitiesAttribute != null)
            {
                chromaticities = ParseChromaticities(chromaticitiesAttribute.Data);
            }

            List<HeaderAttribute> customAttributes = new List<HeaderAttribute>();
            for (int i = 0; i < rawAttributes.Count; i++)
            {
                HeaderAttribute value = rawAttributes[i];
                bool deepStandard = deep &&
                    (string.Equals(value.Name, "version", StringComparison.Ordinal) ||
                     string.Equals(value.Name, "maxSamplesPerPixel", StringComparison.Ordinal));
                if (!ModeledAttributeNames.Contains(value.Name) && !deepStandard)
                {
                    customAttributes.Add(value);
                }
            }

            Header header = new Header(
                partType,
                dataWindow,
                channels,
                compression,
                lineOrder,
                displayWindow,
                pixelAspectRatio,
                screenWindowCenter,
                screenWindowWidth,
                tiles,
                name,
                chromaticities,
                customAttributes);

            foreach (Channel channel in channels)
                if (!FormatCapabilities.IsValidSampling(header, channel))
                    throw Failure(ExrResult.InvalidFile);
            int computedChunkCount = ComputeChunkCount(header);
            if (declaredChunkCount != null && declaredChunkCount.Value != (uint)computedChunkCount)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            return (header, computedChunkCount);
        }

        private static void ValidateFileFlagsAndPartNames(
            IReadOnlyList<Header> headers,
            ParsedFileFlags flags)
        {
            bool anyDeep = false;
            HashSet<string> partNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < headers.Count; i++)
            {
                Header header = headers[i];
                anyDeep |= header.IsDeep;
                if (flags.Multipart && !FormatCapabilities.HasMatchingSharedAttributes(headers[0], header))
                    throw Failure(ExrResult.InvalidFile);
                if (flags.Multipart && !partNames.Add(header.Name))
                {
                    throw Failure(ExrResult.InvalidFile);
                }
            }

            if (flags.NonImage != anyDeep)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            if (!flags.Multipart)
            {
                bool requiresTiledFlag = headers[0].PartType == PartType.Tiled;
                if (flags.Tiled != requiresTiledFlag)
                {
                    throw Failure(ExrResult.InvalidFile);
                }
            }
        }

        private static List<Channel> ParseChannels(ReadOnlySpan<byte> data, int nameLimit)
        {
            Cursor cursor = new Cursor(data, 0);
            List<Channel> channels = new List<Channel>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            string? previousName = null;
            for (; ; )
            {
                if (cursor.PeekByte() == 0)
                {
                    cursor.Skip(1);
                    break;
                }

                string name = cursor.ReadCString(nameLimit, allowEmpty: false);
                int pixelTypeValue = cursor.ReadInt32();
                if (pixelTypeValue < 0)
                {
                    throw Failure(ExrResult.InvalidFile);
                }

                if (pixelTypeValue > (int)PixelType.Float)
                {
                    throw Failure(ExrResult.Unsupported);
                }

                byte pLinear = cursor.ReadByte();
                if (pLinear > 1 || cursor.ReadByte() != 0 || cursor.ReadByte() != 0 || cursor.ReadByte() != 0)
                {
                    throw Failure(ExrResult.InvalidFile);
                }

                int xSampling = cursor.ReadInt32();
                int ySampling = cursor.ReadInt32();
                if (xSampling <= 0 || ySampling <= 0 || !names.Add(name) ||
                    (previousName != null && ModelValidation.Utf8ByteComparer.Compare(previousName, name) >= 0))
                {
                    throw Failure(ExrResult.InvalidFile);
                }

                channels.Add(new Channel(name, (PixelType)pixelTypeValue, xSampling, ySampling, pLinear != 0));
                previousName = name;
            }

            if (channels.Count == 0 || cursor.Remaining != 0)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            return channels;
        }

        private static Compression ParseCompression(HeaderAttribute attribute)
        {
            byte value = attribute.Data[0];
            if (value > (byte)Compression.ZSTD)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            return (Compression)value;
        }

        private static LineOrder ParseLineOrder(HeaderAttribute attribute)
        {
            byte value = attribute.Data[0];
            if (value > (byte)LineOrder.RandomY)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            return (LineOrder)value;
        }

        private static Box2i ParseBox(HeaderAttribute attribute)
        {
            ReadOnlySpan<byte> data = attribute.Data;
            return new Box2i(
                ReadInt32(data),
                ReadInt32(data.Slice(4)),
                ReadInt32(data.Slice(8)),
                ReadInt32(data.Slice(12)));
        }

        private static Vector2 ParseVector2(HeaderAttribute attribute)
        {
            ReadOnlySpan<byte> data = attribute.Data;
            return new Vector2(ReadSingle(data), ReadSingle(data.Slice(4)));
        }

        private static Chromaticities ParseChromaticities(ReadOnlySpan<byte> data)
        {
            return new Chromaticities(
                ReadSingle(data),
                ReadSingle(data.Slice(4)),
                ReadSingle(data.Slice(8)),
                ReadSingle(data.Slice(12)),
                ReadSingle(data.Slice(16)),
                ReadSingle(data.Slice(20)),
                ReadSingle(data.Slice(24)),
                ReadSingle(data.Slice(28)));
        }

        private static TileDescription ParseTileDescription(HeaderAttribute attribute)
        {
            ReadOnlySpan<byte> data = attribute.Data;
            uint tileSizeX = BinaryPrimitives.ReadUInt32LittleEndian(data);
            uint tileSizeY = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
            byte mode = data[8];
            int levelMode = mode & 0x0f;
            int roundingMode = mode >> 4;
            if (tileSizeX == 0 || tileSizeX > int.MaxValue ||
                tileSizeY == 0 || tileSizeY > int.MaxValue ||
                levelMode > (int)TileLevelMode.RipmapLevels ||
                roundingMode > (int)TileRoundingMode.RoundUp)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            return new TileDescription(
                tileSizeX,
                tileSizeY,
                (TileLevelMode)levelMode,
                (TileRoundingMode)roundingMode);
        }

        private static PartType ParsePartType(
            Dictionary<string, HeaderAttribute> attributes,
            ParsedFileFlags flags)
        {
            HeaderAttribute? typeAttribute = Optional(attributes, "type", "string");
            PartType partType;
            if (typeAttribute == null)
            {
                if (flags.Multipart || flags.NonImage)
                {
                    throw Failure(ExrResult.InvalidFile);
                }

                partType = flags.Tiled ? PartType.Tiled : PartType.Scanline;
            }
            else
            {
                string type = DecodeStringValue(typeAttribute, allowEmpty: false);
                switch (type)
                {
                    case "scanlineimage":
                        partType = PartType.Scanline;
                        break;
                    case "tiledimage":
                        partType = PartType.Tiled;
                        break;
                    case "deepscanline":
                        partType = PartType.DeepScanline;
                        break;
                    case "deeptile":
                        partType = PartType.DeepTiled;
                        break;
                    default:
                        throw Failure(ExrResult.Unsupported);
                }
            }

            return partType;
        }

        private static void ValidateWindowEndpoints(Box2i window)
        {
            if (window.MinX <= -WindowCoordinateLimit ||
                window.MinY <= -WindowCoordinateLimit ||
                window.MaxX >= WindowCoordinateLimit ||
                window.MaxY >= WindowCoordinateLimit)
            {
                throw Failure(ExrResult.InvalidFile);
            }
        }

        internal static int ComputeChunkCount(Header header)
        {
            ulong count = 0;
            if (!header.IsTiled)
            {
                int linesPerBlock = LinesPerBlock(header.Compression);
                count = checked(((ulong)header.DataWindow.Height + (uint)linesPerBlock - 1UL) / (uint)linesPerBlock);
            }
            else
            {
                foreach (LevelLayout level in EnumerateLevels(header))
                {
                    count = checked(count + checked((ulong)level.TilesX * (ulong)level.TilesY));
                }
            }

            if (count == 0 || count > int.MaxValue)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            return (int)count;
        }

        private static IEnumerable<LevelLayout> EnumerateLevels(Header header)
        {
            TileDescription tiles = header.Tiles!;
            int xLevels = LevelCount(header.DataWindow.Width, tiles.RoundingMode);
            int yLevels = LevelCount(header.DataWindow.Height, tiles.RoundingMode);
            if (tiles.LevelMode == TileLevelMode.OneLevel)
            {
                yield return CreateLevel(header, 0, 0);
            }
            else if (tiles.LevelMode == TileLevelMode.MipmapLevels)
            {
                int levels = Math.Max(xLevels, yLevels);
                for (int level = 0; level < levels; level++)
                {
                    yield return CreateLevel(header, level, level);
                }
            }
            else
            {
                for (int levelY = 0; levelY < yLevels; levelY++)
                {
                    for (int levelX = 0; levelX < xLevels; levelX++)
                    {
                        yield return CreateLevel(header, levelX, levelY);
                    }
                }
            }
        }

        private static LevelLayout CreateLevel(Header header, int levelX, int levelY)
        {
            TileDescription tiles = header.Tiles!;
            long width = LevelSize(header.DataWindow.Width, levelX, tiles.RoundingMode);
            long height = LevelSize(header.DataWindow.Height, levelY, tiles.RoundingMode);
            int tilesX = checked((int)CeilingDivide((ulong)width, tiles.TileSizeX));
            int tilesY = checked((int)CeilingDivide((ulong)height, tiles.TileSizeY));
            return new LevelLayout(levelX, levelY, width, height, tilesX, tilesY);
        }

        internal static int LevelCount(long size, TileRoundingMode roundingMode)
        {
            int count = 1;
            while (size > 1)
            {
                size = NextLevelSize(size, roundingMode);
                count++;
            }

            return count;
        }

        internal static long LevelSize(long size, int level, TileRoundingMode roundingMode)
        {
            for (int i = 0; i < level && size > 1; i++)
            {
                size = NextLevelSize(size, roundingMode);
            }

            return Math.Max(1L, size);
        }

        private static long NextLevelSize(long size, TileRoundingMode roundingMode)
        {
            return roundingMode == TileRoundingMode.RoundUp
                ? (size / 2L) + (size & 1L)
                : Math.Max(1L, size / 2L);
        }

        private static ulong CeilingDivide(ulong value, uint divisor)
        {
            return (value + divisor - 1UL) / divisor;
        }

        internal static ulong ComputeUncompressedByteCount(Header header, Box2i region)
        {
            ulong total = 0;
            for (int i = 0; i < header.Channels.Count; i++)
            {
                Channel channel = header.Channels[i];
                ulong samples = ModelValidation.CountSamples(region, channel.XSampling, channel.YSampling);
                total = checked(total + checked(samples * (uint)ModelValidation.PixelTypeSize(channel.PixelType)));
            }

            return total;
        }

        internal static int ChunkHeaderByteCount(Header header, bool multipart)
        {
            int baseSize;
            if (header.IsDeep)
            {
                baseSize = header.IsTiled ? 40 : 28;
            }
            else
            {
                baseSize = header.IsTiled ? 20 : 8;
            }

            return multipart ? baseSize + sizeof(int) : baseSize;
        }

        internal static int LinesPerBlock(Compression compression)
        {
            switch (compression)
            {
                case Compression.None:
                case Compression.RLE:
                case Compression.ZIPS:
                    return 1;
                case Compression.ZIP:
                case Compression.PXR24:
                    return 16;
                case Compression.PIZ:
                case Compression.B44:
                case Compression.B44A:
                case Compression.DWAA:
                case Compression.HTJ2K32:
                    return 32;
                case Compression.DWAB:
                case Compression.HTJ2K256:
                    return 256;
                default:
                    throw Failure(ExrResult.Unsupported);
            }
        }

        private static HeaderAttribute Required(
            Dictionary<string, HeaderAttribute> attributes,
            string name,
            string typeName,
            int? byteCount = null)
        {
            if (!attributes.TryGetValue(name, out HeaderAttribute? attribute))
            {
                throw Failure(ExrResult.InvalidFile);
            }

            ValidateAttribute(attribute, typeName, byteCount);
            return attribute;
        }

        private static HeaderAttribute? Optional(
            Dictionary<string, HeaderAttribute> attributes,
            string name,
            string typeName,
            int? byteCount = null)
        {
            if (!attributes.TryGetValue(name, out HeaderAttribute? attribute))
            {
                return null;
            }

            ValidateAttribute(attribute, typeName, byteCount);
            return attribute;
        }

        private static void ValidateAttribute(HeaderAttribute attribute, string typeName, int? byteCount)
        {
            if (!string.Equals(attribute.TypeName, typeName, StringComparison.Ordinal) ||
                (byteCount != null && attribute.ByteLength != byteCount.Value))
            {
                throw Failure(ExrResult.InvalidFile);
            }
        }

        private static string DecodeStringValue(HeaderAttribute attribute, bool allowEmpty)
        {
            ReadOnlySpan<byte> data = attribute.Data;
            if ((!allowEmpty && data.Length == 0) || data.IndexOf((byte)0) >= 0)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            try
            {
                return ModelValidation.StrictUtf8.GetString(data.ToArray());
            }
            catch (DecoderFallbackException)
            {
                throw Failure(ExrResult.InvalidFile);
            }
        }

        private static float ParseSingle(HeaderAttribute attribute)
        {
            return ReadSingle(attribute.Data);
        }

        private static int ReadInt32(ReadOnlySpan<byte> data)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(data);
        }

        private static float ReadSingle(ReadOnlySpan<byte> data)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));
        }

        private static FormatParseException Failure(ExrResult result)
        {
            return new FormatParseException(result);
        }

        private readonly struct LevelLayout
        {
            public LevelLayout(int levelX, int levelY, long width, long height, int tilesX, int tilesY)
            {
                LevelX = levelX;
                LevelY = levelY;
                Width = width;
                Height = height;
                TilesX = tilesX;
                TilesY = tilesY;
            }

            public int LevelX { get; }

            public int LevelY { get; }

            public long Width { get; }

            public long Height { get; }

            public int TilesX { get; }

            public int TilesY { get; }
        }

        private sealed class FormatParseException : Exception
        {
            public FormatParseException(ExrResult result)
            {
                Result = result;
            }

            public ExrResult Result { get; }
        }

        private ref struct Cursor
        {
            private readonly ReadOnlySpan<byte> _source;

            public Cursor(ReadOnlySpan<byte> source, int position)
            {
                _source = source;
                Position = position;
            }

            public int Position
            {
                get; private set;
            }

            public int Remaining => _source.Length - Position;

            public byte PeekByte()
            {
                Require(1);
                return _source[Position];
            }

            public byte ReadByte()
            {
                Require(1);
                return _source[Position++];
            }

            public int ReadInt32()
            {
                Require(sizeof(int));
                int value = BinaryPrimitives.ReadInt32LittleEndian(_source.Slice(Position, sizeof(int)));
                Position += sizeof(int);
                return value;
            }

            public string ReadCString(int byteLimit, bool allowEmpty)
            {
                ReadOnlySpan<byte> remaining = _source.Slice(Position);
                int length = remaining.IndexOf((byte)0);
                if (length < 0 || length > byteLimit || (!allowEmpty && length == 0))
                {
                    throw Failure(ExrResult.InvalidFile);
                }

                string value;
                try
                {
                    value = ModelValidation.StrictUtf8.GetString(remaining.Slice(0, length).ToArray());
                }
                catch (DecoderFallbackException)
                {
                    throw Failure(ExrResult.InvalidFile);
                }

                Position += length + 1;
                return value;
            }

            public void Skip(int byteCount)
            {
                Require(byteCount);
                Position += byteCount;
            }

            private void Require(int byteCount)
            {
                if (byteCount < 0 || Position < 0 || Position > _source.Length || byteCount > _source.Length - Position)
                {
                    throw Failure(ExrResult.InvalidFile);
                }
            }
        }
    }
}
