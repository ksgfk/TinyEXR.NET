using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using TinyEXR.PortV1;
using TinyEXR.V3.Codecs;

namespace TinyEXR.V3
{
    internal static class FlatBlockEncoder
    {
        public static byte[] Encode(
            WriterPartData part,
            BlockInfo info,
            IReadOnlyList<ChannelBuffer> channels,
            bool multipart,
            WriterLimits limits,
            ExrCompressionCodec.EncodeWorkspace workspace,
            ZstdCompressionEncoder zstdEncoder)
        {
            if (part.Header.IsDeep || info.IsDeep)
            {
                throw new WriterPlanException(
                    ExrResult.Unsupported,
                    "Deep block encoding is not implemented by the managed streaming writer yet.",
                    new NotSupportedException("Deep block encoding is not implemented."));
            }

            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            if (channels.Count != part.Header.Channels.Count)
            {
                throw Invalid("Every block must provide exactly one buffer for every header channel.");
            }

            Dictionary<string, ChannelBuffer> sources = new Dictionary<string, ChannelBuffer>(
                channels.Count,
                StringComparer.Ordinal);
            for (int i = 0; i < channels.Count; i++)
            {
                ChannelBuffer source = channels[i] ??
                    throw Invalid("The block channel collection contains a null buffer.");
                if (!sources.TryAdd(source.Name, source))
                {
                    throw Invalid($"Duplicate block channel buffer '{source.Name}'.");
                }
            }

            int rawLength;
            try
            {
                ulong uncompressedByteCount = info.UncompressedByteCount ??
                    throw new InvalidOperationException("A flat block has no uncompressed byte count.");
                Limit(
                    nameof(limits.MaximumUncompressedBlockByteCount),
                    checked((long)uncompressedByteCount),
                    limits.MaximumUncompressedBlockByteCount);
                rawLength = checked((int)uncompressedByteCount);
            }
            catch (OverflowException exception)
            {
                throw new WriterPlanException(
                    ExrResult.Unsupported,
                    "The flat block exceeds the managed address space.",
                    exception);
            }

            ChannelBuffer[] orderedSources = new ChannelBuffer[part.Header.Channels.Count];
            int[] sourceOffsets = new int[orderedSources.Length];
            for (int i = 0; i < orderedSources.Length; i++)
            {
                Channel expected = part.Header.Channels[i];
                if (!sources.TryGetValue(expected.Name, out ChannelBuffer? source))
                {
                    throw Invalid($"The block does not contain channel '{expected.Name}'.");
                }

                if (source.PixelType != expected.PixelType)
                {
                    throw Invalid(
                        $"Block channel '{source.Name}' has pixel type '{source.PixelType}', expected '{expected.PixelType}'.");
                }

                ulong expectedByteCount = checked(
                    ModelValidation.CountSamples(info.Region, expected.XSampling, expected.YSampling) *
                    (uint)ModelValidation.PixelTypeSize(expected.PixelType));
                if ((ulong)source.ByteLength != expectedByteCount)
                {
                    throw Invalid(
                        $"Block channel '{source.Name}' has {source.ByteLength} bytes; {expectedByteCount} bytes are required.");
                }

                orderedSources[i] = source;
            }

            byte[] raw = new byte[rawLength];
            int rawOffset = 0;
            for (long y = info.Region.MinY; y <= info.Region.MaxY; y++)
            {
                for (int channelIndex = 0; channelIndex < part.Header.Channels.Count; channelIndex++)
                {
                    Channel channel = part.Header.Channels[channelIndex];
                    if (y % channel.YSampling != 0)
                    {
                        continue;
                    }

                    int rowByteCount = checked(
                        (int)ModelValidation.CountSampleLocations(
                            info.Region.MinX,
                            info.Region.MaxX,
                            channel.XSampling) *
                        ModelValidation.PixelTypeSize(channel.PixelType));
                    orderedSources[channelIndex].Data.Slice(
                        sourceOffsets[channelIndex],
                        rowByteCount).CopyTo(raw.AsSpan(rawOffset, rowByteCount));
                    sourceOffsets[channelIndex] += rowByteCount;
                    rawOffset += rowByteCount;
                }
            }

            if (rawOffset != raw.Length)
            {
                throw new InvalidOperationException("The gathered block length does not match its canonical EXR size.");
            }

            for (int i = 0; i < orderedSources.Length; i++)
            {
                if (sourceOffsets[i] != orderedSources[i].ByteLength)
                {
                    throw new InvalidOperationException(
                        $"The gathered channel '{orderedSources[i].Name}' did not consume its complete buffer.");
                }
            }

            byte[] payload = EncodePayload(part, info, raw, workspace, zstdEncoder);
            if (payload.Length >= raw.Length &&
                part.Header.Compression != Compression.B44 &&
                part.Header.Compression != Compression.B44A &&
                !ReferenceEquals(payload, raw))
            {
                payload = raw;
            }

            Limit(
                nameof(limits.MaximumEncodedBlockByteCount),
                checked((long)info.ChunkHeaderByteCount + payload.Length),
                limits.MaximumEncodedBlockByteCount);

            byte[] chunk = new byte[checked(info.ChunkHeaderByteCount + payload.Length)];
            int offset = 0;
            if (multipart)
            {
                WriteInt32(chunk, ref offset, part.PartIndex);
            }

            if (info.IsTiled)
            {
                WriteInt32(chunk, ref offset, info.TileX);
                WriteInt32(chunk, ref offset, info.TileY);
                WriteInt32(chunk, ref offset, info.LevelX);
                WriteInt32(chunk, ref offset, info.LevelY);
            }
            else
            {
                WriteInt32(chunk, ref offset, info.Region.MinY);
            }

            WriteInt32(chunk, ref offset, payload.Length);
            if (offset != info.ChunkHeaderByteCount)
            {
                throw new InvalidOperationException("The encoded flat chunk header has an inconsistent size.");
            }

            payload.AsSpan().CopyTo(chunk.AsSpan(offset));
            return chunk;
        }

        private static byte[] EncodePayload(
            WriterPartData part,
            BlockInfo info,
            byte[] raw,
            ExrCompressionCodec.EncodeWorkspace workspace,
            ZstdCompressionEncoder zstdEncoder)
        {
            if (part.Header.Compression == Compression.ZSTD)
            {
                try
                {
                    return zstdEncoder.Encode(raw);
                }
                catch (ZstdCompressionException exception)
                {
                    throw new WriterPlanException(
                        ExrResult.Corrupt,
                        "The ZSTD encoder could not encode the flat block.",
                        exception);
                }
            }

            if (part.Header.Compression == Compression.HTJ2K256 ||
                part.Header.Compression == Compression.HTJ2K32)
            {
                Htj2kEncodeStatus status = Htj2kDecoder.Encode(
                    part.Header,
                    info.Region,
                    raw,
                    out byte[] htj2kPayload,
                    out string? error);
                switch (status)
                {
                    case Htj2kEncodeStatus.Success:
                        return htj2kPayload;
                    case Htj2kEncodeStatus.InvalidArgument:
                        throw Invalid(error ?? "The HTJ2K encoder rejected the flat block.");
                    case Htj2kEncodeStatus.Unsupported:
                        throw new WriterPlanException(
                            ExrResult.Unsupported,
                            error ?? "The flat block is outside the supported HTJ2K profile.",
                            new NotSupportedException(error));
                    default:
                        throw new WriterPlanException(
                            ExrResult.Corrupt,
                            error ?? "The HTJ2K encoder could not encode the flat block.");
                }
            }

            if (part.Header.Compression > Compression.B44A)
            {
                throw new WriterPlanException(
                    ExrResult.Unsupported,
                    $"Compression '{part.Header.Compression}' has no managed flat encoder.");
            }

            ResultCode result = ExrCompressionCodec.TryEncodePayload(
                (CompressionType)(int)part.Header.Compression,
                part.CodecChannels,
                info.Region.MinX,
                info.Region.MinY,
                checked((int)info.Region.Width),
                checked((int)info.Region.Height),
                raw,
                workspace,
                out byte[] payload);
            switch (result)
            {
                case ResultCode.Success:
                    return payload;
                case ResultCode.UnsupportedFeature:
                case ResultCode.UnsupportedFormat:
                    return raw;
                case ResultCode.DataTooLarge:
                    throw new WriterPlanException(
                        ExrResult.Unsupported,
                        "The block is too large for the selected compression codec.");
                default:
                    throw Invalid($"The selected compression codec rejected the block ({result}).");
            }
        }

        private static void WriteInt32(byte[] destination, ref int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset, sizeof(int)), value);
            offset += sizeof(int);
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
    }
}
