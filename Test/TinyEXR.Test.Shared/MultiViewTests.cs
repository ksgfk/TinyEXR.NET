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
            Assert.AreEqual(ExrResult.Success, Exr.EXRLayers(path, out string[] layers), sample.RelativePath);
            CollectionAssert.AreEqual(sample.ExpectedLayers, layers, sample.RelativePath);
            Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(path, out uint version), sample.RelativePath);
            Assert.IsFalse(((version & (1U << 11)) != 0), sample.RelativePath);
            Assert.IsFalse(((version & (1U << 12)) != 0), sample.RelativePath);
            Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromFile(path, out _, out Header? header), sample.RelativePath);
            Assert.IsNotNull(header);
            CollectionAssert.AreEqual(
                sample.ExpectedRootChannels,
                header.Channels
                    .Where(static channel => channel.Name.IndexOf('.') < 0)
                    .Select(static channel => channel.Name)
                    .ToArray(),
                sample.RelativePath);

            Assert.AreEqual(ExrResult.Success, Exr.LoadEXR(path, out float[] defaultRgba, out int defaultWidth, out int defaultHeight), sample.RelativePath);
            Assert.AreEqual(header.DataWindow.Width, defaultWidth, sample.RelativePath);
            Assert.AreEqual(header.DataWindow.Height, defaultHeight, sample.RelativePath);
            Assert.AreEqual(defaultWidth * defaultHeight * 4, defaultRgba.Length, sample.RelativePath);

            foreach (string layer in layers)
            {
                Assert.AreEqual(
                    ExrResult.Success,
                    Exr.LoadEXRWithLayer(path, layer, out float[] layerRgba, out int layerWidth, out int layerHeight),
                    $"{sample.RelativePath}|{layer}");
                Assert.AreEqual(defaultWidth, layerWidth, $"{sample.RelativePath}|{layer}");
                Assert.AreEqual(defaultHeight, layerHeight, $"{sample.RelativePath}|{layer}");
                Assert.AreEqual(layerWidth * layerHeight * 4, layerRgba.Length, $"{sample.RelativePath}|{layer}");
                AssertRgbasDifferent(defaultRgba, layerRgba, $"{sample.RelativePath}|default-vs-{layer}");
            }

            Assert.AreEqual(
                ExrResult.InvalidArgument,
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
        Assert.AreEqual(ExrResult.Success, Exr.EXRLayers(path, out string[] layers));
        Assert.AreEqual(0, layers.Length);

        (uint version, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);
        Assert.IsFalse(((version & (1U << 9)) != 0));
        Assert.IsFalse(((version & (1U << 11)) != 0));
        Assert.IsFalse(((version & (1U << 12)) != 0));
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
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(path, out uint version));
        Assert.IsFalse(((version & (1U << 9)) != 0));
        Assert.IsFalse(((version & (1U << 11)) != 0));
        Assert.IsTrue(((version & (1U << 12)) != 0));

        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRMultipartHeaderFromFile(path, out _, out Header[] headers));
        Assert.AreEqual(4, headers.Length);
        CollectionAssert.AreEqual(
            new[] { "rgba.left", "depth.left", "rgba.right", "depth.right" },
            headers.Select(static header => header.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { PartType.Scanline, PartType.Scanline, PartType.Scanline, PartType.Scanline },
            headers.Select(static header => header.PartType).ToArray());
        CollectionAssert.AreEqual(
            new[] { 4, 1, 4, 1 },
            headers.Select(static header => header.Channels.Count).ToArray());

        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRMultipartImageFromFile(path, out Image? images));
        Assert.IsNotNull(images);
        Assert.AreEqual(4, images.Parts.Count);
        for (int index = 0; index < headers.Length; index++)
        {
            Assert.AreEqual(headers[index].DataWindow.Width, images.Parts[index].Width, $"part[{index}]");
            Assert.AreEqual(headers[index].DataWindow.Height, images.Parts[index].Height, $"part[{index}]");
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
            Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(path, out uint version), relativePath);
            Assert.IsFalse(((version & (1U << 9)) != 0), relativePath);
            Assert.IsTrue(((version & (1U << 11)) != 0), relativePath);
            Assert.IsTrue(((version & (1U << 12)) != 0), relativePath);

            Assert.AreEqual(ExrResult.Success, Exr.ParseEXRMultipartHeaderFromFile(path, out _, out Header[] headers), relativePath);
            Assert.AreEqual(2, headers.Length, relativePath);
            CollectionAssert.AreEqual(
                new[] { "rgba.left", "rgba.right" },
                headers.Select(static header => header.Name).ToArray(),
                relativePath);
            CollectionAssert.AreEqual(
                new[] { PartType.DeepScanline, PartType.DeepScanline },
                headers.Select(static header => header.PartType).ToArray(),
                relativePath);
            foreach (Header header in headers)
            {
                CollectionAssert.AreEqual(
                    new[] { "A", "B", "G", "R", "Z" },
                    header.Channels.Select(static channel => channel.Name).ToArray(),
                    relativePath);
            }

            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRMultipartImageFromFile(path, out Image? images), relativePath);
            Assert.IsNotNull(images);
            Assert.IsTrue(images.Parts.All(p => p.Header.IsDeep));
        }
    }

    private static void AssertDeepSinglePartSample(ExpectedDeepSample sample)
    {
        string path = TestPaths.OpenExr(sample.RelativePath);

        Assert.IsTrue(Exr.IsEXR(path), sample.RelativePath);
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRVersionFromFile(path, out uint version), sample.RelativePath);
        Assert.IsFalse(((version & (1U << 9)) != 0), sample.RelativePath);
        Assert.IsTrue(((version & (1U << 11)) != 0), sample.RelativePath);
        Assert.IsFalse(((version & (1U << 12)) != 0), sample.RelativePath);
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromFile(path, out _, out Header? parsedHeader), sample.RelativePath);
        Assert.IsNotNull(parsedHeader);
        Assert.AreEqual(PartType.DeepScanline, parsedHeader.PartType, sample.RelativePath);
        CollectionAssert.AreEqual(
            new[] { "A", "B", "G", "R", "Z" },
            parsedHeader.Channels.Select(static channel => channel.Name).ToArray(),
            sample.RelativePath);

        Assert.AreEqual(ExrResult.Success, Exr.LoadDeepEXR(path, out Part? image), sample.RelativePath);
        Assert.IsNotNull(image);
        Assert.IsTrue(image!.Header.IsDeep, sample.RelativePath);
        Assert.AreEqual(PartType.DeepScanline, image!.Header.PartType, sample.RelativePath);
        Assert.AreEqual(sample.Width, image.Width, sample.RelativePath);
        Assert.AreEqual(sample.Height, image.Height, sample.RelativePath);
        Assert.AreEqual(sample.Width * sample.Height, ((DeepLevel)image!.Levels[0]).SampleCounts.Length, sample.RelativePath);
        Assert.AreEqual(parsedHeader!.Channels.Count, image!.Header.Channels.Count, sample.RelativePath);
        CollectionAssert.AreEqual(
            new[] { "A", "B", "G", "R", "Z" },
            image.Header.Channels.Select(static channel => channel.Name).ToArray(),
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
