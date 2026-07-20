using System;
using System.Buffers.Binary;
using System.IO;
using TinyEXR.V3.Format;

namespace TinyEXR.V3
{
    internal sealed class ChunkOffsetReconstructionOperation
    {
        private readonly ReaderFileData _sourceData;
        private readonly ReaderLimits _limits;
        private readonly ulong[][] _candidateOffsets;
        private readonly byte[] _chunkHeader;
        private readonly int _totalChunkCount;

        private long _chunkOffset;
        private int _chunksAccepted;
        private int _partIndex;
        private int _headerOffset;
        private int _headerTarget;

        public ChunkOffsetReconstructionOperation(ReaderFileData sourceData, ReaderLimits limits)
        {
            _sourceData = sourceData;
            _limits = limits;
            _candidateOffsets = new ulong[sourceData.Parts.Length][];
            int maximumHeaderByteCount = sourceData.Multipart ? sizeof(int) : 0;
            int totalChunkCount = 0;
            for (int partIndex = 0; partIndex < sourceData.Parts.Length; partIndex++)
            {
                ReaderPartData part = sourceData.Parts[partIndex];
                _candidateOffsets[partIndex] = new ulong[part.Offsets.Length];
                totalChunkCount = checked(totalChunkCount + part.Offsets.Length);
                maximumHeaderByteCount = Math.Max(
                    maximumHeaderByteCount,
                    ExrFormatParser.ChunkHeaderByteCount(part.Header, sourceData.Multipart));
            }

            _totalChunkCount = totalChunkCount;
            _chunkHeader = new byte[maximumHeaderByteCount];
            _chunkOffset = sourceData.PixelDataStart;
            _partIndex = sourceData.Multipart ? -1 : 0;
            _headerTarget = sourceData.Multipart
                ? sizeof(int)
                : ExrFormatParser.ChunkHeaderByteCount(sourceData.Parts[0].Header, multipart: false);
        }

        public bool IsComplete => ReconstructedData != null;

        public ReaderFileData SourceData => _sourceData;

        public ReaderFileData? ReconstructedData { get; private set; }

        public ReaderParserRequest GetNextRequest()
        {
            if (IsComplete)
            {
                throw new InvalidOperationException("The chunk offset table has already been reconstructed.");
            }

            int length = Math.Min(
                _headerTarget - _headerOffset,
                _limits.MaximumReadRequestByteCount);
            return new ReaderParserRequest(
                checked(_chunkOffset + _headerOffset),
                _chunkHeader,
                _headerOffset,
                length);
        }

        public void AcceptRequest(int byteCount)
        {
            if (byteCount <= 0 || byteCount > _headerTarget - _headerOffset)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }

            _headerOffset = checked(_headerOffset + byteCount);
        }

        public ReaderResult? Advance(long? knownLength)
        {
            try
            {
                long headerEnd = checked(_chunkOffset + _headerTarget);
                if (knownLength.HasValue && headerEnd > knownLength.Value)
                {
                    return Corrupt("The source ended inside a chunk header while reconstructing offsets.");
                }

                if (_headerOffset != _headerTarget)
                {
                    return null;
                }

                if (_sourceData.Multipart && _partIndex < 0)
                {
                    int partIndex = BinaryPrimitives.ReadInt32LittleEndian(_chunkHeader);
                    if ((uint)partIndex >= (uint)_sourceData.Parts.Length)
                    {
                        return Corrupt("A multipart chunk contains an invalid part number.");
                    }

                    _partIndex = partIndex;
                    _headerTarget = ExrFormatParser.ChunkHeaderByteCount(
                        _sourceData.Parts[partIndex].Header,
                        multipart: true);
                    return null;
                }

                ReaderResult? validation = AcceptChunk(knownLength);
                if (validation.HasValue)
                {
                    return validation.Value;
                }

                if (_chunksAccepted == _totalChunkCount)
                {
                    ReaderPartData[] reconstructedParts = new ReaderPartData[_sourceData.Parts.Length];
                    for (int partIndex = 0; partIndex < reconstructedParts.Length; partIndex++)
                    {
                        ReaderPartData sourcePart = _sourceData.Parts[partIndex];
                        ulong[] offsets = _candidateOffsets[partIndex];
                        for (int blockIndex = 0; blockIndex < offsets.Length; blockIndex++)
                        {
                            if (offsets[blockIndex] == 0)
                            {
                                return Corrupt("The physical chunk stream does not contain every logical block.");
                            }
                        }

                        reconstructedParts[partIndex] = new ReaderPartData(
                            sourcePart.PartIndex,
                            sourcePart.Header,
                            offsets,
                            sourcePart.Layout,
                            sourcePart.HeaderEnd,
                            sourcePart.HasNameAttribute,
                            sourcePart.HasTypeAttribute,
                            sourcePart.RawAttributes);
                    }

                    ReconstructedData = new ReaderFileData(
                        _sourceData.RawVersionField,
                        _sourceData.Multipart,
                        _sourceData.PixelDataStart,
                        reconstructedParts);
                    return null;
                }

                Array.Clear(_chunkHeader, 0, _headerTarget);
                _headerOffset = 0;
                _partIndex = _sourceData.Multipart ? -1 : 0;
                _headerTarget = _sourceData.Multipart
                    ? sizeof(int)
                    : ExrFormatParser.ChunkHeaderByteCount(
                        _sourceData.Parts[0].Header,
                        multipart: false);
                return null;
            }
            catch (OverflowException exception)
            {
                return new ReaderResult(ExrResult.Corrupt, null, exception);
            }
        }

        private ReaderResult? AcceptChunk(long? knownLength)
        {
            ReaderPartData part = _sourceData.Parts[_partIndex];
            Header header = part.Header;
            int offset = _sourceData.Multipart ? sizeof(int) : 0;
            int blockIndex;
            if (header.IsTiled)
            {
                int tileX = ReadInt32(ref offset);
                int tileY = ReadInt32(ref offset);
                int levelX = ReadInt32(ref offset);
                int levelY = ReadInt32(ref offset);
                if (!part.Layout.TryGetTiledBlockIndex(
                    tileX,
                    tileY,
                    levelX,
                    levelY,
                    out blockIndex))
                {
                    return Corrupt("A tiled chunk contains coordinates outside the part layout.");
                }
            }
            else
            {
                int minimumY = ReadInt32(ref offset);
                if (!part.Layout.TryGetScanlineBlockIndex(minimumY, out blockIndex))
                {
                    return Corrupt("A scanline chunk contains a coordinate outside the part layout.");
                }
            }

            long payloadByteCount;
            if (header.IsDeep)
            {
                long packedSampleCountByteCount = ReadInt64(ref offset);
                long packedSampleByteCount = ReadInt64(ref offset);
                long unpackedSampleByteCount = ReadInt64(ref offset);
                if (packedSampleCountByteCount < 0 ||
                    packedSampleByteCount < 0 ||
                    unpackedSampleByteCount < 0 ||
                    (packedSampleCountByteCount == 0 &&
                        (packedSampleByteCount != 0 || unpackedSampleByteCount != 0)) ||
                    (packedSampleByteCount == 0 && unpackedSampleByteCount != 0))
                {
                    return Corrupt("A deep chunk contains inconsistent payload sizes.");
                }

                payloadByteCount = checked(packedSampleCountByteCount + packedSampleByteCount);
                ReaderResult? compressedLimit = EnforceLimit(
                    nameof(ReaderLimits.MaximumCompressedBlockByteCount),
                    payloadByteCount,
                    _limits.MaximumCompressedBlockByteCount);
                if (compressedLimit.HasValue)
                {
                    return compressedLimit.Value;
                }

                ReaderResult? uncompressedLimit = EnforceLimit(
                    nameof(ReaderLimits.MaximumUncompressedBlockByteCount),
                    unpackedSampleByteCount,
                    _limits.MaximumUncompressedBlockByteCount);
                if (uncompressedLimit.HasValue)
                {
                    return uncompressedLimit.Value;
                }
            }
            else
            {
                int packedByteCount = ReadInt32(ref offset);
                if (packedByteCount < 0)
                {
                    return Corrupt("A flat chunk contains a negative payload size.");
                }

                payloadByteCount = packedByteCount;
                ReaderResult? sizeValidation = ValidateFlatSize(part, blockIndex, packedByteCount);
                if (sizeValidation.HasValue)
                {
                    return sizeValidation.Value;
                }
            }

            if (offset != _headerTarget)
            {
                return Corrupt("A chunk header has an inconsistent encoded length.");
            }

            ulong physicalOffset = checked((ulong)_chunkOffset);
            ulong recordedOffset = part.Offsets[blockIndex];
            if (recordedOffset != 0 && recordedOffset != physicalOffset)
            {
                return Corrupt("A stored chunk offset disagrees with the physical chunk stream.");
            }

            if (_candidateOffsets[_partIndex][blockIndex] != 0)
            {
                return Corrupt("The physical chunk stream contains a duplicate logical block.");
            }

            long nextChunkOffset = checked(_chunkOffset + _headerTarget + payloadByteCount);
            if (knownLength.HasValue && nextChunkOffset > knownLength.Value)
            {
                return Corrupt("A chunk payload extends past the source length while reconstructing offsets.");
            }

            _candidateOffsets[_partIndex][blockIndex] = physicalOffset;
            _chunkOffset = nextChunkOffset;
            _chunksAccepted = checked(_chunksAccepted + 1);
            return null;
        }

        private ReaderResult? ValidateFlatSize(
            ReaderPartData part,
            int blockIndex,
            int packedByteCount)
        {
            ReaderResult? compressedLimit = EnforceLimit(
                nameof(ReaderLimits.MaximumCompressedBlockByteCount),
                packedByteCount,
                _limits.MaximumCompressedBlockByteCount);
            if (compressedLimit.HasValue)
            {
                return compressedLimit.Value;
            }

            BlockInfo info = part.Layout.GetBlockInfo(part.PartIndex, blockIndex, _chunkOffset);
            if (!info.UncompressedByteCount.HasValue ||
                info.UncompressedByteCount.Value > int.MaxValue ||
                info.UncompressedByteCount.Value > (ulong)_limits.MaximumUncompressedBlockByteCount)
            {
                long actual = info.UncompressedByteCount.HasValue &&
                    info.UncompressedByteCount.Value <= long.MaxValue
                        ? (long)info.UncompressedByteCount.Value
                        : long.MaxValue;
                return Limit(
                    nameof(ReaderLimits.MaximumUncompressedBlockByteCount),
                    actual,
                    _limits.MaximumUncompressedBlockByteCount);
            }

            int canonicalByteCount = (int)info.UncompressedByteCount.Value;
            if (part.Header.Compression == Compression.None && packedByteCount != canonicalByteCount)
            {
                return Corrupt("An uncompressed chunk does not match its canonical byte count.");
            }

            if (part.Header.Compression != Compression.HTJ2K256 &&
                part.Header.Compression != Compression.HTJ2K32 &&
                packedByteCount > canonicalByteCount)
            {
                return Corrupt("A compressed chunk is larger than its permitted raw fallback.");
            }

            return null;
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

        private static ReaderResult? EnforceLimit(string name, long actual, long maximum)
        {
            return actual > maximum ? Limit(name, actual, maximum) : null;
        }

        private static ReaderResult Corrupt(string message)
        {
            return new ReaderResult(
                ExrResult.Corrupt,
                null,
                new InvalidDataException(message));
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
