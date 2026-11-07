namespace TinyEXR.Test;

[TestClass]
public sealed class LoadTests
{
    [TestMethod(DisplayName = "asakusa")]
    public void Case_asakusa()
    {
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(TestPaths.Asakusa, out _));
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromFile(TestPaths.Asakusa, out _, out Header? header));
        Assert.IsNotNull(header);
        Assert.IsTrue(header.Channels.Count > 0);
    }

    [TestMethod(DisplayName = "utf8filename")]
    public void Case_utf8filename()
    {
        string path = TestPaths.Regression("日本語.exr");
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(path, out _));
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromFile(path, out _, out Header? header));
        Assert.IsNotNull(header);
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
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(path, out uint version));
        Assert.IsTrue(((version & (1U << 9)) != 0));
    }

    [TestMethod(DisplayName = "Tiles/GoldenGate.exr|Load")]
    public void Case_Tiles_GoldenGate_exr_Load()
    {
        string path = TestPaths.OpenExr("Tiles/GoldenGate.exr");
        (uint version, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(((version & (1U << 9)) != 0));
        Assert.IsFalse(((version & (1U << 11)) != 0));
        Assert.IsFalse(((version & (1U << 12)) != 0));
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(TileLevelMode.OneLevel, header.Tiles!.LevelMode);
        Assert.AreEqual(128U, header.Tiles.TileSizeX);
        Assert.AreEqual(128U, header.Tiles.TileSizeY);
        Assert.AreEqual(1, Exr.EXRNumLevels(image));
        Assert.AreEqual(0, image.Levels[0].LevelX);
        Assert.AreEqual(0, image.Levels[0].LevelY);
        Assert.IsTrue(image.Header.IsTiled && image.Levels[0].Channels.Count > 0);
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
        (uint version, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(((version & (1U << 9)) != 0));
        Assert.IsFalse(((version & (1U << 11)) != 0));
        Assert.IsFalse(((version & (1U << 12)) != 0));
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(TileLevelMode.MipmapLevels, header.Tiles!.LevelMode);
        Assert.AreEqual(TileRoundingMode.RoundDown, header.Tiles.RoundingMode);
        Assert.AreEqual(10, Exr.EXRNumLevels(image));
    }

    [TestMethod(DisplayName = "MultiResolution/Kapaa.exr")]
    public void Case_MultiResolution_Kapaa_exr()
    {
        string path = TestPaths.OpenExr("MultiResolution/Kapaa.exr");
        (uint version, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(((version & (1U << 9)) != 0));
        Assert.IsFalse(((version & (1U << 11)) != 0));
        Assert.IsFalse(((version & (1U << 12)) != 0));
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(TileLevelMode.RipmapLevels, header.Tiles!.LevelMode);
        Assert.AreEqual(TileRoundingMode.RoundUp, header.Tiles.RoundingMode);
        Assert.AreEqual(64U, header.Tiles.TileSizeX);
        Assert.AreEqual(64U, header.Tiles.TileSizeY);
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
                DataWindow: new Box2i(0, 0, 779, 565),
                DisplayWindow: new Box2i(0, 0, 779, 565)),
            new ExpectedSinglePartSample(
                "ScanLines/Carrots.exr",
                Channels: 4,
                DataWindow: new Box2i(0, 0, 599, 399),
                DisplayWindow: new Box2i(0, 0, 599, 399)),
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
                DataWindow: new Box2i(0, 0, 1217, 809),
                DisplayWindow: new Box2i(0, 0, 1217, 809)),
            new ExpectedSinglePartSample(
                "LuminanceChroma/Flowers.exr",
                Channels: 3,
                DataWindow: new Box2i(0, 0, 783, 733),
                DisplayWindow: new Box2i(0, 0, 783, 733)),
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
                DataWindow: new Box2i(0, 0, 799, 799),
                DisplayWindow: new Box2i(0, 0, 799, 799)),
            new ExpectedSinglePartSample(
                "TestImages/GrayRampsHorizontal.exr",
                Channels: 1,
                DataWindow: new Box2i(0, 0, 799, 799),
                DisplayWindow: new Box2i(0, 0, 799, 799)),
            new ExpectedSinglePartSample(
                "TestImages/GrayRampsDiagonal.exr",
                Channels: 1,
                DataWindow: new Box2i(0, 0, 799, 799),
                DisplayWindow: new Box2i(0, 0, 799, 799)),
            new ExpectedSinglePartSample(
                "TestImages/RgbRampsDiagonal.exr",
                Channels: 3,
                DataWindow: new Box2i(0, 0, 799, 799),
                DisplayWindow: new Box2i(0, 0, 799, 799)),
            new ExpectedSinglePartSample(
                "TestImages/SquaresSwirls.exr",
                Channels: 3,
                DataWindow: new Box2i(0, 0, 999, 999),
                DisplayWindow: new Box2i(0, 0, 999, 999)),
            new ExpectedSinglePartSample(
                "TestImages/stripes.exr",
                Channels: 4,
                DataWindow: new Box2i(0, 0, 99, 49),
                DisplayWindow: new Box2i(0, 0, 99, 49)),
            new ExpectedSinglePartSample(
                "TestImages/WideFloatRange.exr",
                Channels: 1,
                DataWindow: new Box2i(0, 0, 499, 499),
                DisplayWindow: new Box2i(0, 0, 499, 499)),
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

        (uint version, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsTrue(((version & (1U << 9)) != 0));
        Assert.IsFalse(((version & (1U << 11)) != 0));
        Assert.IsFalse(((version & (1U << 12)) != 0));
        Assert.AreEqual(5, header.Channels.Count);
        AssertBox(new Box2i(-20, -20, 1019, 1019), header.DataWindow, path);
        AssertBox(new Box2i(0, 0, 999, 999), header.DisplayWindow, path);
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(TileLevelMode.OneLevel, header.Tiles!.LevelMode);
        Assert.AreEqual(TileRoundingMode.RoundDown, header.Tiles.RoundingMode);
        Assert.AreEqual(287U, header.Tiles.TileSizeX);
        Assert.AreEqual(126U, header.Tiles.TileSizeY);
        Assert.AreEqual(header.DataWindow.Width, image.Width);
        Assert.AreEqual(header.DataWindow.Height, image.Height);
        Assert.AreEqual(1, Exr.EXRNumLevels(image));
        using ExrReader reader = ExrReader.OpenMemory(File.ReadAllBytes(path));
        Assert.AreEqual(ExrResult.Success, reader.ParseHeader().Status);
        Assert.AreEqual(36, reader.GetNumBlocks(0));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] MultiResolution extensions|MipContract")]
    public void Case_MultiResolution_extensions_MipContract()
    {
        foreach (ExpectedTiledMipSample sample in new[]
        {
            new ExpectedTiledMipSample(
                "MultiResolution/ColorCodedLevels.exr",
                Channels: 4,
                DataWindow: new Box2i(0, 0, 511, 511),
                RoundingMode: TileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/MirrorPattern.exr",
                Channels: 3,
                DataWindow: new Box2i(0, 0, 511, 511),
                RoundingMode: TileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/OrientationCube.exr",
                Channels: 4,
                DataWindow: new Box2i(0, 0, 511, 3071),
                RoundingMode: TileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/OrientationLatLong.exr",
                Channels: 4,
                DataWindow: new Box2i(0, 0, 1023, 511),
                RoundingMode: TileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/StageEnvCube.exr",
                Channels: 3,
                DataWindow: new Box2i(0, 0, 255, 1535),
                RoundingMode: TileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/StageEnvLatLong.exr",
                Channels: 3,
                DataWindow: new Box2i(0, 0, 999, 499),
                RoundingMode: TileRoundingMode.RoundUp),
            new ExpectedTiledMipSample(
                "MultiResolution/KernerEnvCube.exr",
                Channels: 4,
                DataWindow: new Box2i(0, 0, 255, 1535),
                RoundingMode: TileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/KernerEnvLatLong.exr",
                Channels: 4,
                DataWindow: new Box2i(0, 0, 1023, 511),
                RoundingMode: TileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/WavyLinesCube.exr",
                Channels: 3,
                DataWindow: new Box2i(0, 0, 255, 1535),
                RoundingMode: TileRoundingMode.RoundDown),
            new ExpectedTiledMipSample(
                "MultiResolution/WavyLinesLatLong.exr",
                Channels: 3,
                DataWindow: new Box2i(0, 0, 1023, 511),
                RoundingMode: TileRoundingMode.RoundDown),
        })
        {
            string path = TestPaths.OpenExr(sample.RelativePath);

            Assert.IsTrue(Exr.IsEXR(path), sample.RelativePath);

            (uint version, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);
            Assert.IsTrue(((version & (1U << 9)) != 0), sample.RelativePath);
            Assert.IsFalse(((version & (1U << 11)) != 0), sample.RelativePath);
            Assert.IsFalse(((version & (1U << 12)) != 0), sample.RelativePath);
            Assert.AreEqual(sample.Channels, header.Channels.Count, sample.RelativePath);
            AssertBox(sample.DataWindow, header.DataWindow, sample.RelativePath);
            AssertBox(sample.DataWindow, header.DisplayWindow, sample.RelativePath);
            Assert.IsNotNull(header.Tiles, sample.RelativePath);
            Assert.AreEqual(TileLevelMode.MipmapLevels, header.Tiles!.LevelMode, sample.RelativePath);
            Assert.AreEqual(sample.RoundingMode, header.Tiles.RoundingMode, sample.RelativePath);
            Assert.AreEqual(64U, header.Tiles.TileSizeX, sample.RelativePath);
            Assert.AreEqual(64U, header.Tiles.TileSizeY, sample.RelativePath);
            Assert.AreEqual(
                ComputeExpectedMipLevelCount(header.DataWindow.Width, header.DataWindow.Height, header.Tiles.RoundingMode),
                Exr.EXRNumLevels(image),
                sample.RelativePath);

            foreach (PartLevel level in image.Levels)
            {
                Assert.AreEqual(level.LevelX, level.LevelY, sample.RelativePath);
            }

            Assert.IsTrue(image.Header.IsTiled && image.Levels[0].Channels.Count > 0, sample.RelativePath);
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
            Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromFile(path, out _, out Header? header), sample.RelativePath);
            Assert.IsNotNull(header);

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
            DataWindow: new Box2i(0, 0, 479, 479),
            DisplayWindow: new Box2i(0, 0, 479, 479));

        string path = TestPaths.OpenExr(sample.RelativePath);

        Assert.IsTrue(Exr.IsEXR(path), sample.RelativePath);

        (uint version, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsFalse(((version & (1U << 9)) != 0), sample.RelativePath);
        Assert.IsFalse(((version & (1U << 11)) != 0), sample.RelativePath);
        Assert.IsFalse(((version & (1U << 12)) != 0), sample.RelativePath);
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
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(path, out uint version));
        Assert.IsTrue(((version & (1U << 12)) != 0));
    }

    [TestMethod(DisplayName = "Beachball/multipart.0001.exr|Load")]
    public void Case_Beachball_multipart_0001_exr_Load()
    {
        string path = TestPaths.OpenExr("Beachball/multipart.0001.exr");
        (uint version, Header[] headers, Image images) = ExrTestHelper.LoadMultipart(path);

        Assert.IsTrue(((version & (1U << 12)) != 0));
        Assert.AreEqual(10, headers.Length);
        Assert.AreEqual(10, images.Parts.Count);
    }

    [TestMethod(DisplayName = "Beachbal multiparts")]
    public void Case_Beachbal_multiparts()
    {
        foreach (string relativePath in Enumerable.Range(1, 8).Select(static i => $"Beachball/multipart.{i:0000}.exr"))
        {
            string path = TestPaths.OpenExr(relativePath);
            (uint version, Header[] headers, Image images) = ExrTestHelper.LoadMultipart(path);

            Assert.IsTrue(((version & (1U << 12)) != 0), relativePath);
            Assert.AreEqual(10, headers.Length, relativePath);
            Assert.AreEqual(10, images.Parts.Count, relativePath);
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
        string path = TestPaths.DeepScanline;

        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(path, out uint version));
        Assert.IsFalse(((version & (1U << 9)) != 0));
        Assert.IsTrue(((version & (1U << 11)) != 0));
        Assert.IsFalse(((version & (1U << 12)) != 0));

        Assert.AreEqual(ExrResult.Success, Exr.LoadDeepEXR(path, out Part? image));
        Assert.IsNotNull(image);
        Assert.IsTrue(image!.Header.IsDeep);
        Assert.AreEqual(PartType.DeepScanline, image!.Header.PartType);
        Assert.IsNull(image.Header.Tiles);
        Assert.IsTrue(image.Width > 0);
        Assert.IsTrue(image.Height > 0);
        Assert.AreEqual(image.Width * image.Height, ((DeepLevel)image.Levels[0]).SampleCounts.Length);
        Assert.AreEqual(image.Header.Channels.Count, image.Levels[0].Channels.Count);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Stream read APIs match file APIs")]
    public void Case_StreamReadApis_Match_FileApis()
    {
        string singlePartPath = TestPaths.OpenExr("ScanLines/Desk.exr");
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(singlePartPath, out uint expectedVersion));
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromFile(singlePartPath, out _, out Header? expectedHeader));
        Assert.IsNotNull(expectedHeader);
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromFile(singlePartPath, out Part? expectedImage));
        Assert.IsNotNull(expectedImage);
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXR(singlePartPath, out float[] expectedRgba, out int expectedWidth, out int expectedHeight));

        using (FileStream versionStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromStream(versionStream, out uint streamVersion));
            Assert.AreEqual(0, versionStream.Position);
            Assert.AreEqual(((expectedVersion & (1U << 9)) != 0), ((streamVersion & (1U << 9)) != 0));
            Assert.AreEqual(((expectedVersion & (1U << 12)) != 0), ((streamVersion & (1U << 12)) != 0));
            Assert.AreEqual(((expectedVersion & (1U << 11)) != 0), ((streamVersion & (1U << 11)) != 0));
        }

        using (FileStream headerStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromStream(headerStream, out _, out Header? streamHeader));
            Assert.IsNotNull(streamHeader);
            Assert.AreEqual(0, headerStream.Position);
            ExrTestHelper.EqualHeaders(expectedHeader, streamHeader);
        }

        using (FileStream imageStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromStream(imageStream, out Part? streamImage));
            Assert.IsNotNull(streamImage);
            Assert.AreEqual(0, imageStream.Position);
            ExrTestHelper.EqualImages(expectedImage, streamImage);
        }

        using (FileStream rgbaStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRFromStream(rgbaStream, out float[] streamRgba, out int streamWidth, out int streamHeight));
            Assert.AreEqual(0, rgbaStream.Position);
            Assert.AreEqual(expectedWidth, streamWidth);
            Assert.AreEqual(expectedHeight, streamHeight);
            CollectionAssert.AreEqual(expectedRgba, streamRgba);
        }

        using (FileStream layersStream = File.OpenRead(singlePartPath))
        {
            Assert.AreEqual(ExrResult.Success, Exr.EXRLayers(singlePartPath, out string[] expectedLayers));
            Assert.AreEqual(ExrResult.Success, Exr.EXRLayersFromStream(layersStream, out string[] streamLayers));
            Assert.AreEqual(0, layersStream.Position);
            CollectionAssert.AreEqual(expectedLayers, streamLayers);
        }

        string multipartPath = TestPaths.OpenExr("Beachball/multipart.0001.exr");
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRMultipartHeaderFromFile(multipartPath, out _, out Header[] expectedMultipartHeaders));
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRMultipartImageFromFile(multipartPath, out Image? expectedMultipartImages));
        Assert.IsNotNull(expectedMultipartImages);

        using (FileStream multipartHeaderStream = File.OpenRead(multipartPath))
        {
            Assert.AreEqual(ExrResult.Success, Exr.ParseEXRMultipartHeaderFromStream(multipartHeaderStream, out uint streamVersion, out Header[] streamHeaders));
            Assert.AreEqual(0, multipartHeaderStream.Position);
            Assert.IsTrue(((streamVersion & (1U << 12)) != 0));
            Assert.AreEqual(expectedMultipartHeaders.Length, streamHeaders.Length);
        }

        using (FileStream multipartImageStream = File.OpenRead(multipartPath))
        {
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRMultipartImageFromStream(multipartImageStream, out Image? streamImages));
            Assert.IsNotNull(streamImages);
            Assert.AreEqual(0, multipartImageStream.Position);
            Assert.AreEqual(expectedMultipartImages.Parts.Count, streamImages.Parts.Count);
        }

        string deepPath = TestPaths.DeepScanline;
        Assert.AreEqual(ExrResult.Success, Exr.LoadDeepEXR(deepPath, out Part? expectedDeepImage));
        Assert.IsNotNull(expectedDeepImage);
        using (FileStream deepStream = File.OpenRead(deepPath))
        {
            Assert.AreEqual(ExrResult.Success, Exr.LoadDeepEXRFromStream(deepStream, out Part? streamDeepImage));
            Assert.IsNotNull(streamDeepImage);
            Assert.AreEqual(0, deepStream.Position);
            ExrTestHelper.EqualHeaders(expectedDeepImage!.Header, streamDeepImage!.Header);
            Assert.AreEqual(expectedDeepImage.Width, streamDeepImage.Width);
            Assert.AreEqual(expectedDeepImage.Height, streamDeepImage.Height);
            Assert.AreEqual(expectedDeepImage.Header.Channels.Count, streamDeepImage.Header.Channels.Count);
        }


    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Stream APIs require seekable streams")]
    public void Case_StreamReadApis_RequireSeekableStreams()
    {
        byte[] data = File.ReadAllBytes(TestPaths.Asakusa);
        using NonSeekableReadStream stream = new(data);

        Assert.AreEqual(ExrResult.InvalidArgument, Exr.ParseEXRVersionFromStream(stream, out _));
        Assert.IsFalse(Exr.IsEXRFromStream(stream));
        Assert.AreEqual(ExrResult.InvalidArgument, Exr.ParseEXRHeaderFromStream(stream, out _, out _));
        Assert.AreEqual(ExrResult.InvalidArgument, Exr.LoadEXRFromStream(stream, out _, out _, out _));
    }

    private static void AssertSinglePartLoads(string relativePath)
    {
        string path = TestPaths.OpenExr(relativePath);

        Assert.IsTrue(Exr.IsEXR(path), relativePath);

        (uint version, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsFalse(((version & (1U << 9)) != 0), relativePath);
        Assert.IsFalse(((version & (1U << 11)) != 0), relativePath);
        Assert.IsFalse(((version & (1U << 12)) != 0), relativePath);
        Assert.IsNull(header.Tiles, relativePath);
        Assert.AreEqual(1, image.Levels.Count, relativePath);
        Assert.IsFalse(image.Header.IsTiled, relativePath);
        Assert.AreEqual(header.DataWindow.Width, image.Width, relativePath);
        Assert.AreEqual(header.DataWindow.Height, image.Height, relativePath);
        Assert.IsTrue(image.Header.Channels.Count > 0, relativePath);
    }

    private static void AssertTiledSinglePartLoads(string relativePath)
    {
        string path = TestPaths.OpenExr(relativePath);
        (uint version, _, Part image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(((version & (1U << 9)) != 0));
        Assert.IsFalse(((version & (1U << 11)) != 0));
        Assert.IsFalse(((version & (1U << 12)) != 0));
        Assert.IsTrue(image.Header.IsTiled && image.Levels[0].Channels.Count > 0);
    }

    private static void AssertSinglePartSampleMetadata(ExpectedSinglePartSample sample)
    {
        string path = TestPaths.OpenExr(sample.RelativePath);

        Assert.IsTrue(Exr.IsEXR(path), sample.RelativePath);

        (uint version, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsFalse(((version & (1U << 9)) != 0), sample.RelativePath);
        Assert.IsFalse(((version & (1U << 11)) != 0), sample.RelativePath);
        Assert.IsFalse(((version & (1U << 12)) != 0), sample.RelativePath);
        Assert.AreEqual(sample.Channels, header.Channels.Count, sample.RelativePath);
        AssertBox(sample.DataWindow, header.DataWindow, sample.RelativePath);
        AssertBox(sample.DisplayWindow, header.DisplayWindow, sample.RelativePath);
        Assert.AreEqual(sample.DataWindow.Width, image.Width, sample.RelativePath);
        Assert.AreEqual(sample.DataWindow.Height, image.Height, sample.RelativePath);
    }

    private static void AssertBox(Box2i expected, Box2i actual, string message)
    {
        Assert.AreEqual(expected.MinX, actual.MinX, message);
        Assert.AreEqual(expected.MinY, actual.MinY, message);
        Assert.AreEqual(expected.MaxX, actual.MaxX, message);
        Assert.AreEqual(expected.MaxY, actual.MaxY, message);
    }

    private static int ComputeExpectedMipLevelCount(long width, long height, TileRoundingMode roundingMode)
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

    private static long NextMipDimension(long dimension, TileRoundingMode roundingMode)
    {
        if (dimension <= 1)
        {
            return 1;
        }

        return roundingMode == TileRoundingMode.RoundUp
            ? (dimension + 1) / 2
            : dimension / 2;
    }

    private readonly record struct ExpectedSinglePartSample(
        string RelativePath,
        int Channels,
        Box2i DataWindow,
        Box2i DisplayWindow);

    private readonly record struct ExpectedTiledMipSample(
        string RelativePath,
        int Channels,
        Box2i DataWindow,
        TileRoundingMode RoundingMode);

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
