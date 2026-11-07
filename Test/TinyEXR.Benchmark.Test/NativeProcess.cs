using System.Diagnostics;

namespace TinyEXR.Benchmark.Test;

internal sealed record NativeResult(int ExitCode, string Output, string Error);

internal static class NativeProcess
{
    internal static async Task<NativeResult> Execute(string executable, IEnumerable<string> arguments,
        string log, TimeSpan? limit = null)
    {
        if (!File.Exists(executable))
            throw new FileNotFoundException("Build Benchmark/baseline and prepare official fixtures before testing.", executable);
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(limit ?? TimeSpan.FromSeconds(60));
        bool expired = false;
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            expired = true;
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
        }
        string output = await stdout, error = await stderr;
        await File.WriteAllTextAsync(log, executable + "\n" + string.Join(" ", arguments) + "\n" + output + "\n" + error);
        if (expired) throw new TimeoutException("Native process exceeded " + (limit ?? TimeSpan.FromSeconds(60)) + "; " + log);
        return new(process.ExitCode, output, error);
    }

    internal static void RequireSuccess(NativeResult result) =>
        Assert.AreEqual(0, result.ExitCode, "Native process failed: " + result.Error);
}
