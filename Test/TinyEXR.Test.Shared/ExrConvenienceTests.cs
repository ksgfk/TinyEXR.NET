using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using TinyEXR.Format;

namespace TinyEXR.Test;

[TestClass]
public sealed class ExrConvenienceTests
{
    public static IEnumerable<object[]> Compressions()
        => Enum.GetValues<Compression>().Where(c => c <= Compression.HTJ2K32 && c != Compression.DWAA && c != Compression.DWAB)
            .Select(c => new object[] { c });

    [TestMethod(DisplayName = "Exr unsorted RGB input matches Reader for every supported codec")]
    [DynamicData(nameof(Compressions))]
    public void UnsortedRgbUsesNames(Compression compression)
    {
        Header header = new(PartType.Scanline, new Box2i(-4, -2, 123, 29),
            new[] { new Channel("R", PixelType.Float), new Channel("G", PixelType.Float), new Channel("B", PixelType.Float) },
            compression, LineOrder.DecreasingY, name: "named-single");
        Part part = ExrTestHelper.Flat(header,
            ExrTestHelper.FloatChannel("R", PixelType.Float, Enumerable.Repeat(1.0f, 4096).ToArray()),
            ExrTestHelper.FloatChannel("G", PixelType.Float, Enumerable.Repeat(0.5f, 4096).ToArray()),
            ExrTestHelper.FloatChannel("B", PixelType.Float, Enumerable.Repeat(0.25f, 4096).ToArray()));
        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(part, out byte[] encoded));
        Assert.AreEqual(LineOrder.DecreasingY, header.LineOrder);
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromMemory(encoded, out Part? loaded));
        Assert.IsNotNull(loaded);
        Assert.AreEqual("named-single", loaded.Header.Name);
        Assert.AreEqual(LineOrder.IncreasingY, loaded.Header.LineOrder);
        ExrTestHelper.EqualImages(part, loaded);
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRFromMemory(encoded, out float[] rgba, out int width, out int height));
        Assert.AreEqual(128, width);
        Assert.AreEqual(32, height);
        for (int i = 0; i < rgba.Length; i += 4)
            CollectionAssert.AreEqual(new[] { 1f, 0.5f, 0.25f, 1f }, rgba.AsSpan(i, 4).ToArray());
        WriterResult<byte[]> direct = ExrFile.SaveToMemory(new Image(new[] { part }));
        Assert.AreEqual(ExrResult.Success, direct.Status);
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromMemory(direct.Value!, out _, out Header? directHeader));
        Assert.IsNotNull(directHeader);
        Assert.AreEqual(LineOrder.DecreasingY, directHeader!.LineOrder);
    }

    [TestMethod(DisplayName = "Exr simple save preserves components, HALF and default compression")]
    public void SimpleSaveContracts()
    {
        foreach (int components in new[] { 1, 3, 4 })
            foreach (bool half in new[] { false, true })
                foreach (int width in new[] { 2, 16 })
                {
                    float[] values = Enumerable.Range(0, width * 2 * components).Select(i => (i % components + 1) * 0.25f).ToArray();
                    Assert.AreEqual(ExrResult.Success, Exr.SaveEXRToMemory(values, width, 2, components, half, out byte[] bytes));
                    Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromMemory(bytes, out Part? part));
                    Assert.IsNotNull(part);
                    Assert.AreEqual(width < 16 ? Compression.None : Compression.ZIP, part.Header.Compression);
                    Assert.IsTrue(part.Header.Channels.All(c => c.PixelType == (half ? PixelType.Half : PixelType.Float)));
                    if (components == 1)
                        Assert.AreEqual("A", part.Header.Channels[0].Name);
                    Assert.AreEqual(ExrResult.Success, Exr.LoadEXRFromMemory(bytes, out float[] rgba, out _, out _));
                    for (int p = 0; p < width * 2; p++)
                        for (int c = 0; c < 4; c++)
                            Assert.AreEqual(components == 1 ? values[p] : c == 3 && components == 3 ? 1 : values[p * components + c], rgba[p * 4 + c]);
                }
        Assert.AreEqual(ExrResult.InvalidArgument, Exr.SaveEXRToMemory(new float[2], 1, 1, 2, false, out byte[] failed));
        Assert.AreEqual(0, failed.Length);
    }

    [TestMethod(DisplayName = "Exr immutable metadata and automatic UTF-8 long names")]
    public void NamesAndImmutableHeaders()
    {
        string channelName = new string('光', 14);
        Header header = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel(channelName, PixelType.Float) },
            attributes: new[] { new HeaderAttribute(new string('a', 40), "string", Encoding.UTF8.GetBytes("kept")) });
        Assert.AreEqual(ExrResult.Success, Exr.EXRSetNameAttr(header, new string('n', 50), out Header? named));
        Assert.IsNotNull(named);
        Assert.AreEqual(string.Empty, header.Name);
        Part part = ExrTestHelper.Flat(named, ExrTestHelper.FloatChannel(channelName, PixelType.Float, new[] { 2f }));
        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(part, out byte[] bytes));
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromMemory(bytes, out uint version, out Header? decoded));
        Assert.IsNotNull(decoded);
        Assert.AreNotEqual(0U, version & (1U << 10));
        ExrTestHelper.EqualHeaders(named, decoded);
        Assert.AreEqual(ExrResult.Success, Exr.EXRSetSpectralAttributes(named, SpectralType.Emissive, "radiance", out Header? spectral));
        Assert.IsNotNull(spectral);
        Assert.AreEqual("radiance", Exr.EXRGetSpectralUnits(spectral));
        Assert.IsNull(Exr.EXRGetSpectralUnits(named));
    }

    [TestMethod(DisplayName = "Exr single-part multipart rejection matches memory, stream and file")]
    public void OnePartMultipartAndStreamPosition()
    {
        Part part = CreatePart(PartType.Tiled, Compression.HTJ2K32, TileLevelMode.RipmapLevels, "only");
        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRMultipartImageToMemory(new Image(new[] { part }), out byte[] bytes));
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromMemory(bytes, out uint version));
        Assert.AreNotEqual(0U, version & (1U << 12));
        Assert.AreEqual(0U, version & (1U << 9));
        Assert.AreEqual(ExrResult.Unsupported, Exr.ParseEXRHeaderFromMemory(bytes, out uint rejectedVersion, out Header? rejected));
        Assert.AreEqual(0U, rejectedVersion);
        Assert.IsNull(rejected);
        Assert.AreEqual(ExrResult.Unsupported, Exr.LoadEXRImageFromMemory(bytes, out Part? failed));
        Assert.IsNull(failed);
        string path = Path.Combine(Path.GetTempPath(), $"tinyexr-{Guid.NewGuid():N}.exr");
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.AreEqual(ExrResult.Unsupported, Exr.ParseEXRHeaderFromFile(path, out _, out _));
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRMultipartImageFromFile(path, out Image? fileImage));
            Assert.IsNotNull(fileImage);
            ExrTestHelper.EqualImages(part, fileImage.Parts[0]);
        }
        finally
        {
            File.Delete(path);
        }
        using MemoryStream stream = new(new byte[13].Concat(bytes).ToArray());
        stream.Position = 13;
        Assert.IsTrue(Exr.IsEXRFromStream(stream));
        Assert.AreEqual(13, stream.Position);
        Assert.AreEqual(ExrResult.Unsupported, Exr.ParseEXRHeaderFromStream(stream, out _, out _));
        Assert.AreEqual(13, stream.Position);
        Assert.AreEqual(ExrResult.Unsupported, Exr.LoadEXRImageFromStream(stream, out _));
        Assert.AreEqual(13, stream.Position);
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRMultipartHeaderFromStream(stream, out _, out Header[] headers));
        Assert.AreEqual(1, headers.Length);
        Assert.AreEqual(13, stream.Position);
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRMultipartImageFromStream(stream, out Image? image));
        Assert.IsNotNull(image);
        Assert.AreEqual(13, stream.Position);
        ExrTestHelper.EqualImages(part, image.Parts[0]);
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod(DisplayName = "Exr mixed flat/deep multipart preserves every mip and rip level")]
    public void MixedMultipartAndDeepLevels()
    {
        foreach (Compression compression in new[] { Compression.None, Compression.RLE, Compression.ZIPS })
        {
            Part[] parts =
            {
                CreatePart(PartType.Scanline, compression, TileLevelMode.OneLevel, "flat"),
                CreatePart(PartType.DeepScanline, compression, TileLevelMode.OneLevel, "deep"),
                CreatePart(PartType.Tiled, compression, TileLevelMode.MipmapLevels, "tiles"),
                CreatePart(PartType.DeepTiled, compression, TileLevelMode.RipmapLevels, "deep-tiles"),
            };
            Assert.AreEqual(ExrResult.Success, Exr.SaveEXRMultipartImageToMemory(new Image(parts), out byte[] bytes), compression.ToString());
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRMultipartImageFromMemory(bytes, out Image? loaded));
            Assert.IsNotNull(loaded);
            for (int i = 0; i < parts.Length; i++)
                ExrTestHelper.EqualImages(parts[i], loaded.Parts[i]);
            Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(parts[3], out byte[] deep));
            Assert.AreEqual(ExrResult.Unsupported, Exr.LoadEXRImageFromMemory(deep, out Part? flat));
            Assert.IsNull(flat);
            Assert.AreEqual(ExrResult.Success, Exr.LoadDeepEXRFromMemory(deep, out Part? deepPart));
            Assert.IsNotNull(deepPart);
            ExrTestHelper.EqualImages(parts[3], deepPart);
        }
    }

    [TestMethod(DisplayName = "Exr magic and version probes do not parse headers")]
    public void PrefixOnlyValidation()
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 20000630);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        Assert.IsTrue(Exr.IsEXRFromMemory(bytes.AsSpan(0, 4)));
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromMemory(bytes, out uint version));
        Assert.AreEqual(2U, version);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2U | (1U << 31));
        Assert.IsTrue(Exr.IsEXRFromMemory(bytes));
        Assert.AreEqual(ExrResult.InvalidFile, Exr.ParseEXRVersionFromMemory(bytes, out version));
        Assert.AreEqual(0U, version);
        using MemoryStream stream = new(new byte[17].Concat(bytes).ToArray());
        stream.Position = 17;
        Assert.AreEqual(ExrResult.InvalidFile, Exr.ParseEXRVersionFromStream(stream, out _));
        Assert.AreEqual(17, stream.Position);
        Assert.AreEqual(ExrResult.InvalidFile, Exr.ParseEXRHeaderFromStream(stream, out _, out Header? header));
        Assert.IsNull(header);
        Assert.AreEqual(17, stream.Position);
        Assert.AreEqual(ExrResult.InvalidFile, Exr.LoadEXRFromStream(stream, out float[] rgba, out int width, out int height));
        Assert.AreEqual(0, rgba.Length);
        Assert.AreEqual(0, width);
        Assert.AreEqual(0, height);
        Assert.AreEqual(17, stream.Position);
    }

    internal static Part CreatePart(PartType type, Compression compression, TileLevelMode mode, string name)
    {
        bool tiled = type is PartType.Tiled or PartType.DeepTiled;
        bool deep = type is PartType.DeepTiled or PartType.DeepScanline;
        Header header = new(type, new Box2i(-4, -2, 12, 6), new[] { new Channel("Y", PixelType.Float) }, compression,
            tiles: tiled ? new TileDescription(8, 4, mode, TileRoundingMode.RoundUp) : null, name: name);
        int xs = !tiled || mode == TileLevelMode.OneLevel ? 1 : ExrFormatParser.LevelCount(17, TileRoundingMode.RoundUp);
        int ys = !tiled || mode == TileLevelMode.OneLevel ? 1 : ExrFormatParser.LevelCount(9, TileRoundingMode.RoundUp);
        List<PartLevel> levels = new();
        for (int y = 0; y < (mode == TileLevelMode.MipmapLevels ? Math.Max(xs, ys) : ys); y++)
            for (int x = 0; x < (mode == TileLevelMode.MipmapLevels ? 1 : xs); x++)
            {
                int lx = mode == TileLevelMode.MipmapLevels ? y : x;
                int w = (int)ExrFormatParser.LevelSize(17, lx, TileRoundingMode.RoundUp);
                int h = (int)ExrFormatParser.LevelSize(9, y, TileRoundingMode.RoundUp);
                Box2i region = new(-4, -2, -4 + w - 1, -2 + h - 1);
                int[] counts = Enumerable.Range(0, w * h).Select(i => i % 4).ToArray();
                float[] values = Enumerable.Range(0, deep ? counts.Sum() : w * h).Select(i => (i % 13) * 0.25f + lx + y).ToArray();
                ChannelBuffer[] buffers = { new("Y", PixelType.Float, ExrTestHelper.ToBytes(values)) };
                levels.Add(deep ? new DeepLevel(lx, y, region, counts, buffers) : new FlatLevel(lx, y, region, buffers));
            }
        return new Part(header, levels, isComplete: true);
    }
}
