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

    [TestMethod(DisplayName = "[TinyEXR.NET Test] ScanLines extensions|Metadata")]
    public void Case_ScanLines_extensions_Metadata()
    {
        foreach (ExpectedSinglePartSample sample in new[]
        {
            new ExpectedSinglePartSample(
                "ScanLines/Cannon.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 779, 565),
                DisplayWindow: new ExrBox2i(0, 0, 779, 565)),
            new ExpectedSinglePartSample(
                "ScanLines/Carrots.exr",
                Channels: 4,
                DataWindow: new ExrBox2i(0, 0, 599, 399),
                DisplayWindow: new ExrBox2i(0, 0, 599, 399)),
        })
        {
            AssertSinglePartSampleMetadata(sample);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] LuminanceChroma extensions|Metadata")]
    public void Case_LuminanceChroma_extensions_Metadata()
    {
        foreach (ExpectedSinglePartSample sample in new[]
        {
            new ExpectedSinglePartSample(
                "LuminanceChroma/CrissyField.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 1217, 809),
                DisplayWindow: new ExrBox2i(0, 0, 1217, 809)),
            new ExpectedSinglePartSample(
                "LuminanceChroma/Flowers.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 783, 733),
                DisplayWindow: new ExrBox2i(0, 0, 783, 733)),
        })
        {
            AssertSinglePartSampleMetadata(sample);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] TestImages extensions|Metadata")]
    public void Case_TestImages_extensions_Metadata()
    {
        foreach (ExpectedSinglePartSample sample in new[]
        {
            new ExpectedSinglePartSample(
                "TestImages/GammaChart.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 799, 799),
                DisplayWindow: new ExrBox2i(0, 0, 799, 799)),
            new ExpectedSinglePartSample(
                "TestImages/GrayRampsHorizontal.exr",
                Channels: 1,
                DataWindow: new ExrBox2i(0, 0, 799, 799),
                DisplayWindow: new ExrBox2i(0, 0, 799, 799)),
            new ExpectedSinglePartSample(
                "TestImages/GrayRampsDiagonal.exr",
                Channels: 1,
                DataWindow: new ExrBox2i(0, 0, 799, 799),
                DisplayWindow: new ExrBox2i(0, 0, 799, 799)),
            new ExpectedSinglePartSample(
                "TestImages/RgbRampsDiagonal.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 799, 799),
                DisplayWindow: new ExrBox2i(0, 0, 799, 799)),
            new ExpectedSinglePartSample(
                "TestImages/SquaresSwirls.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 999, 999),
                DisplayWindow: new ExrBox2i(0, 0, 999, 999)),
            new ExpectedSinglePartSample(
                "TestImages/stripes.exr",
                Channels: 4,
                DataWindow: new ExrBox2i(0, 0, 99, 49),
                DisplayWindow: new ExrBox2i(0, 0, 99, 49)),
            new ExpectedSinglePartSample(
                "TestImages/WideFloatRange.exr",
                Channels: 1,
                DataWindow: new ExrBox2i(0, 0, 499, 499),
                DisplayWindow: new ExrBox2i(0, 0, 499, 499)),
        })
        {
            AssertSinglePartSampleMetadata(sample);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Tiles/Spirals.exr|Metadata")]
    public void Case_Tiles_Spirals_exr_Metadata()
    {
        string path = TestPaths.OpenExr("Tiles/Spirals.exr");

        Assert.IsTrue(Exr.IsEXR(path));

        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsTrue(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.AreEqual(5, header.Channels.Count);
        AssertBox(new ExrBox2i(-20, -20, 1019, 1019), header.DataWindow, path);
        AssertBox(new ExrBox2i(0, 0, 999, 999), header.DisplayWindow, path);
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(ExrTileLevelMode.OneLevel, header.Tiles!.LevelMode);
        Assert.AreEqual(ExrTileRoundingMode.RoundDown, header.Tiles.RoundingMode);
        Assert.AreEqual(287, header.Tiles.TileSizeX);
        Assert.AreEqual(126, header.Tiles.TileSizeY);
        Assert.AreEqual(header.DataWindow.Width, image.Width);
        Assert.AreEqual(header.DataWindow.Height, image.Height);
        Assert.AreEqual(1, Exr.EXRNumLevels(image));
        Assert.AreEqual(36, image.Levels[0].Tiles.Count);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] MultiResolution extensions|MipContract")]
    public void Case_MultiResolution_extensions_MipContract()
    {
        foreach (ExpectedTiledMipSample sample in new[]
        {
            new ExpectedTiledMipSample(
                "MultiResolution/ColorCodedLevels.exr",
                Channels: 4,
                DataWindow: new ExrBox2i(0, 0, 511, 511),
                RoundingMode: ExrTileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/MirrorPattern.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 511, 511),
                RoundingMode: ExrTileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/OrientationCube.exr",
                Channels: 4,
                DataWindow: new ExrBox2i(0, 0, 511, 3071),
                RoundingMode: ExrTileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/OrientationLatLong.exr",
                Channels: 4,
                DataWindow: new ExrBox2i(0, 0, 1023, 511),
                RoundingMode: ExrTileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/StageEnvCube.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 255, 1535),
                RoundingMode: ExrTileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/StageEnvLatLong.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 999, 499),
                RoundingMode: ExrTileRoundingMode.RoundUp),
            new ExpectedTiledMipSample(
                "MultiResolution/KernerEnvCube.exr",
                Channels: 4,
                DataWindow: new ExrBox2i(0, 0, 255, 1535),
                RoundingMode: ExrTileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/KernerEnvLatLong.exr",
                Channels: 4,
                DataWindow: new ExrBox2i(0, 0, 1023, 511),
                RoundingMode: ExrTileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/WavyLinesCube.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 255, 1535),
                RoundingMode: ExrTileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/WavyLinesLatLong.exr",
                Channels: 3,
                DataWindow: new ExrBox2i(0, 0, 1023, 511),
                RoundingMode: ExrTileRoundingMode.RoundDown),
        })
        {
            string path = TestPaths.OpenExr(sample.RelativePath);

            Assert.IsTrue(Exr.IsEXR(path), sample.RelativePath);

            (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);
            Assert.IsTrue(version.Tiled, sample.RelativePath);
            Assert.IsFalse(version.NonImage, sample.RelativePath);
            Assert.IsFalse(version.Multipart, sample.RelativePath);
            Assert.AreEqual(sample.Channels, header.Channels.Count, sample.RelativePath);
            AssertBox(sample.DataWindow, header.DataWindow, sample.RelativePath);
            AssertBox(sample.DataWindow, header.DisplayWindow, sample.RelativePath);
            Assert.IsNotNull(header.Tiles, sample.RelativePath);
            Assert.AreEqual(ExrTileLevelMode.MipMapLevels, header.Tiles!.LevelMode, sample.RelativePath);
            Assert.AreEqual(sample.RoundingMode, header.Tiles.RoundingMode, sample.RelativePath);
            Assert.AreEqual(64, header.Tiles.TileSizeX, sample.RelativePath);
            Assert.AreEqual(64, header.Tiles.TileSizeY, sample.RelativePath);
            Assert.AreEqual(
                ComputeExpectedMipLevelCount(header.DataWindow.Width, header.DataWindow.Height, header.Tiles.RoundingMode),
                Exr.EXRNumLevels(image),
                sample.RelativePath);

            foreach (ExrImageLevel level in image.Levels)
            {
                Assert.AreEqual(level.LevelX, level.LevelY, sample.RelativePath);
            }

            Assert.IsTrue(image.Levels[0].Tiles.Count > 0, sample.RelativePath);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] MultiResolution cube-latlong layout")]
    public void Case_MultiResolution_cube_latlong_layout()
    {
        foreach (ExpectedLayoutSample sample in new[]
        {
            new ExpectedLayoutSample(
                "MultiResolution/OrientationCube.exr",
                ExpectedChannels: new[] { "A", "B", "G", "R" },
                Width: 512,
                Height: 3072,
                IsCube: true),
            new ExpectedLayoutSample(
                "MultiResolution/OrientationLatLong.exr",
                ExpectedChannels: new[] { "A", "B", "G", "R" },
                Width: 1024,
                Height: 512,
                IsCube: false),
            new ExpectedLayoutSample(
                "MultiResolution/StageEnvCube.exr",
                ExpectedChannels: new[] { "B", "G", "R" },
                Width: 256,
                Height: 1536,
                IsCube: true),
            new ExpectedLayoutSample(
                "MultiResolution/StageEnvLatLong.exr",
                ExpectedChannels: new[] { "B", "G", "R" },
                Width: 1000,
                Height: 500,
                IsCube: false),
        })
        {
            string path = TestPaths.OpenExr(sample.RelativePath);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header), sample.RelativePath);

            CollectionAssert.AreEqual(
                sample.ExpectedChannels,
                header.Channels.Select(static channel => channel.Name).ToArray(),
                sample.RelativePath);
            Assert.AreEqual(sample.Width, header.DataWindow.Width, sample.RelativePath);
            Assert.AreEqual(sample.Height, header.DataWindow.Height, sample.RelativePath);
            if (sample.IsCube)
            {
                Assert.AreEqual(sample.Width * 6, sample.Height, sample.RelativePath);
            }
            else
            {
                Assert.AreEqual(sample.Width, sample.Height * 2, sample.RelativePath);
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] MultiResolution/WavyLinesSphere.exr|DirectoryCompatibility")]
    public void Case_MultiResolution_WavyLinesSphere_exr_DirectoryCompatibility()
    {
        ExpectedSinglePartSample sample = new(
            "MultiResolution/WavyLinesSphere.exr",
            Channels: 4,
            DataWindow: new ExrBox2i(0, 0, 479, 479),
            DisplayWindow: new ExrBox2i(0, 0, 479, 479));

        string path = TestPaths.OpenExr(sample.RelativePath);

        Assert.IsTrue(Exr.IsEXR(path), sample.RelativePath);

        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsFalse(version.Tiled, sample.RelativePath);
        Assert.IsFalse(version.NonImage, sample.RelativePath);
        Assert.IsFalse(version.Multipart, sample.RelativePath);
        Assert.AreEqual(sample.Channels, header.Channels.Count, sample.RelativePath);
        AssertBox(sample.DataWindow, header.DataWindow, sample.RelativePath);
        AssertBox(sample.DisplayWindow, header.DisplayWindow, sample.RelativePath);
        Assert.IsNull(header.Tiles, sample.RelativePath);
        Assert.AreEqual(1, Exr.EXRNumLevels(image), sample.RelativePath);
        Assert.AreEqual(sample.DataWindow.Width, image.Width, sample.RelativePath);
        Assert.AreEqual(sample.DataWindow.Height, image.Height, sample.RelativePath);
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

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Stream read APIs match file APIs")]
    public void Case_StreamReadApis_Match_FileApis()
    {
        string singlePartPath = TestPaths.OpenExr("ScanLines/Desk.exr");
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(singlePartPath, out ExrVersion expectedVersion));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(singlePartPath, out _, out ExrHeader expectedHeader));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromFile(singlePartPath, expectedHeader, out ExrImage expectedImage));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXR(singlePartPath, out float[] expectedRgba, out int expectedWidth, out int expectedHeight));

        using (FileStream versionStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromStream(versionStream, out ExrVersion streamVersion));
            Assert.AreEqual(0, versionStream.Position);
            Assert.AreEqual(expectedVersion.Tiled, streamVersion.Tiled);
            Assert.AreEqual(expectedVersion.Multipart, streamVersion.Multipart);
            Assert.AreEqual(expectedVersion.NonImage, streamVersion.NonImage);
        }

        using (FileStream headerStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromStream(headerStream, out _, out ExrHeader streamHeader));
            Assert.AreEqual(0, headerStream.Position);
            ExrTestHelper.EqualHeaders(expectedHeader, streamHeader);
        }

        using (FileStream imageStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromStream(imageStream, expectedHeader, out ExrImage streamImage));
            Assert.AreEqual(0, imageStream.Position);
            ExrTestHelper.EqualImages(expectedImage, streamImage);
        }

        using (FileStream rgbaStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRFromStream(rgbaStream, out float[] streamRgba, out int streamWidth, out int streamHeight));
            Assert.AreEqual(0, rgbaStream.Position);
            Assert.AreEqual(expectedWidth, streamWidth);
            Assert.AreEqual(expectedHeight, streamHeight);
            CollectionAssert.AreEqual(expectedRgba, streamRgba);
        }

        using (FileStream layersStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ResultCode.Success, Exr.EXRLayers(singlePartPath, out string[] expectedLayers));
            Assert.AreEqual(ResultCode.Success, Exr.EXRLayersFromStream(layersStream, out string[] streamLayers));
            Assert.AreEqual(0, layersStream.Position);
            CollectionAssert.AreEqual(expectedLayers, streamLayers);
        }

        string multipartPath = TestPaths.OpenExr("Beachball/multipart.0001.exr");
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromFile(multipartPath, out _, out ExrMultipartHeader expectedMultipartHeaders));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromFile(multipartPath, expectedMultipartHeaders, out ExrMultipartImage expectedMultipartImages));

        using (FileStream multipartHeaderStream = File.OpenRead(multipartPath))
        {
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromStream(multipartHeaderStream, out ExrVersion streamVersion, out ExrMultipartHeader streamHeaders));
            Assert.AreEqual(0, multipartHeaderStream.Position);
            Assert.IsTrue(streamVersion.Multipart);
            Assert.AreEqual(expectedMultipartHeaders.Headers.Count, streamHeaders.Headers.Count);
        }

        using (FileStream multipartImageStream = File.OpenRead(multipartPath))
        {
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromStream(multipartImageStream, expectedMultipartHeaders, out ExrMultipartImage streamImages));
            Assert.AreEqual(0, multipartImageStream.Position);
            Assert.AreEqual(expectedMultipartImages.Images.Count, streamImages.Images.Count);
        }

        string deepPath = Path.Combine(TestPaths.NativeTinyExrRoot, "deepscanline.exr");
        Assert.AreEqual(ResultCode.Success, Exr.LoadDeepEXR(deepPath, out ExrHeader expectedDeepHeader, out ExrDeepImage expectedDeepImage));
        using (FileStream deepStream = File.OpenRead(deepPath))
        {
            Assert.AreEqual(ResultCode.Success, Exr.LoadDeepEXRFromStream(deepStream, out ExrHeader streamDeepHeader, out ExrDeepImage streamDeepImage));
            Assert.AreEqual(0, deepStream.Position);
            ExrTestHelper.EqualHeaders(expectedDeepHeader, streamDeepHeader);
            Assert.AreEqual(expectedDeepImage.Width, streamDeepImage.Width);
            Assert.AreEqual(expectedDeepImage.Height, streamDeepImage.Height);
            Assert.AreEqual(expectedDeepImage.Channels.Count, streamDeepImage.Channels.Count);
        }

        using (FileStream readerStream = File.OpenRead(singlePartPath))
        {
            SinglePartExrReader reader = new();
            reader.Read(readerStream);
            Assert.AreEqual(0, readerStream.Position);
            Assert.AreEqual(expectedHeader.DataWindow.Width, reader.Width);
            Assert.AreEqual(expectedHeader.DataWindow.Height, reader.Height);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Stream APIs require seekable streams")]
    public void Case_StreamReadApis_RequireSeekableStreams()
    {
        byte[] data = File.ReadAllBytes(TestPaths.Asakusa);
        using NonSeekableReadStream stream = new(data);

        Assert.AreEqual(ResultCode.InvalidArgument, Exr.ParseEXRVersionFromStream(stream, out _));
        Assert.IsFalse(Exr.IsEXRFromStream(stream));
        Assert.AreEqual(ResultCode.InvalidArgument, Exr.ParseEXRHeaderFromStream(stream, out _, out _));
        Assert.AreEqual(ResultCode.InvalidArgument, Exr.LoadEXRFromStream(stream, out _, out _, out _));
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

    private static void AssertSinglePartSampleMetadata(ExpectedSinglePartSample sample)
    {
        string path = TestPaths.OpenExr(sample.RelativePath);

        Assert.IsTrue(Exr.IsEXR(path), sample.RelativePath);

        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsFalse(version.Tiled, sample.RelativePath);
        Assert.IsFalse(version.NonImage, sample.RelativePath);
        Assert.IsFalse(version.Multipart, sample.RelativePath);
        Assert.AreEqual(sample.Channels, header.Channels.Count, sample.RelativePath);
        AssertBox(sample.DataWindow, header.DataWindow, sample.RelativePath);
        AssertBox(sample.DisplayWindow, header.DisplayWindow, sample.RelativePath);
        Assert.AreEqual(sample.DataWindow.Width, image.Width, sample.RelativePath);
        Assert.AreEqual(sample.DataWindow.Height, image.Height, sample.RelativePath);
    }

    private static void AssertBox(ExrBox2i expected, ExrBox2i actual, string message)
    {
        Assert.AreEqual(expected.MinX, actual.MinX, message);
        Assert.AreEqual(expected.MinY, actual.MinY, message);
        Assert.AreEqual(expected.MaxX, actual.MaxX, message);
        Assert.AreEqual(expected.MaxY, actual.MaxY, message);
    }

    private static int ComputeExpectedMipLevelCount(int width, int height, ExrTileRoundingMode roundingMode)
    {
        int levels = 1;
        while (width > 1 || height > 1)
        {
            width = NextMipDimension(width, roundingMode);
            height = NextMipDimension(height, roundingMode);
            levels++;
        }

        return levels;
    }

    private static int NextMipDimension(int dimension, ExrTileRoundingMode roundingMode)
    {
        if (dimension <= 1)
        {
            return 1;
        }

        return roundingMode == ExrTileRoundingMode.RoundUp
            ? (dimension + 1) / 2
            : dimension / 2;
    }

    private readonly record struct ExpectedSinglePartSample(
        string RelativePath,
        int Channels,
        ExrBox2i DataWindow,
        ExrBox2i DisplayWindow);

    private readonly record struct ExpectedTiledMipSample(
        string RelativePath,
        int Channels,
        ExrBox2i DataWindow,
        ExrTileRoundingMode RoundingMode);

    private readonly record struct ExpectedLayoutSample(
        string RelativePath,
        string[] ExpectedChannels,
        int Width,
        int Height,
        bool IsCube);

    private sealed class NonSeekableReadStream : MemoryStream
    {
        public NonSeekableReadStream(byte[] buffer)
            : base(buffer, writable: false)
        {
        }

        public override bool CanSeek => false;
    }
}
