using System.Buffers.Binary;
using System.Numerics;
#if NET10_0_OR_GREATER
using System.IO.Compression;
#endif

namespace TinyEXR.PortV1
{
    internal static class ExrImplementation
    {
        private const int ExrVersionHeaderSize = 8;
        private const uint Magic = 20000630;

        private sealed class ParsedHeader
        {
            public ExrVersion Version { get; set; } = new ExrVersion();

            public ExrHeader Header { get; set; } = new ExrHeader();

            public int HeaderEndOffset { get; set; }
        }

        private sealed class ParsedMultipartHeaders
        {
            public ExrVersion Version { get; set; } = new ExrVersion();

            public List<ParsedHeader> Headers { get; } = new List<ParsedHeader>();

            public int HeaderSectionEndOffset { get; set; }
        }

        private readonly struct LayerChannel
        {
            public LayerChannel(int index, string name)
            {
                Index = index;
                Name = name;
            }

            public int Index { get; }

            public string Name { get; }
        }

        internal static ResultCode TryReadVersion(ReadOnlySpan<byte> data, out ExrVersion version)
        {
            version = new ExrVersion();

            if (data.Length < ExrVersionHeaderSize)
            {
                return ResultCode.InvalidExrVersion;
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            {
                return ResultCode.InvalidMagicNumver;
            }

            int exrVersion = data[4];
            byte flags = data[5];
            if (exrVersion <= 0)
            {
                return ResultCode.InvalidExrVersion;
            }

            version.Version = exrVersion;
            version.Tiled = (flags & 0x2) != 0;
            version.LongName = (flags & 0x4) != 0;
            version.NonImage = (flags & 0x8) != 0;
            version.Multipart = (flags & 0x10) != 0;
            return ResultCode.Success;
        }

        internal static ResultCode TryReadHeader(ReadOnlySpan<byte> data, out ExrVersion version, out ExrHeader header)
        {
            try
            {
                ResultCode result = TryParseHeader(data, out ParsedHeader? parsed);
                if (result == ResultCode.Success)
                {
                    version = parsed!.Version;
                    header = parsed.Header;
                    return ResultCode.Success;
                }

                version = new ExrVersion();
                header = new ExrHeader();
                return result;
            }
            catch (OverflowException)
            {
                version = new ExrVersion();
                header = new ExrHeader();
                return ResultCode.InvalidHeader;
            }
            catch (ArgumentOutOfRangeException)
            {
                version = new ExrVersion();
                header = new ExrHeader();
                return ResultCode.InvalidData;
            }
        }

        internal static ResultCode TryReadMultipartHeaders(ReadOnlySpan<byte> data, out ExrVersion version, out ExrHeader[] headers)
        {
            headers = Array.Empty<ExrHeader>();
            try
            {
                ResultCode result = TryParseMultipartHeaders(data, out ParsedMultipartHeaders? parsed);
                if (result != ResultCode.Success)
                {
                    version = new ExrVersion();
                    return result;
                }

                version = parsed!.Version;
                headers = parsed.Headers.Select(static part => part.Header).ToArray();
                return ResultCode.Success;
            }
            catch (OverflowException)
            {
                version = new ExrVersion();
                return ResultCode.InvalidHeader;
            }
            catch (ArgumentOutOfRangeException)
            {
                version = new ExrVersion();
                return ResultCode.InvalidData;
            }
        }

        internal static ResultCode TryReadImage(ReadOnlySpan<byte> data, out ExrHeader header, out ExrImage image)
        {
            header = new ExrHeader();
            image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());

            ParsedHeader? parsed;
            try
            {
                ResultCode result = TryParseHeader(data, out parsed);
                if (result != ResultCode.Success)
                {
                    return result;
                }
            }
            catch (OverflowException)
            {
                return ResultCode.InvalidHeader;
            }
            catch (ArgumentOutOfRangeException)
            {
                return ResultCode.InvalidData;
            }

            header = parsed!.Header;

            try
            {
                ResultCode readValidation = ValidateReadableImageHeader(parsed);
                if (readValidation != ResultCode.Success)
                {
                    return readValidation;
                }

                ResultCode offsetResult = TryReadSinglePartChunkOffsets(data, parsed, out long[] offsets);
                if (offsetResult != ResultCode.Success)
                {
                    return offsetResult;
                }

                return TryDecodeImage(data, parsed, offsets, out image);
            }
            catch (OverflowException)
            {
                image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
                return ResultCode.InvalidData;
            }
            catch (ArgumentOutOfRangeException)
            {
                image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
                return ResultCode.InvalidData;
            }
        }

        internal static ResultCode TryReadImage(ReadOnlySpan<byte> data, ExrHeader requestedHeader, out ExrImage image)
        {
            image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
            if (requestedHeader == null)
            {
                return ResultCode.InvalidArgument;
            }

            ParsedHeader? parsed;
            try
            {
                ResultCode result = TryParseHeader(data, out parsed);
                if (result != ResultCode.Success)
                {
                    return result;
                }
            }
            catch (OverflowException)
            {
                return ResultCode.InvalidHeader;
            }
            catch (ArgumentOutOfRangeException)
            {
                return ResultCode.InvalidData;
            }

            ParsedHeader effectiveParsed = new ParsedHeader
            {
                Version = parsed!.Version,
                Header = parsed.Header.CloneShallow(),
                HeaderEndOffset = parsed.HeaderEndOffset,
            };

            if (!TryApplyRequestedPixelTypes(effectiveParsed.Header, requestedHeader))
            {
                return ResultCode.InvalidArgument;
            }

            try
            {
                ResultCode readValidation = ValidateReadableImageHeader(effectiveParsed);
                if (readValidation != ResultCode.Success)
                {
                    return readValidation;
                }

                ResultCode offsetResult = TryReadSinglePartChunkOffsets(data, effectiveParsed, out long[] offsets);
                if (offsetResult != ResultCode.Success)
                {
                    return offsetResult;
                }

                return TryDecodeImage(data, effectiveParsed, offsets, out image);
            }
            catch (OverflowException)
            {
                image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
                return ResultCode.InvalidData;
            }
            catch (ArgumentOutOfRangeException)
            {
                image = new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
                return ResultCode.InvalidData;
            }
        }

        internal static ResultCode TryReadMultipartImages(ReadOnlySpan<byte> data, IReadOnlyList<ExrHeader> requestedHeaders, out ExrImage[] images)
        {
            images = Array.Empty<ExrImage>();
            if (requestedHeaders == null)
            {
                return ResultCode.InvalidArgument;
            }

            ParsedMultipartHeaders? parsed;
            try
            {
                ResultCode result = TryParseMultipartHeaders(data, out parsed);
                if (result != ResultCode.Success)
                {
                    return result;
                }
            }
            catch (OverflowException)
            {
                return ResultCode.InvalidHeader;
            }
            catch (ArgumentOutOfRangeException)
            {
                return ResultCode.InvalidData;
            }

            if (requestedHeaders.Count != parsed!.Headers.Count)
            {
                return ResultCode.InvalidArgument;
            }

            for (int i = 0; i < parsed.Headers.Count; i++)
            {
                if (!TryApplyRequestedPixelTypes(parsed.Headers[i].Header, requestedHeaders[i]))
                {
                    return ResultCode.InvalidArgument;
                }

                ResultCode validation = ValidateReadableImageHeader(parsed.Headers[i]);
                if (validation != ResultCode.Success)
                {
                    return validation;
                }
            }

            ResultCode offsetReadResult = TryReadMultipartChunkOffsets(data, parsed, out long[][] offsetsByPart);
            if (offsetReadResult != ResultCode.Success)
            {
                return offsetReadResult;
            }

            images = new ExrImage[parsed.Headers.Count];
            for (int i = 0; i < parsed.Headers.Count; i++)
            {
                ResultCode decodeResult = TryDecodeImage(data, parsed.Headers[i], offsetsByPart[i], out ExrImage image);
                if (decodeResult != ResultCode.Success)
                {
                    images = Array.Empty<ExrImage>();
                    return decodeResult;
                }

                images[i] = image;
            }

            return ResultCode.Success;
        }

        internal static ResultCode TryReadDeepImage(ReadOnlySpan<byte> data, out ExrHeader header, out ExrDeepImage image)
        {
            header = new ExrHeader();
            image = new ExrDeepImage(0, 0, Array.Empty<int[]>(), Array.Empty<ExrDeepChannel>());

            ParsedHeader? parsed;
            try
            {
                ResultCode result = TryParseHeader(data, out parsed);
                if (result != ResultCode.Success)
                {
                    return result;
                }
            }
            catch (OverflowException)
            {
                return ResultCode.InvalidHeader;
            }
            catch (ArgumentOutOfRangeException)
            {
                return ResultCode.InvalidData;
            }

            header = parsed!.Header;

            if (parsed.Version.Multipart)
            {
                return ResultCode.UnsupportedFeature;
            }

            if (!header.IsDeep || header.Tiles != null)
            {
                return ResultCode.UnsupportedFeature;
            }

            if (!SupportsCompression(header.Compression))
            {
                return ResultCode.UnsupportedFeature;
            }

            try
            {
                return TryDecodeDeepImage(data, parsed, out image);
            }
            catch (OverflowException)
            {
                image = new ExrDeepImage(0, 0, Array.Empty<int[]>(), Array.Empty<ExrDeepChannel>());
                return ResultCode.InvalidData;
            }
            catch (ArgumentOutOfRangeException)
            {
                image = new ExrDeepImage(0, 0, Array.Empty<int[]>(), Array.Empty<ExrDeepChannel>());
                return ResultCode.InvalidData;
            }
        }

        internal static ResultCode TryReadLayers(ReadOnlySpan<byte> data, out string[] layers)
        {
            layers = Array.Empty<string>();
            ResultCode result = TryParseHeader(data, out ParsedHeader? parsed);
            if (result != ResultCode.Success)
            {
                return result;
            }

            layers = GetLayers(parsed!.Header).ToArray();
            return ResultCode.Success;
        }

        internal static ResultCode TryReadRgba(ReadOnlySpan<byte> data, string? layerName, out float[] rgba, out int width, out int height)
        {
            rgba = Array.Empty<float>();
            width = 0;
            height = 0;

            ResultCode result = TryReadImage(data, out ExrHeader header, out ExrImage image);
            if (result != ResultCode.Success)
            {
                return result;
            }

            List<LayerChannel> layerChannels = GetChannelsInLayer(header, layerName);
            if (layerChannels.Count == 0)
            {
                return ResultCode.LayerNotFound;
            }

            width = image.Width;
            height = image.Height;
            rgba = new float[width * height * 4];

            if (layerChannels.Count == 1)
            {
                ExrImageChannel channel = image.Channels[layerChannels[0].Index];
                for (int i = 0; i < width * height; i++)
                {
                    float value = ReadSampleAsFloat(channel.Data, channel.DataType, i);
                    int rgbaOffset = i * 4;
                    rgba[rgbaOffset + 0] = value;
                    rgba[rgbaOffset + 1] = value;
                    rgba[rgbaOffset + 2] = value;
                    rgba[rgbaOffset + 3] = value;
                }

                return ResultCode.Success;
            }

            int r = -1;
            int g = -1;
            int b = -1;
            int a = -1;
            foreach (LayerChannel layerChannel in layerChannels)
            {
                if (string.Equals(layerChannel.Name, "R", StringComparison.Ordinal))
                {
                    r = layerChannel.Index;
                }
                else if (string.Equals(layerChannel.Name, "G", StringComparison.Ordinal))
                {
                    g = layerChannel.Index;
                }
                else if (string.Equals(layerChannel.Name, "B", StringComparison.Ordinal))
                {
                    b = layerChannel.Index;
                }
                else if (string.Equals(layerChannel.Name, "A", StringComparison.Ordinal))
                {
                    a = layerChannel.Index;
                }
            }

            if (r < 0 || g < 0 || b < 0)
            {
                return ResultCode.InvalidData;
            }

            ExrImageChannel rChannel = image.Channels[r];
            ExrImageChannel gChannel = image.Channels[g];
            ExrImageChannel bChannel = image.Channels[b];
            ExrImageChannel? aChannel = a >= 0 ? image.Channels[a] : null;
            for (int i = 0; i < width * height; i++)
            {
                int rgbaOffset = i * 4;
                rgba[rgbaOffset + 0] = ReadSampleAsFloat(rChannel.Data, rChannel.DataType, i);
                rgba[rgbaOffset + 1] = ReadSampleAsFloat(gChannel.Data, gChannel.DataType, i);
                rgba[rgbaOffset + 2] = ReadSampleAsFloat(bChannel.Data, bChannel.DataType, i);
                rgba[rgbaOffset + 3] = aChannel == null ? 1.0f : ReadSampleAsFloat(aChannel.Data, aChannel.DataType, i);
            }

            return ResultCode.Success;
        }

        internal static ResultCode TryWriteImage(ExrImage image, ExrHeader? header, out byte[] encoded)
        {
            encoded = Array.Empty<byte>();
            if (image == null)
            {
                return ResultCode.InvalidArgument;
            }

            ExrHeader effectiveHeader = CreateWriteHeader(image, header);
            if (effectiveHeader.IsMultipart || effectiveHeader.IsDeep || effectiveHeader.Tiles != null)
            {
                return ResultCode.UnsupportedFeature;
            }

            if (!SupportsCompression(effectiveHeader.Compression))
            {
                return ResultCode.UnsupportedFeature;
            }

            if (effectiveHeader.LineOrder != LineOrderType.IncreasingY)
            {
                return ResultCode.UnsupportedFeature;
            }

            int width = image.Width;
            int height = image.Height;
            if (width <= 0 || height <= 0)
            {
                return ResultCode.InvalidArgument;
            }

            foreach (ExrImageChannel channel in image.Channels)
            {
                if (channel.Channel.SamplingX != 1 || channel.Channel.SamplingY != 1)
                {
                    return ResultCode.UnsupportedFeature;
                }

                int expectedLength = checked(width * height * Exr.TypeSize(channel.DataType));
                if (channel.Data.Length != expectedLength)
                {
                    return ResultCode.InvalidArgument;
                }
            }

            int linesPerChunk = NumScanlines(effectiveHeader.Compression);
            int blockCount = (height + linesPerChunk - 1) / linesPerChunk;
            List<byte[]> chunks = new List<byte[]>(blockCount);
            int offsetTableSize = blockCount * sizeof(long);

            using MemoryStream headerStream = new MemoryStream();
            WriteVersion(headerStream);
            WriteHeader(headerStream, effectiveHeader);

            long chunkOffset = headerStream.Length + offsetTableSize;
            long[] offsets = new long[blockCount];
            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                int startY = blockIndex * linesPerChunk;
                int numLines = Math.Min(linesPerChunk, height - startY);
                ResultCode chunkResult = TryEncodeScanlineChunk(image, effectiveHeader, startY, numLines, out byte[] chunk);
                if (chunkResult != ResultCode.Success)
                {
                    return chunkResult;
                }

                offsets[blockIndex] = chunkOffset;
                chunkOffset += chunk.Length;
                chunks.Add(chunk);
            }

            using MemoryStream output = new MemoryStream();
            output.Write(headerStream.ToArray(), 0, checked((int)headerStream.Length));
            byte[] offsetBuffer = new byte[sizeof(long)];
            foreach (long offset in offsets)
            {
                BinaryPrimitives.WriteInt64LittleEndian(offsetBuffer, offset);
                output.Write(offsetBuffer, 0, offsetBuffer.Length);
            }

            foreach (byte[] chunk in chunks)
            {
                output.Write(chunk, 0, chunk.Length);
            }

            encoded = output.ToArray();
            return ResultCode.Success;
        }

        private static ResultCode TryEncodeScanlineChunk(ExrImage image, ExrHeader header, int startY, int numLines, out byte[] chunk)
        {
            chunk = Array.Empty<byte>();

            List<ExrImageChannel> channels = image.Channels.ToList();
            int[] channelOffsets = BuildChannelOffsets(channels.Select(static c => c.Channel.Type).ToArray(), out int pixelSize);
            int width = image.Width;
            byte[] raw = new byte[checked(width * numLines * pixelSize)];

            for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
            {
                ExrImageChannel channel = channels[channelIndex];
                int targetSampleSize = Exr.TypeSize(channel.Channel.Type);
                int sourceSampleSize = Exr.TypeSize(channel.DataType);

                for (int y = 0; y < numLines; y++)
                {
                    int sourceRow = startY + y;
                    int rowBase = checked(y * width * pixelSize);
                    int channelBase = rowBase + checked(channelOffsets[channelIndex] * width);
                    for (int x = 0; x < width; x++)
                    {
                        int sourceOffset = checked((sourceRow * width + x) * sourceSampleSize);
                        int targetOffset = channelBase + x * targetSampleSize;
                        if (!TryConvertSample(channel.Data.AsSpan(sourceOffset, sourceSampleSize), channel.DataType, raw.AsSpan(targetOffset, targetSampleSize), channel.Channel.Type))
                        {
                            return ResultCode.UnsupportedFeature;
                        }
                    }
                }
            }

            byte[] payload;
            ResultCode compressionResult = TryEncodePayload(header.Compression, raw, out payload);
            if (compressionResult != ResultCode.Success)
            {
                return compressionResult;
            }

            chunk = new byte[sizeof(int) * 2 + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(chunk, startY + header.DataWindow.MinY);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(sizeof(int)), payload.Length);
            payload.CopyTo(chunk, sizeof(int) * 2);
            return ResultCode.Success;
        }

        private static ResultCode TryEncodePayload(CompressionType compression, byte[] raw, out byte[] payload)
        {
            payload = raw;
            switch (compression)
            {
                case CompressionType.None:
                    return ResultCode.Success;
                case CompressionType.ZIP:
                case CompressionType.ZIPS:
                    return TryCompressZip(raw, out payload);
                default:
                    return ResultCode.UnsupportedFeature;
            }
        }

        private static ExrHeader CreateWriteHeader(ExrImage image, ExrHeader? header)
        {
            ExrHeader effective = header?.CloneShallow() ?? new ExrHeader();
            effective.DataWindow = new ExrBox2i(0, 0, image.Width - 1, image.Height - 1);
            if (effective.DisplayWindow.Width <= 0 || effective.DisplayWindow.Height <= 0)
            {
                effective.DisplayWindow = effective.DataWindow;
            }

            effective.Channels.Clear();
            foreach (ExrImageChannel channel in image.Channels)
            {
                effective.Channels.Add(new ExrChannel(channel.Channel.Name, channel.Channel.Type, channel.Channel.RequestedPixelType, channel.Channel.SamplingX, channel.Channel.SamplingY, channel.Channel.Linear));
            }

            return effective;
        }

        private static ResultCode ValidateReadableImageHeader(ParsedHeader parsed)
        {
            if (parsed.Header.IsDeep)
            {
                return ResultCode.UnsupportedFeature;
            }

            if (!SupportsCompression(parsed.Header.Compression))
            {
                return ResultCode.UnsupportedFeature;
            }

            foreach (ExrChannel channel in parsed.Header.Channels)
            {
                if (channel.SamplingX != 1 || channel.SamplingY != 1)
                {
                    return ResultCode.UnsupportedFeature;
                }
            }

            return ResultCode.Success;
        }

        private static ResultCode TryReadSinglePartChunkOffsets(ReadOnlySpan<byte> data, ParsedHeader parsed, out long[] offsets)
        {
            ExrHeader header = parsed.Header;
            int chunkCount;
            if (header.Tiles == null)
            {
                int height = header.DataWindow.Height;
                int linesPerChunk = NumScanlines(header.Compression);
                chunkCount = (height + linesPerChunk - 1) / linesPerChunk;
            }
            else
            {
                CalculateTileInfo(header, header.DataWindow.Width, header.DataWindow.Height, out _, out _, out _, out _, out chunkCount);
            }

            int offsetTableOffset = parsed.HeaderEndOffset;
            int offsetTableSize = checked(chunkCount * sizeof(long));
            if (offsetTableOffset < 0 || offsetTableOffset + offsetTableSize > data.Length)
            {
                offsets = Array.Empty<long>();
                return ResultCode.InvalidData;
            }

            offsets = new long[chunkCount];
            for (int i = 0; i < chunkCount; i++)
            {
                offsets[i] = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offsetTableOffset + i * sizeof(long), sizeof(long)));
            }

            return ResultCode.Success;
        }

        private static ResultCode TryReadMultipartChunkOffsets(ReadOnlySpan<byte> data, ParsedMultipartHeaders parsed, out long[][] offsetsByPart)
        {
            offsetsByPart = Array.Empty<long[]>();
            int marker = parsed.HeaderSectionEndOffset;
            long[][] partOffsets = new long[parsed.Headers.Count][];

            for (int partIndex = 0; partIndex < parsed.Headers.Count; partIndex++)
            {
                ExrHeader header = parsed.Headers[partIndex].Header;
                int chunkCount;
                if (header.Tiles == null || header.Tiles.LevelMode == ExrTileLevelMode.OneLevel)
                {
                    chunkCount = header.ChunkCount;
                }
                else
                {
                    CalculateTileInfo(header, header.DataWindow.Width, header.DataWindow.Height, out _, out _, out _, out _, out chunkCount);
                }

                int offsetTableSize = checked(chunkCount * sizeof(long));
                if (marker < 0 || marker + offsetTableSize > data.Length)
                {
                    return ResultCode.InvalidData;
                }

                long[] offsets = new long[chunkCount];
                for (int i = 0; i < chunkCount; i++)
                {
                    long rawOffset = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(marker + i * sizeof(long), sizeof(long)));
                    if (rawOffset < 0 || rawOffset + sizeof(int) >= data.Length)
                    {
                        return ResultCode.InvalidData;
                    }

                    int partNumber = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)rawOffset, sizeof(int)));
                    if (partNumber != partIndex)
                    {
                        return ResultCode.InvalidData;
                    }

                    offsets[i] = rawOffset + sizeof(int);
                }

                partOffsets[partIndex] = offsets;
                marker += offsetTableSize;
            }

            offsetsByPart = partOffsets;
            return ResultCode.Success;
        }

        private static ResultCode TryDecodeImage(ReadOnlySpan<byte> data, ParsedHeader parsed, ReadOnlySpan<long> offsets, out ExrImage image)
        {
            return parsed.Header.Tiles == null
                ? TryDecodeScanlineImage(data, parsed, offsets, out image)
                : TryDecodeTiledImage(data, parsed, offsets, out image);
        }

        private static ResultCode TryDecodeScanlineImage(ReadOnlySpan<byte> data, ParsedHeader parsed, ReadOnlySpan<long> offsets, out ExrImage image)
        {
            ExrHeader header = parsed.Header;
            int width = header.DataWindow.Width;
            int height = header.DataWindow.Height;
            List<ExrImageChannel> channels = CreateOutputChannels(width, height, header.Channels);
            image = new ExrImage(width, height, channels);

            int linesPerChunk = NumScanlines(header.Compression);
            int blockCount = (height + linesPerChunk - 1) / linesPerChunk;
            if (offsets.Length != blockCount)
            {
                return ResultCode.InvalidData;
            }

            int[] channelOffsets = BuildChannelOffsets(header.Channels.Select(static c => c.Type).ToArray(), out int pixelSize);
            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                long chunkOffset = offsets[blockIndex];
                if (chunkOffset < 0 || chunkOffset + sizeof(int) * 2 > data.Length)
                {
                    return ResultCode.InvalidData;
                }

                int lineNumber = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)chunkOffset, sizeof(int)));
                int packedSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)chunkOffset + sizeof(int), sizeof(int)));
                if (packedSize < 0 || chunkOffset + sizeof(int) * 2 + packedSize > data.Length)
                {
                    return ResultCode.InvalidData;
                }

                int relativeLine = lineNumber - header.DataWindow.MinY;
                if (relativeLine < 0)
                {
                    return ResultCode.InvalidData;
                }

                int chunkLineCount = Math.Min(linesPerChunk, height - relativeLine);
                byte[] raw;
                ResultCode decodeResult = TryDecodePayload(header.Compression, data.Slice((int)chunkOffset + sizeof(int) * 2, packedSize), checked(width * chunkLineCount * pixelSize), out raw);
                if (decodeResult != ResultCode.Success)
                {
                    return decodeResult;
                }

                for (int line = 0; line < chunkLineCount; line++)
                {
                    int targetY = header.LineOrder == LineOrderType.IncreasingY
                        ? relativeLine + line
                        : height - 1 - (relativeLine + line);
                    if (targetY < 0 || targetY >= height)
                    {
                        return ResultCode.InvalidData;
                    }

                    int lineBase = line * width * pixelSize;
                    for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
                    {
                        int sourceOffset = lineBase + channelOffsets[channelIndex] * width;
                        int destinationSampleIndex = targetY * width;
                        ResultCode copyResult = TryCopySamples(
                            raw,
                            sourceOffset,
                            header.Channels[channelIndex].Type,
                            channels[channelIndex].Data,
                            destinationSampleIndex,
                            channels[channelIndex].DataType,
                            width);
                        if (copyResult != ResultCode.Success)
                        {
                            return copyResult;
                        }
                    }
                }
            }

            return ResultCode.Success;
        }

        private static ResultCode TryDecodeTiledImage(ReadOnlySpan<byte> data, ParsedHeader parsed, ReadOnlySpan<long> offsets, out ExrImage image)
        {
            ExrHeader header = parsed.Header;
            int width = header.DataWindow.Width;
            int height = header.DataWindow.Height;
            image = new ExrImage(width, height, Array.Empty<ExrImageChannel>());

            if (header.Tiles == null)
            {
                return ResultCode.InvalidData;
            }

            int[] numXTiles;
            int[] numYTiles;
            CalculateTileInfo(header, width, height, out numXTiles, out numYTiles, out int numXLevels, out int numYLevels, out int totalBlocks);
            if (offsets.Length != totalBlocks)
            {
                return ResultCode.InvalidData;
            }

            int[] channelOffsets = BuildChannelOffsets(header.Channels.Select(static c => c.Type).ToArray(), out int pixelSize);
            int blockIndex = 0;
            int levelCount = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? numXLevels * numYLevels : numXLevels;
            ExrImageLevel[] levels = new ExrImageLevel[levelCount];
            List<ExrImageChannel>[] levelChannels = new List<ExrImageChannel>[levelCount];
            List<ExrTile>[] levelTiles = new List<ExrTile>[levelCount];
            int[] levelWidths = new int[levelCount];
            int[] levelHeights = new int[levelCount];
            for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
            {
                int levelX = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex % numXLevels : levelIndex;
                int levelY = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex / numXLevels : levelIndex;
                int levelWidth = LevelSize(width, levelX, header.Tiles.RoundingMode);
                int levelHeight = LevelSize(height, levelY, header.Tiles.RoundingMode);
                levelWidths[levelIndex] = levelWidth;
                levelHeights[levelIndex] = levelHeight;
                levelChannels[levelIndex] = CreateOutputChannels(levelWidth, levelHeight, header.Channels);
                levelTiles[levelIndex] = new List<ExrTile>();
            }

            for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
            {
                int levelX = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex % numXLevels : levelIndex;
                int levelY = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex / numXLevels : levelIndex;
                int levelWidth = levelWidths[levelIndex];
                int levelHeight = levelHeights[levelIndex];
                int tileColumns = numXTiles[levelX];
                int tileRows = numYTiles[levelY];
                List<ExrImageChannel> currentLevelChannels = levelChannels[levelIndex];
                List<ExrTile> currentLevelTiles = levelTiles[levelIndex];

                for (int tileY = 0; tileY < tileRows; tileY++)
                {
                    for (int tileX = 0; tileX < tileColumns; tileX++)
                    {
                        long chunkOffset = offsets[blockIndex++];
                        if (chunkOffset < 0 || chunkOffset + sizeof(int) * 5 > data.Length)
                        {
                            return ResultCode.InvalidData;
                        }

                        int headerTileX = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)chunkOffset, sizeof(int)));
                        int headerTileY = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)chunkOffset + 4, sizeof(int)));
                        int headerLevelX = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)chunkOffset + 8, sizeof(int)));
                        int headerLevelY = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)chunkOffset + 12, sizeof(int)));
                        int packedSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)chunkOffset + 16, sizeof(int)));
                        if (packedSize < 0 || chunkOffset + 20 + packedSize > data.Length)
                        {
                            return ResultCode.InvalidData;
                        }

                        int tilePixelX = headerTileX * header.Tiles.TileSizeX;
                        int tilePixelY = headerTileY * header.Tiles.TileSizeY;
                        int tileWidth = Math.Min(header.Tiles.TileSizeX, levelWidth - tilePixelX);
                        int tileHeight = Math.Min(header.Tiles.TileSizeY, levelHeight - tilePixelY);
                        byte[] raw;
                        ResultCode decodeResult = TryDecodePayload(header.Compression, data.Slice((int)chunkOffset + 20, packedSize), checked(tileWidth * tileHeight * pixelSize), out raw);
                        if (decodeResult != ResultCode.Success)
                        {
                            return decodeResult;
                        }

                        if (headerLevelX != levelX || headerLevelY != levelY)
                        {
                            return ResultCode.InvalidData;
                        }

                        ExrImageChannel[] tileChannels = CreateOutputChannels(tileWidth, tileHeight, header.Channels).ToArray();

                        for (int localY = 0; localY < tileHeight; localY++)
                        {
                            int destinationY = tilePixelY + localY;
                            if (destinationY < 0 || destinationY >= levelHeight)
                            {
                                continue;
                            }

                            int lineBase = localY * tileWidth * pixelSize;
                            for (int channelIndex = 0; channelIndex < currentLevelChannels.Count; channelIndex++)
                            {
                                int sourceOffset = lineBase + channelOffsets[channelIndex] * tileWidth;
                                if (tilePixelX < 0 || tilePixelX + tileWidth > levelWidth)
                                {
                                    continue;
                                }

                                ResultCode levelCopyResult = TryCopySamples(
                                    raw,
                                    sourceOffset,
                                    header.Channels[channelIndex].Type,
                                    currentLevelChannels[channelIndex].Data,
                                    destinationY * levelWidth + tilePixelX,
                                    currentLevelChannels[channelIndex].DataType,
                                    tileWidth);
                                if (levelCopyResult != ResultCode.Success)
                                {
                                    return levelCopyResult;
                                }

                                ResultCode tileCopyResult = TryCopySamples(
                                    raw,
                                    sourceOffset,
                                    header.Channels[channelIndex].Type,
                                    tileChannels[channelIndex].Data,
                                    localY * tileWidth,
                                    tileChannels[channelIndex].DataType,
                                    tileWidth);
                                if (tileCopyResult != ResultCode.Success)
                                {
                                    return tileCopyResult;
                                }
                            }
                        }

                        currentLevelTiles.Add(new ExrTile(tilePixelX, tilePixelY, levelX, levelY, tileWidth, tileHeight, tileChannels));
                    }
                }
            }

            for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
            {
                int levelX = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex % numXLevels : levelIndex;
                int levelY = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex / numXLevels : levelIndex;
                levels[levelIndex] = new ExrImageLevel(levelX, levelY, levelWidths[levelIndex], levelHeights[levelIndex], levelChannels[levelIndex], levelTiles[levelIndex]);
            }

            image = new ExrImage(levels);
            return ResultCode.Success;
        }

        private static ResultCode TryDecodeDeepImage(ReadOnlySpan<byte> data, ParsedHeader parsed, out ExrDeepImage image)
        {
            ExrHeader header = parsed.Header;
            int width = header.DataWindow.Width;
            int height = header.DataWindow.Height;
            image = new ExrDeepImage(0, 0, Array.Empty<int[]>(), Array.Empty<ExrDeepChannel>());

            int linesPerChunk = NumScanlines(header.Compression);
            int blockCount = (height + linesPerChunk - 1) / linesPerChunk;
            int offsetTableOffset = parsed.HeaderEndOffset;
            int offsetTableSize = blockCount * sizeof(long);
            if (offsetTableOffset + offsetTableSize > data.Length)
            {
                return ResultCode.InvalidData;
            }

            long[] offsets = new long[blockCount];
            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                offsets[blockIndex] = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offsetTableOffset + blockIndex * sizeof(long), sizeof(long)));
            }

            int[][] offsetRows = new int[height][];
            float[][][] rowsByChannel = new float[header.Channels.Count][][];
            for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
            {
                rowsByChannel[channelIndex] = new float[height][];
            }

            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                long chunkOffset = offsets[blockIndex];
                if (chunkOffset < 0 || chunkOffset + 28 > data.Length)
                {
                    return ResultCode.InvalidData;
                }

                int lineNo = BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)chunkOffset, 4));
                long packedOffsetSize = BinaryPrimitives.ReadInt64LittleEndian(data.Slice((int)chunkOffset + 4, 8));
                long packedSampleSize = BinaryPrimitives.ReadInt64LittleEndian(data.Slice((int)chunkOffset + 12, 8));
                long unpackedSampleSize = BinaryPrimitives.ReadInt64LittleEndian(data.Slice((int)chunkOffset + 20, 8));
                if (packedOffsetSize < 0 || packedSampleSize < 0 || unpackedSampleSize < 0)
                {
                    return ResultCode.InvalidData;
                }

                ResultCode offsetResult = TryDecodePayload(CompressionType.ZIPS, data.Slice((int)chunkOffset + 28, (int)packedOffsetSize), width * sizeof(int), out byte[] packedOffsets);
                if (offsetResult != ResultCode.Success)
                {
                    return offsetResult;
                }

                ResultCode sampleResult = TryDecodePayload(CompressionType.ZIPS, data.Slice((int)chunkOffset + 28 + (int)packedOffsetSize, (int)packedSampleSize), (int)unpackedSampleSize, out byte[] sampleBytes);
                if (sampleResult != ResultCode.Success)
                {
                    return sampleResult;
                }

                int[] pixelOffsets = new int[width];
                for (int x = 0; x < width; x++)
                {
                    pixelOffsets[x] = BinaryPrimitives.ReadInt32LittleEndian(packedOffsets.AsSpan(x * sizeof(int), sizeof(int)));
                }

                int rowIndex = lineNo - header.DataWindow.MinY;
                if (rowIndex < 0 || rowIndex >= height)
                {
                    return ResultCode.InvalidData;
                }

                offsetRows[rowIndex] = pixelOffsets;
                int sampleSize = header.Channels.Sum(static channel => Exr.TypeSize(channel.Type));
                if ((pixelOffsets.Length == 0 ? 0 : pixelOffsets[pixelOffsets.Length - 1]) * sampleSize != sampleBytes.Length)
                {
                    return ResultCode.InvalidData;
                }

                int sampleCount = sampleSize == 0 ? 0 : sampleBytes.Length / sampleSize;
                int dataOffset = 0;
                for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
                {
                    ExrPixelType pixelType = header.Channels[channelIndex].Type;
                    float[] row = new float[sampleCount];
                    int typeSize = Exr.TypeSize(pixelType);
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        row[sampleIndex] = ReadSampleAsFloat(sampleBytes, pixelType, dataOffset / typeSize + sampleIndex);
                    }

                    rowsByChannel[channelIndex][rowIndex] = row;
                    dataOffset += sampleCount * typeSize;
                }
            }

            image = new ExrDeepImage(
                width,
                height,
                offsetRows,
                header.Channels.Select((channel, index) => new ExrDeepChannel(channel.Name, rowsByChannel[index])).ToArray());
            return ResultCode.Success;
        }

        private static ResultCode TryDecodePayload(CompressionType compression, ReadOnlySpan<byte> payload, int expectedSize, out byte[] raw)
        {
            raw = Array.Empty<byte>();
            switch (compression)
            {
                case CompressionType.None:
                    if (payload.Length != expectedSize)
                    {
                        return ResultCode.InvalidData;
                    }

                    raw = payload.ToArray();
                    return ResultCode.Success;
                case CompressionType.ZIPS:
                case CompressionType.ZIP:
                    return TryDecompressZip(payload, expectedSize, out raw);
                default:
                    return ResultCode.UnsupportedFeature;
            }
        }

        private static bool SupportsCompression(CompressionType compression)
        {
            switch (compression)
            {
                case CompressionType.None:
                    return true;
                case CompressionType.ZIPS:
                case CompressionType.ZIP:
#if NET10_0_OR_GREATER
                    return true;
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        private static ResultCode TryParseHeader(ReadOnlySpan<byte> data, out ParsedHeader? parsed)
        {
            parsed = null;
            ResultCode versionResult = TryReadVersion(data, out ExrVersion version);
            if (versionResult != ResultCode.Success)
            {
                return versionResult;
            }

            if (version.Multipart)
            {
                return ResultCode.UnsupportedFeature;
            }

            ResultCode parseResult = TryParseHeaderAt(data.Slice(ExrVersionHeaderSize), version, out ExrHeader? header, out int headerLength, out bool emptyHeader);
            if (parseResult != ResultCode.Success)
            {
                return parseResult;
            }

            if (emptyHeader || header == null)
            {
                return ResultCode.InvalidHeader;
            }

            parsed = new ParsedHeader
            {
                Version = version,
                Header = header,
                HeaderEndOffset = ExrVersionHeaderSize + headerLength,
            };
            return ResultCode.Success;
        }

        private static ResultCode TryParseMultipartHeaders(ReadOnlySpan<byte> data, out ParsedMultipartHeaders? parsed)
        {
            parsed = null;
            ResultCode versionResult = TryReadVersion(data, out ExrVersion version);
            if (versionResult != ResultCode.Success)
            {
                return versionResult;
            }

            if (!version.Multipart)
            {
                return ResultCode.UnsupportedFeature;
            }

            ParsedMultipartHeaders result = new ParsedMultipartHeaders
            {
                Version = version,
            };

            int offset = ExrVersionHeaderSize;
            while (true)
            {
                ResultCode headerResult = TryParseHeaderAt(data.Slice(offset), version, out ExrHeader? header, out int headerLength, out bool emptyHeader);
                if (headerResult != ResultCode.Success)
                {
                    return headerResult;
                }

                if (emptyHeader)
                {
                    offset += 1;
                    break;
                }

                if (header == null || header.ChunkCount <= 0)
                {
                    return ResultCode.InvalidData;
                }

                result.Headers.Add(new ParsedHeader
                {
                    Version = version,
                    Header = header,
                    HeaderEndOffset = offset + headerLength,
                });

                offset += headerLength;
            }

            result.HeaderSectionEndOffset = offset;
            parsed = result;
            return ResultCode.Success;
        }

        private static ResultCode TryParseHeaderAt(ReadOnlySpan<byte> data, ExrVersion version, out ExrHeader? header, out int headerLength, out bool emptyHeader)
        {
            header = null;
            headerLength = 0;
            emptyHeader = false;

            if (version.Multipart)
            {
                if (data.Length == 0)
                {
                    return ResultCode.InvalidData;
                }

                if (data[0] == 0)
                {
                    emptyHeader = true;
                    headerLength = 1;
                    return ResultCode.Success;
                }
            }

            int offset = 0;
            ExrHeader parsedHeader = new ExrHeader
            {
                HasLongNames = version.LongName,
                IsMultipart = version.Multipart,
                IsDeep = version.NonImage,
            };

            bool hasCompression = false;
            bool hasDataWindow = false;
            bool hasDisplayWindow = false;
            bool hasLineOrder = false;
            bool hasPixelAspectRatio = false;
            bool hasScreenWindowCenter = false;
            bool hasScreenWindowWidth = false;
            bool hasName = false;
            bool hasType = false;

            while (true)
            {
                if (offset >= data.Length)
                {
                    return ResultCode.InvalidData;
                }

                if (data[offset] == 0)
                {
                    offset++;
                    break;
                }

                if (!TryReadNullTerminatedString(data, ref offset, out string attributeName) ||
                    !TryReadNullTerminatedString(data, ref offset, out string attributeType))
                {
                    return ResultCode.InvalidData;
                }

                if (offset + sizeof(int) > data.Length)
                {
                    return ResultCode.InvalidData;
                }

                int valueSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int)));
                offset += sizeof(int);
                if (valueSize < 0 || offset + valueSize > data.Length)
                {
                    return ResultCode.InvalidData;
                }

                ReadOnlySpan<byte> value = data.Slice(offset, valueSize);
                offset += valueSize;

                switch (attributeName)
                {
                    case "name":
                        parsedHeader.Name = DecodeStringAttributeValue(value);
                        hasName = !string.IsNullOrEmpty(parsedHeader.Name);
                        break;
                    case "type":
                        parsedHeader.PartType = DecodeStringAttributeValue(value);
                        hasType = !string.IsNullOrEmpty(parsedHeader.PartType);
                        if (string.Equals(parsedHeader.PartType, "deepscanline", StringComparison.Ordinal) ||
                            string.Equals(parsedHeader.PartType, "deeptile", StringComparison.Ordinal))
                        {
                            parsedHeader.IsDeep = true;
                        }
                        break;
                    case "channels":
                        ResultCode channelResult = TryParseChannels(value, parsedHeader.Channels);
                        if (channelResult != ResultCode.Success)
                        {
                            return channelResult;
                        }
                        break;
                    case "compression":
                        if (value.Length < 1)
                        {
                            return ResultCode.InvalidHeader;
                        }

                        if (!Enum.IsDefined(typeof(CompressionType), (int)value[0]))
                        {
                            return ResultCode.UnsupportedFormat;
                        }

                        parsedHeader.Compression = (CompressionType)value[0];
                        hasCompression = true;
                        break;
                    case "dataWindow":
                        if (value.Length < 16)
                        {
                            return ResultCode.InvalidHeader;
                        }

                        parsedHeader.DataWindow = new ExrBox2i(
                            BinaryPrimitives.ReadInt32LittleEndian(value),
                            BinaryPrimitives.ReadInt32LittleEndian(value.Slice(4)),
                            BinaryPrimitives.ReadInt32LittleEndian(value.Slice(8)),
                            BinaryPrimitives.ReadInt32LittleEndian(value.Slice(12)));
                        hasDataWindow = true;
                        break;
                    case "displayWindow":
                        if (value.Length < 16)
                        {
                            return ResultCode.InvalidHeader;
                        }

                        parsedHeader.DisplayWindow = new ExrBox2i(
                            BinaryPrimitives.ReadInt32LittleEndian(value),
                            BinaryPrimitives.ReadInt32LittleEndian(value.Slice(4)),
                            BinaryPrimitives.ReadInt32LittleEndian(value.Slice(8)),
                            BinaryPrimitives.ReadInt32LittleEndian(value.Slice(12)));
                        hasDisplayWindow = true;
                        break;
                    case "lineOrder":
                        if (value.Length < 1)
                        {
                            return ResultCode.InvalidHeader;
                        }

                        parsedHeader.LineOrder = (LineOrderType)value[0];
                        hasLineOrder = true;
                        break;
                    case "pixelAspectRatio":
                        if (value.Length < 4)
                        {
                            return ResultCode.InvalidHeader;
                        }

                        parsedHeader.PixelAspectRatio = Exr.ReadSingleLittleEndian(value);
                        hasPixelAspectRatio = true;
                        break;
                    case "screenWindowCenter":
                        if (value.Length < 8)
                        {
                            return ResultCode.InvalidHeader;
                        }

                        parsedHeader.ScreenWindowCenter = new Vector2(
                            Exr.ReadSingleLittleEndian(value),
                            Exr.ReadSingleLittleEndian(value.Slice(4)));
                        hasScreenWindowCenter = true;
                        break;
                    case "screenWindowWidth":
                        if (value.Length < 4)
                        {
                            return ResultCode.InvalidHeader;
                        }

                        parsedHeader.ScreenWindowWidth = Exr.ReadSingleLittleEndian(value);
                        hasScreenWindowWidth = true;
                        break;
                    case "tiles":
                        if (value.Length < 9)
                        {
                            return ResultCode.InvalidHeader;
                        }

                        parsedHeader.Tiles = new ExrTileDescription
                        {
                            TileSizeX = BinaryPrimitives.ReadInt32LittleEndian(value),
                            TileSizeY = BinaryPrimitives.ReadInt32LittleEndian(value.Slice(4)),
                            LevelMode = (ExrTileLevelMode)(value[8] & 0x3),
                            RoundingMode = (ExrTileRoundingMode)((value[8] >> 4) & 0x1),
                        };
                        break;
                    case "chunkCount":
                        if (value.Length < sizeof(int))
                        {
                            return ResultCode.InvalidHeader;
                        }

                        parsedHeader.ChunkCount = BinaryPrimitives.ReadInt32LittleEndian(value);
                        break;
                    default:
                        parsedHeader.CustomAttributes.Add(new ExrAttribute(attributeName, attributeType, value.ToArray()));
                        break;
                }
            }

            if (!hasCompression || !hasDataWindow || !hasDisplayWindow || !hasLineOrder || !hasPixelAspectRatio || !hasScreenWindowCenter || !hasScreenWindowWidth || parsedHeader.Channels.Count == 0)
            {
                return ResultCode.InvalidHeader;
            }

            if ((version.Multipart || version.NonImage) && (!hasName || !hasType))
            {
                return ResultCode.InvalidHeader;
            }

            parsedHeader.HeaderLength = offset;
            header = parsedHeader;
            headerLength = offset;
            return ResultCode.Success;
        }

        private static ResultCode TryParseChannels(ReadOnlySpan<byte> value, IList<ExrChannel> channels)
        {
            channels.Clear();
            int offset = 0;
            while (offset < value.Length)
            {
                if (!TryReadNullTerminatedString(value, ref offset, out string name))
                {
                    return ResultCode.InvalidData;
                }

                if (string.IsNullOrEmpty(name))
                {
                    return ResultCode.Success;
                }

                if (offset + 16 > value.Length)
                {
                    return ResultCode.InvalidData;
                }

                int pixelType = BinaryPrimitives.ReadInt32LittleEndian(value.Slice(offset, 4));
                byte linear = value[offset + 4];
                int xSampling = BinaryPrimitives.ReadInt32LittleEndian(value.Slice(offset + 8, 4));
                int ySampling = BinaryPrimitives.ReadInt32LittleEndian(value.Slice(offset + 12, 4));
                offset += 16;

                if (!Enum.IsDefined(typeof(ExrPixelType), pixelType))
                {
                    return ResultCode.UnsupportedFormat;
                }

                channels.Add(new ExrChannel(name, (ExrPixelType)pixelType, xSampling, ySampling, linear));
            }

            return ResultCode.InvalidData;
        }

        private static string DecodeStringAttributeValue(ReadOnlySpan<byte> value)
        {
            int length = value.Length;
            if (length > 0 && value[length - 1] == 0)
            {
                length--;
            }

            return System.Text.Encoding.UTF8.GetString(value.Slice(0, length));
        }

        private static bool TryReadNullTerminatedString(ReadOnlySpan<byte> data, ref int offset, out string value)
        {
            for (int i = offset; i < data.Length; i++)
            {
                if (data[i] == 0)
                {
                    value = System.Text.Encoding.UTF8.GetString(data.Slice(offset, i - offset));
                    offset = i + 1;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        private static int NumScanlines(CompressionType compression)
        {
            return compression == CompressionType.ZIP ? 16 : 1;
        }

        private static List<ExrImageChannel> CreateOutputChannels(int width, int height, IEnumerable<ExrChannel> sourceChannels)
        {
            List<ExrImageChannel> channels = new List<ExrImageChannel>();
            foreach (ExrChannel channel in sourceChannels)
            {
                ExrPixelType outputType = channel.RequestedPixelType;
                channels.Add(new ExrImageChannel(channel, outputType, new byte[checked(width * height * Exr.TypeSize(outputType))]));
            }

            return channels;
        }

        private static int[] BuildChannelOffsets(ExrPixelType[] pixelTypes, out int pixelSize)
        {
            int[] offsets = new int[pixelTypes.Length];
            int running = 0;
            for (int i = 0; i < pixelTypes.Length; i++)
            {
                offsets[i] = running;
                running += Exr.TypeSize(pixelTypes[i]);
            }

            pixelSize = running;
            return offsets;
        }

        private static List<string> GetLayers(ExrHeader header)
        {
            List<string> layers = new List<string>();
            foreach (ExrChannel channel in header.Channels)
            {
                int separator = channel.Name.LastIndexOf('.');
                if (separator > 0 && separator + 1 < channel.Name.Length)
                {
                    string layer = channel.Name.Substring(0, separator);
                    if (!ContainsOrdinal(layers, layer))
                    {
                        layers.Add(layer);
                    }
                }
            }

            return layers;
        }

        private static bool ContainsOrdinal(IList<string> values, string candidate)
        {
            foreach (string value in values)
            {
                if (string.Equals(value, candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<LayerChannel> GetChannelsInLayer(ExrHeader header, string? layerName)
        {
            List<LayerChannel> channels = new List<LayerChannel>();
            string effectiveLayer = string.IsNullOrWhiteSpace(layerName) ? string.Empty : layerName;
            for (int i = 0; i < header.Channels.Count; i++)
            {
                string channelName = header.Channels[i].Name;
                if (effectiveLayer.Length == 0)
                {
                    int separator = channelName.LastIndexOf('.');
                    if (separator > 0)
                    {
                        continue;
                    }

                    if (separator == 0 && separator + 1 < channelName.Length)
                    {
                        channelName = channelName.Substring(separator + 1);
                    }
                }
                else
                {
                    string prefix = effectiveLayer + ".";
                    if (!channelName.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    channelName = channelName.Substring(prefix.Length);
                }

                channels.Add(new LayerChannel(i, channelName));
            }

            return channels;
        }

        private static float ReadSampleAsFloat(byte[] data, ExrPixelType pixelType, int index)
        {
            int offset = index * Exr.TypeSize(pixelType);
            return pixelType switch
            {
                ExrPixelType.UInt => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)),
                ExrPixelType.Half => HalfHelper.HalfToSingle(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2))),
                ExrPixelType.Float => Exr.ReadSingleLittleEndian(data.AsSpan(offset, 4)),
                _ => 0.0f,
            };
        }

        private static bool TryApplyRequestedPixelTypes(ExrHeader parsedHeader, ExrHeader requestedHeader)
        {
            if (requestedHeader.Channels.Count != parsedHeader.Channels.Count)
            {
                return false;
            }

            for (int i = 0; i < parsedHeader.Channels.Count; i++)
            {
                ExrChannel parsedChannel = parsedHeader.Channels[i];
                ExrChannel requestedChannel = requestedHeader.Channels[i];
                if (!string.Equals(parsedChannel.Name, requestedChannel.Name, StringComparison.Ordinal) ||
                    parsedChannel.Type != requestedChannel.Type)
                {
                    return false;
                }

                parsedChannel.RequestedPixelType = requestedChannel.RequestedPixelType;
            }

            return true;
        }

        private static ResultCode TryCopySamples(
            byte[] source,
            int sourceOffset,
            ExrPixelType sourceType,
            byte[] destination,
            int destinationSampleIndex,
            ExrPixelType destinationType,
            int sampleCount)
        {
            int sourceSampleSize = Exr.TypeSize(sourceType);
            int destinationSampleSize = Exr.TypeSize(destinationType);
            if (sourceSampleSize <= 0 || destinationSampleSize <= 0)
            {
                return ResultCode.UnsupportedFeature;
            }

            int destinationOffset = checked(destinationSampleIndex * destinationSampleSize);
            if (sourceType == destinationType)
            {
                Buffer.BlockCopy(source, sourceOffset, destination, destinationOffset, checked(sampleCount * sourceSampleSize));
                return ResultCode.Success;
            }

            for (int i = 0; i < sampleCount; i++)
            {
                if (!TryConvertSample(
                    source.AsSpan(sourceOffset + i * sourceSampleSize, sourceSampleSize),
                    sourceType,
                    destination.AsSpan(destinationOffset + i * destinationSampleSize, destinationSampleSize),
                    destinationType))
                {
                    return ResultCode.UnsupportedFeature;
                }
            }

            return ResultCode.Success;
        }

        private static bool TryConvertSample(ReadOnlySpan<byte> source, ExrPixelType sourceType, Span<byte> destination, ExrPixelType targetType)
        {
            if (sourceType == targetType)
            {
                source.CopyTo(destination);
                return true;
            }

            float floatValue = sourceType switch
            {
                ExrPixelType.UInt => BinaryPrimitives.ReadUInt32LittleEndian(source),
                ExrPixelType.Half => HalfHelper.HalfToSingle(BinaryPrimitives.ReadUInt16LittleEndian(source)),
                ExrPixelType.Float => Exr.ReadSingleLittleEndian(source),
                _ => 0.0f,
            };

            if (targetType == ExrPixelType.Float)
            {
                Exr.WriteSingleLittleEndian(destination, floatValue);
                return true;
            }

            if (targetType == ExrPixelType.Half)
            {
                ushort half = HalfHelper.SingleToHalf(floatValue);
                BinaryPrimitives.WriteUInt16LittleEndian(destination, half);
                return true;
            }

            if (targetType == ExrPixelType.UInt)
            {
                if (float.IsNaN(floatValue) || float.IsInfinity(floatValue) || floatValue < 0.0f || floatValue > uint.MaxValue)
                {
                    return false;
                }

                BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)floatValue);
                return true;
            }

            return false;
        }

        private static void WriteVersion(Stream stream)
        {
            Span<byte> version = stackalloc byte[ExrVersionHeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(version, Magic);
            version[4] = 2;
            version[5] = 0;
            version[6] = 0;
            version[7] = 0;
            stream.Write(version);
        }

        private static void WriteHeader(Stream stream, ExrHeader header)
        {
            if (!string.IsNullOrWhiteSpace(header.Name))
            {
                WriteAttribute(stream, "name", "string", System.Text.Encoding.UTF8.GetBytes(header.Name + "\0"));
            }

            WriteAttribute(stream, "channels", "chlist", EncodeChannels(header.Channels));
            WriteAttribute(stream, "compression", "compression", new[] { (byte)header.Compression });
            WriteAttribute(stream, "dataWindow", "box2i", EncodeBox(header.DataWindow));
            WriteAttribute(stream, "displayWindow", "box2i", EncodeBox(header.DisplayWindow));
            WriteAttribute(stream, "lineOrder", "lineOrder", new[] { (byte)header.LineOrder });
            WriteAttribute(stream, "pixelAspectRatio", "float", EncodeSingle(header.PixelAspectRatio));
            WriteAttribute(stream, "screenWindowCenter", "v2f", EncodeVector2(header.ScreenWindowCenter));
            WriteAttribute(stream, "screenWindowWidth", "float", EncodeSingle(header.ScreenWindowWidth));

            foreach (ExrAttribute attribute in header.CustomAttributes)
            {
                WriteAttribute(stream, attribute.Name, attribute.TypeName, attribute.Value);
            }

            stream.WriteByte(0);
        }

        private static byte[] EncodeChannels(IList<ExrChannel> channels)
        {
            using MemoryStream stream = new MemoryStream();
            byte[] headerBuffer = new byte[16];
            foreach (ExrChannel channel in channels)
            {
                byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(channel.Name);
                stream.Write(nameBytes, 0, nameBytes.Length);
                stream.WriteByte(0);

                BinaryPrimitives.WriteInt32LittleEndian(headerBuffer, (int)channel.Type);
                headerBuffer[4] = channel.Linear;
                headerBuffer[5] = 0;
                headerBuffer[6] = 0;
                headerBuffer[7] = 0;
                BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(8), channel.SamplingX);
                BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(12), channel.SamplingY);
                stream.Write(headerBuffer, 0, headerBuffer.Length);
            }

            stream.WriteByte(0);
            return stream.ToArray();
        }

        private static void WriteAttribute(Stream stream, string name, string typeName, byte[] value)
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
            byte[] typeBytes = System.Text.Encoding.UTF8.GetBytes(typeName);
            stream.Write(nameBytes, 0, nameBytes.Length);
            stream.WriteByte(0);
            stream.Write(typeBytes, 0, typeBytes.Length);
            stream.WriteByte(0);
            Span<byte> sizeBytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(sizeBytes, value.Length);
            stream.Write(sizeBytes);
            stream.Write(value, 0, value.Length);
        }

        private static byte[] EncodeBox(ExrBox2i box)
        {
            byte[] data = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(data, box.MinX);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), box.MinY);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), box.MaxX);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), box.MaxY);
            return data;
        }

        private static byte[] EncodeSingle(float value)
        {
            byte[] data = new byte[4];
            Exr.WriteSingleLittleEndian(data, value);
            return data;
        }

        private static byte[] EncodeVector2(Vector2 value)
        {
            byte[] data = new byte[8];
            Exr.WriteSingleLittleEndian(data, value.X);
            Exr.WriteSingleLittleEndian(data.AsSpan(4), value.Y);
            return data;
        }

        private static int LevelSize(int topLevelSize, int level, ExrTileRoundingMode roundingMode)
        {
            int divisor = 1 << level;
            int size = topLevelSize / divisor;
            if (roundingMode == ExrTileRoundingMode.RoundUp && size * divisor < topLevelSize)
            {
                size++;
            }

            return Math.Max(size, 1);
        }

        private static void CalculateTileInfo(ExrHeader header, int width, int height, out int[] numXTiles, out int[] numYTiles, out int numXLevels, out int numYLevels, out int totalBlocks)
        {
            if (header.Tiles == null)
            {
                throw new InvalidOperationException();
            }

            switch (header.Tiles.LevelMode)
            {
                case ExrTileLevelMode.OneLevel:
                    numXLevels = 1;
                    numYLevels = 1;
                    break;
                case ExrTileLevelMode.MipMapLevels:
                    numXLevels = RoundLog2(Math.Max(width, height), header.Tiles.RoundingMode) + 1;
                    numYLevels = numXLevels;
                    break;
                default:
                    numXLevels = RoundLog2(width, header.Tiles.RoundingMode) + 1;
                    numYLevels = RoundLog2(height, header.Tiles.RoundingMode) + 1;
                    break;
            }

            numXTiles = new int[numXLevels];
            numYTiles = new int[numYLevels];
            totalBlocks = 0;
            for (int i = 0; i < numXLevels; i++)
            {
                numXTiles[i] = (LevelSize(width, i, header.Tiles.RoundingMode) + header.Tiles.TileSizeX - 1) / header.Tiles.TileSizeX;
            }

            for (int i = 0; i < numYLevels; i++)
            {
                numYTiles[i] = (LevelSize(height, i, header.Tiles.RoundingMode) + header.Tiles.TileSizeY - 1) / header.Tiles.TileSizeY;
            }

            if (header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels)
            {
                for (int ly = 0; ly < numYLevels; ly++)
                {
                    for (int lx = 0; lx < numXLevels; lx++)
                    {
                        totalBlocks += numYTiles[ly] * numXTiles[lx];
                    }
                }
            }
            else
            {
                for (int level = 0; level < numXLevels; level++)
                {
                    totalBlocks += numYTiles[level] * numXTiles[level];
                }
            }
        }

        private static int RoundLog2(int x, ExrTileRoundingMode roundingMode)
        {
            int result = 0;
            int value = x;
            while (value > 1)
            {
                value >>= 1;
                result++;
            }

            if (roundingMode == ExrTileRoundingMode.RoundUp && (1 << result) < x)
            {
                result++;
            }

            return result;
        }

#if NET10_0_OR_GREATER
        private static ResultCode TryDecompressZip(ReadOnlySpan<byte> compressed, int expectedSize, out byte[] raw)
        {
            if (expectedSize == compressed.Length)
            {
                raw = compressed.ToArray();
                return ResultCode.Success;
            }

            byte[] tmp;
            try
            {
                using MemoryStream input = new MemoryStream(compressed.ToArray(), writable: false);
                using ZLibStream zlib = new ZLibStream(input, CompressionMode.Decompress);
                using MemoryStream output = new MemoryStream();
                zlib.CopyTo(output);
                tmp = output.ToArray();
            }
            catch
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            if (tmp.Length != expectedSize)
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            for (int i = 1; i < tmp.Length; i++)
            {
                tmp[i] = unchecked((byte)(tmp[i - 1] + tmp[i] - 128));
            }

            raw = new byte[expectedSize];
            int half = (expectedSize + 1) / 2;
            int sourceA = 0;
            int sourceB = half;
            for (int i = 0; i < expectedSize; i += 2)
            {
                raw[i] = tmp[sourceA++];
                if (i + 1 < expectedSize)
                {
                    raw[i + 1] = tmp[sourceB++];
                }
            }

            return ResultCode.Success;
        }

        private static ResultCode TryCompressZip(byte[] raw, out byte[] payload)
        {
            byte[] tmp = new byte[raw.Length];
            int half = (raw.Length + 1) / 2;
            int targetA = 0;
            int targetB = half;
            for (int i = 0; i < raw.Length; i += 2)
            {
                tmp[targetA++] = raw[i];
                if (i + 1 < raw.Length)
                {
                    tmp[targetB++] = raw[i + 1];
                }
            }

            int previous = tmp.Length == 0 ? 0 : tmp[0];
            for (int i = 1; i < tmp.Length; i++)
            {
                int current = tmp[i];
                tmp[i] = unchecked((byte)(current - previous + 384));
                previous = current;
            }

            try
            {
                using MemoryStream output = new MemoryStream();
                using (ZLibStream zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    zlib.Write(tmp, 0, tmp.Length);
                }

                payload = output.ToArray();
                if (payload.Length >= raw.Length)
                {
                    payload = raw.ToArray();
                }

                return ResultCode.Success;
            }
            catch
            {
                payload = Array.Empty<byte>();
                return ResultCode.SerialzationFailed;
            }
        }
#else
        private static ResultCode TryDecompressZip(ReadOnlySpan<byte> compressed, int expectedSize, out byte[] raw)
        {
            raw = Array.Empty<byte>();
            return ResultCode.UnsupportedFeature;
        }

        private static ResultCode TryCompressZip(byte[] raw, out byte[] payload)
        {
            payload = Array.Empty<byte>();
            return ResultCode.UnsupportedFeature;
        }
#endif
    }

    internal static class HalfHelper
    {
        internal static float HalfToSingle(ushort value)
        {
            uint sign = (uint)(value >> 15) & 0x1;
            uint exp = (uint)(value >> 10) & 0x1f;
            uint mantissa = (uint)value & 0x3ff;

            if (exp == 0)
            {
                if (mantissa == 0)
                {
                    return BitConverter.Int32BitsToSingle((int)(sign << 31));
                }

                while ((mantissa & 0x400) == 0)
                {
                    mantissa <<= 1;
                    exp--;
                }

                exp++;
                mantissa &= ~0x400u;
            }
            else if (exp == 31)
            {
                uint bits = (sign << 31) | 0x7f800000u | (mantissa << 13);
                return BitConverter.Int32BitsToSingle((int)bits);
            }

            exp = exp + (127 - 15);
            uint result = (sign << 31) | (exp << 23) | (mantissa << 13);
            return BitConverter.Int32BitsToSingle((int)result);
        }

        internal static ushort SingleToHalf(float value)
        {
            uint bits = (uint)BitConverter.SingleToInt32Bits(value);
            uint sign = (bits >> 16) & 0x8000u;
            uint mantissa = bits & 0x7fffffu;
            int exponent = (int)((bits >> 23) & 0xffu) - 127 + 15;

            if (exponent <= 0)
            {
                if (exponent < -10)
                {
                    return (ushort)sign;
                }

                mantissa = (mantissa | 0x800000u) >> (1 - exponent);
                if ((mantissa & 0x1000u) != 0)
                {
                    mantissa += 0x2000u;
                }

                return (ushort)(sign | (mantissa >> 13));
            }

            if (exponent >= 31)
            {
                return (ushort)(sign | 0x7c00u);
            }

            if ((mantissa & 0x1000u) != 0)
            {
                mantissa += 0x2000u;
                if ((mantissa & 0x800000u) != 0)
                {
                    mantissa = 0;
                    exponent++;
                    if (exponent >= 31)
                    {
                        return (ushort)(sign | 0x7c00u);
                    }
                }
            }

            return (ushort)(sign | ((uint)exponent << 10) | (mantissa >> 13));
        }
    }
}
