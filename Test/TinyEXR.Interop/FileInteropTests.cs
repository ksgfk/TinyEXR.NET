using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using TinyEXR.Codecs;
using TinyEXR.Format;

namespace TinyEXR.Interop;

[TestClass]
[TestCategory("OpenEXR")]
public sealed class FileInteropTests : InteropTestBase
{
    public static IEnumerable<object[]> FlatCompressions() =>
        Enum.GetValues<Compression>().Where(c => c <= Compression.HTJ2K32 && c is not (Compression.DWAA or Compression.DWAB))
            .Select(c => new object[] { c });

    public static IEnumerable<object[]> DeepCompressions() =>
        new[] { Compression.None, Compression.RLE, Compression.ZIPS }.Select(c => new object[] { c });

    public static IEnumerable<object[]> ScanlineCases() =>
        FlatCompressions().SelectMany(c => new[] { false, true }.Select(sampled => new object[] { c[0], sampled }));

    public static IEnumerable<object[]> FlatTileCases() => TileCases(FlatCompressions());
    public static IEnumerable<object[]> DeepTileCases() => TileCases(DeepCompressions());

    private static IEnumerable<object[]> TileCases(IEnumerable<object[]> compressions) =>
        from compression in compressions
        from mode in Enum.GetValues<TileLevelMode>()
        from multipart in new[] { false, true }
        select new object[] { compression[0], mode, multipart };

    public static IEnumerable<object[]> Fixtures() => new[]
    {
        "test/unit/regression/2by2.exr", "test/unit/regression/2by2_htj2k32.exr",
        "test/unit/regression/piz-bug-issue-100.exr", "test/unit/regression/issue-160-piz-decode.exr",
        "test/unit/regression/000-issue194.exr", "test/unit/regression/flaga.exr",
        "test/unit/regression/tiled_half_1x1_alpha.exr", "data/deep_tiled_sample.exr", "data/deepscanline.exr",
    }.Select(path => new object[] { path });

    [TestMethod]
    [DynamicData(nameof(FlatCompressions))]
    public async Task IndependentlyGeneratedOfficialFilePreservesSpecialValues(Compression compression)
    {
        string generated = Path.Combine(Work, "generated.exr"), dump = Path.Combine(Work, "generated.bin");
        await OracleAsync("file_oracle", "generate", ((int)compression).ToString(CultureInfo.InvariantCulture), generated);
        await OracleAsync("file_oracle", "dump", generated, dump);
        Equal(DumpManaged(generated), File.ReadAllBytes(dump), compression + " independent official generation");
        ReaderResult<Image> image = ExrFile.LoadFromFile(generated);
        Assert.IsTrue(image.IsSuccess, image.Error?.ToString());
        await VerifyRoundTripAsync(image.Value!, compression + " generated special values");
    }

    [TestMethod]
    [DynamicData(nameof(ScanlineCases))]
    public async Task FlatScanlineRoundTripsBothDirections(Compression compression, bool sampled)
    {
        Part part = CreatePart(PartType.Scanline, compression, TileLevelMode.OneLevel, "scanline", sampled);
        await VerifyRoundTripAsync(new Image(new[] { part }), $"{compression} scanline sampled={sampled}");
    }

    [TestMethod]
    [DynamicData(nameof(FlatTileCases))]
    public async Task FlatTilesRoundTripEveryLevel(Compression compression, TileLevelMode mode, bool multipart)
    {
        Part tiled = CreatePart(PartType.Tiled, compression, mode, "tiled");
        Part[] parts = multipart
            ? new[] { CreatePart(PartType.Scanline, compression, TileLevelMode.OneLevel, "scanline"), tiled }
            : new[] { tiled };
        await VerifyRoundTripAsync(new Image(parts), $"{compression} {mode} multipart={multipart}");
    }

    [TestMethod]
    [DynamicData(nameof(DeepCompressions))]
    public async Task DeepZeroSamplesRoundTrip(Compression compression)
    {
        Header header = new(PartType.DeepTiled, new Box2i(0, 0, 4, 2), new[] { new Channel("Z", PixelType.Float) },
            compression, tiles: new TileDescription(3, 2));
        Part part = new(header, new[] { new DeepLevel(0, 0, header.DataWindow, new int[15],
            new[] { new ChannelBuffer("Z", PixelType.Float, Array.Empty<byte>()) }) }, true);
        await VerifyRoundTripAsync(new Image(new[] { part }), compression + " zero deep samples");
    }

    [TestMethod]
    [DynamicData(nameof(DeepCompressions))]
    public async Task DeepScanlineRoundTripsBothDirections(Compression compression)
    {
        Part part = CreatePart(PartType.DeepScanline, compression, TileLevelMode.OneLevel, "deep");
        await VerifyRoundTripAsync(new Image(new[] { part }), compression + " deep scanline");
    }

    [TestMethod]
    [DynamicData(nameof(DeepTileCases))]
    public async Task DeepTilesRoundTripCountsAndSamplesOnEveryLevel(Compression compression, TileLevelMode mode, bool multipart)
    {
        Part tiled = CreatePart(PartType.DeepTiled, compression, mode, "deep-tiled");
        Part[] parts = multipart
            ? new[] { CreatePart(PartType.Scanline, compression, TileLevelMode.OneLevel, "scanline"),
                CreatePart(PartType.DeepScanline, compression, TileLevelMode.OneLevel, "deep"), tiled }
            : new[] { tiled };
        await VerifyRoundTripAsync(new Image(parts), $"{compression} deep {mode} mixed multipart={multipart}");
    }

    [TestMethod]
    [DataRow(Compression.HTJ2K32, PixelType.Half)]
    [DataRow(Compression.HTJ2K32, PixelType.Float)]
    [DataRow(Compression.HTJ2K256, PixelType.Half)]
    [DataRow(Compression.HTJ2K256, PixelType.Float)]
    public async Task HtjRgbRctPreservesSpecialValues(Compression compression, PixelType type)
    {
        Header header = new(PartType.Scanline, new Box2i(0, 0, 127, 255),
            new[] { new Channel("R", type), new Channel("B", type), new Channel("G", type) }, compression);
        uint[] bits = { 0, 0x80000000, 1, 0x007fffff, 0x00800000, 0x7f7fffff, 0xff7fffff, 0x7f800000, 0xff800000, 0x7fc12345, 0x7f800001, 0x3f800000 };
        float[] floats = Enumerable.Range(0, 128 * 256).Select(i => BitConverter.Int32BitsToSingle((int)bits[(i / 32) % bits.Length])).ToArray();
        ushort[] halves = new ushort[floats.Length];
        PixelConversion.FloatToHalf(floats, halves);
        byte[] bytes = type == PixelType.Half ? MemoryMarshal.AsBytes(halves.AsSpan()).ToArray() : MemoryMarshal.AsBytes(floats.AsSpan()).ToArray();
        Part part = new(header, new[] { new FlatLevel(0, 0, header.DataWindow,
            header.Channels.Select(c => new ChannelBuffer(c.Name, type, bytes))) }, true);
        await VerifyRoundTripAsync(new Image(new[] { part }), compression + " RGB RCT " + type);
    }

    [TestMethod]
    [DataRow(Compression.B44, false)]
    [DataRow(Compression.B44, true)]
    [DataRow(Compression.B44A, false)]
    [DataRow(Compression.B44A, true)]
    public async Task B44QuantizationMatchesForAllHalfInputs(Compression compression, bool linear)
    {
        Header header = new(PartType.Scanline, new Box2i(0, 0, 1023, 63),
            new[] { new Channel("H", PixelType.Half, perceptuallyLinear: linear) }, compression);
        byte[] halves = MemoryMarshal.AsBytes(Enumerable.Range(0, 65536).Select(i => (ushort)i).ToArray().AsSpan()).ToArray();
        Part part = new(header, new[] { new FlatLevel(0, 0, header.DataWindow, new[] { new ChannelBuffer("H", PixelType.Half, halves) }) }, true);
        await VerifyRoundTripAsync(new Image(new[] { part }), compression + " all HALF inputs linear=" + linear);
    }

    [TestMethod]
    [DataRow(Compression.DWAA)]
    [DataRow(Compression.DWAB)]
    public async Task DwaRawFallbackMatchesOfficialReader(Compression compression)
    {
        Header header = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel("F", PixelType.Float) });
        Part part = new(header, new[] { new FlatLevel(0, 0, header.DataWindow,
            new[] { new ChannelBuffer("F", PixelType.Float, new byte[4]) }) }, true);
        WriterResult<byte[]> saved = ExrFile.SaveToMemory(new Image(new[] { part }));
        Assert.IsTrue(saved.IsSuccess, saved.Error?.ToString());
        byte[] bytes = saved.Value!;
        byte[] marker = Encoding.ASCII.GetBytes("compression\0compression\0");
        bytes[bytes.AsSpan().IndexOf(marker) + marker.Length + 4] = (byte)compression;
        string path = Path.Combine(Work, "dwa-raw.exr"), dump = Path.Combine(Work, "dwa-raw.bin");
        await File.WriteAllBytesAsync(path, bytes);
        await OracleAsync("file_oracle", "dump", path, dump);
        Equal(DumpManaged(path), File.ReadAllBytes(dump), compression + " official raw fallback");
    }

    [TestMethod]
    [DynamicData(nameof(Fixtures))]
    public async Task RetainedFixtureMatchesOfficialInterpretation(string relative)
    {
        string path = Path.Combine(RepoRoot, "TinyEXR.Native", "tinyexr", relative), dump = Path.Combine(Work, "fixture.bin");
        await OracleAsync("file_oracle", "dump", path, dump);
        Equal(DumpManaged(path), File.ReadAllBytes(dump), "official interpretation of " + relative);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "B44 exp table: all 65536 HALF inputs")]
    [DataRow(true, DisplayName = "B44 log table: all 65536 HALF inputs")]
    public async Task B44PerceptualTableMatchesOfficialValues(bool logarithm)
    {
        string path = Path.Combine(Work, "tables.bin");
        await OracleAsync("file_oracle", "tables", path);
        byte[] official = await File.ReadAllBytesAsync(path);
        Assert.AreEqual(2 * 65536 * sizeof(ushort), official.Length);
        Equal(official.AsSpan(logarithm ? 65536 * sizeof(ushort) : 0, 65536 * sizeof(ushort)),
            MemoryMarshal.AsBytes((logarithm ? B44Tables.Log : B44Tables.Exp).AsSpan()), logarithm ? "B44 log" : "B44 exp");
    }

    [TestMethod]
    public async Task OversizedHtjTileIsRejectedByBothReaders()
    {
        string path = Path.Combine(RepoRoot, "TinyEXR.Native", "tinyexr", "test/unit/regression/tiled_htj2k256.exr");
        Assert.AreEqual(ExrResult.Corrupt, ExrFile.LoadFromFile(path).Status);
        NativeOutput output = await ExecuteOracleAsync("file_oracle", "dump", path, Path.Combine(Work, "rejected.bin"));
        Assert.AreNotEqual(0, output.ExitCode, output.StandardOutput);
        StringAssert.Contains(output.StandardError, "invalid packed size");
    }

    private async Task VerifyRoundTripAsync(Image image, string label)
    {
        string managed = Path.Combine(Work, "managed.exr"), native = Path.Combine(Work, "native.exr"), dump = Path.Combine(Work, "dump.bin");
        WriterResult result = ExrFile.SaveToFile(image, managed);
        Assert.AreEqual(ExrResult.Success, result.Status, label + " managed encode: " + result.Error);
        await OracleAsync("file_oracle", "dump", managed, dump);
        Equal(DumpManaged(managed), File.ReadAllBytes(dump), label + " managed encode -> native decode (all levels and samples)");
        ReaderResult<Image> original = ExrFile.LoadFromFile(managed);
        Assert.IsTrue(original.IsSuccess, label + " materialize managed file");
        bool lossless = image.Parts[0].Header.Compression is not (Compression.PXR24 or Compression.B44 or Compression.B44A);
        if (lossless)
            EqualImages(image, original.Value!, label + " managed");
        await OracleAsync("file_oracle", "reencode", managed, native);
        await OracleAsync("file_oracle", "dump", native, dump);
        Equal(DumpManaged(native), File.ReadAllBytes(dump), label + " native encode -> managed decode (all levels and samples)");
        ReaderResult<Image> roundtrip = ExrFile.LoadFromFile(native);
        Assert.IsTrue(roundtrip.IsSuccess, label + " materialize native file");
        if (lossless)
            EqualImages(image, roundtrip.Value!, label + " native");
    }

    private static byte[] DumpManaged(string path)
    {
        ReaderResult<Image> result = ExrFile.LoadFromFile(path);
        Assert.IsTrue(result.IsSuccess, path + " materialize: " + result.Error);
        using MemoryStream output = new();
        using BinaryWriter writer = new(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(result.Value!.Parts.Count);
        foreach (Part part in result.Value.Parts)
        {
            Header h = part.Header;
            writer.Write((int)h.PartType);
            using (ExrReader metadata = ExrReader.OpenMemory(File.ReadAllBytes(path)))
            {
                Assert.IsTrue(metadata.ParseHeader().IsSuccess, "metadata");
                HeaderAttribute[] attributes = metadata.GetRawHeaderAttributes(result.Value.Parts.ToList().IndexOf(part))
                    .Where(a => a.Name is not ("channels" or "type" or "chunkCount" or "version" or "maxSamplesPerPixel"))
                    .OrderBy(a => a.Name, ModelValidation.Utf8ByteComparer).ToArray();
                writer.Write(attributes.Length);
                foreach (HeaderAttribute attribute in attributes)
                {
                    foreach (string text in new[] { attribute.Name, attribute.TypeName })
                    {
                        byte[] name = Encoding.UTF8.GetBytes(text);
                        writer.Write(name.Length); writer.Write(name);
                    }
                    writer.Write(attribute.ByteLength); writer.Write(attribute.Data);
                }
            }
            writer.Write(h.Channels.Count);
            foreach (Channel channel in h.Channels)
            {
                byte[] name = Encoding.UTF8.GetBytes(channel.Name);
                writer.Write(name.Length);
                writer.Write(name);
                writer.Write((int)channel.PixelType);
                writer.Write(channel.XSampling);
                writer.Write(channel.YSampling);
                writer.Write(channel.PerceptuallyLinear ? 1 : 0);
            }
            writer.Write(part.Levels.Count);
            foreach (PartLevel level in part.Levels.OrderBy(l => l.LevelY).ThenBy(l => l.LevelX))
            {
                writer.Write(level.LevelX);
                writer.Write(level.LevelY);
                writer.Write(level.Region.MinX);
                writer.Write(level.Region.MinY);
                writer.Write((int)level.Region.Width);
                writer.Write((int)level.Region.Height);
                writer.Write(level is DeepLevel ? 1 : 0);
                if (level is DeepLevel deep)
                {
                    writer.Write(deep.SampleCounts.Length);
                    foreach (int count in deep.SampleCounts) writer.Write(count);
                }
                foreach (Channel channel in h.Channels)
                {
                    ReadOnlySpan<byte> data = level.GetChannel(channel.Name).Data;
                    writer.Write((ulong)data.Length);
                    writer.Write(data);
                }
            }
        }
        return output.ToArray();
    }

    private static Part CreatePart(PartType type, Compression compression, TileLevelMode mode, string name, bool sampled = false, uint tileRows = 4, int scanlineRows = 32)
    {
        bool tiled = type is PartType.Tiled or PartType.DeepTiled, deep = type is PartType.DeepScanline or PartType.DeepTiled;
        int width = tiled ? 17 : 128, height = tiled ? 9 : scanlineRows;
        Header header = new(type, new Box2i(-4, -2, width - 5, height - 3),
            new[] { new Channel("F", PixelType.Float), new Channel("H", PixelType.Half, sampled ? 2 : 1, sampled ? 2 : 1), new Channel("U", PixelType.UInt) },
            compression, displayWindow: new Box2i(-4, -2, 123, 29), tiles: tiled ? new TileDescription(8, tileRows, mode, TileRoundingMode.RoundUp) : null, name: name);
        header = header.WithIntAttribute("customFrame", 17)
            .WithFloatAttribute("customFloat", BitConverter.Int32BitsToSingle(0x7fc12345))
            .WithPreviewImage(new PreviewImage(2, 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        int xs = !tiled || mode == TileLevelMode.OneLevel ? 1 : ExrFormatParser.LevelCount(width, TileRoundingMode.RoundUp);
        int ys = !tiled || mode == TileLevelMode.OneLevel ? 1 : ExrFormatParser.LevelCount(height, TileRoundingMode.RoundUp);
        List<PartLevel> levels = new();
        for (int y = 0; y < (mode == TileLevelMode.MipmapLevels ? Math.Max(xs, ys) : ys); y++)
            for (int x = 0; x < (mode == TileLevelMode.MipmapLevels ? 1 : xs); x++)
            {
                int lx = mode == TileLevelMode.MipmapLevels ? y : x;
                int w = (int)ExrFormatParser.LevelSize(width, lx, TileRoundingMode.RoundUp), h = (int)ExrFormatParser.LevelSize(height, y, TileRoundingMode.RoundUp);
                Box2i region = new(-4, -2, w - 5, h - 3);
                int[] counts = Enumerable.Range(0, w * h).Select(i => i % 4).ToArray();
                List<ChannelBuffer> buffers = new();
                foreach (Channel channel in header.Channels)
                {
                    int n = deep ? counts.Sum() : (int)(ModelValidation.CountSampleLocations(region.MinX, region.MaxX, channel.XSampling) * ModelValidation.CountSampleLocations(region.MinY, region.MaxY, channel.YSampling));
                    float[] values = Enumerable.Range(0, n).Select(i => (i % 13) * 0.25f + lx + y).ToArray();
                    byte[] data;
                    if (channel.PixelType == PixelType.Half)
                    {
                        ushort[] half = new ushort[n];
                        PixelConversion.FloatToHalf(values, half);
                        data = MemoryMarshal.AsBytes(half.AsSpan()).ToArray();
                    }
                    else if (channel.PixelType == PixelType.Float)
                        data = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
                    else
                        data = MemoryMarshal.AsBytes(Enumerable.Range(0, n).Select(i => (uint)i * 100003U).ToArray().AsSpan()).ToArray();
                    buffers.Add(new ChannelBuffer(channel.Name, channel.PixelType, data));
                }
                levels.Add(deep ? new DeepLevel(lx, y, region, counts, buffers) : new FlatLevel(lx, y, region, buffers));
            }
        return new Part(header, levels, isComplete: true);
    }

    private static void EqualImages(Image expected, Image actual, string label)
    {
        Assert.AreEqual(expected.Parts.Count, actual.Parts.Count, label + " parts");
        for (int p = 0; p < expected.Parts.Count; p++)
        {
            Part a = expected.Parts[p], b = actual.Parts[p];
            Assert.AreEqual(a.Levels.Count, b.Levels.Count, label + " levels");
            foreach (PartLevel level in a.Levels)
            {
                PartLevel other = b.GetLevel(level.LevelX, level.LevelY);
                Assert.IsTrue(level.Region.Equals(other.Region), label + " region");
                if (level is DeepLevel deep)
                    Equal(MemoryMarshal.AsBytes(deep.SampleCounts).ToArray(), MemoryMarshal.AsBytes(((DeepLevel)other).SampleCounts).ToArray(), label + " counts");
                foreach (ChannelBuffer channel in level.Channels)
                    Equal(channel.Data.ToArray(), other.GetChannel(channel.Name).Data.ToArray(), label + $" {channel.Name} level {level.LevelX},{level.LevelY} samples");
            }
        }
    }

}
