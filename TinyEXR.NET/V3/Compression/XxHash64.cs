using System.Buffers.Binary;

namespace TinyEXR.V3.Codecs
{
    /// <summary>
    /// Minimal one-shot xxHash64 implementation for the Zstandard content checksum.
    /// </summary>
    internal static class XxHash64
    {
        private const ulong Prime1 = 11400714785074694791UL;
        private const ulong Prime2 = 14029467366897019727UL;
        private const ulong Prime3 = 1609587929392839161UL;
        private const ulong Prime4 = 9650029242287828579UL;
        private const ulong Prime5 = 2870177450012600261UL;

        internal static ulong Compute(ReadOnlySpan<byte> source, ulong seed = 0)
        {
            int offset = 0;
            ulong hash;

            unchecked
            {
                if (source.Length >= 32)
                {
                    ulong lane1 = seed + Prime1 + Prime2;
                    ulong lane2 = seed + Prime2;
                    ulong lane3 = seed;
                    ulong lane4 = seed - Prime1;
                    int stripeLimit = source.Length - 32;

                    do
                    {
                        lane1 = Round(lane1, BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8)));
                        offset += 8;
                        lane2 = Round(lane2, BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8)));
                        offset += 8;
                        lane3 = Round(lane3, BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8)));
                        offset += 8;
                        lane4 = Round(lane4, BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8)));
                        offset += 8;
                    }
                    while (offset <= stripeLimit);

                    hash = RotateLeft(lane1, 1)
                        + RotateLeft(lane2, 7)
                        + RotateLeft(lane3, 12)
                        + RotateLeft(lane4, 18);
                    hash = MergeRound(hash, lane1);
                    hash = MergeRound(hash, lane2);
                    hash = MergeRound(hash, lane3);
                    hash = MergeRound(hash, lane4);
                }
                else
                {
                    hash = seed + Prime5;
                }

                hash += (ulong)source.Length;

                while (source.Length - offset >= 8)
                {
                    ulong lane = Round(0, BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8)));
                    hash ^= lane;
                    hash = RotateLeft(hash, 27) * Prime1 + Prime4;
                    offset += 8;
                }

                if (source.Length - offset >= 4)
                {
                    hash ^= (ulong)BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4)) * Prime1;
                    hash = RotateLeft(hash, 23) * Prime2 + Prime3;
                    offset += 4;
                }

                while (offset < source.Length)
                {
                    hash ^= source[offset] * Prime5;
                    hash = RotateLeft(hash, 11) * Prime1;
                    offset++;
                }

                hash ^= hash >> 33;
                hash *= Prime2;
                hash ^= hash >> 29;
                hash *= Prime3;
                hash ^= hash >> 32;
            }

            return hash;
        }

        private static ulong Round(ulong accumulator, ulong lane)
        {
            unchecked
            {
                accumulator += lane * Prime2;
                accumulator = RotateLeft(accumulator, 31);
                return accumulator * Prime1;
            }
        }

        private static ulong MergeRound(ulong accumulator, ulong lane)
        {
            unchecked
            {
                accumulator ^= Round(0, lane);
                return accumulator * Prime1 + Prime4;
            }
        }

        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }
    }
}
