using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace TinyEXR.Benchmark;

public sealed class BenchmarkBuildConfig : ManualConfig
{
    public BenchmarkBuildConfig()
    {
        Add(DefaultConfig.Instance);
        // The generated harness rebuilds project references in a separate MSBuild
        // process. The outer dotnet build's properties are not inherited.
        // A mutator also applies to jobs selected with --job or benchmark attributes.
        AddJob(Job.Default.WithArguments([new MsBuildArgument("/p:SignAssemblyKey=false")]).AsMutator());
        WithOptions(ConfigOptions.StopOnFirstError);
    }
}
