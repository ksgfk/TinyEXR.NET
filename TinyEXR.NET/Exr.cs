using System.Buffers.Binary;
using TinyEXR.Format;
using TinyEXR.IO;

namespace TinyEXR
{
    /// <summary>
    /// Convenience entry points over ExrReader and ExrWriter. All models and status codes
    /// use the V3 API. Caller-owned streams remain open and retain their position.
    /// </summary>
    public static class Exr
    {
        private const uint MultipartFlag = 1U << 12;
        private delegate ExrResult ReadOperation<T>(ExrReader reader, out T? value) where T : class;

        public static ExrResult LoadEXRImageFromFile(string filename, out Part? part)
            => ReadFile(filename, ReadFlat, out part);

        public static ExrResult LoadDeepEXR(string filename, out Part? part)
            => ReadFile(filename, ReadDeep, out part);

        public static ExrResult LoadEXRMultipartImageFromFile(string filename, out Image? image)
            => ReadFile(filename, ReadMultipart, out image);

        public static ExrResult ParseEXRHeaderFromFile(string filename, out uint rawVersionField, out Header? header)
        {
            ExrResult status = ReadFile(filename, ReadSingleHeader, out HeaderSet? parsed);
            rawVersionField = parsed?.Version ?? 0;
            header = parsed?.Headers[0];
            return status;
        }

        public static ExrResult ParseEXRMultipartHeaderFromFile(string filename, out uint rawVersionField, out Header[] headers)
        {
            ExrResult status = ReadFile(filename, ReadMultipartHeaders, out HeaderSet? parsed);
            rawVersionField = parsed?.Version ?? 0;
            headers = parsed?.Headers ?? Array.Empty<Header>();
            return status;
        }

        public static ExrResult LoadEXRImageFromMemory(ReadOnlySpan<byte> data, out Part? part)
            => ReadMemory(data, ReadFlat, out part);

        public static ExrResult LoadDeepEXRFromMemory(ReadOnlySpan<byte> data, out Part? part)
            => ReadMemory(data, ReadDeep, out part);

        public static ExrResult LoadEXRMultipartImageFromMemory(ReadOnlySpan<byte> data, out Image? image)
            => ReadMemory(data, ReadMultipart, out image);

        public static ExrResult ParseEXRHeaderFromMemory(ReadOnlySpan<byte> data, out uint rawVersionField, out Header? header)
        {
            ExrResult status = ReadMemory(data, ReadSingleHeader, out HeaderSet? parsed);
            rawVersionField = parsed?.Version ?? 0;
            header = parsed?.Headers[0];
            return status;
        }

        public static ExrResult ParseEXRMultipartHeaderFromMemory(ReadOnlySpan<byte> data, out uint rawVersionField, out Header[] headers)
        {
            ExrResult status = ReadMemory(data, ReadMultipartHeaders, out HeaderSet? parsed);
            rawVersionField = parsed?.Version ?? 0;
            headers = parsed?.Headers ?? Array.Empty<Header>();
            return status;
        }

        public static ExrResult LoadEXRImageFromStream(Stream stream, out Part? part)
            => ReadStream(stream, ReadFlat, out part);

        public static ExrResult LoadDeepEXRFromStream(Stream stream, out Part? part)
            => ReadStream(stream, ReadDeep, out part);

        public static ExrResult LoadEXRMultipartImageFromStream(Stream stream, out Image? image)
            => ReadStream(stream, ReadMultipart, out image);

        public static ExrResult ParseEXRHeaderFromStream(Stream stream, out uint rawVersionField, out Header? header)
        {
            ExrResult status = ReadStream(stream, ReadSingleHeader, out HeaderSet? parsed);
            rawVersionField = parsed?.Version ?? 0;
            header = parsed?.Headers[0];
            return status;
        }

        public static ExrResult ParseEXRMultipartHeaderFromStream(Stream stream, out uint rawVersionField, out Header[] headers)
        {
            ExrResult status = ReadStream(stream, ReadMultipartHeaders, out HeaderSet? parsed);
            rawVersionField = parsed?.Version ?? 0;
            headers = parsed?.Headers ?? Array.Empty<Header>();
            return status;
        }

        public static ExrResult ParseEXRVersionFromMemory(ReadOnlySpan<byte> data, out uint rawVersionField)
        {
            rawVersionField = 0;
            if (data.Length < 8 || !ExrFile.IsExr(data))
                return ExrResult.InvalidFile;
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
            ExrResult status = ExrFormatParser.InterpretVersionField(version, out _, out _);
            if (status == ExrResult.Success)
                rawVersionField = version;
            return status;
        }

        public static ExrResult ParseEXRVersionFromStream(Stream stream, out uint rawVersionField)
        {
            ExrResult status = ReadPrefix(stream, 8, out byte[] prefix);
            rawVersionField = 0;
            return status == ExrResult.Success ? ParseEXRVersionFromMemory(prefix, out rawVersionField) : status;
        }

        public static ExrResult ParseEXRVersionFromFile(string filename, out uint rawVersionField)
        {
            rawVersionField = 0;
            try
            {
                using FileStream stream = File.OpenRead(filename);
                return ParseEXRVersionFromStream(stream, out rawVersionField);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        public static bool IsEXRFromMemory(ReadOnlySpan<byte> data) => ExrFile.IsExr(data);
        public static bool IsEXRFromStream(Stream stream)
            => ReadPrefix(stream, 4, out byte[] prefix) == ExrResult.Success && ExrFile.IsExr(prefix);
        public static bool IsEXR(string filename)
        {
            try
            {
                using FileStream stream = File.OpenRead(filename);
                return IsEXRFromStream(stream);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return false;
            }
        }

        public static ExrResult LoadEXR(string filename, out float[] rgba, out int width, out int height)
            => LoadEXRWithLayer(filename, null, out rgba, out width, out height);
        public static ExrResult LoadEXRWithLayer(string filename, string? layer, out float[] rgba, out int width, out int height)
            => BuildRgba(LoadEXRImageFromFile(filename, out Part? part), part, layer, out rgba, out width, out height);
        public static ExrResult LoadEXRFromMemory(ReadOnlySpan<byte> data, out float[] rgba, out int width, out int height)
            => LoadEXRWithLayerFromMemory(data, null, out rgba, out width, out height);
        public static ExrResult LoadEXRWithLayerFromMemory(ReadOnlySpan<byte> data, string? layer, out float[] rgba, out int width, out int height)
            => BuildRgba(LoadEXRImageFromMemory(data, out Part? part), part, layer, out rgba, out width, out height);
        public static ExrResult LoadEXRFromStream(Stream stream, out float[] rgba, out int width, out int height)
            => LoadEXRWithLayerFromStream(stream, null, out rgba, out width, out height);
        public static ExrResult LoadEXRWithLayerFromStream(Stream stream, string? layer, out float[] rgba, out int width, out int height)
            => BuildRgba(LoadEXRImageFromStream(stream, out Part? part), part, layer, out rgba, out width, out height);

        public static ExrResult EXRLayers(string filename, out string[] layers)
            => BuildLayers(ParseEXRHeaderFromFile(filename, out _, out Header? header), header, out layers);
        public static ExrResult EXRLayersFromMemory(ReadOnlySpan<byte> data, out string[] layers)
            => BuildLayers(ParseEXRHeaderFromMemory(data, out _, out Header? header), header, out layers);
        public static ExrResult EXRLayersFromStream(Stream stream, out string[] layers)
            => BuildLayers(ParseEXRHeaderFromStream(stream, out _, out Header? header), header, out layers);

        public static ExrResult SaveEXRToMemory(ReadOnlySpan<float> data, int width, int height, int components, bool asFp16, out byte[] outBuffer)
        {
            outBuffer = Array.Empty<byte>();
            try
            {
                return SaveEXRImageToMemory(CreateSimplePart(data, width, height, components, asFp16), out outBuffer);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        public static ExrResult SaveEXR(ReadOnlySpan<float> data, int width, int height, int components, bool asFp16, string filename)
        {
            try
            {
                return SaveEXRImageToFile(CreateSimplePart(data, width, height, components, asFp16), filename);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        public static ExrResult SaveEXRImageToMemory(Part part, out byte[] encoded)
        {
            encoded = Array.Empty<byte>();
            try
            {
                return SaveMemory(new Image(new[] { Normalize(part) }), false, out encoded);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        public static ExrResult SaveEXRImageToFile(Part part, string filename)
        {
            try
            {
                return ExrFile.SaveToFile(new Image(new[] { Normalize(part) }), filename).Status;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        public static ExrResult SaveEXRMultipartImageToMemory(Image image, out byte[] encoded)
        {
            encoded = Array.Empty<byte>();
            try
            {
                return SaveMemory(Normalize(image), true, out encoded);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        public static ExrResult SaveEXRMultipartImageToFile(Image image, string filename)
        {
            try
            {
                return ExrFile.SaveToFile(Normalize(image), filename, new WriterOptions(forceMultipart: true)).Status;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        public static int EXRNumLevels(Part part) => part?.Levels.Count ?? 0;

        public static ExrResult EXRSetNameAttr(Header header, string? name, out Header? updated)
        {
            updated = null;
            if (header == null)
                return ExrResult.InvalidArgument;
            try
            {
                updated = CopyHeader(header, header.LineOrder, name ?? string.Empty);
                return ExrResult.Success;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        public static bool IsSpectralEXR(string filename)
            => ParseEXRHeaderFromFile(filename, out _, out Header? header) == ExrResult.Success && Spectral.IsSpectral(header!);
        public static bool IsSpectralEXRFromMemory(ReadOnlySpan<byte> data)
            => ParseEXRHeaderFromMemory(data, out _, out Header? header) == ExrResult.Success && Spectral.IsSpectral(header!);
        public static bool IsSpectralEXRFromStream(Stream stream)
            => ParseEXRHeaderFromStream(stream, out _, out Header? header) == ExrResult.Success && Spectral.IsSpectral(header!);
        public static SpectralType EXRGetSpectrumType(Header header) => Spectral.GetSpectrumType(header);
        public static string EXRFormatWavelength(float wavelengthNm)
        {
            string channel = Spectral.GetChannelName(SpectralType.Reflective, wavelengthNm);
            return channel.Substring(2, channel.Length - 4);
        }
        public static string EXRSpectralChannelName(float wavelengthNm, int stokesComponent)
            => Spectral.GetChannelName(SpectralType.Emissive, wavelengthNm, stokesComponent);
        public static string EXRReflectiveChannelName(float wavelengthNm)
            => Spectral.GetChannelName(SpectralType.Reflective, wavelengthNm);
        public static float EXRParseSpectralChannelWavelength(string channelName)
            => Spectral.TryParseChannelWavelength(channelName, out float value) ? value : -1;
        public static int EXRGetStokesComponent(string channelName)
            => Spectral.TryGetStokesComponent(channelName, out int value) ? value : -1;
        public static bool EXRIsSpectralChannel(string channelName)
            => Spectral.TryParseChannelWavelength(channelName, out _);
        public static float[] EXRGetWavelengths(Header header) => Spectral.GetWavelengths(header);
        public static string? EXRGetSpectralUnits(Header header) => Spectral.GetUnits(header);
        public static ExrResult EXRSetSpectralAttributes(Header header, SpectralType spectrumType, string units, out Header? updated)
        {
            updated = null;
            try
            {
                updated = Spectral.WithSpectralAttributes(header, spectrumType, units);
                return ExrResult.Success;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        private static Part CreateSimplePart(ReadOnlySpan<float> data, int width, int height, int components, bool half)
        {
            if (components != 1 && components != 3 && components != 4)
                throw new ArgumentOutOfRangeException(nameof(components));
            Part part = PartConversion.FromInterleavedFloat(data, width, height, components,
                half ? PixelType.Half : PixelType.Float, width < 16 && height < 16 ? Compression.None : Compression.ZIP);
            if (components != 1)
                return part;
            Channel channel = new Channel("A", half ? PixelType.Half : PixelType.Float);
            Header header = new Header(PartType.Scanline, part.Header.DataWindow, new[] { channel }, part.Header.Compression);
            FlatLevel level = (FlatLevel)part.Levels[0];
            return new Part(header, new[] { new FlatLevel(0, 0, level.Region,
                new[] { new ChannelBuffer("A", channel.PixelType, level.GetChannel("Y").Data) }) }, isComplete: true);
        }

        private static ExrResult SaveMemory(Image image, bool multipart, out byte[] encoded)
        {
            WriterResult<byte[]> result = ExrFile.SaveToMemory(image, new WriterOptions(forceMultipart: multipart));
            encoded = result.Status == ExrResult.Success ? result.Value! : Array.Empty<byte>();
            return result.Status;
        }

        private static Image Normalize(Image image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            return new Image(image.Parts.Select(Normalize));
        }

        private static Part Normalize(Part part)
        {
            if (part == null)
                throw new ArgumentNullException(nameof(part));
            Header header = part.Header;
            return header.PartType == PartType.Scanline && header.LineOrder != LineOrder.IncreasingY
                ? new Part(CopyHeader(header, LineOrder.IncreasingY, header.Name), part.Levels, part.IsComplete) : part;
        }

        private static Header CopyHeader(Header header, LineOrder lineOrder, string name)
            => new Header(header.PartType, header.DataWindow, header.Channels, header.Compression,
                lineOrder, header.DisplayWindow, header.PixelAspectRatio, header.ScreenWindowCenter,
                header.ScreenWindowWidth, header.Tiles, name, header.Chromaticities, header.Attributes);

        private sealed class HeaderSet
        {
            internal uint Version { get; }
            internal Header[] Headers { get; }
            internal HeaderSet(uint version, Header[] headers)
            {
                Version = version;
                Headers = headers;
            }
        }

        private static ExrResult ReadSingleHeader(ExrReader reader, out HeaderSet? value) => ReadHeaders(reader, false, out value);
        private static ExrResult ReadMultipartHeaders(ExrReader reader, out HeaderSet? value) => ReadHeaders(reader, true, out value);
        private static ExrResult ReadHeaders(ExrReader reader, bool multipart, out HeaderSet? value)
        {
            value = null;
            ExrResult status = reader.ParseHeader().Status;
            if (status != ExrResult.Success)
                return status;
            uint version = reader.GetRawVersionField();
            if (((version & MultipartFlag) != 0) != multipart)
                return ExrResult.Unsupported;
            Header[] headers = new Header[reader.NumParts];
            for (int i = 0; i < headers.Length; i++)
                headers[i] = reader.GetHeader(i);
            value = new HeaderSet(version, headers);
            return ExrResult.Success;
        }

        private static ExrResult ReadFlat(ExrReader reader, out Part? part) => ReadSingle(reader, false, out part);
        private static ExrResult ReadDeep(ExrReader reader, out Part? part) => ReadSingle(reader, true, out part);
        private static ExrResult ReadSingle(ExrReader reader, bool deep, out Part? part)
        {
            part = null;
            ExrResult status = ReadSingleHeader(reader, out HeaderSet? headers);
            if (status != ExrResult.Success)
                return status;
            if (headers!.Headers[0].IsDeep != deep)
                return ExrResult.Unsupported;
            ReaderResult<Part> result = reader.ReadPart(0);
            if (result.Status == ExrResult.Success)
                part = result.Value;
            return result.Status;
        }

        private static ExrResult ReadMultipart(ExrReader reader, out Image? image)
        {
            image = null;
            ExrResult status = ReadMultipartHeaders(reader, out HeaderSet? headers);
            if (status != ExrResult.Success)
                return status;
            Part[] parts = new Part[headers!.Headers.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                ReaderResult<Part> result = reader.ReadPart(i);
                if (result.Status != ExrResult.Success)
                    return result.Status;
                parts[i] = result.Value!;
            }
            image = new Image(parts);
            return ExrResult.Success;
        }

        private static ExrResult ReadMemory<T>(ReadOnlySpan<byte> data, ReadOperation<T> operation, out T? value) where T : class
        {
            value = null;
            try
            {
                using ExrReader reader = ExrReader.OpenMemory(data.ToArray());
                ExrResult status = operation(reader, out T? result);
                if (status == ExrResult.Success)
                    value = result;
                return status;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        private static ExrResult ReadFile<T>(string filename, ReadOperation<T> operation, out T? value) where T : class
        {
            value = null;
            try
            {
                using FileStream stream = File.OpenRead(filename);
                return ReadStream(stream, operation, out value);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                value = null;
                return Status(exception);
            }
        }

        private static ExrResult ReadStream<T>(Stream stream, ReadOperation<T> operation, out T? value) where T : class
        {
            value = null;
            try
            {
                if (stream == null || !stream.CanRead || !stream.CanSeek)
                    return ExrResult.InvalidArgument;
                long position = stream.Position;
                T? result;
                ExrResult status;
                try
                {
                    using StreamDataSource source = new StreamDataSource(stream, position, leaveOpen: true);
                    using ExrReader reader = ExrReader.OpenSource(source);
                    status = operation(reader, out result);
                }
                finally
                {
                    stream.Position = position;
                }
                if (status == ExrResult.Success)
                    value = result;
                return status;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        private static ExrResult ReadPrefix(Stream stream, int length, out byte[] prefix)
        {
            prefix = Array.Empty<byte>();
            try
            {
                if (stream == null || !stream.CanRead || !stream.CanSeek)
                    return ExrResult.InvalidArgument;
                long position = stream.Position;
                byte[] bytes = new byte[length];
                try
                {
                    int offset = 0;
                    while (offset < length)
                    {
                        int count = stream.Read(bytes, offset, length - offset);
                        if (count == 0)
                            return ExrResult.InvalidFile;
                        offset += count;
                    }
                }
                finally
                {
                    stream.Position = position;
                }
                prefix = bytes;
                return ExrResult.Success;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        private static ExrResult BuildLayers(ExrResult status, Header? header, out string[] layers)
        {
            layers = Array.Empty<string>();
            if (status != ExrResult.Success)
                return status;
            try
            {
                List<string> result = new List<string>();
                foreach (Channel channel in header!.Channels)
                {
                    int separator = channel.Name.LastIndexOf('.');
                    if (separator > 0 && separator + 1 < channel.Name.Length)
                    {
                        string layer = channel.Name.Substring(0, separator);
                        if (!result.Contains(layer, StringComparer.Ordinal))
                            result.Add(layer);
                    }
                }
                layers = result.ToArray();
                return ExrResult.Success;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        private static ExrResult BuildRgba(ExrResult status, Part? part, string? layer, out float[] rgba, out int width, out int height)
        {
            rgba = Array.Empty<float>();
            width = 0;
            height = 0;
            if (status != ExrResult.Success)
                return status;
            try
            {
                List<(int Index, string Name)> selected = new List<(int, string)>();
                string prefix = string.IsNullOrWhiteSpace(layer) ? string.Empty : layer + ".";
                for (int i = 0; i < part!.Header.Channels.Count; i++)
                {
                    string name = part.Header.Channels[i].Name;
                    if (prefix.Length == 0)
                    {
                        int separator = name.LastIndexOf('.');
                        if (separator > 0)
                            continue;
                        if (separator == 0 && name.Length > 1)
                            name = name.Substring(1);
                    }
                    else
                    {
                        if (!name.StartsWith(prefix, StringComparison.Ordinal))
                            continue;
                        name = name.Substring(prefix.Length);
                    }
                    selected.Add((i, name));
                }
                if (selected.Count == 0)
                    return ExrResult.InvalidArgument;
                int r = -1, g = -1, b = -1, a = -1;
                foreach ((int index, string name) in selected)
                {
                    switch (name)
                    {
                        case "R":
                            r = index;
                            break;
                        case "G":
                            g = index;
                            break;
                        case "B":
                            b = index;
                            break;
                        case "A":
                            a = index;
                            break;
                    }
                }
                if (selected.Count > 1 && (r < 0 || g < 0 || b < 0))
                    return ExrResult.InvalidArgument;
                InterleavedFloatImage pixels = PartConversion.ToInterleavedFloat(part);
                int count = checked(pixels.Width * pixels.Height);
                float[] result = new float[checked(count * 4)];
                for (int pixel = 0; pixel < count; pixel++)
                {
                    int source = pixel * pixels.Channels, target = pixel * 4;
                    if (selected.Count == 1)
                    {
                        float value = pixels.Data[source + selected[0].Index];
                        result[target] = result[target + 1] = result[target + 2] = result[target + 3] = value;
                    }
                    else
                    {
                        result[target] = pixels.Data[source + r];
                        result[target + 1] = pixels.Data[source + g];
                        result[target + 2] = pixels.Data[source + b];
                        result[target + 3] = a < 0 ? 1 : pixels.Data[source + a];
                    }
                }
                rgba = result;
                width = pixels.Width;
                height = pixels.Height;
                return ExrResult.Success;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                return Status(exception);
            }
        }

        private static bool IsExpected(Exception exception)
            => exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException ||
                exception is NotSupportedException || exception is ObjectDisposedException || exception is OutOfMemoryException || exception is OverflowException;
        private static ExrResult Status(Exception exception)
            => exception is OutOfMemoryException ? ExrResult.OutOfMemory :
                exception is IOException || exception is UnauthorizedAccessException || exception is ObjectDisposedException ? ExrResult.IO :
                exception is NotSupportedException ? ExrResult.Unsupported : ExrResult.InvalidArgument;
    }
}
