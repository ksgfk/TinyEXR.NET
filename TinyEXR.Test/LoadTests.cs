namespace TinyEXR.Test;

[TestClass]
public sealed class LoadTests
{
    [TestMethod]
    public void Asakusa_can_parse_header()
    {
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(TestPaths.Asakusa, out ExrVersion version));
        Assert.IsFalse(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(TestPaths.Asakusa, out _, out ExrHeader header));
        Assert.IsTrue(header.Channels.Count > 0);
    }

    [TestMethod]
    public void Unicode_regression_filename_can_parse_header()
    {
        string path = TestPaths.Regression("日本語.exr");
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out _));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header));
        Assert.IsTrue(header.Channels.Count > 0);
    }

    [TestMethod]
    public void Single_part_openexr_images_load()
    {
        foreach (object[] row in ExrTestData.SinglePartImageFiles())
        {
            string relativePath = (string)row[0];
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
    }

    [TestMethod]
    public void Subsampled_chroma_images_load_with_sampled_channel_buffers()
    {
        foreach (object[] row in ExrTestData.SubsampledChromaImageFiles())
        {
            string relativePath = (string)row[0];
            string path = TestPaths.OpenExr(relativePath);
            (_, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

            ExrImageChannel y = image.GetChannel("Y");
            ExrImageChannel ry = image.GetChannel("RY");
            ExrImageChannel by = image.GetChannel("BY");

            Assert.AreEqual(1, y.Channel.SamplingX, relativePath);
            Assert.AreEqual(1, y.Channel.SamplingY, relativePath);
            Assert.AreEqual(2, ry.Channel.SamplingX, relativePath);
            Assert.AreEqual(2, ry.Channel.SamplingY, relativePath);
            Assert.AreEqual(2, by.Channel.SamplingX, relativePath);
            Assert.AreEqual(2, by.Channel.SamplingY, relativePath);

            int fullSampleCount = image.Width * image.Height;
            int chromaSampleCount = ((image.Width + 1) / 2) * ((image.Height + 1) / 2);
            Assert.AreEqual(fullSampleCount * sizeof(ushort), y.Data.Length, relativePath);
            Assert.AreEqual(chromaSampleCount * sizeof(ushort), ry.Data.Length, relativePath);
            Assert.AreEqual(chromaSampleCount * sizeof(ushort), by.Data.Length, relativePath);
            Assert.IsTrue(Array.Exists(y.Data, static value => value != 0), relativePath);
            Assert.IsTrue(Array.Exists(ry.Data, static value => value != 0), relativePath);
            Assert.IsTrue(Array.Exists(by.Data, static value => value != 0), relativePath);
            Assert.AreEqual(header.DataWindow.Width, image.Width, relativePath);
            Assert.AreEqual(header.DataWindow.Height, image.Height, relativePath);
        }
    }

    [TestMethod]
    public void GoldenGate_header_reports_single_tile_level()
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

    [TestMethod]
    public void Garden_tiled_image_loads()
    {
        string path = TestPaths.OpenExr("LuminanceChroma/Garden.exr");
        (ExrVersion version, _, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.IsTrue(image.Levels[0].Tiles.Count > 0);
    }

    [TestMethod]
    public void Ocean_tiled_image_loads()
    {
        string path = TestPaths.OpenExr("Tiles/Ocean.exr");
        (ExrVersion version, _, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.IsTrue(image.Levels[0].Tiles.Count > 0);
    }

    [TestMethod]
    public void Bonita_loads_all_mipmap_levels()
    {
        string path = TestPaths.OpenExr("MultiResolution/Bonita.exr");
        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(ExrTileLevelMode.MipMapLevels, header.Tiles!.LevelMode);
        Assert.AreEqual(ExrTileRoundingMode.RoundDown, header.Tiles.RoundingMode);
        Assert.AreEqual(10, Exr.EXRNumLevels(image));
    }

    [TestMethod]
    public void Kapaa_loads_all_ripmap_levels()
    {
        string path = TestPaths.OpenExr("MultiResolution/Kapaa.exr");
        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(ExrTileLevelMode.RipMapLevels, header.Tiles!.LevelMode);
        Assert.AreEqual(ExrTileRoundingMode.RoundUp, header.Tiles.RoundingMode);
        Assert.AreEqual(64, header.Tiles.TileSizeX);
        Assert.AreEqual(64, header.Tiles.TileSizeY);
        Assert.AreEqual(11 * 11, Exr.EXRNumLevels(image));
    }

    [TestMethod]
    public void Spirals_pxr24_tiled_image_loads()
    {
        string path = TestPaths.OpenExr("Tiles/Spirals.exr");
        (ExrVersion version, _, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.IsTrue(image.Levels[0].Tiles.Count > 0);
    }

    [TestMethod]
    public void Multipart_beachball_0001_loads_all_parts()
    {
        string path = TestPaths.OpenExr("Beachball/multipart.0001.exr");
        (ExrVersion version, ExrMultipartHeader headers, ExrMultipartImage images) = ExrTestHelper.LoadMultipart(path);

        Assert.IsTrue(version.Multipart);
        Assert.AreEqual(10, headers.Headers.Count);
        Assert.AreEqual(10, images.Images.Count);
    }

    [TestMethod]
    public void Multipart_beachball_frames_load()
    {
        foreach (object[] row in ExrTestData.MultipartFrames())
        {
            string relativePath = (string)row[0];
            string path = TestPaths.OpenExr(relativePath);
            (ExrVersion version, ExrMultipartHeader headers, ExrMultipartImage images) = ExrTestHelper.LoadMultipart(path);

            Assert.IsTrue(version.Multipart, relativePath);
            Assert.AreEqual(10, headers.Headers.Count, relativePath);
            Assert.AreEqual(10, images.Images.Count, relativePath);
        }
    }

    [TestMethod]
    public void Missing_beachball_frames_return_cannot_open_file()
    {
        Assert.AreEqual(
            ResultCode.CannotOpenFile,
            Exr.ParseEXRVersionFromFile(TestPaths.OpenExrImagesRoot + Path.DirectorySeparatorChar + "Beachball" + Path.DirectorySeparatorChar + "multipart.0000.exr", out _));

        Assert.AreEqual(
            ResultCode.CannotOpenFile,
            Exr.ParseEXRVersionFromFile(TestPaths.OpenExrImagesRoot + Path.DirectorySeparatorChar + "Beachball" + Path.DirectorySeparatorChar + "singlepart.0000.exr", out _));
    }
}
