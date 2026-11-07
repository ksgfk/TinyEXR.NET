namespace TinyEXR.Test;

[TestClass]
public sealed class FixturePathTests
{
    [TestMethod]
    public void TinyExrSamplesUsePinnedCache()
    {
        Assert.AreEqual(Path.Combine(TestPaths.RepoRoot, ".cache", "tinyexr"), TestPaths.NativeTinyExrRoot);
        Assert.IsTrue(File.Exists(TestPaths.DeepScanline));
    }

    [TestMethod]
    public void MissingTinyExrCacheReportsPreparationCommand()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var error = Assert.ThrowsExactly<DirectoryNotFoundException>(() => TestPaths.ResolveTinyExrRoot(missing));
        StringAssert.Contains(error.Message, "Scripts/prepare-tinyexr.ps1");
    }
}
