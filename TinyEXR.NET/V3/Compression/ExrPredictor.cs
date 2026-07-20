using System;
using System.Runtime.InteropServices;

#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
#endif

namespace TinyEXR.V3.Codecs
{
    internal static class ExrPredictor
    {
#if NET8_0_OR_GREATER
        private static readonly Vector128<byte> EvenShuffle = Vector128.Create(
            (byte)0, 2, 4, 6, 8, 10, 12, 14,
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

        private static readonly Vector128<byte> OddShuffle = Vector128.Create(
            (byte)1, 3, 5, 7, 9, 11, 13, 15,
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

        private static readonly Vector128<byte> Shift1 = Vector128.Create(
            byte.MaxValue, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);

        private static readonly Vector128<byte> Shift2 = Vector128.Create(
            byte.MaxValue, byte.MaxValue, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);

        private static readonly Vector128<byte> Shift4 = Vector128.Create(
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);

        private static readonly Vector128<byte> Shift8 = Vector128.Create(
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
            0, 1, 2, 3, 4, 5, 6, 7);
#endif

        internal static bool IsVectorized
        {
            get
            {
#if NET8_0_OR_GREATER
                return Sse2.IsSupported || AdvSimd.Arm64.IsSupported;
#else
                return false;
#endif
            }
        }

        internal static void Apply(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            Apply(source, destination, IsVectorized);
        }

        internal static void Apply(
            ReadOnlySpan<byte> source,
            Span<byte> destination,
            bool vectorized)
        {
            Validate(source, destination);
            int half = (source.Length + 1) / 2;
            int sourceOffset = 0;
            int targetA = 0;
            int targetB = half;
#if NET8_0_OR_GREATER
            if (vectorized && IsVectorized)
            {
                int blockCount = source.Length / 32;
                for (int block = 0; block < blockCount; block++)
                {
                    Vector128<byte> first = Load(source.Slice(sourceOffset, 16));
                    Vector128<byte> second = Load(source.Slice(sourceOffset + 16, 16));
                    Vector128<byte> firstEven = Vector128.Shuffle(first, EvenShuffle);
                    Vector128<byte> secondEven = Vector128.Shuffle(second, EvenShuffle);
                    Vector128<byte> firstOdd = Vector128.Shuffle(first, OddShuffle);
                    Vector128<byte> secondOdd = Vector128.Shuffle(second, OddShuffle);
                    Store(
                        destination.Slice(targetA, 16),
                        Vector128.Create(
                            firstEven.AsUInt64().GetElement(0),
                            secondEven.AsUInt64().GetElement(0)).AsByte());
                    Store(
                        destination.Slice(targetB, 16),
                        Vector128.Create(
                            firstOdd.AsUInt64().GetElement(0),
                            secondOdd.AsUInt64().GetElement(0)).AsByte());
                    sourceOffset += 32;
                    targetA += 16;
                    targetB += 16;
                }
            }
#endif
            for (int index = sourceOffset; index < source.Length; index += 2)
            {
                destination[targetA++] = source[index];
                if (index + 1 < source.Length)
                {
                    destination[targetB++] = source[index + 1];
                }
            }

            ApplyPredictor(destination.Slice(0, source.Length), vectorized);
        }

        internal static void Undo(Span<byte> predicted, int length, Span<byte> destination)
        {
            Undo(predicted, length, destination, IsVectorized);
        }

        internal static void Undo(
            Span<byte> predicted,
            int length,
            Span<byte> destination,
            bool vectorized)
        {
            if (length < 0 || length > predicted.Length || length > destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            if (predicted.Slice(0, length).Overlaps(destination.Slice(0, length)))
            {
                throw new ArgumentException("The predictor input and destination must not overlap.", nameof(destination));
            }

            UndoPredictor(predicted.Slice(0, length), vectorized);
            int half = (length + 1) / 2;
            int sourceA = 0;
            int sourceB = half;
            int destinationOffset = 0;
#if NET8_0_OR_GREATER
            if (vectorized && IsVectorized)
            {
                int blockCount = (length / 2) / 16;
                for (int block = 0; block < blockCount; block++)
                {
                    Vector128<byte> even = Load(predicted.Slice(sourceA, 16));
                    Vector128<byte> odd = Load(predicted.Slice(sourceB, 16));
                    Store(destination.Slice(destinationOffset, 16), InterleaveLow(even, odd));
                    Store(destination.Slice(destinationOffset + 16, 16), InterleaveHigh(even, odd));
                    sourceA += 16;
                    sourceB += 16;
                    destinationOffset += 32;
                }
            }
#endif
            while (destinationOffset < length)
            {
                destination[destinationOffset++] = predicted[sourceA++];
                if (destinationOffset < length)
                {
                    destination[destinationOffset++] = predicted[sourceB++];
                }
            }
        }

        private static void Validate(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (destination.Length < source.Length)
            {
                throw new ArgumentException("The destination is shorter than the source.", nameof(destination));
            }

            if (source.Overlaps(destination.Slice(0, source.Length)))
            {
                throw new ArgumentException("The predictor source and destination must not overlap.", nameof(destination));
            }
        }

        private static void ApplyPredictor(Span<byte> data, bool vectorized)
        {
            if (data.Length < 2)
            {
                return;
            }

            int vectorStart = data.Length - 16;
#if NET8_0_OR_GREATER
            if (vectorized && IsVectorized)
            {
                Vector128<byte> bias = Vector128.Create((byte)128);
                while (vectorStart >= 1)
                {
                    Vector128<byte> current = Load(data.Slice(vectorStart, 16));
                    Vector128<byte> previous = Load(data.Slice(vectorStart - 1, 16));
                    Store(
                        data.Slice(vectorStart, 16),
                        Vector128.Add(Vector128.Subtract(current, previous), bias));
                    vectorStart -= 16;
                }
            }
#endif
            int scalarEnd = vectorStart + 16;
            int previousValue = data[0];
            for (int index = 1; index < scalarEnd; index++)
            {
                int current = data[index];
                data[index] = unchecked((byte)(current - previousValue + 384));
                previousValue = current;
            }
        }

        private static void UndoPredictor(Span<byte> data, bool vectorized)
        {
            if (data.Length < 2)
            {
                return;
            }

            int offset = 1;
#if NET8_0_OR_GREATER
            if (vectorized && IsVectorized)
            {
                byte carry = data[0];
                Vector128<byte> bias = Vector128.Create((byte)128);
                while (offset <= data.Length - 16)
                {
                    Vector128<byte> prefix = Vector128.Add(Load(data.Slice(offset, 16)), bias);
                    prefix = Vector128.Add(prefix, Vector128.Shuffle(prefix, Shift1));
                    prefix = Vector128.Add(prefix, Vector128.Shuffle(prefix, Shift2));
                    prefix = Vector128.Add(prefix, Vector128.Shuffle(prefix, Shift4));
                    prefix = Vector128.Add(prefix, Vector128.Shuffle(prefix, Shift8));
                    prefix = Vector128.Add(prefix, Vector128.Create(carry));
                    Store(data.Slice(offset, 16), prefix);
                    carry = prefix.GetElement(15);
                    offset += 16;
                }
            }
#endif
            for (; offset < data.Length; offset++)
            {
                data[offset] = unchecked((byte)(data[offset - 1] + data[offset] - 128));
            }
        }

#if NET8_0_OR_GREATER
        private static Vector128<byte> Load(ReadOnlySpan<byte> source)
        {
            return MemoryMarshal.Cast<byte, Vector128<byte>>(source)[0];
        }

        private static void Store(Span<byte> destination, Vector128<byte> value)
        {
            MemoryMarshal.Cast<byte, Vector128<byte>>(destination)[0] = value;
        }

        private static Vector128<byte> InterleaveLow(Vector128<byte> left, Vector128<byte> right)
        {
            if (Sse2.IsSupported)
            {
                return Sse2.UnpackLow(left, right);
            }

            return AdvSimd.Arm64.ZipLow(left, right);
        }

        private static Vector128<byte> InterleaveHigh(Vector128<byte> left, Vector128<byte> right)
        {
            if (Sse2.IsSupported)
            {
                return Sse2.UnpackHigh(left, right);
            }

            return AdvSimd.Arm64.ZipHigh(left, right);
        }
#endif
    }
}
