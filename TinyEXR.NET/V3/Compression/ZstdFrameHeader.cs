using System.Buffers.Binary;

namespace TinyEXR.V3.Codecs
{
    internal readonly struct ZstdFrameHeader
    {
        internal ZstdFrameHeader(
            uint magicNumber,
            byte descriptor,
            bool isSingleSegment,
            bool hasChecksum,
            bool hasDictionaryId,
            uint dictionaryId,
            bool hasFrameContentSize,
            ulong frameContentSize,
            ulong windowSize,
            int headerSize,
            bool isSkippable,
            uint skippablePayloadSize)
        {
            MagicNumber = magicNumber;
            Descriptor = descriptor;
            IsSingleSegment = isSingleSegment;
            HasChecksum = hasChecksum;
            HasDictionaryId = hasDictionaryId;
            DictionaryId = dictionaryId;
            HasFrameContentSize = hasFrameContentSize;
            FrameContentSize = frameContentSize;
            WindowSize = windowSize;
            HeaderSize = headerSize;
            IsSkippable = isSkippable;
            SkippablePayloadSize = skippablePayloadSize;
        }

        internal uint MagicNumber { get; }

        internal byte Descriptor { get; }

        internal bool IsSingleSegment { get; }

        internal bool HasChecksum { get; }

        internal bool HasDictionaryId { get; }

        internal uint DictionaryId { get; }

        internal bool HasFrameContentSize { get; }

        internal ulong FrameContentSize { get; }

        internal ulong WindowSize { get; }

        /// <summary>
        /// Number of bytes occupied by magic and frame header. For a skippable frame,
        /// this is the fixed eight-byte magic/size prefix.
        /// </summary>
        internal int HeaderSize { get; }

        internal bool IsSkippable { get; }

        internal uint SkippablePayloadSize { get; }
    }

    internal static class ZstdFrameHeaderParser
    {
        internal const uint ZstandardMagic = 0xFD2FB528U;
        internal const uint SkippableMagicStart = 0x184D2A50U;
        internal const uint SkippableMagicEnd = 0x184D2A5FU;

        internal static ZstdFrameStatus Parse(
            ReadOnlySpan<byte> source,
            out ZstdFrameHeader header,
            out int bytesConsumed)
        {
            header = default;
            bytesConsumed = 0;

            if (source.Length < sizeof(uint))
            {
                return ZstdFrameStatus.Truncated;
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(source);
            if (magic >= SkippableMagicStart && magic <= SkippableMagicEnd)
            {
                return ParseSkippable(source, magic, out header, out bytesConsumed);
            }

            if (magic != ZstandardMagic)
            {
                return ZstdFrameStatus.InvalidMagic;
            }

            int offset = sizeof(uint);
            if (source.Length == offset)
            {
                return ZstdFrameStatus.Truncated;
            }

            byte descriptor = source[offset++];
            if ((descriptor & 0x08) != 0)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int contentSizeFlag = descriptor >> 6;
            bool isSingleSegment = (descriptor & 0x20) != 0;
            bool hasChecksum = (descriptor & 0x04) != 0;
            int dictionaryIdFlag = descriptor & 0x03;

            ulong windowSize = 0;
            if (!isSingleSegment)
            {
                if (source.Length == offset)
                {
                    return ZstdFrameStatus.Truncated;
                }

                byte windowDescriptor = source[offset++];
                int exponent = windowDescriptor >> 3;
                int mantissa = windowDescriptor & 0x07;
                int windowLog = checked(10 + exponent);
                ulong windowBase = 1UL << windowLog;
                ulong windowAdd = checked((windowBase >> 3) * (ulong)mantissa);
                windowSize = checked(windowBase + windowAdd);
            }

            int dictionaryIdSize = dictionaryIdFlag == 0 ? 0 : 1 << (dictionaryIdFlag - 1);
            if (!HasBytes(source, offset, dictionaryIdSize))
            {
                return ZstdFrameStatus.Truncated;
            }

            uint dictionaryId = dictionaryIdSize switch
            {
                0 => 0,
                1 => source[offset],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2)),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4)),
                _ => throw new InvalidOperationException("Invalid Zstandard dictionary ID field size."),
            };
            offset += dictionaryIdSize;

            int contentSizeFieldSize = contentSizeFlag switch
            {
                0 => isSingleSegment ? 1 : 0,
                1 => 2,
                2 => 4,
                3 => 8,
                _ => throw new InvalidOperationException("Invalid Zstandard content size flag."),
            };
            if (!HasBytes(source, offset, contentSizeFieldSize))
            {
                return ZstdFrameStatus.Truncated;
            }

            bool hasFrameContentSize = contentSizeFieldSize != 0;
            ulong frameContentSize = contentSizeFieldSize switch
            {
                0 => 0,
                1 => source[offset],
                2 => checked((ulong)BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2)) + 256UL),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4)),
                8 => BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8)),
                _ => throw new InvalidOperationException("Invalid Zstandard content size field size."),
            };
            offset += contentSizeFieldSize;

            if (isSingleSegment)
            {
                windowSize = frameContentSize;
            }

            header = new ZstdFrameHeader(
                magic,
                descriptor,
                isSingleSegment,
                hasChecksum,
                dictionaryIdSize != 0,
                dictionaryId,
                hasFrameContentSize,
                frameContentSize,
                windowSize,
                offset,
                isSkippable: false,
                skippablePayloadSize: 0);

            if (windowSize > ZstdFrameLimits.MaximumWindowSize)
            {
                return ZstdFrameStatus.WindowTooLarge;
            }

            if (hasFrameContentSize && frameContentSize > (ulong)ZstdFrameLimits.MaximumOutputSize)
            {
                return ZstdFrameStatus.ContentSizeTooLarge;
            }

            bytesConsumed = offset;
            return ZstdFrameStatus.Success;
        }

        private static ZstdFrameStatus ParseSkippable(
            ReadOnlySpan<byte> source,
            uint magic,
            out ZstdFrameHeader header,
            out int bytesConsumed)
        {
            header = default;
            bytesConsumed = 0;
            if (source.Length < 8)
            {
                return ZstdFrameStatus.Truncated;
            }

            uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4));
            ulong totalSize = checked(8UL + payloadSize);
            if (totalSize > (ulong)source.Length)
            {
                return ZstdFrameStatus.Truncated;
            }

            if (totalSize > int.MaxValue)
            {
                return ZstdFrameStatus.ContentSizeTooLarge;
            }

            bytesConsumed = (int)totalSize;
            header = new ZstdFrameHeader(
                magic,
                descriptor: 0,
                isSingleSegment: false,
                hasChecksum: false,
                hasDictionaryId: false,
                dictionaryId: 0,
                hasFrameContentSize: false,
                frameContentSize: 0,
                windowSize: 0,
                headerSize: 8,
                isSkippable: true,
                skippablePayloadSize: payloadSize);
            return ZstdFrameStatus.Skipped;
        }

        private static bool HasBytes(ReadOnlySpan<byte> source, int offset, int count)
        {
            return offset >= 0 && count >= 0 && offset <= source.Length && source.Length - offset >= count;
        }
    }
}
