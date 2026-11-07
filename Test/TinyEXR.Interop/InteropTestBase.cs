using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace TinyEXR.Interop;

public abstract class InteropTestBase
{
    public TestContext TestContext { get; set; } = null!;
    protected string Work { get; private set; } = null!;
    internal static string RepoRoot { get; } = FindRepoRoot();
    private int _invocation;

    [TestInitialize]
    public void InitializeWorkspace()
    {
        Work = Path.Combine(TestContext.TestRunDirectory!, "interop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Work);
        TestContext.WriteLine($"Managed SIMD: {SimdRuntime.Info}; artifacts: {Work}");
    }

    [TestCleanup]
    public void CleanupWorkspace()
    {
        if (TestContext.CurrentTestOutcome == UnitTestOutcome.Passed)
        {
            Directory.Delete(Work, recursive: true);
        }
        else
        {
            foreach (string path in Directory.EnumerateFiles(Work, "*", SearchOption.AllDirectories))
                TestContext.AddResultFile(path);
        }
    }

    private protected async Task<NativeOutput> ExecuteOracleAsync(string tool, params string[] arguments)
    {
        string? configured = Environment.GetEnvironmentVariable("TINYEXR_OPENEXR_REFERENCE_DIR");
        string directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(RepoRoot, ".cache", "openexr-reference")
            : Path.GetFullPath(configured, RepoRoot);
        string executable = ResolveOracle(directory, tool);
        string log = Path.Combine(Work, $"{++_invocation:D2}-{tool}.log");
        return await NativeProcess.ExecuteAsync(executable, arguments, Work, log, TimeSpan.FromSeconds(60));
    }

    protected async Task OracleAsync(string tool, params string[] arguments)
    {
        NativeOutput output = await ExecuteOracleAsync(tool, arguments);
        Assert.AreEqual(0, output.ExitCode, $"{tool}: {output.StandardOutput}\n{output.StandardError}");
    }

    internal static string ResolveOracle(string directory, string tool)
    {
        string name = tool + (OperatingSystem.IsWindows() ? ".exe" : "");
        string path = Path.Combine(directory, name);
        if (!File.Exists(path)) path = Path.Combine(directory, "Release", name);
        Assert.IsTrue(File.Exists(path), $"Missing official OpenEXR tool '{tool}' in '{directory}'. " +
            "Build Test/NativeReference first, or set TINYEXR_OPENEXR_REFERENCE_DIR. Tests are not skipped.");
        return path;
    }

    protected static void Equal(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string label)
    {
        if (expected.SequenceEqual(actual)) return;
        int offset = 0;
        while (offset < Math.Min(expected.Length, actual.Length) && expected[offset] == actual[offset]) offset++;
        Assert.Fail($"{label}: mismatch at byte {offset}; lengths {expected.Length}/{actual.Length}");
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TinyEXR.NET.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot locate TinyEXR.NET.sln from the test application directory.");
    }
}

internal sealed record NativeOutput(int ExitCode, string StandardOutput, string StandardError);

internal static class NativeProcess
{
    internal static async Task<NativeOutput> ExecuteAsync(
        string executable, IReadOnlyList<string> arguments, string work, string log, TimeSpan timeout)
    {
        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = work,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        await File.WriteAllTextAsync(log, $"{executable}\nArguments: {string.Join(" | ", arguments)}\n");
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource cancellation = new(timeout);
        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* The process exited between timeout and kill. */ }
            await process.WaitForExitAsync();
        }
        string[] text = await Task.WhenAll(stdout, stderr);
        await File.AppendAllTextAsync(log, $"Exit: {process.ExitCode}; timed out: {timedOut}\nstdout:\n{text[0]}\nstderr:\n{text[1]}");
        Assert.IsFalse(timedOut, $"Native tool exceeded {timeout.TotalSeconds} seconds: {executable}. See {log}");
        return new NativeOutput(process.ExitCode, text[0], text[1]);
    }
}
