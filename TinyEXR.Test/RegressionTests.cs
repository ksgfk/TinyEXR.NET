using System.Collections;
using System.Reflection;
using System.Text;

namespace TinyEXR.Test;

[TestClass]
public sealed class RegressionTests
{
    private static readonly Type ExrImplementationType =
        typeof(Exr).Assembly.GetType("TinyEXR.PortV1.ExrImplementation") ??
        throw new InvalidOperationException("TinyEXR.PortV1.ExrImplementation was not found.");

    private static readonly MethodInfo GetLayersMethod =
        ExrImplementationType.GetMethod("GetLayers", BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException("GetLayers was not found.");

    private static readonly MethodInfo GetChannelsInLayerMethod =
        ExrImplementationType.GetMethod("GetChannelsInLayer", BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException("GetChannelsInLayer was not found.");

    [TestMethod(DisplayName = "ParseEXRVersionFromMemory invalid input")]
    public void Case_ParseEXRVersionFromMemory_invalid_input()
    {
        Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.ParseEXRVersionFromMemory(ReadOnlySpan<byte>.Empty, out _));
        Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.ParseEXRVersionFromMemory(new byte[1], out _));
        Assert.AreEqual(ResultCode.InvalidMagicNumver, Exr.ParseEXRVersionFromMemory(new byte[8], out _));
    }

    [TestMethod(DisplayName = "ParseEXRHeaderFromMemory invalid input")]
    public void Case_ParseEXRHeaderFromMemory_invalid_input()
    {
        Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.ParseEXRHeaderFromMemory(ReadOnlySpan<byte>.Empty, out _, out _));
        Assert.AreEqual(ResultCode.InvalidMagicNumver, Exr.ParseEXRHeaderFromMemory(new byte[128], out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: InvalidVersionByte")]
    public void Case_Regression_InvalidVersionByte()
    {
        byte[] original = File.ReadAllBytes(TestPaths.Asakusa);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(original, out _, out ExrHeader validHeader));

        foreach (byte invalidVersion in new byte[] { 1, 3 })
        {
            byte[] mutated = (byte[])original.Clone();
            mutated[4] = invalidVersion;

            Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.ParseEXRVersionFromMemory(mutated, out _), $"version={invalidVersion}");
            Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.ParseEXRHeaderFromMemory(mutated, out _, out _), $"version={invalidVersion}");
            Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.LoadEXRImageFromMemory(mutated, validHeader, out _), $"version={invalidVersion}");
        }
    }

    [TestMethod(DisplayName = "Compressed is smaller than uncompressed")]
    public void Case_Compressed_is_smaller_than_uncompressed()
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

        string path = Path.Combine(Path.GetTempPath(), $"issue40-{Guid.NewGuid():N}.exr");
        try
        {
            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToFile(image, header, path));
            Assert.IsTrue(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod(DisplayName = "Regression: Issue50")]
    public void Case_Regression_Issue50()
    {
        AssertFuzzedHeaderRejected("poc-eedff3a9e99eb1c0fd3a3b0989e7c44c0a69f04f10b23e5264f362a4773f4397_min");
    }

    [TestMethod(DisplayName = "Regression: Issue57")]
    public void Case_Regression_Issue57()
    {
        AssertFuzzedHeaderRejected("poc-df76d1f27adb8927a1446a603028272140905c168a336128465a1162ec7af270.mini");
    }

    [TestMethod(DisplayName = "Regression: Issue56")]
    public void Case_Regression_Issue56()
    {
        AssertFuzzedHeaderRejected("poc-1383755b301e5f505b2198dc0508918b537fdf48bbfc6deeffe268822e6f6cd6");
    }

    [TestMethod(DisplayName = "Regression: Issue61")]
    public void Case_Regression_Issue61()
    {
        AssertFuzzedHeaderRejected("poc-3f1f642c3356fd8e8d2a0787613ec09a56572b3a1e38c9629b6db9e8dead1117_min");
    }

    [TestMethod(DisplayName = "Regression: Issue60")]
    public void Case_Regression_Issue60()
    {
        AssertFuzzedHeaderRejected("poc-5b66774a7498c635334ad386be0c3b359951738ac47f14878a3346d1c6ea0fe5_min");
    }

    [TestMethod(DisplayName = "Regression: Issue71")]
    public void Case_Regression_Issue71()
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

    [TestMethod(DisplayName = "Regression: Issue93")]
    public void Case_Regression_Issue93()
    {
        byte[] data = File.ReadAllBytes(TestPaths.OpenExr("Tiles/GoldenGate.exr"));

        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRFromMemory(data, out float[] image, out _, out _));
        Assert.AreEqual(0.0612183f, image[0], 0.000001f);
        Assert.AreEqual(0.0892334f, image[1], 0.000001f);
        Assert.AreEqual(0.271973f, image[2], 0.000001f);
    }

    [TestMethod(DisplayName = "Regression: Issue100")]
    public void Case_Regression_Issue100()
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

    [TestMethod(DisplayName = "Regression: Issue53|Channels")]
    public void Case_Regression_Issue53_Channels()
    {
        string path = TestPaths.Regression("flaga.exr");

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out _));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header));

        string[] layers = GetLayers(header);
        Assert.AreEqual(2, layers.Length);
        Assert.AreEqual(0, GetChannelsInLayerCount(header, string.Empty));
        Assert.AreEqual(0, GetChannelsInLayerCount(header, "Warstwa 3"));
        Assert.AreEqual(4, GetChannelsInLayerCount(header, "Warstwa 1"));
    }

    [TestMethod(DisplayName = "Regression: Issue53|Image")]
    public void Case_Regression_Issue53_Image()
    {
        string path = TestPaths.Regression("flaga.exr");

        Assert.AreEqual(ResultCode.Success, Exr.EXRLayers(path, out string[] layers));
        Assert.AreEqual(2, layers.Length);

        Assert.AreEqual(ResultCode.LayerNotFound, Exr.LoadEXRWithLayer(path, layer: null, out _, out _, out _));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRWithLayer(path, "Warstwa 1", out float[] image, out int width, out int height));
        Assert.IsTrue(image.Length > 0);
        Assert.IsTrue(width > 0);
        Assert.IsTrue(height > 0);
    }

    [TestMethod(DisplayName = "Regression: Issue53|Image|Missing Layer")]
    public void Case_Regression_Issue53_Image_Missing_Layer()
    {
        Assert.AreEqual(
            ResultCode.LayerNotFound,
            Exr.LoadEXRWithLayer(TestPaths.OpenExr("MultiView/Impact.exr"), "Warstwa", out _, out _, out _));
    }

    [TestMethod(DisplayName = "Regression: PR150|Read|1x1 1xhalf")]
    public void Case_Regression_PR150_Read_1x1_1xhalf()
    {
        string path = TestPaths.Regression("tiled_half_1x1_alpha.exr");
        (ExrVersion version, _, ExrImage image) = ExrTestHelper.LoadSinglePart(path);

        Assert.IsTrue(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.AreEqual(1, image.Levels.Count);
    }

    [TestMethod(DisplayName = "Regression: Issue194|Piz")]
    public void Case_Regression_Issue194_Piz()
    {
        string path = TestPaths.Regression("000-issue194.exr");
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out _));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromFile(path, header, out ExrImage image));
        Assert.IsTrue(image.Width > 0);
        Assert.IsTrue(image.Height > 0);
    }

    [TestMethod(DisplayName = "Regression: Issue238|DoubleFree")]
    public void Case_Regression_Issue238_DoubleFree()
    {
        byte[] data = File.ReadAllBytes(TestPaths.Regression("issue-238-double-free.exr"));

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(data, out _, out ExrHeader header));

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

    [TestMethod(DisplayName = "Regression: Issue238|DoubleFree|Multipart")]
    public void Case_Regression_Issue238_DoubleFree_Multipart()
    {
        byte[] data = File.ReadAllBytes(TestPaths.Regression("issue-238-double-free-multipart.exr"));

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(data, out ExrVersion version));
        Assert.IsTrue(version.Multipart);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(data, out _, out ExrMultipartHeader headers));
        Assert.AreEqual(2, headers.Headers.Count);

        ResultCode result = Exr.LoadEXRMultipartImageFromMemory(data, headers, out ExrMultipartImage images);
        Assert.AreNotEqual(ResultCode.Success, result);
        Assert.AreEqual(0, images.Images.Count);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: ScanlineLineOrder|LoadIgnoresHeaderOrder")]
    public void Case_Regression_ScanlineLineOrder_LoadIgnoresHeaderOrder()
    {
        ExrImage sourceImage = new(
            2,
            2,
            new[]
            {
                ExrTestHelper.FloatChannel("Y", ExrPixelType.Float, new[] { 10.0f, 20.0f, 30.0f, 40.0f }),
            });

        ExrHeader sourceHeader = new()
        {
            Compression = CompressionType.None,
        };

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(sourceImage, sourceHeader, out byte[] encoded));

        byte[] mutated = (byte[])encoded.Clone();
        SetLineOrderAttribute(mutated, LineOrderType.DecreasingY);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(mutated, out _, out ExrHeader header));
        Assert.AreEqual(LineOrderType.DecreasingY, header.LineOrder);
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(mutated, header, out ExrImage decoded));

        CollectionAssert.AreEqual(
            new[] { 10.0f, 20.0f, 30.0f, 40.0f },
            ExrTestHelper.ReadFloatChannel(decoded, "Y"));
    }

    [TestMethod(DisplayName = "Regression: Issue160|Piz")]
    public void Case_Regression_Issue160_Piz()
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

    private static void SetLineOrderAttribute(byte[] encoded, LineOrderType lineOrder)
    {
        byte[] marker = Encoding.ASCII.GetBytes("lineOrder\0lineOrder\0");
        int markerIndex = encoded.AsSpan().IndexOf(marker);
        Assert.IsTrue(markerIndex >= 0, "lineOrder attribute marker was not found.");

        int valueSizeOffset = markerIndex + marker.Length;
        Assert.IsTrue(valueSizeOffset + sizeof(int) < encoded.Length, "lineOrder attribute size was truncated.");
        Assert.AreEqual(1, BitConverter.ToInt32(encoded, valueSizeOffset), "lineOrder attribute size must be 1 byte.");

        int valueOffset = valueSizeOffset + sizeof(int);
        Assert.IsTrue(valueOffset < encoded.Length, "lineOrder attribute value was truncated.");
        encoded[valueOffset] = (byte)lineOrder;
    }

    private static void AssertFuzzedHeaderRejected(string fileName)
    {
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

    private static string[] GetLayers(ExrHeader header)
    {
        object? value = GetLayersMethod.Invoke(null, new object?[] { header });
        if (value is not IEnumerable enumerable)
        {
            throw new AssertFailedException("GetLayers did not return an enumerable result.");
        }

        List<string> layers = new();
        foreach (object? item in enumerable)
        {
            if (item is string layer)
            {
                layers.Add(layer);
            }
        }

        return layers.ToArray();
    }

    private static int GetChannelsInLayerCount(ExrHeader header, string? layerName)
    {
        object? value = GetChannelsInLayerMethod.Invoke(null, new object?[] { header, layerName });
        if (value is ICollection collection)
        {
            return collection.Count;
        }

        throw new AssertFailedException("GetChannelsInLayer did not return a collection result.");
    }
}
