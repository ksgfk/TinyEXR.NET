using BenchmarkDotNet.Running;

if (args.Length == 1 && string.Equals(
    args[0],
    "--prepare-v3-compression-fixtures",
    StringComparison.Ordinal))
{
    TinyEXR.Benchmark.V3CompressionBenchmarkData.Instance.PrintFixtureSummary();
    return;
}

if (args.Length == 5 && args[0] == "--summarize-comparison")
{
    TinyEXR.Benchmark.ComparisonReport.Write(args[1], args[2], args[3], args[4]);
    return;
}

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
    .Run(args, new TinyEXR.Benchmark.BenchmarkBuildConfig()).ToArray();
if (summaries.Any(summary => summary.HasCriticalValidationErrors ||
    summary.Reports.Any(report => !report.Success || report.ResultStatistics == null)))
{
    Console.Error.WriteLine("Managed benchmark failed to build or measure; see the BenchmarkDotNet log above.");
    Environment.ExitCode = 1;
}
