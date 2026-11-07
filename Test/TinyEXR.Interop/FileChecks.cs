using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TinyEXR;
using TinyEXR.Codecs;
using TinyEXR.Format;

internal static class FileChecks
{
    public static void Run(string nativeDirectory, string work)
    {
        string tables = Path.Combine(work, "tables.bin");
        RunNative(nativeDirectory, "file_oracle", "tables", tables);
        byte[] expectedTables = MemoryMarshal.AsBytes(B44Tables.Exp.AsSpan()).ToArray()
            .Concat(MemoryMarshal.AsBytes(B44Tables.Log.AsSpan()).ToArray()).ToArray();
        Equal(expectedTables, File.ReadAllBytes(tables), "all 131072 B44 perceptual table entries");
        VerifyPinnedDeepWriterLimitations(nativeDirectory, work);
        int cases = 0;
        foreach (Compression compression in Enum.GetValues<Compression>().Where(c => c != Compression.DWAA && c != Compression.DWAB))
        {
            Part scanline = CreatePart(PartType.Scanline, compression, TileLevelMode.OneLevel, "scanline");
            Check(new Image(new[] { scanline }), compression + " scanline", basicHarness: true);
            Part sampled = CreatePart(PartType.Scanline, compression, TileLevelMode.OneLevel, "sampled", sampled: true);
            Check(new Image(new[] { sampled }), compression + " sampled", basicHarness: true);
            foreach (TileLevelMode mode in Enum.GetValues<TileLevelMode>())
            {
                Part tiled = CreatePart(PartType.Tiled, compression, mode, "tiled");
                Check(new Image(new[] { tiled }), compression + " " + mode, basicHarness: mode == TileLevelMode.OneLevel);
                Check(new Image(new[] { scanline, tiled }), compression + " multipart " + mode, basicHarness: false);
            }
            if (compression is Compression.None or Compression.RLE or Compression.ZIPS or Compression.ZIP or Compression.HTJ2K32 or Compression.HTJ2K256)
            {
                Part deep = CreatePart(PartType.DeepScanline, compression, TileLevelMode.OneLevel, "deep");
                bool singleRowBlocks = compression is Compression.None or Compression.RLE or Compression.ZIPS;
                Check(new Image(new[] { deep }), compression + " deep scanline", basicHarness: false, nativeEncode: singleRowBlocks);
                Part nativeDeep = singleRowBlocks ? deep : CreatePart(PartType.DeepScanline, compression, TileLevelMode.OneLevel, "deep", scanlineRows: 1);
                if (!singleRowBlocks)
                    Check(new Image(new[] { nativeDeep }), compression + " one-row deep scanline", basicHarness: false);
                foreach (TileLevelMode mode in Enum.GetValues<TileLevelMode>())
                {
                    Part tiledDeep = CreatePart(PartType.DeepTiled, compression, mode, "deep-tiled");
                    // The pinned native writer emits channel-major multi-row deep tiles;
                    // its decoder and OpenEXR require row-major channel planes. Exercise
                    // native decode at full tile height, and native encode with one-row tiles.
                    Check(new Image(new[] { tiledDeep }), compression + " deep " + mode, basicHarness: false, nativeEncode: false);
                    Part rowDeep = CreatePart(PartType.DeepTiled, compression, mode, "deep-tiled", tileRows: 1);
                    Check(new Image(new[] { rowDeep }), compression + " deep one-row tiles " + mode, basicHarness: false);
                    Check(new Image(new[] { scanline, deep, tiledDeep }), compression + " mixed multipart " + mode, basicHarness: false, nativeEncode: false);
                    Check(new Image(new[] { scanline, nativeDeep, rowDeep }), compression + " mixed multipart one-row tiles " + mode, basicHarness: false);
                }
            }
            Console.WriteLine($"{compression}: full-file, all-level and deep cross-checks passed");
        }
        Console.WriteLine($"Passed {cases} full-file cases and exhaustive B44 tables ({SimdRuntime.Info}).");

        void Check(Image image, string label, bool basicHarness, bool nativeEncode = true)
        {
            string managed = Path.Combine(work, "managed.exr"), native = Path.Combine(work, "native.exr"), dump = Path.Combine(work, "dump.bin");
            WriterResult result = ExrFile.SaveToFile(image, managed);
            Require(result.Status == ExrResult.Success, label + " managed encode: " + result.Error);
            RunNative(nativeDirectory, "file_oracle", "dump", managed, dump);
            Equal(DumpManaged(managed), File.ReadAllBytes(dump), label + " managed encode -> native decode (every block)");
            ReaderResult<Image> original = ExrFile.LoadFromFile(managed);
            Require(original.IsSuccess, label + " materialize managed file");
            bool lossless = image.Parts[0].Header.Compression is not (Compression.PXR24 or Compression.B44 or Compression.B44A);
            if (lossless)
                EqualImages(image, original.Value!, label + " managed");
            if (!nativeEncode)
            {
                cases++;
                return;
            }
            RunNative(nativeDirectory, "file_oracle", "reencode", managed, native);
            RunNative(nativeDirectory, "file_oracle", "dump", native, dump);
            Equal(DumpManaged(native), File.ReadAllBytes(dump), label + " native encode -> managed decode (every block)");
            ReaderResult<Image> roundtrip = ExrFile.LoadFromFile(native);
            Require(roundtrip.IsSuccess, label + " materialize native file");
            if (lossless)
                EqualImages(image, roundtrip.Value!, label + " native");
            if (basicHarness)
            {
                RunNative(nativeDirectory, "encode_harness", managed, native, image.Parts[0].Header.Compression.ToString().ToLowerInvariant());
                RunNative(nativeDirectory, "file_oracle", "dump", native, dump);
                Equal(DumpManaged(native), File.ReadAllBytes(dump), label + " upstream harness");
                if (lossless)
                    RunNative(nativeDirectory, "compare_exr", managed, native);
            }
            cases++;
        }
    }

    private static void VerifyPinnedDeepWriterLimitations(string nativeDirectory, string work)
    {
        // These are reproducible reference defects, not managed exceptions to data integrity.
        // Keep them executable so a future upstream upgrade must revisit the matrix boundaries.
        string input = Path.Combine(work, "deep-defect-input.exr"), output = Path.Combine(work, "deep-defect-output.exr");
        Part tiled = CreatePart(PartType.DeepTiled, Compression.None, TileLevelMode.OneLevel, "deep");
        Require(ExrFile.SaveToFile(new Image(new[] { tiled }), input).IsSuccess, "deep defect input");
        RunNative(nativeDirectory, "file_oracle", "reencode", input, output);
        ReaderResult<Image> decoded = ExrFile.LoadFromFile(output);
        Require(decoded.IsSuccess, "pinned native deep tile writer status changed");
        Require(!tiled.Levels[0].GetChannel("F").Data.SequenceEqual(decoded.Value!.Parts[0].Levels[0].GetChannel("F").Data),
            "pinned native deep tile ordering defect changed; revisit native encode matrix");
        string dump = Path.Combine(work, "deep-defect-dump.bin");
        RunNative(nativeDirectory, "file_oracle", "dump", output, dump);
        Equal(DumpManaged(output), File.ReadAllBytes(dump), "native self-roundtrip deep tile defect reproduces in both readers");
        Part scanline = CreatePart(PartType.DeepScanline, Compression.ZIP, TileLevelMode.OneLevel, "deep");
        Require(ExrFile.SaveToFile(new Image(new[] { scanline }), input).IsSuccess, "deep ZIP defect input");
        RunNative(nativeDirectory, "file_oracle", "reencode", input, output);
        Require(ExrFile.LoadFromFile(output).Status == ExrResult.Corrupt,
            "pinned native multi-row deep cumulative-count defect changed; revisit native encode matrix");
        Console.WriteLine("Confirmed pinned native multi-row deep writer defects; native encode tests use one-row deep chunks.");
    }

    private static byte[] DumpManaged(string path)
    {
        using ExrReader reader = ExrReader.OpenMemory(File.ReadAllBytes(path));
        Require(reader.ParseHeader().IsSuccess, path + " parse");
        using MemoryStream output = new();
        using BinaryWriter writer = new(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(reader.NumParts);
        for (int p = 0; p < reader.NumParts; p++)
        {
            Header h = reader.GetHeader(p);
            writer.Write((int)h.PartType);
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
            writer.Write(reader.GetNumBlocks(p));
            for (int b = 0; b < reader.GetNumBlocks(p); b++)
            {
                BlockInfo info = reader.GetBlockInfo(p, b);
                writer.Write(info.LevelX);
                writer.Write(info.LevelY);
                writer.Write(info.Region.MinX);
                writer.Write(info.Region.MinY);
                writer.Write((int)info.Region.Width);
                writer.Write((int)info.Region.Height);
                writer.Write(info.IsDeep ? 1 : 0);
                if (info.IsDeep)
                {
                    int[] counts = new int[checked((int)(info.Region.Width * info.Region.Height))];
                    Require(reader.DecodeDeepCounts(p, b, counts).IsSuccess, "deep counts");
                    writer.Write(counts.Length);
                    foreach (int count in counts)
                        writer.Write(count);
                    int total = counts.Sum();
                    DeepChannelDestination[] channels = h.Channels.Select(c => new DeepChannelDestination(c.Name, new byte[total * (c.PixelType == PixelType.Half ? 2 : 4)])).ToArray();
                    Require(reader.DecodeDeepSamples(p, b, counts, channels).IsSuccess, "deep samples");
                    foreach (DeepChannelDestination channel in channels)
                    {
                        writer.Write((ulong)channel.Data.Length);
                        writer.Write(channel.Data.Span);
                    }
                }
                else
                {
                    byte[] raw = new byte[checked((int)info.UncompressedByteCount!.Value)];
                    Require(reader.DecodeBlock(p, b, raw).IsSuccess, "flat decode");
                    writer.Write((ulong)raw.Length);
                    writer.Write(raw);
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
            compression, tiles: tiled ? new TileDescription(8, tileRows, mode, TileRoundingMode.RoundUp) : null, name: name);
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
        Require(expected.Parts.Count == actual.Parts.Count, label + " parts");
        for (int p = 0; p < expected.Parts.Count; p++)
        {
            Part a = expected.Parts[p], b = actual.Parts[p];
            Require(a.Levels.Count == b.Levels.Count, label + " levels");
            foreach (PartLevel level in a.Levels)
            {
                PartLevel other = b.GetLevel(level.LevelX, level.LevelY);
                Require(level.Region.Equals(other.Region), label + " region");
                if (level is DeepLevel deep)
                    Equal(MemoryMarshal.AsBytes(deep.SampleCounts).ToArray(), MemoryMarshal.AsBytes(((DeepLevel)other).SampleCounts).ToArray(), label + " counts");
                foreach (ChannelBuffer channel in level.Channels)
                    Equal(channel.Data.ToArray(), other.GetChannel(channel.Name).Data.ToArray(), label + $" {channel.Name} level {level.LevelX},{level.LevelY} samples");
            }
        }
    }

    private static void RunNative(string directory, string tool, params string[] args)
    {
        ProcessStartInfo start = new(Path.Combine(directory, tool + (OperatingSystem.IsWindows() ? ".exe" : "")))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (string arg in args)
            start.ArgumentList.Add(arg);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Require(process.ExitCode == 0, $"{tool} {string.Join(" ", args)}: {output} {error}");
    }

    private static void Equal(byte[] expected, byte[] actual, string label)
    {
        if (expected.AsSpan().SequenceEqual(actual))
            return;
        int i = 0;
        while (i < Math.Min(expected.Length, actual.Length) && expected[i] == actual[i])
            i++;
        throw new Exception($"{label}: mismatch at byte {i}; lengths {expected.Length}/{actual.Length}");
    }
    private static void Require(bool condition, string label)
    {
        if (!condition)
            throw new Exception(label);
    }
}
