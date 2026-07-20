using System.Buffers.Binary;

namespace TinyEXR.V3.Codecs
{
    /// <summary>
    /// Decoder for standard Zstandard frames, including raw, RLE, and
    /// entropy-compressed blocks. Dictionary-backed frames are not supported.
    /// </summary>
    internal static class ZstdFrameDecoder
    {
        internal static ZstdFrameStatus Decode(
            ReadOnlySpan<byte> source,
            Span<byte> destination,
            out int bytesConsumed,
            out int bytesWritten,
            out ZstdFrameHeader header)
        {
            bytesConsumed = 0;
            bytesWritten = 0;

            ZstdFrameStatus headerStatus = ZstdFrameHeaderParser.Parse(
                source,
                out header,
                out int headerBytes);
            if (headerStatus == ZstdFrameStatus.Skipped)
            {
                bytesConsumed = headerBytes;
                return ZstdFrameStatus.Skipped;
            }

            if (headerStatus != ZstdFrameStatus.Success)
            {
                return headerStatus;
            }

            bytesConsumed = headerBytes;
            if (header.DictionaryId != 0)
            {
                return ZstdFrameStatus.DictionaryNotSupported;
            }

            if (header.HasFrameContentSize && header.FrameContentSize > (ulong)destination.Length)
            {
                return ZstdFrameStatus.DestinationTooSmall;
            }

            ulong blockMaximumSize = header.WindowSize < (ulong)ZstdFrameLimits.MaximumBlockSize
                ? header.WindowSize
                : (ulong)ZstdFrameLimits.MaximumBlockSize;
            int inputOffset = headerBytes;
            int outputOffset = 0;
            ZstdCompressedBlockState compressedBlockState = new();

            while (true)
            {
                if (source.Length - inputOffset < 3)
                {
                    bytesConsumed = inputOffset;
                    bytesWritten = outputOffset;
                    return ZstdFrameStatus.Truncated;
                }

                uint blockHeader = (uint)(source[inputOffset]
                    | (source[inputOffset + 1] << 8)
                    | (source[inputOffset + 2] << 16));
                inputOffset += 3;

                bool isLastBlock = (blockHeader & 1U) != 0;
                int blockType = (int)((blockHeader >> 1) & 0x03U);
                uint blockSize = blockHeader >> 3;
                if (blockSize > blockMaximumSize)
                {
                    bytesConsumed = inputOffset;
                    bytesWritten = outputOffset;
                    return ZstdFrameStatus.Corrupt;
                }

                if (blockType == 3)
                {
                    bytesConsumed = inputOffset;
                    bytesWritten = outputOffset;
                    return ZstdFrameStatus.Corrupt;
                }

                int payloadSize = blockType == 1 ? 1 : (int)blockSize;
                // libzstd's one-shot decoder accepts a zero-regeneration RLE block in a
                // zero-sized window. Preserve that compatibility exception: the encoded
                // payload is still one byte, while blockSize above remains constrained by
                // the window and represents the regenerated size for RLE blocks.

                if (source.Length - inputOffset < payloadSize)
                {
                    bytesConsumed = inputOffset;
                    bytesWritten = outputOffset;
                    return ZstdFrameStatus.Truncated;
                }

                if (blockType == 2)
                {
                    ZstdFrameStatus compressedStatus = ZstdCompressedBlockDecoder.Decode(
                        source.Slice(inputOffset, payloadSize),
                        destination,
                        outputOffset,
                        (int)blockMaximumSize,
                        header.WindowSize,
                        header.HasFrameContentSize,
                        header.FrameContentSize,
                        compressedBlockState,
                        out int compressedBytesWritten);
                    if (compressedStatus != ZstdFrameStatus.Success)
                    {
                        bytesConsumed = checked(inputOffset + payloadSize);
                        bytesWritten = outputOffset;
                        return compressedStatus;
                    }

                    inputOffset = checked(inputOffset + payloadSize);
                    outputOffset = checked(outputOffset + compressedBytesWritten);
                    if (isLastBlock)
                    {
                        break;
                    }

                    continue;
                }

                int regeneratedSize = (int)blockSize;
                if (header.HasFrameContentSize
                    && (ulong)outputOffset + (ulong)regeneratedSize > header.FrameContentSize)
                {
                    bytesConsumed = inputOffset;
                    bytesWritten = outputOffset;
                    return ZstdFrameStatus.ContentSizeMismatch;
                }

                if (regeneratedSize > ZstdFrameLimits.MaximumOutputSize - outputOffset)
                {
                    bytesConsumed = inputOffset;
                    bytesWritten = outputOffset;
                    return ZstdFrameStatus.ContentSizeTooLarge;
                }

                if (regeneratedSize > destination.Length - outputOffset)
                {
                    bytesConsumed = inputOffset;
                    bytesWritten = outputOffset;
                    return ZstdFrameStatus.DestinationTooSmall;
                }

                if (blockType == 0)
                {
                    source.Slice(inputOffset, regeneratedSize).CopyTo(destination.Slice(outputOffset, regeneratedSize));
                    inputOffset += regeneratedSize;
                }
                else
                {
                    byte repeatedValue = source[inputOffset++];
                    destination.Slice(outputOffset, regeneratedSize).Fill(repeatedValue);
                }

                outputOffset += regeneratedSize;
                if (isLastBlock)
                {
                    break;
                }
            }

            if (header.HasFrameContentSize && (ulong)outputOffset != header.FrameContentSize)
            {
                bytesConsumed = inputOffset;
                bytesWritten = outputOffset;
                return ZstdFrameStatus.ContentSizeMismatch;
            }

            if (header.HasChecksum)
            {
                if (source.Length - inputOffset < sizeof(uint))
                {
                    bytesConsumed = inputOffset;
                    bytesWritten = outputOffset;
                    return ZstdFrameStatus.Truncated;
                }

                uint expectedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(inputOffset, sizeof(uint)));
                uint actualChecksum = unchecked((uint)XxHash64.Compute(destination.Slice(0, outputOffset)));
                if (expectedChecksum != actualChecksum)
                {
                    bytesConsumed = inputOffset + sizeof(uint);
                    bytesWritten = outputOffset;
                    return ZstdFrameStatus.ChecksumMismatch;
                }

                inputOffset += sizeof(uint);
            }

            bytesConsumed = inputOffset;
            bytesWritten = outputOffset;
            return ZstdFrameStatus.Success;
        }
    }

    /// <summary>
    /// Standards-compliant Zstandard frame encoder using raw and RLE blocks only.
    /// Entropy-compressed block emission is intentionally reserved for Phase Z3.
    /// </summary>
    internal static class ZstdRawRleEncoder
    {
        internal static ZstdFrameStatus GetEncodedSize(
            ReadOnlySpan<byte> source,
            bool includeChecksum,
            out int encodedSize)
        {
            encodedSize = 0;
            if (source.Length > ZstdFrameLimits.MaximumOutputSize)
            {
                return ZstdFrameStatus.ContentSizeTooLarge;
            }

            int contentSizeFieldSize = GetContentSizeFieldSize(source.Length);
            int size = checked(sizeof(uint) + 1 + contentSizeFieldSize + (includeChecksum ? sizeof(uint) : 0));
            int blockCount = source.Length == 0
                ? 1
                : checked((source.Length + ZstdFrameLimits.MaximumBlockSize - 1) / ZstdFrameLimits.MaximumBlockSize);

            int sourceOffset = 0;
            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                int blockSize = Math.Min(ZstdFrameLimits.MaximumBlockSize, source.Length - sourceOffset);
                bool useRle = blockSize > 1 && IsRun(source.Slice(sourceOffset, blockSize));
                size = checked(size + 3 + (useRle ? 1 : blockSize));
                sourceOffset = checked(sourceOffset + blockSize);
            }

            encodedSize = size;
            return ZstdFrameStatus.Success;
        }

        internal static ZstdFrameStatus Encode(
            ReadOnlySpan<byte> source,
            Span<byte> destination,
            bool includeChecksum,
            out int bytesWritten)
        {
            bytesWritten = 0;
            ZstdFrameStatus sizeStatus = GetEncodedSize(source, includeChecksum, out int requiredSize);
            if (sizeStatus != ZstdFrameStatus.Success)
            {
                return sizeStatus;
            }

            if (destination.Length < requiredSize)
            {
                return ZstdFrameStatus.DestinationTooSmall;
            }

            int offset = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, sizeof(uint)), ZstdFrameHeaderParser.ZstandardMagic);
            offset += sizeof(uint);

            int contentSizeFieldSize = GetContentSizeFieldSize(source.Length);
            byte contentSizeFlag = contentSizeFieldSize switch
            {
                1 => 0,
                2 => 1,
                4 => 2,
                _ => throw new InvalidOperationException("Invalid Zstandard frame content size field size."),
            };
            byte descriptor = (byte)((contentSizeFlag << 6) | 0x20 | (includeChecksum ? 0x04 : 0));
            destination[offset++] = descriptor;
            WriteContentSize(destination.Slice(offset, contentSizeFieldSize), source.Length, contentSizeFieldSize);
            offset += contentSizeFieldSize;

            int blockCount = source.Length == 0
                ? 1
                : checked((source.Length + ZstdFrameLimits.MaximumBlockSize - 1) / ZstdFrameLimits.MaximumBlockSize);
            int sourceOffset = 0;
            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                int blockSize = Math.Min(ZstdFrameLimits.MaximumBlockSize, source.Length - sourceOffset);
                bool useRle = blockSize > 1 && IsRun(source.Slice(sourceOffset, blockSize));
                bool isLastBlock = blockIndex == blockCount - 1;
                uint blockHeader = ((uint)blockSize << 3)
                    | (useRle ? 2U : 0U)
                    | (isLastBlock ? 1U : 0U);
                destination[offset] = (byte)blockHeader;
                destination[offset + 1] = (byte)(blockHeader >> 8);
                destination[offset + 2] = (byte)(blockHeader >> 16);
                offset += 3;

                if (useRle)
                {
                    destination[offset++] = source[sourceOffset];
                }
                else
                {
                    source.Slice(sourceOffset, blockSize).CopyTo(destination.Slice(offset, blockSize));
                    offset += blockSize;
                }

                sourceOffset = checked(sourceOffset + blockSize);
            }

            if (includeChecksum)
            {
                uint checksum = unchecked((uint)XxHash64.Compute(source));
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, sizeof(uint)), checksum);
                offset += sizeof(uint);
            }

            if (offset != requiredSize)
            {
                throw new InvalidOperationException("Zstandard encoded size calculation did not match bytes written.");
            }

            bytesWritten = offset;
            return ZstdFrameStatus.Success;
        }

        private static int GetContentSizeFieldSize(int contentSize)
        {
            if (contentSize < 256)
            {
                return 1;
            }

            if (contentSize < 256 + 65536)
            {
                return 2;
            }

            return 4;
        }

        private static void WriteContentSize(Span<byte> destination, int contentSize, int fieldSize)
        {
            switch (fieldSize)
            {
                case 1:
                    destination[0] = (byte)contentSize;
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16LittleEndian(destination, checked((ushort)(contentSize - 256)));
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)contentSize);
                    break;
                default:
                    throw new InvalidOperationException("Invalid Zstandard frame content size field size.");
            }
        }

        private static bool IsRun(ReadOnlySpan<byte> source)
        {
            byte value = source[0];
            for (int i = 1; i < source.Length; i++)
            {
                if (source[i] != value)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
