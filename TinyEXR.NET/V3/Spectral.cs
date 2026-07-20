using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TinyEXR.V3.IO;

namespace TinyEXR.V3
{
    /// <summary>
    /// TinyEXR v3 helpers for the JCGT 2021 spectral OpenEXR layout.
    /// </summary>
    public static class Spectral
    {
        public const string LayoutVersion = "1.0";

        public const int MaximumWavelengthCount = 4096;

        private const string LayoutVersionAttribute = "spectralLayoutVersion";
        private const string ReflectiveUnitsAttribute = "ROOT/units";
        private const string EmissiveUnitsAttribute = "emissiveUnits";
        private const string HandednessAttribute = "polarisationHandedness";

        public static bool IsSpectral(Header header)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            return FindAttribute(header, LayoutVersionAttribute) != null;
        }

        public static SpectrumType GetSpectrumType(Header header)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            bool hasReflective = false;
            bool hasEmissive = false;
            bool hasPolarisation = false;
            foreach (Channel channel in header.Channels)
            {
                if (channel.Name.StartsWith("T.", StringComparison.Ordinal))
                {
                    hasReflective = true;
                }
                else if (TryGetStokesComponent(channel.Name, out int stokes))
                {
                    hasEmissive = true;
                    if (stokes != 0)
                    {
                        hasPolarisation = true;
                    }
                }
            }

            if (hasReflective)
            {
                return SpectrumType.Reflective;
            }

            if (hasPolarisation)
            {
                return SpectrumType.Polarised;
            }

            return hasEmissive ? SpectrumType.Emissive : SpectrumType.None;
        }

        public static string GetChannelName(
            SpectrumType spectrumType,
            float wavelengthNanometers,
            int stokesComponent = 0)
        {
            ModelValidation.ValidateEnum(spectrumType, nameof(spectrumType));
            if (spectrumType == SpectrumType.None)
            {
                throw new ArgumentOutOfRangeException(nameof(spectrumType));
            }

            if (float.IsNaN(wavelengthNanometers) ||
                float.IsInfinity(wavelengthNanometers) ||
                wavelengthNanometers < 0.0f ||
                wavelengthNanometers >= int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(wavelengthNanometers));
            }

            int whole = (int)wavelengthNanometers;
            int fraction = (int)(((wavelengthNanometers - whole) * 1_000_000.0f) + 0.5f);
            if (fraction >= 1_000_000)
            {
                whole++;
                fraction -= 1_000_000;
            }

            string wavelength = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1:000000}",
                whole,
                fraction);
            if (spectrumType == SpectrumType.Reflective)
            {
                return string.Concat("T.", wavelength, "nm");
            }

            int clampedStokes = Math.Max(0, Math.Min(3, stokesComponent));
            return string.Format(
                CultureInfo.InvariantCulture,
                "S{0}.{1}nm",
                clampedStokes,
                wavelength);
        }

        public static bool TryParseChannelWavelength(
            string? channelName,
            out float wavelengthNanometers)
        {
            wavelengthNanometers = -1.0f;
            if (string.IsNullOrEmpty(channelName))
            {
                return false;
            }

            ReadOnlySpan<char> remaining = channelName.AsSpan();
            if (remaining.Length >= 3 &&
                remaining[0] == 'S' &&
                remaining[1] >= '0' &&
                remaining[1] <= '3' &&
                remaining[2] == '.')
            {
                remaining = remaining.Slice(3);
            }
            else if (remaining.Length >= 2 && remaining[0] == 'T' && remaining[1] == '.')
            {
                remaining = remaining.Slice(2);
            }
            else
            {
                return false;
            }

            if (remaining.Length <= 2 ||
                remaining[remaining.Length - 2] != 'n' ||
                remaining[remaining.Length - 1] != 'm')
            {
                return false;
            }

            ReadOnlySpan<char> numeric = remaining.Slice(0, remaining.Length - 2);
            if (numeric.Length == 0)
            {
                return false;
            }

            char[] normalized = numeric.ToArray();
            bool hasDigit = false;
            bool hasDecimal = false;
            for (int index = 0; index < normalized.Length; index++)
            {
                char value = normalized[index];
                if (value >= '0' && value <= '9')
                {
                    hasDigit = true;
                    continue;
                }

                if ((value == ',' || value == '.') && !hasDecimal)
                {
                    normalized[index] = '.';
                    hasDecimal = true;
                    continue;
                }

                if ((value == '+' || value == '-') && index == 0)
                {
                    continue;
                }

                return false;
            }

            if (!hasDigit ||
                !float.TryParse(
                    new string(normalized),
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out wavelengthNanometers) ||
                float.IsNaN(wavelengthNanometers) ||
                float.IsInfinity(wavelengthNanometers) ||
                wavelengthNanometers < 0.0f)
            {
                wavelengthNanometers = -1.0f;
                return false;
            }

            return true;
        }

        public static bool TryGetStokesComponent(string? channelName, out int stokesComponent)
        {
            stokesComponent = -1;
            if (string.IsNullOrEmpty(channelName) || channelName.Length < 3)
            {
                return false;
            }

            if (channelName[0] == 'S' &&
                channelName[1] >= '0' &&
                channelName[1] <= '3' &&
                channelName[2] == '.')
            {
                stokesComponent = channelName[1] - '0';
                return true;
            }

            return false;
        }

        public static bool IsSpectralChannel(string? channelName)
        {
            return TryParseChannelWavelength(channelName, out _);
        }

        public static float[] GetWavelengths(Header header)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            List<float> candidates = new List<float>();
            foreach (Channel channel in header.Channels)
            {
                if (TryParseChannelWavelength(channel.Name, out float wavelength))
                {
                    candidates.Add(wavelength);
                }
            }

            candidates.Sort();
            List<float> result = new List<float>(Math.Min(candidates.Count, MaximumWavelengthCount));
            foreach (float wavelength in candidates)
            {
                if (result.Count != 0 && wavelength <= result[result.Count - 1] + 0.01f)
                {
                    continue;
                }

                if (result.Count == MaximumWavelengthCount)
                {
                    break;
                }

                result.Add(wavelength);
            }

            return result.ToArray();
        }

        public static string? GetUnits(Header header)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            return GetStringAttribute(header, ReflectiveUnitsAttribute) ??
                GetStringAttribute(header, EmissiveUnitsAttribute);
        }

        public static string? GetPolarisationHandedness(Header header)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            return GetStringAttribute(header, HandednessAttribute);
        }

        public static Header WithSpectralAttributes(
            Header header,
            SpectrumType spectrumType,
            string? units = null,
            string polarisationHandedness = "left")
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            ValidateSpectralType(spectrumType);
            if (units != null && units.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("Spectral units must not contain NUL characters.", nameof(units));
            }

            if (polarisationHandedness == null)
            {
                throw new ArgumentNullException(nameof(polarisationHandedness));
            }

            List<HeaderAttribute> attributes = new List<HeaderAttribute>(header.Attributes.Count + 3);
            foreach (HeaderAttribute attribute in header.Attributes)
            {
                if (attribute.Name != LayoutVersionAttribute &&
                    attribute.Name != ReflectiveUnitsAttribute &&
                    attribute.Name != EmissiveUnitsAttribute &&
                    attribute.Name != HandednessAttribute)
                {
                    attributes.Add(attribute);
                }
            }

            attributes.Add(CreateStringAttribute(LayoutVersionAttribute, LayoutVersion));
            if (!string.IsNullOrEmpty(units))
            {
                attributes.Add(CreateStringAttribute(
                    spectrumType == SpectrumType.Reflective
                        ? ReflectiveUnitsAttribute
                        : EmissiveUnitsAttribute,
                    units!));
            }

            if (spectrumType == SpectrumType.Polarised)
            {
                attributes.Add(CreateStringAttribute(HandednessAttribute, polarisationHandedness));
            }

            return new Header(
                header.PartType,
                header.DataWindow,
                header.Channels,
                header.Compression,
                header.LineOrder,
                header.DisplayWindow,
                header.PixelAspectRatio,
                header.ScreenWindowCenter,
                header.ScreenWindowWidth,
                header.Tiles,
                header.Name,
                header.Chromaticities,
                attributes);
        }

        public static Part CreateEmissivePart(
            int width,
            int height,
            ReadOnlySpan<float> wavelengths,
            ReadOnlySpan<float> samples,
            string? units = null,
            Compression compression = Compression.ZIP)
        {
            return CreatePart(
                SpectrumType.Emissive,
                width,
                height,
                wavelengths,
                samples,
                units,
                compression);
        }

        public static Part CreateReflectivePart(
            int width,
            int height,
            ReadOnlySpan<float> wavelengths,
            ReadOnlySpan<float> samples,
            string? units = null,
            Compression compression = Compression.ZIP)
        {
            return CreatePart(
                SpectrumType.Reflective,
                width,
                height,
                wavelengths,
                samples,
                units,
                compression);
        }

        private static Part CreatePart(
            SpectrumType spectrumType,
            int width,
            int height,
            ReadOnlySpan<float> wavelengths,
            ReadOnlySpan<float> samples,
            string? units,
            Compression compression)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (wavelengths.Length == 0 || wavelengths.Length > MaximumWavelengthCount)
            {
                throw new ArgumentOutOfRangeException(nameof(wavelengths));
            }

            int pixelCount = checked(width * height);
            int requiredSamples = checked(pixelCount * wavelengths.Length);
            if (samples.Length != requiredSamples)
            {
                throw new ArgumentException(
                    $"The spectral cube requires exactly {requiredSamples} samples.",
                    nameof(samples));
            }

            List<Channel> channels = new List<Channel>(wavelengths.Length);
            List<ChannelBuffer> buffers = new List<ChannelBuffer>(wavelengths.Length);
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int wavelengthIndex = 0; wavelengthIndex < wavelengths.Length; wavelengthIndex++)
            {
                string name = GetChannelName(spectrumType, wavelengths[wavelengthIndex]);
                if (!names.Add(name))
                {
                    throw new ArgumentException(
                        $"Wavelengths must remain distinct after six-decimal channel-name formatting; duplicate '{name}'.",
                        nameof(wavelengths));
                }

                channels.Add(new Channel(name, PixelType.Float));
                byte[] data = new byte[checked(pixelCount * sizeof(float))];
                int sourceOffset = wavelengthIndex * pixelCount;
                for (int pixel = 0; pixel < pixelCount; pixel++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        data.AsSpan(pixel * sizeof(float), sizeof(float)),
                        BitConverter.SingleToInt32Bits(samples[sourceOffset + pixel]));
                }

                buffers.Add(new ChannelBuffer(name, PixelType.Float, data));
            }

            Box2i dataWindow = new Box2i(0, 0, width - 1, height - 1);
            Header header = new Header(
                PartType.Scanline,
                dataWindow,
                channels,
                compression: compression);
            header = WithSpectralAttributes(header, spectrumType, units);
            return new Part(
                header,
                new[] { new FlatLevel(0, 0, dataWindow, buffers) },
                isComplete: true);
        }

        private static void ValidateSpectralType(SpectrumType spectrumType)
        {
            ModelValidation.ValidateEnum(spectrumType, nameof(spectrumType));
            if (spectrumType == SpectrumType.None)
            {
                throw new ArgumentOutOfRangeException(nameof(spectrumType));
            }
        }

        private static HeaderAttribute CreateStringAttribute(string name, string value)
        {
            return new HeaderAttribute(name, "string", Encoding.UTF8.GetBytes(value));
        }

        private static HeaderAttribute? FindAttribute(Header header, string name)
        {
            foreach (HeaderAttribute attribute in header.Attributes)
            {
                if (attribute.Name == name)
                {
                    return attribute;
                }
            }

            return null;
        }

        private static string? GetStringAttribute(Header header, string name)
        {
            HeaderAttribute? attribute = FindAttribute(header, name);
            if (attribute == null || attribute.TypeName != "string")
            {
                return null;
            }

            try
            {
                return ModelValidation.StrictUtf8.GetString(attribute.Data);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Materialized wavelength-major spectral cube. Each Stokes plane is laid out
    /// as [wavelength][y * width + x].
    /// </summary>
    public sealed class SpectralImage
    {
        private readonly float[] _wavelengths;
        private readonly float[]?[] _stokes;

        private SpectralImage(
            int width,
            int height,
            SpectrumType spectrumType,
            float[] wavelengths,
            float[]?[] stokes,
            string units,
            string handedness)
        {
            Width = width;
            Height = height;
            SpectrumType = spectrumType;
            _wavelengths = wavelengths;
            _stokes = stokes;
            Units = units;
            PolarisationHandedness = handedness;
        }

        public int Width { get; }

        public int Height { get; }

        public SpectrumType SpectrumType { get; }

        public int WavelengthCount => _wavelengths.Length;

        public ReadOnlySpan<float> Wavelengths => _wavelengths;

        public string Units { get; }

        public string PolarisationHandedness { get; }

        public bool HasStokesPlane(int stokesComponent)
        {
            return (uint)stokesComponent < 4U && _stokes[stokesComponent] != null;
        }

        public ReadOnlySpan<float> GetStokesPlane(int stokesComponent)
        {
            if ((uint)stokesComponent >= 4U)
            {
                throw new ArgumentOutOfRangeException(nameof(stokesComponent));
            }

            return _stokes[stokesComponent] ?? ReadOnlySpan<float>.Empty;
        }

        public float GetSample(int stokesComponent, int wavelengthIndex, int x, int y)
        {
            if (!HasStokesPlane(stokesComponent) ||
                (uint)wavelengthIndex >= (uint)WavelengthCount ||
                (uint)x >= (uint)Width ||
                (uint)y >= (uint)Height)
            {
                return 0.0f;
            }

            int pixelCount = checked(Width * Height);
            return _stokes[stokesComponent]![
                checked((wavelengthIndex * pixelCount) + (y * Width) + x)];
        }

        public int CopyPixelSpectrum(
            int stokesComponent,
            int x,
            int y,
            Span<float> destination)
        {
            if (!HasStokesPlane(stokesComponent) ||
                (uint)x >= (uint)Width ||
                (uint)y >= (uint)Height)
            {
                return 0;
            }

            if (destination.Length < WavelengthCount)
            {
                throw new ArgumentException(
                    "The destination must contain one element per wavelength.",
                    nameof(destination));
            }

            int pixelCount = checked(Width * Height);
            int pixelOffset = checked((y * Width) + x);
            float[] plane = _stokes[stokesComponent]!;
            for (int wavelengthIndex = 0; wavelengthIndex < WavelengthCount; wavelengthIndex++)
            {
                destination[wavelengthIndex] = plane[(wavelengthIndex * pixelCount) + pixelOffset];
            }

            return WavelengthCount;
        }

        public static SpectralImage FromPart(Part part)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            if (!Spectral.IsSpectral(part.Header))
            {
                throw new NotSupportedException("The part does not carry spectralLayoutVersion.");
            }

            SpectrumType spectrumType = Spectral.GetSpectrumType(part.Header);
            if (spectrumType == SpectrumType.None)
            {
                throw new NotSupportedException("The part has no spectral channels.");
            }

            float[] wavelengths = Spectral.GetWavelengths(part.Header);
            if (wavelengths.Length == 0)
            {
                throw new NotSupportedException("The part has no valid spectral wavelengths.");
            }

            if (part.Header.IsDeep)
            {
                throw new NotSupportedException("Deep spectral parts cannot be represented as a wavelength cube.");
            }

            PartLevel materialized = part.GetLevel(0, 0);
            if (!(materialized is FlatLevel level) ||
                level.Region.MinX != part.Header.DataWindow.MinX ||
                level.Region.MinY != part.Header.DataWindow.MinY ||
                level.Region.MaxX != part.Header.DataWindow.MaxX ||
                level.Region.MaxY != part.Header.DataWindow.MaxY)
            {
                throw new NotSupportedException("A spectral cube requires a fully materialized base level.");
            }

            int width = checked((int)level.Width);
            int height = checked((int)level.Height);
            int pixelCount = checked(width * height);
            int stokesCount = spectrumType == SpectrumType.Polarised ? 4 : 1;
            float[]?[] stokes = new float[4][];
            for (int stokesIndex = 0; stokesIndex < stokesCount; stokesIndex++)
            {
                stokes[stokesIndex] = new float[checked(wavelengths.Length * pixelCount)];
            }

            foreach (Channel channel in part.Header.Channels)
            {
                if (!Spectral.TryParseChannelWavelength(channel.Name, out float wavelength))
                {
                    continue;
                }

                int stokesIndex = Spectral.TryGetStokesComponent(channel.Name, out int parsedStokes)
                    ? parsedStokes
                    : 0;
                if (stokesIndex >= stokesCount)
                {
                    continue;
                }

                int wavelengthIndex = FindWavelength(wavelengths, wavelength);
                if (wavelengthIndex < 0)
                {
                    continue;
                }

                ChannelBuffer buffer = level.GetChannel(channel.Name);
                if (buffer.PixelType != channel.PixelType)
                {
                    throw new InvalidDataException(
                        $"Spectral channel '{channel.Name}' has inconsistent pixel types.");
                }

                Span<float> destination = stokes[stokesIndex]!.AsSpan(
                    checked(wavelengthIndex * pixelCount),
                    pixelCount);
                FillPlane(channel, buffer, level.Region, width, height, destination);
            }

            return new SpectralImage(
                width,
                height,
                spectrumType,
                wavelengths,
                stokes,
                Spectral.GetUnits(part.Header) ?? string.Empty,
                Spectral.GetPolarisationHandedness(part.Header) ?? string.Empty);
        }

        public static ReaderResult<SpectralImage> LoadFromMemory(
            ReadOnlyMemory<byte> data,
            ReaderOptions? options = null)
        {
            using (ExrReader reader = ExrReader.OpenMemory(data, options))
            {
                return Load(reader);
            }
        }

        public static ReaderResult<SpectralImage> LoadFromFile(
            string path,
            ReaderOptions? options = null)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (StreamDataSource source = new StreamDataSource(stream, leaveOpen: true))
                using (ExrReader reader = ExrReader.OpenSource(
                    source,
                    new ReaderOptions(options?.Limits, leaveOpen: true)))
                {
                    return Load(reader);
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                return new ReaderResult<SpectralImage>(
                    new ReaderResult(ExrResult.IO, null, exception),
                    null);
            }
        }

        private static ReaderResult<SpectralImage> Load(ExrReader reader)
        {
            ReaderResult parseResult = reader.ParseHeader();
            if (!parseResult.IsSuccess)
            {
                return new ReaderResult<SpectralImage>(parseResult, null);
            }

            ReaderResult<Part> partResult = reader.ReadPart(0);
            if (!partResult.IsSuccess || partResult.Value == null)
            {
                return new ReaderResult<SpectralImage>(partResult.Operation, null);
            }

            try
            {
                SpectralImage image = FromPart(partResult.Value);
                return new ReaderResult<SpectralImage>(partResult.Operation, image);
            }
            catch (OutOfMemoryException exception)
            {
                return Failure(ExrResult.OutOfMemory, partResult.BytesWritten, exception);
            }
            catch (NotSupportedException exception)
            {
                return Failure(ExrResult.Unsupported, partResult.BytesWritten, exception);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidDataException ||
                exception is OverflowException)
            {
                return Failure(ExrResult.Corrupt, partResult.BytesWritten, exception);
            }
        }

        private static ReaderResult<SpectralImage> Failure(
            ExrResult status,
            long bytesWritten,
            Exception exception)
        {
            return new ReaderResult<SpectralImage>(
                new ReaderResult(status, null, exception, bytesWritten),
                null);
        }

        private static int FindWavelength(float[] wavelengths, float target)
        {
            for (int index = 0; index < wavelengths.Length; index++)
            {
                if (Math.Abs(wavelengths[index] - target) < 0.01f)
                {
                    return index;
                }
            }

            return -1;
        }

        private static void FillPlane(
            Channel channel,
            ChannelBuffer buffer,
            Box2i region,
            int width,
            int height,
            Span<float> destination)
        {
            int sampledWidth = checked((int)ModelValidation.CountSampleLocations(
                region.MinX,
                region.MaxX,
                channel.XSampling));
            int sampledHeight = checked((int)ModelValidation.CountSampleLocations(
                region.MinY,
                region.MaxY,
                channel.YSampling));
            int sampleCount = checked(sampledWidth * sampledHeight);
            int expectedBytes = checked(sampleCount * ModelValidation.PixelTypeSize(channel.PixelType));
            if (buffer.ByteLength != expectedBytes)
            {
                throw new InvalidDataException(
                    $"Spectral channel '{channel.Name}' contains {buffer.ByteLength} bytes; {expectedBytes} are required.");
            }

            if (sampledWidth == 0 || sampledHeight == 0)
            {
                return;
            }

            if (channel.XSampling == 1 && channel.YSampling == 1)
            {
                DecodeSamples(buffer, destination);
                return;
            }

            for (int y = 0; y < height; y++)
            {
                int absoluteY = checked(region.MinY + y);
                int sampledY = checked((int)ModelValidation.CountSampleLocations(
                    region.MinY,
                    absoluteY,
                    channel.YSampling) - 1);
                sampledY = Math.Max(0, Math.Min(sampledHeight - 1, sampledY));
                for (int x = 0; x < width; x++)
                {
                    int absoluteX = checked(region.MinX + x);
                    int sampledX = checked((int)ModelValidation.CountSampleLocations(
                        region.MinX,
                        absoluteX,
                        channel.XSampling) - 1);
                    sampledX = Math.Max(0, Math.Min(sampledWidth - 1, sampledX));
                    destination[(y * width) + x] = ReadSample(
                        buffer,
                        (sampledY * sampledWidth) + sampledX);
                }
            }
        }

        private static void DecodeSamples(ChannelBuffer buffer, Span<float> destination)
        {
            if (buffer.SampleCount != destination.Length)
            {
                throw new InvalidDataException("The spectral plane sample count does not match its image dimensions.");
            }

            switch (buffer.PixelType)
            {
                case PixelType.Float:
                    for (int index = 0; index < destination.Length; index++)
                    {
                        destination[index] = BitConverter.Int32BitsToSingle(
                            BinaryPrimitives.ReadInt32LittleEndian(
                                buffer.Data.Slice(index * sizeof(float), sizeof(float))));
                    }

                    break;
                case PixelType.UInt:
                    for (int index = 0; index < destination.Length; index++)
                    {
                        destination[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                            buffer.Data.Slice(index * sizeof(uint), sizeof(uint)));
                    }

                    break;
                case PixelType.Half:
                    ushort[] half = new ushort[destination.Length];
                    for (int index = 0; index < half.Length; index++)
                    {
                        half[index] = BinaryPrimitives.ReadUInt16LittleEndian(
                            buffer.Data.Slice(index * sizeof(ushort), sizeof(ushort)));
                    }

                    PixelConversion.HalfToFloat(half, destination);
                    break;
            }
        }

        private static float ReadSample(ChannelBuffer buffer, int sampleIndex)
        {
            switch (buffer.PixelType)
            {
                case PixelType.Float:
                    return BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(
                            buffer.Data.Slice(sampleIndex * sizeof(float), sizeof(float))));
                case PixelType.UInt:
                    return BinaryPrimitives.ReadUInt32LittleEndian(
                        buffer.Data.Slice(sampleIndex * sizeof(uint), sizeof(uint)));
                case PixelType.Half:
                    return PixelConversion.HalfBitsToSingle(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            buffer.Data.Slice(sampleIndex * sizeof(ushort), sizeof(ushort))));
                default:
                    return 0.0f;
            }
        }
    }
}
