namespace TinyEXR.V3.Codecs
{
    internal readonly struct ZstdFseEntry
    {
        internal ZstdFseEntry(byte symbol, ushort newState, byte numberBits)
        {
            Symbol = symbol;
            NewState = newState;
            NumberBits = numberBits;
        }

        internal byte Symbol { get; }

        internal ushort NewState { get; }

        internal byte NumberBits { get; }
    }

    internal sealed class ZstdFseTable
    {
        private ZstdFseTable(int tableLog, ZstdFseEntry[] entries)
        {
            TableLog = tableLog;
            Entries = entries;
        }

        internal int TableLog { get; }

        internal ZstdFseEntry[] Entries { get; }

        internal static ZstdFrameStatus Read(
            ReadOnlySpan<byte> source,
            int maximumSymbol,
            int maximumTableLog,
            out ZstdFseTable? table,
            out int bytesConsumed)
        {
            table = null;
            bytesConsumed = 0;
            if (source.IsEmpty)
            {
                return ZstdFrameStatus.Truncated;
            }

            short[] normalizedCounts = new short[maximumSymbol + 1];
            ZstdForwardBitReader reader = new(source);
            if (!reader.TryReadBits(4, out uint tableLogBits))
            {
                return ZstdFrameStatus.Truncated;
            }

            int tableLog = checked((int)tableLogBits + 5);
            if (tableLog > maximumTableLog)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int remaining = checked((1 << tableLog) + 1);
            int threshold = 1 << tableLog;
            int numberBits = tableLog + 1;
            int symbol = 0;
            bool previousWasZero = false;

            while (remaining > 1 && symbol <= maximumSymbol)
            {
                if (previousWasZero)
                {
                    while (true)
                    {
                        if (!reader.TryReadBits(2, out uint repeatCode))
                        {
                            return ZstdFrameStatus.Truncated;
                        }

                        symbol = checked(symbol + (int)repeatCode);
                        if (symbol > maximumSymbol + 1)
                        {
                            return ZstdFrameStatus.Corrupt;
                        }

                        if (repeatCode != 3)
                        {
                            break;
                        }
                    }

                    if (symbol > maximumSymbol)
                    {
                        break;
                    }
                }

                int maximumLowValue = checked((2 * threshold - 1) - remaining);
                if (!reader.TryPeekBits(numberBits - 1, out uint lowBits))
                {
                    return ZstdFrameStatus.Truncated;
                }

                int count;
                if (lowBits < (uint)maximumLowValue)
                {
                    if (!reader.TryReadBits(numberBits - 1, out uint encodedCount))
                    {
                        return ZstdFrameStatus.Truncated;
                    }

                    count = (int)encodedCount;
                }
                else
                {
                    if (!reader.TryReadBits(numberBits, out uint encodedCount))
                    {
                        return ZstdFrameStatus.Truncated;
                    }

                    count = (int)encodedCount;
                    if (count >= threshold)
                    {
                        count -= maximumLowValue;
                    }
                }

                count--;
                if (count < -1)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                if (count >= 0)
                {
                    remaining -= count;
                }
                else
                {
                    remaining--;
                }

                if (remaining < 1)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                normalizedCounts[symbol++] = (short)count;
                previousWasZero = count == 0;

                if (remaining < threshold)
                {
                    if (remaining <= 1)
                    {
                        break;
                    }

                    numberBits = FloorLog2((uint)remaining) + 1;
                    threshold = 1 << (numberBits - 1);
                }
            }

            if (remaining != 1 || symbol == 0 || symbol > maximumSymbol + 1)
            {
                return ZstdFrameStatus.Corrupt;
            }

            bytesConsumed = reader.BytesConsumed;
            if (bytesConsumed > source.Length)
            {
                return ZstdFrameStatus.Truncated;
            }

            return Build(normalizedCounts, symbol - 1, tableLog, out table);
        }

        internal static ZstdFrameStatus Build(
            ReadOnlySpan<short> normalizedCounts,
            int maximumSymbol,
            int tableLog,
            out ZstdFseTable? table)
        {
            table = null;
            if (tableLog < 0 || tableLog > 9 || maximumSymbol < 0 || maximumSymbol >= normalizedCounts.Length)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int tableSize = 1 << tableLog;
            int probabilityTotal = 0;
            int nonZeroSymbols = 0;
            for (int symbol = 0; symbol <= maximumSymbol; symbol++)
            {
                int count = normalizedCounts[symbol];
                if (count < -1)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                if (count != 0)
                {
                    nonZeroSymbols++;
                    probabilityTotal = checked(probabilityTotal + (count == -1 ? 1 : count));
                }
            }

            if (probabilityTotal != tableSize || nonZeroSymbols == 0)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int[] symbolsByState = new int[tableSize];
            Array.Fill(symbolsByState, -1);
            int[] symbolNext = new int[maximumSymbol + 1];
            int highThreshold = tableSize - 1;

            for (int symbol = 0; symbol <= maximumSymbol; symbol++)
            {
                int count = normalizedCounts[symbol];
                if (count == -1)
                {
                    if (highThreshold < 0)
                    {
                        return ZstdFrameStatus.Corrupt;
                    }

                    symbolsByState[highThreshold--] = symbol;
                    symbolNext[symbol] = 1;
                }
                else
                {
                    symbolNext[symbol] = count;
                }
            }

            int tableMask = tableSize - 1;
            int step = (tableSize >> 1) + (tableSize >> 3) + 3;
            int position = 0;
            for (int symbol = 0; symbol <= maximumSymbol; symbol++)
            {
                int count = normalizedCounts[symbol];
                for (int occurrence = 0; occurrence < count; occurrence++)
                {
                    if (position < 0 || position >= tableSize || symbolsByState[position] != -1)
                    {
                        return ZstdFrameStatus.Corrupt;
                    }

                    symbolsByState[position] = symbol;
                    position = (position + step) & tableMask;
                    while (position > highThreshold)
                    {
                        position = (position + step) & tableMask;
                    }
                }
            }

            if (position != 0 || symbolsByState.Any(static value => value < 0))
            {
                return ZstdFrameStatus.Corrupt;
            }

            ZstdFseEntry[] entries = new ZstdFseEntry[tableSize];
            for (int state = 0; state < tableSize; state++)
            {
                int symbol = symbolsByState[state];
                int nextState = symbolNext[symbol]++;
                if (nextState <= 0)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                int numberBits = tableLog - FloorLog2((uint)nextState);
                int newState = checked((nextState << numberBits) - tableSize);
                if (numberBits < 0 || numberBits > tableLog || newState < 0 || newState >= tableSize)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                entries[state] = new ZstdFseEntry((byte)symbol, (ushort)newState, (byte)numberBits);
            }

            table = new ZstdFseTable(tableLog, entries);
            return ZstdFrameStatus.Success;
        }

        internal static ZstdFrameStatus CreateRle(int symbol, int maximumSymbol, out ZstdFseTable? table)
        {
            table = null;
            if (symbol < 0 || symbol > maximumSymbol)
            {
                return ZstdFrameStatus.Corrupt;
            }

            table = new ZstdFseTable(0, new[] { new ZstdFseEntry((byte)symbol, 0, 0) });
            return ZstdFrameStatus.Success;
        }

        internal static ZstdFrameStatus DecodeByteStream(
            ReadOnlySpan<byte> source,
            int maximumOutputSize,
            out byte[] decoded)
        {
            decoded = Array.Empty<byte>();
            ZstdFrameStatus tableStatus = Read(
                source,
                maximumSymbol: 255,
                maximumTableLog: 6,
                out ZstdFseTable? table,
                out int tableBytes);
            if (tableStatus != ZstdFrameStatus.Success || table is null)
            {
                return tableStatus;
            }

            ReadOnlySpan<byte> bitstream = source.Slice(tableBytes);
            if (!ZstdReverseBitReader.TryCreate(bitstream, out ZstdReverseBitReader reader))
            {
                return ZstdFrameStatus.Corrupt;
            }

            if (!reader.TryReadBits(table.TableLog, out uint state1Bits)
                || !reader.TryReadBits(table.TableLog, out uint state2Bits))
            {
                return ZstdFrameStatus.Corrupt;
            }

            int state1 = (int)state1Bits;
            int state2 = (int)state2Bits;
            List<byte> output = new(Math.Min(maximumOutputSize, 256));

            while (true)
            {
                if (!TryAppendSymbol(table, state1, output, maximumOutputSize))
                {
                    return ZstdFrameStatus.Corrupt;
                }

                if (!TryUpdateStatePadded(table, ref state1, ref reader))
                {
                    if (!TryAppendSymbol(table, state2, output, maximumOutputSize))
                    {
                        return ZstdFrameStatus.Corrupt;
                    }

                    break;
                }

                if (!TryAppendSymbol(table, state2, output, maximumOutputSize))
                {
                    return ZstdFrameStatus.Corrupt;
                }

                if (!TryUpdateStatePadded(table, ref state2, ref reader))
                {
                    if (!TryAppendSymbol(table, state1, output, maximumOutputSize))
                    {
                        return ZstdFrameStatus.Corrupt;
                    }

                    break;
                }
            }

            if (!reader.IsAtEnd)
            {
                return ZstdFrameStatus.Corrupt;
            }

            decoded = output.ToArray();
            return ZstdFrameStatus.Success;
        }

        internal static bool TryInitializeState(
            ZstdFseTable table,
            ref ZstdReverseBitReader reader,
            out int state)
        {
            state = 0;
            if (!reader.TryReadBits(table.TableLog, out uint stateBits) || stateBits >= table.Entries.Length)
            {
                return false;
            }

            state = (int)stateBits;
            return true;
        }

        internal static bool TryUpdateState(
            ZstdFseTable table,
            ref int state,
            ref ZstdReverseBitReader reader)
        {
            if ((uint)state >= (uint)table.Entries.Length)
            {
                return false;
            }

            ZstdFseEntry entry = table.Entries[state];
            if (!reader.TryReadBits(entry.NumberBits, out uint lowBits))
            {
                return false;
            }

            int newState = checked(entry.NewState + (int)lowBits);
            if ((uint)newState >= (uint)table.Entries.Length)
            {
                return false;
            }

            state = newState;
            return true;
        }

        private static bool TryUpdateStatePadded(
            ZstdFseTable table,
            ref int state,
            ref ZstdReverseBitReader reader)
        {
            if ((uint)state >= (uint)table.Entries.Length)
            {
                return false;
            }

            ZstdFseEntry entry = table.Entries[state];
            bool complete = reader.TryReadBitsPadded(entry.NumberBits, out uint lowBits);
            int newState = checked(entry.NewState + (int)lowBits);
            if ((uint)newState >= (uint)table.Entries.Length)
            {
                return false;
            }

            state = newState;
            return complete;
        }

        private static bool TryAppendSymbol(
            ZstdFseTable table,
            int state,
            List<byte> output,
            int maximumOutputSize)
        {
            if ((uint)state >= (uint)table.Entries.Length || output.Count >= maximumOutputSize)
            {
                return false;
            }

            output.Add(table.Entries[state].Symbol);
            return true;
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
