using TinyEXR.Benchmark;

[assembly: DoNotParallelize]
namespace TinyEXR.Benchmark.Test;

[TestClass]
public sealed class BenchmarkAdapterTests
{
    public TestContext TestContext { get; set; } = null!;
    private string _work = null!;
    private int _calls;
    public static IEnumerable<object[]> Cases() =>
        from implementation in new[] { "Managed", "TinyEXR", "OpenEXR" }
        from compression in Enum.GetValues<Compression>().Where(c => (int)c <= 11)
        where implementation == "OpenEXR" || FileComparisonData.SharedCompressions.Contains(compression)
        select new object[] { implementation, compression };

    [TestInitialize]
    public void Initialize()
    {
        _work = Path.Combine(FileComparisonData.RepoRoot, ".cache", "benchmark-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (TestContext.CurrentTestOutcome == UnitTestOutcome.Passed)
        {
            string parent = Path.GetFullPath(Path.Combine(FileComparisonData.RepoRoot, ".cache", "benchmark-test")) + Path.DirectorySeparatorChar;
            Assert.IsTrue(Path.GetFullPath(_work).StartsWith(parent, StringComparison.OrdinalIgnoreCase));
            foreach (string file in Directory.EnumerateFiles(_work, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(_work, recursive: true);
        }
        else
        {
            foreach (string file in Directory.EnumerateFiles(_work)) TestContext.AddResultFile(file);
            TestContext.WriteLine("Failure inputs retained at " + _work);
        }
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task DecodeOfficialFile(string implementation, Compression compression)
    {
        byte[] input = FileComparisonData.LoadVerified(compression);
        ValidateHeader(input, compression);
        byte[] expected = FileComparisonData.LoadVerified(compression, raw: true);
        string file = Path.Combine(_work, "input.exr");
        File.WriteAllBytes(file, input);
        if (implementation == "Managed")
        {
            var decoded = ExrFile.LoadFromMemory(input);
            Assert.IsTrue(decoded.IsSuccess, decoded.Error?.ToString());
            Equal(expected, FileComparisonData.Canonical(decoded.Value!), "Managed/" + compression);
        }
        else
        {
            string output = Path.Combine(_work, "actual.raw");
            var result = await Native("decode", implementation, file, output);
            if (implementation == "TinyEXR" && compression is Compression.HTJ2K32 or Compression.HTJ2K256)
            {
                Assert.AreEqual(3, result.ExitCode, result.Error);
                StringAssert.Contains(result.Error, "Unsupported: TinyEXR status -3");
                Assert.IsFalse(File.Exists(output), "Unsupported must not publish partially decoded samples.");
                TestContext.WriteLine(result.Error);
            }
            else
            {
                NativeProcess.RequireSuccess(result);
                Equal(expected, File.ReadAllBytes(output), implementation + "/" + compression);
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EncodedFileIsFullyReadableByOfficial(string implementation, Compression compression)
    {
        string file = Path.Combine(_work, "encoded.exr");
        if (implementation == "Managed")
        {
            var encoded = ExrFile.SaveToMemory(FileComparisonData.CreateSource(), compression);
            Assert.IsTrue(encoded.IsSuccess, encoded.Error?.ToString());
            File.WriteAllBytes(file, encoded.Value!);
        }
        else NativeProcess.RequireSuccess(await Native("encode", implementation, compression.ToString(), file));
        ValidateHeader(File.ReadAllBytes(file), compression);
        string official = Path.Combine(_work, "official.raw");
        NativeProcess.RequireSuccess(await Native("decode", "OpenEXR", file, official));
        byte[] expected = File.ReadAllBytes(official);
        Assert.HasCount(FileComparisonData.RawBytes, expected);
        // PXR24 is lossless for this HALF input.
        if (compression is not (Compression.B44 or Compression.B44A or Compression.DWAA or Compression.DWAB))
            Equal(FileComparisonData.Canonical(FileComparisonData.CreateSource()), expected, "Lossless/" + implementation + "/" + compression);
        if (implementation == "Managed")
        {
            var decoded = ExrFile.LoadFromMemory(File.ReadAllBytes(file));
            Assert.IsTrue(decoded.IsSuccess, decoded.Error?.ToString());
            Equal(expected, FileComparisonData.Canonical(decoded.Value!), "Same-file managed decode");
        }
        else
        {
            string own = Path.Combine(_work, "own.raw");
            NativeProcess.RequireSuccess(await Native("decode", implementation, file, own));
            Equal(expected, File.ReadAllBytes(own), "Same-file " + implementation + " decode");
        }
    }

    [TestMethod]
    public void SharedInputChecksumAndManifestAreEnforced()
    {
        File.Copy(Path.Combine(FileComparisonData.Root, "manifest.csv"), Path.Combine(_work, "manifest.csv"));
        File.WriteAllBytes(Path.Combine(_work, "None.exr"), [1, 2, 3]);
        Assert.ThrowsExactly<InvalidDataException>(() => FileComparisonData.LoadVerified(Compression.None, root: _work));
        string[] manifest = File.ReadAllLines(Path.Combine(_work, "manifest.csv"));
        manifest[2] = manifest[1];
        File.WriteAllLines(Path.Combine(_work, "manifest.csv"), manifest);
        Assert.ThrowsExactly<InvalidDataException>(() => FileComparisonData.LoadVerified(Compression.None, root: _work));
    }

    [TestMethod]
    [DataRow("Managed")]
    [DataRow("TinyEXR")]
    [DataRow("OpenEXR")]
    public async Task CorruptInputFails(string implementation)
    {
        byte[] input = [0x76, 0x2f, 0x31, 0x01, 2, 0, 0, 0];
        if (implementation == "Managed") Assert.IsFalse(ExrFile.LoadFromMemory(input).IsSuccess);
        else
        {
            string path = Path.Combine(_work, "corrupt.exr");
            File.WriteAllBytes(path, input);
            var result = await Native("decode", implementation, path, Path.Combine(_work, "bad.raw"));
            Assert.AreEqual(1, result.ExitCode, result.Error);
            Assert.IsFalse(File.Exists(Path.Combine(_work, "bad.raw")));
        }
    }

    [TestMethod]
    public async Task MissingNativeToolFails() =>
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            NativeProcess.Execute(Path.Combine(_work, "missing"), [], Path.Combine(_work, "missing.log")));

    [TestMethod]
    public async Task NonzeroNativeExitIsNotSwallowed()
    {
        var result = await Native("invalid-command");
        Assert.AreEqual(1, result.ExitCode);
        await Assert.ThrowsExactlyAsync<AssertFailedException>(() => Task.Run(() => NativeProcess.RequireSuccess(result)));
    }

    [TestMethod]
    public async Task NativeTimeoutTerminatesProcessAndRetainsLog()
    {
        string shell = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
        // pwsh is a test helper here; no benchmark timing depends on this process.
        string pwsh = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe")
            : "/usr/bin/pwsh";
        string log = Path.Combine(_work, "timeout.log");
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => NativeProcess.Execute(pwsh,
            ["-NoProfile", "-Command", "Write-Output ready; [Console]::Error.WriteLine('stderr'); Start-Sleep -Seconds 60"],
            log, TimeSpan.FromSeconds(2)));
        StringAssert.Contains(File.ReadAllText(log), "ready");
        StringAssert.Contains(File.ReadAllText(log), "stderr");
    }

    [TestMethod]
    [DataRow("Missing")]
    [DataRow("Duplicate")]
    [DataRow("UnexpectedFailure")]
    [DataRow("UnsupportedWithTiming")]
    public void InvalidSummaryFails(string mutation)
    {
        var rows = ComparisonReport.Keys.Select(k => new ComparisonRow(k.Implementation, k.Operation, k.Compression,
            ComparisonReport.IsUnsupported(k.Implementation, k.Operation, k.Compression) ? "Unsupported" : "Measured",
            ComparisonReport.IsUnsupported(k.Implementation, k.Operation, k.Compression) ? null : 1,
            100, null, "Unsupported: TinyEXR EXR_ERROR_UNSUPPORTED (-3); official HTJ2K multiple tile-parts")).ToList();
        ComparisonReport.Validate(rows);
        switch (mutation)
        {
            case "Missing": rows.RemoveAt(0); break;
            case "Duplicate": rows.Add(rows[0]); break;
            case "UnexpectedFailure": rows[0] = rows[0] with { Status = "Unsupported", MeanMilliseconds = null }; break;
            default:
                int i = rows.FindIndex(r => r.Status == "Unsupported");
                rows[i] = rows[i] with { MeanMilliseconds = 1 }; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => ComparisonReport.Validate(rows));
    }

    [TestMethod]
    [DataRow("Reuse")]
    [DataRow("WrongVersion")]
    [DataRow("Dirty")]
    public async Task SourceCacheNeverOverwritesLocalState(string scenario)
    {
        string FindExecutable(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator).Select(p => Path.Combine(p.Trim('"'), name + (OperatingSystem.IsWindows() ? ".exe" : "")))
            .First(File.Exists);
        string git = FindExecutable("git"), pwsh = FindExecutable("pwsh");
        string checkout = Path.Combine(_work, "source");
        async Task<NativeResult> Git(params string[] args) => await NativeProcess.Execute(git, args,
            Path.Combine(_work, $"git-{_calls++}.log"));
        NativeProcess.RequireSuccess(await Git("clone", "--shared", "--quiet",
            Path.Combine(FileComparisonData.RepoRoot, ".cache", "tinyexr"), checkout));
        NativeProcess.RequireSuccess(await Git("-C", checkout, "remote", "set-url", "origin", "https://invalid.example.test/no-network"));
        if (scenario == "Dirty") File.WriteAllText(Path.Combine(checkout, "local-edit.txt"), "keep this");
        if (scenario == "WrongVersion")
            NativeProcess.RequireSuccess(await Git("-C", checkout, "-c", "user.name=Cache Test",
                "-c", "user.email=cache@example.invalid", "commit", "--allow-empty", "-m", "Local commit"));
        string head = (await Git("-C", checkout, "rev-parse", "HEAD")).Output;
        var result = await NativeProcess.Execute(pwsh,
            ["-NoProfile", "-File", Path.Combine(FileComparisonData.RepoRoot, "Scripts", "prepare-tinyexr.ps1"),
             "-DestinationRoot", checkout], Path.Combine(_work, "prepare.log"));
        if (scenario == "Reuse")
        {
            NativeProcess.RequireSuccess(result);
            StringAssert.Contains(result.Output, "no network access");
        }
        else
        {
            Assert.AreNotEqual(0, result.ExitCode, "Dirty/wrong source must not be accepted.");
            StringAssert.Contains(result.Error, "overwritten");
        }
        Assert.AreEqual(head, (await Git("-C", checkout, "rev-parse", "HEAD")).Output);
        if (scenario == "Dirty") Assert.AreEqual("keep this", File.ReadAllText(Path.Combine(checkout, "local-edit.txt")));
    }
    private Task<NativeResult> Native(params string[] arguments)
    {
        string root = Environment.GetEnvironmentVariable("TINYEXR_BENCHMARK_NATIVE_DIR") ??
            Path.Combine(FileComparisonData.RepoRoot, ".cache", "benchmark-native-clang");
        string name = "file_fixture_tool" + (OperatingSystem.IsWindows() ? ".exe" : "");
        string executable = Path.Combine(root, name);
        if (!File.Exists(executable)) executable = Path.Combine(root, "Release", name);
        return NativeProcess.Execute(executable, arguments, Path.Combine(_work, $"native-{_calls++}.log"));
    }

    private static void ValidateHeader(byte[] file, Compression compression)
    {
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromMemory(file, out uint version, out Header? header));
        Assert.IsNotNull(header);
        Assert.AreEqual(2u, version);
        Assert.AreEqual(new Box2i(0, 0, FileComparisonData.Width - 1, FileComparisonData.Height - 1), header.DataWindow);
        Assert.AreEqual(header.DataWindow, header.DisplayWindow);
        Assert.AreEqual(LineOrder.IncreasingY, header.LineOrder);
        Assert.AreEqual(compression, header.Compression);
        Assert.AreEqual(PartType.Scanline, header.PartType);
        CollectionAssert.AreEqual(new[] { "A", "B", "G", "R" }, header.Channels.Select(c => c.Name).ToArray());
        foreach (var channel in header.Channels)
        {
            Assert.AreEqual(PixelType.Half, channel.PixelType);
            Assert.AreEqual(1, channel.XSampling);
            Assert.AreEqual(1, channel.YSampling);
        }
    }

    private static void Equal(byte[] expected, byte[] actual, string label)
    {
        if (expected.AsSpan().SequenceEqual(actual)) return;
        int offset = 0; while (offset < Math.Min(expected.Length, actual.Length) && expected[offset] == actual[offset]) offset++;
        Assert.Fail($"{label}: first mismatch at byte {offset}, part 0, level (0,0), row {offset / (1920 * 8)}, channel {(offset % (1920 * 8)) / (1920 * 2)}; lengths {expected.Length}/{actual.Length}.");
    }
}
