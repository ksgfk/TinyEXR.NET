using BenchmarkDotNet.Running;

if (args.Length == 1 && string.Equals(
    args[0],
    "--prepare-v3-compression-fixtures",
    StringComparison.Ordinal))
{
    TinyEXR.Benchmark.V3CompressionBenchmarkData.Instance.PrintFixtureSummary();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
