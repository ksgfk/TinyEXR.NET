namespace TinyEXR.Test;

[TestClass]
public sealed class RegressionTests
{
    [TestMethod]
    public void ParseEXRVersionFromMemory_rejects_invalid_input()
    {
        Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.ParseEXRVersionFromMemory(ReadOnlySpan<byte>.Empty, out _));
        Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.ParseEXRVersionFromMemory(new byte[1], out _));
        Assert.AreEqual(ResultCode.InvalidMagicNumver, Exr.ParseEXRVersionFromMemory(new byte[8], out _));
    }

    [TestMethod]
    public void ParseEXRHeaderFromMemory_rejects_invalid_input()
    {
        Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.ParseEXRHeaderFromMemory(ReadOnlySpan<byte>.Empty, out _, out _));
        Assert.AreEqual(ResultCode.InvalidMagicNumver, Exr.ParseEXRHeaderFromMemory(new byte[128], out _, out _));

        byte[] truncatedHeader = new byte[24];
        Buffer.BlockCopy(File.ReadAllBytes(TestPaths.Asakusa), 0, truncatedHeader, 0, 8);
        Assert.AreEqual(ResultCode.InvalidHeader, Exr.ParseEXRHeaderFromMemory(truncatedHeader, out _, out _));
    }

    [TestMethod]
    public void Fuzzed_regression_headers_are_rejected()
    {
        foreach (object[] row in ExrTestData.FuzzedHeaderFiles())
        {
            string fileName = (string)row[0];
            string path = TestPaths.Regression(fileName);

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version), fileName);
            Assert.IsFalse(version.Tiled, fileName);
            Assert.IsFalse(version.NonImage, fileName);
            Assert.IsFalse(version.Multipart, fileName);

            ResultCode headerResult = Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header);
            if (headerResult == ResultCode.Success)
            {
                Assert.AreNotEqual(ResultCode.Success, Exr.LoadEXRImageFromFile(path, header, out _), fileName);
            }
            else
            {
                Assert.AreNotEqual(ResultCode.Success, headerResult, fileName);
            }
        }
    }

    [TestMethod]
    public void Issue71_loadexr_returns_expected_pixels()
    {
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXR(TestPaths.Regression("2by2.exr"), out float[] image, out int width, out int height));
        Assert.AreEqual(2, width);
        Assert.AreEqual(2, height);

        Assert.AreEqual(0.0f, image[8], 0.000001f);
        Assert.AreEqual(0.447021f, image[9], 0.000001f);
        Assert.AreEqual(1.0f, image[10], 0.000001f);
        Assert.AreEqual(0.250977f, image[11], 0.000001f);
        Assert.AreEqual(0.0f, image[12], 0.000001f);
        Assert.AreEqual(0.0f, image[13], 0.000001f);
        Assert.AreEqual(0.0f, image[14], 0.000001f);
        Assert.AreEqual(1.0f, image[15], 0.000001f);
    }

    [TestMethod]
    public void Issue93_tiled_load_from_memory_works()
    {
        byte[] data = File.ReadAllBytes(TestPaths.OpenExr("Tiles/GoldenGate.exr"));

        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRFromMemory(data, out float[] image, out _, out _));
        Assert.AreEqual(0.0612183f, image[0], 0.000001f);
        Assert.AreEqual(0.0892334f, image[1], 0.000001f);
        Assert.AreEqual(0.271973f, image[2], 0.000001f);
    }

    [TestMethod]
    public void Issue100_piz_bug_image_has_expected_edge_pixels()
    {
        byte[] data = File.ReadAllBytes(TestPaths.Regression("piz-bug-issue-100.exr"));

        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRFromMemory(data, out float[] image, out int width, out int height));
        Assert.AreEqual(35, width);
        Assert.AreEqual(1, height);

        Assert.AreEqual(0.0f, image[0], 0.000001f);
        Assert.AreEqual(0.0f, image[1], 0.000001f);
        Assert.AreEqual(0.0f, image[2], 0.000001f);
        Assert.AreEqual(0.0f, image[3], 0.000001f);

        int lastPixelOffset = 4 * 34;
        Assert.AreEqual(1.0f, image[lastPixelOffset + 0], 0.000001f);
        Assert.AreEqual(1.0f, image[lastPixelOffset + 1], 0.000001f);
        Assert.AreEqual(1.0f, image[lastPixelOffset + 2], 0.000001f);
        Assert.AreEqual(1.0f, image[lastPixelOffset + 3], 0.000001f);
    }

    [TestMethod]
    public void Issue53_layer_listing_and_layer_loading_work()
    {
        string path = TestPaths.Regression("flaga.exr");

        Assert.AreEqual(ResultCode.Success, Exr.EXRLayers(path, out string[] layers));
        Assert.AreEqual(2, layers.Length);
        CollectionAssert.Contains(layers, "Warstwa 1");
        CollectionAssert.Contains(layers, "Warstwa 2");

        Assert.AreEqual(ResultCode.LayerNotFound, Exr.LoadEXRWithLayer(path, layer: null, out _, out _, out _));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRWithLayer(path, "Warstwa 1", out float[] image, out int width, out int height));
        Assert.IsTrue(image.Length > 0);
        Assert.IsTrue(width > 0);
        Assert.IsTrue(height > 0);
    }

    [TestMethod]
    public void Missing_layer_returns_layer_not_found()
    {
        Assert.AreEqual(
            ResultCode.LayerNotFound,
            Exr.LoadEXRWithLayer(TestPaths.OpenExr("MultiView/Impact.exr"), "Warstwa", out _, out _, out _));
    }

    [TestMethod]
    public void Issue150_tiled_half_1x1_alpha_loads()
    {
        string path = TestPaths.Regression("tiled_half_1x1_alpha.exr");
        (ExrVersion version, _, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.AreEqual(1, image.Levels.Count);
    }

    [TestMethod]
    public void Issue194_piz_regression_loads()
    {
        string path = TestPaths.Regression("000-issue194.exr");
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromFile(path, header, out ExrImage image));
        Assert.IsTrue(image.Width > 0);
        Assert.IsTrue(image.Height > 0);
    }

    [TestMethod]
    public void Issue238_single_part_failure_is_repeatable_and_nonfatal()
    {
        byte[] data = File.ReadAllBytes(TestPaths.Regression("issue-238-double-free.exr"));

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(data, out _, out ExrHeader header));

        for (int i = 0; i < 2; i++)
        {
            ResultCode lowLevelResult = Exr.LoadEXRImageFromMemory(data, header, out ExrImage image);
            Assert.AreNotEqual(ResultCode.Success, lowLevelResult);
            Assert.IsTrue(image.Width >= 0);
            Assert.IsTrue(image.Height >= 0);

            ResultCode highLevelResult = Exr.LoadEXRFromMemory(data, out float[] rgba, out int width, out int height);
            Assert.AreNotEqual(ResultCode.Success, highLevelResult);
            Assert.IsTrue(rgba.Length >= 0);
            Assert.IsTrue(width >= 0);
            Assert.IsTrue(height >= 0);
        }
    }

    [TestMethod]
    public void Issue238_multipart_failure_is_repeatable_and_nonfatal()
    {
        byte[] data = File.ReadAllBytes(TestPaths.Regression("issue-238-double-free-multipart.exr"));

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(data, out ExrVersion version));
        Assert.IsTrue(version.Multipart);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(data, out _, out ExrMultipartHeader headers));
        Assert.AreEqual(2, headers.Headers.Count);

        for (int i = 0; i < 2; i++)
        {
            ResultCode result = Exr.LoadEXRMultipartImageFromMemory(data, headers, out ExrMultipartImage images);
            Assert.AreNotEqual(ResultCode.Success, result);
            Assert.AreEqual(0, images.Images.Count);
        }
    }

    [TestMethod]
    public void Issue160_piz_regression_preserves_expected_pixels()
    {
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXR(TestPaths.Regression("issue-160-piz-decode.exr"), out float[] image, out int width, out int height));
        Assert.AreEqual(420, width);
        Assert.AreEqual(32, height);

        Assert.AreEqual(1.0f, image[(24 * width + 410) * 4 + 0], 0.000001f);
        Assert.AreEqual(1.0f, image[(24 * width + 410) * 4 + 1], 0.000001f);
        Assert.AreEqual(1.0f, image[(24 * width + 410) * 4 + 2], 0.000001f);

        Assert.AreEqual(1.0f, image[(28 * width + 412) * 4 + 0], 0.000001f);
        Assert.AreEqual(1.0f, image[(28 * width + 412) * 4 + 1], 0.000001f);
        Assert.AreEqual(1.0f, image[(28 * width + 412) * 4 + 2], 0.000001f);

        Assert.AreEqual(1.0f, image[(28 * width + 418) * 4 + 0], 0.000001f);
        Assert.AreEqual(1.0f, image[(28 * width + 418) * 4 + 1], 0.000001f);
        Assert.AreEqual(1.0f, image[(28 * width + 418) * 4 + 2], 0.000001f);

        Assert.AreEqual(0.0f, image[(30 * width + 417) * 4 + 0], 0.000001f);
        Assert.AreEqual(1.0f, image[(30 * width + 417) * 4 + 1], 0.000001f);
        Assert.AreEqual(1.0f, image[(30 * width + 417) * 4 + 2], 0.000001f);
    }
}
