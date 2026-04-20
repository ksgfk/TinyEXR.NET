using System.Globalization;

namespace TinyEXR.Benchmark;

internal sealed record SampleEntry(string Id, string Kind, string RelativePath);

internal sealed record SampleBuffer(string Id, string Kind, string FullPath, byte[] Bytes);

internal sealed record RgbaPrepared(float[] Rgba, int Width, int Height, int Components, bool AsFp16);

internal sealed record SinglePartPrepared(ExrVersion Version, ExrHeader Header, ExrImage Image);

internal sealed record MultipartPrepared(ExrVersion Version, ExrMultipartHeader Headers, ExrMultipartImage Images);

internal static class BenchmarkSamples
{
    public static readonly string[] SinglePartSampleIds =
    [
        "desk_scanline",
        "kapaa_multires",
    ];

    public static readonly string[] MultipartSampleIds =
    [
        "beachball_multipart_0001",
    ];

    public static readonly string[] DeepSampleIds =
    [
        "balls_deep_scanline",
    ];
}

internal sealed class SampleRepository
{
    public static SampleRepository Instance { get; } = new();

    private readonly Dictionary<string, SampleBuffer> _buffers;
    private readonly Dictionary<string, RgbaPrepared> _rgbaPrepared;
    private readonly Dictionary<string, SinglePartPrepared> _singlePartPrepared;
    private readonly Dictionary<string, MultipartPrepared> _multipartPrepared;

    private SampleRepository()
    {
        string repoRoot = FindRepoRoot();
        string manifestPath = Path.Combine(repoRoot, "Benchmark", "sample-manifest.csv");
        string openExrRoot = Path.Combine(repoRoot, ".cache", "openexr-images");

        if (!Directory.Exists(openExrRoot))
        {
            throw new DirectoryNotFoundException($"OpenEXR sample cache was not found: {openExrRoot}");
        }

        _buffers = new Dictionary<string, SampleBuffer>(StringComparer.Ordinal);
        foreach (SampleEntry entry in ParseManifest(manifestPath))
        {
            string fullPath = Path.Combine(openExrRoot, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Required benchmark sample was not found: {fullPath}", fullPath);
            }

            _buffers.Add(entry.Id, new SampleBuffer(entry.Id, entry.Kind, fullPath, File.ReadAllBytes(fullPath)));
        }

        _rgbaPrepared = new Dictionary<string, RgbaPrepared>(StringComparer.Ordinal)
        {
            ["desk_scanline"] = PrepareRgba(GetBuffer("desk_scanline")),
        };

        _singlePartPrepared = new Dictionary<string, SinglePartPrepared>(StringComparer.Ordinal)
        {
            ["desk_scanline"] = PrepareSinglePart(GetBuffer("desk_scanline")),
            ["kapaa_multires"] = PrepareSinglePart(GetBuffer("kapaa_multires")),
        };

        _multipartPrepared = new Dictionary<string, MultipartPrepared>(StringComparer.Ordinal)
        {
            ["beachball_multipart_0001"] = PrepareMultipart(GetBuffer("beachball_multipart_0001")),
        };
    }

    public SampleBuffer GetBuffer(string sampleId)
    {
        return _buffers.TryGetValue(sampleId, out SampleBuffer? buffer)
            ? buffer
            : throw new KeyNotFoundException($"Unknown benchmark sample id: {sampleId}");
    }

    public RgbaPrepared GetRgba(string sampleId)
    {
        return _rgbaPrepared.TryGetValue(sampleId, out RgbaPrepared? prepared)
            ? prepared
            : throw new KeyNotFoundException($"Missing RGBA benchmark input for sample: {sampleId}");
    }

    public SinglePartPrepared GetSinglePart(string sampleId)
    {
        return _singlePartPrepared.TryGetValue(sampleId, out SinglePartPrepared? prepared)
            ? prepared
            : throw new KeyNotFoundException($"Missing single-part benchmark input for sample: {sampleId}");
    }

    public MultipartPrepared GetMultipart(string sampleId)
    {
        return _multipartPrepared.TryGetValue(sampleId, out MultipartPrepared? prepared)
            ? prepared
            : throw new KeyNotFoundException($"Missing multipart benchmark input for sample: {sampleId}");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TinyEXR.NET.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "Benchmark", "sample-manifest.csv")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Unable to locate repository root from '{AppContext.BaseDirectory}'.");
    }

    private static IEnumerable<SampleEntry> ParseManifest(string manifestPath)
    {
        string[] lines = File.ReadAllLines(manifestPath);
        if (lines.Length == 0 || !string.Equals(lines[0], "sample_id,kind,relative_path", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected benchmark sample manifest header: {manifestPath}");
        }

        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(',', 3, StringSplitOptions.None);
            if (parts.Length != 3)
            {
                throw new InvalidDataException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Malformed benchmark sample manifest entry at line {0}: {1}",
                        index + 1,
                        line));
            }

            yield return new SampleEntry(parts[0], parts[1], parts[2]);
        }
    }

    private static RgbaPrepared PrepareRgba(SampleBuffer sample)
    {
        BenchmarkGuard.Success(
            Exr.LoadEXRFromMemory(sample.Bytes, out float[] rgba, out int width, out int height),
            $"LoadEXRFromMemory setup failed for {sample.Id}");

        return new RgbaPrepared(rgba, width, height, Components: 4, AsFp16: false);
    }

    private static SinglePartPrepared PrepareSinglePart(SampleBuffer sample)
    {
        BenchmarkGuard.Success(
            Exr.ParseEXRVersionFromMemory(sample.Bytes, out ExrVersion version),
            $"ParseEXRVersionFromMemory setup failed for {sample.Id}");
        BenchmarkGuard.Success(
            Exr.ParseEXRHeaderFromMemory(sample.Bytes, out _, out ExrHeader header),
            $"ParseEXRHeaderFromMemory setup failed for {sample.Id}");
        BenchmarkGuard.Success(
            Exr.LoadEXRImageFromMemory(sample.Bytes, header, out ExrImage image),
            $"LoadEXRImageFromMemory setup failed for {sample.Id}");

        return new SinglePartPrepared(version, header, image);
    }

    private static MultipartPrepared PrepareMultipart(SampleBuffer sample)
    {
        BenchmarkGuard.Success(
            Exr.ParseEXRVersionFromMemory(sample.Bytes, out ExrVersion version),
            $"ParseEXRVersionFromMemory setup failed for {sample.Id}");
        BenchmarkGuard.Success(
            Exr.ParseEXRMultipartHeaderFromMemory(sample.Bytes, out _, out ExrMultipartHeader headers),
            $"ParseEXRMultipartHeaderFromMemory setup failed for {sample.Id}");
        BenchmarkGuard.Success(
            Exr.LoadEXRMultipartImageFromMemory(sample.Bytes, headers, out ExrMultipartImage images),
            $"LoadEXRMultipartImageFromMemory setup failed for {sample.Id}");

        return new MultipartPrepared(version, headers, images);
    }
}

internal static class BenchmarkGuard
{
    public static void Success(ResultCode result, string context)
    {
        if (result != ResultCode.Success)
        {
            throw new InvalidOperationException($"{context}: {result}");
        }
    }
}
