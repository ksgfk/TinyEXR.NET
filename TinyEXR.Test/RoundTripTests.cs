namespace TinyEXR.Test;

[TestClass]
public sealed class RoundTripTests
{
    [TestMethod]
    public void Scanline_images_round_trip_through_memory()
    {
        foreach (object[] row in ExrTestData.ScanlineRoundTripFiles())
        {
            string relativePath = (string)row[0];
            string path = TestPaths.OpenExr(relativePath);
            (ExrVersion _, ExrHeader header1, ExrImage image1) = ExrTestHelper.LoadSinglePart(path);

            ResultCode saveResult = Exr.SaveEXRImageToMemory(image1, header1, out byte[] encoded);
            if (header1.LineOrder != LineOrderType.IncreasingY)
            {
                Assert.AreEqual(ResultCode.UnsupportedFeature, saveResult, relativePath);
                continue;
            }

            Assert.AreEqual(ResultCode.Success, saveResult, relativePath);
            Assert.IsTrue(encoded.Length > 0, relativePath);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out _), relativePath);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader header2), relativePath);
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, header2, out ExrImage image2), relativePath);

            ExrTestHelper.EqualHeaders(header1, header2);
            ExrTestHelper.EqualImages(image1, image2);
        }
    }

    [TestMethod]
    public void Multi_resolution_images_round_trip_through_memory()
    {
        foreach (object[] row in ExrTestData.MultiResolutionRoundTripFiles())
        {
            string relativePath = (string)row[0];
            string path = TestPaths.OpenExr(relativePath);
            (ExrVersion _, ExrHeader header1, ExrImage image1) = ExrTestHelper.LoadSinglePart(path);

            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image1, header1, out byte[] encoded), relativePath);
            Assert.IsTrue(encoded.Length > 0, relativePath);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out _), relativePath);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader header2), relativePath);
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, header2, out ExrImage image2), relativePath);

            ExrTestHelper.EqualHeaders(header1, header2);
            ExrTestHelper.EqualImages(image1, image2);
        }
    }

    [TestMethod]
    public void Multipart_beachball_round_trips_through_memory()
    {
        string path = TestPaths.OpenExr("Beachball/multipart.0001.exr");
        (ExrVersion _, ExrMultipartHeader headers1, ExrMultipartImage images1) = ExrTestHelper.LoadMultipart(path);

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRMultipartImageToMemory(images1, headers1, out byte[] encoded));
        Assert.IsTrue(encoded.Length > 0);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out ExrVersion version2));
        Assert.IsTrue(version2.Multipart);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(encoded, out _, out ExrMultipartHeader headers2));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromMemory(encoded, headers2, out ExrMultipartImage images2));

        Assert.AreEqual(headers1.Headers.Count, headers2.Headers.Count);
        Assert.AreEqual(images1.Images.Count, images2.Images.Count);
        for (int i = 0; i < headers1.Headers.Count; i++)
        {
            ExrTestHelper.EqualHeaders(headers1.Headers[i], headers2.Headers[i]);
            ExrTestHelper.EqualImages(images1.Images[i], images2.Images[i]);
        }
    }

    [TestMethod]
    public void Mixed_single_part_images_can_be_combined_into_multipart()
    {
        List<ExrHeader> headers1 = new();
        List<ExrImage> images1 = new();

        foreach (object[] row in ExrTestData.MultipartCombineFiles())
        {
            string relativePath = (string)row[0];
            string path = TestPaths.OpenExr(relativePath);
            (ExrVersion _, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);
            Exr.EXRSetNameAttr(header, relativePath);
            headers1.Add(header);
            images1.Add(image);
        }

        ExrMultipartHeader multipartHeaders = new(headers1);
        ExrMultipartImage multipartImages = new(images1);
        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRMultipartImageToMemory(multipartImages, multipartHeaders, out byte[] encoded));
        Assert.IsTrue(encoded.Length > 0);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out ExrVersion version2));
        Assert.IsTrue(version2.Multipart);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(encoded, out _, out ExrMultipartHeader headers2));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromMemory(encoded, headers2, out ExrMultipartImage images2));

        Assert.AreEqual(headers1.Count, headers2.Headers.Count);
        Assert.AreEqual(images1.Count, images2.Images.Count);
        for (int i = 0; i < headers1.Count; i++)
        {
            ExrTestHelper.EqualHeaders(headers1[i], headers2.Headers[i], ignoreMultipartState: true);
            ExrTestHelper.EqualImages(images1[i], images2.Images[i]);
        }
    }

    [TestMethod]
    public void Zip_compressed_single_pixel_can_be_saved_and_loaded()
    {
        ExrImage image = new(
            1,
            1,
            new[]
            {
                ExrTestHelper.FloatChannel("B", ExrPixelType.Float, new[] { 0.0f }),
                ExrTestHelper.FloatChannel("G", ExrPixelType.Float, new[] { 0.0f }),
                ExrTestHelper.FloatChannel("R", ExrPixelType.Float, new[] { 1.0f }),
            });

        ExrHeader header = new()
        {
            Compression = CompressionType.ZIP,
        };

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        Assert.IsTrue(encoded.Length > 0);
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRFromMemory(encoded, out float[] rgba, out int width, out int height));
        Assert.AreEqual(1, width);
        Assert.AreEqual(1, height);
        CollectionAssert.AreEqual(new[] { 1.0f, 0.0f, 0.0f, 1.0f }, rgba);
    }
}
