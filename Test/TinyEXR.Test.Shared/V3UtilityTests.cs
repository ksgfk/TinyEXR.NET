using System.Buffers.Binary;
using System.Numerics;
using V3 = TinyEXR.V3;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3UtilityTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 pixel conversions preserve normalized endpoints and ties-to-even")]
    public void Case_V3PixelConversion_PreservesNormalizedEndpointsAndRounding()
    {
        ushort[] source16 = Enumerable.Range(0, 256).Select(static index => (ushort)(index * 257)).ToArray();
        float[] normalized16 = new float[source16.Length];
        ushort[] roundTrip16 = new ushort[source16.Length];
        V3.PixelConversion.UInt16ToFloat(
            source16,
            normalized16,
            V3.PixelConversionMode.Normalized);
        V3.PixelConversion.FloatToUInt16(
            normalized16,
            roundTrip16,
            V3.PixelConversionMode.Normalized);
        CollectionAssert.AreEqual(source16, roundTrip16);
        Assert.AreEqual(0.0f, normalized16[0]);
        Assert.AreEqual(1.0f, normalized16[normalized16.Length - 1]);

        V3.PixelConversion.FloatToUInt16(
            new[] { -1.0f, 0.4f / ushort.MaxValue, 2.0f, 0.50001f / ushort.MaxValue },
            roundTrip16.AsSpan(0, 4),
            V3.PixelConversionMode.Normalized);
        CollectionAssert.AreEqual(
            new ushort[] { 0, 0, ushort.MaxValue, 1 },
            roundTrip16.AsSpan(0, 4).ToArray());

        uint[] source32 = { 0U, 1U, 0x80000000U, 0xfffffffeU, uint.MaxValue };
        float[] normalized32 = new float[source32.Length];
        uint[] roundTrip32 = new uint[source32.Length];
        V3.PixelConversion.UIntToFloat(
            source32,
            normalized32,
            V3.PixelConversionMode.Normalized);
        V3.PixelConversion.FloatToUInt(
            normalized32,
            roundTrip32,
            V3.PixelConversionMode.Normalized);
        Assert.AreEqual(0U, roundTrip32[0]);
        Assert.AreEqual(uint.MaxValue, roundTrip32[roundTrip32.Length - 1]);
        Assert.AreEqual(1.0f, normalized32[normalized32.Length - 1]);

        ushort[] normalizedHalf = new ushort[2];
        V3.PixelConversion.UIntToHalf(
            new[] { 0U, uint.MaxValue },
            normalizedHalf,
            V3.PixelConversionMode.Normalized);
        CollectionAssert.AreEqual(new ushort[] { 0x0000, 0x3c00 }, normalizedHalf);
        V3.PixelConversion.HalfToUInt(
            new ushort[] { 0x3e00, 0xbc00 },
            roundTrip32.AsSpan(0, 2));
        CollectionAssert.AreEqual(new uint[] { 2, 0 }, roundTrip32.AsSpan(0, 2).ToArray());
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 part bridge expands mixed sampled channels to interleaved float")]
    public void Case_V3PartConversion_ExpandsMixedSampledChannels()
    {
        V3.Box2i region = new(0, 0, 3, 1);
        V3.Header header = new(
            V3.PartType.Scanline,
            region,
            new[]
            {
                new V3.Channel("F", V3.PixelType.Float),
                new V3.Channel("H", V3.PixelType.Half),
                new V3.Channel("U", V3.PixelType.UInt, 2, 1),
            });
        V3.Part part = new(
            header,
            new[]
            {
                new V3.FlatLevel(
                    0,
                    0,
                    region,
                    new[]
                    {
                        FloatBuffer("F", Enumerable.Range(0, 8).Select(static value => (float)value).ToArray()),
                        HalfBuffer("H", Enumerable.Repeat((ushort)0x3c00, 8).ToArray()),
                        UIntBuffer("U", new uint[] { 10, 20, 30, 40 }),
                    }),
            },
            isComplete: true);

        V3.InterleavedFloatImage image = V3.PartConversion.ToInterleavedFloat(part);
        CollectionAssert.AreEqual(new[] { "F", "H", "U" }, image.ChannelNames.ToArray());
        Assert.AreEqual(4, image.Width);
        Assert.AreEqual(2, image.Height);
        float[] expectedUInt = { 10, 10, 20, 20, 30, 30, 40, 40 };
        for (int pixel = 0; pixel < 8; pixel++)
        {
            Assert.AreEqual(pixel, image.GetSample(pixel % 4, pixel / 4, 0));
            Assert.AreEqual(1.0f, image.GetSample(pixel % 4, pixel / 4, 1));
            Assert.AreEqual(expectedUInt[pixel], image.GetSample(pixel % 4, pixel / 4, 2));
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 part bridge creates planar Half channels from RGBA float")]
    public void Case_V3PartConversion_CreatesPlanarHalfChannelsFromRgba()
    {
        float[] rgba =
        {
            1.0f, 0.5f, 0.0f, 1.0f,
            2.0f, 1.0f, 0.5f, 0.25f,
        };
        V3.Part part = V3.PartConversion.FromInterleavedFloat(
            rgba,
            2,
            1,
            4,
            V3.PixelType.Half,
            V3.Compression.ZSTD);
        Assert.AreEqual(V3.Compression.ZSTD, part.Header.Compression);
        V3.InterleavedFloatImage decoded = V3.PartConversion.ToInterleavedFloat(part);
        CollectionAssert.AreEqual(new[] { "A", "B", "G", "R" }, decoded.ChannelNames.ToArray());
        float[] expected =
        {
            1.0f, 0.0f, 0.5f, 1.0f,
            0.25f, 0.5f, 1.0f, 2.0f,
        };
        CollectionAssert.AreEqual(expected, decoded.Data.ToArray());
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 part bridge reconstructs luminance chroma RGBA")]
    public void Case_V3PartConversion_ReconstructsLuminanceChromaRgba()
    {
        V3.Box2i region = new(-4, 6, -1, 7);
        V3.Header header = new(
            V3.PartType.Scanline,
            region,
            new[]
            {
                new V3.Channel("A", V3.PixelType.Float),
                new V3.Channel("BY", V3.PixelType.Float, 2, 2),
                new V3.Channel("RY", V3.PixelType.Float, 2, 2),
                new V3.Channel("Y", V3.PixelType.Float),
            });
        V3.Part part = new(
            header,
            new[]
            {
                new V3.FlatLevel(
                    0,
                    0,
                    region,
                    new[]
                    {
                        FloatBuffer("A", Enumerable.Repeat(0.75f, 8).ToArray()),
                        FloatBuffer("BY", new[] { -1.0f, -1.0f }),
                        FloatBuffer("RY", new[] { 1.0f, 1.0f }),
                        FloatBuffer("Y", Enumerable.Repeat(0.5f, 8).ToArray()),
                    }),
            },
            isComplete: true);
        Assert.IsTrue(V3.PartConversion.IsLuminanceChroma(part));
        V3.InterleavedFloatImage rgba = V3.PartConversion.LuminanceChromaToRgbaFloat(part);
        float expectedGreen = (0.5f - 0.2126f) / 0.7152f;
        for (int pixel = 0; pixel < 8; pixel++)
        {
            Assert.AreEqual(1.0f, rgba.Data[(pixel * 4) + 0], 0.000001f);
            Assert.AreEqual(expectedGreen, rgba.Data[(pixel * 4) + 1], 0.000001f);
            Assert.AreEqual(0.0f, rgba.Data[(pixel * 4) + 2], 0.000001f);
            Assert.AreEqual(0.75f, rgba.Data[(pixel * 4) + 3], 0.000001f);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 resize matches identity and exact 2x box averages")]
    public void Case_V3Resize_MatchesIdentityAndExactBoxAverages()
    {
        const int width = 8;
        const int height = 6;
        float[] source = Enumerable.Range(0, width * height).Select(static value => (float)value).ToArray();
        float[] identity = new float[source.Length];
        V3.ImageProcessing.Resize(
            source,
            width,
            height,
            identity,
            width,
            height,
            1,
            V3.ResizeFilter.Triangle);
        CollectionAssert.AreEqual(source, identity);

        float[] downsampled = new float[4 * 3];
        V3.ImageProcessing.Resize(
            source,
            width,
            height,
            downsampled,
            4,
            3,
            1,
            V3.ResizeFilter.Box);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                float expected =
                    (source[(2 * y * width) + (2 * x)] +
                     source[(2 * y * width) + (2 * x) + 1] +
                     source[((2 * y + 1) * width) + (2 * x)] +
                     source[((2 * y + 1) * width) + (2 * x) + 1]) /
                    4.0f;
                Assert.AreEqual(expected, downsampled[(y * 4) + x], 0.0001f);
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 streaming resize matches whole-image resize")]
    public void Case_V3StreamingResize_MatchesWholeImageResize()
    {
        const int sourceWidth = 17;
        const int sourceHeight = 13;
        const int destinationWidth = 9;
        const int destinationHeight = 7;
        const int channels = 3;
        float[] source = new float[sourceWidth * sourceHeight * channels];
        for (int index = 0; index < source.Length; index++)
        {
            source[index] = ((index * 7) % 101) * 0.03f;
        }

        float[] whole = new float[destinationWidth * destinationHeight * channels];
        V3.ImageProcessing.Resize(
            source,
            sourceWidth,
            sourceHeight,
            whole,
            destinationWidth,
            destinationHeight,
            channels,
            V3.ResizeFilter.Mitchell);

        float[] streaming = new float[whole.Length];
        float[] row = new float[destinationWidth * channels];
        int sourceY = 0;
        using (V3.StreamingImageResizer resizer = new V3.StreamingImageResizer(
            sourceWidth,
            sourceHeight,
            destinationWidth,
            destinationHeight,
            channels,
            V3.PixelType.Float,
            V3.ResizeFilter.Mitchell))
        {
            while (true)
            {
                V3.ExrResult result = resizer.PullRow(row, out int destinationY);
                if (result == V3.ExrResult.WouldBlock)
                {
                    resizer.PushRow(
                        sourceY,
                        source.AsSpan(sourceY * sourceWidth * channels, sourceWidth * channels));
                    sourceY++;
                    continue;
                }

                Assert.AreEqual(V3.ExrResult.Success, result);
                if (destinationY == destinationHeight)
                {
                    break;
                }

                row.CopyTo(streaming, destinationY * row.Length);
            }

            Assert.IsTrue(resizer.IsComplete);
            Assert.AreEqual(sourceHeight, sourceY);
        }

        CollectionAssert.AreEqual(whole, streaming);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 streaming resize supports Half and UInt rows")]
    public void Case_V3StreamingResize_SupportsHalfAndUIntRows()
    {
        ushort[] halfSource = Enumerable.Repeat((ushort)0x3e00, 8).ToArray();
        using (V3.StreamingImageResizer halfResizer = new V3.StreamingImageResizer(
            4,
            2,
            2,
            1,
            1,
            V3.PixelType.Half,
            V3.ResizeFilter.Box))
        {
            ushort[] destination = new ushort[2];
            Assert.AreEqual(V3.ExrResult.WouldBlock, halfResizer.PullRow(destination, out _));
            halfResizer.PushRow(0, halfSource.AsSpan(0, 4));
            Assert.AreEqual(V3.ExrResult.WouldBlock, halfResizer.PullRow(destination, out _));
            halfResizer.PushRow(1, halfSource.AsSpan(4, 4));
            Assert.AreEqual(V3.ExrResult.Success, halfResizer.PullRow(destination, out int destinationY));
            Assert.AreEqual(0, destinationY);
            CollectionAssert.AreEqual(new ushort[] { 0x3e00, 0x3e00 }, destination);
        }

        using (V3.StreamingImageResizer uintResizer = new V3.StreamingImageResizer(
            2,
            1,
            1,
            1,
            1,
            V3.PixelType.UInt,
            V3.ResizeFilter.Box))
        {
            uintResizer.PushRow(0, new uint[] { 2, 3 });
            uint[] destination = new uint[1];
            Assert.AreEqual(V3.ExrResult.Success, uintResizer.PullRow(destination, out _));
            Assert.AreEqual(2U, destination[0], "2.5 must use ties-to-even narrowing.");
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 tone maps in place and preserves alpha")]
    public void Case_V3ToneMap_IsMonotonicInPlaceAndPreservesAlpha()
    {
        float[] source =
        {
            0.0f, 0.0f, 0.0f, 0.25f,
            1.0f, 2.0f, 0.5f, 0.50f,
            2.0f, 4.0f, 1.0f, 0.75f,
            3.0f, 6.0f, 1.5f, 1.00f,
        };
        float[] aces = new float[source.Length];
        V3.ImageProcessing.ToneMap(source, aces, 4, V3.ToneMapOperator.Aces);
        Assert.AreEqual(0.0f, aces[0], 0.000001f);
        Assert.IsTrue(aces[4] <= aces[8] && aces[8] <= aces[12]);
        Assert.IsTrue(aces[12] <= 1.0f);
        for (int pixel = 0; pixel < 4; pixel++)
        {
            Assert.AreEqual(source[(pixel * 4) + 3], aces[(pixel * 4) + 3]);
        }

        float[] inPlace = (float[])source.Clone();
        float[] outOfPlace = new float[source.Length];
        V3.ImageProcessing.ToneMap(inPlace, inPlace, 4, V3.ToneMapOperator.Reinhard);
        V3.ImageProcessing.ToneMap(source, outOfPlace, 4, V3.ToneMapOperator.Reinhard);
        CollectionAssert.AreEqual(outOfPlace, inPlace);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 color matrices round trip and map D65 white")]
    public void Case_V3ColorMatrices_RoundTripAndMapD65White()
    {
        V3.ColorMatrix3x3 forward = V3.ImageProcessing.GetColorMatrix(
            V3.ColorSpace.Srgb,
            V3.ColorSpace.Rec2020);
        V3.ColorMatrix3x3 reverse = V3.ImageProcessing.GetColorMatrix(
            V3.ColorSpace.Rec2020,
            V3.ColorSpace.Srgb);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                float actual = 0.0f;
                for (int inner = 0; inner < 3; inner++)
                {
                    actual += reverse[row, inner] * forward[inner, column];
                }

                Assert.AreEqual(row == column ? 1.0f : 0.0f, actual, 0.0001f);
            }
        }

        V3.ColorMatrix3x3 srgbToXyz = V3.ImageProcessing.GetColorMatrix(
            V3.ColorSpace.Srgb,
            V3.ColorSpace.Xyz);
        float[] xyz = new float[3];
        V3.ImageProcessing.ApplyColorMatrix(new[] { 1.0f, 1.0f, 1.0f }, xyz, 3, srgbToXyz);
        Assert.AreEqual(0.95047f, xyz[0], 0.001f);
        Assert.AreEqual(1.0f, xyz[1], 0.001f);
        Assert.AreEqual(1.08883f, xyz[2], 0.001f);

        Vector3 fallback = V3.ImageProcessing.GetLuminanceWeights(null);
        Assert.AreEqual(0.2126f, fallback.X, 0.000001f);
        Assert.AreEqual(0.7152f, fallback.Y, 0.000001f);
        Assert.AreEqual(0.0722f, fallback.Z, 0.000001f);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 transfer functions round trip")]
    public void Case_V3TransferFunctions_RoundTrip()
    {
        float[] linear = Enumerable.Range(0, 64).Select(static index => index / 63.0f).ToArray();
        float[] encoded = new float[linear.Length];
        float[] decoded = new float[linear.Length];
        V3.TransferFunction[] functions =
        {
            V3.TransferFunction.Srgb,
            V3.TransferFunction.Gamma22,
            V3.TransferFunction.Gamma24,
            V3.TransferFunction.Rec709,
            V3.TransferFunction.Pq,
            V3.TransferFunction.Hlg,
        };
        foreach (V3.TransferFunction function in functions)
        {
            V3.ImageProcessing.EncodeTransfer(linear, encoded, function);
            V3.ImageProcessing.DecodeTransfer(encoded, decoded, function);
            for (int index = 0; index < linear.Length; index++)
            {
                Assert.AreEqual(linear[index], decoded[index], 0.002f, function.ToString());
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 parses and applies identity 3D cube LUTs")]
    public void Case_V3Lut3D_ParsesAndAppliesIdentityCube()
    {
        const string cube =
            "# comment\n" +
            "TITLE \"identity\"\n" +
            "LUT_3D_SIZE 2\n" +
            "DOMAIN_MIN 0 0 0 # inline comment\n" +
            "DOMAIN_MAX 1 1 1\n" +
            "0 0 0 # black\n1 0 0\n0 1 0\n1 1 0\n" +
            "0 0 1\n1 0 1\n0 1 1\n1 1 1\n";
        V3.Lut3D lut = V3.Lut3D.ParseCube(cube);
        Assert.AreEqual(2, lut.Size);
        float[] source =
        {
            0.1f, 0.3f, 0.8f, 0.25f,
            0.3f, 0.3f, 0.7f, 0.50f,
            0.5f, 0.3f, 0.6f, 0.75f,
            0.7f, 0.3f, 0.5f, 1.00f,
        };
        foreach (V3.LutInterpolation interpolation in Enum.GetValues<V3.LutInterpolation>())
        {
            float[] destination = new float[source.Length];
            lut.Apply(source, destination, 4, interpolation);
            for (int index = 0; index < source.Length; index++)
            {
                Assert.AreEqual(source[index], destination[index], 0.00001f, interpolation.ToString());
            }
        }

        Assert.AreEqual(
            V3.ExrResult.Unsupported,
            V3.Lut3D.TryParseCube("LUT_1D_SIZE 2\n0\n1\n", out _));
        Assert.AreEqual(
            V3.ExrResult.Corrupt,
            V3.Lut3D.TryParseCube("LUT_3D_SIZE 2\n0 0 0\n", out _));
    }

    private static V3.ChannelBuffer FloatBuffer(string name, IReadOnlyList<float> values)
    {
        byte[] data = new byte[values.Count * sizeof(float)];
        for (int index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(values[index]));
        }

        return new V3.ChannelBuffer(name, V3.PixelType.Float, data);
    }

    private static V3.ChannelBuffer HalfBuffer(string name, IReadOnlyList<ushort> values)
    {
        byte[] data = new byte[values.Count * sizeof(ushort)];
        for (int index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(index * sizeof(ushort), sizeof(ushort)),
                values[index]);
        }

        return new V3.ChannelBuffer(name, V3.PixelType.Half, data);
    }

    private static V3.ChannelBuffer UIntBuffer(string name, IReadOnlyList<uint> values)
    {
        byte[] data = new byte[values.Count * sizeof(uint)];
        for (int index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(index * sizeof(uint), sizeof(uint)),
                values[index]);
        }

        return new V3.ChannelBuffer(name, V3.PixelType.UInt, data);
    }
}
