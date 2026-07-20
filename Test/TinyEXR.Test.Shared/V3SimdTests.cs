using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using V3 = TinyEXR.V3;
using V3Codecs = TinyEXR.V3.Codecs;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3SimdTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 SIMD capabilities match runtime tiers")]
    public void Case_V3Simd_CapabilitiesMatchRuntimeTiers()
    {
        CollectionAssert.AreEqual(
            new uint[] { 0, 1, 2, 4, 8 },
            Enum.GetValues(typeof(V3.SimdCapabilities))
                .Cast<V3.SimdCapabilities>()
                .Select(static value => (uint)value)
                .ToArray());

        V3.SimdCapabilities expected = V3.SimdCapabilities.None;
        if (V3.SimdRuntime.IntrinsicsCompiled)
        {
            if (Sse2.IsSupported) expected |= V3.SimdCapabilities.Sse2;
            if (Sse41.IsSupported) expected |= V3.SimdCapabilities.Sse41;
            if (Avx2.IsSupported) expected |= V3.SimdCapabilities.Avx2;
            if (AdvSimd.IsSupported) expected |= V3.SimdCapabilities.Neon;
        }

        Assert.AreEqual(expected, V3.SimdRuntime.Capabilities);
        Assert.AreEqual(ExpectedInfo(expected), V3.SimdRuntime.Info);

        V3.SimdConversionPath expectedPath =
            (expected & V3.SimdCapabilities.Neon) != 0
                ? V3.SimdConversionPath.Neon
                : (expected & V3.SimdCapabilities.Sse2) != 0
                    ? V3.SimdConversionPath.Sse2
                    : V3.SimdConversionPath.Scalar;
        Assert.AreEqual(expectedPath, V3.PixelConversion.SelectedPath);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 SIMD exhaustively widens every binary16 pattern")]
    public void Case_V3Simd_ExhaustiveHalfToFloatIsBitAccurate()
    {
        ushort[] source = new ushort[ushort.MaxValue + 1];
        for (int index = 0; index < source.Length; index++)
        {
            source[index] = (ushort)index;
        }

        float[] actual = new float[source.Length];
        float[] scalar = new float[source.Length];
        V3.PixelConversion.HalfToFloat(source, actual);
        V3.PixelConversion.HalfToFloat(source, scalar, V3.SimdConversionPath.Scalar);

        for (int index = 0; index < source.Length; index++)
        {
            ushort half = source[index];
            int actualBits = BitConverter.SingleToInt32Bits(actual[index]);
            int scalarBits = BitConverter.SingleToInt32Bits(scalar[index]);
            if (actualBits != scalarBits)
            {
                Assert.Fail($"Selected and scalar paths differ for half 0x{half:x4}: 0x{actualBits:x8} != 0x{scalarBits:x8}.");
            }

            if (IsHalfNaN(half))
            {
                if (!float.IsNaN(actual[index]))
                {
                    Assert.Fail($"Half 0x{half:x4} did not widen to NaN.");
                }
            }
            else
            {
                int expectedBits = (int)ExpectedHalfToFloatBits(half);
                if (actualBits != expectedBits)
                {
                    Assert.Fail($"Half 0x{half:x4} widened to 0x{actualBits:x8}; expected 0x{expectedBits:x8}.");
                }
            }
        }

        ushort[] roundTrip = new ushort[source.Length];
        V3.PixelConversion.FloatToHalf(actual, roundTrip);
        for (int index = 0; index < source.Length; index++)
        {
            ushort original = source[index];
            ushort converted = roundTrip[index];
            if (IsHalfNaN(original))
            {
                if (!IsHalfNaN(converted) || (converted & 0x8000) != (original & 0x8000))
                {
                    Assert.Fail($"NaN half 0x{original:x4} round-tripped as 0x{converted:x4}.");
                }
            }
            else if (converted != original)
            {
                Assert.Fail($"Half 0x{original:x4} round-tripped as 0x{converted:x4}.");
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 SIMD narrows fixed and random floats with ties-to-even")]
    public void Case_V3Simd_FloatToHalfMatchesOracleAndScalar()
    {
        List<float> values = new()
        {
            0.0f,
            BitConverter.Int32BitsToSingle(unchecked((int)0x80000000U)),
            float.Epsilon,
            -float.Epsilon,
            float.PositiveInfinity,
            float.NegativeInfinity,
            BitConverter.Int32BitsToSingle(unchecked((int)0x7f800001U)),
            BitConverter.Int32BitsToSingle(unchecked((int)0xffc12345U)),
            65_504.0f,
            65_519.0f,
            65_520.0f,
            -65_520.0f,
            1.0f,
            -1.0f,
            1.00048828125f,
            1.00146484375f,
            0.00006103515625f,
            0.000000059604644775390625f,
            0.0000000298023223876953125f,
        };

        uint state = 0x12345678U;
        for (int index = 0; index < 50_000; index++)
        {
            state = unchecked((state * 1_664_525U) + 1_013_904_223U);
            values.Add(BitConverter.Int32BitsToSingle((int)state));
        }

        for (int index = 0; index < 16_384; index++)
        {
            state = unchecked((state * 1_664_525U) + 1_013_904_223U);
            uint normalBits = (state & 0x807fffffU) | ((113U + (state % 30U)) << 23);
            values.Add(BitConverter.Int32BitsToSingle((int)normalBits));
        }

        float[] source = values.ToArray();
        ushort[] actual = new ushort[source.Length];
        ushort[] scalar = new ushort[source.Length];
        V3.PixelConversion.FloatToHalf(source, actual);
        V3.PixelConversion.FloatToHalf(source, scalar, V3.SimdConversionPath.Scalar);

        for (int index = 0; index < source.Length; index++)
        {
            if (actual[index] != scalar[index])
            {
                Assert.Fail($"Selected and scalar paths differ at {index}: 0x{actual[index]:x4} != 0x{scalar[index]:x4}.");
            }

            if (V3.SimdRuntime.IntrinsicsCompiled)
            {
                ushort oracle = BitConverter.HalfToUInt16Bits((Half)source[index]);
                if (float.IsNaN(source[index]))
                {
                    if (!IsHalfNaN(actual[index]) ||
                        (actual[index] & 0x8000) != (oracle & 0x8000))
                    {
                        Assert.Fail($"Float NaN 0x{BitConverter.SingleToInt32Bits(source[index]):x8} narrowed to 0x{actual[index]:x4}.");
                    }
                }
                else if (actual[index] != oracle)
                {
                    Assert.Fail($"Float 0x{BitConverter.SingleToInt32Bits(source[index]):x8} narrowed to 0x{actual[index]:x4}; oracle is 0x{oracle:x4}.");
                }
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 SIMD unsigned widening matches scalar bit for bit")]
    public void Case_V3Simd_UnsignedWideningMatchesScalar()
    {
        byte[] bytes = Enumerable.Range(0, 259).Select(static index => (byte)index).ToArray();
        ushort[] uint16 = Enumerable.Range(0, ushort.MaxValue + 1)
            .Select(static index => (ushort)index)
            .ToArray();
        uint[] uint32 = new uint[4099];
        uint state = 0x12345678U;
        for (int index = 0; index < uint32.Length; index++)
        {
            state = unchecked((state * 1_664_525U) + 1_013_904_223U);
            uint32[index] = state;
        }

        uint32[0] = 0;
        uint32[1] = 1;
        uint32[2] = 0x7fffffffU;
        uint32[3] = 0x80000000U;
        uint32[4] = uint.MaxValue;
        foreach (V3.PixelConversionMode mode in Enum.GetValues(typeof(V3.PixelConversionMode)))
        {
            AssertUnsignedWideningMatches(
                bytes,
                mode,
                static (source, destination, conversionMode, path) =>
                    V3.PixelConversion.ByteToFloat(source, destination, conversionMode, path));
            AssertUnsignedWideningMatches(
                uint16,
                mode,
                static (source, destination, conversionMode, path) =>
                    V3.PixelConversion.UInt16ToFloat(source, destination, conversionMode, path));
            AssertUnsignedWideningMatches(
                uint32,
                mode,
                static (source, destination, conversionMode, path) =>
                    V3.PixelConversion.UIntToFloat(source, destination, conversionMode, path));
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 SIMD color matrix matches scalar bit for bit")]
    public void Case_V3Simd_ColorMatrixMatchesScalar()
    {
        V3.ColorMatrix3x3 matrix = new(
            1.25f, -0.5f, 0.125f,
            -0.75f, 0.625f, 1.5f,
            0.25f, 2.0f, -1.125f);
        uint state = 0x9e3779b9U;
        foreach (int channels in new[] { 3, 4 })
        {
            float[] source = new float[checked(1003 * channels)];
            for (int index = 0; index < source.Length; index++)
            {
                state = unchecked((state * 1_664_525U) + 1_013_904_223U);
                source[index] = ((int)(state >> 8) - 0x7fffff) / 65536.0f;
            }

            float[] actual = new float[source.Length];
            float[] scalar = new float[source.Length];
            V3.ImageProcessing.ApplyColorMatrix(source, actual, channels, matrix, vectorized: true);
            V3.ImageProcessing.ApplyColorMatrix(source, scalar, channels, matrix, vectorized: false);
            for (int index = 0; index < source.Length; index++)
            {
                Assert.AreEqual(
                    BitConverter.SingleToInt32Bits(scalar[index]),
                    BitConverter.SingleToInt32Bits(actual[index]),
                    $"{channels} channels, element {index}");
            }

            float[] inPlace = (float[])source.Clone();
            V3.ImageProcessing.ApplyColorMatrix(inPlace, inPlace, channels, matrix, vectorized: true);
            CollectionAssert.AreEqual(actual, inPlace);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 SIMD EXR predictor matches scalar for every tail")]
    public void Case_V3Simd_ExrPredictorMatchesScalarForEveryTail()
    {
        uint state = 0x243f6a88U;
        for (int length = 0; length <= 513; length++)
        {
            byte[] source = new byte[length];
            for (int index = 0; index < source.Length; index++)
            {
                state = unchecked((state * 1_664_525U) + 1_013_904_223U);
                source[index] = (byte)(state >> 24);
            }

            byte[] scalar = new byte[length];
            byte[] selected = new byte[length];
            V3Codecs.ExrPredictor.Apply(source, scalar, vectorized: false);
            V3Codecs.ExrPredictor.Apply(source, selected, V3Codecs.ExrPredictor.IsVectorized);
            CollectionAssert.AreEqual(scalar, selected, $"encoded length {length}");

            byte[] scalarWork = (byte[])scalar.Clone();
            byte[] selectedWork = (byte[])selected.Clone();
            byte[] scalarRoundTrip = new byte[length];
            byte[] selectedRoundTrip = new byte[length];
            V3Codecs.ExrPredictor.Undo(scalarWork, length, scalarRoundTrip, vectorized: false);
            V3Codecs.ExrPredictor.Undo(
                selectedWork,
                length,
                selectedRoundTrip,
                V3Codecs.ExrPredictor.IsVectorized);
            CollectionAssert.AreEqual(source, scalarRoundTrip, $"scalar round trip length {length}");
            CollectionAssert.AreEqual(source, selectedRoundTrip, $"selected round trip length {length}");
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 SIMD conversion validates lengths, tails, and overlap")]
    public void Case_V3Simd_ConversionBoundsAndOverlapAreExplicit()
    {
        V3.PixelConversion.HalfToFloat(ReadOnlySpan<ushort>.Empty, Span<float>.Empty);
        V3.PixelConversion.FloatToHalf(ReadOnlySpan<float>.Empty, Span<ushort>.Empty);

        float sentinelFloat = BitConverter.Int32BitsToSingle(unchecked((int)0x7f123456U));
        float[] floats = Enumerable.Repeat(sentinelFloat, 4).ToArray();
        V3.PixelConversion.HalfToFloat(new ushort[] { 0x3c00, 0xc000 }, floats);
        Assert.AreEqual(1.0f, floats[0]);
        Assert.AreEqual(-2.0f, floats[1]);
        Assert.AreEqual(BitConverter.SingleToInt32Bits(sentinelFloat), BitConverter.SingleToInt32Bits(floats[2]));
        Assert.AreEqual(BitConverter.SingleToInt32Bits(sentinelFloat), BitConverter.SingleToInt32Bits(floats[3]));

        ushort[] halves = Enumerable.Repeat((ushort)0x55aa, 4).ToArray();
        V3.PixelConversion.FloatToHalf(new float[] { 1.0f, -2.0f }, halves);
        Assert.AreEqual((ushort)0x3c00, halves[0]);
        Assert.AreEqual((ushort)0xc000, halves[1]);
        Assert.AreEqual((ushort)0x55aa, halves[2]);
        Assert.AreEqual((ushort)0x55aa, halves[3]);

        float[] tooShortFloats = new[] { sentinelFloat };
        AssertThrows<ArgumentException>(() =>
            V3.PixelConversion.HalfToFloat(new ushort[] { 0, 1 }, tooShortFloats));
        Assert.AreEqual(BitConverter.SingleToInt32Bits(sentinelFloat), BitConverter.SingleToInt32Bits(tooShortFloats[0]));

        ushort[] tooShortHalves = new[] { (ushort)0x55aa };
        AssertThrows<ArgumentException>(() =>
            V3.PixelConversion.FloatToHalf(new float[] { 0.0f, 1.0f }, tooShortHalves));
        Assert.AreEqual((ushort)0x55aa, tooShortHalves[0]);

        AssertHalfToFloatOverlapRejected();
        AssertFloatToHalfOverlapRejected();
        AssertDisjointRegionsInSharedStorageAreAccepted();
    }

    private static void AssertHalfToFloatOverlapRejected()
    {
        byte[] storage = new byte[32];
        Span<ushort> source = MemoryMarshal.Cast<byte, ushort>(storage.AsSpan(0, 8));
        Span<float> destination = MemoryMarshal.Cast<byte, float>(storage.AsSpan(0, 16));
        try
        {
            V3.PixelConversion.HalfToFloat(source, destination);
        }
        catch (ArgumentException)
        {
            return;
        }

        Assert.Fail("Half-to-float overlap was accepted.");
    }

    private static void AssertFloatToHalfOverlapRejected()
    {
        byte[] storage = new byte[32];
        Span<float> source = MemoryMarshal.Cast<byte, float>(storage.AsSpan(0, 16));
        Span<ushort> destination = MemoryMarshal.Cast<byte, ushort>(storage.AsSpan(0, 8));
        try
        {
            V3.PixelConversion.FloatToHalf(source, destination);
        }
        catch (ArgumentException)
        {
            return;
        }

        Assert.Fail("Float-to-half overlap was accepted.");
    }

    private static void AssertDisjointRegionsInSharedStorageAreAccepted()
    {
        byte[] halfStorage = new byte[24];
        Span<ushort> halfSource = MemoryMarshal.Cast<byte, ushort>(halfStorage.AsSpan(0, 8));
        halfSource[0] = 0x3c00;
        halfSource[1] = 0xc000;
        halfSource[2] = 0x0000;
        halfSource[3] = 0x7c00;
        Span<float> floatDestination = MemoryMarshal.Cast<byte, float>(halfStorage.AsSpan(8, 16));
        V3.PixelConversion.HalfToFloat(halfSource, floatDestination);
        Assert.AreEqual(1.0f, floatDestination[0]);
        Assert.AreEqual(-2.0f, floatDestination[1]);

        byte[] floatStorage = new byte[24];
        Span<float> floatSource = MemoryMarshal.Cast<byte, float>(floatStorage.AsSpan(0, 16));
        floatSource[0] = 1.0f;
        floatSource[1] = -2.0f;
        floatSource[2] = 0.0f;
        floatSource[3] = float.PositiveInfinity;
        Span<ushort> halfDestination = MemoryMarshal.Cast<byte, ushort>(floatStorage.AsSpan(16, 8));
        V3.PixelConversion.FloatToHalf(floatSource, halfDestination);
        Assert.AreEqual((ushort)0x3c00, halfDestination[0]);
        Assert.AreEqual((ushort)0xc000, halfDestination[1]);
    }

    private static uint ExpectedHalfToFloatBits(ushort value)
    {
        uint sign = (uint)(value & 0x8000) << 16;
        uint exponent = (uint)(value >> 10) & 0x1f;
        uint mantissa = (uint)value & 0x03ff;
        if (exponent == 0)
        {
            if (mantissa == 0)
            {
                return sign;
            }

            int adjustment = -1;
            do
            {
                mantissa <<= 1;
                adjustment++;
            }
            while ((mantissa & 0x0400) == 0);

            return sign |
                ((uint)(127 - 15 - adjustment) << 23) |
                ((mantissa & 0x03ff) << 13);
        }

        if (exponent == 31)
        {
            return sign | 0x7f800000U | (mantissa << 13);
        }

        return sign | ((exponent + 112U) << 23) | (mantissa << 13);
    }

    private static void AssertUnsignedWideningMatches<T>(
        T[] source,
        V3.PixelConversionMode mode,
        Action<ReadOnlySpan<T>, Span<float>, V3.PixelConversionMode, V3.SimdConversionPath> convert)
        where T : struct
    {
        float[] actual = Enumerable.Repeat(float.NaN, source.Length + 3).ToArray();
        float[] scalar = Enumerable.Repeat(float.NaN, source.Length + 3).ToArray();
        convert(source, actual, mode, V3.PixelConversion.SelectedPath);
        convert(source, scalar, mode, V3.SimdConversionPath.Scalar);
        for (int index = 0; index < actual.Length; index++)
        {
            Assert.AreEqual(
                BitConverter.SingleToInt32Bits(scalar[index]),
                BitConverter.SingleToInt32Bits(actual[index]),
                $"{typeof(T).Name} {mode} sample {index}");
        }
    }

    private static bool IsHalfNaN(ushort value)
    {
        return (value & 0x7c00) == 0x7c00 && (value & 0x03ff) != 0;
    }

    private static string ExpectedInfo(V3.SimdCapabilities capabilities)
    {
        if ((capabilities & V3.SimdCapabilities.Neon) != 0) return "neon";
        if ((capabilities & V3.SimdCapabilities.Avx2) != 0)
        {
            bool hasF16c = V3.SimdRuntime.IntrinsicsCompiled &&
                X86Base.IsSupported &&
                Avx.IsSupported &&
                (X86Base.CpuId(1, 0).Item3 & (1 << 29)) != 0;
            return hasF16c ? "avx2+f16c" : "avx2";
        }

        if ((capabilities & V3.SimdCapabilities.Sse41) != 0) return "sse4.1";
        if ((capabilities & V3.SimdCapabilities.Sse2) != 0) return "sse2";
        return "scalar";
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but caught {exception.GetType().Name}: {exception.Message}");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
