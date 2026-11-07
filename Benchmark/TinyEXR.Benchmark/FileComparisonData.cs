using System.Globalization;

namespace TinyEXR.Benchmark;

/// <summary>Preparation and verification shared by whole-file benchmarks and MSTest.</summary>
public static class FileComparisonData
{
    public const int Width = 1920, Height = 1080, RawBytes = Width * Height * 4 * 2;
    public const string OpenExrCommit = "3950290530c4fd4776667d0dc4d65a9d003eb936";
    public static readonly Compression[] SharedCompressions =
        [Compression.None, Compression.RLE, Compression.ZIPS, Compression.ZIP, Compression.PIZ,
         Compression.PXR24, Compression.B44, Compression.B44A, Compression.HTJ2K256, Compression.HTJ2K32];
    public static string RepoRoot
    {
        get
        {
            for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "TinyEXR.NET.sln"))) return d.FullName;
            throw new DirectoryNotFoundException("Cannot locate TinyEXR.NET.sln.");
        }
    }
    public static string Root => Path.Combine(RepoRoot, ".cache", "compression-benchmarks", "official");
    public static Image CreateSource() => V3CompressionBenchmarkData.CreateSource();

    public static byte[] LoadVerified(Compression compression, bool raw = false, string? root = null)
    {
        root ??= Root;
        string[] lines = File.ReadAllLines(Path.Combine(root, "manifest.csv"));
        const string header = "Compression,File,EncodedBytes,Fnv1a64,RawFile,RawBytes,RawFnv1a64,OpenEXRCommit,Width,Height,Layout";
        if (lines.Length != 13 || lines[0] != header) throw new InvalidDataException("Incomplete official fixture manifest.");
        Dictionary<Compression, string[]> rows = [];
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split(',');
            if (cells.Length != 11 || !Enum.TryParse(cells[0], out Compression c) ||
                (int)c < 0 || (int)c > 11 || !rows.TryAdd(c, cells) ||
                cells[7] != OpenExrCommit || cells[8] != "1920" || cells[9] != "1080" || cells[10] != "ABGR_HALF" ||
                cells[1] != c + ".exr" || cells[4] != c + ".raw" || cells[5] != RawBytes.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("Fixture version, layout or key mismatch.");
        }
        if (!rows.TryGetValue(compression, out string[]? selected)) throw new InvalidDataException("Missing fixture.");
        int index = raw ? 4 : 1;
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, selected[index]));
        if (bytes.LongLength != long.Parse(selected[index + 1], CultureInfo.InvariantCulture) ||
            Checksum(bytes) != selected[index + 2])
            throw new InvalidDataException("Fixture checksum mismatch.");
        return bytes;
    }

    public static string Checksum(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in bytes) hash = unchecked((hash ^ b) * 1099511628211UL);
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    public static byte[] Canonical(Image image)
    {
        if (image.Parts.Count != 1) throw new InvalidDataException("Expected one part.");
        Part part = image.Parts[0];
        if (!part.IsComplete || part.Width != Width || part.Height != Height || part.Levels.Count != 1 ||
            part.Levels[0].Channels.Count != 4 || part.Header.PartType != PartType.Scanline)
            throw new InvalidDataException("Unexpected image geometry or incomplete materialization.");
        string[] names = ["A", "B", "G", "R"];
        byte[] bytes = new byte[RawBytes];
        for (int c = 0; c < 4; c++)
        {
            var channel = part.Levels[0].Channels.Single(p => p.Name == names[c]);
            if (channel.PixelType != PixelType.Half || channel.Data.Length != Width * Height * 2)
                throw new InvalidDataException("Unexpected pixel type or sample count.");
            for (int y = 0; y < Height; y++)
                channel.Data.Slice(y * Width * 2, Width * 2).CopyTo(bytes.AsSpan((y * 4 + c) * Width * 2));
        }
        return bytes;
    }
}
