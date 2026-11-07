namespace TinyEXR.Test;

[TestClass]
public sealed class SubsampledChannelTests
{
    [TestMethod(DisplayName = "Exr sampled channels expand negative coordinates and layer selection")]
    [DynamicData(nameof(ExrConvenienceTests.Compressions), typeof(ExrConvenienceTests))]
    public void SampledRgba(Compression compression)
    {
        foreach (bool layered in new[] { false, true })
        {
            string[] names = layered ? new[] { "beauty.R", "beauty.G", "beauty.B" } : new[] { "Y" };
            Channel[] channels = names.Select(n => new Channel(n, PixelType.Float, 2, 2)).ToArray();
            Header header = new(PartType.Scanline, new Box2i(-4, -2, 11, 5), channels, compression);
            float[] samples = Enumerable.Range(0, 32).Select(i => i * 0.25f).ToArray();
            Part source = ExrTestHelper.Flat(header, names.Select(n => ExrTestHelper.FloatChannel(n, PixelType.Float, samples)).ToArray());
            Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(source, out byte[] bytes));
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromMemory(bytes, out Part? decoded));
            Assert.IsNotNull(decoded);
            ExrTestHelper.EqualImages(source, decoded);
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRWithLayerFromMemory(bytes, layered ? "beauty" : null, out float[] rgba, out int width, out int height));
            Assert.AreEqual(16, width);
            Assert.AreEqual(8, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    for (int c = 0; c < 4; c++)
                        Assert.AreEqual(layered && c == 3 ? 1 : samples[(y / 2) * 8 + x / 2], rgba[(y * width + x) * 4 + c]);
            Assert.AreEqual(ExrResult.InvalidArgument, Exr.LoadEXRWithLayerFromMemory(bytes, "absent", out rgba, out width, out height));
            Assert.AreEqual(0, rgba.Length);
            Assert.AreEqual(0, width);
            Assert.AreEqual(0, height);
        }
    }
}
