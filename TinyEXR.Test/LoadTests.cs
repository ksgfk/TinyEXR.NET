namespace TinyEXR.Test;

[TestClass]
public sealed class LoadTests
{
    [TestMethod(DisplayName = "asakusa")]
    public void Case_asakusa()
    {
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(TestPaths.Asakusa, out _));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(TestPaths.Asakusa, out _, out ExrHeader header));
        Assert.IsTrue(header.Channels.Count > 0);
    }

    [TestMethod(DisplayName = "utf8filename")]
    public void Case_utf8filename()
    {
        string path = TestPaths.Regression("日本語.exr");
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out _));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header));
        Assert.IsTrue(header.Channels.Count > 0);
    }

    [TestMethod(DisplayName = "ScanLines")]
    public void Case_ScanLines()
    {
        foreach (string relativePath in new[]
        {
            "ScanLines/Blobbies.exr",
            "ScanLines/CandleGlass.exr",
            "ScanLines/Desk.exr",
            "ScanLines/MtTamWest.exr",
            "ScanLines/PrismsLenses.exr",
            "ScanLines/StillLife.exr",
            "ScanLines/Tree.exr",
        })
        {
            AssertSinglePartLoads(relativePath);
        }
    }

    [TestMethod(DisplayName = "Chromaticities")]
    public void Case_Chromaticities()
    {
        foreach (string relativePath in new[]
        {
            "Chromaticities/Rec709.exr",
            "Chromaticities/Rec709_YC.exr",
            "Chromaticities/XYZ.exr",
            "Chromaticities/XYZ_YC.exr",
        })
        {
            AssertSinglePartLoads(relativePath);
        }
    }

    [TestMethod(DisplayName = "TestImages")]
    public void Case_TestImages()
    {
        foreach (string relativePath in new[]
        {
            "TestImages/AllHalfValues.exr",
            "TestImages/BrightRings.exr",
            "TestImages/BrightRingsNanInf.exr",
            "TestImages/WideColorGamut.exr",
        })
        {
            AssertSinglePartLoads(relativePath);
        }
    }

    [TestMethod(DisplayName = "LuminanceChroma")]
    public void Case_LuminanceChroma()
    {
        foreach (string relativePath in new[]
        {
            "LuminanceChroma/MtTamNorth.exr",
            "LuminanceChroma/StarField.exr",
        })
        {
            AssertSinglePartLoads(relativePath);
        }
    }

    [TestMethod(DisplayName = "DisplayWindow")]
    public void Case_DisplayWindow()
    {
        foreach (string relativePath in Enumerable.Range(1, 16).Select(static i => $"DisplayWindow/t{i:00}.exr"))
        {
            AssertSinglePartLoads(relativePath);
        }
    }

    [TestMethod(DisplayName = "Tiles/GoldenGate.exr")]
    public void Case_Tiles_GoldenGate_exr_Version()
    {
        string path = TestPaths.OpenExr("Tiles/GoldenGate.exr");
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version));
        Assert.IsTrue(version.Tiled);
    }

    [TestMethod(DisplayName = "Tiles/GoldenGate.exr|Load")]
    public void Case_Tiles_GoldenGate_exr_Load()
    {
        string path = TestPaths.OpenExr("Tiles/GoldenGate.exr");
        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(ExrTileLevelMode.OneLevel, header.Tiles!.LevelMode);
        Assert.AreEqual(128, header.Tiles.TileSizeX);
        Assert.AreEqual(128, header.Tiles.TileSizeY);
        Assert.AreEqual(1, Exr.EXRNumLevels(image));
        Assert.AreEqual(0, image.Levels[0].LevelX);
        Assert.AreEqual(0, image.Levels[0].LevelY);
        Assert.IsTrue(image.Levels[0].Tiles.Count > 0);
    }

    [TestMethod(DisplayName = "LuminanceChroma/Garden.exr|Load")]
    public void Case_LuminanceChroma_Garden_exr_Load()
    {
        AssertTiledSinglePartLoads("LuminanceChroma/Garden.exr");
    }

    [TestMethod(DisplayName = "Tiles/Ocean.exr")]
    public void Case_Tiles_Ocean_exr()
    {
        AssertTiledSinglePartLoads("Tiles/Ocean.exr");
    }

    [TestMethod(DisplayName = "MultiResolution/Bonita.exr")]
    public void Case_MultiResolution_Bonita_exr()
    {
        string path = TestPaths.OpenExr("MultiResolution/Bonita.exr");
        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(ExrTileLevelMode.MipMapLevels, header.Tiles!.LevelMode);
        Assert.AreEqual(ExrTileRoundingMode.RoundDown, header.Tiles.RoundingMode);
        Assert.AreEqual(10, Exr.EXRNumLevels(image));
    }

    [TestMethod(DisplayName = "MultiResolution/Kapaa.exr")]
    public void Case_MultiResolution_Kapaa_exr()
    {
        string path = TestPaths.OpenExr("MultiResolution/Kapaa.exr");
        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(ExrTileLevelMode.RipMapLevels, header.Tiles!.LevelMode);
        Assert.AreEqual(ExrTileRoundingMode.RoundUp, header.Tiles.RoundingMode);
        Assert.AreEqual(64, header.Tiles.TileSizeX);
        Assert.AreEqual(64, header.Tiles.TileSizeY);
        Assert.AreEqual(11 * 11, Exr.EXRNumLevels(image));
    }

    [TestMethod(DisplayName = "Beachball/multipart.0001.exr")]
    public void Case_Beachball_multipart_0001_exr_Version()
    {
        string path = TestPaths.OpenExr("Beachball/multipart.0001.exr");
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version));
        Assert.IsTrue(version.Multipart);
    }

    [TestMethod(DisplayName = "Beachball/multipart.0001.exr|Load")]
    public void Case_Beachball_multipart_0001_exr_Load()
    {
        string path = TestPaths.OpenExr("Beachball/multipart.0001.exr");
        (ExrVersion version, ExrMultipartHeader headers, ExrMultipartImage images) = ExrTestHelper.LoadMultipart(path);

        Assert.IsTrue(version.Multipart);
        Assert.AreEqual(10, headers.Headers.Count);
        Assert.AreEqual(10, images.Images.Count);
    }

    [TestMethod(DisplayName = "Beachbal multiparts")]
    public void Case_Beachbal_multiparts()
    {
        foreach (string relativePath in Enumerable.Range(1, 8).Select(static i => $"Beachball/multipart.{i:0000}.exr"))
        {
            string path = TestPaths.OpenExr(relativePath);
            (ExrVersion version, ExrMultipartHeader headers, ExrMultipartImage images) = ExrTestHelper.LoadMultipart(path);

            Assert.IsTrue(version.Multipart, relativePath);
            Assert.AreEqual(10, headers.Headers.Count, relativePath);
            Assert.AreEqual(10, images.Images.Count, relativePath);
        }
    }

    [TestMethod(DisplayName = "Beachbal singleparts")]
    public void Case_Beachbal_singleparts()
    {
        foreach (string relativePath in Enumerable.Range(1, 8).Select(static i => $"Beachball/singlepart.{i:0000}.exr"))
        {
            AssertSinglePartLoads(relativePath);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] deepscanline.exr|LoadDeep")]
    public void Case_deepscanline_exr_LoadDeep()
    {
        string path = Path.Combine(TestPaths.NativeTinyExrRoot, "deepscanline.exr");

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version));
        Assert.IsFalse(version.Tiled);
        Assert.IsTrue(version.NonImage);
        Assert.IsFalse(version.Multipart);

        Assert.AreEqual(ResultCode.Success, Exr.LoadDeepEXR(path, out ExrHeader header, out ExrDeepImage image));
        Assert.IsTrue(header.IsDeep);
        Assert.AreEqual("deepscanline", header.PartType);
        Assert.IsNull(header.Tiles);
        Assert.IsTrue(image.Width > 0);
        Assert.IsTrue(image.Height > 0);
        Assert.AreEqual(image.Height, image.OffsetTable.Length);
        Assert.AreEqual(header.Channels.Count, image.Channels.Count);
    }

    private static void AssertSinglePartLoads(string relativePath)
    {
        string path = TestPaths.OpenExr(relativePath);

        Assert.IsTrue(Exr.IsEXR(path), relativePath);

        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsFalse(version.Tiled, relativePath);
        Assert.IsFalse(version.NonImage, relativePath);
        Assert.IsFalse(version.Multipart, relativePath);
        Assert.IsNull(header.Tiles, relativePath);
        Assert.AreEqual(1, image.Levels.Count, relativePath);
        Assert.AreEqual(0, image.Levels[0].Tiles.Count, relativePath);
        Assert.AreEqual(header.DataWindow.Width, image.Width, relativePath);
        Assert.AreEqual(header.DataWindow.Height, image.Height, relativePath);
        Assert.IsTrue(image.Channels.Count > 0, relativePath);
    }

    private static void AssertTiledSinglePartLoads(string relativePath)
    {
        string path = TestPaths.OpenExr(relativePath);
        (ExrVersion version, _, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.IsTrue(image.Levels[0].Tiles.Count > 0);
    }
}
