using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using TinyEXR.PortV1;
using TinyEXR.V3.Codecs;

namespace TinyEXR.V3
{
    internal sealed class FlatBlockOperation
    {
        private readonly Header _header;
        private readonly ReaderPartData _part;
        private readonly bool _multipart;
        private readonly ReaderLimits _limits;
        private readonly ReaderDecodeResources _resources;
        private byte[] _chunkHeader;
        private int _chunkHeaderLength;
        private int _chunkHeaderOffset;
        private byte[]? _payload;
        private int _payloadLength;
        private int _payloadOffset;
        private bool _headerValidated;
        private bool _consumed;

        public FlatBlockOperation(
            ReaderPartData part,
            BlockInfo info,
            bool multipart,
            ReaderLimits limits,
            ReaderDecodeResources resources)
        {
            _part = part;
            _header = part.Header;
            _multipart = multipart;
            _limits = limits;
            _resources = resources;
            _chunkHeader = Array.Empty<byte>();
            Reset(info);
        }

        public BlockInfo Info { get; private set; }

        /// <summary>The part this operation is bound to; rebinding across parts requires a new operation.</summary>
        public ReaderPartData Part => _part;

        public bool HeaderComplete => _chunkHeaderOffset == _chunkHeaderLength;

        public bool HeaderValidated => _headerValidated;

        public bool PayloadComplete => _headerValidated && _payloadOffset == _payloadLength;

        /// <summary>
        /// Rebinds the operation to another block, reusing the rented buffers.
        /// </summary>
        public void Reset(BlockInfo info)
        {
            Info = info;
            _chunkHeaderLength = info.ChunkHeaderByteCount;
            if (_chunkHeader.Length < _chunkHeaderLength)
            {
                _resources.ReturnBytes(_chunkHeader);
                _chunkHeader = _resources.RentBytes(_chunkHeaderLength);
            }

            _chunkHeaderOffset = 0;
            _payloadOffset = 0;
            _payloadLength = 0;
            _headerValidated = false;
            _consumed = false;
        }

        /// <summary>True once <see cref="Decode"/> has run, so the operation must be rebound before reuse.</summary>
        public bool IsConsumed => _consumed;

        /// <summary>Marks a completed decode so the next block rebinds rather than reusing stale state.</summary>
        public void MarkConsumed()
        {
            _consumed = true;
        }

        /// <summary>Returns every rented buffer to the reader's pool.</summary>
        public void Release()
        {
            _resources.ReturnBytes(_chunkHeader);
            _chunkHeader = Array.Empty<byte>();
            _chunkHeaderLength = 0;
            _resources.ReturnBytes(_payload);
            _payload = null;
            _payloadLength = 0;
            _headerValidated = false;
        }

        public ReaderParserRequest GetNextRequest()
        {
            if (!HeaderComplete)
            {
                int length = Math.Min(
                    _chunkHeaderLength - _chunkHeaderOffset,
                    _limits.MaximumReadRequestByteCount);
                return new ReaderParserRequest(
                    checked(Info.FileOffset + _chunkHeaderOffset),
                    _chunkHeader,
                    _chunkHeaderOffset,
                    length);
            }

            if (!_headerValidated)
            {
                throw new InvalidOperationException("The chunk header has not been validated.");
            }

            if (PayloadComplete)
            {
                throw new InvalidOperationException("The block payload is already complete.");
            }

            int payloadLength = Math.Min(
                _payloadLength - _payloadOffset,
                _limits.MaximumReadRequestByteCount);
            return new ReaderParserRequest(
                checked(Info.FileOffset + _chunkHeaderLength + _payloadOffset),
                _payload!,
                _payloadOffset,
                payloadLength);
        }

        public void AcceptRequest(int byteCount)
        {
            if (!HeaderComplete)
            {
                _chunkHeaderOffset = checked(_chunkHeaderOffset + byteCount);
                return;
            }

            if (!_headerValidated)
            {
                throw new InvalidOperationException("The chunk header has not been validated.");
            }

            _payloadOffset = checked(_payloadOffset + byteCount);
        }

        public ReaderResult? ValidateHeader(long? knownLength)
        {
            if (!HeaderComplete || _headerValidated)
            {
                throw new InvalidOperationException("The chunk header is not ready for validation.");
            }

            int offset = 0;
            if (_multipart)
            {
                int partNumber = ReadInt32(ref offset);
                if (partNumber != Info.PartIndex)
                {
                    return Corrupt("The multipart chunk identifies a different part.");
                }
            }

            int packedSize;
            if (Info.IsTiled)
            {
                int tileX = ReadInt32(ref offset);
                int tileY = ReadInt32(ref offset);
                int levelX = ReadInt32(ref offset);
                int levelY = ReadInt32(ref offset);
                packedSize = ReadInt32(ref offset);
                if (tileX != Info.TileX || tileY != Info.TileY ||
                    levelX != Info.LevelX || levelY != Info.LevelY)
                {
                    return Corrupt("The tiled chunk coordinates do not match its offset-table index.");
                }
            }
            else
            {
                int minimumY = ReadInt32(ref offset);
                packedSize = ReadInt32(ref offset);
                if (minimumY != Info.Region.MinY)
                {
                    return Corrupt("The scanline chunk coordinate does not match its offset-table index.");
                }
            }

            if (offset != _chunkHeaderLength || packedSize < 0)
            {
                return Corrupt("The flat chunk header is invalid.");
            }

            if (packedSize > _limits.MaximumCompressedBlockByteCount)
            {
                return Limit(
                    nameof(ReaderLimits.MaximumCompressedBlockByteCount),
                    packedSize,
                    _limits.MaximumCompressedBlockByteCount);
            }

            if (!Info.UncompressedByteCount.HasValue ||
                Info.UncompressedByteCount.Value > int.MaxValue ||
                Info.UncompressedByteCount.Value > (ulong)_limits.MaximumUncompressedBlockByteCount)
            {
                long actual = Info.UncompressedByteCount.HasValue &&
                    Info.UncompressedByteCount.Value <= long.MaxValue
                        ? (long)Info.UncompressedByteCount.Value
                        : long.MaxValue;
                return Limit(
                    nameof(ReaderLimits.MaximumUncompressedBlockByteCount),
                    actual,
                    _limits.MaximumUncompressedBlockByteCount);
            }

            int expectedSize = (int)Info.UncompressedByteCount.Value;
            if (_header.Compression == Compression.None && packedSize != expectedSize)
            {
                return Corrupt("An uncompressed EXR block does not match its canonical byte count.");
            }

            if (_header.Compression != Compression.B44 &&
                _header.Compression != Compression.B44A &&
                _header.Compression != Compression.HTJ2K256 &&
                _header.Compression != Compression.HTJ2K32 &&
                packedSize > expectedSize)
            {
                return Corrupt("A compressed EXR block is larger than its permitted raw fallback.");
            }

            long payloadEnd = checked(Info.FileOffset + _chunkHeaderLength + (long)packedSize);
            if (knownLength.HasValue && payloadEnd > knownLength.Value)
            {
                return Corrupt("The EXR block payload extends past the source length.");
            }

            if (_payload == null || _payload.Length < packedSize)
            {
                _resources.ReturnBytes(_payload);
                _payload = _resources.RentBytes(packedSize);
            }

            _payloadLength = packedSize;
            _payloadOffset = 0;
            _headerValidated = true;
            return null;
        }

        public ReaderResult Decode(Span<byte> destination)
        {
            if (!PayloadComplete)
            {
                throw new InvalidOperationException("The block payload is incomplete.");
            }

            int expectedSize = checked((int)Info.UncompressedByteCount!.Value);
            if (_payloadLength == expectedSize &&
                _header.Compression != Compression.B44 &&
                _header.Compression != Compression.B44A)
            {
                // A stored raw fallback block; copy it straight to the caller's canonical buffer.
                _payload!.AsSpan(0, expectedSize).CopyTo(destination);
                return new ReaderResult(ExrResult.Success, null, null, expectedSize);
            }

            if (_header.Compression == Compression.HTJ2K256 ||
                _header.Compression == Compression.HTJ2K32)
            {
                // The HTJ2K codestream parser derives every segment bound from the array length, so it needs an
                // exact-length payload rather than a pooled buffer with spare capacity.
                byte[] exactPayload = _payloadLength == _payload!.Length
                    ? _payload
                    : _payload.AsSpan(0, _payloadLength).ToArray();
                Htj2kDecodeStatus status = Htj2kDecoder.Decode(
                    _header,
                    Info.Region,
                    exactPayload,
                    destination.Slice(0, expectedSize),
                    _resources.Htj2kPool,
                    out string? error);
                if (status == Htj2kDecodeStatus.Unsupported)
                {
                    return Unsupported(error ?? "The HTJ2K block uses an unsupported profile feature.");
                }

                if (status != Htj2kDecodeStatus.Success)
                {
                    return Corrupt(error ?? "The HTJ2K block is invalid.");
                }

                return new ReaderResult(ExrResult.Success, null, null, expectedSize);
            }

            if (_header.Compression == Compression.DWAA ||
                _header.Compression == Compression.DWAB)
            {
                return Unsupported($"Compression '{_header.Compression}' is not implemented by the managed block decoder.");
            }

            ResultCode result = ExrCompressionCodec.TryDecodePayload(
                (CompressionType)(int)_header.Compression,
                _part.CodecChannels,
                Info.Region.MinX,
                Info.Region.MinY,
                checked((int)Info.Region.Width),
                checked((int)Info.Region.Height),
                _payload!,
                _payloadLength,
                expectedSize,
                _resources.CodecWorkspace,
                out byte[] decoded,
                out int decodedLength);
            if (result == ResultCode.UnsupportedFeature || result == ResultCode.UnsupportedFormat)
            {
                return Unsupported($"Compression '{_header.Compression}' is not supported for this block layout.");
            }

            if (result != ResultCode.Success || decodedLength != expectedSize)
            {
                return Corrupt($"The compressed EXR block could not be decoded ({result}).");
            }

            decoded.AsSpan(0, decodedLength).CopyTo(destination);
            return new ReaderResult(ExrResult.Success, null, null, expectedSize);
        }

        private int ReadInt32(ref int offset)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(_chunkHeader.AsSpan(offset, sizeof(int)));
            offset += sizeof(int);
            return value;
        }

        private static ReaderResult Corrupt(string message)
        {
            return new ReaderResult(ExrResult.Corrupt, null, new InvalidOperationException(message));
        }

        private static ReaderResult Unsupported(string message)
        {
            return new ReaderResult(ExrResult.Unsupported, null, new NotSupportedException(message));
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
