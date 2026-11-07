using System.Buffers.Binary;
using System.Text;
namespace TinyEXR.Test;

[TestClass]
public sealed class RegressionTests
{
    [TestMethod(DisplayName = "Regression: Issue71")]
    public void Case_Regression_Issue71()
    {
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXR(TestPaths.Regression("2by2.exr"), out float[] image, out int width, out int height));
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

    [TestMethod(DisplayName = "Regression: Issue100")]
    public void Case_Regression_Issue100()
    {
        byte[] data = File.ReadAllBytes(TestPaths.Regression("piz-bug-issue-100.exr"));

        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRFromMemory(data, out float[] image, out int width, out int height));
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

    [TestMethod(DisplayName = "Regression: Issue160|Piz")]
    public void Case_Regression_Issue160_Piz()
    {
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXR(TestPaths.Regression("issue-160-piz-decode.exr"), out float[] image, out int width, out int height));
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
    [TestMethod(DisplayName = "V3 regression fixtures use ExrReader with deterministic failure outputs")]
    public void RegressionFixtures()
    {
        foreach (string name in new[] { "2by2.exr", "piz-bug-issue-100.exr", "issue-160-piz-decode.exr", "tiled_half_1x1_alpha.exr", "000-issue194.exr", "flaga.exr" })
        {
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromFile(TestPaths.Regression(name), out Part? part), name);
            Assert.IsNotNull(part);
            Assert.IsTrue(part.IsComplete);
        }
        Assert.AreEqual(ExrResult.Success, Exr.EXRLayers(TestPaths.Regression("flaga.exr"), out string[] layers));
        Assert.AreEqual(2, layers.Length);
        Assert.AreEqual(ExrResult.Success, Exr.LoadEXRWithLayer(TestPaths.Regression("flaga.exr"), "Warstwa 1", out _, out _, out _));
        Assert.AreEqual(ExrResult.InvalidArgument, Exr.LoadEXR(TestPaths.Regression("flaga.exr"), out float[] rgba, out int width, out int height));
        Assert.AreEqual(0, rgba.Length);
        Assert.AreEqual(0, width);
        Assert.AreEqual(0, height);
        foreach (object[] row in ExrTestData.FuzzedHeaderFiles())
            AssertRejected(File.ReadAllBytes(TestPaths.Regression((string)row[0])));
        foreach (string name in new[] { "issue-238-double-free.exr", "issue-238-double-free-multipart.exr" })
            AssertRejected(File.ReadAllBytes(TestPaths.Regression(name)));
        foreach (string name in new[]
        {
            "autofuzz_146551958", "memory_DOS_1",
            "asan_heap-oob_7faf9aba03ac_414_75af58c21b9b9e994747f9d6a5fc46d4_exr",
            "asan_heap-oob_7f6798416389_229_18bd946a4fde157b9974d16a51a4851d_exr",
            "signal_sigsegv_7ffff7b21e8a_389_bf048bf41ca71b4e00d2b0edd0a39e27_exr",
            "imf_test_deep_tile_file_fuzz_broken_exr",
        })
            AssertRejected(File.ReadAllBytes(TestPaths.OpenExr("Damaged/" + name)));
    }

    [TestMethod(DisplayName = "Exr rejects unknown flags, excessive attributes, truncated and corrupt data")]
    public void ResourceAndCorruptionParity()
    {
        Part part = PartConversion.FromInterleavedFloat(new float[4096], 128, 32, 1, PixelType.Float, Compression.PIZ);
        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(part, out byte[] valid));
        foreach (uint version in new[] { 1U, 3U, 2U | (1U << 31) })
        {
            byte[] bytes = (byte[])valid.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), version);
            AssertRejected(bytes);
        }
        foreach (int length in new[] { 0, 1, 4, 7, 8, 25, valid.Length - 1 })
            AssertRejected(valid.AsSpan(0, length).ToArray());
        byte[] corrupt = (byte[])valid.Clone();
        ExrBinaryMutationHelper.SetHeaderByteAttributeValue(corrupt, 0, "compression", "compression", 255);
        AssertRejected(corrupt);
        using ExrReader reader = ExrReader.OpenMemory(valid);
        Assert.AreEqual(ExrResult.Success, reader.ParseHeader().Status);
        int end = (int)reader.GetHeaderEndOffset(0) - 1;
        using MemoryStream oversized = new();
        oversized.Write(valid.AsSpan(0, end));
        for (int i = 0; i < 4096; i++)
        {
            oversized.Write(Encoding.UTF8.GetBytes($"extra{i}\0blob\0"));
            oversized.Write(new byte[4]);
        }
        oversized.Write(valid.AsSpan(end));
        AssertRejected(oversized.ToArray());
        byte[] payloadCorrupt = (byte[])valid.Clone();
        int chunk = (int)reader.GetBlockInfo(0, 0).FileOffset;
        payloadCorrupt[chunk + 8] = 255;
        payloadCorrupt[chunk + 9] = 255;
        AssertRejected(payloadCorrupt);
    }

    [TestMethod(DisplayName = "Reader reconstructs missing offsets for scanline and tiled files")]
    public void OffsetReconstruction()
    {
        foreach (PartType type in new[] { PartType.Scanline, PartType.Tiled })
        {
            Part part = ExrConvenienceTests.CreatePart(type, Compression.ZIP, TileLevelMode.OneLevel, "test");
            Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(part, out byte[] bytes));
            using ExrReader reader = ExrReader.OpenMemory(bytes);
            Assert.AreEqual(ExrResult.Success, reader.ParseHeader().Status);
            Array.Clear(bytes, (int)reader.GetHeaderEndOffset(0), 8);
            Assert.AreEqual(ExrResult.Success, Exr.LoadEXRImageFromMemory(bytes, out Part? decoded));
            Assert.IsNotNull(decoded);
            ExrTestHelper.EqualImages(part, decoded);
        }
    }

    private static void AssertRejected(byte[] bytes)
    {
        ReaderResult<Image> direct = ExrFile.LoadFromMemory(bytes);
        Assert.AreNotEqual(ExrResult.Success, direct.Status);
        using ExrReader reader = ExrReader.OpenMemory(bytes);
        ExrResult headerStatus = reader.ParseHeader().Status;
        bool multipart = bytes.Length >= 8 && (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) & (1U << 12)) != 0;
        bool deep = headerStatus == ExrResult.Success && reader.GetHeader(0).IsDeep;
        ExrResult expectedStatus;
        if (multipart)
        {
            expectedStatus = Exr.LoadEXRMultipartImageFromMemory(bytes, out Image? image);
            Assert.IsNull(image);
        }
        else if (deep)
        {
            expectedStatus = Exr.LoadDeepEXRFromMemory(bytes, out Part? part);
            Assert.IsNull(part);
        }
        else
        {
            expectedStatus = Exr.LoadEXRImageFromMemory(bytes, out Part? part);
            Assert.IsNull(part);
        }
        Assert.IsTrue(direct.Status == expectedStatus, $"Reader={direct.Status}, Exr={expectedStatus}");
        using MemoryStream stream = new(new byte[11].Concat(bytes).ToArray());
        stream.Position = 11;
        if (multipart)
            Assert.AreEqual(expectedStatus, Exr.LoadEXRMultipartImageFromStream(stream, out _));
        else if (deep)
            Assert.AreEqual(expectedStatus, Exr.LoadDeepEXRFromStream(stream, out _));
        else
            Assert.AreEqual(expectedStatus, Exr.LoadEXRImageFromStream(stream, out _));
        Assert.AreEqual(11, stream.Position);
        Assert.IsTrue(stream.CanRead);
    }
}
