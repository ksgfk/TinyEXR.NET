using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using TinyEXR.PortV1;
using TinyEXR.V3.Format;
using V3 = TinyEXR.V3;
using V3IO = TinyEXR.V3.IO;

namespace TinyEXR
{
    /// <summary>
    /// Incremental migration bridge from the tinyexr v1 facade to the v3 core.
    /// Unsupported v1 facade shapes deliberately fall back to PortV1.
    /// </summary>
    internal static class V1FacadeAdapter
    {
        private static readonly HashSet<string> ReservedWriteAttributes = new HashSet<string>(StringComparer.Ordinal)
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
            "version",
            "maxSamplesPerPixel",
            "chromaticities",
        };

        internal static bool TryParseSingleHeader(
            ReadOnlySpan<byte> data,
            out ResultCode result,
            out ExrVersion version,
            out ExrHeader header)
        {
            result = ResultCode.InvalidData;
            version = new ExrVersion();
            header = new ExrHeader();

            try
            {
                V3.ExrResult parseResult = ExrFormatParser.Parse(data.ToArray(), out ParsedFile? parsed);
                if (parseResult != V3.ExrResult.Success || parsed == null ||
                    parsed.Flags.Multipart || parsed.Parts.Count != 1)
                {
                    return false;
                }

                ParsedPartIndex part = parsed.Parts[0];
                version = ConvertVersion(parsed);
                header = ConvertHeader(parsed, part);
                result = ResultCode.Success;
                return true;
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static bool TryParseSingleHeader(
            Stream stream,
            out ResultCode result,
            out ExrVersion version,
            out ExrHeader header)
        {
            result = ResultCode.InvalidData;
            version = new ExrVersion();
            header = new ExrHeader();

            if (!StreamWindowDataSource.TryCreate(stream, out StreamWindowDataSource? source))
            {
                return false;
            }

            try
            {
                using V3.ExrReader reader = V3.ExrReader.OpenSource(source!);
                V3.ReaderResult parseResult = reader.ParseHeader();
                if (!parseResult.IsSuccess || reader.NumParts != 1)
                {
                    return false;
                }

                V3.Header sourceHeader = reader.GetHeader(0);
                version = ConvertVersion(reader.GetRawVersionField());
                header = ConvertHeader(
                    sourceHeader,
                    reader.HasHeaderAttribute(0, "name"),
                    reader.HasHeaderAttribute(0, "type"),
                    version.Multipart,
                    version.LongName,
                    reader.GetNumBlocks(0),
                    reader.GetHeaderEndOffset(0),
                    reader.GetRawHeaderAttributes(0));
                result = ResultCode.Success;
                return true;
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static bool TryParseMultipartHeaders(
            ReadOnlySpan<byte> data,
            out ResultCode result,
            out ExrVersion version,
            out ExrHeader[] headers)
        {
            result = ResultCode.InvalidData;
            version = new ExrVersion();
            headers = Array.Empty<ExrHeader>();

            try
            {
                V3.ExrResult parseResult = ExrFormatParser.Parse(data.ToArray(), out ParsedFile? parsed);
                if (parseResult != V3.ExrResult.Success || parsed == null ||
                    !parsed.Flags.Multipart || parsed.Parts.Count == 0)
                {
                    return false;
                }

                version = ConvertVersion(parsed);
                headers = new ExrHeader[parsed.Parts.Count];
                for (int partIndex = 0; partIndex < parsed.Parts.Count; partIndex++)
                {
                    headers[partIndex] = ConvertHeader(parsed, parsed.Parts[partIndex]);
                }

                result = ResultCode.Success;
                return true;
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static bool TryParseMultipartHeaders(
            Stream stream,
            out ResultCode result,
            out ExrVersion version,
            out ExrHeader[] headers)
        {
            result = ResultCode.InvalidData;
            version = new ExrVersion();
            headers = Array.Empty<ExrHeader>();
            if (!StreamWindowDataSource.TryCreate(stream, out StreamWindowDataSource? source))
            {
                return false;
            }

            try
            {
                using V3.ExrReader reader = V3.ExrReader.OpenSource(source!);
                V3.ReaderResult parseResult = reader.ParseHeader();
                if (!parseResult.IsSuccess || reader.NumParts == 0)
                {
                    return false;
                }

                version = ConvertVersion(reader.GetRawVersionField());
                if (!version.Multipart)
                {
                    return false;
                }

                headers = new ExrHeader[reader.NumParts];
                for (int partIndex = 0; partIndex < headers.Length; partIndex++)
                {
                    headers[partIndex] = ConvertHeader(
                        reader.GetHeader(partIndex),
                        reader.HasHeaderAttribute(partIndex, "name"),
                        reader.HasHeaderAttribute(partIndex, "type"),
                        multipart: true,
                        version.LongName,
                        reader.GetNumBlocks(partIndex),
                        reader.GetHeaderEndOffset(partIndex),
                        reader.GetRawHeaderAttributes(partIndex));
                }

                result = ResultCode.Success;
                return true;
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static bool TryReadMultipartImages(
            ReadOnlySpan<byte> data,
            IReadOnlyList<ExrHeader> requestedHeaders,
            out ResultCode result,
            out ExrImage[] images)
        {
            result = ResultCode.InvalidData;
            images = Array.Empty<ExrImage>();
            try
            {
                using V3.ExrReader reader = V3.ExrReader.OpenMemory(data.ToArray());
                return TryReadMultipartImages(reader, requestedHeaders, out result, out images);
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static bool TryReadMultipartImages(
            Stream stream,
            IReadOnlyList<ExrHeader> requestedHeaders,
            out ResultCode result,
            out ExrImage[] images)
        {
            result = ResultCode.InvalidData;
            images = Array.Empty<ExrImage>();
            if (!StreamWindowDataSource.TryCreate(stream, out StreamWindowDataSource? source))
            {
                return false;
            }

            try
            {
                using V3.ExrReader reader = V3.ExrReader.OpenSource(source!);
                return TryReadMultipartImages(reader, requestedHeaders, out result, out images);
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryReadMultipartImages(
            V3.ExrReader reader,
            IReadOnlyList<ExrHeader> requestedHeaders,
            out ResultCode result,
            out ExrImage[] images)
        {
            result = ResultCode.InvalidData;
            images = Array.Empty<ExrImage>();
            V3.ReaderResult headerResult = reader.ParseHeader();
            if (!headerResult.IsSuccess || reader.NumParts == 0 ||
                (reader.GetRawVersionField() & (1U << 12)) == 0)
            {
                return false;
            }

            if (requestedHeaders == null || requestedHeaders.Count != reader.NumParts)
            {
                result = ResultCode.InvalidArgument;
                return true;
            }

            for (int partIndex = 0; partIndex < reader.NumParts; partIndex++)
            {
                V3.Header sourceHeader = reader.GetHeader(partIndex);
                if (sourceHeader.IsDeep)
                {
                    result = ResultCode.UnsupportedFeature;
                    return true;
                }

                if (sourceHeader.Compression == V3.Compression.DWAA ||
                    sourceHeader.Compression == V3.Compression.DWAB)
                {
                    result = ResultCode.UnsupportedFeature;
                    return true;
                }

                ResultCode validation = ValidateRequestedChannels(
                    sourceHeader,
                    requestedHeaders[partIndex]);
                if (validation != ResultCode.Success)
                {
                    result = validation;
                    return true;
                }
            }

            ExrImage[] decoded = new ExrImage[reader.NumParts];
            for (int partIndex = 0; partIndex < decoded.Length; partIndex++)
            {
                V3.ReaderResult<V3.Part> readResult = reader.ReadPart(partIndex);
                if (!readResult.IsSuccess || readResult.Value == null)
                {
                    result = MapResult(readResult.Status);
                    return true;
                }

                decoded[partIndex] = ConvertFlatPart(
                    reader.GetHeader(partIndex),
                    requestedHeaders[partIndex],
                    readResult.Value);
            }

            images = decoded;
            result = ResultCode.Success;
            return true;
        }

        internal static bool TryReadLayers(
            ReadOnlySpan<byte> data,
            out ResultCode result,
            out string[] layers)
        {
            layers = Array.Empty<string>();
            if (!TryParseSingleHeader(data, out result, out ExrVersion version, out ExrHeader header))
            {
                return false;
            }

            if (result != ResultCode.Success)
            {
                return true;
            }

            if (!IsFlatImage(version, header))
            {
                return false;
            }

            layers = ExrImplementation.GetLayers(header).ToArray();
            return true;
        }

        internal static bool TryReadLayers(
            Stream stream,
            out ResultCode result,
            out string[] layers)
        {
            layers = Array.Empty<string>();
            if (!TryParseSingleHeader(stream, out result, out ExrVersion version, out ExrHeader header))
            {
                return false;
            }

            if (result != ResultCode.Success)
            {
                return true;
            }

            if (!IsFlatImage(version, header))
            {
                return false;
            }

            layers = ExrImplementation.GetLayers(header).ToArray();
            return true;
        }

        internal static bool TryReadRgba(
            ReadOnlySpan<byte> data,
            string? layerName,
            out ResultCode result,
            out float[] rgba,
            out int width,
            out int height)
        {
            rgba = Array.Empty<float>();
            width = 0;
            height = 0;
            if (!TryParseSingleHeader(data, out result, out ExrVersion version, out ExrHeader header))
            {
                return false;
            }

            if (result != ResultCode.Success)
            {
                return true;
            }

            if (version.Multipart || version.NonImage)
            {
                result = ResultCode.InvalidData;
                return true;
            }

            if (!IsFlatImage(version, header))
            {
                return false;
            }

            RequestFloatForHalfChannels(header);
            if (!TryReadFlatImage(data, header, out result, out ExrImage image))
            {
                return false;
            }

            if (result == ResultCode.Success)
            {
                result = ExrImplementation.TryBuildRgbaFromImage(
                    header,
                    image,
                    layerName,
                    out rgba,
                    out width,
                    out height);
            }

            return true;
        }

        internal static bool TryReadRgba(
            Stream stream,
            string? layerName,
            out ResultCode result,
            out float[] rgba,
            out int width,
            out int height)
        {
            rgba = Array.Empty<float>();
            width = 0;
            height = 0;
            if (!TryParseSingleHeader(stream, out result, out ExrVersion version, out ExrHeader header))
            {
                return false;
            }

            if (result != ResultCode.Success)
            {
                return true;
            }

            if (version.Multipart || version.NonImage)
            {
                result = ResultCode.InvalidData;
                return true;
            }

            if (!IsFlatImage(version, header))
            {
                return false;
            }

            RequestFloatForHalfChannels(header);
            if (!TryReadFlatImage(stream, header, out result, out ExrImage image))
            {
                return false;
            }

            if (result == ResultCode.Success)
            {
                result = ExrImplementation.TryBuildRgbaFromImage(
                    header,
                    image,
                    layerName,
                    out rgba,
                    out width,
                    out height);
            }

            return true;
        }

        internal static bool TryReadFlatImage(
            ReadOnlySpan<byte> data,
            ExrHeader requestedHeader,
            out ResultCode result,
            out ExrImage image)
        {
            result = ResultCode.InvalidData;
            image = EmptyImage();

            try
            {
                using V3.ExrReader reader = V3.ExrReader.OpenMemory(data.ToArray());
                return TryReadFlatImage(reader, requestedHeader, out result, out image);
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
            catch (ArgumentException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
            catch (InvalidOperationException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
        }

        internal static bool TryReadFlatImage(
            ReadOnlySpan<byte> data,
            out ResultCode result,
            out ExrHeader header,
            out ExrImage image)
        {
            image = EmptyImage();
            if (!TryParseSingleHeader(data, out result, out ExrVersion version, out header))
            {
                return false;
            }

            if (result != ResultCode.Success)
            {
                return true;
            }

            if (!IsFlatImage(version, header))
            {
                return false;
            }

            return TryReadFlatImage(data, header, out result, out image);
        }

        internal static bool TryReadFlatImage(
            Stream stream,
            ExrHeader requestedHeader,
            out ResultCode result,
            out ExrImage image)
        {
            result = ResultCode.InvalidData;
            image = EmptyImage();
            if (!StreamWindowDataSource.TryCreate(stream, out StreamWindowDataSource? source))
            {
                return false;
            }

            try
            {
                using V3.ExrReader reader = V3.ExrReader.OpenSource(source!);
                return TryReadFlatImage(reader, requestedHeader, out result, out image);
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static bool TryReadFlatImage(
            Stream stream,
            out ResultCode result,
            out ExrHeader header,
            out ExrImage image)
        {
            image = EmptyImage();
            if (!TryParseSingleHeader(stream, out result, out ExrVersion version, out header))
            {
                return false;
            }

            if (result != ResultCode.Success)
            {
                return true;
            }

            if (!IsFlatImage(version, header))
            {
                return false;
            }

            return TryReadFlatImage(stream, header, out result, out image);
        }

        internal static bool TryReadDeepImage(
            ReadOnlySpan<byte> data,
            out ResultCode result,
            out ExrHeader header,
            out ExrDeepImage image)
        {
            result = ResultCode.InvalidData;
            header = new ExrHeader();
            image = EmptyDeepImage();

            try
            {
                using V3.ExrReader reader = V3.ExrReader.OpenMemory(data.ToArray());
                return TryReadDeepImage(reader, out result, out header, out image);
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
            catch (ArgumentException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
            catch (InvalidOperationException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
            catch (NotSupportedException)
            {
                result = ResultCode.UnsupportedFeature;
                return true;
            }
        }

        internal static bool TryReadDeepImage(
            Stream stream,
            out ResultCode result,
            out ExrHeader header,
            out ExrDeepImage image)
        {
            result = ResultCode.InvalidData;
            header = new ExrHeader();
            image = EmptyDeepImage();
            if (!StreamWindowDataSource.TryCreate(stream, out StreamWindowDataSource? source))
            {
                return false;
            }

            try
            {
                using V3.ExrReader reader = V3.ExrReader.OpenSource(source!);
                return TryReadDeepImage(reader, out result, out header, out image);
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                result = ResultCode.InvalidData;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                result = ResultCode.UnsupportedFeature;
                return true;
            }
        }

        private static bool TryReadDeepImage(
            V3.ExrReader reader,
            out ResultCode result,
            out ExrHeader header,
            out ExrDeepImage image)
        {
            result = ResultCode.InvalidData;
            header = new ExrHeader();
            image = EmptyDeepImage();

            V3.ReaderResult headerResult = reader.ParseHeader();
            if (!headerResult.IsSuccess || reader.NumParts != 1)
            {
                return false;
            }

            ExrVersion version = ConvertVersion(reader.GetRawVersionField());
            V3.Header sourceHeader = reader.GetHeader(0);
            header = ConvertHeader(
                sourceHeader,
                reader.HasHeaderAttribute(0, "name"),
                reader.HasHeaderAttribute(0, "type"),
                version.Multipart,
                version.LongName,
                reader.GetNumBlocks(0),
                reader.GetHeaderEndOffset(0),
                reader.GetRawHeaderAttributes(0));

            if (version.Multipart ||
                (sourceHeader.PartType != V3.PartType.DeepScanline &&
                 sourceHeader.PartType != V3.PartType.DeepTiled))
            {
                return false;
            }

            if (sourceHeader.PartType == V3.PartType.DeepTiled &&
                sourceHeader.Tiles!.LevelMode != V3.TileLevelMode.OneLevel)
            {
                result = ResultCode.UnsupportedFeature;
                return true;
            }

            for (int channelIndex = 0; channelIndex < sourceHeader.Channels.Count; channelIndex++)
            {
                V3.Channel channel = sourceHeader.Channels[channelIndex];
                if (channel.XSampling != 1 || channel.YSampling != 1)
                {
                    return false;
                }
            }

            if (sourceHeader.Compression == V3.Compression.DWAA ||
                sourceHeader.Compression == V3.Compression.DWAB)
            {
                result = ResultCode.UnsupportedFeature;
                return true;
            }

            V3.ReaderResult<V3.Part> readResult = reader.ReadPart(0);
            if (!readResult.IsSuccess || readResult.Value == null)
            {
                result = MapResult(readResult.Status);
                return true;
            }

            image = ConvertDeepPart(sourceHeader, readResult.Value);
            result = ResultCode.Success;
            return true;
        }

        private static bool TryReadFlatImage(
            V3.ExrReader reader,
            ExrHeader requestedHeader,
            out ResultCode result,
            out ExrImage image)
        {
            result = ResultCode.InvalidData;
            image = EmptyImage();

            V3.ReaderResult headerResult = reader.ParseHeader();
            if (!headerResult.IsSuccess || reader.NumParts != 1)
            {
                return false;
            }

            V3.Header sourceHeader = reader.GetHeader(0);
            if (sourceHeader.IsDeep ||
                (sourceHeader.PartType != V3.PartType.Scanline &&
                 sourceHeader.PartType != V3.PartType.Tiled))
            {
                return false;
            }

            if (sourceHeader.Compression == V3.Compression.DWAA ||
                sourceHeader.Compression == V3.Compression.DWAB)
            {
                result = ResultCode.UnsupportedFeature;
                return true;
            }

            ResultCode validation = ValidateRequestedChannels(sourceHeader, requestedHeader);
            if (validation != ResultCode.Success)
            {
                result = validation;
                return true;
            }

            V3.ReaderResult<V3.Part> readResult = reader.ReadPart(0);
            if (!readResult.IsSuccess || readResult.Value == null)
            {
                result = MapResult(readResult.Status);
                return true;
            }

            image = ConvertFlatPart(sourceHeader, requestedHeader, readResult.Value);
            result = ResultCode.Success;
            return true;
        }

        private static ExrImage ConvertFlatPart(
            V3.Header sourceHeader,
            ExrHeader requestedHeader,
            V3.Part part)
        {
            List<ExrImageLevel> levels = new List<ExrImageLevel>(part.Levels.Count);
            for (int levelIndex = 0; levelIndex < part.Levels.Count; levelIndex++)
            {
                V3.FlatLevel sourceLevel = part.Levels[levelIndex] as V3.FlatLevel ??
                    throw new InvalidOperationException("A flat part returned a non-flat level.");
                int width = checked((int)sourceLevel.Width);
                int height = checked((int)sourceLevel.Height);
                List<ExrImageChannel> channels = ConvertLevelChannels(
                    sourceHeader,
                    requestedHeader,
                    sourceLevel);
                List<ExrTile> tiles = sourceHeader.IsTiled
                    ? BuildTiles(sourceHeader, sourceLevel, channels)
                    : new List<ExrTile>();
                levels.Add(new ExrImageLevel(
                    sourceLevel.LevelX,
                    sourceLevel.LevelY,
                    width,
                    height,
                    channels,
                    tiles));
            }

            return new ExrImage(levels);
        }

        private static ExrDeepImage ConvertDeepPart(V3.Header header, V3.Part part)
        {
            if (!part.IsComplete || part.Levels.Count != 1 ||
                part.GetLevel(0, 0) is not V3.DeepLevel level ||
                level.Region.MinX != header.DataWindow.MinX ||
                level.Region.MinY != header.DataWindow.MinY ||
                level.Region.MaxX != header.DataWindow.MaxX ||
                level.Region.MaxY != header.DataWindow.MaxY)
            {
                throw new InvalidOperationException("A complete deep scanline read returned an incomplete base level.");
            }

            int width = checked((int)level.Width);
            int height = checked((int)level.Height);
            ReadOnlySpan<int> sampleCounts = level.SampleCounts;
            if (sampleCounts.Length != checked(width * height))
            {
                throw new InvalidOperationException("The deep sample-count buffer does not match the image dimensions.");
            }

            int[][] offsetTable = new int[height][];
            int[] rowSampleCounts = new int[height];
            int pixelIndex = 0;
            ulong totalSamples = 0;
            for (int rowIndex = 0; rowIndex < height; rowIndex++)
            {
                int[] offsets = new int[width];
                int rowSampleCount = 0;
                for (int x = 0; x < width; x++)
                {
                    int count = sampleCounts[pixelIndex++];
                    rowSampleCount = checked(rowSampleCount + count);
                    offsets[x] = rowSampleCount;
                }

                offsetTable[rowIndex] = offsets;
                rowSampleCounts[rowIndex] = rowSampleCount;
                totalSamples = checked(totalSamples + (uint)rowSampleCount);
            }

            if (totalSamples != level.TotalSamples)
            {
                throw new InvalidOperationException("The deep row sample totals do not match the materialized level.");
            }

            List<ExrDeepChannel> channels = new List<ExrDeepChannel>(header.Channels.Count);
            for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
            {
                V3.Channel sourceChannel = header.Channels[channelIndex];
                V3.ChannelBuffer sourceBuffer = level.GetChannel(sourceChannel.Name);
                float[] samples = ConvertDeepSamplesToFloat(sourceBuffer);
                if ((ulong)samples.LongLength != totalSamples)
                {
                    throw new InvalidOperationException(
                        $"Deep channel '{sourceChannel.Name}' does not contain one sample stream for every pixel.");
                }

                float[][] rows = new float[height][];
                int sourceOffset = 0;
                for (int rowIndex = 0; rowIndex < height; rowIndex++)
                {
                    int rowSampleCount = rowSampleCounts[rowIndex];
                    float[] row = new float[rowSampleCount];
                    samples.AsSpan(sourceOffset, rowSampleCount).CopyTo(row);
                    rows[rowIndex] = row;
                    sourceOffset = checked(sourceOffset + rowSampleCount);
                }

                if (sourceOffset != samples.Length)
                {
                    throw new InvalidOperationException(
                        $"Deep channel '{sourceChannel.Name}' contains trailing samples.");
                }

                channels.Add(new ExrDeepChannel(sourceChannel.Name, rows));
            }

            return new ExrDeepImage(width, height, offsetTable, channels);
        }

        private static float[] ConvertDeepSamplesToFloat(V3.ChannelBuffer source)
        {
            int sampleCount = checked((int)source.SampleCount);
            float[] result = new float[sampleCount];
            switch (source.PixelType)
            {
                case V3.PixelType.UInt:
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        result[sampleIndex] = BinaryPrimitives.ReadUInt32LittleEndian(
                            source.Data.Slice(sampleIndex * sizeof(uint), sizeof(uint)));
                    }

                    break;
                case V3.PixelType.Half:
                    ushort[] half = new ushort[sampleCount];
                    if (BitConverter.IsLittleEndian)
                    {
                        MemoryMarshal.Cast<byte, ushort>(source.Data).CopyTo(half);
                    }
                    else
                    {
                        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                        {
                            half[sampleIndex] = BinaryPrimitives.ReadUInt16LittleEndian(
                                source.Data.Slice(sampleIndex * sizeof(ushort), sizeof(ushort)));
                        }
                    }

                    V3.PixelConversion.HalfToFloat(half, result);
                    break;
                case V3.PixelType.Float:
                    if (BitConverter.IsLittleEndian)
                    {
                        MemoryMarshal.Cast<byte, float>(source.Data).CopyTo(result);
                    }
                    else
                    {
                        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                        {
                            result[sampleIndex] = BitConverter.Int32BitsToSingle(
                                BinaryPrimitives.ReadInt32LittleEndian(
                                    source.Data.Slice(sampleIndex * sizeof(float), sizeof(float))));
                        }
                    }

                    break;
                default:
                    throw new NotSupportedException($"Deep pixel type '{source.PixelType}' is not supported.");
            }

            return result;
        }

        private static List<ExrImageChannel> ConvertLevelChannels(
            V3.Header sourceHeader,
            ExrHeader requestedHeader,
            V3.FlatLevel level)
        {
            List<ExrImageChannel> channels = new List<ExrImageChannel>(sourceHeader.Channels.Count);
            for (int channelIndex = 0; channelIndex < sourceHeader.Channels.Count; channelIndex++)
            {
                V3.Channel sourceChannel = sourceHeader.Channels[channelIndex];
                ExrChannel requestedChannel = requestedHeader.Channels[channelIndex];
                V3.ChannelBuffer sourceBuffer = level.GetChannel(sourceChannel.Name);
                byte[] channelData = requestedChannel.RequestedPixelType == ExrPixelType.Float &&
                    sourceChannel.PixelType == V3.PixelType.Half
                        ? ConvertHalfToFloat(sourceBuffer.Data)
                        : sourceBuffer.Data.ToArray();
                channels.Add(new ExrImageChannel(
                    CreateOutputChannel(sourceChannel, requestedChannel.RequestedPixelType),
                    requestedChannel.RequestedPixelType,
                    channelData));
            }

            return channels;
        }

        private static List<ExrTile> BuildTiles(
            V3.Header header,
            V3.FlatLevel level,
            IReadOnlyList<ExrImageChannel> levelChannels)
        {
            V3.TileDescription description = header.Tiles ??
                throw new InvalidOperationException("A tiled part has no tile description.");
            int levelWidth = checked((int)level.Width);
            int levelHeight = checked((int)level.Height);
            int tileSizeX = checked((int)description.TileSizeX);
            int tileSizeY = checked((int)description.TileSizeY);
            int tilesX = checked((levelWidth + tileSizeX - 1) / tileSizeX);
            int tilesY = checked((levelHeight + tileSizeY - 1) / tileSizeY);
            List<ExrTile> tiles = new List<ExrTile>(checked(tilesX * tilesY));
            for (int tileY = 0; tileY < tilesY; tileY++)
            {
                int offsetY = checked(tileY * tileSizeY);
                int tileHeight = Math.Min(tileSizeY, levelHeight - offsetY);
                for (int tileX = 0; tileX < tilesX; tileX++)
                {
                    int offsetX = checked(tileX * tileSizeX);
                    int tileWidth = Math.Min(tileSizeX, levelWidth - offsetX);
                    List<ExrImageChannel> tileChannels = new List<ExrImageChannel>(levelChannels.Count);
                    for (int channelIndex = 0; channelIndex < levelChannels.Count; channelIndex++)
                    {
                        ExrImageChannel source = levelChannels[channelIndex];
                        if (source.Channel.SamplingX != 1 || source.Channel.SamplingY != 1)
                        {
                            throw new InvalidOperationException("Tiled channel sampling must be one.");
                        }

                        int sampleSize = source.DataType == ExrPixelType.Half
                            ? sizeof(ushort)
                            : sizeof(uint);
                        int rowByteCount = checked(tileWidth * sampleSize);
                        byte[] data = new byte[checked(rowByteCount * tileHeight)];
                        for (int row = 0; row < tileHeight; row++)
                        {
                            int sourceOffset = checked(
                                ((offsetY + row) * levelWidth + offsetX) * sampleSize);
                            source.Data.AsSpan(sourceOffset, rowByteCount).CopyTo(
                                data.AsSpan(row * rowByteCount, rowByteCount));
                        }

                        tileChannels.Add(new ExrImageChannel(
                            CloneOutputChannel(source.Channel),
                            source.DataType,
                            data));
                    }

                    tiles.Add(new ExrTile(
                        offsetX,
                        offsetY,
                        level.LevelX,
                        level.LevelY,
                        tileWidth,
                        tileHeight,
                        tileChannels));
                }
            }

            return tiles;
        }

        private static ExrChannel CreateOutputChannel(
            V3.Channel source,
            ExrPixelType requestedPixelType)
        {
            return new ExrChannel(
                source.Name,
                (ExrPixelType)(int)source.PixelType,
                requestedPixelType,
                source.XSampling,
                source.YSampling,
                source.PerceptuallyLinear ? (byte)1 : (byte)0);
        }

        private static ExrChannel CloneOutputChannel(ExrChannel source)
        {
            return new ExrChannel(
                source.Name,
                source.Type,
                source.RequestedPixelType,
                source.SamplingX,
                source.SamplingY,
                source.Linear);
        }

        internal static bool TryWriteFlatImage(
            ExrImage image,
            ExrHeader? header,
            out ResultCode result,
            out byte[] encoded)
        {
            return TryWriteFlatImages(
                new[] { image },
                new ExrHeader?[] { header },
                multipart: false,
                out result,
                out encoded);
        }

        internal static bool TryWriteMultipartImages(
            IReadOnlyList<ExrImage> images,
            IReadOnlyList<ExrHeader> headers,
            out ResultCode result,
            out byte[] encoded)
        {
            ExrHeader?[] nullableHeaders;
            if (headers == null)
            {
                nullableHeaders = Array.Empty<ExrHeader?>();
            }
            else
            {
                nullableHeaders = new ExrHeader?[headers.Count];
                for (int headerIndex = 0; headerIndex < headers.Count; headerIndex++)
                {
                    nullableHeaders[headerIndex] = headers[headerIndex];
                }
            }

            return TryWriteFlatImages(
                images,
                nullableHeaders,
                multipart: true,
                out result,
                out encoded);
        }

        private static bool TryWriteFlatImages(
            IReadOnlyList<ExrImage> images,
            IReadOnlyList<ExrHeader?> headers,
            bool multipart,
            out ResultCode result,
            out byte[] encoded)
        {
            result = ResultCode.InvalidArgument;
            encoded = Array.Empty<byte>();
            try
            {
                if (images == null || headers == null || images.Count != headers.Count ||
                    (multipart ? images.Count == 0 : images.Count != 1))
                {
                    return false;
                }

                V3.Header[] writeHeaders = new V3.Header[images.Count];
                for (int partIndex = 0; partIndex < images.Count; partIndex++)
                {
                    if (!TryCreateWriteHeader(
                        images[partIndex],
                        headers[partIndex],
                        multipart,
                        out V3.Header? writeHeader))
                    {
                        return false;
                    }

                    writeHeaders[partIndex] = writeHeader!;
                }

                using MemoryStream stream = new MemoryStream();
                using V3IO.StreamDataSink sink = new V3IO.StreamDataSink(stream, leaveOpen: true);
                using V3.ExrWriter writer = V3.ExrWriter.OpenSink(
                    sink,
                    new V3.WriterOptions(forceMultipart: multipart));
                for (int partIndex = 0; partIndex < writeHeaders.Length; partIndex++)
                {
                    writer.AddPart(writeHeaders[partIndex]);
                }

                V3.WriterResult beginResult = writer.Begin();
                if (!beginResult.IsSuccess)
                {
                    result = MapWriterResult(beginResult);
                    return true;
                }

                for (int partIndex = 0; partIndex < writeHeaders.Length; partIndex++)
                {
                    V3.Header writeHeader = writeHeaders[partIndex];
                    ExrImage image = images[partIndex];
                    for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(partIndex); blockIndex++)
                    {
                        V3.BlockInfo block = writer.GetBlockInfo(partIndex, blockIndex);
                        ExrImageLevel level = GetWriteLevel(image, block.LevelX, block.LevelY);
                        List<V3.ChannelBuffer> channels = CreateBlockChannels(
                            level,
                            writeHeader,
                            block.Region);
                        V3.WriterResult blockResult = writeHeader.IsTiled
                            ? writer.WriteTile(
                                partIndex,
                                block.TileX,
                                block.TileY,
                                block.LevelX,
                                block.LevelY,
                                channels)
                            : writer.WriteScanlineBlock(
                                partIndex,
                                block.Region.MinY,
                                channels);
                        if (!blockResult.IsSuccess)
                        {
                            result = MapWriterResult(blockResult);
                            return true;
                        }
                    }
                }

                V3.WriterResult endResult = writer.End();
                if (!endResult.IsSuccess)
                {
                    result = MapWriterResult(endResult);
                    return true;
                }

                encoded = stream.ToArray();
                result = ResultCode.Success;
                return true;
            }
            catch (V3.WriterLimitExceededException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OutOfMemoryException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (OverflowException)
            {
                result = ResultCode.DataTooLarge;
                return true;
            }
            catch (ArgumentException)
            {
                result = ResultCode.InvalidArgument;
                return true;
            }
            catch (InvalidOperationException)
            {
                result = ResultCode.SerialzationFailed;
                return true;
            }
            catch (NotSupportedException)
            {
                result = ResultCode.UnsupportedFeature;
                return true;
            }
        }

        private static ExrVersion ConvertVersion(ParsedFile parsed)
        {
            return new ExrVersion
            {
                Version = parsed.FileVersion,
                Tiled = parsed.Flags.Tiled,
                LongName = parsed.Flags.LongNames,
                NonImage = parsed.Flags.NonImage,
                Multipart = parsed.Flags.Multipart,
            };
        }

        private static ExrVersion ConvertVersion(uint rawVersionField)
        {
            return new ExrVersion
            {
                Version = (int)(rawVersionField & 0xffU),
                Tiled = (rawVersionField & (1U << 9)) != 0,
                LongName = (rawVersionField & (1U << 10)) != 0,
                NonImage = (rawVersionField & (1U << 11)) != 0,
                Multipart = (rawVersionField & (1U << 12)) != 0,
            };
        }

        private static ExrHeader ConvertHeader(ParsedFile parsed, ParsedPartIndex part)
        {
            V3.Header source = part.Header;
            ExrHeader result = new ExrHeader
            {
                Name = HasRawAttribute(part, "name") ? source.Name : null,
                PartType = HasRawAttribute(part, "type") ? ConvertPartType(source.PartType) : null,
                Compression = (CompressionType)(int)source.Compression,
                LineOrder = (LineOrderType)(int)source.LineOrder,
                PixelAspectRatio = source.PixelAspectRatio,
                ScreenWindowCenter = source.ScreenWindowCenter,
                ScreenWindowWidth = source.ScreenWindowWidth,
                DataWindow = ConvertBox(source.DataWindow),
                DisplayWindow = ConvertBox(source.DisplayWindow),
                Tiles = source.Tiles == null ? null : ConvertTiles(source.Tiles),
                IsDeep = source.IsDeep,
                IsMultipart = parsed.Flags.Multipart,
                HasLongNames = parsed.Flags.LongNames,
                ChunkCount = part.Chunks.Count,
                HeaderLength = part.HeaderEnd,
            };

            for (int channelIndex = 0; channelIndex < source.Channels.Count; channelIndex++)
            {
                V3.Channel channel = source.Channels[channelIndex];
                result.Channels.Add(new ExrChannel(
                    channel.Name,
                    (ExrPixelType)(int)channel.PixelType,
                    channel.XSampling,
                    channel.YSampling,
                    channel.PerceptuallyLinear ? (byte)1 : (byte)0));
            }

            for (int attributeIndex = 0; attributeIndex < part.RawAttributes.Count; attributeIndex++)
            {
                V3.HeaderAttribute attribute = part.RawAttributes[attributeIndex].Value;
                if (!IsV1ModeledAttribute(attribute.Name))
                {
                    result.CustomAttributes.Add(new ExrAttribute(
                        attribute.Name,
                        attribute.TypeName,
                        attribute.Data.ToArray()));
                }
            }

            return result;
        }

        private static ExrHeader ConvertHeader(
            V3.Header source,
            bool hasNameAttribute,
            bool hasTypeAttribute,
            bool multipart,
            bool longNames,
            int chunkCount,
            long headerEnd,
            IReadOnlyList<V3.HeaderAttribute> rawAttributes)
        {
            ExrHeader result = new ExrHeader
            {
                Name = hasNameAttribute ? source.Name : null,
                PartType = hasTypeAttribute ? ConvertPartType(source.PartType) : null,
                Compression = (CompressionType)(int)source.Compression,
                LineOrder = (LineOrderType)(int)source.LineOrder,
                PixelAspectRatio = source.PixelAspectRatio,
                ScreenWindowCenter = source.ScreenWindowCenter,
                ScreenWindowWidth = source.ScreenWindowWidth,
                DataWindow = ConvertBox(source.DataWindow),
                DisplayWindow = ConvertBox(source.DisplayWindow),
                Tiles = source.Tiles == null ? null : ConvertTiles(source.Tiles),
                IsDeep = source.IsDeep,
                IsMultipart = multipart,
                HasLongNames = longNames,
                ChunkCount = chunkCount,
                HeaderLength = checked((int)headerEnd),
            };

            for (int channelIndex = 0; channelIndex < source.Channels.Count; channelIndex++)
            {
                V3.Channel channel = source.Channels[channelIndex];
                result.Channels.Add(new ExrChannel(
                    channel.Name,
                    (ExrPixelType)(int)channel.PixelType,
                    channel.XSampling,
                    channel.YSampling,
                    channel.PerceptuallyLinear ? (byte)1 : (byte)0));
            }

            for (int attributeIndex = 0; attributeIndex < rawAttributes.Count; attributeIndex++)
            {
                V3.HeaderAttribute attribute = rawAttributes[attributeIndex];
                if (!IsV1ModeledAttribute(attribute.Name))
                {
                    result.CustomAttributes.Add(new ExrAttribute(
                        attribute.Name,
                        attribute.TypeName,
                        attribute.Data.ToArray()));
                }
            }

            return result;
        }

        private static bool TryCreateWriteHeader(
            ExrImage image,
            ExrHeader? header,
            bool multipart,
            out V3.Header? writeHeader)
        {
            writeHeader = null;
            if (image == null || image.Width <= 0 || image.Height <= 0 || image.Levels.Count == 0)
            {
                return false;
            }

            ExrHeader effective = header ?? new ExrHeader();
            if (effective.IsDeep ||
                effective.Compression == CompressionType.DWAA ||
                effective.Compression == CompressionType.DWAB ||
                !Enum.IsDefined(typeof(CompressionType), effective.Compression))
            {
                return false;
            }

            ExrImageLevel level = image.Levels[0];
            if (level == null || level.LevelX != 0 || level.LevelY != 0 ||
                level.Width != image.Width || level.Height != image.Height ||
                level.Channels.Count == 0)
            {
                return false;
            }

            try
            {
                ExrBox2i dataWindow = ShouldDefaultToImageWindow(
                    effective.DataWindow,
                    image.Width,
                    image.Height)
                        ? new ExrBox2i(0, 0, image.Width - 1, image.Height - 1)
                        : effective.DataWindow;
                if ((long)dataWindow.MaxX - dataWindow.MinX + 1L != image.Width ||
                    (long)dataWindow.MaxY - dataWindow.MinY + 1L != image.Height)
                {
                    return false;
                }

                ExrBox2i displayWindow = ShouldDefaultToImageWindow(
                    effective.DisplayWindow,
                    image.Width,
                    image.Height)
                        ? dataWindow
                        : effective.DisplayWindow;

                V3.TileDescription? tiles = null;
                V3.PartType partType = V3.PartType.Scanline;
                V3.LineOrder lineOrder = V3.LineOrder.IncreasingY;
                if (effective.Tiles == null)
                {
                    if (image.Levels.Count != 1)
                    {
                        return false;
                    }
                }
                else
                {
                    if (effective.Tiles.TileSizeX <= 0 || effective.Tiles.TileSizeY <= 0 ||
                        !Enum.IsDefined(typeof(ExrTileLevelMode), effective.Tiles.LevelMode) ||
                        !Enum.IsDefined(typeof(ExrTileRoundingMode), effective.Tiles.RoundingMode) ||
                        !Enum.IsDefined(typeof(LineOrderType), effective.LineOrder))
                    {
                        return false;
                    }

                    tiles = new V3.TileDescription(
                        checked((uint)effective.Tiles.TileSizeX),
                        checked((uint)effective.Tiles.TileSizeY),
                        (V3.TileLevelMode)(int)effective.Tiles.LevelMode,
                        (V3.TileRoundingMode)(int)effective.Tiles.RoundingMode);
                    partType = V3.PartType.Tiled;
                    lineOrder = (V3.LineOrder)(int)effective.LineOrder;
                }

                string? partName = null;
                if (multipart)
                {
                    if (string.IsNullOrWhiteSpace(effective.Name))
                    {
                        return false;
                    }

                    partName = effective.Name;
                }
                else if (!string.IsNullOrWhiteSpace(effective.Name))
                {
                    return false;
                }

                List<V3.Channel> channels = new List<V3.Channel>(level.Channels.Count);
                bool hasLongNames = multipart && IsLongName(partName!);
                for (int channelIndex = 0; channelIndex < level.Channels.Count; channelIndex++)
                {
                    ExrImageChannel imageChannel = level.Channels[channelIndex];
                    if (imageChannel == null || imageChannel.Channel == null)
                    {
                        return false;
                    }

                    ExrChannel channel = imageChannel.Channel;
                    if (channel.SamplingX <= 0 || channel.SamplingY <= 0 ||
                        channel.Linear > 1 ||
                        !Enum.IsDefined(typeof(ExrPixelType), channel.Type) ||
                        !Enum.IsDefined(typeof(ExrPixelType), imageChannel.DataType) ||
                        (tiles != null && (channel.SamplingX != 1 || channel.SamplingY != 1)) ||
                        (tiles == null && !IsWriteSamplingAligned(dataWindow, channel)))
                    {
                        return false;
                    }

                    channels.Add(new V3.Channel(
                        channel.Name,
                        (V3.PixelType)(int)channel.Type,
                        channel.SamplingX,
                        channel.SamplingY,
                        perceptuallyLinear: channel.Linear != 0));
                    hasLongNames |= IsLongName(channel.Name);
                }

                List<V3.HeaderAttribute> attributes = new List<V3.HeaderAttribute>(
                    effective.CustomAttributes.Count);
                HashSet<string> customNames = new HashSet<string>(StringComparer.Ordinal);
                V3.Chromaticities? chromaticities = null;
                for (int attributeIndex = 0; attributeIndex < effective.CustomAttributes.Count; attributeIndex++)
                {
                    ExrAttribute attribute = effective.CustomAttributes[attributeIndex];
                    if (attribute == null || !customNames.Add(attribute.Name))
                    {
                        return false;
                    }

                    if (string.Equals(attribute.Name, "chromaticities", StringComparison.Ordinal))
                    {
                        if (chromaticities.HasValue ||
                            !string.Equals(attribute.TypeName, "chromaticities", StringComparison.Ordinal) ||
                            attribute.Value.Length != 8 * sizeof(float))
                        {
                            return false;
                        }

                        chromaticities = DecodeChromaticities(attribute.Value);
                        continue;
                    }

                    if (ReservedWriteAttributes.Contains(attribute.Name))
                    {
                        return false;
                    }

                    attributes.Add(new V3.HeaderAttribute(
                        attribute.Name,
                        attribute.TypeName,
                        attribute.Value));
                    hasLongNames |= IsLongName(attribute.Name) || IsLongName(attribute.TypeName);
                }

                if (effective.HasLongNames != hasLongNames)
                {
                    return false;
                }

                V3.Header candidate = new V3.Header(
                    partType,
                    new V3.Box2i(dataWindow.MinX, dataWindow.MinY, dataWindow.MaxX, dataWindow.MaxY),
                    channels,
                    (V3.Compression)(int)effective.Compression,
                    lineOrder,
                    new V3.Box2i(
                        displayWindow.MinX,
                        displayWindow.MinY,
                        displayWindow.MaxX,
                        displayWindow.MaxY),
                    effective.PixelAspectRatio,
                    effective.ScreenWindowCenter,
                    effective.ScreenWindowWidth,
                    tiles,
                    partName,
                    chromaticities,
                    attributes);

                for (int channelIndex = 0; channelIndex < candidate.Channels.Count; channelIndex++)
                {
                    if (!string.Equals(
                        candidate.Channels[channelIndex].Name,
                        level.Channels[channelIndex].Channel.Name,
                        StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                if (!ValidateWriteLevels(image, candidate))
                {
                    return false;
                }

                writeHeader = candidate;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static List<V3.ChannelBuffer> CreateBlockChannels(
            ExrImageLevel level,
            V3.Header header,
            V3.Box2i region)
        {
            int relativeX = checked(region.MinX - header.DataWindow.MinX);
            int relativeY = checked(region.MinY - header.DataWindow.MinY);
            int blockWidth = checked((int)region.Width);
            int blockHeight = checked((int)region.Height);
            if (relativeX < 0 || relativeY < 0 ||
                blockWidth > level.Width - relativeX ||
                blockHeight > level.Height - relativeY)
            {
                throw new ArgumentException("The writer block region does not fit inside its image level.");
            }

            List<V3.ChannelBuffer> channels = new List<V3.ChannelBuffer>(level.Channels.Count);
            for (int channelIndex = 0; channelIndex < level.Channels.Count; channelIndex++)
            {
                ExrImageChannel source = level.Channels[channelIndex];
                V3.Channel target = header.Channels[channelIndex];
                int sourceSampleSize = PixelTypeSize(source.DataType);
                int levelMaximumX = checked(header.DataWindow.MinX + level.Width - 1);
                int levelMaximumY = checked(header.DataWindow.MinY + level.Height - 1);
                int sourceSampleWidth = checked((int)V3.ModelValidation.CountSampleLocations(
                    header.DataWindow.MinX,
                    levelMaximumX,
                    target.XSampling));
                int startSampleX = region.MinX == header.DataWindow.MinX
                    ? 0
                    : checked((int)V3.ModelValidation.CountSampleLocations(
                        header.DataWindow.MinX,
                        checked(region.MinX - 1),
                        target.XSampling));
                int startSampleY = region.MinY == header.DataWindow.MinY
                    ? 0
                    : checked((int)V3.ModelValidation.CountSampleLocations(
                        header.DataWindow.MinY,
                        checked(region.MinY - 1),
                        target.YSampling));
                int blockSampleWidth = checked((int)V3.ModelValidation.CountSampleLocations(
                    region.MinX,
                    region.MaxX,
                    target.XSampling));
                int blockSampleHeight = checked((int)V3.ModelValidation.CountSampleLocations(
                    region.MinY,
                    region.MaxY,
                    target.YSampling));
                int rowSourceByteCount = checked(blockSampleWidth * sourceSampleSize);
                ReadOnlySpan<byte> sourceSamples;
                if (startSampleX == 0 && blockSampleWidth == sourceSampleWidth)
                {
                    int sourceOffset = checked(startSampleY * sourceSampleWidth * sourceSampleSize);
                    int sourceByteCount = checked(rowSourceByteCount * blockSampleHeight);
                    sourceSamples = source.Data.AsSpan(sourceOffset, sourceByteCount);
                }
                else
                {
                    byte[] gathered = new byte[checked(rowSourceByteCount * blockSampleHeight)];
                    for (int rowIndex = 0; rowIndex < blockSampleHeight; rowIndex++)
                    {
                        int sourceOffset = checked(
                            ((startSampleY + rowIndex) * sourceSampleWidth + startSampleX) *
                            sourceSampleSize);
                        source.Data.AsSpan(sourceOffset, rowSourceByteCount).CopyTo(
                            gathered.AsSpan(rowIndex * rowSourceByteCount, rowSourceByteCount));
                    }

                    sourceSamples = gathered;
                }

                channels.Add(new V3.ChannelBuffer(
                    source.Channel.Name,
                    target.PixelType,
                    ConvertWriteSamples(sourceSamples, source.DataType, target.PixelType)));
            }

            return channels;
        }

        private static bool ValidateWriteLevels(ExrImage image, V3.Header header)
        {
            if (!header.IsTiled)
            {
                return image.Levels.Count == 1 && ValidateWriteLevel(
                    image.Levels[0],
                    header,
                    0,
                    0,
                    image.Width,
                    image.Height,
                    expectedTileCount: null);
            }

            V3.TileDescription tiles = header.Tiles!;
            int xLevelCount = ExrFormatParser.LevelCount(
                header.DataWindow.Width,
                tiles.RoundingMode);
            int yLevelCount = ExrFormatParser.LevelCount(
                header.DataWindow.Height,
                tiles.RoundingMode);
            int expectedLevelCount = tiles.LevelMode switch
            {
                V3.TileLevelMode.OneLevel => 1,
                V3.TileLevelMode.MipmapLevels => Math.Max(xLevelCount, yLevelCount),
                V3.TileLevelMode.RipmapLevels => checked(xLevelCount * yLevelCount),
                _ => 0,
            };
            if (image.Levels.Count != expectedLevelCount)
            {
                return false;
            }

            int levelIndex = 0;
            if (tiles.LevelMode == V3.TileLevelMode.RipmapLevels)
            {
                for (int levelY = 0; levelY < yLevelCount; levelY++)
                {
                    for (int levelX = 0; levelX < xLevelCount; levelX++)
                    {
                        if (!ValidateTiledWriteLevel(
                            image.Levels[levelIndex++],
                            header,
                            levelX,
                            levelY))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            for (int level = 0; level < expectedLevelCount; level++)
            {
                int levelX = tiles.LevelMode == V3.TileLevelMode.OneLevel ? 0 : level;
                int levelY = tiles.LevelMode == V3.TileLevelMode.OneLevel ? 0 : level;
                if (!ValidateTiledWriteLevel(
                    image.Levels[levelIndex++],
                    header,
                    levelX,
                    levelY))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateTiledWriteLevel(
            ExrImageLevel level,
            V3.Header header,
            int levelX,
            int levelY)
        {
            V3.TileDescription tiles = header.Tiles!;
            int width = checked((int)ExrFormatParser.LevelSize(
                header.DataWindow.Width,
                levelX,
                tiles.RoundingMode));
            int height = checked((int)ExrFormatParser.LevelSize(
                header.DataWindow.Height,
                levelY,
                tiles.RoundingMode));
            int tilesX = checked((int)(((long)width + tiles.TileSizeX - 1L) / tiles.TileSizeX));
            int tilesY = checked((int)(((long)height + tiles.TileSizeY - 1L) / tiles.TileSizeY));
            return ValidateWriteLevel(
                level,
                header,
                levelX,
                levelY,
                width,
                height,
                checked(tilesX * tilesY));
        }

        private static bool ValidateWriteLevel(
            ExrImageLevel level,
            V3.Header header,
            int levelX,
            int levelY,
            int width,
            int height,
            int? expectedTileCount)
        {
            if (level == null || level.LevelX != levelX || level.LevelY != levelY ||
                level.Width != width || level.Height != height ||
                level.Channels.Count != header.Channels.Count ||
                (expectedTileCount.HasValue && level.Tiles.Count > 0 &&
                 level.Tiles.Count != expectedTileCount.Value))
            {
                return false;
            }

            for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
            {
                ExrImageChannel imageChannel = level.Channels[channelIndex];
                V3.Channel headerChannel = header.Channels[channelIndex];
                if (imageChannel == null || imageChannel.Channel == null ||
                    !string.Equals(imageChannel.Channel.Name, headerChannel.Name, StringComparison.Ordinal) ||
                    (int)imageChannel.Channel.Type != (int)headerChannel.PixelType ||
                    imageChannel.Channel.SamplingX != headerChannel.XSampling ||
                    imageChannel.Channel.SamplingY != headerChannel.YSampling ||
                    (imageChannel.Channel.Linear != 0) != headerChannel.PerceptuallyLinear ||
                    !Enum.IsDefined(typeof(ExrPixelType), imageChannel.DataType))
                {
                    return false;
                }

                V3.Box2i levelRegion = new V3.Box2i(
                    header.DataWindow.MinX,
                    header.DataWindow.MinY,
                    checked(header.DataWindow.MinX + width - 1),
                    checked(header.DataWindow.MinY + height - 1));
                ulong sampleCount = V3.ModelValidation.CountSamples(
                    levelRegion,
                    headerChannel.XSampling,
                    headerChannel.YSampling);
                long expectedLength = checked((long)sampleCount * PixelTypeSize(imageChannel.DataType));
                if (expectedLength > int.MaxValue || imageChannel.Data.Length != (int)expectedLength)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsWriteSamplingAligned(ExrBox2i dataWindow, ExrChannel channel)
        {
            return dataWindow.MinX % channel.SamplingX == 0 &&
                dataWindow.MinY % channel.SamplingY == 0 &&
                dataWindow.Width % channel.SamplingX == 0 &&
                dataWindow.Height % channel.SamplingY == 0;
        }

        private static byte[] ConvertWriteSamples(
            ReadOnlySpan<byte> source,
            ExrPixelType sourceType,
            V3.PixelType targetType)
        {
            int sourceSampleSize = PixelTypeSize(sourceType);
            int sampleCount = source.Length / sourceSampleSize;
            if ((int)sourceType == (int)targetType)
            {
                return source.ToArray();
            }

            float[] values = new float[sampleCount];
            switch (sourceType)
            {
                case ExrPixelType.UInt:
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        values[sampleIndex] = BinaryPrimitives.ReadUInt32LittleEndian(
                            source.Slice(sampleIndex * sizeof(uint), sizeof(uint)));
                    }

                    break;
                case ExrPixelType.Half:
                    ushort[] half = new ushort[sampleCount];
                    if (BitConverter.IsLittleEndian)
                    {
                        MemoryMarshal.Cast<byte, ushort>(source).CopyTo(half);
                    }
                    else
                    {
                        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                        {
                            half[sampleIndex] = BinaryPrimitives.ReadUInt16LittleEndian(
                                source.Slice(sampleIndex * sizeof(ushort), sizeof(ushort)));
                        }
                    }

                    V3.PixelConversion.HalfToFloat(half, values);
                    break;
                case ExrPixelType.Float:
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        values[sampleIndex] = ReadSingleLittleEndian(
                            source.Slice(sampleIndex * sizeof(float), sizeof(float)));
                    }

                    break;
                default:
                    throw new NotSupportedException($"Source pixel type '{sourceType}' is not supported.");
            }

            int targetSampleSize = V3.ModelValidation.PixelTypeSize(targetType);
            byte[] converted = new byte[checked(sampleCount * targetSampleSize)];
            switch (targetType)
            {
                case V3.PixelType.UInt:
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        float value = values[sampleIndex];
                        if (float.IsNaN(value) || float.IsInfinity(value) ||
                            value < 0.0f || value > uint.MaxValue)
                        {
                            throw new NotSupportedException(
                                "A floating-point sample cannot be represented as UINT.");
                        }

                        BinaryPrimitives.WriteUInt32LittleEndian(
                            converted.AsSpan(sampleIndex * sizeof(uint), sizeof(uint)),
                            (uint)value);
                    }

                    break;
                case V3.PixelType.Half:
                    ushort[] half = new ushort[sampleCount];
                    V3.PixelConversion.FloatToHalf(values, half);
                    if (BitConverter.IsLittleEndian)
                    {
                        MemoryMarshal.AsBytes(half.AsSpan()).CopyTo(converted);
                    }
                    else
                    {
                        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(
                                converted.AsSpan(sampleIndex * sizeof(ushort), sizeof(ushort)),
                                half[sampleIndex]);
                        }
                    }

                    break;
                case V3.PixelType.Float:
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(
                            converted.AsSpan(sampleIndex * sizeof(float), sizeof(float)),
                            BitConverter.SingleToInt32Bits(values[sampleIndex]));
                    }

                    break;
                default:
                    throw new NotSupportedException($"Target pixel type '{targetType}' is not supported.");
            }

            return converted;
        }

        private static int PixelTypeSize(ExrPixelType pixelType)
        {
            return pixelType == ExrPixelType.Half ? sizeof(ushort) : sizeof(uint);
        }

        private static ExrImageLevel GetWriteLevel(ExrImage image, int levelX, int levelY)
        {
            for (int levelIndex = 0; levelIndex < image.Levels.Count; levelIndex++)
            {
                ExrImageLevel level = image.Levels[levelIndex];
                if (level.LevelX == levelX && level.LevelY == levelY)
                {
                    return level;
                }
            }

            throw new InvalidOperationException(
                $"The image does not contain tiled level ({levelX}, {levelY}).");
        }

        private static V3.Chromaticities DecodeChromaticities(ReadOnlySpan<byte> value)
        {
            return new V3.Chromaticities(
                ReadSingleLittleEndian(value.Slice(0, 4)),
                ReadSingleLittleEndian(value.Slice(4, 4)),
                ReadSingleLittleEndian(value.Slice(8, 4)),
                ReadSingleLittleEndian(value.Slice(12, 4)),
                ReadSingleLittleEndian(value.Slice(16, 4)),
                ReadSingleLittleEndian(value.Slice(20, 4)),
                ReadSingleLittleEndian(value.Slice(24, 4)),
                ReadSingleLittleEndian(value.Slice(28, 4)));
        }

        private static float ReadSingleLittleEndian(ReadOnlySpan<byte> value)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(value));
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

        private static bool IsLongName(string value)
        {
            return V3.ModelValidation.StrictUtf8.GetByteCount(value) > 31;
        }

        private static bool IsFlatImage(ExrVersion version, ExrHeader header)
        {
            return !version.Multipart &&
                !version.NonImage &&
                !header.IsDeep;
        }

        private static void RequestFloatForHalfChannels(ExrHeader header)
        {
            for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
            {
                ExrChannel channel = header.Channels[channelIndex];
                if (channel.Type == ExrPixelType.Half)
                {
                    channel.RequestedPixelType = ExrPixelType.Float;
                }
            }
        }

        private static ResultCode ValidateRequestedChannels(V3.Header source, ExrHeader requested)
        {
            if (requested.Channels.Count != source.Channels.Count)
            {
                return ResultCode.InvalidArgument;
            }

            for (int channelIndex = 0; channelIndex < source.Channels.Count; channelIndex++)
            {
                V3.Channel sourceChannel = source.Channels[channelIndex];
                ExrChannel requestedChannel = requested.Channels[channelIndex];
                if (!string.Equals(sourceChannel.Name, requestedChannel.Name, StringComparison.Ordinal) ||
                    (int)sourceChannel.PixelType != (int)requestedChannel.Type)
                {
                    return ResultCode.InvalidArgument;
                }

                bool supported = sourceChannel.PixelType switch
                {
                    V3.PixelType.Half => requestedChannel.RequestedPixelType == ExrPixelType.Half ||
                        requestedChannel.RequestedPixelType == ExrPixelType.Float,
                    V3.PixelType.UInt => requestedChannel.RequestedPixelType == ExrPixelType.UInt,
                    V3.PixelType.Float => requestedChannel.RequestedPixelType == ExrPixelType.Float,
                    _ => false,
                };
                if (!supported)
                {
                    return ResultCode.UnsupportedFeature;
                }
            }

            return ResultCode.Success;
        }

        private static byte[] ConvertHalfToFloat(ReadOnlySpan<byte> source)
        {
            int sampleCount = source.Length / sizeof(ushort);
            ushort[] half = new ushort[sampleCount];
            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.Cast<byte, ushort>(source).CopyTo(half);
            }
            else
            {
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    half[sampleIndex] = BinaryPrimitives.ReadUInt16LittleEndian(
                        source.Slice(sampleIndex * sizeof(ushort), sizeof(ushort)));
                }
            }

            float[] converted = new float[sampleCount];
            V3.PixelConversion.HalfToFloat(half, converted);
            byte[] result = new byte[checked(sampleCount * sizeof(float))];
            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.AsBytes(converted.AsSpan()).CopyTo(result);
            }
            else
            {
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        result.AsSpan(sampleIndex * sizeof(float), sizeof(float)),
                        BitConverter.SingleToInt32Bits(converted[sampleIndex]));
                }
            }

            return result;
        }

        private static ResultCode MapWriterResult(V3.WriterResult result)
        {
            if (result.Error is V3.WriterLimitExceededException)
            {
                return ResultCode.DataTooLarge;
            }

            return result.Status switch
            {
                V3.ExrResult.Success => ResultCode.Success,
                V3.ExrResult.InvalidArgument => ResultCode.InvalidArgument,
                V3.ExrResult.InvalidFile => ResultCode.InvalidHeader,
                V3.ExrResult.Unsupported => ResultCode.UnsupportedFeature,
                V3.ExrResult.OutOfMemory => ResultCode.DataTooLarge,
                V3.ExrResult.IO => ResultCode.CannotWriteFile,
                V3.ExrResult.Corrupt => ResultCode.SerialzationFailed,
                _ => ResultCode.CannotWriteFile,
            };
        }

        private static ResultCode MapResult(V3.ExrResult result)
        {
            return result switch
            {
                V3.ExrResult.Success => ResultCode.Success,
                V3.ExrResult.InvalidArgument => ResultCode.InvalidArgument,
                V3.ExrResult.InvalidFile => ResultCode.InvalidFile,
                V3.ExrResult.Unsupported => ResultCode.UnsupportedFeature,
                V3.ExrResult.OutOfMemory => ResultCode.DataTooLarge,
                V3.ExrResult.IO => ResultCode.InvalidFile,
                V3.ExrResult.Corrupt => ResultCode.InvalidData,
                _ => ResultCode.InvalidData,
            };
        }

        private static bool HasRawAttribute(ParsedPartIndex part, string name)
        {
            for (int attributeIndex = 0; attributeIndex < part.RawAttributes.Count; attributeIndex++)
            {
                if (string.Equals(part.RawAttributes[attributeIndex].Value.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsV1ModeledAttribute(string name)
        {
            switch (name)
            {
                case "name":
                case "type":
                case "channels":
                case "compression":
                case "dataWindow":
                case "displayWindow":
                case "lineOrder":
                case "pixelAspectRatio":
                case "screenWindowCenter":
                case "screenWindowWidth":
                case "tiles":
                case "chunkCount":
                    return true;
                default:
                    return false;
            }
        }

        private static string ConvertPartType(V3.PartType partType)
        {
            return partType switch
            {
                V3.PartType.Scanline => "scanlineimage",
                V3.PartType.Tiled => "tiledimage",
                V3.PartType.DeepScanline => "deepscanline",
                V3.PartType.DeepTiled => "deeptile",
                _ => throw new ArgumentOutOfRangeException(nameof(partType)),
            };
        }

        private static ExrBox2i ConvertBox(V3.Box2i box)
        {
            return new ExrBox2i(box.MinX, box.MinY, box.MaxX, box.MaxY);
        }

        private static ExrTileDescription ConvertTiles(V3.TileDescription tiles)
        {
            return new ExrTileDescription
            {
                TileSizeX = checked((int)tiles.TileSizeX),
                TileSizeY = checked((int)tiles.TileSizeY),
                LevelMode = (ExrTileLevelMode)(int)tiles.LevelMode,
                RoundingMode = (ExrTileRoundingMode)(int)tiles.RoundingMode,
            };
        }

        private static ExrImage EmptyImage()
        {
            return new ExrImage(0, 0, Array.Empty<ExrImageChannel>());
        }

        private static ExrDeepImage EmptyDeepImage()
        {
            return new ExrDeepImage(
                0,
                0,
                Array.Empty<int[]>(),
                Array.Empty<ExrDeepChannel>());
        }

        private sealed class StreamWindowDataSource : V3IO.IExactDataSource
        {
            private readonly Stream _stream;
            private readonly long _origin;
            private readonly object _gate = new object();

            private StreamWindowDataSource(Stream stream, long origin, long length)
            {
                _stream = stream;
                _origin = origin;
                Length = length;
            }

            public bool HasKnownLength => true;

            public long Length { get; }

            public bool TryGetLength(out long length)
            {
                length = Length;
                return true;
            }

            public V3IO.DataTransferResult ReadExactly(long offset, Span<byte> destination)
            {
                if (offset < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }

                long end = checked(offset + destination.Length);
                lock (_gate)
                {
                    long previousPosition = 0;
                    bool restorePosition = false;
                    int bytesRead = 0;
                    try
                    {
                        previousPosition = _stream.Position;
                        restorePosition = true;
                        if (offset > Length || (offset == Length && destination.Length != 0))
                        {
                            return V3IO.DataTransferResult.EndOfSource(0);
                        }

                        int targetLength = destination.Length;
                        if (end > Length)
                        {
                            targetLength = checked((int)(Length - offset));
                        }

                        _stream.Seek(checked(_origin + offset), SeekOrigin.Begin);
                        while (bytesRead < targetLength)
                        {
                            int count = _stream.Read(destination.Slice(bytesRead, targetLength - bytesRead));
                            if (count == 0)
                            {
                                return V3IO.DataTransferResult.EndOfSource(bytesRead);
                            }

                            bytesRead = checked(bytesRead + count);
                        }

                        return targetLength == destination.Length
                            ? V3IO.DataTransferResult.Success(bytesRead)
                            : V3IO.DataTransferResult.EndOfSource(bytesRead);
                    }
                    catch (ObjectDisposedException exception)
                    {
                        return V3IO.DataTransferResult.Disposed(bytesRead, exception, isByteCountExact: false);
                    }
                    catch (IOException exception)
                    {
                        return V3IO.DataTransferResult.IoError(bytesRead, exception, isByteCountExact: false);
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException ||
                        exception is NotSupportedException ||
                        exception is InvalidOperationException)
                    {
                        return V3IO.DataTransferResult.IoError(bytesRead, exception, isByteCountExact: false);
                    }
                    finally
                    {
                        if (restorePosition && _stream.CanSeek)
                        {
                            try
                            {
                                _stream.Position = previousPosition;
                            }
                            catch (ArgumentException)
                            {
                            }
                            catch (IOException)
                            {
                            }
                            catch (NotSupportedException)
                            {
                            }
                            catch (ObjectDisposedException)
                            {
                            }
                        }
                    }
                }
            }

            public static bool TryCreate(Stream stream, out StreamWindowDataSource? source)
            {
                source = null;
                if (stream == null)
                {
                    return false;
                }

                try
                {
                    if (!stream.CanRead || !stream.CanSeek)
                    {
                        return false;
                    }

                    long origin = stream.Position;
                    long length = checked(stream.Length - origin);
                    if (origin < 0 || length < 0)
                    {
                        return false;
                    }

                    source = new StreamWindowDataSource(stream, origin, length);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (NotSupportedException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
                catch (IOException)
                {
                    return false;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }
        }
    }
}
