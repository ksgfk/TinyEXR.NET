namespace TinyEXR.Test;

[TestClass]
public sealed class MultiViewTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] MultiView|LayersAndDefaultLoad")]
    public void Case_MultiView_LayersAndDefaultLoad()
    {
        foreach (ExpectedMultiViewSample sample in new[]
        {
            new ExpectedMultiViewSample(
                "MultiView/Adjuster.exr",
                ExpectedLayers: new[] { "left", "right" },
                ExpectedRootChannels: new[] { "B", "G", "R" }),
            new ExpectedMultiViewSample(
                "MultiView/Balls.exr",
                ExpectedLayers: new[] { "right" },
                ExpectedRootChannels: new[] { "B", "G", "R" }),
            new ExpectedMultiViewSample(
                "MultiView/Fog.exr",
                ExpectedLayers: new[] { "right" },
                ExpectedRootChannels: new[] { "Y" }),
            new ExpectedMultiViewSample(
                "MultiView/Impact.exr",
                ExpectedLayers: new[] { "right" },
                ExpectedRootChannels: new[] { "B", "G", "R" }),
            new ExpectedMultiViewSample(
                "MultiView/LosPadres.exr",
                ExpectedLayers: new[] { "left" },
                ExpectedRootChannels: new[] { "B", "G", "R" }),
        })
        {
            string path = TestPaths.OpenExr(sample.RelativePath);

            Assert.IsTrue(Exr.IsEXR(path), sample.RelativePath);
            Assert.AreEqual(ResultCode.Success, Exr.EXRLayers(path, out string[] layers), sample.RelativePath);
            CollectionAssert.AreEqual(sample.ExpectedLayers, layers, sample.RelativePath);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version), sample.RelativePath);
            Assert.IsFalse(version.NonImage, sample.RelativePath);
            Assert.IsFalse(version.Multipart, sample.RelativePath);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header), sample.RelativePath);
            CollectionAssert.AreEqual(
                sample.ExpectedRootChannels,
                header.Channels
                    .Where(static channel => channel.Name.IndexOf('.') < 0)
                    .Select(static channel => channel.Name)
                    .ToArray(),
                sample.RelativePath);

            Assert.AreEqual(ResultCode.Success, Exr.LoadEXR(path, out float[] defaultRgba, out int defaultWidth, out int defaultHeight), sample.RelativePath);
            Assert.AreEqual(header.DataWindow.Width, defaultWidth, sample.RelativePath);
            Assert.AreEqual(header.DataWindow.Height, defaultHeight, sample.RelativePath);
            Assert.AreEqual(defaultWidth * defaultHeight * 4, defaultRgba.Length, sample.RelativePath);

            foreach (string layer in layers)
            {
                Assert.AreEqual(
                    ResultCode.Success,
                    Exr.LoadEXRWithLayer(path, layer, out float[] layerRgba, out int layerWidth, out int layerHeight),
                    $"{sample.RelativePath}|{layer}");
                Assert.AreEqual(defaultWidth, layerWidth, $"{sample.RelativePath}|{layer}");
                Assert.AreEqual(defaultHeight, layerHeight, $"{sample.RelativePath}|{layer}");
                Assert.AreEqual(layerWidth * layerHeight * 4, layerRgba.Length, $"{sample.RelativePath}|{layer}");
                AssertRgbasDifferent(defaultRgba, layerRgba, $"{sample.RelativePath}|default-vs-{layer}");
            }

            Assert.AreEqual(
                ResultCode.LayerNotFound,
                Exr.LoadEXRWithLayer(path, "__missing__", out _, out _, out _),
                sample.RelativePath);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] v2/LeftView|LoadDeep")]
    public void Case_v2_LeftView_LoadDeep()
    {
        foreach (ExpectedDeepSample sample in new[]
        {
            new ExpectedDeepSample("v2/LeftView/Balls.exr", Width: 1431, Height: 761),
            new ExpectedDeepSample("v2/LeftView/Ground.exr", Width: 1920, Height: 741),
            new ExpectedDeepSample("v2/LeftView/Leaves.exr", Width: 1920, Height: 1080),
            new ExpectedDeepSample("v2/LeftView/Trunks.exr", Width: 1920, Height: 814),
        })
        {
            AssertDeepSinglePartSample(sample);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] v2/LowResLeftView|LoadDeep")]
    public void Case_v2_LowResLeftView_LoadDeep()
    {
        foreach (ExpectedDeepSample sample in new[]
        {
            new ExpectedDeepSample("v2/LowResLeftView/Balls.exr", Width: 764, Height: 406),
            new ExpectedDeepSample("v2/LowResLeftView/Ground.exr", Width: 1024, Height: 396),
            new ExpectedDeepSample("v2/LowResLeftView/Leaves.exr", Width: 1024, Height: 576),
            new ExpectedDeepSample("v2/LowResLeftView/Trunks.exr", Width: 1024, Height: 435),
        })
        {
            AssertDeepSinglePartSample(sample);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] v2/LowResLeftView/composited.exr|Load")]
    public void Case_v2_LowResLeftView_composited_exr_Load()
    {
        string path = TestPaths.OpenExr("v2/LowResLeftView/composited.exr");

        Assert.IsTrue(Exr.IsEXR(path));
        Assert.AreEqual(ResultCode.Success, Exr.EXRLayers(path, out string[] layers));
        Assert.AreEqual(0, layers.Length);

        (ExrVersion version, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsFalse(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        CollectionAssert.AreEqual(
            new[] { "A", "B", "G", "R" },
            header.Channels.Select(static channel => channel.Name).ToArray());
        Assert.AreEqual(1022, image.Width);
        Assert.AreEqual(574, image.Height);
        Assert.AreEqual(1, Exr.EXRNumLevels(image));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] v2/Stereo/composited.exr|Multipart")]
    public void Case_v2_Stereo_composited_exr_Multipart()
    {
        string path = TestPaths.OpenExr("v2/Stereo/composited.exr");

        Assert.IsTrue(Exr.IsEXR(path));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version));
        Assert.IsFalse(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsTrue(version.Multipart);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromFile(path, out _, out ExrMultipartHeader headers));
        Assert.AreEqual(4, headers.Headers.Count);
        CollectionAssert.AreEqual(
            new[] { "rgba.left", "depth.left", "rgba.right", "depth.right" },
            headers.Headers.Select(static header => header.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "scanlineimage", "scanlineimage", "scanlineimage", "scanlineimage" },
            headers.Headers.Select(static header => header.PartType).ToArray());
        CollectionAssert.AreEqual(
            new[] { 4, 1, 4, 1 },
            headers.Headers.Select(static header => header.Channels.Count).ToArray());

        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromFile(path, headers, out ExrMultipartImage images));
        Assert.AreEqual(4, images.Images.Count);
        for (int index = 0; index < headers.Headers.Count; index++)
        {
            Assert.AreEqual(headers.Headers[index].DataWindow.Width, images.Images[index].Width, $"part[{index}]");
            Assert.AreEqual(headers.Headers[index].DataWindow.Height, images.Images[index].Height, $"part[{index}]");
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] v2/Stereo deep multipart|ParseHeaders")]
    public void Case_v2_Stereo_deep_multipart_ParseHeaders()
    {
        foreach (string relativePath in new[]
        {
            "v2/Stereo/Balls.exr",
            "v2/Stereo/Ground.exr",
            "v2/Stereo/Leaves.exr",
            "v2/Stereo/Trunks.exr",
        })
        {
            string path = TestPaths.OpenExr(relativePath);

            Assert.IsTrue(Exr.IsEXR(path), relativePath);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version), relativePath);
            Assert.IsFalse(version.Tiled, relativePath);
            Assert.IsTrue(version.NonImage, relativePath);
            Assert.IsTrue(version.Multipart, relativePath);

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromFile(path, out _, out ExrMultipartHeader headers), relativePath);
            Assert.AreEqual(2, headers.Headers.Count, relativePath);
            CollectionAssert.AreEqual(
                new[] { "rgba.left", "rgba.right" },
                headers.Headers.Select(static header => header.Name).ToArray(),
                relativePath);
            CollectionAssert.AreEqual(
                new[] { "deepscanline", "deepscanline" },
                headers.Headers.Select(static header => header.PartType).ToArray(),
                relativePath);
            foreach (ExrHeader header in headers.Headers)
            {
                CollectionAssert.AreEqual(
                    new[] { "A", "B", "G", "R", "Z" },
                    header.Channels.Select(static channel => channel.Name).ToArray(),
                    relativePath);
            }

            Assert.AreEqual(ResultCode.UnsupportedFeature, Exr.LoadEXRMultipartImageFromFile(path, headers, out ExrMultipartImage images), relativePath);
            Assert.AreEqual(0, images.Images.Count, relativePath);
        }
    }

    private static void AssertDeepSinglePartSample(ExpectedDeepSample sample)
    {
        string path = TestPaths.OpenExr(sample.RelativePath);

        Assert.IsTrue(Exr.IsEXR(path), sample.RelativePath);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version), sample.RelativePath);
        Assert.IsFalse(version.Tiled, sample.RelativePath);
        Assert.IsTrue(version.NonImage, sample.RelativePath);
        Assert.IsFalse(version.Multipart, sample.RelativePath);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader parsedHeader), sample.RelativePath);
        Assert.AreEqual("deepscanline", parsedHeader.PartType, sample.RelativePath);
        CollectionAssert.AreEqual(
            new[] { "A", "B", "G", "R", "Z" },
            parsedHeader.Channels.Select(static channel => channel.Name).ToArray(),
            sample.RelativePath);

        Assert.AreEqual(ResultCode.Success, Exr.LoadDeepEXR(path, out ExrHeader header, out ExrDeepImage image), sample.RelativePath);
        Assert.IsTrue(header.IsDeep, sample.RelativePath);
        Assert.AreEqual("deepscanline", header.PartType, sample.RelativePath);
        Assert.AreEqual(sample.Width, image.Width, sample.RelativePath);
        Assert.AreEqual(sample.Height, image.Height, sample.RelativePath);
        Assert.AreEqual(sample.Height, image.OffsetTable.Length, sample.RelativePath);
        Assert.AreEqual(header.Channels.Count, image.Channels.Count, sample.RelativePath);
        CollectionAssert.AreEqual(
            new[] { "A", "B", "G", "R", "Z" },
            image.Channels.Select(static channel => channel.Name).ToArray(),
            sample.RelativePath);
    }

    private static void AssertRgbasDifferent(float[] expected, float[] actual, string message)
    {
        Assert.AreEqual(expected.Length, actual.Length, message);
        Assert.IsFalse(expected.AsSpan().SequenceEqual(actual), message);
    }

    private readonly record struct ExpectedMultiViewSample(
        string RelativePath,
        string[] ExpectedLayers,
        string[] ExpectedRootChannels);

    private readonly record struct ExpectedDeepSample(
        string RelativePath,
        int Width,
        int Height);
}
