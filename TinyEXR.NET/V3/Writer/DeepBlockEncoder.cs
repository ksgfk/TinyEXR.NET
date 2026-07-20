using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using TinyEXR.PortV1;
using TinyEXR.V3.Codecs;

namespace TinyEXR.V3
{
    internal readonly struct DeepEncodedBlock
    {
        public DeepEncodedBlock(byte[] chunk, int maximumSamplesPerPixel)
        {
            Chunk = chunk;
            MaximumSamplesPerPixel = maximumSamplesPerPixel;
        }

        public byte[] Chunk { get; }

        public int MaximumSamplesPerPixel { get; }
    }

    internal static class DeepBlockEncoder
    {
        public static DeepEncodedBlock Encode(
            WriterPartData part,
            BlockInfo info,
            ReadOnlySpan<int> counts,
            IReadOnlyList<ChannelBuffer> channels,
            bool multipart,
            WriterLimits limits,
            ExrCompressionCodec.EncodeWorkspace workspace,
            ZstdCompressionEncoder zstdEncoder)
        {
            if (!part.Header.IsDeep || !info.IsDeep)
            {
                throw Invalid("A deep encoder operation requires a deep part.");
            }

            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            int pixelCount;
            try
            {
                pixelCount = checked((int)ModelValidation.CountPixels(info.Region));
            }
            catch (OverflowException exception)
            {
                throw Unsupported("The deep block pixel count exceeds the managed address space.", exception);
            }

            if (counts.Length != pixelCount)
            {
                throw Invalid(
                    $"The deep count buffer has {counts.Length} entries; {pixelCount} entries are required.");
            }

            if (channels.Count != part.Header.Channels.Count)
            {
                throw Invalid("Every deep block must provide exactly one buffer for every header channel.");
            }

            ulong totalSamples = 0;
            int maximumSamplesPerPixel = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                int count = counts[i];
                if (count < 0)
                {
                    throw Invalid("Deep sample counts must be non-negative.");
                }

                totalSamples = checked(totalSamples + (uint)count);
                maximumSamplesPerPixel = Math.Max(maximumSamplesPerPixel, count);
            }

            Limit(
                nameof(limits.MaximumDeepSampleCount),
                checked((long)totalSamples),
                limits.MaximumDeepSampleCount);

            Dictionary<string, ChannelBuffer> sources = new Dictionary<string, ChannelBuffer>(
                channels.Count,
                StringComparer.Ordinal);
            for (int i = 0; i < channels.Count; i++)
            {
                ChannelBuffer source = channels[i] ??
                    throw Invalid("The deep block channel collection contains a null buffer.");
                if (!sources.TryAdd(source.Name, source))
                {
                    throw Invalid($"Duplicate deep block channel buffer '{source.Name}'.");
                }
            }

            ChannelBuffer[] orderedSources = new ChannelBuffer[part.Header.Channels.Count];
            int samplePayloadLength = 0;
            for (int channelIndex = 0; channelIndex < part.Header.Channels.Count; channelIndex++)
            {
                Channel expected = part.Header.Channels[channelIndex];
                if (!sources.TryGetValue(expected.Name, out ChannelBuffer? source))
                {
                    throw Invalid($"The deep block does not contain channel '{expected.Name}'.");
                }

                if (source.PixelType != expected.PixelType)
                {
                    throw Invalid(
                        $"Deep block channel '{source.Name}' has pixel type '{source.PixelType}', expected '{expected.PixelType}'.");
                }

                ulong expectedByteCount = checked(
                    totalSamples * (uint)ModelValidation.PixelTypeSize(expected.PixelType));
                if ((ulong)source.ByteLength != expectedByteCount)
                {
                    throw Invalid(
                        $"Deep block channel '{source.Name}' has {source.ByteLength} bytes; {expectedByteCount} bytes are required.");
                }

                samplePayloadLength = checked(samplePayloadLength + source.ByteLength);
                orderedSources[channelIndex] = source;
            }

            int countPayloadLength = checked(pixelCount * sizeof(int));
            Limit(
                nameof(limits.MaximumUncompressedBlockByteCount),
                checked((long)countPayloadLength + samplePayloadLength),
                limits.MaximumUncompressedBlockByteCount);

            int width = checked((int)info.Region.Width);
            int height = checked((int)info.Region.Height);
            byte[] countRaw = new byte[countPayloadLength];
            int countTarget = 0;
            int pixelIndex = 0;
            for (int row = 0; row < height; row++)
            {
                long cumulative = 0;
                for (int x = 0; x < width; x++)
                {
                    cumulative = checked(cumulative + counts[pixelIndex++]);
                    if (cumulative > int.MaxValue)
                    {
                        throw Unsupported(
                            "A deep block scanline has more samples than the EXR cumulative count table can represent.");
                    }

                    BinaryPrimitives.WriteInt32LittleEndian(
                        countRaw.AsSpan(countTarget, sizeof(int)),
                        (int)cumulative);
                    countTarget += sizeof(int);
                }
            }

            int[] sourceOffsets = new int[orderedSources.Length];
            byte[] sampleRaw = new byte[samplePayloadLength];
            int sampleTarget = 0;
            pixelIndex = 0;
            for (int row = 0; row < height; row++)
            {
                long rowSamples = 0;
                for (int x = 0; x < width; x++)
                {
                    rowSamples = checked(rowSamples + counts[pixelIndex++]);
                }

                for (int channelIndex = 0; channelIndex < orderedSources.Length; channelIndex++)
                {
                    int rowByteCount = checked(
                        (int)rowSamples *
                        ModelValidation.PixelTypeSize(part.Header.Channels[channelIndex].PixelType));
                    orderedSources[channelIndex].Data.Slice(
                        sourceOffsets[channelIndex],
                        rowByteCount).CopyTo(sampleRaw.AsSpan(sampleTarget, rowByteCount));
                    sourceOffsets[channelIndex] += rowByteCount;
                    sampleTarget += rowByteCount;
                }
            }

            if (countTarget != countRaw.Length || sampleTarget != sampleRaw.Length)
            {
                throw new InvalidOperationException("The encoded deep payload has an inconsistent size.");
            }

            for (int channelIndex = 0; channelIndex < orderedSources.Length; channelIndex++)
            {
                if (sourceOffsets[channelIndex] != orderedSources[channelIndex].ByteLength)
                {
                    throw new InvalidOperationException(
                        $"The encoded deep channel '{orderedSources[channelIndex].Name}' did not consume its complete buffer.");
                }
            }

            byte[] packedCounts = countRaw;
            byte[] packedSamples = sampleRaw;
            if (part.Header.Compression == Compression.ZSTD)
            {
                try
                {
                    packedCounts = zstdEncoder.Encode(countRaw);
                    packedSamples = zstdEncoder.Encode(sampleRaw);
                }
                catch (ZstdCompressionException exception)
                {
                    throw new WriterPlanException(
                        ExrResult.Corrupt,
                        "The ZSTD encoder could not encode the deep block.",
                        exception);
                }
            }
            else if (part.Header.Compression == Compression.RLE ||
                part.Header.Compression == Compression.ZIPS ||
                part.Header.Compression == Compression.ZIP)
            {
                packedCounts = EncodeLegacyPayload(part.Header.Compression, countRaw, workspace);
                packedSamples = EncodeLegacyPayload(part.Header.Compression, sampleRaw, workspace);
            }

            int chunkLength = checked(
                info.ChunkHeaderByteCount + packedCounts.Length + packedSamples.Length);
            Limit(
                nameof(limits.MaximumEncodedBlockByteCount),
                chunkLength,
                limits.MaximumEncodedBlockByteCount);

            byte[] chunk = new byte[chunkLength];
            int headerOffset = 0;
            if (multipart)
            {
                WriteInt32(chunk, ref headerOffset, part.PartIndex);
            }

            if (info.IsTiled)
            {
                WriteInt32(chunk, ref headerOffset, info.TileX);
                WriteInt32(chunk, ref headerOffset, info.TileY);
                WriteInt32(chunk, ref headerOffset, info.LevelX);
                WriteInt32(chunk, ref headerOffset, info.LevelY);
            }
            else
            {
                WriteInt32(chunk, ref headerOffset, info.Region.MinY);
            }

            WriteUInt64(chunk, ref headerOffset, checked((ulong)packedCounts.Length));
            WriteUInt64(chunk, ref headerOffset, checked((ulong)packedSamples.Length));
            WriteUInt64(chunk, ref headerOffset, checked((ulong)sampleRaw.Length));
            if (headerOffset != info.ChunkHeaderByteCount)
            {
                throw new InvalidOperationException("The encoded deep chunk header has an inconsistent size.");
            }

            packedCounts.AsSpan().CopyTo(chunk.AsSpan(headerOffset));
            packedSamples.AsSpan().CopyTo(chunk.AsSpan(headerOffset + packedCounts.Length));

            return new DeepEncodedBlock(chunk, maximumSamplesPerPixel);
        }

        private static byte[] EncodeLegacyPayload(
            Compression compression,
            byte[] raw,
            ExrCompressionCodec.EncodeWorkspace workspace)
        {
            ResultCode result = ExrCompressionCodec.TryEncodeDeepPayload(
                (CompressionType)(int)compression,
                raw,
                workspace,
                out byte[] payload);
            switch (result)
            {
                case ResultCode.Success:
                    return payload;
                case ResultCode.DataTooLarge:
                    throw Unsupported("The deep payload is too large for the selected compression codec.");
                case ResultCode.UnsupportedFeature:
                case ResultCode.UnsupportedFormat:
                    throw Unsupported($"Compression '{compression}' is not supported for deep payloads.");
                default:
                    throw Invalid($"The selected deep compression codec rejected the payload ({result}).");
            }
        }

        private static void WriteInt32(byte[] destination, ref int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset, sizeof(int)), value);
            offset += sizeof(int);
        }

        private static void WriteUInt64(byte[] destination, ref int offset, ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination.AsSpan(offset, sizeof(ulong)), value);
            offset += sizeof(ulong);
        }

        private static void Limit(string name, long actual, long maximum)
        {
            if (actual > maximum)
            {
                throw new WriterPlanException(
                    ExrResult.Unsupported,
                    $"Writer limit '{name}' was exceeded.",
                    new WriterLimitExceededException(name, actual, maximum));
            }
        }

        private static WriterPlanException Invalid(string message)
        {
            return new WriterPlanException(
                ExrResult.InvalidArgument,
                message,
                new ArgumentException(message));
        }

        private static WriterPlanException Unsupported(string message, Exception? innerException = null)
        {
            return new WriterPlanException(
                ExrResult.Unsupported,
                message,
                innerException ?? new NotSupportedException(message));
        }
    }
}
