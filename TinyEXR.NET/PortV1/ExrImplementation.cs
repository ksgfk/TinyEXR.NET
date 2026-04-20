using System.Buffers.Binary;
using System.Numerics;
using System.IO.Compression;

namespace TinyEXR.PortV1
{
    internal static class ExrImplementation
    {
        private const int ExrVersionHeaderSize = 8;
        // OpenEXR uses the low 8 bits of the version field as the file format version
        // number. The current format version is fixed at 2; tiled/deep/multipart capability
        // is described by the flag bits that follow. Accepting other values here would
        // silently treat non-standard or future files as today's v2 layout.
        private const int SupportedExrVersion = 2;
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

        private sealed class PreparedImagePart
        {
            public ExrHeader Header { get; set; } = new ExrHeader();

            public List<byte[]> Chunks { get; } = new List<byte[]>();
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
            if (exrVersion != SupportedExrVersion)
            {
                return ResultCode.InvalidExrVersion;
            }

            version.Version = SupportedExrVersion;
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
                ResultCode result = TryParseDeepHeader(data, out parsed);
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

            if (!SupportsDeepCompression(header.Compression))
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
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float value = ReadChannelSampleAsFloat(channel, width, x, y);
                        int rgbaOffset = (y * width + x) * 4;
                        rgba[rgbaOffset + 0] = value;
                        rgba[rgbaOffset + 1] = value;
                        rgba[rgbaOffset + 2] = value;
                        rgba[rgbaOffset + 3] = value;
                    }
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
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int rgbaOffset = (y * width + x) * 4;
                    rgba[rgbaOffset + 0] = ReadChannelSampleAsFloat(rChannel, width, x, y);
                    rgba[rgbaOffset + 1] = ReadChannelSampleAsFloat(gChannel, width, x, y);
                    rgba[rgbaOffset + 2] = ReadChannelSampleAsFloat(bChannel, width, x, y);
                    rgba[rgbaOffset + 3] = aChannel == null ? 1.0f : ReadChannelSampleAsFloat(aChannel, width, x, y);
                }
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

            ResultCode prepareResult = TryPrepareImagePart(image, header, multipartPart: false, out PreparedImagePart? preparedPart);
            if (prepareResult != ResultCode.Success)
            {
                return prepareResult;
            }

            return TryAssembleSinglePart(preparedPart!, out encoded);
        }

        internal static ResultCode TryWriteMultipartImages(IReadOnlyList<ExrImage> images, IReadOnlyList<ExrHeader> headers, out byte[] encoded)
        {
            encoded = Array.Empty<byte>();
            if (images == null || headers == null)
            {
                return ResultCode.InvalidArgument;
            }

            if (images.Count != headers.Count)
            {
                return ResultCode.InvalidArgument;
            }

            List<PreparedImagePart> parts = new List<PreparedImagePart>(images.Count);
            HashSet<string> partNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < images.Count; i++)
            {
                ResultCode prepareResult = TryPrepareImagePart(images[i], headers[i], multipartPart: true, out PreparedImagePart? preparedPart);
                if (prepareResult != ResultCode.Success)
                {
                    return prepareResult;
                }

                string? partName = preparedPart!.Header.Name;
                if (string.IsNullOrWhiteSpace(partName) || !partNames.Add(partName))
                {
                    encoded = Array.Empty<byte>();
                    return ResultCode.InvalidArgument;
                }

                parts.Add(preparedPart);
            }

            return TryAssembleMultipart(parts, out encoded);
        }

        private static ResultCode TryPrepareImagePart(ExrImage image, ExrHeader? header, bool multipartPart, out PreparedImagePart? preparedPart)
        {
            preparedPart = null;
            ResultCode headerResult = TryCreateWriteHeader(image, header, multipartPart, out ExrHeader effectiveHeader);
            if (headerResult != ResultCode.Success)
            {
                return headerResult;
            }

            if (effectiveHeader.IsDeep)
            {
                return ResultCode.UnsupportedFeature;
            }

            if (!SupportsCompression(effectiveHeader.Compression))
            {
                return ResultCode.UnsupportedFeature;
            }

            preparedPart = new PreparedImagePart
            {
                Header = effectiveHeader,
            };

            if (effectiveHeader.Tiles == null)
            {
                ResultCode scanlineResult = TryBuildScanlineChunks(image, effectiveHeader, preparedPart.Chunks);
                if (scanlineResult != ResultCode.Success)
                {
                    preparedPart = null;
                    return scanlineResult;
                }
            }
            else
            {
                ResultCode tiledResult = TryBuildTiledChunks(image, effectiveHeader, preparedPart.Chunks);
                if (tiledResult != ResultCode.Success)
                {
                    preparedPart = null;
                    return tiledResult;
                }
            }

            if (multipartPart)
            {
                preparedPart.Header.ChunkCount = preparedPart.Chunks.Count;
            }

            return ResultCode.Success;
        }

        private static ResultCode TryAssembleSinglePart(PreparedImagePart part, out byte[] encoded)
        {
            encoded = Array.Empty<byte>();

            using MemoryStream headerStream = new MemoryStream();
            WriteVersion(headerStream, tiled: part.Header.Tiles != null, multipart: false, nonImage: false, longName: part.Header.HasLongNames);
            WriteHeader(headerStream, part.Header);

            long chunkOffset = headerStream.Length + checked(part.Chunks.Count * sizeof(long));
            long[] offsets = new long[part.Chunks.Count];
            for (int i = 0; i < part.Chunks.Count; i++)
            {
                offsets[i] = chunkOffset;
                chunkOffset += part.Chunks[i].Length;
            }

            using MemoryStream output = new MemoryStream();
            output.Write(headerStream.ToArray(), 0, checked((int)headerStream.Length));
            WriteOffsetTable(output, offsets);
            WriteChunks(output, part.Chunks);
            encoded = output.ToArray();
            return ResultCode.Success;
        }

        private static ResultCode TryAssembleMultipart(IReadOnlyList<PreparedImagePart> parts, out byte[] encoded)
        {
            encoded = Array.Empty<byte>();
            if (parts.Count == 0)
            {
                return ResultCode.InvalidArgument;
            }

            bool anyTiled = false;
            bool anyLongNames = false;
            foreach (PreparedImagePart part in parts)
            {
                anyTiled |= part.Header.Tiles != null;
                anyLongNames |= part.Header.HasLongNames;
            }

            using MemoryStream headerStream = new MemoryStream();
            WriteVersion(headerStream, tiled: anyTiled, multipart: true, nonImage: false, longName: anyLongNames);
            foreach (PreparedImagePart part in parts)
            {
                WriteHeader(headerStream, part.Header);
            }

            headerStream.WriteByte(0);

            long totalOffsetTableSize = 0;
            foreach (PreparedImagePart part in parts)
            {
                totalOffsetTableSize += checked(part.Chunks.Count * sizeof(long));
            }

            List<List<byte[]>> multipartChunks = new List<List<byte[]>>(parts.Count);
            List<long[]> offsetTables = new List<long[]>(parts.Count);
            long chunkOffset = headerStream.Length + totalOffsetTableSize;
            for (int partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                PreparedImagePart part = parts[partIndex];
                List<byte[]> encodedChunks = new List<byte[]>(part.Chunks.Count);
                long[] offsets = new long[part.Chunks.Count];
                for (int i = 0; i < part.Chunks.Count; i++)
                {
                    byte[] multipartChunk = WrapMultipartChunk(partIndex, part.Chunks[i]);
                    encodedChunks.Add(multipartChunk);
                    offsets[i] = chunkOffset;
                    chunkOffset += multipartChunk.Length;
                }

                multipartChunks.Add(encodedChunks);
                offsetTables.Add(offsets);
            }

            using MemoryStream output = new MemoryStream();
            output.Write(headerStream.ToArray(), 0, checked((int)headerStream.Length));
            foreach (long[] offsetTable in offsetTables)
            {
                WriteOffsetTable(output, offsetTable);
            }

            foreach (List<byte[]> encodedChunks in multipartChunks)
            {
                WriteChunks(output, encodedChunks);
            }

            encoded = output.ToArray();
            return ResultCode.Success;
        }

        private static ResultCode TryBuildScanlineChunks(ExrImage image, ExrHeader header, IList<byte[]> chunks)
        {
            int linesPerChunk = NumScanlines(header.Compression);
            int blockCount = (image.Height + linesPerChunk - 1) / linesPerChunk;
            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                int startY = blockIndex * linesPerChunk;
                int numLines = Math.Min(linesPerChunk, image.Height - startY);
                ResultCode chunkResult = TryEncodeScanlineChunk(image.Levels[0], header, startY, numLines, out byte[] chunk);
                if (chunkResult != ResultCode.Success)
                {
                    return chunkResult;
                }

                chunks.Add(chunk);
            }

            return ResultCode.Success;
        }

        private static ResultCode TryBuildTiledChunks(ExrImage image, ExrHeader header, IList<byte[]> chunks)
        {
            if (header.Tiles == null)
            {
                return ResultCode.InvalidArgument;
            }

            CalculateTileInfo(header, image.Width, image.Height, out int[] numXTiles, out int[] numYTiles, out int numXLevels, out int numYLevels, out int totalBlocks);
            if (totalBlocks <= 0)
            {
                return ResultCode.InvalidArgument;
            }

            int levelCount = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? numXLevels * numYLevels : numXLevels;
            if (image.Levels.Count != levelCount)
            {
                return ResultCode.InvalidArgument;
            }

            for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
            {
                ExrImageLevel level = image.Levels[levelIndex];
                int levelX = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex % numXLevels : levelIndex;
                int levelY = header.Tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex / numXLevels : levelIndex;
                int tileRows = numYTiles[levelY];
                int tileColumns = numXTiles[levelX];

                for (int tileY = 0; tileY < tileRows; tileY++)
                {
                    for (int tileX = 0; tileX < tileColumns; tileX++)
                    {
                        ResultCode chunkResult = TryEncodeTileChunk(level, header, tileX, tileY, levelX, levelY, out byte[] chunk);
                        if (chunkResult != ResultCode.Success)
                        {
                            return chunkResult;
                        }

                        chunks.Add(chunk);
                    }
                }
            }

            return ResultCode.Success;
        }

        private static ResultCode TryEncodeScanlineChunk(ExrImageLevel level, ExrHeader header, int startY, int numLines, out byte[] chunk)
        {
            chunk = Array.Empty<byte>();
            ResultCode rawResult = TryEncodePixelBlock(level.Channels, header, level.Width, level.Height, 0, startY, level.Width, numLines, out byte[] raw);
            if (rawResult != ResultCode.Success)
            {
                return rawResult;
            }

            ResultCode payloadResult = ExrCompressionCodec.TryEncodePayload(header.Compression, header.Channels, 0, startY, level.Width, numLines, raw, out byte[] payload);
            if (payloadResult != ResultCode.Success)
            {
                return payloadResult;
            }

            chunk = new byte[sizeof(int) * 2 + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(chunk, startY + header.DataWindow.MinY);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(sizeof(int)), payload.Length);
            payload.CopyTo(chunk, sizeof(int) * 2);
            return ResultCode.Success;
        }

        private static ResultCode TryEncodeTileChunk(ExrImageLevel level, ExrHeader header, int tileX, int tileY, int levelX, int levelY, out byte[] chunk)
        {
            chunk = Array.Empty<byte>();
            if (header.Tiles == null)
            {
                return ResultCode.InvalidArgument;
            }

            int tilePixelX = tileX * header.Tiles.TileSizeX;
            int tilePixelY = tileY * header.Tiles.TileSizeY;
            int tileWidth = Math.Min(header.Tiles.TileSizeX, level.Width - tilePixelX);
            int tileHeight = Math.Min(header.Tiles.TileSizeY, level.Height - tilePixelY);
            if (tileWidth <= 0 || tileHeight <= 0)
            {
                return ResultCode.InvalidArgument;
            }

            ResultCode rawResult = TryEncodePixelBlock(level.Channels, header, level.Width, level.Height, tilePixelX, tilePixelY, tileWidth, tileHeight, out byte[] raw);
            if (rawResult != ResultCode.Success)
            {
                return rawResult;
            }

            ResultCode payloadResult = ExrCompressionCodec.TryEncodePayload(header.Compression, header.Channels, tilePixelX, tilePixelY, tileWidth, tileHeight, raw, out byte[] payload);
            if (payloadResult != ResultCode.Success)
            {
                return payloadResult;
            }

            chunk = new byte[sizeof(int) * 5 + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(chunk, tileX);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), tileY);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(8), levelX);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(12), levelY);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(16), payload.Length);
            payload.CopyTo(chunk, 20);
            return ResultCode.Success;
        }

        private static ResultCode TryEncodePixelBlock(
            IList<ExrImageChannel> channels,
            ExrHeader header,
            int sourceWidth,
            int sourceHeight,
            int startX,
            int startY,
            int blockWidth,
            int blockHeight,
            out byte[] raw)
        {
            raw = Array.Empty<byte>();
            if (channels.Count != header.Channels.Count)
            {
                return ResultCode.InvalidArgument;
            }

            if (startX < 0 || startY < 0 || blockWidth <= 0 || blockHeight <= 0 ||
                startX + blockWidth > sourceWidth || startY + blockHeight > sourceHeight)
            {
                return ResultCode.InvalidArgument;
            }

            if (!TryCalculateDecodedBlockSize(header.Channels, startX, startY, blockWidth, blockHeight, out int rawSize))
            {
                return ResultCode.InvalidArgument;
            }

            raw = new byte[rawSize];
            int rawOffset = 0;

            for (int y = 0; y < blockHeight; y++)
            {
                int sourceY = startY + y;
                for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
                {
                    ExrImageChannel channel = channels[channelIndex];
                    ExrChannel headerChannel = header.Channels[channelIndex];
                    int targetSampleSize = Exr.TypeSize(headerChannel.Type);
                    if (targetSampleSize <= 0)
                    {
                        raw = Array.Empty<byte>();
                        return ResultCode.UnsupportedFeature;
                    }

                    if (!IsSampledCoordinate(sourceY, headerChannel.SamplingY))
                    {
                        continue;
                    }

                    if (!TryGetSampleIndex(0, sourceY, headerChannel.SamplingY, out int sourceSampleY))
                    {
                        raw = Array.Empty<byte>();
                        return ResultCode.InvalidArgument;
                    }

                    int sourceSampleWidth = GetChannelSampleWidth(sourceWidth, headerChannel);
                    int startSampleX = CountSamplePositions(0, startX, headerChannel.SamplingX);
                    int rowSampleCount = GetChannelSampleWidth(blockWidth, headerChannel, startX);
                    int rowBytes = checked(rowSampleCount * targetSampleSize);
                    ResultCode copyResult = TryCopySamplesToByteOffset(
                        channel.Data,
                        checked((sourceSampleY * sourceSampleWidth + startSampleX) * Exr.TypeSize(channel.DataType)),
                        channel.DataType,
                        raw,
                        rawOffset,
                        headerChannel.Type,
                        rowSampleCount);
                    if (copyResult != ResultCode.Success)
                    {
                        raw = Array.Empty<byte>();
                        return copyResult;
                    }

                    rawOffset += rowBytes;
                }
            }

            if (rawOffset != raw.Length)
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidArgument;
            }

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

        private static ResultCode TryCreateWriteHeader(ExrImage image, ExrHeader? header, bool multipartPart, out ExrHeader effective)
        {
            effective = header?.CloneShallow() ?? new ExrHeader();

            if (image.Width <= 0 || image.Height <= 0 || image.Levels.Count == 0)
            {
                return ResultCode.InvalidArgument;
            }

            ExrImageLevel baseLevel = image.Levels[0];
            if (baseLevel.LevelX != 0 || baseLevel.LevelY != 0 || baseLevel.Width != image.Width || baseLevel.Height != image.Height)
            {
                return ResultCode.InvalidArgument;
            }

            if (effective.Tiles == null)
            {
                if (image.Levels.Count != 1)
                {
                    return ResultCode.InvalidArgument;
                }
            }
            else
            {
                ResultCode tiledValidationResult = ValidateTiledImageLevels(image, effective.Tiles);
                if (tiledValidationResult != ResultCode.Success)
                {
                    return tiledValidationResult;
                }
            }

            if (ShouldDefaultToImageWindow(effective.DataWindow, image.Width, image.Height))
            {
                effective.DataWindow = new ExrBox2i(0, 0, image.Width - 1, image.Height - 1);
            }
            else if (effective.DataWindow.Width != image.Width || effective.DataWindow.Height != image.Height)
            {
                return ResultCode.InvalidArgument;
            }

            if (ShouldDefaultToImageWindow(effective.DisplayWindow, image.Width, image.Height))
            {
                effective.DisplayWindow = effective.DataWindow;
            }

            // tinyexr v1 save path always emits scanline chunks in increasing order.
            // Align with that behavior instead of rejecting non-increasing input headers.
            if (effective.Tiles == null)
            {
                effective.LineOrder = LineOrderType.IncreasingY;
            }

            effective.IsMultipart = multipartPart;
            if (multipartPart)
            {
                effective.PartType = effective.Tiles == null ? "scanlineimage" : "tiledimage";
            }

            effective.Channels.Clear();
            foreach (ExrImageChannel channel in baseLevel.Channels)
            {
                effective.Channels.Add(new ExrChannel(channel.Channel.Name, channel.Channel.Type, channel.Channel.RequestedPixelType, channel.Channel.SamplingX, channel.Channel.SamplingY, channel.Channel.Linear));
            }

            ResultCode levelValidationResult = ValidateLevelChannels(image.Levels, effective.Channels);
            if (levelValidationResult != ResultCode.Success)
            {
                return levelValidationResult;
            }

            return ResultCode.Success;
        }

        private static bool ShouldDefaultToImageWindow(ExrBox2i window, int imageWidth, int imageHeight)
        {
            if (window.Width <= 0 || window.Height <= 0)
            {
                return true;
            }

            return (imageWidth != 1 || imageHeight != 1) &&
                window.MinX == 0 &&
                window.MinY == 0 &&
                window.MaxX == 0 &&
                window.MaxY == 0;
        }

        private static ResultCode ValidateTiledImageLevels(ExrImage image, ExrTileDescription tiles)
        {
            ExrHeader header = new ExrHeader
            {
                Tiles = new ExrTileDescription
                {
                    TileSizeX = tiles.TileSizeX,
                    TileSizeY = tiles.TileSizeY,
                    LevelMode = tiles.LevelMode,
                    RoundingMode = tiles.RoundingMode,
                },
            };

            if (tiles.TileSizeX <= 0 || tiles.TileSizeY <= 0)
            {
                return ResultCode.InvalidArgument;
            }

            CalculateTileInfo(header, image.Width, image.Height, out int[] numXTiles, out int[] numYTiles, out int numXLevels, out int numYLevels, out _);
            int expectedLevelCount = tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? numXLevels * numYLevels : numXLevels;
            if (image.Levels.Count != expectedLevelCount)
            {
                return ResultCode.InvalidArgument;
            }

            for (int levelIndex = 0; levelIndex < expectedLevelCount; levelIndex++)
            {
                ExrImageLevel level = image.Levels[levelIndex];
                int expectedLevelX = tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex % numXLevels : levelIndex;
                int expectedLevelY = tiles.LevelMode == ExrTileLevelMode.RipMapLevels ? levelIndex / numXLevels : levelIndex;
                int expectedWidth = LevelSize(image.Width, expectedLevelX, tiles.RoundingMode);
                int expectedHeight = LevelSize(image.Height, expectedLevelY, tiles.RoundingMode);
                if (level.LevelX != expectedLevelX || level.LevelY != expectedLevelY || level.Width != expectedWidth || level.Height != expectedHeight)
                {
                    return ResultCode.InvalidArgument;
                }

                if (level.Tiles.Count > 0)
                {
                    int expectedTileCount = numXTiles[expectedLevelX] * numYTiles[expectedLevelY];
                    if (level.Tiles.Count != expectedTileCount)
                    {
                        return ResultCode.InvalidArgument;
                    }
                }
            }

            return ResultCode.Success;
        }

        private static ResultCode ValidateLevelChannels(IList<ExrImageLevel> levels, IList<ExrChannel> headerChannels)
        {
            for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
            {
                ExrImageLevel level = levels[levelIndex];
                if (level.Channels.Count != headerChannels.Count)
                {
                    return ResultCode.InvalidArgument;
                }

                for (int channelIndex = 0; channelIndex < level.Channels.Count; channelIndex++)
                {
                    ExrImageChannel imageChannel = level.Channels[channelIndex];
                    ExrChannel headerChannel = headerChannels[channelIndex];
                    if (!string.Equals(imageChannel.Channel.Name, headerChannel.Name, StringComparison.Ordinal) ||
                        imageChannel.Channel.Type != headerChannel.Type ||
                        imageChannel.Channel.SamplingX != headerChannel.SamplingX ||
                        imageChannel.Channel.SamplingY != headerChannel.SamplingY ||
                        imageChannel.Channel.Linear != headerChannel.Linear)
                    {
                        return ResultCode.InvalidArgument;
                    }

                    int sampleSize = Exr.TypeSize(imageChannel.DataType);
                    if (sampleSize <= 0)
                    {
                        return ResultCode.UnsupportedFeature;
                    }

                    int expectedLength = GetChannelByteLength(level.Width, level.Height, headerChannel, imageChannel.DataType);
                    if (imageChannel.Data.Length != expectedLength)
                    {
                        return ResultCode.InvalidArgument;
                    }
                }
            }

            return ResultCode.Success;
        }

        private static void WriteOffsetTable(Stream stream, IReadOnlyList<long> offsets)
        {
            byte[] buffer = new byte[sizeof(long)];
            for (int i = 0; i < offsets.Count; i++)
            {
                BinaryPrimitives.WriteInt64LittleEndian(buffer, offsets[i]);
                stream.Write(buffer, 0, buffer.Length);
            }
        }

        private static void WriteChunks(Stream stream, IEnumerable<byte[]> chunks)
        {
            foreach (byte[] chunk in chunks)
            {
                stream.Write(chunk, 0, chunk.Length);
            }
        }

        private static byte[] WrapMultipartChunk(int partIndex, byte[] chunk)
        {
            byte[] wrapped = new byte[sizeof(int) + chunk.Length];
            BinaryPrimitives.WriteInt32LittleEndian(wrapped, partIndex);
            Buffer.BlockCopy(chunk, 0, wrapped, sizeof(int), chunk.Length);
            return wrapped;
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
                if (channel.SamplingX <= 0 || channel.SamplingY <= 0)
                {
                    return ResultCode.UnsupportedFeature;
                }
            }

            ResultCode requestedPixelTypeValidation = ValidateRequestedReadPixelTypes(parsed.Header);
            if (requestedPixelTypeValidation != ResultCode.Success)
            {
                return requestedPixelTypeValidation;
            }

            return ResultCode.Success;
        }

        private static ResultCode ValidateRequestedReadPixelTypes(ExrHeader header)
        {
            foreach (ExrChannel channel in header.Channels)
            {
                if (!IsSupportedRequestedReadPixelType(channel.Type, channel.RequestedPixelType))
                {
                    return ResultCode.UnsupportedFeature;
                }
            }

            return ResultCode.Success;
        }

        private static bool IsSupportedRequestedReadPixelType(ExrPixelType storedType, ExrPixelType requestedType)
        {
            // tinyexr v1 only widens HALF to FLOAT on read. UINT and FLOAT
            // channels must be loaded using their stored type.
            return storedType switch
            {
                ExrPixelType.Half => requestedType == ExrPixelType.Half || requestedType == ExrPixelType.Float,
                ExrPixelType.UInt => requestedType == ExrPixelType.UInt,
                ExrPixelType.Float => requestedType == ExrPixelType.Float,
                _ => false,
            };
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

            if (ContainsInvalidOffsets(offsets))
            {
                ResultCode reconstructResult = header.Tiles == null
                    ? TryReconstructLineOffsets(data, parsed, offsets)
                    : TryReconstructTileOffsets(data, parsed, offsets);
                if (reconstructResult != ResultCode.Success)
                {
                    offsets = Array.Empty<long>();
                    return reconstructResult;
                }
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

        private static bool ContainsInvalidOffsets(ReadOnlySpan<long> offsets)
        {
            foreach (long offset in offsets)
            {
                if (offset <= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // Match tinyexr v1's fallback for incomplete line offset tables by rebuilding
        // offsets from the chunk stream that starts immediately after the table.
        private static ResultCode TryReconstructLineOffsets(ReadOnlySpan<byte> data, ParsedHeader parsed, Span<long> offsets)
        {
            int marker = checked(parsed.HeaderEndOffset + offsets.Length * sizeof(long));
            if (marker < 0 || marker > data.Length)
            {
                return ResultCode.InvalidData;
            }

            for (int blockIndex = 0; blockIndex < offsets.Length; blockIndex++)
            {
                if (marker + sizeof(int) * 2 > data.Length)
                {
                    return ResultCode.InvalidData;
                }

                int packedSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(marker + sizeof(int), sizeof(int)));
                if (packedSize < 0 || packedSize >= data.Length)
                {
                    return ResultCode.InvalidData;
                }

                offsets[blockIndex] = marker;
                marker = checked(marker + sizeof(int) * 2 + packedSize);
            }

            return ResultCode.Success;
        }

        // Match tinyexr v1's tile offset reconstruction by walking the serialized
        // tile chunks and placing their actual file offsets back into the flat table.
        private static ResultCode TryReconstructTileOffsets(ReadOnlySpan<byte> data, ParsedHeader parsed, Span<long> offsets)
        {
            ExrHeader header = parsed.Header;
            if (header.Tiles == null)
            {
                return ResultCode.InvalidData;
            }

            CalculateTileInfo(header, header.DataWindow.Width, header.DataWindow.Height, out int[] numXTiles, out int[] numYTiles, out int numXLevels, out int numYLevels, out int totalBlocks);
            if (offsets.Length != totalBlocks)
            {
                return ResultCode.InvalidData;
            }

            int marker = checked(parsed.HeaderEndOffset + offsets.Length * sizeof(long));
            if (marker < 0 || marker > data.Length)
            {
                return ResultCode.InvalidData;
            }

            for (int blockIndex = 0; blockIndex < offsets.Length; blockIndex++)
            {
                int tileOffset = marker;
                if (marker + sizeof(int) * 5 > data.Length)
                {
                    return ResultCode.InvalidData;
                }

                int tileX = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(marker, sizeof(int)));
                int tileY = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(marker + 4, sizeof(int)));
                int levelX = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(marker + 8, sizeof(int)));
                int levelY = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(marker + 12, sizeof(int)));
                int packedSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(marker + 16, sizeof(int)));
                if (packedSize < 0)
                {
                    return ResultCode.InvalidData;
                }

                marker = checked(marker + sizeof(int) * 5 + packedSize);
                if (marker > data.Length)
                {
                    return ResultCode.InvalidData;
                }

                if (!TryGetTileOffsetIndex(header, numXTiles, numYTiles, numXLevels, numYLevels, tileX, tileY, levelX, levelY, out int offsetIndex))
                {
                    return ResultCode.InvalidData;
                }

                offsets[offsetIndex] = tileOffset;
            }

            return ContainsInvalidOffsets(offsets) ? ResultCode.InvalidData : ResultCode.Success;
        }

        private static bool TryGetTileOffsetIndex(
            ExrHeader header,
            ReadOnlySpan<int> numXTiles,
            ReadOnlySpan<int> numYTiles,
            int numXLevels,
            int numYLevels,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            out int offsetIndex)
        {
            offsetIndex = -1;
            if (header.Tiles == null || tileX < 0 || tileY < 0 || levelX < 0 || levelY < 0)
            {
                return false;
            }

            switch (header.Tiles.LevelMode)
            {
                case ExrTileLevelMode.OneLevel:
                    if (levelX != 0 || levelY != 0 || tileX >= numXTiles[0] || tileY >= numYTiles[0])
                    {
                        return false;
                    }

                    offsetIndex = checked(tileY * numXTiles[0] + tileX);
                    return true;

                case ExrTileLevelMode.MipMapLevels:
                    if (levelX != levelY || levelX >= numXLevels || levelY >= numYLevels || tileX >= numXTiles[levelX] || tileY >= numYTiles[levelY])
                    {
                        return false;
                    }

                    offsetIndex = checked(GetTileLevelOffsetBase(numXTiles, numYTiles, numXLevels, levelX, levelY, ripMap: false) + tileY * numXTiles[levelX] + tileX);
                    return true;

                case ExrTileLevelMode.RipMapLevels:
                    if (levelX >= numXLevels || levelY >= numYLevels || tileX >= numXTiles[levelX] || tileY >= numYTiles[levelY])
                    {
                        return false;
                    }

                    offsetIndex = checked(GetTileLevelOffsetBase(numXTiles, numYTiles, numXLevels, levelX, levelY, ripMap: true) + tileY * numXTiles[levelX] + tileX);
                    return true;

                default:
                    return false;
            }
        }

        private static int GetTileLevelOffsetBase(ReadOnlySpan<int> numXTiles, ReadOnlySpan<int> numYTiles, int numXLevels, int levelX, int levelY, bool ripMap)
        {
            int offsetBase = 0;
            if (ripMap)
            {
                for (int y = 0; y < levelY; y++)
                {
                    for (int x = 0; x < numXLevels; x++)
                    {
                        offsetBase = checked(offsetBase + numXTiles[x] * numYTiles[y]);
                    }
                }

                for (int x = 0; x < levelX; x++)
                {
                    offsetBase = checked(offsetBase + numXTiles[x] * numYTiles[levelY]);
                }
            }
            else
            {
                for (int level = 0; level < levelX; level++)
                {
                    offsetBase = checked(offsetBase + numXTiles[level] * numYTiles[level]);
                }
            }

            return offsetBase;
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
                if (!TryCalculateDecodedBlockSize(header.Channels, 0, relativeLine, width, chunkLineCount, out int expectedRawSize))
                {
                    return ResultCode.InvalidData;
                }

                byte[] raw;
                ResultCode decodeResult = ExrCompressionCodec.TryDecodePayload(
                    header.Compression,
                    header.Channels,
                    0,
                    relativeLine,
                    width,
                    chunkLineCount,
                    data.Slice((int)chunkOffset + sizeof(int) * 2, packedSize),
                    expectedRawSize,
                    out raw);
                if (decodeResult != ResultCode.Success)
                {
                    return decodeResult;
                }

                int rawOffset = 0;
                for (int line = 0; line < chunkLineCount; line++)
                {
                    int sourceY = relativeLine + line;
                    // tinyexr decodes scanline chunks in line offset table order, so the
                    // destination rows are always increasing Y regardless of the header attribute.
                    if (sourceY < 0 || sourceY >= height)
                    {
                        return ResultCode.InvalidData;
                    }

                    for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
                    {
                        ExrChannel headerChannel = header.Channels[channelIndex];
                        if (!IsSampledCoordinate(sourceY, headerChannel.SamplingY))
                        {
                            continue;
                        }

                        if (!TryGetSampleIndex(0, sourceY, headerChannel.SamplingY, out int destinationSampleY))
                        {
                            return ResultCode.InvalidData;
                        }

                        int rowSampleCount = GetChannelSampleWidth(width, headerChannel);
                        int rowBytes = checked(rowSampleCount * Exr.TypeSize(headerChannel.Type));
                        if (rawOffset + rowBytes > raw.Length)
                        {
                            return ResultCode.InvalidData;
                        }

                        int destinationSampleIndex = checked(destinationSampleY * rowSampleCount);
                        ResultCode copyResult = TryCopySamples(
                            raw,
                            rawOffset,
                            headerChannel.Type,
                            channels[channelIndex].Data,
                            destinationSampleIndex,
                            channels[channelIndex].DataType,
                            rowSampleCount);
                        if (copyResult != ResultCode.Success)
                        {
                            return copyResult;
                        }

                        rawOffset += rowBytes;
                    }
                }

                if (rawOffset != raw.Length)
                {
                    return ResultCode.InvalidData;
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
                        if (!TryCalculateDecodedBlockSize(header.Channels, tilePixelX, tilePixelY, tileWidth, tileHeight, out int expectedRawSize))
                        {
                            return ResultCode.InvalidData;
                        }

                        byte[] raw;
                        ResultCode decodeResult = ExrCompressionCodec.TryDecodePayload(
                            header.Compression,
                            header.Channels,
                            tilePixelX,
                            tilePixelY,
                            tileWidth,
                            tileHeight,
                            data.Slice((int)chunkOffset + 20, packedSize),
                            expectedRawSize,
                            out raw);
                        if (decodeResult != ResultCode.Success)
                        {
                            return decodeResult;
                        }

                        if (headerLevelX != levelX || headerLevelY != levelY)
                        {
                            return ResultCode.InvalidData;
                        }

                        ExrImageChannel[] tileChannels = CreateOutputChannels(tileWidth, tileHeight, header.Channels, tilePixelX, tilePixelY).ToArray();

                        int rawOffset = 0;
                        for (int localY = 0; localY < tileHeight; localY++)
                        {
                            int destinationY = tilePixelY + localY;
                            if (destinationY < 0 || destinationY >= levelHeight)
                            {
                                continue;
                            }

                            for (int channelIndex = 0; channelIndex < currentLevelChannels.Count; channelIndex++)
                            {
                                ExrChannel headerChannel = header.Channels[channelIndex];
                                if (!IsSampledCoordinate(destinationY, headerChannel.SamplingY))
                                {
                                    continue;
                                }

                                if (!TryGetSampleIndex(0, destinationY, headerChannel.SamplingY, out int levelSampleY) ||
                                    !TryGetSampleIndex(tilePixelY, destinationY, headerChannel.SamplingY, out int tileSampleY))
                                {
                                    return ResultCode.InvalidData;
                                }

                                int rowSampleCount = GetChannelSampleWidth(tileWidth, headerChannel, tilePixelX);
                                int rowBytes = checked(rowSampleCount * Exr.TypeSize(headerChannel.Type));
                                if (rawOffset + rowBytes > raw.Length)
                                {
                                    return ResultCode.InvalidData;
                                }

                                int levelSampleWidth = GetChannelSampleWidth(levelWidth, headerChannel);
                                int levelSampleX = CountSamplePositions(0, tilePixelX, headerChannel.SamplingX);
                                ResultCode levelCopyResult = TryCopySamples(
                                    raw,
                                    rawOffset,
                                    headerChannel.Type,
                                    currentLevelChannels[channelIndex].Data,
                                    checked(levelSampleY * levelSampleWidth + levelSampleX),
                                    currentLevelChannels[channelIndex].DataType,
                                    rowSampleCount);
                                if (levelCopyResult != ResultCode.Success)
                                {
                                    return levelCopyResult;
                                }

                                int tileSampleWidth = GetChannelSampleWidth(tileWidth, headerChannel, tilePixelX);
                                ResultCode tileCopyResult = TryCopySamples(
                                    raw,
                                    rawOffset,
                                    headerChannel.Type,
                                    tileChannels[channelIndex].Data,
                                    checked(tileSampleY * tileSampleWidth),
                                    tileChannels[channelIndex].DataType,
                                    rowSampleCount);
                                if (tileCopyResult != ResultCode.Success)
                                {
                                    return tileCopyResult;
                                }

                                rawOffset += rowBytes;
                            }
                        }

                        if (rawOffset != raw.Length)
                        {
                            return ResultCode.InvalidData;
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

                ResultCode offsetResult = TryDecodePayload(header.Compression, data.Slice((int)chunkOffset + 28, (int)packedOffsetSize), width * sizeof(int), out byte[] packedOffsets);
                if (offsetResult != ResultCode.Success)
                {
                    return offsetResult;
                }

                ResultCode sampleResult = TryDecodePayload(header.Compression, data.Slice((int)chunkOffset + 28 + (int)packedOffsetSize, (int)packedSampleSize), (int)unpackedSampleSize, out byte[] sampleBytes);
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
            return ExrCompressionCodec.TryDecodeDeepPayload(compression, payload, expectedSize, out raw);
        }

        private static bool SupportsCompression(CompressionType compression)
        {
            switch (compression)
            {
                case CompressionType.None:
                case CompressionType.RLE:
                case CompressionType.PIZ:
                case CompressionType.B44:
                case CompressionType.B44A:
                    return true;
                case CompressionType.ZIPS:
                case CompressionType.ZIP:
                case CompressionType.PXR24:
                    return true;
                default:
                    return false;
            }
        }

        private static bool SupportsDeepCompression(CompressionType compression)
        {
            switch (compression)
            {
                case CompressionType.None:
                case CompressionType.RLE:
                case CompressionType.ZIPS:
                case CompressionType.ZIP:
                    return true;
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

        private static ResultCode TryParseDeepHeader(ReadOnlySpan<byte> data, out ParsedHeader? parsed)
        {
            parsed = null;
            ResultCode versionResult = TryReadVersion(data, out ExrVersion version);
            if (versionResult != ResultCode.Success)
            {
                return versionResult;
            }

            if (version.Multipart || !version.NonImage || version.Tiled)
            {
                return ResultCode.UnsupportedFeature;
            }

            int offset = ExrVersionHeaderSize;
            ExrHeader parsedHeader = new ExrHeader
            {
                HasLongNames = version.LongName,
                IsDeep = true,
                PartType = "deepscanline",
            };

            bool hasCompression = false;
            bool hasDataWindow = false;
            bool hasDisplayWindow = false;
            bool hasLineOrder = false;
            bool hasPixelAspectRatio = false;
            bool hasScreenWindowCenter = false;
            bool hasScreenWindowWidth = false;

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
                    case "type":
                        string partType = DecodeStringAttributeValue(value);
                        if (string.Equals(partType, "deeptile", StringComparison.Ordinal))
                        {
                            return ResultCode.UnsupportedFeature;
                        }

                        if (!string.IsNullOrEmpty(partType))
                        {
                            parsedHeader.PartType = partType;
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

            if (!hasCompression || !hasDataWindow || parsedHeader.Channels.Count == 0)
            {
                return ResultCode.InvalidHeader;
            }

            if (!hasDisplayWindow)
            {
                parsedHeader.DisplayWindow = parsedHeader.DataWindow;
            }

            if (!hasLineOrder)
            {
                parsedHeader.LineOrder = LineOrderType.IncreasingY;
            }

            if (!hasPixelAspectRatio)
            {
                parsedHeader.PixelAspectRatio = 1.0f;
            }

            if (!hasScreenWindowCenter)
            {
                parsedHeader.ScreenWindowCenter = Vector2.Zero;
            }

            if (!hasScreenWindowWidth)
            {
                parsedHeader.ScreenWindowWidth = 1.0f;
            }

            parsedHeader.HeaderLength = offset;
            parsed = new ParsedHeader
            {
                Version = version,
                Header = parsedHeader,
                HeaderEndOffset = offset,
            };
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
            switch (compression)
            {
                case CompressionType.ZIP:
                case CompressionType.PXR24:
                    return 16;
                case CompressionType.PIZ:
                case CompressionType.B44:
                case CompressionType.B44A:
                    return 32;
                default:
                    return 1;
            }
        }

        private static List<ExrImageChannel> CreateOutputChannels(int width, int height, IEnumerable<ExrChannel> sourceChannels, int startX = 0, int startY = 0)
        {
            List<ExrImageChannel> channels = new List<ExrImageChannel>();
            foreach (ExrChannel channel in sourceChannels)
            {
                ExrPixelType outputType = channel.RequestedPixelType;
                channels.Add(new ExrImageChannel(channel, outputType, new byte[GetChannelByteLength(width, height, channel, outputType, startX, startY)]));
            }

            return channels;
        }

        private static int CountSamplePositions(int start, int size, int sampling)
        {
            if (size <= 0)
            {
                return 0;
            }

            int remainder = start % sampling;
            if (remainder < 0)
            {
                remainder += sampling;
            }

            int firstOffset = remainder == 0 ? 0 : sampling - remainder;
            if (firstOffset >= size)
            {
                return 0;
            }

            return ((size - 1 - firstOffset) / sampling) + 1;
        }

        private static bool IsSampledCoordinate(int coordinate, int sampling)
        {
            return coordinate % sampling == 0;
        }

        private static bool TryGetSampleIndex(int start, int coordinate, int sampling, out int sampleIndex)
        {
            sampleIndex = -1;
            if (coordinate < start || !IsSampledCoordinate(coordinate, sampling))
            {
                return false;
            }

            sampleIndex = CountSamplePositions(start, coordinate - start + 1, sampling) - 1;
            return sampleIndex >= 0;
        }

        private static int GetChannelSampleWidth(int width, ExrChannel channel, int startX = 0)
        {
            return CountSamplePositions(startX, width, channel.SamplingX);
        }

        private static int GetChannelSampleHeight(int height, ExrChannel channel, int startY = 0)
        {
            return CountSamplePositions(startY, height, channel.SamplingY);
        }

        private static int GetChannelByteLength(int width, int height, ExrChannel channel, ExrPixelType dataType, int startX = 0, int startY = 0)
        {
            return checked(checked(GetChannelSampleWidth(width, channel, startX) * GetChannelSampleHeight(height, channel, startY)) * Exr.TypeSize(dataType));
        }

        private static bool TryCalculateDecodedBlockSize(IList<ExrChannel> channels, int startX, int startY, int width, int height, out int byteCount)
        {
            byteCount = 0;
            try
            {
                for (int i = 0; i < channels.Count; i++)
                {
                    byteCount = checked(byteCount + GetChannelByteLength(width, height, channels[i], channels[i].Type, startX, startY));
                }

                return true;
            }
            catch (OverflowException)
            {
                byteCount = 0;
                return false;
            }
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

        private static float ReadChannelSampleAsFloat(ExrImageChannel channel, int imageWidth, int x, int y)
        {
            int sampleWidth = GetChannelSampleWidth(imageWidth, channel.Channel);
            int sampleX = CountSamplePositions(0, x + 1, channel.Channel.SamplingX) - 1;
            int sampleY = CountSamplePositions(0, y + 1, channel.Channel.SamplingY) - 1;
            return ReadSampleAsFloat(channel.Data, channel.DataType, checked(sampleY * sampleWidth + sampleX));
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

        private static ResultCode TryCopySamplesToByteOffset(
            byte[] source,
            int sourceOffset,
            ExrPixelType sourceType,
            byte[] destination,
            int destinationOffset,
            ExrPixelType destinationType,
            int sampleCount)
        {
            int sourceSampleSize = Exr.TypeSize(sourceType);
            int destinationSampleSize = Exr.TypeSize(destinationType);
            if (sourceSampleSize <= 0 || destinationSampleSize <= 0)
            {
                return ResultCode.UnsupportedFeature;
            }

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

        private static void WriteVersion(Stream stream, bool tiled, bool multipart, bool nonImage, bool longName)
        {
            Span<byte> version = stackalloc byte[ExrVersionHeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(version, Magic);
            // Emit the spec-defined format version. Feature differences belong in the flag
            // bits below, not in alternative version byte values.
            version[4] = SupportedExrVersion;
            byte flags = 0;
            if (tiled)
            {
                flags |= 0x2;
            }

            if (longName)
            {
                flags |= 0x4;
            }

            if (nonImage)
            {
                flags |= 0x8;
            }

            if (multipart)
            {
                flags |= 0x10;
            }

            version[5] = flags;
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

            if (header.IsMultipart || header.IsDeep)
            {
                string partType = header.PartType ?? (header.Tiles == null ? "scanlineimage" : "tiledimage");
                WriteAttribute(stream, "type", "string", System.Text.Encoding.UTF8.GetBytes(partType + "\0"));
            }

            WriteAttribute(stream, "channels", "chlist", EncodeChannels(header.Channels));
            WriteAttribute(stream, "compression", "compression", new[] { (byte)header.Compression });
            WriteAttribute(stream, "dataWindow", "box2i", EncodeBox(header.DataWindow));
            WriteAttribute(stream, "displayWindow", "box2i", EncodeBox(header.DisplayWindow));
            WriteAttribute(stream, "lineOrder", "lineOrder", new[] { (byte)header.LineOrder });
            WriteAttribute(stream, "pixelAspectRatio", "float", EncodeSingle(header.PixelAspectRatio));
            WriteAttribute(stream, "screenWindowCenter", "v2f", EncodeVector2(header.ScreenWindowCenter));
            WriteAttribute(stream, "screenWindowWidth", "float", EncodeSingle(header.ScreenWindowWidth));
            if (header.Tiles != null)
            {
                WriteAttribute(stream, "tiles", "tiledesc", EncodeTileDescription(header.Tiles));
            }

            if ((header.IsMultipart || header.IsDeep) && header.ChunkCount > 0)
            {
                WriteAttribute(stream, "chunkCount", "int", EncodeInt32(header.ChunkCount));
            }

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

        private static byte[] EncodeTileDescription(ExrTileDescription tiles)
        {
            byte[] data = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(data, tiles.TileSizeX);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), tiles.TileSizeY);
            data[8] = (byte)(((int)tiles.RoundingMode << 4) | ((int)tiles.LevelMode & 0x3));
            return data;
        }

        private static byte[] EncodeInt32(int value)
        {
            byte[] data = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(data, value);
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
