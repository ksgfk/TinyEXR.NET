namespace TinyEXR.V3.Codecs
{
    internal readonly struct ZstdHuffmanEntry
    {
        internal ZstdHuffmanEntry(byte symbol, byte numberBits)
        {
            Symbol = symbol;
            NumberBits = numberBits;
        }

        internal byte Symbol { get; }

        internal byte NumberBits { get; }
    }

    internal sealed class ZstdHuffmanTable
    {
        private const int MaximumTableLog = 11;

        private ZstdHuffmanTable(int tableLog, ZstdHuffmanEntry[] entries)
        {
            TableLog = tableLog;
            Entries = entries;
        }

        internal int TableLog { get; }

        internal ZstdHuffmanEntry[] Entries { get; }

        internal static ZstdFrameStatus Read(
            ReadOnlySpan<byte> source,
            out ZstdHuffmanTable? table,
            out int bytesConsumed)
        {
            table = null;
            bytesConsumed = 0;
            if (source.IsEmpty)
            {
                return ZstdFrameStatus.Truncated;
            }

            byte[] weights;
            int headerByte = source[0];
            if (headerByte >= 128)
            {
                int weightCount = headerByte - 127;
                int packedSize = checked((weightCount + 1) >> 1);
                if (source.Length - 1 < packedSize)
                {
                    return ZstdFrameStatus.Truncated;
                }

                weights = new byte[weightCount];
                for (int index = 0; index < weightCount; index++)
                {
                    byte packed = source[1 + (index >> 1)];
                    weights[index] = (byte)((index & 1) == 0 ? packed >> 4 : packed & 0x0f);
                }

                bytesConsumed = checked(1 + packedSize);
            }
            else
            {
                int compressedWeightsSize = headerByte;
                if (compressedWeightsSize == 0 || source.Length - 1 < compressedWeightsSize)
                {
                    return compressedWeightsSize == 0
                        ? ZstdFrameStatus.Corrupt
                        : ZstdFrameStatus.Truncated;
                }

                ZstdFrameStatus weightsStatus = ZstdFseTable.DecodeByteStream(
                    source.Slice(1, compressedWeightsSize),
                    maximumOutputSize: 255,
                    out weights);
                if (weightsStatus != ZstdFrameStatus.Success)
                {
                    return weightsStatus;
                }

                bytesConsumed = checked(1 + compressedWeightsSize);
            }

            if (weights.Length == 0 || weights.Length >= 256)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int weightTotal = 0;
            for (int index = 0; index < weights.Length; index++)
            {
                int weight = weights[index];
                if (weight > MaximumTableLog)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                if (weight != 0)
                {
                    weightTotal = checked(weightTotal + (1 << (weight - 1)));
                }
            }

            if (weightTotal == 0)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int tableLog = FloorLog2((uint)weightTotal) + 1;
            if (tableLog > MaximumTableLog)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int total = 1 << tableLog;
            int remainder = total - weightTotal;
            if (remainder <= 0 || (remainder & (remainder - 1)) != 0)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int impliedWeight = FloorLog2((uint)remainder) + 1;
            if (impliedWeight > MaximumTableLog)
            {
                return ZstdFrameStatus.Corrupt;
            }

            byte[] completeWeights = new byte[weights.Length + 1];
            weights.CopyTo(completeWeights, 0);
            completeWeights[^1] = (byte)impliedWeight;

            int weightOneCount = 0;
            for (int index = 0; index < completeWeights.Length; index++)
            {
                if (completeWeights[index] == 1)
                {
                    weightOneCount++;
                }
            }

            if (weightOneCount < 2 || (weightOneCount & 1) != 0)
            {
                return ZstdFrameStatus.Corrupt;
            }

            ZstdHuffmanEntry[] entries = new ZstdHuffmanEntry[total];
            int tablePosition = 0;
            for (int weight = 1; weight <= tableLog; weight++)
            {
                int repetitions = 1 << (weight - 1);
                int numberBits = tableLog + 1 - weight;
                for (int symbol = 0; symbol < completeWeights.Length; symbol++)
                {
                    if (completeWeights[symbol] != weight)
                    {
                        continue;
                    }

                    if (tablePosition > entries.Length - repetitions)
                    {
                        return ZstdFrameStatus.Corrupt;
                    }

                    ZstdHuffmanEntry entry = new((byte)symbol, (byte)numberBits);
                    for (int repeat = 0; repeat < repetitions; repeat++)
                    {
                        entries[tablePosition++] = entry;
                    }
                }
            }

            if (tablePosition != entries.Length)
            {
                return ZstdFrameStatus.Corrupt;
            }

            table = new ZstdHuffmanTable(tableLog, entries);
            return ZstdFrameStatus.Success;
        }

        internal ZstdFrameStatus DecodeStream(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (!ZstdReverseBitReader.TryCreate(source, out ZstdReverseBitReader reader))
            {
                return ZstdFrameStatus.Corrupt;
            }

            for (int outputIndex = 0; outputIndex < destination.Length; outputIndex++)
            {
                if (!reader.TryPeekBitsPadded(TableLog, out uint tableIndex)
                    || tableIndex >= Entries.Length)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                ZstdHuffmanEntry entry = Entries[tableIndex];
                if (entry.NumberBits == 0 || !reader.TrySkipBits(entry.NumberBits))
                {
                    return ZstdFrameStatus.Corrupt;
                }

                destination[outputIndex] = entry.Symbol;
            }

            return reader.IsAtEnd ? ZstdFrameStatus.Success : ZstdFrameStatus.Corrupt;
        }

        private static int FloorLog2(uint value)
        {
            if (value == 0)
            {
                return -1;
            }

            int result = 0;
            while ((value >>= 1) != 0)
            {
                result++;
            }

            return result;
        }
    }
}
