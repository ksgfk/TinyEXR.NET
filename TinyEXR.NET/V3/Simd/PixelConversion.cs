using System;
using System.Runtime.InteropServices;

#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
#endif

namespace TinyEXR.V3
{
    /// <summary>
    /// Converts between IEEE 754 binary16 bit patterns and managed single-precision values.
    /// </summary>
    public static class PixelConversion
    {
        /// <summary>
        /// Converts every binary16 bit pattern in <paramref name="source"/> to a float.
        /// The destination may be longer than the source; trailing elements are unchanged.
        /// The source and written destination region must not overlap.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The destination is too short, or the source overlaps the written destination region.
        /// </exception>
        public static void HalfToFloat(ReadOnlySpan<ushort> source, Span<float> destination)
        {
            ValidateHalfToFloatArguments(source, destination);
            ConvertHalfToFloat(source, destination, SelectedPath);
        }

        /// <summary>
        /// Converts every float in <paramref name="source"/> to an IEEE 754 binary16 bit pattern
        /// using round-to-nearest, ties-to-even. The destination may be longer than the source;
        /// trailing elements are unchanged. The source and written destination region must not overlap.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The destination is too short, or the source overlaps the written destination region.
        /// </exception>
        public static void FloatToHalf(ReadOnlySpan<float> source, Span<ushort> destination)
        {
            ValidateFloatToHalfArguments(source, destination);
            ConvertFloatToHalf(source, destination, SelectedPath);
        }

        /// <summary>
        /// Widens UInt samples to float. Normalized conversion maps the full UInt range to [0, 1].
        /// </summary>
        public static void UIntToFloat(
            ReadOnlySpan<uint> source,
            Span<float> destination,
            PixelConversionMode mode = PixelConversionMode.Raw)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            ConvertUIntToFloat(source, destination, mode, SelectedPath);
        }

        /// <summary>
        /// Narrows float samples to UInt with clamping and round-to-nearest, ties-to-even.
        /// Normalized conversion maps [0, 1] to the full UInt range.
        /// </summary>
        public static void FloatToUInt(
            ReadOnlySpan<float> source,
            Span<uint> destination,
            PixelConversionMode mode = PixelConversionMode.Raw)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            double scale = mode == PixelConversionMode.Normalized ? uint.MaxValue : 1.0;
            for (int index = 0; index < source.Length; index++)
            {
                destination[index] = NarrowToUInt32(source[index] * scale);
            }
        }

        /// <summary>
        /// Converts Half bit patterns to UInt through the canonical float representation.
        /// </summary>
        public static void HalfToUInt(
            ReadOnlySpan<ushort> source,
            Span<uint> destination,
            PixelConversionMode mode = PixelConversionMode.Raw)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            if (source.Length == 0)
            {
                return;
            }

            float[] scratch = new float[Math.Min(source.Length, 256)];
            int offset = 0;
            while (offset < source.Length)
            {
                int count = Math.Min(scratch.Length, source.Length - offset);
                HalfToFloat(source.Slice(offset, count), scratch.AsSpan(0, count));
                FloatToUInt(scratch.AsSpan(0, count), destination.Slice(offset, count), mode);
                offset += count;
            }
        }

        /// <summary>
        /// Converts UInt samples to Half bit patterns through the canonical float representation.
        /// </summary>
        public static void UIntToHalf(
            ReadOnlySpan<uint> source,
            Span<ushort> destination,
            PixelConversionMode mode = PixelConversionMode.Raw)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            if (source.Length == 0)
            {
                return;
            }

            float[] scratch = new float[Math.Min(source.Length, 256)];
            int offset = 0;
            while (offset < source.Length)
            {
                int count = Math.Min(scratch.Length, source.Length - offset);
                UIntToFloat(source.Slice(offset, count), scratch.AsSpan(0, count), mode);
                FloatToHalf(scratch.AsSpan(0, count), destination.Slice(offset, count));
                offset += count;
            }
        }

        public static void ByteToFloat(
            ReadOnlySpan<byte> source,
            Span<float> destination,
            PixelConversionMode mode = PixelConversionMode.Raw)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            ConvertByteToFloat(source, destination, mode, SelectedPath);
        }

        public static void UInt16ToFloat(
            ReadOnlySpan<ushort> source,
            Span<float> destination,
            PixelConversionMode mode = PixelConversionMode.Raw)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            ConvertUInt16ToFloat(source, destination, mode, SelectedPath);
        }

        public static void FloatToByte(
            ReadOnlySpan<float> source,
            Span<byte> destination,
            PixelConversionMode mode = PixelConversionMode.Raw)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            double scale = mode == PixelConversionMode.Normalized ? byte.MaxValue : 1.0;
            for (int index = 0; index < source.Length; index++)
            {
                destination[index] = (byte)NarrowUnsigned(source[index] * scale, byte.MaxValue);
            }
        }

        public static void FloatToUInt16(
            ReadOnlySpan<float> source,
            Span<ushort> destination,
            PixelConversionMode mode = PixelConversionMode.Raw)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            double scale = mode == PixelConversionMode.Normalized ? ushort.MaxValue : 1.0;
            for (int index = 0; index < source.Length; index++)
            {
                destination[index] = (ushort)NarrowUnsigned(source[index] * scale, ushort.MaxValue);
            }
        }

        internal static SimdConversionPath SelectedPath
        {
            get
            {
#if NET8_0_OR_GREATER
                if (AdvSimd.IsSupported)
                {
                    return SimdConversionPath.Neon;
                }

                if (Sse2.IsSupported)
                {
                    return SimdConversionPath.Sse2;
                }
#endif
                return SimdConversionPath.Scalar;
            }
        }

        internal static void HalfToFloat(
            ReadOnlySpan<ushort> source,
            Span<float> destination,
            SimdConversionPath path)
        {
            ValidateHalfToFloatArguments(source, destination);
            ValidatePath(path);
            ConvertHalfToFloat(source, destination, path);
        }

        internal static void FloatToHalf(
            ReadOnlySpan<float> source,
            Span<ushort> destination,
            SimdConversionPath path)
        {
            ValidateFloatToHalfArguments(source, destination);
            ValidatePath(path);
            ConvertFloatToHalf(source, destination, path);
        }

        internal static void ByteToFloat(
            ReadOnlySpan<byte> source,
            Span<float> destination,
            PixelConversionMode mode,
            SimdConversionPath path)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            ValidatePath(path);
            ConvertByteToFloat(source, destination, mode, path);
        }

        internal static void UInt16ToFloat(
            ReadOnlySpan<ushort> source,
            Span<float> destination,
            PixelConversionMode mode,
            SimdConversionPath path)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            ValidatePath(path);
            ConvertUInt16ToFloat(source, destination, mode, path);
        }

        internal static void UIntToFloat(
            ReadOnlySpan<uint> source,
            Span<float> destination,
            PixelConversionMode mode,
            SimdConversionPath path)
        {
            ValidateConversionArguments(source, destination);
            ValidateMode(mode);
            ValidatePath(path);
            ConvertUIntToFloat(source, destination, mode, path);
        }

        private static void ValidateHalfToFloatArguments(
            ReadOnlySpan<ushort> source,
            Span<float> destination)
        {
            if (destination.Length < source.Length)
            {
                throw new ArgumentException("The destination must contain at least one element per source value.", nameof(destination));
            }

            if (MemoryMarshal.AsBytes(source).Overlaps(
                MemoryMarshal.AsBytes(destination.Slice(0, source.Length))))
            {
                throw new ArgumentException("The source and written destination region must not overlap.", nameof(destination));
            }
        }

        private static void ValidateFloatToHalfArguments(
            ReadOnlySpan<float> source,
            Span<ushort> destination)
        {
            if (destination.Length < source.Length)
            {
                throw new ArgumentException("The destination must contain at least one element per source value.", nameof(destination));
            }

            if (MemoryMarshal.AsBytes(source).Overlaps(
                MemoryMarshal.AsBytes(destination.Slice(0, source.Length))))
            {
                throw new ArgumentException("The source and written destination region must not overlap.", nameof(destination));
            }
        }

        private static void ValidateConversionArguments<TSource, TDestination>(
            ReadOnlySpan<TSource> source,
            Span<TDestination> destination)
            where TSource : struct
            where TDestination : struct
        {
            if (destination.Length < source.Length)
            {
                throw new ArgumentException(
                    "The destination must contain at least one element per source value.",
                    nameof(destination));
            }

            if (MemoryMarshal.AsBytes(source).Overlaps(
                MemoryMarshal.AsBytes(destination.Slice(0, source.Length))))
            {
                throw new ArgumentException(
                    "The source and written destination region must not overlap.",
                    nameof(destination));
            }
        }

        private static void ValidateMode(PixelConversionMode mode)
        {
            ModelValidation.ValidateEnum(mode, nameof(mode));
        }

        private static void ValidatePath(SimdConversionPath path)
        {
            if (path == SimdConversionPath.Scalar)
            {
                return;
            }

#if NET8_0_OR_GREATER
            if ((path == SimdConversionPath.Sse2 && Sse2.IsSupported) ||
                (path == SimdConversionPath.Neon && AdvSimd.IsSupported))
            {
                return;
            }
#endif
            throw new PlatformNotSupportedException($"The requested conversion path '{path}' is not available.");
        }

        private static void ConvertHalfToFloat(
            ReadOnlySpan<ushort> source,
            Span<float> destination,
            SimdConversionPath path)
        {
#if NET8_0_OR_GREATER
            if (path != SimdConversionPath.Scalar)
            {
                ConvertHalfToFloatVector128(source, destination);
                return;
            }
#endif
            ConvertHalfToFloatScalar(source, destination);
        }

        private static void ConvertFloatToHalf(
            ReadOnlySpan<float> source,
            Span<ushort> destination,
            SimdConversionPath path)
        {
#if NET8_0_OR_GREATER
            if (path != SimdConversionPath.Scalar)
            {
                ConvertFloatToHalfVector128(source, destination);
                return;
            }
#endif
            ConvertFloatToHalfScalar(source, destination);
        }

        private static void ConvertByteToFloat(
            ReadOnlySpan<byte> source,
            Span<float> destination,
            PixelConversionMode mode,
            SimdConversionPath path)
        {
            float scale = mode == PixelConversionMode.Normalized ? 1.0f / byte.MaxValue : 1.0f;
            int scalarStart = 0;
#if NET8_0_OR_GREATER
            if (path != SimdConversionPath.Scalar)
            {
                scalarStart = ConvertByteToFloatVector128(source, destination, scale);
            }
#endif
            for (int index = scalarStart; index < source.Length; index++)
            {
                destination[index] = source[index] * scale;
            }
        }

        private static void ConvertUInt16ToFloat(
            ReadOnlySpan<ushort> source,
            Span<float> destination,
            PixelConversionMode mode,
            SimdConversionPath path)
        {
            float scale = mode == PixelConversionMode.Normalized ? 1.0f / ushort.MaxValue : 1.0f;
            int scalarStart = 0;
#if NET8_0_OR_GREATER
            if (path != SimdConversionPath.Scalar)
            {
                scalarStart = ConvertUInt16ToFloatVector128(source, destination, scale);
            }
#endif
            for (int index = scalarStart; index < source.Length; index++)
            {
                destination[index] = source[index] * scale;
            }
        }

        private static void ConvertUIntToFloat(
            ReadOnlySpan<uint> source,
            Span<float> destination,
            PixelConversionMode mode,
            SimdConversionPath path)
        {
            if (mode == PixelConversionMode.Normalized)
            {
                const double inverse = 1.0 / uint.MaxValue;
                for (int index = 0; index < source.Length; index++)
                {
                    destination[index] = (float)(source[index] * inverse);
                }

                return;
            }

            int scalarStart = 0;
#if NET8_0_OR_GREATER
            if (path != SimdConversionPath.Scalar)
            {
                scalarStart = ConvertUIntToFloatVector128(source, destination);
            }
#endif
            for (int index = scalarStart; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

#if NET8_0_OR_GREATER
        private static int ConvertByteToFloatVector128(
            ReadOnlySpan<byte> source,
            Span<float> destination,
            float scale)
        {
            int vectorCount = source.Length / Vector128<byte>.Count;
            int elementCount = vectorCount * Vector128<byte>.Count;
            ReadOnlySpan<Vector128<byte>> sourceVectors = MemoryMarshal.Cast<byte, Vector128<byte>>(
                source.Slice(0, elementCount));
            Span<Vector128<float>> destinationVectors = MemoryMarshal.Cast<float, Vector128<float>>(
                destination.Slice(0, elementCount));
            Vector128<float> scaleVector = Vector128.Create(scale);
            for (int vectorIndex = 0; vectorIndex < vectorCount; vectorIndex++)
            {
                (Vector128<ushort> lower16, Vector128<ushort> upper16) =
                    Vector128.Widen(sourceVectors[vectorIndex]);
                StoreUInt16AsFloat(lower16, destinationVectors, vectorIndex * 4, scaleVector);
                StoreUInt16AsFloat(upper16, destinationVectors, (vectorIndex * 4) + 2, scaleVector);
            }

            return elementCount;
        }

        private static int ConvertUInt16ToFloatVector128(
            ReadOnlySpan<ushort> source,
            Span<float> destination,
            float scale)
        {
            int vectorCount = source.Length / Vector128<ushort>.Count;
            int elementCount = vectorCount * Vector128<ushort>.Count;
            ReadOnlySpan<Vector128<ushort>> sourceVectors = MemoryMarshal.Cast<ushort, Vector128<ushort>>(
                source.Slice(0, elementCount));
            Span<Vector128<float>> destinationVectors = MemoryMarshal.Cast<float, Vector128<float>>(
                destination.Slice(0, elementCount));
            Vector128<float> scaleVector = Vector128.Create(scale);
            for (int vectorIndex = 0; vectorIndex < vectorCount; vectorIndex++)
            {
                StoreUInt16AsFloat(sourceVectors[vectorIndex], destinationVectors, vectorIndex * 2, scaleVector);
            }

            return elementCount;
        }

        private static int ConvertUIntToFloatVector128(
            ReadOnlySpan<uint> source,
            Span<float> destination)
        {
            int vectorCount = source.Length / Vector128<uint>.Count;
            int elementCount = vectorCount * Vector128<uint>.Count;
            ReadOnlySpan<Vector128<uint>> sourceVectors = MemoryMarshal.Cast<uint, Vector128<uint>>(
                source.Slice(0, elementCount));
            Span<Vector128<float>> destinationVectors = MemoryMarshal.Cast<float, Vector128<float>>(
                destination.Slice(0, elementCount));
            for (int vectorIndex = 0; vectorIndex < vectorCount; vectorIndex++)
            {
                destinationVectors[vectorIndex] = Vector128.ConvertToSingle(sourceVectors[vectorIndex]);
            }

            return elementCount;
        }

        private static void StoreUInt16AsFloat(
            Vector128<ushort> source,
            Span<Vector128<float>> destination,
            int destinationIndex,
            Vector128<float> scale)
        {
            (Vector128<uint> lower, Vector128<uint> upper) = Vector128.Widen(source);
            destination[destinationIndex] = Vector128.Multiply(Vector128.ConvertToSingle(lower), scale);
            destination[destinationIndex + 1] = Vector128.Multiply(Vector128.ConvertToSingle(upper), scale);
        }

        private static void ConvertHalfToFloatVector128(
            ReadOnlySpan<ushort> source,
            Span<float> destination)
        {
            int vectorCount = source.Length / Vector128<ushort>.Count;
            ReadOnlySpan<Vector128<ushort>> sourceVectors = MemoryMarshal.Cast<ushort, Vector128<ushort>>(
                source.Slice(0, vectorCount * Vector128<ushort>.Count));
            Span<Vector128<float>> destinationVectors = MemoryMarshal.Cast<float, Vector128<float>>(
                destination.Slice(0, vectorCount * Vector128<ushort>.Count));

            for (int vectorIndex = 0; vectorIndex < vectorCount; vectorIndex++)
            {
                (Vector128<uint> lower, Vector128<uint> upper) = Vector128.Widen(sourceVectors[vectorIndex]);
                destinationVectors[vectorIndex * 2] = ConvertHalfVector(lower);
                destinationVectors[(vectorIndex * 2) + 1] = ConvertHalfVector(upper);
            }

            int scalarStart = vectorCount * Vector128<ushort>.Count;
            ConvertHalfToFloatScalar(source.Slice(scalarStart), destination.Slice(scalarStart));
        }

        private static Vector128<float> ConvertHalfVector(Vector128<uint> half)
        {
            Vector128<uint> sign = Vector128.ShiftLeft(half & Vector128.Create(0x8000U), 16);
            Vector128<uint> exponent = half & Vector128.Create(0x7c00U);
            Vector128<uint> mantissa = half & Vector128.Create(0x03ffU);

            Vector128<uint> normalBits = sign |
                Vector128.Add(
                    Vector128.ShiftLeft(half & Vector128.Create(0x7fffU), 13),
                    Vector128.Create(0x38000000U));
            Vector128<uint> specialBits = sign |
                Vector128.Create(0x7f800000U) |
                Vector128.ShiftLeft(mantissa, 13);
            Vector128<float> subnormalValues = Vector128.Multiply(
                Vector128.ConvertToSingle(mantissa.AsInt32()),
                Vector128.Create(1.0f / 16_777_216.0f));
            Vector128<uint> subnormalBits = subnormalValues.AsUInt32() | sign;

            Vector128<uint> zeroExponent = Vector128.Equals(exponent, Vector128<uint>.Zero);
            Vector128<uint> specialExponent = Vector128.Equals(exponent, Vector128.Create(0x7c00U));
            Vector128<uint> finiteBits = Vector128.ConditionalSelect(zeroExponent, subnormalBits, normalBits);
            return Vector128.ConditionalSelect(specialExponent, specialBits, finiteBits).AsSingle();
        }

        private static void ConvertFloatToHalfVector128(
            ReadOnlySpan<float> source,
            Span<ushort> destination)
        {
            int vectorCount = source.Length / Vector128<float>.Count;
            ReadOnlySpan<Vector128<float>> sourceVectors = MemoryMarshal.Cast<float, Vector128<float>>(
                source.Slice(0, vectorCount * Vector128<float>.Count));
            int outputIndex = 0;
            for (int vectorIndex = 0; vectorIndex < vectorCount; vectorIndex++)
            {
                Vector128<uint> bits = sourceVectors[vectorIndex].AsUInt32();
                if (AllLanesHaveNormalHalfExponent(bits))
                {
                    Vector128<uint> converted = ConvertNormalFloatVector(bits);
                    destination[outputIndex] = (ushort)converted.GetElement(0);
                    destination[outputIndex + 1] = (ushort)converted.GetElement(1);
                    destination[outputIndex + 2] = (ushort)converted.GetElement(2);
                    destination[outputIndex + 3] = (ushort)converted.GetElement(3);
                }
                else
                {
                    destination[outputIndex] = SingleToHalfBits(source[outputIndex]);
                    destination[outputIndex + 1] = SingleToHalfBits(source[outputIndex + 1]);
                    destination[outputIndex + 2] = SingleToHalfBits(source[outputIndex + 2]);
                    destination[outputIndex + 3] = SingleToHalfBits(source[outputIndex + 3]);
                }

                outputIndex += Vector128<float>.Count;
            }

            ConvertFloatToHalfScalar(source.Slice(outputIndex), destination.Slice(outputIndex));
        }

        private static bool AllLanesHaveNormalHalfExponent(Vector128<uint> bits)
        {
            for (int lane = 0; lane < Vector128<uint>.Count; lane++)
            {
                uint exponent = (bits.GetElement(lane) >> 23) & 0xffU;
                if (exponent < 113U || exponent > 142U)
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector128<uint> ConvertNormalFloatVector(Vector128<uint> bits)
        {
            Vector128<uint> sign = Vector128.ShiftRightLogical(bits, 16) & Vector128.Create(0x8000U);
            Vector128<uint> exponent =
                (Vector128.ShiftRightLogical(bits, 23) & Vector128.Create(0xffU)) - Vector128.Create(112U);
            Vector128<uint> mantissa = bits & Vector128.Create(0x7fffffU);
            Vector128<uint> roundedMantissa = mantissa +
                Vector128.Create(0x0fffU) +
                (Vector128.ShiftRightLogical(mantissa, 13) & Vector128.Create(1U));
            Vector128<uint> carry = Vector128.ShiftRightLogical(roundedMantissa, 23);
            Vector128<uint> halfMantissa =
                Vector128.ShiftRightLogical(roundedMantissa, 13) & Vector128.Create(0x03ffU);
            Vector128<uint> halfExponent = Vector128.ShiftLeft(exponent + carry, 10);
            return sign | halfExponent | halfMantissa;
        }
#endif

        private static void ConvertHalfToFloatScalar(
            ReadOnlySpan<ushort> source,
            Span<float> destination)
        {
            for (int index = 0; index < source.Length; index++)
            {
                destination[index] = HalfBitsToSingle(source[index]);
            }
        }

        private static void ConvertFloatToHalfScalar(
            ReadOnlySpan<float> source,
            Span<ushort> destination)
        {
            for (int index = 0; index < source.Length; index++)
            {
                destination[index] = SingleToHalfBits(source[index]);
            }
        }

        internal static float HalfBitsToSingle(ushort value)
        {
            uint sign = (uint)(value & 0x8000U) << 16;
            uint exponent = (uint)(value >> 10) & 0x1fU;
            uint mantissa = (uint)value & 0x03ffU;
            uint bits;
            if (exponent == 0)
            {
                if (mantissa == 0)
                {
                    bits = sign;
                }
                else
                {
                    int adjustment = -1;
                    do
                    {
                        mantissa <<= 1;
                        adjustment++;
                    }
                    while ((mantissa & 0x0400U) == 0);

                    mantissa &= 0x03ffU;
                    bits = sign |
                        ((uint)(127 - 15 - adjustment) << 23) |
                        (mantissa << 13);
                }
            }
            else if (exponent == 31)
            {
                bits = sign | 0x7f800000U | (mantissa << 13);
            }
            else
            {
                bits = sign |
                    ((exponent - 15U + 127U) << 23) |
                    (mantissa << 13);
            }

            return BitConverter.Int32BitsToSingle((int)bits);
        }

        private static uint NarrowToUInt32(double value)
        {
            return (uint)NarrowUnsigned(value, uint.MaxValue);
        }

        private static ulong NarrowUnsigned(double value, ulong maximum)
        {
            if (!(value > 0.0))
            {
                return 0;
            }

            if (value >= maximum)
            {
                return maximum;
            }

            return (ulong)Math.Round(value, MidpointRounding.ToEven);
        }

        private static ushort SingleToHalfBits(float value)
        {
            uint bits = (uint)BitConverter.SingleToInt32Bits(value);
            uint sign = (bits >> 16) & 0x8000U;
            int exponent = (int)((bits >> 23) & 0xffU);
            uint mantissa = bits & 0x7fffffU;

            if (exponent == 0xff)
            {
                return (ushort)(sign | 0x7c00U |
                    (mantissa != 0 ? 0x0200U | (mantissa >> 13) : 0U));
            }

            exponent = exponent - 127 + 15;
            if (exponent >= 31)
            {
                return (ushort)(sign | 0x7c00U);
            }

            if (exponent <= 0)
            {
                if (exponent < -10)
                {
                    return (ushort)sign;
                }

                mantissa |= 0x800000U;
                int shift = 14 - exponent;
                uint rounded = mantissa >> shift;
                uint remainder = mantissa & ((1U << shift) - 1U);
                uint halfway = 1U << (shift - 1);
                if (remainder > halfway || (remainder == halfway && (rounded & 1U) != 0))
                {
                    rounded++;
                }

                return (ushort)(sign | rounded);
            }

            uint resultMantissa = mantissa >> 13;
            uint normalRemainder = mantissa & 0x1fffU;
            if (normalRemainder > 0x1000U ||
                (normalRemainder == 0x1000U && (resultMantissa & 1U) != 0))
            {
                resultMantissa++;
                if (resultMantissa == 0x0400U)
                {
                    resultMantissa = 0;
                    exponent++;
                    if (exponent >= 31)
                    {
                        return (ushort)(sign | 0x7c00U);
                    }
                }
            }

            return (ushort)(sign | ((uint)exponent << 10) | resultMantissa);
        }
    }

    internal enum SimdConversionPath
    {
        Scalar = 0,
        Sse2 = 1,
        Neon = 2,
    }
}
