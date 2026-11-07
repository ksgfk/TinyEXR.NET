using TinyEXR.IO;

[assembly: DoNotParallelize]

namespace TinyEXR.PackageSmoke;

[TestClass]
[TestCategory("Package")]
public sealed class PackageTests
{
    [TestMethod]
    public void ModelsAndIoRoundTripFromInstalledPackage()
    {
        Part part = Spectral.CreateEmissivePart(1, 1, new[] { 550.0f }, new[] { 2.5f }, "radiance");
        WriterResult<byte[]> saved = ExrFile.SaveToMemory(new Image(new[] { part }), Compression.ZIP);
        Assert.IsTrue(saved.IsSuccess, saved.Error?.ToString());
        Assert.IsNotNull(saved.Value);
        ReaderResult<Image> loaded = ExrFile.LoadFromMemory(saved.Value);
        Assert.IsTrue(loaded.IsSuccess, loaded.Error?.ToString());
        Assert.IsNotNull(loaded.Value);
        Assert.AreEqual(1, loaded.Value.Parts.Count);
        using ExrReader reader = ExrReader.OpenSource(new MemoryDataSource(saved.Value));
        Assert.IsTrue(reader.ParseHeader().IsSuccess);
        Assert.IsTrue(reader.ReadPart(0).IsSuccess);
    }

    [TestMethod]
    public void SpectralAndConvenienceApisAreAvailable()
    {
        Part part = Spectral.CreateEmissivePart(1, 1, new[] { 550.0f }, new[] { 2.5f }, "radiance");
        WriterResult<byte[]> saved = ExrFile.SaveToMemory(new Image(new[] { part }));
        Assert.IsTrue(saved.IsSuccess, saved.Error?.ToString());
        ReaderResult<SpectralImage> spectral = SpectralImage.LoadFromMemory(saved.Value!);
        Assert.IsTrue(spectral.IsSuccess, spectral.Error?.ToString());
        Assert.AreEqual(SpectralType.Emissive, spectral.Value!.SpectrumType);
        Assert.AreEqual(2.5f, spectral.Value.GetSample(0, 0, 0, 0));
        Assert.AreEqual(ExrResult.Success, Exr.EXRSetSpectralAttributes(part.Header, SpectralType.Emissive, "radiance", out Header? updated));
        Assert.IsNotNull(updated);
        Assert.AreEqual(SpectralType.Emissive, Exr.EXRGetSpectrumType(updated));
    }

    [TestMethod]
    [DataRow(Compression.None)]
    [DataRow(Compression.RLE)]
    [DataRow(Compression.ZIPS)]
    [DataRow(Compression.ZIP)]
    [DataRow(Compression.PIZ)]
    [DataRow(Compression.PXR24)]
    [DataRow(Compression.B44)]
    [DataRow(Compression.B44A)]
    [DataRow(Compression.HTJ2K32)]
    [DataRow(Compression.HTJ2K256)]
    public void CodecAndStreamConvenienceRoundTrip(Compression compression)
    {
        Part pixels = PartConversion.FromInterleavedFloat(Enumerable.Repeat(0.5f, 128 * 32 * 3).ToArray(),
            128, 32, 3, PixelType.Half, compression);
        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(pixels, out byte[] bytes));
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromMemory(bytes, out Part? decoded));
        Assert.IsNotNull(decoded);
        foreach (ChannelBuffer channel in pixels.Levels[0].Channels)
            Assert.IsTrue(channel.Data.SequenceEqual(decoded.Levels[0].GetChannel(channel.Name).Data), channel.Name);
        using MemoryStream stream = new(new byte[13].Concat(bytes).ToArray());
        stream.Position = 13;
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRFromStream(stream, out float[] rgba, out int width, out int height));
        Assert.AreEqual(13L, stream.Position);
        Assert.AreEqual(128, width);
        Assert.AreEqual(32, height);
        Assert.AreEqual(0.5f, rgba[0]);
        Assert.AreEqual(1f, rgba[3]);
    }

    [TestMethod]
    [DataRow(3, 1, 0)]
    public void ApiVersionRemains31(int major, int minor, int patch)
    {
        Version actual = new(ExrFile.ApiVersionMajor, ExrFile.ApiVersionMinor, ExrFile.ApiVersionPatch);
        Assert.AreEqual(new Version(major, minor, patch), actual);
    }

    [TestMethod]
    public void ContextMetadataAndBudgetsWorkFromInstalledPackage()
    {
        using ExrContext context = new();
        Part part = Spectral.CreateEmissivePart(1, 1, new[] { 550f }, new[] { 2.5f });
        Header metadata = part.Header.WithIntAttribute("frame", 12)
            .WithPreviewImage(new PreviewImage(1, 1, new byte[] { 1, 2, 3, 255 }));
        WriterResult<byte[]> saved = ExrFile.SaveToMemory(new Image(new[] { new Part(metadata, part.Levels, true) }), new WriterOptions(context: context));
        Assert.IsTrue(saved.IsSuccess, saved.Error?.ToString());
        ReaderResult<Image> loaded = ExrFile.LoadFromMemory(saved.Value!, new ReaderOptions(context: context));
        Assert.IsTrue(loaded.IsSuccess, loaded.Error?.ToString());
        Header header = loaded.Value!.Parts[0].Header;
        Assert.IsTrue(header.TryGetPreviewImage(out PreviewImage? preview));
        Assert.AreEqual((byte)255, preview!.Rgba8[3]);
        Assert.IsTrue(header.TryGetIntAttribute("frame", out int frame));
        Assert.AreEqual(12, frame);
        Assert.AreEqual(ExrResult.LimitExceeded, ExrFile.LoadFromMemory(saved.Value!,
            new ReaderOptions(new ReaderLimits(maximumFileByteCount: 8))).Status);
        context.Trim();
    }
}
