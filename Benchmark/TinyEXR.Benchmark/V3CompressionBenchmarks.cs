using System.Buffers.Binary;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using V3 = TinyEXR.V3;

namespace TinyEXR.Benchmark;

[MemoryDiagnoser]
[Config(typeof(V3CompressionBenchmarkConfig))]
public class V3CompressionBenchmarks
{
    private V3.Image _source = null!;
    private byte[] _encoded = null!;

    [ParamsSource(nameof(Compressions))]
    public V3.Compression Compression { get; set; }

    public IEnumerable<V3.Compression> Compressions => V3CompressionBenchmarkData.SelectedCompressions;

    [GlobalSetup]
    public void Setup()
    {
        V3CompressionBenchmarkData data = V3CompressionBenchmarkData.Instance;
        _source = data.Source;
        _encoded = data.GetEncoded(Compression);
    }

    [Benchmark]
    public byte[] Encode()
    {
        return V3.ExrFile.SaveToMemory(_source, Compression).Value!;
    }

    [Benchmark]
    public V3.Image Decode()
    {
        return V3.ExrFile.LoadFromMemory(_encoded).Value!;
    }
}

public sealed class V3CompressionBenchmarkConfig : ManualConfig
{
    public V3CompressionBenchmarkConfig()
    {
        AddColumn(
            V3RawThroughputColumn.Instance,
            V3EncodedSizeColumn.Instance,
            V3CompressionRatioColumn.Instance);
    }
}

internal sealed class V3CompressionBenchmarkData
{
    public const int Width = 1920;
    public const int Height = 1080;
    public const int ChannelCount = 4;
    public const int BytesPerSample = 2;
    public const int RawByteCount = Width * Height * ChannelCount * BytesPerSample;

    public static readonly V3.Compression[] Compressions =
    [
        V3.Compression.None,
        V3.Compression.RLE,
        V3.Compression.ZIPS,
        V3.Compression.ZIP,
        V3.Compression.PIZ,
        V3.Compression.PXR24,
        V3.Compression.B44,
        V3.Compression.B44A,
        V3.Compression.HTJ2K256,
        V3.Compression.HTJ2K32,
        V3.Compression.ZSTD,
    ];

    public static IReadOnlyList<V3.Compression> SelectedCompressions
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable("TINYEXR_BENCHMARK_COMPRESSION");
            if (string.IsNullOrWhiteSpace(value))
            {
                return Compressions;
            }

            if (!Enum.TryParse(value, ignoreCase: true, out V3.Compression compression) ||
                !Compressions.Contains(compression))
            {
                throw new InvalidOperationException(
                    $"TINYEXR_BENCHMARK_COMPRESSION must name a benchmarked compression method, not '{value}'.");
            }

            return [compression];
        }
    }

    public static V3CompressionBenchmarkData Instance { get; } = new();

    private readonly Dictionary<V3.Compression, byte[]> _encoded;
    private bool _fixturesWritten;

    private V3CompressionBenchmarkData()
    {
        Source = CreateSource();
        IReadOnlyList<V3.Compression> compressions = SelectedCompressions;
        _encoded = new Dictionary<V3.Compression, byte[]>(compressions.Count);
        foreach (V3.Compression compression in compressions)
        {
            V3.WriterResult<byte[]> saved = V3.ExrFile.SaveToMemory(Source, compression);
            if (!saved.IsSuccess || saved.Value == null)
            {
                throw new InvalidOperationException(
                    $"Unable to prepare V3 {compression} benchmark input: {saved.Status}: {saved.Error}");
            }

            V3.ReaderResult<V3.Image> loaded = V3.ExrFile.LoadFromMemory(saved.Value);
            if (!loaded.IsSuccess || loaded.Value == null)
            {
                throw new InvalidOperationException(
                    $"Unable to validate V3 {compression} benchmark input: {loaded.Status}: {loaded.Error}");
            }

            V3.Part part = loaded.Value.Parts[0];
            if (part.Width != Width || part.Height != Height ||
                part.Levels.Count != 1 || part.Levels[0].Channels.Count != ChannelCount)
            {
                throw new InvalidDataException($"V3 {compression} benchmark input has unexpected geometry.");
            }

            _encoded.Add(compression, saved.Value);
        }
    }

    public V3.Image Source { get; }

    public byte[] GetEncoded(V3.Compression compression)
    {
        return _encoded.TryGetValue(compression, out byte[]? bytes)
            ? bytes
            : throw new ArgumentOutOfRangeException(nameof(compression), compression, "Compression is not benchmarked.");
    }

    public void WriteFixtures()
    {
        if (_fixturesWritten)
        {
            return;
        }

        string directory = FixtureDirectory;
        Directory.CreateDirectory(directory);
        foreach ((V3.Compression compression, byte[] bytes) in _encoded)
        {
            string path = GetFixturePath(compression);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                File.WriteAllBytes(path, bytes);
            }
        }

        _fixturesWritten = true;
    }

    public void PrintFixtureSummary()
    {
        WriteFixtures();
        Console.WriteLine($"V3 compression fixtures: {FixtureDirectory}");
        Console.WriteLine($"Raw payload: {RawByteCount.ToString(CultureInfo.InvariantCulture)} bytes");
        foreach (V3.Compression compression in SelectedCompressions)
        {
            int encodedBytes = GetEncoded(compression).Length;
            double ratio = (double)RawByteCount / encodedBytes;
            Console.WriteLine(
                $"{compression,-10} {encodedBytes,10:N0} bytes  {ratio,6:F2}x");
        }
    }

    public static string FixtureDirectory => Path.Combine(
        FindRepoRoot(),
        ".cache",
        "compression-benchmarks",
        "v3-managed");

    public static string GetFixturePath(V3.Compression compression)
    {
        return Path.Combine(FixtureDirectory, $"{compression.ToString().ToLowerInvariant()}.exr");
    }

    private static V3.Image CreateSource()
    {
        V3.Box2i region = new(0, 0, Width - 1, Height - 1);
        string[] channelNames = ["A", "B", "G", "R"];
        V3.Channel[] descriptions = channelNames
            .Select(static name => new V3.Channel(name, V3.PixelType.Half))
            .ToArray();
        V3.ChannelBuffer[] buffers = channelNames
            .Select(name => new V3.ChannelBuffer(name, V3.PixelType.Half, CreateChannel(name)))
            .ToArray();
        V3.Header header = new(V3.PartType.Scanline, region, descriptions);
        V3.FlatLevel level = new(0, 0, region, buffers);
        return new V3.Image(new[] { new V3.Part(header, new[] { level }, isComplete: true) });
    }

    private static byte[] CreateChannel(string name)
    {
        byte[] data = new byte[Width * Height * BytesPerSample];
        int offset = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                ushort bits = name switch
                {
                    "A" => 0x3C00,
                    "B" => (ushort)(0x3000 | (((x >> 2) + (3 * (y >> 2))) & 0x03FF)),
                    "G" => (ushort)(0x3400 | ((y * 0x03FF) / (Height - 1))),
                    "R" => (ushort)(0x3800 | ((x * 0x03FF) / (Width - 1))),
                    _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown benchmark channel."),
                };
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, BytesPerSample), bits);
                offset += BytesPerSample;
            }
        }

        return data;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TinyEXR.NET.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Unable to locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}

internal static class V3CompressionColumnData
{
    public static V3.Compression GetCompression(BenchmarkCase benchmarkCase)
    {
        object? value = benchmarkCase.Parameters.Items
            .Single(item => string.Equals(item.Name, nameof(V3CompressionBenchmarks.Compression), StringComparison.Ordinal))
            .Value;
        return value is V3.Compression compression
            ? compression
            : throw new InvalidOperationException("The compression benchmark parameter is unavailable.");
    }

    public static long? GetEncodedByteCount(BenchmarkCase benchmarkCase)
    {
        return V3CompressionBenchmarkData.Instance
            .GetEncoded(GetCompression(benchmarkCase))
            .LongLength;
    }
}

internal sealed class V3RawThroughputColumn : IColumn
{
    public static V3RawThroughputColumn Instance { get; } = new();

    public string Id => nameof(V3RawThroughputColumn);
    public string ColumnName => "MiB/s";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Uncompressed image payload processed per second.";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        return GetValue(summary, benchmarkCase, summary.Style);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        BenchmarkReport? report = summary[benchmarkCase];
        double? meanNanoseconds = report?.ResultStatistics?.Mean;
        if (!meanNanoseconds.HasValue || meanNanoseconds.Value <= 0.0)
        {
            return "-";
        }

        double mibPerSecond = V3CompressionBenchmarkData.RawByteCount * 1_000_000_000.0 /
            meanNanoseconds.Value /
            (1024.0 * 1024.0);
        return mibPerSecond.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public bool IsAvailable(Summary summary) => true;
}

internal sealed class V3EncodedSizeColumn : IColumn
{
    public static V3EncodedSizeColumn Instance { get; } = new();

    public string Id => nameof(V3EncodedSizeColumn);
    public string ColumnName => "Encoded MiB";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 1;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Encoded EXR size produced by TinyEXR.NET V3.";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        return GetValue(summary, benchmarkCase, summary.Style);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        long? bytes = V3CompressionColumnData.GetEncodedByteCount(benchmarkCase);
        return bytes.HasValue
            ? (bytes.Value / (1024.0 * 1024.0)).ToString("0.000", CultureInfo.InvariantCulture)
            : "-";
    }

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public bool IsAvailable(Summary summary) => true;
}

internal sealed class V3CompressionRatioColumn : IColumn
{
    public static V3CompressionRatioColumn Instance { get; } = new();

    public string Id => nameof(V3CompressionRatioColumn);
    public string ColumnName => "Ratio";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 2;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Uncompressed payload bytes divided by encoded EXR bytes.";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        return GetValue(summary, benchmarkCase, summary.Style);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        long? bytes = V3CompressionColumnData.GetEncodedByteCount(benchmarkCase);
        return bytes.HasValue && bytes.Value != 0
            ? ((double)V3CompressionBenchmarkData.RawByteCount / bytes.Value)
                .ToString("0.00", CultureInfo.InvariantCulture)
            : "-";
    }

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public bool IsAvailable(Summary summary) => true;
}
