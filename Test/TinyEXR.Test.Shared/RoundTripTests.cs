namespace TinyEXR.Test;

[TestClass]
public sealed class RoundTripTests
{
    [TestMethod(DisplayName = "Saving ScanLines")]
    public void Case_Saving_ScanLines()
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
            AssertSinglePartRoundTrip(relativePath);
        }
    }

    [TestMethod(DisplayName = "Saving MultiResolution")]
    public void Case_Saving_MultiResolution()
    {
        foreach (string relativePath in new[]
        {
            "MultiResolution/Bonita.exr",
            "MultiResolution/Kapaa.exr",
        })
        {
            AssertSinglePartRoundTrip(relativePath);
        }
    }

    [TestMethod(DisplayName = "Saving multipart")]
    public void Case_Saving_multipart()
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

    [TestMethod(DisplayName = "Saving multipart|Combine")]
    public void Case_Saving_multipart_Combine()
    {
        List<ExrHeader> headers1 = new();
        List<ExrImage> images1 = new();
        string[] relativePaths =
        {
            "MultiResolution/Kapaa.exr",
            "Tiles/GoldenGate.exr",
            "ScanLines/Desk.exr",
            "MultiResolution/PeriodicPattern.exr",
        };

        foreach (string relativePath in relativePaths)
        {
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

    private static void AssertSinglePartRoundTrip(string relativePath)
    {
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
