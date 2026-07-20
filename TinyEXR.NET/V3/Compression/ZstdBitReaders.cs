namespace TinyEXR.V3.Codecs
{
    /// <summary>
    /// Forward, least-significant-bit-first reader used by FSE table descriptions.
    /// </summary>
    internal ref struct ZstdForwardBitReader
    {
        private readonly ReadOnlySpan<byte> source;
        private int bitPosition;

        internal ZstdForwardBitReader(ReadOnlySpan<byte> source)
        {
            this.source = source;
            bitPosition = 0;
        }

        internal int BytesConsumed => checked((bitPosition + 7) >> 3);

        internal bool TryReadBits(int count, out uint value)
        {
            value = 0;
            if (count < 0 || count > 31 || bitPosition > checked(source.Length * 8) - count)
            {
                return false;
            }

            value = ReadBitsAt(bitPosition, count);
            bitPosition = checked(bitPosition + count);
            return true;
        }

        internal bool TryPeekBits(int count, out uint value)
        {
            value = 0;
            if (count < 0 || count > 31 || bitPosition > checked(source.Length * 8) - count)
            {
                return false;
            }

            value = ReadBitsAt(bitPosition, count);
            return true;
        }

        private uint ReadBitsAt(int startBit, int count)
        {
            uint value = 0;
            for (int bit = 0; bit < count; bit++)
            {
                int sourceBit = checked(startBit + bit);
                uint next = (uint)((source[sourceBit >> 3] >> (sourceBit & 7)) & 1);
                value |= next << bit;
            }

            return value;
        }
    }

    /// <summary>
    /// Backward Zstandard bitstream reader. The highest set bit of the last byte
    /// is the end marker; fields are then consumed toward the start of the span.
    /// </summary>
    internal ref struct ZstdReverseBitReader
    {
        private readonly ReadOnlySpan<byte> source;
        private int remainingBits;

        private ZstdReverseBitReader(ReadOnlySpan<byte> source, int remainingBits)
        {
            this.source = source;
            this.remainingBits = remainingBits;
        }

        internal int RemainingBits => remainingBits;

        internal bool IsAtEnd => remainingBits == 0;

        internal static bool TryCreate(ReadOnlySpan<byte> source, out ZstdReverseBitReader reader)
        {
            reader = default;
            if (source.IsEmpty)
            {
                return false;
            }

            byte lastByte = source[^1];
            if (lastByte == 0)
            {
                return false;
            }

            int markerBit = 7;
            while ((lastByte & (1 << markerBit)) == 0)
            {
                markerBit--;
            }

            int dataBits = checked(((source.Length - 1) * 8) + markerBit);
            reader = new ZstdReverseBitReader(source, dataBits);
            return true;
        }

        internal bool TryReadBits(int count, out uint value)
        {
            value = 0;
            if (count < 0 || count > 31 || count > remainingBits)
            {
                return false;
            }

            remainingBits -= count;
            value = ReadBitsAt(remainingBits, count);
            return true;
        }

        /// <summary>
        /// Reads a transition at the end of an FSE stream. Missing low-order bits
        /// are treated as zero, matching the terminal overflow behavior of the
        /// reference bitstream decoder. False means the read crossed the start.
        /// </summary>
        internal bool TryReadBitsPadded(int count, out uint value)
        {
            value = 0;
            if (count < 0 || count > 31)
            {
                return false;
            }

            if (count <= remainingBits)
            {
                return TryReadBits(count, out value);
            }

            int available = remainingBits;
            if (available != 0)
            {
                value = ReadBitsAt(0, available) << (count - available);
            }

            remainingBits = 0;
            return false;
        }

        internal bool TryPeekBitsPadded(int count, out uint value)
        {
            value = 0;
            if (count < 0 || count > 31)
            {
                return false;
            }

            if (count <= remainingBits)
            {
                value = ReadBitsAt(remainingBits - count, count);
                return true;
            }

            if (remainingBits != 0)
            {
                value = ReadBitsAt(0, remainingBits) << (count - remainingBits);
            }

            return true;
        }

        internal bool TrySkipBits(int count)
        {
            if (count < 0 || count > remainingBits)
            {
                return false;
            }

            remainingBits -= count;
            return true;
        }

        private uint ReadBitsAt(int startBit, int count)
        {
            uint value = 0;
            for (int bit = 0; bit < count; bit++)
            {
                int sourceBit = checked(startBit + bit);
                uint next = (uint)((source[sourceBit >> 3] >> (sourceBit & 7)) & 1);
                value |= next << bit;
            }

            return value;
        }
    }
}
