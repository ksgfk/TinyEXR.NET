using V3 = TinyEXR.V3;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3HighLevelTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 high-level memory API round-trips complete mixed multipart images")]
    public void Case_V3HighLevel_MemoryRoundTripsMixedMultipartImage()
    {
        V3.Image source = CreateImage();
        V3.WriterResult<byte[]> saved = V3.ExrFile.SaveToMemory(source, V3.Compression.ZSTD);
        Assert.AreEqual(V3.ExrResult.Success, saved.Status, saved.Error?.ToString());
        Assert.IsNotNull(saved.Value);
        Assert.IsTrue(V3.ExrFile.IsExr(saved.Value));

        V3.ReaderResult<V3.Image> loaded = V3.ExrFile.LoadFromMemory(saved.Value);
        Assert.AreEqual(V3.ExrResult.Success, loaded.Status, loaded.Error?.ToString());
        Assert.IsNotNull(loaded.Value);
        AssertImageMatches(source, loaded.Value, V3.Compression.ZSTD);

        Assert.IsFalse(V3.ExrFile.IsExr(ReadOnlySpan<byte>.Empty));
        Assert.IsFalse(V3.ExrFile.IsExr(new byte[] { 0x76, 0x2f, 0x31, 0x00 }));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 high-level stream API honors logical origins and async I/O")]
    public async Task Case_V3HighLevel_StreamOriginAndAsyncRoundTrip()
    {
        V3.Image source = CreateImage();
        byte[] prefix = { 9, 8, 7, 6, 5, 4, 3 };
        using MemoryStream stream = new();
        stream.Write(prefix);
        long origin = stream.Position;

        V3.WriterResult saved = await V3.ExrFile.SaveToStreamAsync(
            source,
            stream,
            V3.Compression.ZIP,
            new V3.WriterOptions(leaveOpen: true));
        Assert.AreEqual(V3.ExrResult.Success, saved.Status, saved.Error?.ToString());
        Assert.IsTrue(stream.CanWrite);
        CollectionAssert.AreEqual(prefix, stream.ToArray().AsSpan(0, prefix.Length).ToArray());
        Assert.IsTrue(stream.Position > origin);

        stream.Position = origin;
        V3.ReaderResult<V3.Image> loaded = await V3.ExrFile.LoadFromStreamAsync(
            stream,
            new V3.ReaderOptions(leaveOpen: true));
        Assert.AreEqual(V3.ExrResult.Success, loaded.Status, loaded.Error?.ToString());
        Assert.IsNotNull(loaded.Value);
        Assert.IsTrue(stream.CanRead);
        AssertImageMatches(source, loaded.Value, V3.Compression.ZIP);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 high-level file API maps I/O and round-trips")]
    public async Task Case_V3HighLevel_FileRoundTripAndIoResult()
    {
        V3.Image source = CreateImage();
        string directory = Path.Combine(Path.GetTempPath(), "TinyEXR.NET-v3-tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".exr");
        try
        {
            V3.WriterResult saved = await V3.ExrFile.SaveToFileAsync(
                source,
                path,
                V3.Compression.ZSTD);
            Assert.AreEqual(V3.ExrResult.Success, saved.Status, saved.Error?.ToString());

            V3.ReaderResult<V3.Image> loaded = V3.ExrFile.LoadFromFile(path);
            Assert.AreEqual(V3.ExrResult.Success, loaded.Status, loaded.Error?.ToString());
            Assert.IsNotNull(loaded.Value);
            AssertImageMatches(source, loaded.Value, V3.Compression.ZSTD);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        V3.ReaderResult<V3.Image> missing = V3.ExrFile.LoadFromFile(
            Path.Combine(directory, Guid.NewGuid().ToString("N") + ".missing"));
        Assert.AreEqual(V3.ExrResult.IO, missing.Status);
        Assert.IsNull(missing.Value);
        Assert.IsInstanceOfType<IOException>(missing.Error);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 high-level save rejects partial materializations atomically")]
    public void Case_V3HighLevel_SaveRejectsPartialPart()
    {
        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 1, 1),
            new[] { new V3.Channel("R", V3.PixelType.Float) });
        V3.Part partial = new(
            header,
            new[]
            {
                new V3.FlatLevel(
                    0,
                    0,
                    new V3.Box2i(0, 0, 1, 0),
                    new[] { new V3.ChannelBuffer("R", V3.PixelType.Float, new byte[8]) }),
            });

        V3.WriterResult<byte[]> result = V3.ExrFile.SaveToMemory(new V3.Image(new[] { partial }));
        Assert.AreEqual(V3.ExrResult.InvalidArgument, result.Status);
        Assert.IsNull(result.Value);
        Assert.IsInstanceOfType<ArgumentException>(result.Error);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 high-level save preserves forced multipart framing")]
    public void Case_V3HighLevel_ForceMultipartOptionFlowsToWriter()
    {
        V3.Image mixed = CreateImage();
        V3.Image source = new(new[] { mixed.Parts[0] });
        V3.WriterResult<byte[]> saved = V3.ExrFile.SaveToMemory(
            source,
            options: new V3.WriterOptions(forceMultipart: true));

        Assert.AreEqual(V3.ExrResult.Success, saved.Status, saved.Error?.ToString());
        Assert.IsNotNull(saved.Value);
        Assert.AreNotEqual(
            0U,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(saved.Value.AsSpan(4)) & (1U << 12));
        V3.ReaderResult<V3.Image> loaded = V3.ExrFile.LoadFromMemory(saved.Value);
        Assert.AreEqual(V3.ExrResult.Success, loaded.Status, loaded.Error?.ToString());
        Assert.IsNotNull(loaded.Value);
        Assert.AreEqual(1, loaded.Value.Parts.Count);
        AssertImageMatches(source, loaded.Value, source.Parts[0].Header.Compression);
    }

    private static V3.Image CreateImage()
    {
        V3.Header flatHeader = new(
            V3.PartType.Tiled,
            new V3.Box2i(-1, 2, 2, 4),
            new[]
            {
                new V3.Channel("A", V3.PixelType.Half),
                new V3.Channel("R", V3.PixelType.Float),
            },
            compression: V3.Compression.PIZ,
            tiles: new V3.TileDescription(
                2,
                2,
                V3.TileLevelMode.MipmapLevels,
                V3.TileRoundingMode.RoundUp),
            name: "flat");
        V3.FlatLevel[] flatLevels =
        {
            CreateFlatLevel(flatHeader, 0, new V3.Box2i(-1, 2, 2, 4)),
            CreateFlatLevel(flatHeader, 1, new V3.Box2i(-1, 2, 0, 3)),
            CreateFlatLevel(flatHeader, 2, new V3.Box2i(-1, 2, -1, 2)),
        };
        V3.Part flatPart = new(flatHeader, flatLevels, isComplete: true);

        V3.Header deepHeader = new(
            V3.PartType.DeepScanline,
            new V3.Box2i(0, -1, 2, 1),
            new[]
            {
                new V3.Channel("A", V3.PixelType.Half),
                new V3.Channel("Z", V3.PixelType.Float),
            },
            compression: V3.Compression.ZIPS,
            name: "deep");
        int[] counts = { 0, 1, 2, 3, 1, 0, 2, 1, 4 };
        int totalSamples = counts.Sum();
        V3.DeepLevel deepLevel = new(
            0,
            0,
            deepHeader.DataWindow,
            counts,
            deepHeader.Channels,
            new[]
            {
                new V3.ChannelBuffer("A", V3.PixelType.Half, CreateBytes(totalSamples, 2, 31)),
                new V3.ChannelBuffer("Z", V3.PixelType.Float, CreateBytes(totalSamples, 4, 73)),
            });
        V3.Part deepPart = new(deepHeader, new[] { deepLevel }, isComplete: true);
        return new V3.Image(new[] { flatPart, deepPart });
    }

    private static V3.FlatLevel CreateFlatLevel(V3.Header header, int level, V3.Box2i region)
    {
        int pixels = checked((int)(region.Width * region.Height));
        return new V3.FlatLevel(
            level,
            level,
            region,
            new[]
            {
                new V3.ChannelBuffer("A", V3.PixelType.Half, CreateBytes(pixels, 2, 11 + level)),
                new V3.ChannelBuffer("R", V3.PixelType.Float, CreateBytes(pixels, 4, 41 + level)),
            });
    }

    private static byte[] CreateBytes(int samples, int elementSize, int seed)
    {
        byte[] result = new byte[checked(samples * elementSize)];
        uint state = unchecked((uint)seed);
        for (int index = 0; index < result.Length; index++)
        {
            state = unchecked((state * 1_664_525U) + 1_013_904_223U);
            result[index] = (byte)(state >> 24);
        }

        return result;
    }

    private static void AssertImageMatches(
        V3.Image expected,
        V3.Image actual,
        V3.Compression expectedCompression)
    {
        Assert.AreEqual(expected.Parts.Count, actual.Parts.Count);
        for (int partIndex = 0; partIndex < expected.Parts.Count; partIndex++)
        {
            V3.Part expectedPart = expected.Parts[partIndex];
            V3.Part actualPart = actual.Parts[partIndex];
            Assert.AreEqual(expectedPart.Header.Name, actualPart.Header.Name);
            Assert.AreEqual(expectedPart.Header.PartType, actualPart.Header.PartType);
            Assert.AreEqual(expectedCompression, actualPart.Header.Compression);
            Assert.IsTrue(actualPart.IsComplete);
            Assert.AreEqual(expectedPart.Levels.Count, actualPart.Levels.Count);
            for (int levelIndex = 0; levelIndex < expectedPart.Levels.Count; levelIndex++)
            {
                V3.PartLevel expectedLevel = expectedPart.Levels[levelIndex];
                V3.PartLevel actualLevel = actualPart.GetLevel(
                    expectedLevel.LevelX,
                    expectedLevel.LevelY);
                Assert.AreEqual(expectedLevel.Region.MinX, actualLevel.Region.MinX);
                Assert.AreEqual(expectedLevel.Region.MinY, actualLevel.Region.MinY);
                Assert.AreEqual(expectedLevel.Region.MaxX, actualLevel.Region.MaxX);
                Assert.AreEqual(expectedLevel.Region.MaxY, actualLevel.Region.MaxY);
                if (expectedLevel is V3.DeepLevel expectedDeep)
                {
                    V3.DeepLevel actualDeep = (V3.DeepLevel)actualLevel;
                    CollectionAssert.AreEqual(
                        expectedDeep.SampleCounts.ToArray(),
                        actualDeep.SampleCounts.ToArray());
                }

                foreach (V3.ChannelBuffer expectedChannel in expectedLevel.Channels)
                {
                    CollectionAssert.AreEqual(
                        expectedChannel.Data.ToArray(),
                        actualLevel.GetChannel(expectedChannel.Name).Data.ToArray(),
                        $"Part {partIndex}, level ({expectedLevel.LevelX}, {expectedLevel.LevelY}), channel {expectedChannel.Name}");
                }
            }
        }
    }
}
