namespace TinyEXR.Interop;

[TestClass]
[TestCategory("Infrastructure")]
public sealed class OracleInfrastructureTests : InteropTestBase
{
    [TestMethod]
    public void MissingOracleProducesAssertionFailure()
    {
        AssertFailedException error = Assert.ThrowsExactly<AssertFailedException>(() => ResolveOracle(Work, "missing_oracle"));
        StringAssert.Contains(error.Message, "Build Test/NativeReference first");
    }

    [TestMethod]
    public async Task NonzeroExitProducesAssertionFailureWithDiagnostics()
    {
        // The real oracle returns an error for a missing command, without needing a fake test runner.
        AssertFailedException error = await Assert.ThrowsExactlyAsync<AssertFailedException>(() => OracleAsync("file_oracle"));
        StringAssert.Contains(error.Message, "file_oracle");
        Assert.IsTrue(Directory.EnumerateFiles(Work, "*.log").Any());
    }

    [TestMethod]
    public async Task TimedOutProcessIsTerminatedAndReportedAsAssertionFailure()
    {
        string executable = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") : "/bin/sh";
        string[] arguments = OperatingSystem.IsWindows()
            ? new[] { "/d", "/c", "ping -n 30 127.0.0.1 > nul" } : new[] { "-c", "sleep 30" };
        string log = Path.Combine(Work, "timeout.log");
        AssertFailedException error = await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
            NativeProcess.ExecuteAsync(executable, arguments, Work, log, TimeSpan.FromMilliseconds(100)));
        StringAssert.Contains(error.Message, "exceeded");
        StringAssert.Contains(await File.ReadAllTextAsync(log), "timed out: True");
    }
}
