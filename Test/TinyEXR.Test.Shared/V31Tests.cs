using System.Buffers.Binary;
using TinyEXR.IO;

namespace TinyEXR.Test;

[TestClass]
public sealed class V31Tests
{
    private static Part Flat(string name = "", int width = 4, int height = 4)
    {
        Header h = new(PartType.Scanline, new Box2i(0, 0, width - 1, height - 1),
            new[] { new Channel("Y", PixelType.Float) }, Compression.ZIP, name: name);
        return new Part(h, new[] { new FlatLevel(0, 0, h.DataWindow,
            new[] { new ChannelBuffer("Y", PixelType.Float, new byte[width * height * 4]) }) }, true);
    }

    private static byte[] Save(params Part[] parts)
    {
        WriterResult<byte[]> result = ExrFile.SaveToMemory(new Image(parts));
        Assert.AreEqual(ExrResult.Success, result.Status, result.Error?.ToString());
        return result.Value!;
    }

    [TestMethod]
    public void ContextEnforcesExclusiveLifetimeAndCanBeReusedAcrossReadAndWrite()
    {
        using ExrContext context = new();
        byte[] data = Save(Flat());
        using (ExrReader reader = ExrReader.OpenMemory(data, new ReaderOptions(context: context)))
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => ExrReader.OpenMemory(data, new ReaderOptions(context: context)));
            Assert.ThrowsExactly<InvalidOperationException>(() => context.Trim());
            Assert.ThrowsExactly<InvalidOperationException>(() => ExrFile.SaveToMemory(new Image(new[] { Flat() }), new WriterOptions(context: context)));
            Assert.ThrowsExactly<InvalidOperationException>(() => context.Dispose());
            Assert.AreEqual(ExrResult.Success, reader.ParseHeader().Status);
            Assert.AreEqual(ExrResult.Success, reader.ReadPart(0).Status);
        }
        WriterResult<byte[]> saved = ExrFile.SaveToMemory(new Image(new[] { Flat() }), new WriterOptions(context: context));
        Assert.AreEqual(ExrResult.Success, saved.Status);
        Assert.AreEqual(ExrResult.Success, ExrFile.LoadFromMemory(saved.Value!, new ReaderOptions(context: context)).Status);
        context.Trim();
        context.Dispose();
        context.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => ExrReader.OpenMemory(data, new ReaderOptions(context: context)));
    }

    [TestMethod]
    public async Task FileBudgetsUseRelativeExtentAndPreserveUnknownLengthRetries()
    {
        byte[] data = Save(Flat());
        ReaderOptions tooSmall = new(new ReaderLimits(maximumFileByteCount: data.Length - 1));
        Assert.AreEqual(ExrResult.LimitExceeded, ExrFile.LoadFromMemory(data, tooSmall).Status);
        Assert.AreEqual(ExrResult.Success, ExrFile.LoadFromMemory(data, new ReaderOptions(new ReaderLimits(maximumFileByteCount: data.Length))).Status);
        using SuppliedDataSource source = SuppliedDataSource.CreateUnknownLength();
        using ExrReader reader = ExrReader.OpenSource(source, tooSmall);
        Assert.AreEqual(ExrResult.WouldBlock, reader.ParseHeader().Status);
        source.Supply(0, data);
        Assert.AreEqual(ExrResult.Success, reader.ParseHeader().Status);
        Assert.AreEqual(ExrResult.LimitExceeded, reader.ReadPart(0).Status);
        using MemoryStream stream = new();
        stream.Write(new byte[37]);
        WriterOptions exact = new(new WriterLimits(maximumFileByteCount: data.Length));
        Assert.AreEqual(ExrResult.Success, (await ExrFile.SaveToStreamAsync(new Image(new[] { Flat() }), stream, exact)).Status);
        Assert.AreEqual(37 + data.Length, stream.Length);
        WriterResult<byte[]> limited = ExrFile.SaveToMemory(new Image(new[] { Flat() }), new WriterOptions(new WriterLimits(maximumFileByteCount: data.Length - 1)));
        Assert.AreEqual(ExrResult.LimitExceeded, limited.Status);
        Assert.IsNull(limited.Value);
    }

    [TestMethod]
    public async Task MaterializationBudgetIncludesAllPartsAndWriterInput()
    {
        Image image = new(new[] { Flat("a"), Flat("b") });
        byte[] data = Save(image.Parts.ToArray());
        ReaderOptions limits = new(new ReaderLimits(maximumMaterializedByteCount: 127));
        ReaderResult<Image> result = ExrFile.LoadFromMemory(data, limits);
        Assert.AreEqual(ExrResult.LimitExceeded, result.Status);
        Assert.IsNull(result.Value);
        Assert.AreEqual(128L, ((ReaderLimitExceededException)result.Error!).Actual);
        using MemoryStream stream = new(data);
        Assert.AreEqual(ExrResult.LimitExceeded, (await ExrFile.LoadFromStreamAsync(stream, limits)).Status);
        Assert.AreEqual(ExrResult.Success, ExrFile.LoadFromMemory(data, new ReaderOptions(new ReaderLimits(maximumMaterializedByteCount: 128))).Status);
        Assert.AreEqual(ExrResult.LimitExceeded, ExrFile.SaveToMemory(image, new WriterOptions(new WriterLimits(maximumMaterializedByteCount: 127))).Status);
    }

    [TestMethod]
    public void TypedAttributesAreImmutableAndPreviewIsValidated()
    {
        Header original = Flat().Header;
        byte[] pixels = { 1, 2, 3, 4, 5, 6, 7, 8 };
        PreviewImage preview = new(2, 1, pixels);
        pixels[0] = 255;
        float nan = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc12345));
        Header changed = original.WithIntAttribute("frame", 42).WithFloatAttribute("custom", nan).WithPreviewImage(preview);
        Assert.IsFalse(original.TryGetIntAttribute("frame", out _));
        Assert.IsTrue(changed.TryGetIntAttribute("frame", out int frame));
        Assert.AreEqual(42, frame);
        Assert.IsFalse(changed.TryGetFloatAttribute("frame", out _));
        Assert.IsTrue(changed.TryGetFloatAttribute("custom", out float restored));
        Assert.AreEqual(BitConverter.SingleToInt32Bits(nan), BitConverter.SingleToInt32Bits(restored));
        Assert.ThrowsExactly<ArgumentException>(() => original.WithIntAttribute("dataWindow", 1));
        Part part = new(changed, Flat().Levels, true);
        Header decoded = ExrFile.LoadFromMemory(Save(part)).Value!.Parts[0].Header;
        Assert.IsTrue(decoded.TryGetPreviewImage(out PreviewImage? decodedPreview));
        Assert.AreEqual(1, decodedPreview!.Rgba8[0]);
        Assert.IsTrue(decoded.TryGetFloatAttribute("custom", out restored));
        Assert.AreEqual(BitConverter.SingleToInt32Bits(nan), BitConverter.SingleToInt32Bits(restored));
        Assert.ThrowsExactly<ArgumentException>(() => new PreviewImage(2, 1, new byte[7]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PreviewImage(int.MaxValue, int.MaxValue, Array.Empty<byte>()));
        Header malformed = new(PartType.Scanline, original.DataWindow, original.Channels,
            attributes: new[] { new HeaderAttribute("preview", "preview", new byte[8]) });
        Assert.IsFalse(malformed.TryGetPreviewImage(out _));
    }

    [TestMethod]
    public void MultipartNamesAreGeneratedWithoutChangingCallerObjects()
    {
        Part a = Flat(), b = Flat("part0"), c = Flat();
        Image decoded = ExrFile.LoadFromMemory(Save(a, b, c)).Value!;
        CollectionAssert.AreEqual(new[] { "part1", "part0", "part2" }, decoded.Parts.Select(p => p.Header.Name).ToArray());
        Assert.AreEqual("", a.Header.Name);
        Assert.AreEqual("", c.Header.Name);
        Assert.ThrowsExactly<ArgumentException>(() => new Image(new[] { Flat("same"), Flat("same") }));
    }

    [TestMethod]
    public void SpectralSelectionAndExpandedBudgetApplyBeforeCubeAllocation()
    {
        Part spectral = Spectral.CreateEmissivePart(8, 8, new[] { 400f, 500f, 600f, 700f }, new float[256]);
        spectral = new Part(new Header(spectral.Header.PartType, spectral.Header.DataWindow, spectral.Header.Channels,
            spectral.Header.Compression, name: "spectrum", attributes: spectral.Header.Attributes), spectral.Levels, true);
        byte[] data = Save(Flat("flat", 8, 8), spectral);
        Assert.AreEqual(ExrResult.Success, SpectralImage.LoadFromMemory(data).Status);
        Assert.AreEqual(ExrResult.Success, SpectralImage.LoadFromMemory(data, 1).Status);
        Assert.AreEqual(ExrResult.Unsupported, SpectralImage.LoadFromMemory(data, 0).Status);
        Assert.AreEqual(ExrResult.InvalidArgument, SpectralImage.LoadFromMemory(data, 2).Status);
        Assert.AreEqual(ExrResult.InvalidArgument, SpectralImage.LoadFromMemory(data, -1).Status);
        Assert.ThrowsExactly<ReaderLimitExceededException>(() => SpectralImage.FromPart(spectral, new ReaderLimits(maximumMaterializedByteCount: 512)));
        Assert.ThrowsExactly<ReaderLimitExceededException>(() => SpectralImage.FromPart(spectral, new ReaderLimits(maximumMaterializedByteCount: 1024)));
        Assert.AreEqual(4, SpectralImage.FromPart(spectral, new ReaderLimits(maximumMaterializedByteCount: 1040)).WavelengthCount);
        Assert.IsFalse(Spectral.TryParseChannelWavelength("S0.100000000,000000nm", out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Spectral.GetChannelName(SpectralType.Emissive, 100000000f));
    }

    [TestMethod]
    public void SpectralWavelengthQuotaRejectsInsteadOfTruncating()
    {
        Channel[] channels = Enumerable.Range(0, Spectral.MaximumWavelengthCount + 1)
            .Select(wavelength => new Channel(Spectral.GetChannelName(SpectralType.Emissive, wavelength), PixelType.Half))
            .ToArray();
        Header header = new(PartType.Scanline, new Box2i(0, 0, 0, 0), channels);
        Assert.ThrowsExactly<ReaderLimitExceededException>(() => Spectral.GetWavelengths(header));
    }

    [TestMethod]
    public void OfficialCompressionIdsAndDeepRestrictionsAreEnforced()
    {
        Assert.AreEqual(12, Convert.ToInt32(Enum.Parse<Compression>("LJ2K")));
        Assert.AreEqual(13, Convert.ToInt32(Enum.Parse<Compression>("ZSTD")));
        foreach (Compression compression in new[] { Compression.ZIP, Compression.PIZ, Compression.HTJ2K32, Compression.HTJ2K256 })
        {
            Header header = new(PartType.DeepScanline, new Box2i(0, 0, 0, 0), new[] { new Channel("Z", PixelType.Float) }, compression);
            Part part = new(header, new[] { new DeepLevel(0, 0, header.DataWindow, new[] { 0 },
                new[] { new ChannelBuffer("Z", PixelType.Float, Array.Empty<byte>()) }) }, true);
            Assert.AreEqual(ExrResult.InvalidArgument, ExrFile.SaveToMemory(new Image(new[] { part })).Status);
        }
        byte[] encoded = Save(Flat());
        byte[] marker = System.Text.Encoding.ASCII.GetBytes("compression\0compression\0");
        int index = encoded.AsSpan().IndexOf(marker) + marker.Length + 4;
        foreach (Compression compression in new[] { Compression.LJ2K, Compression.ZSTD })
        {
            encoded[index] = (byte)compression;
            Assert.AreEqual(ExrResult.Unsupported, ExrFile.LoadFromMemory(encoded).Status);
        }
    }
}
