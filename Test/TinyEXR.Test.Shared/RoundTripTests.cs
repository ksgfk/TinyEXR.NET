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
        (uint _, Header[] headers1, Image images1) = ExrTestHelper.LoadMultipart(path);

        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRMultipartImageToMemory(images1, out byte[] encoded));
        Assert.IsTrue(encoded.Length > 0);
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromMemory(encoded, out uint version2));
        Assert.IsTrue(((version2 & (1U << 12)) != 0));
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRMultipartHeaderFromMemory(encoded, out _, out Header[] headers2));
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRMultipartImageFromMemory(encoded, out Image? images2));
        Assert.IsNotNull(images2);

        Assert.AreEqual(headers1.Length, headers2.Length);
        Assert.AreEqual(images1.Parts.Count, images2.Parts.Count);
        for (int i = 0; i < headers1.Length; i++)
        {
            ExrTestHelper.EqualHeaders(headers1[i], headers2[i]);
            ExrTestHelper.EqualImages(images1.Parts[i], images2.Parts[i]);
        }
    }

    [TestMethod(DisplayName = "Saving multipart|Combine")]
    public void Case_Saving_multipart_Combine()
    {
        List<Header> headers1 = new();
        List<Part> images1 = new();
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
            (uint _, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);
            Assert.AreEqual(ExrResult.Success, Exr.EXRSetNameAttr(header, relativePath, out Header? named));
            Assert.IsNotNull(named);
            header = new Header(named!.PartType, named.DataWindow, named.Channels, named.Compression,
                named.LineOrder, new Box2i(0, 0, 2047, 2047), 1, named.ScreenWindowCenter,
                named.ScreenWindowWidth, named.Tiles, named.Name, attributes: named.Attributes.Where(a => a.Name != "timeCode").ToArray());
            image = new Part(header, image.Levels, image.IsComplete);
            headers1.Add(header);
            images1.Add(image);
        }

        Image multipartImages = new(images1);
        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRMultipartImageToMemory(multipartImages, out byte[] encoded));
        Assert.IsTrue(encoded.Length > 0);
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromMemory(encoded, out uint version2));
        Assert.IsTrue(((version2 & (1U << 12)) != 0));
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRMultipartHeaderFromMemory(encoded, out _, out Header[] headers2));
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRMultipartImageFromMemory(encoded, out Image? images2));
        Assert.IsNotNull(images2);

        Assert.AreEqual(headers1.Count, headers2.Length);
        Assert.AreEqual(images1.Count, images2.Parts.Count);
        for (int i = 0; i < headers1.Count; i++)
        {
            ExrTestHelper.EqualHeaders(headers1[i], headers2[i], ignoreMultipartState: true);
            ExrTestHelper.EqualImages(images1[i], images2.Parts[i]);
        }
    }

    private static void AssertSinglePartRoundTrip(string relativePath)
    {
        string path = TestPaths.OpenExr(relativePath);
        (uint _, Header header1, Part image1) = ExrTestHelper.LoadSinglePart(path);

        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(image1, out byte[] encoded), relativePath);
        Assert.IsTrue(encoded.Length > 0, relativePath);
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromMemory(encoded, out _), relativePath);
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out Header? header2), relativePath);
        Assert.IsNotNull(header2);
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromMemory(encoded, out Part? image2), relativePath);
        Assert.IsNotNull(image2);

        ExrTestHelper.EqualHeaders(header1, header2);
        ExrTestHelper.EqualImages(image1, image2);
    }
}
