using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace TinyEXR.V3
{
    internal sealed class DeepBlockOperation
    {
        private readonly Header _header;
        private readonly bool _multipart;
        private readonly ReaderLimits _limits;
        private readonly byte[] _chunkHeader;
        private readonly int _pixelCount;
        private readonly int _countByteCount;
        private readonly int _sampleStride;

        private int _chunkHeaderOffset;
        private byte[]? _offsetPayload;
        private int _offsetPayloadOffset;
        private byte[]? _samplePayload;
        private int _samplePayloadOffset;
        private int _packedOffsetByteCount;
        private int _packedSampleByteCount;
        private int _unpackedSampleByteCount;
        private int[]? _counts;
        private long _totalSamples;

        public DeepBlockOperation(
            Header header,
            BlockInfo info,
            bool multipart,
            ReaderLimits limits)
        {
            _header = header;
            Info = info;
            _multipart = multipart;
            _limits = limits;
            _chunkHeader = new byte[info.ChunkHeaderByteCount];
            _pixelCount = checked((int)checked(info.Region.Width * info.Region.Height));
            _countByteCount = checked(_pixelCount * sizeof(int));

            int sampleStride = 0;
            for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
            {
                sampleStride = checked(
                    sampleStride + ModelValidation.PixelTypeSize(header.Channels[channelIndex].PixelType));
            }

            _sampleStride = sampleStride;
        }

        public BlockInfo Info { get; }

        public int PixelCount => _pixelCount;

        public bool HeaderComplete => _chunkHeaderOffset == _chunkHeader.Length;

        public bool HeaderValidated => _offsetPayload != null;

        public bool OffsetPayloadComplete =>
            _offsetPayload != null && _offsetPayloadOffset == _offsetPayload.Length;

        public bool CountsDecoded => _counts != null;

        public long TotalSamples => _counts != null
            ? _totalSamples
            : throw new InvalidOperationException("The deep sample counts have not been decoded.");

        public ReadOnlySpan<int> DecodedCounts => _counts ??
            throw new InvalidOperationException("The deep sample counts have not been decoded.");

        public ReadOnlyMemory<int> DecodedCountMemory => _counts ??
            throw new InvalidOperationException("The deep sample counts have not been decoded.");

        public bool SamplePayloadStarted => _samplePayload != null;

        public bool SamplePayloadComplete =>
            _samplePayload != null && _samplePayloadOffset == _samplePayload.Length;

        public ReaderParserRequest GetNextCountsRequest()
        {
            if (!HeaderComplete)
            {
                int length = Math.Min(
                    _chunkHeader.Length - _chunkHeaderOffset,
                    _limits.MaximumReadRequestByteCount);
                return new ReaderParserRequest(
                    checked(Info.FileOffset + _chunkHeaderOffset),
                    _chunkHeader,
                    _chunkHeaderOffset,
                    length);
            }

            if (_offsetPayload == null)
            {
                throw new InvalidOperationException("The deep chunk header has not been validated.");
            }

            if (OffsetPayloadComplete)
            {
                throw new InvalidOperationException("The deep count payload is already complete.");
            }

            int payloadLength = Math.Min(
                _offsetPayload.Length - _offsetPayloadOffset,
                _limits.MaximumReadRequestByteCount);
            return new ReaderParserRequest(
                checked(Info.FileOffset + _chunkHeader.Length + _offsetPayloadOffset),
                _offsetPayload,
                _offsetPayloadOffset,
                payloadLength);
        }

        public ReaderParserRequest GetNextSamplesRequest()
        {
            if (_samplePayload == null)
            {
                throw new InvalidOperationException("The deep sample payload has not been started.");
            }

            if (SamplePayloadComplete)
            {
                throw new InvalidOperationException("The deep sample payload is already complete.");
            }

            int length = Math.Min(
                _samplePayload.Length - _samplePayloadOffset,
                _limits.MaximumReadRequestByteCount);
            return new ReaderParserRequest(
                checked(
                    Info.FileOffset +
                    _chunkHeader.Length +
                    _packedOffsetByteCount +
                    _samplePayloadOffset),
                _samplePayload,
                _samplePayloadOffset,
                length);
        }

        public void AcceptCountsRequest(int byteCount)
        {
            if (!HeaderComplete)
            {
                _chunkHeaderOffset = checked(_chunkHeaderOffset + byteCount);
                return;
            }

            if (_offsetPayload == null)
            {
                throw new InvalidOperationException("The deep chunk header has not been validated.");
            }

            _offsetPayloadOffset = checked(_offsetPayloadOffset + byteCount);
        }

        public void AcceptSamplesRequest(int byteCount)
        {
            if (_samplePayload == null)
            {
                throw new InvalidOperationException("The deep sample payload has not been started.");
            }

            _samplePayloadOffset = checked(_samplePayloadOffset + byteCount);
        }

        public ReaderResult? ValidateHeader(long? knownLength)
        {
            if (!HeaderComplete || _offsetPayload != null)
            {
                throw new InvalidOperationException("The deep chunk header is not ready for validation.");
            }

            int offset = 0;
            if (_multipart)
            {
                int partNumber = ReadInt32(ref offset);
                if (partNumber != Info.PartIndex)
                {
                    return Corrupt("The multipart deep chunk identifies a different part.");
                }
            }

            if (Info.IsTiled)
            {
                int tileX = ReadInt32(ref offset);
                int tileY = ReadInt32(ref offset);
                int levelX = ReadInt32(ref offset);
                int levelY = ReadInt32(ref offset);
                if (tileX != Info.TileX || tileY != Info.TileY ||
                    levelX != Info.LevelX || levelY != Info.LevelY)
                {
                    return Corrupt("The deep tile coordinates do not match its offset-table index.");
                }
            }
            else
            {
                int minimumY = ReadInt32(ref offset);
                if (minimumY != Info.Region.MinY)
                {
                    return Corrupt("The deep scanline coordinate does not match its offset-table index.");
                }
            }

            long packedOffsetByteCount = ReadInt64(ref offset);
            long packedSampleByteCount = ReadInt64(ref offset);
            long unpackedSampleByteCount = ReadInt64(ref offset);
            if (offset != _chunkHeader.Length ||
                packedOffsetByteCount <= 0 ||
                packedSampleByteCount < 0 ||
                unpackedSampleByteCount < 0 ||
                (packedSampleByteCount == 0 && unpackedSampleByteCount != 0))
            {
                return Corrupt("The deep chunk contains invalid payload sizes.");
            }

            long compressedByteCount = checked(packedOffsetByteCount + packedSampleByteCount);
            if (compressedByteCount > _limits.MaximumCompressedBlockByteCount)
            {
                return Limit(
                    nameof(ReaderLimits.MaximumCompressedBlockByteCount),
                    compressedByteCount,
                    _limits.MaximumCompressedBlockByteCount);
            }

            long uncompressedByteCount = checked((long)_countByteCount + unpackedSampleByteCount);
            if (uncompressedByteCount > _limits.MaximumUncompressedBlockByteCount)
            {
                return Limit(
                    nameof(ReaderLimits.MaximumUncompressedBlockByteCount),
                    uncompressedByteCount,
                    _limits.MaximumUncompressedBlockByteCount);
            }

            if (_header.Compression == Compression.None &&
                (packedOffsetByteCount != _countByteCount ||
                    packedSampleByteCount != unpackedSampleByteCount))
            {
                return Corrupt("An uncompressed deep chunk does not match its declared byte counts.");
            }

            long payloadEnd = checked(
                Info.FileOffset +
                _chunkHeader.Length +
                compressedByteCount);
            if (knownLength.HasValue && payloadEnd > knownLength.Value)
            {
                return Corrupt("The deep chunk payload extends past the source length.");
            }

            _packedOffsetByteCount = checked((int)packedOffsetByteCount);
            _packedSampleByteCount = checked((int)packedSampleByteCount);
            _unpackedSampleByteCount = checked((int)unpackedSampleByteCount);
            _offsetPayload = new byte[checked((int)packedOffsetByteCount)];
            _offsetPayloadOffset = 0;
            return null;
        }

        public ReaderResult DecodeCounts(Span<int> destination)
        {
            if (!OffsetPayloadComplete)
            {
                throw new InvalidOperationException("The deep count payload is incomplete.");
            }

            if (_counts != null)
            {
                return CopyCounts(destination);
            }

            ReaderResult? decodeFailure = DeepPayloadDecoder.Decode(
                _header.Compression,
                _offsetPayload!,
                _countByteCount,
                out byte[] decodedOffsets);
            if (decodeFailure.HasValue)
            {
                return decodeFailure.Value;
            }

            int width = checked((int)Info.Region.Width);
            int height = checked((int)Info.Region.Height);
            int[] decodedCounts = new int[_pixelCount];
            long totalSamples = 0;
            int countIndex = 0;
            for (int row = 0; row < height; row++)
            {
                int previous = 0;
                for (int x = 0; x < width; x++)
                {
                    int cumulative = BinaryPrimitives.ReadInt32LittleEndian(
                        decodedOffsets.AsSpan(countIndex * sizeof(int), sizeof(int)));
                    if (cumulative < previous)
                    {
                        return Corrupt("A deep sample-count row is negative or non-monotonic.");
                    }

                    int count = cumulative - previous;
                    decodedCounts[countIndex] = count;
                    totalSamples = checked(totalSamples + count);
                    if (totalSamples > _limits.MaximumDeepSampleCount)
                    {
                        return Limit(
                            nameof(ReaderLimits.MaximumDeepSampleCount),
                            totalSamples,
                            _limits.MaximumDeepSampleCount);
                    }

                    previous = cumulative;
                    countIndex++;
                }
            }

            long expectedSampleByteCount = checked(totalSamples * _sampleStride);
            if (expectedSampleByteCount != _unpackedSampleByteCount)
            {
                return Corrupt(
                    "The deep chunk's unpacked sample byte count does not match its sample-count table.");
            }

            _counts = decodedCounts;
            _totalSamples = totalSamples;
            _offsetPayload = Array.Empty<byte>();
            _offsetPayloadOffset = 0;
            return CopyCounts(destination);
        }

        public ReaderResult CopyCounts(Span<int> destination)
        {
            if (_counts == null)
            {
                throw new InvalidOperationException("The deep sample counts have not been decoded.");
            }

            _counts.AsSpan().CopyTo(destination);
            return new ReaderResult(ExrResult.Success, null, null, _countByteCount);
        }

        public void ValidateSamplesArguments(
            ReadOnlySpan<int> counts,
            IReadOnlyList<DeepChannelDestination> destinations)
        {
            if (_counts == null)
            {
                throw new InvalidOperationException(
                    "DecodeDeepCounts must complete successfully for this block before samples are decoded.");
            }

            if (counts.Length < _pixelCount)
            {
                throw new ArgumentException(
                    $"The count buffer contains {counts.Length} entries; {_pixelCount} entries are required.",
                    nameof(counts));
            }

            for (int index = 0; index < _pixelCount; index++)
            {
                if (counts[index] != _counts[index])
                {
                    throw new ArgumentException(
                        "The supplied counts differ from the counts decoded for this block.",
                        nameof(counts));
                }
            }

            if (destinations == null)
            {
                throw new ArgumentNullException(nameof(destinations));
            }

            if (destinations.Count != _header.Channels.Count)
            {
                throw new ArgumentException(
                    $"The destination list contains {destinations.Count} channels; {_header.Channels.Count} channels are required.",
                    nameof(destinations));
            }

            for (int channelIndex = 0; channelIndex < _header.Channels.Count; channelIndex++)
            {
                DeepChannelDestination? destination = destinations[channelIndex];
                if (destination == null)
                {
                    throw new ArgumentException(
                        "The destination list must not contain null entries.",
                        nameof(destinations));
                }

                Channel channel = _header.Channels[channelIndex];
                if (!string.Equals(destination.Name, channel.Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Destination channel {channelIndex} must be named '{channel.Name}'.",
                        nameof(destinations));
                }

                int required = checked(
                    (int)checked(_totalSamples * ModelValidation.PixelTypeSize(channel.PixelType)));
                if (destination.Data.Length < required)
                {
                    throw new ArgumentException(
                        $"Destination channel '{channel.Name}' contains {destination.Data.Length} bytes; {required} bytes are required.",
                        nameof(destinations));
                }
            }
        }

        public ReaderResult? ValidateKnownLength(long? knownLength)
        {
            if (!knownLength.HasValue)
            {
                return null;
            }

            long payloadEnd = checked(
                Info.FileOffset +
                _chunkHeader.Length +
                _packedOffsetByteCount +
                _packedSampleByteCount);
            return payloadEnd <= knownLength.Value
                ? null
                : Corrupt("The deep chunk payload extends past the source length.");
        }

        public void BeginSamples()
        {
            if (_counts == null)
            {
                throw new InvalidOperationException("The deep sample counts have not been decoded.");
            }

            if (_samplePayload == null)
            {
                _samplePayload = _packedSampleByteCount == 0
                    ? Array.Empty<byte>()
                    : new byte[_packedSampleByteCount];
                _samplePayloadOffset = 0;
            }
        }

        public ReaderResult DecodeSamples(IReadOnlyList<DeepChannelDestination> destinations)
        {
            if (!SamplePayloadComplete || _counts == null)
            {
                throw new InvalidOperationException("The deep sample payload is incomplete.");
            }

            ReaderResult? decodeFailure = DeepPayloadDecoder.Decode(
                _header.Compression,
                _samplePayload!,
                _unpackedSampleByteCount,
                out byte[] decodedSamples);
            if (decodeFailure.HasValue)
            {
                return decodeFailure.Value;
            }

            int channelCount = _header.Channels.Count;
            int[] channelOffsets = new int[channelCount];
            int planarByteCount = 0;
            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                channelOffsets[channelIndex] = planarByteCount;
                planarByteCount = checked(
                    planarByteCount +
                    checked((int)checked(
                        _totalSamples *
                        ModelValidation.PixelTypeSize(_header.Channels[channelIndex].PixelType))));
            }

            if (planarByteCount != _unpackedSampleByteCount)
            {
                return Corrupt("The deep channel layout does not match the decoded sample byte count.");
            }

            byte[] planar = new byte[planarByteCount];
            int width = checked((int)Info.Region.Width);
            int height = checked((int)Info.Region.Height);
            int countIndex = 0;
            int sourceOffset = 0;
            long destinationSampleOffset = 0;
            for (int row = 0; row < height; row++)
            {
                long rowSamples = 0;
                for (int x = 0; x < width; x++)
                {
                    rowSamples = checked(rowSamples + _counts[countIndex++]);
                }

                for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
                {
                    int elementSize = ModelValidation.PixelTypeSize(
                        _header.Channels[channelIndex].PixelType);
                    int rowByteCount = checked((int)checked(rowSamples * elementSize));
                    int destinationOffset = checked(
                        channelOffsets[channelIndex] +
                        checked((int)checked(destinationSampleOffset * elementSize)));
                    decodedSamples.AsSpan(sourceOffset, rowByteCount).CopyTo(
                        planar.AsSpan(destinationOffset, rowByteCount));
                    sourceOffset = checked(sourceOffset + rowByteCount);
                }

                destinationSampleOffset = checked(destinationSampleOffset + rowSamples);
            }

            if (sourceOffset != decodedSamples.Length || destinationSampleOffset != _totalSamples)
            {
                return Corrupt("The decoded deep sample payload has an inconsistent layout.");
            }

            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                int length = channelIndex + 1 < channelCount
                    ? channelOffsets[channelIndex + 1] - channelOffsets[channelIndex]
                    : planar.Length - channelOffsets[channelIndex];
                planar.AsMemory(channelOffsets[channelIndex], length).CopyTo(
                    destinations[channelIndex].Data);
            }

            return new ReaderResult(
                ExrResult.Success,
                null,
                null,
                _unpackedSampleByteCount);
        }

        private int ReadInt32(ref int offset)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(
                _chunkHeader.AsSpan(offset, sizeof(int)));
            offset += sizeof(int);
            return value;
        }

        private long ReadInt64(ref int offset)
        {
            long value = BinaryPrimitives.ReadInt64LittleEndian(
                _chunkHeader.AsSpan(offset, sizeof(long)));
            offset += sizeof(long);
            return value;
        }

        private static ReaderResult Corrupt(string message)
        {
            return new ReaderResult(
                ExrResult.Corrupt,
                null,
                new InvalidOperationException(message));
        }

        private static ReaderResult Limit(string name, long actual, long maximum)
        {
            return new ReaderResult(
                ExrResult.Unsupported,
                null,
                new ReaderLimitExceededException(name, actual, maximum));
        }
    }
}
