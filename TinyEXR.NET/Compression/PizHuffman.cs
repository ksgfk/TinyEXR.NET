// Port of tinyexr v3 exr_piz.c canonical Huffman/RLE, revision eb0ba07f44a33e1ce5bc0fb74142a3a6558f0661.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
using System.Buffers.Binary;

namespace TinyEXR.Codecs
{
    internal static class PizHuffman
    {
        private const int Symbols = 65537;
        private const int LookupBits = 14;

        internal sealed class Workspace
        {
            internal readonly ulong[] Codes = new ulong[Symbols];
            internal readonly long[] Frequencies = new long[Symbols];
            internal readonly int[] Links = new int[Symbols];
            internal readonly int[] Heap = new int[Symbols];
            internal readonly int[] Lookup = new int[1 << LookupBits];
            internal readonly List<int>?[] LongCodes = new List<int>?[1 << LookupBits];
        }

        public static byte[] Encode(ReadOnlySpan<ushort> samples, Workspace workspace)
        {
            if (samples.IsEmpty)
                return Array.Empty<byte>();
            ulong[] codes = workspace.Codes;
            long[] frequency = workspace.Frequencies;
            int[] links = workspace.Links, heap = workspace.Heap;
            Array.Clear(codes, 0, codes.Length);
            Array.Clear(frequency, 0, frequency.Length);
            foreach (ushort sample in samples)
                frequency[sample]++;
            int minimum = 0, maximum = 0, count = 0;
            while (frequency[minimum] == 0)
                minimum++;
            for (int i = minimum; i < Symbols; i++)
            {
                links[i] = i;
                if (frequency[i] != 0)
                {
                    heap[count++] = i;
                    maximum = i;
                }
            }
            maximum++;
            frequency[maximum] = 1;
            heap[count++] = maximum;
            for (int i = count / 2 - 1; i >= 0; i--)
                Down(heap, count, i, frequency);
            while (count > 1)
            {
                int first = heap[0];
                heap[0] = heap[--count];
                Down(heap, count, 0, frequency);
                int second = heap[0];
                heap[0] = heap[--count];
                Down(heap, count, 0, frequency);
                frequency[second] += frequency[first];
                heap[count] = second;
                int at = count++;
                while (at > 0)
                {
                    int parent = (at - 1) / 2;
                    if (frequency[heap[parent]] <= frequency[heap[at]])
                        break;
                    (heap[parent], heap[at]) = (heap[at], heap[parent]);
                    at = parent;
                }
                for (int i = second; ; i = links[i])
                {
                    if (++codes[i] > 58)
                        throw new InvalidDataException("PIZ Huffman code exceeds 58 bits.");
                    if (links[i] == i)
                    {
                        links[i] = first;
                        break;
                    }
                }
                for (int i = first; ; i = links[i])
                {
                    if (++codes[i] > 58)
                        throw new InvalidDataException("PIZ Huffman code exceeds 58 bits.");
                    if (links[i] == i)
                        break;
                }
            }
            Canonical(codes, minimum, maximum);
            BitWriter table = new BitWriter();
            for (int symbol = minimum; symbol <= maximum; symbol++)
            {
                int length = (int)(codes[symbol] & 63);
                if (length == 0)
                {
                    int run = 1;
                    while (symbol < maximum && run < 261 && (codes[symbol + 1] & 63) == 0)
                    {
                        symbol++;
                        run++;
                    }
                    if (run >= 6)
                    {
                        table.Put(6, 63);
                        table.Put(8, (uint)(run - 6));
                        continue;
                    }
                    if (run >= 2)
                    {
                        table.Put(6, (uint)(59 + run - 2));
                        continue;
                    }
                }
                table.Put(6, (uint)length);
            }
            BitWriter data = new BitWriter();
            int previous = samples[0], repeats = 0;
            for (int i = 1; i < samples.Length; i++)
            {
                if (previous == samples[i] && repeats < 255)
                    repeats++;
                else
                {
                    Send(data, codes[previous], repeats, codes[maximum]);
                    repeats = 0;
                }
                previous = samples[i];
            }
            Send(data, codes[previous], repeats, codes[maximum]);
            byte[] tableBytes = table.Finish(), dataBytes = data.Finish();
            byte[] output = new byte[checked(20 + tableBytes.Length + dataBytes.Length)];
            BinaryPrimitives.WriteInt32LittleEndian(output, minimum);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), maximum);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), tableBytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12), data.BitCount);
            tableBytes.CopyTo(output, 20);
            dataBytes.CopyTo(output, 20 + tableBytes.Length);
            return output;
        }

        public static bool Decode(ReadOnlySpan<byte> source, Span<ushort> output, Workspace workspace)
        {
            if (output.IsEmpty)
                return source.IsEmpty;
            if (source.Length < 20)
                return false;
            int minimum = BinaryPrimitives.ReadInt32LittleEndian(source);
            int maximum = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4));
            int tableLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8));
            int bits = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(12));
            if (minimum < 0 || maximum >= Symbols || minimum > maximum ||
                tableLength < 0 || tableLength > source.Length - 20 || bits < 0 ||
                bits > 8L * (source.Length - 20 - tableLength))
                return false;
            ulong[] codes = workspace.Codes;
            int[] active = workspace.Heap;
            int activeCount = 0;
            BitReader table = new BitReader(source.Slice(20, tableLength), checked(tableLength * 8));
            for (int symbol = minimum; symbol <= maximum; symbol++)
            {
                if (!table.Take(6, out ulong length))
                    return false;
                if (length >= 59)
                {
                    int run;
                    if (length == 63)
                    {
                        if (!table.Take(8, out ulong extra))
                            return false;
                        run = (int)extra + 6;
                    }
                    else
                        run = (int)length - 59 + 2;
                    if (run > maximum - symbol + 1)
                        return false;
                    symbol += run - 1;
                }
                else if (length != 0)
                {
                    codes[symbol] = length;
                    active[activeCount++] = symbol;
                }
            }
            Span<ulong> counts = stackalloc ulong[59];
            counts.Clear();
            for (int i = 0; i < activeCount; i++) counts[(int)codes[active[i]]]++;
            ulong next = 0;
            for (int length = 58; length > 0; length--)
            {
                ulong following = (next + counts[length]) >> 1;
                counts[length] = next;
                next = following;
            }
            for (int i = 0; i < activeCount; i++)
            {
                int symbol = active[i], length = (int)codes[symbol];
                codes[symbol] = (ulong)(uint)length | (counts[length]++ << 6);
            }
            int[] lookup = workspace.Lookup;
            Array.Clear(lookup, 0, lookup.Length);
            foreach (List<int>? bucket in workspace.LongCodes)
                bucket?.Clear();
            for (int index = 0; index < activeCount; index++)
            {
                int symbol = active[index];
                int length = (int)(codes[symbol] & 63);
                if (length == 0)
                    continue;
                ulong code = codes[symbol] >> 6;
                if ((code >> length) != 0)
                    return false;
                if (length <= LookupBits)
                {
                    int start = (int)(code << (LookupBits - length)), fill = 1 << (LookupBits - length);
                    for (int i = start; i < start + fill; i++)
                    {
                        if (lookup[i] != 0 || workspace.LongCodes[i]?.Count > 0)
                            return false;
                        lookup[i] = (symbol << 6) | length;
                    }
                }
                else
                {
                    int prefix = (int)(code >> (length - LookupBits));
                    if (lookup[prefix] != 0)
                        return false;
                    (workspace.LongCodes[prefix] ??= new List<int>()).Add(symbol);
                }
            }
            BitReader data = new BitReader(source.Slice(20 + tableLength), bits);
            int target = 0;
            while (data.Remaining > 0)
            {
                int prefix = (int)data.Peek(LookupBits);
                int packed = lookup[prefix], symbol = packed >> 6, length = packed & 63;
                if (length == 0)
                {
                    List<int>? bucket = workspace.LongCodes[prefix];
                    if (bucket == null)
                        return false;
                    foreach (int candidate in bucket)
                    {
                        int candidateLength = (int)(codes[candidate] & 63);
                        if (candidateLength <= data.Remaining && data.Peek(candidateLength) == (codes[candidate] >> 6))
                        {
                            symbol = candidate;
                            length = candidateLength;
                            break;
                        }
                    }
                }
                if (length == 0 || !data.Skip(length))
                    return false;
                if (symbol == maximum)
                {
                    if (target == 0 || !data.Take(8, out ulong run) || run > (ulong)(output.Length - target))
                        return false;
                    output.Slice(target, (int)run).Fill(output[target - 1]);
                    target += (int)run;
                }
                else
                {
                    if (target == output.Length)
                        return false;
                    output[target++] = (ushort)symbol;
                }
            }
            return target == output.Length;
        }

        private static void Canonical(ulong[] codes, int minimum, int maximum)
        {
            Span<ulong> counts = stackalloc ulong[59];
            counts.Clear();
            for (int i = minimum; i <= maximum; i++)
                counts[(int)codes[i]]++;
            ulong next = 0;
            for (int length = 58; length > 0; length--)
            {
                ulong following = (next + counts[length]) >> 1;
                counts[length] = next;
                next = following;
            }
            for (int i = minimum; i <= maximum; i++)
            {
                int length = (int)codes[i];
                if (length > 0)
                    codes[i] = (ulong)(uint)length | (counts[length]++ << 6);
            }
        }

        private static void Down(int[] heap, int count, int position, long[] frequency)
        {
            while (true)
            {
                int left = 2 * position + 1, right = left + 1, smallest = position;
                if (left < count && frequency[heap[left]] < frequency[heap[smallest]])
                    smallest = left;
                if (right < count && frequency[heap[right]] < frequency[heap[smallest]])
                    smallest = right;
                if (smallest == position)
                    return;
                (heap[position], heap[smallest]) = (heap[smallest], heap[position]);
                position = smallest;
            }
        }

        private static void Send(BitWriter writer, ulong symbol, int repeats, ulong runCode)
        {
            int length = (int)(symbol & 63), runLength = (int)(runCode & 63);
            if (length + runLength + 8 < length * repeats)
            {
                writer.Put(length, symbol >> 6);
                writer.Put(runLength, runCode >> 6);
                writer.Put(8, (uint)repeats);
            }
            else
            for (int i = 0; i <= repeats; i++)
                writer.Put(length, symbol >> 6);
        }

        private sealed class BitWriter
        {
            private readonly MemoryStream _output = new MemoryStream();
            private uint _buffer;
            private int _count;
            public int BitCount
            {
                get; private set;
            }
            public void Put(int count, ulong value)
            {
                BitCount = checked(BitCount + count);
                while (count > 0)
                {
                    int take = Math.Min(8 - _count, count);
                    count -= take;
                    _buffer = (_buffer << take) | (uint)((value >> count) & ((1U << take) - 1));
                    _count += take;
                    if (_count == 8)
                    {
                        _output.WriteByte((byte)_buffer);
                        _count = 0;
                        _buffer = 0;
                    }
                }
            }
            public byte[] Finish()
            {
                if (_count > 0)
                {
                    _output.WriteByte((byte)(_buffer << (8 - _count)));
                    _count = 0;
                }
                return _output.ToArray();
            }
        }

        private ref struct BitReader
        {
            private readonly ReadOnlySpan<byte> _source;
            private readonly int _length;
            private int _position;
            public BitReader(ReadOnlySpan<byte> source, int length)
            {
                _source = source;
                _length = length;
                _position = 0;
            }
            public int Remaining => _length - _position;
            public ulong Peek(int count)
            {
                if (count <= 24 && Remaining >= count && _source.Length - (_position >> 3) >= 4)
                    return (BinaryPrimitives.ReadUInt32BigEndian(_source.Slice(_position >> 3)) >> (32 - (_position & 7) - count)) & ((1U << count) - 1);
                ulong value = 0;
                int available = Math.Min(count, Remaining), position = _position;
                while (available > 0)
                {
                    int take = Math.Min(available, 8 - (position & 7));
                    value = (value << take) | (uint)((_source[position >> 3] >> (8 - (position & 7) - take)) & ((1 << take) - 1));
                    position += take;
                    available -= take;
                }
                return value << Math.Max(0, count - Remaining);
            }
            public bool Skip(int count)
            {
                if (count > Remaining) return false;
                _position += count;
                return true;
            }
            public bool Take(int count, out ulong value)
            {
                value = 0;
                if (count > Remaining)
                    return false;
                value = Peek(count);
                _position += count;
                return true;
            }
        }
    }
}
