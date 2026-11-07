using System.Runtime.InteropServices;
namespace TinyEXR.Test;

[TestClass]
public sealed class SpectralAndCompressionTests
{
    [TestMethod(DisplayName = "Exr spectral helpers delegate to immutable Spectral models")]
    public void SpectralHelpers()
    {
        Header rgb = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel("R", PixelType.Float) });
        Assert.AreEqual(SpectralType.None, Exr.EXRGetSpectrumType(rgb));
        foreach (SpectralType type in new[] { SpectralType.Reflective, SpectralType.Emissive, SpectralType.Polarised })
        {
            int stokes = type == SpectralType.Polarised ? 2 : 0;
            string name = type == SpectralType.Reflective ? Exr.EXRReflectiveChannelName(550.25f) : Exr.EXRSpectralChannelName(550.25f, stokes);
            Assert.AreEqual(Spectral.GetChannelName(type, 550.25f, stokes), name);
            Assert.AreEqual("550,250000", Exr.EXRFormatWavelength(550.25f));
            Assert.AreEqual(550.25f, Exr.EXRParseSpectralChannelWavelength(name));
            Assert.IsTrue(Exr.EXRIsSpectralChannel(name));
            Header h = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel(name, PixelType.Float) });
            Assert.AreEqual(ExrResult.Success, Exr.EXRSetSpectralAttributes(h, type, "test-units", out Header? updated));
            Assert.IsNotNull(updated);
            Assert.AreEqual(type, Exr.EXRGetSpectrumType(updated));
            Assert.IsFalse(Spectral.IsSpectral(h));
            Assert.IsTrue(Spectral.IsSpectral(updated));
            CollectionAssert.AreEqual(new[] { 550.25f }, Exr.EXRGetWavelengths(updated));
            Part p = ExrTestHelper.Flat(updated, ExrTestHelper.FloatChannel(name, PixelType.Float, new[] { 2f }));
            Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(p, out byte[] bytes));
            Assert.IsTrue(Exr.IsSpectralEXRFromMemory(bytes));
            using MemoryStream stream = new(bytes);
            Assert.IsTrue(Exr.IsSpectralEXRFromStream(stream));
            Assert.AreEqual(0, stream.Position);
        }
        Assert.AreEqual(-1f, Exr.EXRParseSpectralChannelWavelength("R"));
        Assert.AreEqual(-1, Exr.EXRGetStokesComponent("R"));
    }

    [TestMethod(DisplayName = "V3 codecs mixed HALF/FLOAT/UINT compressed and raw blocks")]
    [DynamicData(nameof(ExrConvenienceTests.Compressions), typeof(ExrConvenienceTests))]
    public void MixedTypesAndSizes(Compression compression)
    {
        foreach ((int width, int height) in new[] { (1, 1), (7, 5), (128, 32), (256, 128) })
        {
            Header header = new(PartType.Scanline, new Box2i(0, 0, width - 1, height - 1),
                new[] { new Channel("H", PixelType.Half), new Channel("F", PixelType.Float), new Channel("U", PixelType.UInt) }, compression);
            int count = width * height;
            float[] half = Enumerable.Repeat(0.5f, count).ToArray();
            float[] floats = Enumerable.Range(0, count).Select(i => (i % 17) * 0.25f).ToArray();
            uint[] uints = Enumerable.Range(0, count).Select(i => (uint)i * 100003U).ToArray();
            Part part = ExrTestHelper.Flat(header, ExrTestHelper.FloatChannel("H", PixelType.Half, half),
                ExrTestHelper.FloatChannel("F", PixelType.Float, floats), new ChannelBuffer("U", PixelType.UInt, ExrTestHelper.ToBytes(uints)));
            Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(part, out byte[] bytes));
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromMemory(bytes, out Part? decoded));
            Assert.IsNotNull(decoded);
            ExrTestHelper.EqualImages(part, decoded);
        }
    }

    [TestMethod(DisplayName = "Zlib backends reject truncation, checksum damage and over-expansion")]
    public void BoundedZlibValidation()
    {
        byte[] raw = Enumerable.Range(0, 16384).Select(i => (byte)(i % 7)).ToArray();
        byte[] encoded = TinyEXR.Codecs.ZlibBackend.Deflate(raw);
        byte[] decoded = new byte[raw.Length];
        Assert.AreEqual(ExrResult.Success, TinyEXR.Codecs.ZlibBackend.Inflate(encoded, decoded));
        CollectionAssert.AreEqual(raw, decoded);
        for (int n = 0; n < encoded.Length; n++)
            Assert.AreEqual(ExrResult.Corrupt, TinyEXR.Codecs.ZlibBackend.Inflate(encoded.AsSpan(0, n), decoded), $"prefix {n}");
        encoded[^1] ^= 1;
        Assert.AreEqual(ExrResult.Corrupt, TinyEXR.Codecs.ZlibBackend.Inflate(encoded, decoded));
        encoded[^1] ^= 1;
        Assert.AreEqual(ExrResult.Corrupt, TinyEXR.Codecs.ZlibBackend.Inflate(encoded, new byte[raw.Length - 1]));
        Assert.AreEqual(ExrResult.Corrupt, TinyEXR.Codecs.ZlibBackend.Inflate(encoded, new byte[raw.Length + 1]));
    }

    [TestMethod(DisplayName = "DWA accepts raw blocks, rejects compressed read and all encoding")]
    [DataRow(Compression.DWAA)]
    [DataRow(Compression.DWAB)]
    public void DwaRawOnly(Compression compression)
    {
        Part part = PartConversion.FromInterleavedFloat(new[] { 1f }, 1, 1, 1, PixelType.Float, Compression.None);
        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(part, out byte[] bytes));
        ExrBinaryMutationHelper.SetHeaderByteAttributeValue(bytes, 0, "compression", "compression", (byte)compression);
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromMemory(bytes, out Part? loaded));
        Assert.IsNotNull(loaded);
        ExrTestHelper.EqualImages(part, loaded);
        Assert.AreEqual(ExrResult.Unsupported, Exr.SaveEXRImageToMemory(loaded, out byte[] rejected));
        Assert.AreEqual(0, rejected.Length);
        Part packed = PartConversion.FromInterleavedFloat(new float[4096], 128, 32, 1, PixelType.Half, Compression.PIZ);
        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(packed, out bytes));
        ExrBinaryMutationHelper.SetHeaderByteAttributeValue(bytes, 0, "compression", "compression", (byte)compression);
        Assert.AreNotEqual(ExrResult.Success, Exr.LoadEXRImageFromMemory(bytes, out loaded));
        Assert.IsNull(loaded);
    }
}
