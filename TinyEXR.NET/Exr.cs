using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using TinyEXR.PortV1;

namespace TinyEXR
{
    public static class Exr
    {
        private const string SpectralLayoutVersionAttribute = "spectralLayoutVersion";
        private const string ReflectiveUnitsAttribute = "ROOT/units";
        private const string EmissiveUnitsAttribute = "emissiveUnits";
        private const string PolarisationHandednessAttribute = "polarisationHandedness";

        public static ResultCode LoadEXR(string filename, out float[] rgba, out int width, out int height)
        {
            return TryReadRgba(filename, out rgba, out width, out height);
        }

        public static ResultCode LoadEXRWithLayer(string filename, string? layer, out float[] rgba, out int width, out int height)
        {
            return TryReadRgba(filename, layer, out rgba, out width, out height);
        }

        public static ResultCode LoadEXRFromMemory(ReadOnlySpan<byte> data, out float[] rgba, out int width, out int height)
        {
            return TryReadRgba(data, out rgba, out width, out height);
        }

        public static ResultCode SaveEXRToMemory(
            ReadOnlySpan<float> data,
            int width,
            int height,
            int components,
            bool asFp16,
            out byte[] outBuffer)
        {
            return TryWriteRgba(data, width, height, components, asFp16, out outBuffer);
        }

        public static ResultCode SaveEXR(
            ReadOnlySpan<float> data,
            int width,
            int height,
            int components,
            bool asFp16,
            string filename)
        {
            return TryWriteRgba(data, width, height, components, asFp16, filename);
        }

        public static ResultCode EXRLayers(string filename, out string[] layers)
        {
            return TryReadLayers(filename, out layers);
        }

        public static bool IsEXR(string filename)
        {
            return TryReadVersion(filename, out _) == ResultCode.Success;
        }

        public static bool IsEXRFromMemory(ReadOnlySpan<byte> data)
        {
            return TryReadVersion(data, out _) == ResultCode.Success;
        }

        public static ResultCode ParseEXRVersionFromFile(string filename, out ExrVersion version)
        {
            return TryReadVersion(filename, out version);
        }

        public static ResultCode ParseEXRVersionFromMemory(ReadOnlySpan<byte> data, out ExrVersion version)
        {
            return TryReadVersion(data, out version);
        }

        public static ResultCode ParseEXRHeaderFromFile(string filename, out ExrVersion version, out ExrHeader header)
        {
            return TryReadHeader(filename, out version, out header);
        }

        public static ResultCode ParseEXRHeaderFromMemory(ReadOnlySpan<byte> data, out ExrVersion version, out ExrHeader header)
        {
            return TryReadHeader(data, out version, out header);
        }

        public static ResultCode ParseEXRMultipartHeaderFromFile(string filename, out ExrVersion version, out ExrMultipartHeader headers)
        {
            if (!TryReadFile(filename, out byte[] data, out ResultCode fileResult))
            {
                version = new ExrVersion();
                headers = new ExrMultipartHeader(Array.Empty<ExrHeader>());
                return fileResult;
            }

            return ParseEXRMultipartHeaderFromMemory(data, out version, out headers);
        }

        public static ResultCode ParseEXRMultipartHeaderFromMemory(ReadOnlySpan<byte> data, out ExrVersion version, out ExrMultipartHeader headers)
        {
            ResultCode result = ExrImplementation.TryReadMultipartHeaders(data, out version, out ExrHeader[] parsedHeaders);
            headers = new ExrMultipartHeader(parsedHeaders);
            return result;
        }

        public static ResultCode LoadEXRImageFromFile(string filename, ExrHeader header, out ExrImage image)
        {
            if (!TryReadFile(filename, out byte[] data, out ResultCode fileResult))
            {
                image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
                return fileResult;
            }

            return LoadEXRImageFromMemory(data, header, out image);
        }

        public static ResultCode LoadEXRImageFromMemory(ReadOnlySpan<byte> data, ExrHeader header, out ExrImage image)
        {
            if (header == null)
            {
                image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
                return ResultCode.InvalidArgument;
            }

            return ExrImplementation.TryReadImage(data, header, out image);
        }

        public static ResultCode LoadEXRMultipartImageFromFile(string filename, ExrMultipartHeader headers, out ExrMultipartImage images)
        {
            if (!TryReadFile(filename, out byte[] data, out ResultCode fileResult))
            {
                images = new ExrMultipartImage(Array.Empty<ExrImage>());
                return fileResult;
            }

            return LoadEXRMultipartImageFromMemory(data, headers, out images);
        }

        public static ResultCode LoadEXRMultipartImageFromMemory(ReadOnlySpan<byte> data, ExrMultipartHeader headers, out ExrMultipartImage images)
        {
            if (headers == null)
            {
                images = new ExrMultipartImage(Array.Empty<ExrImage>());
                return ResultCode.InvalidArgument;
            }

            ResultCode result = ExrImplementation.TryReadMultipartImages(data, headers.Headers.ToArray(), out ExrImage[] decodedImages);
            images = new ExrMultipartImage(decodedImages);
            return result;
        }

        public static ResultCode SaveEXRImageToMemory(ExrImage image, ExrHeader header, out byte[] encoded)
        {
            if (header == null)
            {
                encoded = Array.Empty<byte>();
                return ResultCode.InvalidArgument;
            }

            return TryWriteImage(image, header, out encoded);
        }

        public static ResultCode SaveEXRImageToFile(ExrImage image, ExrHeader header, string filename)
        {
            ResultCode result = SaveEXRImageToMemory(image, header, out byte[] encoded);
            if (result != ResultCode.Success)
            {
                return result;
            }

            return TryWriteFile(filename, encoded);
        }

        public static ResultCode SaveEXRMultipartImageToMemory(ExrMultipartImage images, ExrMultipartHeader headers, out byte[] encoded)
        {
            encoded = Array.Empty<byte>();
            if (images == null || headers == null)
            {
                return ResultCode.InvalidArgument;
            }

            return ResultCode.UnsupportedFeature;
        }

        public static ResultCode SaveEXRMultipartImageToFile(ExrMultipartImage images, ExrMultipartHeader headers, string filename)
        {
            ResultCode result = SaveEXRMultipartImageToMemory(images, headers, out byte[] encoded);
            if (result != ResultCode.Success)
            {
                return result;
            }

            return TryWriteFile(filename, encoded);
        }

        public static ResultCode LoadDeepEXR(string filename, out ExrHeader header, out ExrDeepImage image)
        {
            return TryReadDeepImage(filename, out header, out image);
        }

        public static int EXRNumLevels(ExrImage image)
        {
            return image?.Levels.Count ?? 0;
        }

        public static void EXRSetNameAttr(ExrHeader header, string? name)
        {
            if (header != null)
            {
                header.Name = name;
            }
        }

        internal static ResultCode TryReadVersion(string filename, out ExrVersion version)
        {
            if (!TryReadFile(filename, out byte[] data, out ResultCode fileResult))
            {
                version = new ExrVersion();
                return fileResult;
            }

            return TryReadVersion(data, out version);
        }

        internal static ResultCode TryReadVersion(ReadOnlySpan<byte> data, out ExrVersion version)
        {
            return ExrImplementation.TryReadVersion(data, out version);
        }

        internal static ResultCode TryReadHeader(string filename, out ExrVersion version, out ExrHeader header)
        {
            if (!TryReadFile(filename, out byte[] data, out ResultCode fileResult))
            {
                version = new ExrVersion();
                header = new ExrHeader();
                return fileResult;
            }

            return TryReadHeader(data, out version, out header);
        }

        internal static ResultCode TryReadHeader(ReadOnlySpan<byte> data, out ExrVersion version, out ExrHeader header)
        {
            return ExrImplementation.TryReadHeader(data, out version, out header);
        }

        internal static ResultCode TryReadHeader(string filename, out ExrHeader header)
        {
            ResultCode result = TryReadHeader(filename, out _, out header);
            return result;
        }

        internal static ResultCode TryReadHeader(ReadOnlySpan<byte> data, out ExrHeader header)
        {
            ResultCode result = TryReadHeader(data, out _, out header);
            return result;
        }

        internal static ResultCode TryReadImage(string filename, out ExrHeader header, out ExrImage image)
        {
            if (!TryReadFile(filename, out byte[] data, out ResultCode fileResult))
            {
                header = new ExrHeader();
                image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
                return fileResult;
            }

            return TryReadImage(data, out header, out image);
        }

        internal static ResultCode TryReadImage(ReadOnlySpan<byte> data, out ExrHeader header, out ExrImage image)
        {
            return ExrImplementation.TryReadImage(data, out header, out image);
        }

        internal static ResultCode TryReadDeepImage(string filename, out ExrHeader header, out ExrDeepImage image)
        {
            if (!TryReadFile(filename, out byte[] data, out ResultCode fileResult))
            {
                header = new ExrHeader();
                image = new ExrDeepImage(0, 0, Array.Empty<int[]>(), Array.Empty<ExrDeepChannel>());
                return fileResult;
            }

            return TryReadDeepImage(data, out header, out image);
        }

        internal static ResultCode TryReadDeepImage(ReadOnlySpan<byte> data, out ExrHeader header, out ExrDeepImage image)
        {
            return ExrImplementation.TryReadDeepImage(data, out header, out image);
        }

        internal static ResultCode TryReadLayers(string filename, out string[] layers)
        {
            if (!TryReadFile(filename, out byte[] data, out ResultCode fileResult))
            {
                layers = Array.Empty<string>();
                return fileResult;
            }

            return TryReadLayers(data, out layers);
        }

        internal static ResultCode TryReadLayers(ReadOnlySpan<byte> data, out string[] layers)
        {
            return ExrImplementation.TryReadLayers(data, out layers);
        }

        internal static ResultCode TryReadRgba(string filename, out float[] rgba, out int width, out int height)
        {
            return TryReadRgba(filename, layerName: null, out rgba, out width, out height);
        }

        internal static ResultCode TryReadRgba(string filename, string? layerName, out float[] rgba, out int width, out int height)
        {
            if (!TryReadFile(filename, out byte[] data, out ResultCode fileResult))
            {
                rgba = Array.Empty<float>();
                width = 0;
                height = 0;
                return fileResult;
            }

            return TryReadRgba(data, layerName, out rgba, out width, out height);
        }

        internal static ResultCode TryReadRgba(ReadOnlySpan<byte> data, out float[] rgba, out int width, out int height)
        {
            return TryReadRgba(data, layerName: null, out rgba, out width, out height);
        }

        internal static ResultCode TryReadRgba(ReadOnlySpan<byte> data, string? layerName, out float[] rgba, out int width, out int height)
        {
            return ExrImplementation.TryReadRgba(data, layerName, out rgba, out width, out height);
        }

        internal static ResultCode TryWriteImage(ExrImage image, out byte[] encoded)
        {
            return TryWriteImage(image, header: null, out encoded);
        }

        internal static ResultCode TryWriteImage(ExrImage image, ExrHeader? header, out byte[] encoded)
        {
            return ExrImplementation.TryWriteImage(image, header, out encoded);
        }

        internal static ResultCode TryWriteImage(string filename, ExrImage image, ExrHeader? header = null)
        {
            ResultCode result = TryWriteImage(image, header, out byte[] encoded);
            if (result != ResultCode.Success)
            {
                return result;
            }

            return TryWriteFile(filename, encoded);
        }

        internal static ResultCode TryWriteRgba(
            ReadOnlySpan<float> data,
            int width,
            int height,
            int components,
            bool asFp16,
            out byte[] encoded)
        {
            encoded = Array.Empty<byte>();
            ResultCode result = TryBuildRgbaImage(data, width, height, components, asFp16, out ExrImage image, out ExrHeader header);
            if (result != ResultCode.Success)
            {
                return result;
            }

            return TryWriteImage(image, header, out encoded);
        }

        internal static ResultCode TryWriteRgba(
            ReadOnlySpan<float> data,
            int width,
            int height,
            int components,
            bool asFp16,
            string filename)
        {
            ResultCode result = TryWriteRgba(data, width, height, components, asFp16, out byte[] encoded);
            if (result != ResultCode.Success)
            {
                return result;
            }

            return TryWriteFile(filename, encoded);
        }

        internal static bool IsExr(string filename)
        {
            return IsEXR(filename);
        }

        internal static bool IsExr(ReadOnlySpan<byte> data)
        {
            return TryReadVersion(data, out _) == ResultCode.Success;
        }

        internal static bool IsExrFromMemory(ReadOnlySpan<byte> data)
        {
            return IsEXRFromMemory(data);
        }

        public static bool IsSpectralEXR(string filename)
        {
            return TryReadHeader(filename, out ExrHeader header) == ResultCode.Success &&
                EXRGetSpectrumType(header).HasValue;
        }

        public static bool IsSpectralEXRFromMemory(ReadOnlySpan<byte> data)
        {
            return TryReadHeader(data, out ExrHeader header) == ResultCode.Success &&
                EXRGetSpectrumType(header).HasValue;
        }

        public static SpectrumType? EXRGetSpectrumType(ExrHeader header)
        {
            if (header == null)
            {
                return null;
            }

            if (FindCustomAttribute(header, SpectralLayoutVersionAttribute) == null)
            {
                return null;
            }

            bool hasReflective = false;
            bool hasEmissive = false;
            bool hasStokes = false;
            foreach (ExrChannel channel in header.Channels)
            {
                if (channel.Name.StartsWith("T.", StringComparison.Ordinal))
                {
                    hasReflective = true;
                }
                else if (channel.Name.Length >= 3 &&
                    channel.Name[0] == 'S' &&
                    channel.Name[1] >= '0' &&
                    channel.Name[1] <= '3' &&
                    channel.Name[2] == '.')
                {
                    hasEmissive = true;
                    if (channel.Name[1] != '0')
                    {
                        hasStokes = true;
                    }
                }
            }

            if (hasReflective)
            {
                return SpectrumType.Reflective;
            }

            if (hasStokes)
            {
                return SpectrumType.Polarised;
            }

            if (hasEmissive)
            {
                return SpectrumType.Emissive;
            }

            return null;
        }

        public static string EXRFormatWavelength(float wavelengthNm)
        {
            int whole = (int)wavelengthNm;
            int fraction = (int)((wavelengthNm - whole) * 1000000.0f + 0.5f);
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:000000}", whole, fraction);
        }

        public static string EXRSpectralChannelName(float wavelengthNm, int stokesComponent)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "S{0}.{1}nm",
                stokesComponent,
                EXRFormatWavelength(wavelengthNm));
        }

        public static string EXRReflectiveChannelName(float wavelengthNm)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "T.{0}nm",
                EXRFormatWavelength(wavelengthNm));
        }

        public static float EXRParseSpectralChannelWavelength(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
            {
                return -1.0f;
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
            else if (remaining.Length >= 2 &&
                remaining[0] == 'T' &&
                remaining[1] == '.')
            {
                remaining = remaining.Slice(2);
            }
            else
            {
                return -1.0f;
            }

            int nmIndex = remaining.IndexOf("nm".AsSpan(), StringComparison.Ordinal);
            if (nmIndex <= 0)
            {
                return -1.0f;
            }

            string wavelength = remaining.Slice(0, nmIndex).ToString().Replace(',', '.');
            if (!float.TryParse(wavelength, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            {
                return -1.0f;
            }

            return result;
        }

        public static int EXRGetStokesComponent(string channelName)
        {
            if (string.IsNullOrEmpty(channelName) || channelName.Length < 3)
            {
                return -1;
            }

            if (channelName[0] == 'S' &&
                channelName[1] >= '0' &&
                channelName[1] <= '3' &&
                channelName[2] == '.')
            {
                return channelName[1] - '0';
            }

            return -1;
        }

        public static bool EXRIsSpectralChannel(string channelName)
        {
            return EXRParseSpectralChannelWavelength(channelName) > 0.0f;
        }

        public static float[] EXRGetWavelengths(ExrHeader header)
        {
            if (header == null || header.Channels.Count == 0)
            {
                return Array.Empty<float>();
            }

            List<float> wavelengths = new List<float>();
            foreach (ExrChannel channel in header.Channels)
            {
                float wavelength = EXRParseSpectralChannelWavelength(channel.Name);
                if (wavelength <= 0.0f)
                {
                    continue;
                }

                bool exists = false;
                foreach (float existing in wavelengths)
                {
                    if (Math.Abs(existing - wavelength) < 0.01f)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    wavelengths.Add(wavelength);
                }
            }

            wavelengths.Sort();
            return wavelengths.ToArray();
        }

        public static ResultCode EXRSetSpectralAttributes(ExrHeader header, SpectrumType spectrumType, string units)
        {
            if (header == null)
            {
                return ResultCode.InvalidArgument;
            }

            SetOrReplaceCustomAttribute(header, ExrAttribute.FromString(SpectralLayoutVersionAttribute, "1.0"));
            RemoveCustomAttribute(header, ReflectiveUnitsAttribute);
            RemoveCustomAttribute(header, EmissiveUnitsAttribute);
            RemoveCustomAttribute(header, PolarisationHandednessAttribute);

            if (!string.IsNullOrEmpty(units))
            {
                string unitsAttributeName = spectrumType == SpectrumType.Reflective
                    ? ReflectiveUnitsAttribute
                    : EmissiveUnitsAttribute;
                SetOrReplaceCustomAttribute(header, ExrAttribute.FromString(unitsAttributeName, units));
            }

            if (spectrumType == SpectrumType.Polarised)
            {
                SetOrReplaceCustomAttribute(header, ExrAttribute.FromString(PolarisationHandednessAttribute, "left"));
            }

            return ResultCode.Success;
        }

        public static string? EXRGetSpectralUnits(ExrHeader header)
        {
            return FindCustomAttribute(header, ReflectiveUnitsAttribute)?.GetStringValue() ??
                FindCustomAttribute(header, EmissiveUnitsAttribute)?.GetStringValue();
        }

        internal static int TypeSize(ExrPixelType type)
        {
            switch (type)
            {
                case ExrPixelType.UInt:
                    return 4;
                case ExrPixelType.Half:
                    return 2;
                case ExrPixelType.Float:
                    return 4;
                default:
                    return 0;
            }
        }

        internal static float ReadSingleLittleEndian(ReadOnlySpan<byte> data)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));
        }

        internal static void WriteSingleLittleEndian(Span<byte> destination, float value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));
        }

        private static ResultCode TryBuildRgbaImage(
            ReadOnlySpan<float> data,
            int width,
            int height,
            int components,
            bool asFp16,
            out ExrImage image,
            out ExrHeader header)
        {
            image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
            header = new ExrHeader();

            if (width <= 0 || height <= 0)
            {
                return ResultCode.InvalidArgument;
            }

            if (components != 1 && components != 3 && components != 4)
            {
                return ResultCode.InvalidArgument;
            }

            int pixelCount = checked(width * height);
            if (pixelCount * components != data.Length)
            {
                return ResultCode.InvalidArgument;
            }

            ExrPixelType saveType = asFp16 ? ExrPixelType.Half : ExrPixelType.Float;
            string[] channelNames;
            int[] componentIndices;
            switch (components)
            {
                case 4:
                    channelNames = new[] { "A", "B", "G", "R" };
                    componentIndices = new[] { 3, 2, 1, 0 };
                    break;
                case 3:
                    channelNames = new[] { "B", "G", "R" };
                    componentIndices = new[] { 2, 1, 0 };
                    break;
                default:
                    channelNames = new[] { "A" };
                    componentIndices = new[] { 0 };
                    break;
            }

            ExrImageChannel[] channels = new ExrImageChannel[channelNames.Length];
            for (int channelIndex = 0; channelIndex < channelNames.Length; channelIndex++)
            {
                byte[] channelData = new byte[pixelCount * sizeof(float)];
                int componentIndex = componentIndices[channelIndex];
                for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
                {
                    float value = data[pixelIndex * components + componentIndex];
                    WriteSingleLittleEndian(channelData.AsSpan(pixelIndex * sizeof(float), sizeof(float)), value);
                }

                channels[channelIndex] = new ExrImageChannel(
                    new ExrChannel(channelNames[channelIndex], saveType),
                    ExrPixelType.Float,
                    channelData);
            }

            image = new ExrImage(width, height, channels);
            header = new ExrHeader
            {
                Compression = (width < 16 && height < 16) ? CompressionType.None : CompressionType.ZIP,
            };
            return ResultCode.Success;
        }

        private static bool TryReadFile(string filename, out byte[] data, out ResultCode result)
        {
            data = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(filename))
            {
                result = ResultCode.InvalidArgument;
                return false;
            }

            try
            {
                data = File.ReadAllBytes(filename);
                result = ResultCode.Success;
                return true;
            }
            catch (ArgumentException)
            {
                result = ResultCode.InvalidArgument;
                return false;
            }
            catch (NotSupportedException)
            {
                result = ResultCode.InvalidArgument;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                result = ResultCode.CannotOpenFile;
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                result = ResultCode.CannotOpenFile;
                return false;
            }
            catch (FileNotFoundException)
            {
                result = ResultCode.CannotOpenFile;
                return false;
            }
            catch (IOException)
            {
                result = ResultCode.CannotOpenFile;
                return false;
            }
        }

        private static ResultCode TryWriteFile(string filename, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return ResultCode.InvalidArgument;
            }

            try
            {
                File.WriteAllBytes(filename, data);
                return ResultCode.Success;
            }
            catch (ArgumentException)
            {
                return ResultCode.InvalidArgument;
            }
            catch (NotSupportedException)
            {
                return ResultCode.InvalidArgument;
            }
            catch (UnauthorizedAccessException)
            {
                return ResultCode.CannotWriteFile;
            }
            catch (DirectoryNotFoundException)
            {
                return ResultCode.CannotWriteFile;
            }
            catch (IOException)
            {
                return ResultCode.CannotWriteFile;
            }
        }

        private static ExrAttribute? FindCustomAttribute(ExrHeader header, string name)
        {
            if (header == null)
            {
                return null;
            }

            foreach (ExrAttribute attribute in header.CustomAttributes)
            {
                if (string.Equals(attribute.Name, name, StringComparison.Ordinal))
                {
                    return attribute;
                }
            }

            return null;
        }

        private static void SetOrReplaceCustomAttribute(ExrHeader header, ExrAttribute attribute)
        {
            int existingIndex = FindCustomAttributeIndex(header, attribute.Name);
            if (existingIndex >= 0)
            {
                header.CustomAttributes[existingIndex] = attribute;
            }
            else
            {
                header.CustomAttributes.Add(attribute);
            }
        }

        private static void RemoveCustomAttribute(ExrHeader header, string name)
        {
            int existingIndex = FindCustomAttributeIndex(header, name);
            if (existingIndex >= 0)
            {
                header.CustomAttributes.RemoveAt(existingIndex);
            }
        }

        private static int FindCustomAttributeIndex(ExrHeader header, string name)
        {
            for (int i = 0; i < header.CustomAttributes.Count; i++)
            {
                if (string.Equals(header.CustomAttributes[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
