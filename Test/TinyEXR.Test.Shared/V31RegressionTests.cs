using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using TinyEXR.Codecs;

namespace TinyEXR.Test;

[TestClass]
public sealed class V31RegressionTests
{
    private static byte[] Save(Part part)
    {
        WriterResult<byte[]> result = ExrFile.SaveToMemory(new Image(new[] { part }));
        Assert.AreEqual(ExrResult.Success, result.Status, result.Error?.ToString());
        return result.Value!;
    }

    private static Part Flat(Header header) => new(header, new[] { new FlatLevel(0, 0, header.DataWindow,
        header.Channels.Select(c => new ChannelBuffer(c.Name, c.PixelType,
            new byte[checked((int)(header.DataWindow.Width * header.DataWindow.Height * (c.PixelType == PixelType.Half ? 2 : 4)))]))) }, true);

    [TestMethod]
    public void PreviewPayloadAndBudgetsAreValidatedBeforeUse()
    {
        Header header = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel("Y", PixelType.Float) });
        header = header.WithPreviewImage(new PreviewImage(2, 1, new byte[8]));
        byte[] valid = Save(Flat(header));
        byte[] marker = Encoding.ASCII.GetBytes("preview\0preview\0");
        int value = valid.AsSpan().IndexOf(marker) + marker.Length + 4;
        Assert.IsTrue(value > marker.Length);
        foreach (uint width in new uint[] { 0, 3, uint.MaxValue })
        {
            byte[] damaged = (byte[])valid.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(damaged.AsSpan(value), width);
            Assert.AreEqual(ExrResult.InvalidFile, ExrFile.LoadFromMemory(damaged).Status);
        }
        Assert.AreEqual(ExrResult.LimitExceeded, ExrFile.LoadFromMemory(valid,
            new ReaderOptions(new ReaderLimits(maximumAttributeByteCount: 15))).Status);
        Assert.AreEqual(ExrResult.LimitExceeded, ExrFile.SaveToMemory(new Image(new[] { Flat(header) }),
            new WriterOptions(new WriterLimits(maximumHeaderByteCount: 16))).Status);
        Header bad = new(PartType.Scanline, header.DataWindow, header.Channels,
            attributes: new[] { new HeaderAttribute("preview", "preview", new byte[8]) });
        Assert.AreEqual(ExrResult.InvalidArgument, ExrFile.SaveToMemory(new Image(new[] { Flat(bad) })).Status);
    }

    [TestMethod]
    public void SpectralHalfInputCannotBypassExpandedFloatBudgetOrDecodeOtherParts()
    {
        Part source = Spectral.CreateEmissivePart(8, 8, new[] { 400f, 500f, 600f, 700f }, new float[256]);
        Header header = new(PartType.Scanline, source.Header.DataWindow,
            source.Header.Channels.Select(c => new Channel(c.Name, PixelType.Half)), Compression.ZIP,
            name: "spectral", attributes: source.Header.Attributes);
        Part half = Flat(header);
        byte[] data = Save(half);
        ReaderOptions limited = new(new ReaderLimits(maximumMaterializedByteCount: 512));
        Assert.AreEqual(ExrResult.Success, ExrFile.LoadFromMemory(data, limited).Status);
        ReaderResult<SpectralImage> expanded = SpectralImage.LoadFromMemory(data, limited);
        Assert.AreEqual(ExrResult.LimitExceeded, expanded.Status);
        Assert.IsNull(expanded.Value);
        ReaderLimitExceededException error = (ReaderLimitExceededException)expanded.Error!;
        Assert.AreEqual(1040L, error.Actual); // 1024 bytes of samples plus four wavelengths.

        Header first = new(PartType.Scanline, header.DataWindow, new[] { new Channel("Y", PixelType.Float) }, name: "flat");
        byte[] multipart = ExrFile.SaveToMemory(new Image(new[] { Flat(first), half })).Value!;
        using (ExrReader reader = ExrReader.OpenMemory(multipart))
        {
            Assert.IsTrue(reader.ParseHeader().IsSuccess);
            BlockInfo info = reader.GetBlockInfo(0, 0);
            BinaryPrimitives.WriteInt32LittleEndian(multipart.AsSpan((int)info.FileOffset + 4), 1000); // Invalid scanline in the unselected part.
        }
        Assert.AreEqual(ExrResult.Corrupt, ExrFile.LoadFromMemory(multipart).Status);
        Assert.AreEqual(ExrResult.Success, SpectralImage.LoadFromMemory(multipart).Status);
        Assert.AreEqual(ExrResult.Success, SpectralImage.LoadFromMemory(multipart, 1).Status);
    }

    [TestMethod]
    public void FileBudgetCountsAccessedExtentInsteadOfTrailingContainerBytes()
    {
        Header header = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel("Y", PixelType.Float) });
        byte[] data = Save(Flat(header));
        byte[] container = new byte[data.Length + 1024];
        data.CopyTo(container, 0);
        Assert.AreEqual(ExrResult.Success, ExrFile.LoadFromMemory(container,
            new ReaderOptions(new ReaderLimits(maximumFileByteCount: data.Length))).Status);
    }

    [TestMethod]
    public void DeepEdgeTilesAcceptOnlyCompactOrNominalRawCountTables()
    {
        Header header = new(PartType.DeepTiled, new Box2i(-2, -1, 0, 0),
            new[] { new Channel("A", PixelType.Half), new Channel("Z", PixelType.Float) },
            Compression.None, tiles: new TileDescription(4, 4));
        int[] counts = { 0, 1, 2, 3, 1, 0 };
        byte[] half = MemoryMarshal.AsBytes(Enumerable.Range(0, 7).Select(i => (ushort)(0x3800 + i)).ToArray().AsSpan()).ToArray();
        byte[] floats = MemoryMarshal.AsBytes(Enumerable.Range(0, 7).Select(i => (float)i).ToArray().AsSpan()).ToArray();
        Part part = new(header, new[] { new DeepLevel(0, 0, header.DataWindow, counts,
            new[] { new ChannelBuffer("A", PixelType.Half, half), new ChannelBuffer("Z", PixelType.Float, floats) }) }, true);
        byte[] compact = Save(part);
        using ExrReader reader = ExrReader.OpenMemory(compact);
        Assert.IsTrue(reader.ParseHeader().IsSuccess);
        BlockInfo info = reader.GetBlockInfo(0, 0);
        int start = (int)info.FileOffset + info.ChunkHeaderByteCount;
        int end = start + counts.Length * 4;
        foreach (int tableBytes in new[] { 64, 60 })
        {
            byte[] padded = new byte[compact.Length + tableBytes - counts.Length * 4];
            compact.AsSpan(0, end).CopyTo(padded);
            padded.AsSpan(end, tableBytes - counts.Length * 4).Fill(0xff); // Unspecified official padding is ignored.
            compact.AsSpan(end).CopyTo(padded.AsSpan(start + tableBytes));
            BinaryPrimitives.WriteInt64LittleEndian(padded.AsSpan((int)info.FileOffset + 16), tableBytes);
            ReaderResult<Image> result = ExrFile.LoadFromMemory(padded);
            Assert.AreEqual(tableBytes == 64 ? ExrResult.Success : ExrResult.Corrupt, result.Status);
            if (result.IsSuccess)
            {
                DeepLevel level = (DeepLevel)result.Value!.Parts[0].Levels[0];
                CollectionAssert.AreEqual(counts, level.SampleCounts.ToArray());
                CollectionAssert.AreEqual(half, level.GetChannel("A").Data.ToArray());
                CollectionAssert.AreEqual(floats, level.GetChannel("Z").Data.ToArray());
            }
        }
    }

    [TestMethod]
    public void HuffmanWorkspaceRejectsStaleCodesAndRleRejectsTrailingData()
    {
        CodecWorkspace workspace = new();
        CodecContext context = new(new[] { new Channel("H", PixelType.Half) }, new Box2i(0, 0, 127, 31));
        byte[] raw = new byte[context.ByteCount], decoded = new byte[raw.Length];
        for (int pass = 0; pass < 6; pass++)
        {
            if ((pass & 1) == 0) new Random(pass).NextBytes(raw); else Array.Clear(raw);
            byte[] compressed = PizCodec.Encode(context, raw, workspace);
            Assert.AreEqual(ExrResult.Success, PizCodec.Decode(context, compressed, decoded, workspace));
            CollectionAssert.AreEqual(raw, decoded);
            Assert.AreEqual(ExrResult.Corrupt, PizCodec.Decode(context, compressed.Concat(new byte[] { 0 }).ToArray(), decoded, workspace));
        }
        byte[] rle = RleCodec.Encode(new byte[1024], workspace);
        Assert.AreEqual(ExrResult.Corrupt, RleCodec.Decode(rle.Concat(new byte[] { 0, 0 }).ToArray(), new byte[1024], workspace));
    }

    [TestMethod]
    public void MultipartRejectsInconsistentDisplayWindowsAndAttributeLimitsIncludeStandardFields()
    {
        Header a = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel("Y", PixelType.Float) }, name: "a");
        Header b = new(PartType.Scanline, a.DataWindow, a.Channels, displayWindow: new Box2i(0, 0, 1, 1), name: "b");
        Assert.AreEqual(ExrResult.InvalidArgument, ExrFile.SaveToMemory(new Image(new[] { Flat(a), Flat(b) })).Status);
        Assert.AreEqual(ExrResult.LimitExceeded, ExrFile.SaveToMemory(new Image(new[] { Flat(a) }),
            new WriterOptions(new WriterLimits(maximumAttributesPerPart: 8))).Status);
    }
}
