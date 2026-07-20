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
        private readonly bool _multipart;
        private readonly ReaderLimits _limits;
        private readonly byte[] _chunkHeader;
        private int _chunkHeaderOffset;
        private byte[]? _payload;
        private int _payloadOffset;

        public FlatBlockOperation(
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
        }

        public BlockInfo Info { get; }

        public bool HeaderComplete => _chunkHeaderOffset == _chunkHeader.Length;

        public bool HeaderValidated => _payload != null;

        public bool PayloadComplete => _payload != null && _payloadOffset == _payload.Length;

        public ReaderParserRequest GetNextRequest()
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

            if (_payload == null)
            {
                throw new InvalidOperationException("The chunk header has not been validated.");
            }

            if (PayloadComplete)
            {
                throw new InvalidOperationException("The block payload is already complete.");
            }

            int payloadLength = Math.Min(
                _payload.Length - _payloadOffset,
                _limits.MaximumReadRequestByteCount);
            return new ReaderParserRequest(
                checked(Info.FileOffset + _chunkHeader.Length + _payloadOffset),
                _payload,
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

            if (_payload == null)
            {
                throw new InvalidOperationException("The chunk header has not been validated.");
            }

            _payloadOffset = checked(_payloadOffset + byteCount);
        }

        public ReaderResult? ValidateHeader(long? knownLength)
        {
            if (!HeaderComplete || _payload != null)
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

            if (offset != _chunkHeader.Length || packedSize < 0)
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

            long payloadEnd = checked(Info.FileOffset + _chunkHeader.Length + (long)packedSize);
            if (knownLength.HasValue && payloadEnd > knownLength.Value)
            {
                return Corrupt("The EXR block payload extends past the source length.");
            }

            _payload = packedSize == 0 ? Array.Empty<byte>() : new byte[packedSize];
            _payloadOffset = 0;
            return null;
        }

        public ReaderResult Decode(Span<byte> destination)
        {
            if (!PayloadComplete)
            {
                throw new InvalidOperationException("The block payload is incomplete.");
            }

            int expectedSize = checked((int)Info.UncompressedByteCount!.Value);
            byte[] decoded;
            if (_payload!.Length == expectedSize &&
                _header.Compression != Compression.B44 &&
                _header.Compression != Compression.B44A)
            {
                decoded = _payload.ToArray();
            }
            else if (_header.Compression == Compression.ZSTD)
            {
                decoded = new byte[expectedSize];
                ZstdFrameStatus status = ZstdFrameDecoder.Decode(
                    _payload!,
                    decoded,
                    out int consumed,
                    out int written,
                    out _);
                if (status != ZstdFrameStatus.Success ||
                    consumed != _payload!.Length ||
                    written != expectedSize)
                {
                    if (status == ZstdFrameStatus.DictionaryNotSupported ||
                        status == ZstdFrameStatus.WindowTooLarge ||
                        status == ZstdFrameStatus.ContentSizeTooLarge ||
                        status == ZstdFrameStatus.UnsupportedCompressedBlock)
                    {
                        return Unsupported($"The ZSTD block is not supported ({status}).");
                    }

                    return Corrupt($"The ZSTD block is invalid ({status}).");
                }
            }
            else if (_header.Compression == Compression.HTJ2K256 ||
                _header.Compression == Compression.HTJ2K32)
            {
                decoded = new byte[expectedSize];
                Htj2kDecodeStatus status = Htj2kDecoder.Decode(
                    _header,
                    Info.Region,
                    _payload!,
                    decoded,
                    out string? error);
                if (status == Htj2kDecodeStatus.Unsupported)
                {
                    return Unsupported(error ?? "The HTJ2K block uses an unsupported profile feature.");
                }

                if (status != Htj2kDecodeStatus.Success)
                {
                    return Corrupt(error ?? "The HTJ2K block is invalid.");
                }
            }
            else if (_header.Compression == Compression.DWAA ||
                _header.Compression == Compression.DWAB)
            {
                return Unsupported($"Compression '{_header.Compression}' is not implemented by the managed block decoder.");
            }
            else
            {
                List<ExrChannel> channels = new List<ExrChannel>(_header.Channels.Count);
                for (int i = 0; i < _header.Channels.Count; i++)
                {
                    Channel channel = _header.Channels[i];
                    channels.Add(new ExrChannel(
                        channel.Name,
                        (ExrPixelType)(int)channel.PixelType,
                        channel.XSampling,
                        channel.YSampling,
                        channel.PerceptuallyLinear ? (byte)1 : (byte)0));
                }

                ResultCode result = ExrCompressionCodec.TryDecodePayload(
                    (CompressionType)(int)_header.Compression,
                    channels,
                    Info.Region.MinX,
                    Info.Region.MinY,
                    checked((int)Info.Region.Width),
                    checked((int)Info.Region.Height),
                    _payload!,
                    expectedSize,
                    out decoded);
                if (result == ResultCode.UnsupportedFeature || result == ResultCode.UnsupportedFormat)
                {
                    return Unsupported($"Compression '{_header.Compression}' is not supported for this block layout.");
                }

                if (result != ResultCode.Success || decoded.Length != expectedSize)
                {
                    return Corrupt($"The compressed EXR block could not be decoded ({result}).");
                }
            }

            decoded.AsSpan().CopyTo(destination);
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
