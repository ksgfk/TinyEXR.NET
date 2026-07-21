using System.Diagnostics;
using BenchmarkDotNet.Running;
using V3 = TinyEXR.V3;

if (args.Length == 1 && string.Equals(
    args[0],
    "--prepare-v3-compression-fixtures",
    StringComparison.Ordinal))
{
    TinyEXR.Benchmark.V3CompressionBenchmarkData.Instance.PrintFixtureSummary();
    return;
}

if (args.Length >= 3 && string.Equals(
    args[0],
    "--profile-v3-compression",
    StringComparison.Ordinal))
{
    if (!Enum.TryParse(args[2], ignoreCase: true, out V3.Compression compression))
    {
        throw new ArgumentException($"Unknown compression method '{args[2]}'.", nameof(args));
    }

    int iterationCount = args.Length >= 4 ? int.Parse(args[3]) : 1;
    if (iterationCount < 1)
    {
        throw new ArgumentOutOfRangeException(nameof(args), "The iteration count must be positive.");
    }

    Environment.SetEnvironmentVariable("TINYEXR_BENCHMARK_COMPRESSION", compression.ToString());
    TinyEXR.Benchmark.V3CompressionBenchmarks benchmark = new()
    {
        Compression = compression,
    };
    benchmark.Setup();

    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    long startedAt = Stopwatch.GetTimestamp();
    object? result = null;
    for (int iteration = 0; iteration < iterationCount; iteration++)
    {
        result = string.Equals(args[1], "encode", StringComparison.OrdinalIgnoreCase)
            ? benchmark.Encode()
            : string.Equals(args[1], "decode", StringComparison.OrdinalIgnoreCase)
                ? benchmark.Decode()
                : throw new ArgumentException($"Unknown operation '{args[1]}'.", nameof(args));
    }

    TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    int resultValue = result switch
    {
        byte[] encoded => encoded.Length,
        V3.Image image => checked((int)(
            image.Parts[0].Width +
            image.Parts[0].Height +
            image.Parts[0].Levels[0].Channels.Count)),
        _ => 0,
    };
    Console.WriteLine(
        $"{compression} {args[1]}: {iterationCount} iteration(s), " +
        $"{elapsed.TotalMilliseconds:F2} ms, {allocated / (1024.0 * 1024.0):F2} MiB allocated, result {resultValue}.");
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
