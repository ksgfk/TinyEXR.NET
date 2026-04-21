using System.Buffers.Binary;
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

    public static IEnumerable<object[]> DamagedSelectedCases()
    {
        yield return new object[] { new DamagedSelectedSample("autofuzz_146551958", "header-only", ExpectedVersionTiled: false, ExpectedHeaderTiled: false, ExpectedDeep: false, UseDeepLoad: false) };
        yield return new object[] { new DamagedSelectedSample("memory_DOS_1", "header-only", ExpectedVersionTiled: false, ExpectedHeaderTiled: false, ExpectedDeep: false, UseDeepLoad: false) };
        yield return new object[] { new DamagedSelectedSample("asan_heap-oob_7faf9aba03ac_414_75af58c21b9b9e994747f9d6a5fc46d4_exr", "scanline", ExpectedVersionTiled: false, ExpectedHeaderTiled: false, ExpectedDeep: false, UseDeepLoad: false) };
        yield return new object[] { new DamagedSelectedSample("asan_heap-oob_7f6798416389_229_18bd946a4fde157b9974d16a51a4851d_exr", "tiled", ExpectedVersionTiled: true, ExpectedHeaderTiled: true, ExpectedDeep: false, UseDeepLoad: false) };
        yield return new object[] { new DamagedSelectedSample("signal_sigsegv_7ffff7b21e8a_389_bf048bf41ca71b4e00d2b0edd0a39e27_exr", "deep-scanline", ExpectedVersionTiled: false, ExpectedHeaderTiled: false, ExpectedDeep: true, UseDeepLoad: true) };
        yield return new object[] { new DamagedSelectedSample("imf_test_deep_tile_file_fuzz_broken_exr", "deep-tile", ExpectedVersionTiled: false, ExpectedHeaderTiled: true, ExpectedDeep: true, UseDeepLoad: true) };
    }

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

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: LongNameVersionBit|SinglePart")]
    public void Case_Regression_LongNameVersionBit_SinglePart()
    {
        string longChannelName = new string('C', 42);
        string longAttributeName = new string('A', 42);
        ExrImage image = new(
            1,
            1,
            new[]
            {
                ExrTestHelper.FloatChannel(longChannelName, ExrPixelType.Float, new[] { 1.0f }),
            });

        ExrHeader header = new()
        {
            Compression = CompressionType.None,
            HasLongNames = true,
        };
        header.CustomAttributes.Add(ExrAttribute.FromString(longAttributeName, "enabled"));

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out ExrVersion version));
        Assert.IsTrue(version.LongName);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader parsedHeader));
        Assert.IsTrue(parsedHeader.HasLongNames);
        Assert.AreEqual(longChannelName, parsedHeader.Channels[0].Name);
        Assert.AreEqual(longAttributeName, parsedHeader.CustomAttributes[0].Name);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: LongNameVersionBit|Multipart")]
    public void Case_Regression_LongNameVersionBit_Multipart()
    {
        ExrImage shortImage = new(
            1,
            1,
            new[]
            {
                ExrTestHelper.FloatChannel("Y", ExrPixelType.Float, new[] { 0.25f }),
            });
        ExrHeader shortHeader = new()
        {
            Name = "short-part",
            Compression = CompressionType.None,
        };

        string longChannelName = new string('M', 42);
        ExrImage longImage = new(
            1,
            1,
            new[]
            {
                ExrTestHelper.FloatChannel(longChannelName, ExrPixelType.Float, new[] { 0.75f }),
            });
        ExrHeader longHeader = new()
        {
            Name = "long-part",
            Compression = CompressionType.None,
            HasLongNames = true,
        };

        ExrMultipartImage images = new(new[] { shortImage, longImage });
        ExrMultipartHeader headers = new(new[] { shortHeader, longHeader });

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRMultipartImageToMemory(images, headers, out byte[] encoded));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out ExrVersion version));
        Assert.IsTrue(version.Multipart);
        Assert.IsTrue(version.LongName);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(encoded, out _, out ExrMultipartHeader parsedHeaders));
        Assert.AreEqual(2, parsedHeaders.Headers.Count);
        Assert.AreEqual(longChannelName, parsedHeaders.Headers[1].Channels[0].Name);
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

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: OffsetTable|Scanline|Reconstruct")]
    public void Case_Regression_OffsetTable_Scanline_Reconstruct()
    {
        ExrImage sourceImage = new(
            2,
            4,
            new[]
            {
                ExrTestHelper.FloatChannel("Y", ExrPixelType.Float, new[]
                {
                    1.0f, 2.0f,
                    3.0f, 4.0f,
                    5.0f, 6.0f,
                    7.0f, 8.0f,
                }),
            });

        ExrHeader sourceHeader = new()
        {
            Compression = CompressionType.None,
        };

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(sourceImage, sourceHeader, out byte[] encoded));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader header));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, header, out ExrImage expected));

        byte[] mutated = (byte[])encoded.Clone();
        ZeroFirstSinglePartOffset(mutated);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(mutated, out _, out ExrHeader mutatedHeader));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(mutated, mutatedHeader, out ExrImage actual));

        ExrTestHelper.EqualImages(expected, actual);
        CollectionAssert.AreEqual(
            ExrTestHelper.ReadFloatChannel(expected, "Y"),
            ExrTestHelper.ReadFloatChannel(actual, "Y"));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: OffsetTable|Tile|Reconstruct")]
    public void Case_Regression_OffsetTable_Tile_Reconstruct()
    {
        ExrImage sourceImage = new(
            4,
            4,
            new[]
            {
                ExrTestHelper.FloatChannel("Y", ExrPixelType.Float, new[]
                {
                    1.0f, 2.0f, 3.0f, 4.0f,
                    5.0f, 6.0f, 7.0f, 8.0f,
                    9.0f, 10.0f, 11.0f, 12.0f,
                    13.0f, 14.0f, 15.0f, 16.0f,
                }),
            });

        ExrHeader sourceHeader = new()
        {
            Compression = CompressionType.None,
            Tiles = new ExrTileDescription
            {
                TileSizeX = 2,
                TileSizeY = 2,
                LevelMode = ExrTileLevelMode.OneLevel,
                RoundingMode = ExrTileRoundingMode.RoundDown,
            },
        };

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(sourceImage, sourceHeader, out byte[] encoded));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader header));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, header, out ExrImage expected));

        byte[] mutated = (byte[])encoded.Clone();
        ZeroFirstSinglePartOffset(mutated);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(mutated, out _, out ExrHeader mutatedHeader));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(mutated, mutatedHeader, out ExrImage actual));

        ExrTestHelper.EqualImages(expected, actual);
        CollectionAssert.AreEqual(
            ExrTestHelper.ReadFloatChannel(expected, "Y"),
            ExrTestHelper.ReadFloatChannel(actual, "Y"));
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

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: RequestedPixelTypes|ReadAcceptsTinyExrMatrix")]
    public void Case_Regression_RequestedPixelTypes_ReadAcceptsTinyExrMatrix()
    {
        byte[] encoded = CreateRequestedPixelTypesRegressionImage();

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader header));
        ExrTestHelper.SetRequestedPixelTypes(
            header,
            static channel => channel.Name switch
            {
                "H" => ExrPixelType.Float,
                "U" => ExrPixelType.UInt,
                "F" => ExrPixelType.Float,
                _ => throw new InvalidOperationException($"Unexpected channel '{channel.Name}'."),
            });

        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, header, out ExrImage image));

        Assert.AreEqual(ExrPixelType.Float, image.GetChannel("H").DataType);
        Assert.AreEqual(ExrPixelType.UInt, image.GetChannel("U").DataType);
        Assert.AreEqual(ExrPixelType.Float, image.GetChannel("F").DataType);
        CollectionAssert.AreEqual(new[] { 1.5f, 2.0f }, ExrTestHelper.ReadFloatChannel(image, "H"));
        CollectionAssert.AreEqual(new uint[] { 7u, 42u }, ExrTestHelper.ReadUIntChannel(image, "U"));
        CollectionAssert.AreEqual(new[] { 3.25f, 4.5f }, ExrTestHelper.ReadFloatChannel(image, "F"));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: RequestedPixelTypes|ReadRejectsNonTinyExrMatrix")]
    public void Case_Regression_RequestedPixelTypes_ReadRejectsNonTinyExrMatrix()
    {
        byte[] encoded = CreateRequestedPixelTypesRegressionImage();

        AssertRequestedPixelTypeLoadFails(encoded, "H", ExrPixelType.UInt);
        AssertRequestedPixelTypeLoadFails(encoded, "U", ExrPixelType.Half);
        AssertRequestedPixelTypeLoadFails(encoded, "U", ExrPixelType.Float);
        AssertRequestedPixelTypeLoadFails(encoded, "F", ExrPixelType.Half);
        AssertRequestedPixelTypeLoadFails(encoded, "F", ExrPixelType.UInt);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: MalformedCompression|InvalidUnknown")]
    public void Case_Regression_MalformedCompression_InvalidUnknown()
    {
        byte[] mutated = LoadMalformedPizBase();
        ExrBinaryMutationHelper.SetHeaderByteAttributeValue(mutated, headerIndex: 0, "compression", "compression", 0x7f);

        Assert.AreEqual(ResultCode.UnsupportedFormat, Exr.ParseEXRHeaderFromMemory(mutated, out _, out _));
        Assert.AreNotEqual(ResultCode.Success, Exr.LoadEXRFromMemory(mutated, out _, out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: MalformedCompression|ShortDecodePIZ")]
    public void Case_Regression_MalformedCompression_ShortDecodePIZ()
    {
        byte[] original = LoadMalformedPizBase();
        byte[] mutated = ExrBinaryMutationHelper.TruncateFirstScanlineChunkPayload(original, bytesRemovedFromPayload: 1);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(mutated, out _, out ExrHeader header));
        Assert.AreEqual(CompressionType.PIZ, header.Compression);
        Assert.AreNotEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(mutated, header, out _));
        Assert.AreNotEqual(ResultCode.Success, Exr.LoadEXRFromMemory(mutated, out _, out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Regression: MalformedCompression|EarlyEofPIZ")]
    public void Case_Regression_MalformedCompression_EarlyEofPIZ()
    {
        byte[] original = LoadMalformedPizBase();
        byte[] mutated = ExrBinaryMutationHelper.TruncateFirstScanlineChunkPayload(original, bytesRemovedFromPayload: 32);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(mutated, out _, out ExrHeader header));
        Assert.AreEqual(CompressionType.PIZ, header.Compression);
        Assert.AreNotEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(mutated, header, out _));
        Assert.AreNotEqual(ResultCode.Success, Exr.LoadEXRFromMemory(mutated, out _, out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Damaged corpus|Selected regression samples")]
    [DynamicData(nameof(DamagedSelectedCases))]
    public void Case_Damaged_corpus_selected_regression_samples(DamagedSelectedSample sample)
    {
        string path = TestPaths.OpenExr($"Damaged/{sample.FileName}");

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version), sample.FileName);
        Assert.IsFalse(version.Multipart, sample.FileName);
        Assert.AreEqual(sample.ExpectedVersionTiled, version.Tiled, sample.FileName);
        Assert.AreEqual(sample.ExpectedDeep, version.NonImage, sample.FileName);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header), sample.FileName);
        Assert.AreEqual(sample.ExpectedHeaderTiled, header.Tiles is not null, sample.FileName);
        Assert.AreEqual(sample.ExpectedDeep, header.IsDeep, sample.FileName);

        if (sample.UseDeepLoad)
        {
            Assert.AreNotEqual(ResultCode.Success, Exr.LoadDeepEXR(path, out ExrHeader deepHeader, out ExrDeepImage deepImage), sample.FileName);
            Assert.IsTrue(deepHeader.IsDeep || deepImage.Width == 0, sample.FileName);
            return;
        }

        Assert.AreNotEqual(ResultCode.Success, Exr.LoadEXRImageFromFile(path, header, out _), sample.FileName);
    }

    private static void SetLineOrderAttribute(byte[] encoded, LineOrderType lineOrder)
    {
        ExrBinaryMutationHelper.SetHeaderByteAttributeValue(encoded, headerIndex: 0, "lineOrder", "lineOrder", (byte)lineOrder);
    }

    private static void ZeroFirstSinglePartOffset(byte[] encoded)
    {
        int offsetTableOffset = FindSinglePartOffsetTableOffset(encoded);
        Assert.IsTrue(offsetTableOffset + sizeof(long) <= encoded.Length, "offset table entry was truncated.");
        Array.Clear(encoded, offsetTableOffset, sizeof(long));
    }

    private static int FindSinglePartOffsetTableOffset(byte[] encoded)
    {
        int offset = 8;
        while (true)
        {
            int nameEnd = Array.IndexOf(encoded, (byte)0, offset);
            Assert.IsTrue(nameEnd >= 0, "attribute name terminator was not found.");
            if (nameEnd == offset)
            {
                return offset + 1;
            }

            offset = nameEnd + 1;

            int typeEnd = Array.IndexOf(encoded, (byte)0, offset);
            Assert.IsTrue(typeEnd >= 0, "attribute type terminator was not found.");
            offset = typeEnd + 1;

            Assert.IsTrue(offset + sizeof(int) <= encoded.Length, "attribute size field was truncated.");
            int attributeSize = BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(offset, sizeof(int)));
            Assert.IsTrue(attributeSize >= 0, "attribute size must be non-negative.");
            offset += sizeof(int);

            Assert.IsTrue(offset + attributeSize <= encoded.Length, "attribute value was truncated.");
            offset += attributeSize;
        }
    }

    private static byte[] CreateRequestedPixelTypesRegressionImage()
    {
        ExrImage image = new(
            2,
            1,
            new[]
            {
                ExrTestHelper.FloatChannel("H", ExrPixelType.Half, new[] { 1.5f, 2.0f }),
                ExrTestHelper.UIntChannel("U", ExrPixelType.UInt, new uint[] { 7u, 42u }),
                ExrTestHelper.FloatChannel("F", ExrPixelType.Float, new[] { 3.25f, 4.5f }),
            });

        ExrHeader header = new()
        {
            Compression = CompressionType.None,
        };

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        return encoded;
    }

    private static byte[] LoadMalformedPizBase()
    {
        byte[] original = File.ReadAllBytes(TestPaths.Regression("piz-bug-issue-100.exr"));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(original, out _, out ExrHeader header));
        Assert.AreEqual(CompressionType.PIZ, header.Compression);
        return original;
    }

    private static void AssertRequestedPixelTypeLoadFails(byte[] encoded, string channelName, ExrPixelType requestedPixelType)
    {
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader header));
        ExrTestHelper.SetRequestedPixelTypes(
            header,
            channel => string.Equals(channel.Name, channelName, StringComparison.Ordinal) ? requestedPixelType : channel.Type);

        ResultCode result = Exr.LoadEXRImageFromMemory(encoded, header, out _);
        Assert.AreEqual(ResultCode.UnsupportedFeature, result, $"{channelName}->{requestedPixelType}");
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

    public readonly record struct DamagedSelectedSample(
        string FileName,
        string Category,
        bool ExpectedVersionTiled,
        bool ExpectedHeaderTiled,
        bool ExpectedDeep,
        bool UseDeepLoad)
    {
        public override string ToString()
        {
            return $"{Category}:{FileName}";
        }
    }
}
