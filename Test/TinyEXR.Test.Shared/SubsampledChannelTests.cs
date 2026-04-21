namespace TinyEXR.Test;

[TestClass]
public sealed class SubsampledChannelTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] Subsampled single-channel scanline round-trips and expands in LoadEXR")]
    public void Case_SubsampledSingleChannel_ScanlineRoundTripAndConvenienceLoad()
    {
        const int width = 4;
        const int height = 20;
        const int samplingX = 2;
        const int samplingY = 2;

        float[] samples =
        {
            1.0f, 2.0f,
            3.0f, 4.0f,
            5.0f, 6.0f,
            7.0f, 8.0f,
            9.0f, 10.0f,
            11.0f, 12.0f,
            13.0f, 14.0f,
            15.0f, 16.0f,
            17.0f, 18.0f,
            19.0f, 20.0f,
        };

        ExrImage image = new(
            width,
            height,
            new[]
            {
                new ExrImageChannel(
                    new ExrChannel("Y", ExrPixelType.Float, samplingX, samplingY, 1),
                    ExrPixelType.Float,
                    ExrTestHelper.ToBytes(samples)),
            });
        ExrHeader header = new()
        {
            Compression = CompressionType.ZIP,
        };

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader decodedHeader));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, decodedHeader, out ExrImage decodedImage));

        Assert.AreEqual(1, decodedHeader.Channels.Count);
        Assert.AreEqual(samplingX, decodedHeader.Channels[0].SamplingX);
        Assert.AreEqual(samplingY, decodedHeader.Channels[0].SamplingY);
        CollectionAssert.AreEqual(samples, ExrTestHelper.ReadFloatChannel(decodedImage, "Y"));

        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRFromMemory(encoded, out float[] rgba, out int rgbaWidth, out int rgbaHeight));
        Assert.AreEqual(width, rgbaWidth);
        Assert.AreEqual(height, rgbaHeight);
        CollectionAssert.AreEqual(BuildExpandedRgba(width, height, samplingX, samplingY, samples), rgba);
    }

    [TestMethod(DisplayName = "Subsampled layered RGBA round-trips with PIZ and expands in layer RGBA load")]
    public void Case_SubsampledLayeredRgba_PizRoundTripAndLayerLoad()
    {
        const int width = 4;
        const int height = 40;
        const int samplingX = 2;
        const int samplingY = 2;

        float[] r = BuildSequentialSamples(100.0f, 40);
        float[] g = BuildSequentialSamples(200.0f, 40);
        float[] b = BuildSequentialSamples(300.0f, 40);
        float[] a = BuildSequentialSamples(400.0f, 40);

        ExrImage image = new(
            width,
            height,
            new[]
            {
                CreateFloatChannel("beauty.A", samplingX, samplingY, a),
                CreateFloatChannel("beauty.B", samplingX, samplingY, b),
                CreateFloatChannel("beauty.G", samplingX, samplingY, g),
                CreateFloatChannel("beauty.R", samplingX, samplingY, r),
            });
        ExrHeader header = new()
        {
            Compression = CompressionType.PIZ,
        };

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader decodedHeader));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, decodedHeader, out ExrImage decodedImage));

        Assert.AreEqual(4, decodedHeader.Channels.Count);
        Assert.AreEqual(CompressionType.PIZ, decodedHeader.Compression);
        CollectionAssert.AreEqual(a, ExrTestHelper.ReadFloatChannel(decodedImage, "beauty.A"));
        CollectionAssert.AreEqual(b, ExrTestHelper.ReadFloatChannel(decodedImage, "beauty.B"));
        CollectionAssert.AreEqual(g, ExrTestHelper.ReadFloatChannel(decodedImage, "beauty.G"));
        CollectionAssert.AreEqual(r, ExrTestHelper.ReadFloatChannel(decodedImage, "beauty.R"));

        Assert.AreEqual(ResultCode.Success, Exr.TryReadRgba(encoded, "beauty", out float[] rgba, out int rgbaWidth, out int rgbaHeight));
        Assert.AreEqual(width, rgbaWidth);
        Assert.AreEqual(height, rgbaHeight);
        CollectionAssert.AreEqual(BuildExpandedRgba(width, height, samplingX, samplingY, r, g, b, a), rgba);
    }

    private static ExrImageChannel CreateFloatChannel(string name, int samplingX, int samplingY, float[] samples)
    {
        return new ExrImageChannel(
            new ExrChannel(name, ExrPixelType.Float, samplingX, samplingY, 1),
            ExrPixelType.Float,
            ExrTestHelper.ToBytes(samples));
    }

    private static float[] BuildSequentialSamples(float start, int count)
    {
        float[] values = new float[count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = start + i;
        }

        return values;
    }

    private static float[] BuildExpandedRgba(int width, int height, int samplingX, int samplingY, float[] singleChannel)
    {
        int sampleWidth = (width + samplingX - 1) / samplingX;
        float[] rgba = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sampleY = y / samplingY;
            for (int x = 0; x < width; x++)
            {
                float value = singleChannel[sampleY * sampleWidth + x / samplingX];
                int rgbaOffset = (y * width + x) * 4;
                rgba[rgbaOffset + 0] = value;
                rgba[rgbaOffset + 1] = value;
                rgba[rgbaOffset + 2] = value;
                rgba[rgbaOffset + 3] = value;
            }
        }

        return rgba;
    }

    private static float[] BuildExpandedRgba(int width, int height, int samplingX, int samplingY, float[] r, float[] g, float[] b, float[] a)
    {
        int sampleWidth = (width + samplingX - 1) / samplingX;
        float[] rgba = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sampleY = y / samplingY;
            for (int x = 0; x < width; x++)
            {
                int sampleIndex = sampleY * sampleWidth + x / samplingX;
                int rgbaOffset = (y * width + x) * 4;
                rgba[rgbaOffset + 0] = r[sampleIndex];
                rgba[rgbaOffset + 1] = g[sampleIndex];
                rgba[rgbaOffset + 2] = b[sampleIndex];
                rgba[rgbaOffset + 3] = a[sampleIndex];
            }
        }

        return rgba;
    }
}
