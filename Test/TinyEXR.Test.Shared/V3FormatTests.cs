using System.Buffers.Binary;
using System.Text;
using TinyEXR.V3.Format;
using V3 = TinyEXR.V3;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3FormatTests
{
    private const uint TiledFlag = 1U << 9;
    private const uint LongNamesFlag = 1U << 10;
    private const uint NonImageFlag = 1U << 11;
    private const uint MultipartFlag = 1U << 12;

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format parses asakusa header and scanline index")]
    public void Case_V3Format_ParsesAsakusaHeaderAndIndex()
    {
        ParsedFile file = ParseFile(TestPaths.Asakusa);
        Assert.AreEqual(2, file.FileVersion);
        Assert.AreEqual(2U, file.RawVersionField);
        Assert.IsFalse(file.Flags.Tiled);
        Assert.IsFalse(file.Flags.LongNames);
        Assert.IsFalse(file.Flags.NonImage);
        Assert.IsFalse(file.Flags.Multipart);
        Assert.AreEqual(8, file.HeadersStart);
        Assert.AreEqual(file.HeadersEnd, file.OffsetTablesStart);
        Assert.IsTrue(file.OffsetTablesEnd < file.FileLength);
        Assert.AreEqual(1, file.Parts.Count);

        ParsedPartIndex part = file.Parts[0];
        Assert.AreEqual(V3.PartType.Scanline, part.Header.PartType);
        Assert.AreEqual(0, part.MissingChunkCount);
        Assert.IsTrue(part.Chunks.Count > 1);
        Assert.IsTrue(part.Chunks.All(chunk => chunk.FileOffset >= (ulong)file.PixelDataStart));
        Assert.IsTrue(part.Chunks.All(chunk => !chunk.Geometry.IsTiled && !chunk.Geometry.IsDeep));
        Assert.IsTrue(part.Chunks.All(chunk => chunk.Geometry.UncompressedByteCount > 0));
        Assert.AreEqual(8, part.Chunks[0].Geometry.ChunkHeaderByteCount);
        AssertRawStandardAttributes(part);
        Assert.IsFalse(part.Header.Attributes.Any(attribute => attribute.Name == "channels"));
        CollectionAssert.AreEqual(
            part.Header.Channels.Select(channel => channel.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            part.Header.Channels.Select(channel => channel.Name).ToArray());
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format indexes multipart headers and offset tables")]
    public void Case_V3Format_IndexesMultipartParts()
    {
        ParsedFile file = ParseFile(TestPaths.OpenExr("Beachball/multipart.0001.exr"));
        Assert.IsTrue(file.Flags.Multipart);
        Assert.IsFalse(file.Flags.Tiled);
        Assert.IsFalse(file.Flags.NonImage);
        Assert.AreEqual(10, file.Parts.Count);
        Assert.AreEqual(10, file.Parts.Select(part => part.Header.Name).Distinct(StringComparer.Ordinal).Count());

        int tableStart = file.OffsetTablesStart;
        for (int i = 0; i < file.Parts.Count; i++)
        {
            ParsedPartIndex part = file.Parts[i];
            Assert.AreEqual(i, part.PartIndex);
            Assert.IsFalse(string.IsNullOrEmpty(part.Header.Name));
            Assert.AreEqual(V3.PartType.Scanline, part.Header.PartType);
            Assert.AreEqual((uint)part.Chunks.Count, part.DeclaredChunkCount);
            Assert.AreEqual(tableStart, part.OffsetTableStart);
            Assert.AreEqual(part.OffsetTableStart + part.Chunks.Count * sizeof(ulong), part.OffsetTableEnd);
            Assert.AreEqual(12, part.Chunks[0].Geometry.ChunkHeaderByteCount);
            Assert.AreEqual(0, part.MissingChunkCount);
            tableStart = part.OffsetTableEnd;
        }

        Assert.AreEqual(file.OffsetTablesEnd, tableStart);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format computes MIPMAP and RIPMAP geometry")]
    public void Case_V3Format_ComputesMipAndRipGeometry()
    {
        ParsedPartIndex mip = ParseFile(TestPaths.OpenExr("MultiResolution/PeriodicPattern.exr")).Parts[0];
        Assert.AreEqual(V3.TileLevelMode.MipmapLevels, mip.Header.Tiles!.LevelMode);
        Assert.AreEqual(122, mip.Chunks.Count);
        ParsedChunkIndex lastMip = mip.Chunks[mip.Chunks.Count - 1];
        Assert.AreEqual(9, lastMip.Geometry.LevelX);
        Assert.AreEqual(9, lastMip.Geometry.LevelY);
        Assert.AreEqual(1L, lastMip.Geometry.Region.Width);
        Assert.AreEqual(1L, lastMip.Geometry.Region.Height);

        ParsedPartIndex rip = ParseFile(TestPaths.OpenExr("MultiResolution/Kapaa.exr")).Parts[0];
        Assert.AreEqual(V3.TileLevelMode.RipmapLevels, rip.Header.Tiles!.LevelMode);
        Assert.AreEqual(V3.TileRoundingMode.RoundUp, rip.Header.Tiles.RoundingMode);
        Assert.AreEqual(858, rip.Chunks.Count);
        ParsedChunkIndex lastRip = rip.Chunks[rip.Chunks.Count - 1];
        Assert.AreEqual(10, lastRip.Geometry.LevelX);
        Assert.AreEqual(10, lastRip.Geometry.LevelY);
        Assert.AreEqual(1L, lastRip.Geometry.Region.Width);
        Assert.AreEqual(1L, lastRip.Geometry.Region.Height);
        Assert.IsTrue(rip.Chunks.All(chunk => chunk.Geometry.IsTiled));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format indexes deep and HTJ2K fixtures without payload decode")]
    public void Case_V3Format_IndexesDeepAndHtj2kFixtures()
    {
        ParsedFile deepScanlineFile = ParseFile(TestPaths.DeepScanline);
        ParsedPartIndex deepScanline = deepScanlineFile.Parts[0];
        Assert.IsTrue(deepScanlineFile.Flags.NonImage);
        Assert.AreEqual(V3.PartType.DeepScanline, deepScanline.Header.PartType);
        Assert.AreEqual(480, deepScanline.Chunks.Count);
        Assert.AreEqual(28, deepScanline.Chunks[0].Geometry.ChunkHeaderByteCount);
        Assert.IsNull(deepScanline.Chunks[0].Geometry.UncompressedByteCount);
        Assert.IsTrue(deepScanline.RawAttributes.Any(attribute => attribute.Value.Name == "version"));
        Assert.IsFalse(deepScanline.Header.Attributes.Any(attribute => attribute.Name == "version"));

        string deepTiledPath = Path.Combine(TestPaths.NativeTinyExrRoot, "data", "deep_tiled_sample.exr");
        ParsedFile deepTiledFile = ParseFile(deepTiledPath);
        ParsedPartIndex deepTiled = deepTiledFile.Parts[0];
        Assert.IsTrue(deepTiledFile.Flags.NonImage);
        Assert.IsFalse(deepTiledFile.Flags.Tiled);
        Assert.AreEqual(V3.PartType.DeepTiled, deepTiled.Header.PartType);
        Assert.AreEqual(4, deepTiled.Chunks.Count);
        Assert.AreEqual(40, deepTiled.Chunks[0].Geometry.ChunkHeaderByteCount);
        Assert.AreEqual(0, deepTiled.Chunks[0].Geometry.TileX);
        Assert.AreEqual(1, deepTiled.Chunks[3].Geometry.TileX);
        Assert.AreEqual(1, deepTiled.Chunks[3].Geometry.TileY);

        ParsedPartIndex htj32 = ParseFile(TestPaths.Regression("2by2_htj2k32.exr")).Parts[0];
        Assert.AreEqual(V3.Compression.HTJ2K32, htj32.Header.Compression);
        Assert.AreEqual(V3.PartType.Scanline, htj32.Header.PartType);
        ParsedPartIndex htj256 = ParseFile(TestPaths.Regression("tiled_htj2k256.exr")).Parts[0];
        Assert.AreEqual(V3.Compression.HTJ2K256, htj256.Header.Compression);
        Assert.AreEqual(V3.PartType.Tiled, htj256.Header.PartType);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format header-only parse preserves metadata and missing offsets")]
    public void Case_V3Format_HeaderOnlyParseDoesNotReadPixelPayload()
    {
        List<SyntheticAttribute> attributes = StandardAttributes(
            V3.Compression.ZIP,
            Box(-2, -3, -1, -2),
            Channels(
                Channel("A", V3.PixelType.Half, pLinear: true),
                Channel("Z", V3.PixelType.Float, xSampling: 2)));
        attributes.Add(new SyntheticAttribute("chromaticities", "chromaticities", Chromaticities()));
        attributes.Add(new SyntheticAttribute("testCustom", "blob", new byte[] { 1, 2, 3, 4 }));
        SyntheticFile headerOnly = BuildSingle(attributes, offsetCount: 1);

        ParsedFile file = ParseMemory(headerOnly.Bytes);
        Assert.AreEqual(headerOnly.Bytes.Length, file.OffsetTablesEnd);
        Assert.AreEqual(file.OffsetTablesEnd, file.PixelDataStart);
        Assert.AreEqual(1, file.Parts[0].MissingChunkCount);
        Assert.IsTrue(file.Parts[0].Chunks[0].IsMissing);
        Assert.AreEqual(-2, file.Parts[0].Header.DataWindow.MinX);
        Assert.AreEqual(-3, file.Parts[0].Header.DataWindow.MinY);
        Assert.AreEqual(2, file.Parts[0].Header.Channels[1].XSampling);
        Assert.IsTrue(file.Parts[0].Header.Channels[0].PerceptuallyLinear);
        Assert.IsNotNull(file.Parts[0].Header.Chromaticities);
        Assert.AreEqual(1, file.Parts[0].Header.Attributes.Count);
        Assert.AreEqual("testCustom", file.Parts[0].Header.Attributes[0].Name);

        SyntheticFile minimalPayload = BuildSingle(
            StandardAttributes(),
            offsetCount: 1,
            payloadByteCount: 8,
            pointOffsetsAtPayload: true);
        Array.Fill(minimalPayload.Bytes, (byte)0xff, minimalPayload.PayloadStart, 8);
        ParsedFile payloadFile = ParseMemory(minimalPayload.Bytes);
        Assert.AreEqual((ulong)minimalPayload.PayloadStart, payloadFile.Parts[0].Chunks[0].FileOffset);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format derives single deep chunks and accepts unknown max samples")]
    public void Case_V3Format_DerivesSingleDeepChunksAndAcceptsUnknownMaxSamples()
    {
        List<SyntheticAttribute> deepScanlineAttributes = DeepAttributes(
            tiled: false,
            dataWindow: Box(0, 0, 1, 1),
            maxSamplesPerPixel: -1);
        ParsedPartIndex deepScanline = ParseMemory(BuildSingle(
            deepScanlineAttributes,
            offsetCount: 2,
            flags: NonImageFlag).Bytes).Parts[0];
        Assert.AreEqual(V3.PartType.DeepScanline, deepScanline.Header.PartType);
        Assert.IsNull(deepScanline.DeclaredChunkCount);
        Assert.AreEqual(2, deepScanline.Chunks.Count);

        List<SyntheticAttribute> declaredDeepScanline = DeepAttributes(
            tiled: false,
            dataWindow: Box(0, 0, 1, 1),
            maxSamplesPerPixel: 0);
        declaredDeepScanline.Add(new SyntheticAttribute("chunkCount", "int", Int32(2)));
        Assert.AreEqual(
            (uint)2,
            ParseMemory(BuildSingle(declaredDeepScanline, 2, flags: NonImageFlag).Bytes)
                .Parts[0]
                .DeclaredChunkCount);

        List<SyntheticAttribute> wrongChunkCount = DeepAttributes(
            tiled: false,
            dataWindow: Box(0, 0, 1, 1),
            maxSamplesPerPixel: 0);
        wrongChunkCount.Add(new SyntheticAttribute("chunkCount", "int", Int32(1)));
        AssertResult(
            BuildSingle(wrongChunkCount, 2, flags: NonImageFlag).Bytes,
            V3.ExrResult.Corrupt,
            "single deep declared chunk count mismatch");

        List<SyntheticAttribute> negativeTwo = DeepAttributes(
            tiled: false,
            dataWindow: Box(0, 0, 0, 0),
            maxSamplesPerPixel: -2);
        AssertResult(
            BuildSingle(negativeTwo, 1, flags: NonImageFlag).Bytes,
            V3.ExrResult.Corrupt,
            "maxSamplesPerPixel below unknown sentinel");

        List<SyntheticAttribute> deepTiledAttributes = DeepAttributes(
            tiled: true,
            dataWindow: Box(0, 0, 3, 3),
            maxSamplesPerPixel: -1);
        ParsedPartIndex deepTiled = ParseMemory(BuildSingle(
            deepTiledAttributes,
            offsetCount: 4,
            flags: NonImageFlag).Bytes).Parts[0];
        Assert.AreEqual(V3.PartType.DeepTiled, deepTiled.Header.PartType);
        Assert.IsNull(deepTiled.DeclaredChunkCount);
        Assert.AreEqual(4, deepTiled.Chunks.Count);

        List<SyntheticAttribute> multipartWithoutChunkCountA = MultipartScanlineAttributes("a", includeChunkCount: false);
        List<SyntheticAttribute> multipartWithoutChunkCountB = MultipartScanlineAttributes("b", includeChunkCount: false);
        AssertResult(
            BuildMultipart(
                new SyntheticPart(multipartWithoutChunkCountA, 1),
                new SyntheticPart(multipartWithoutChunkCountB, 1)),
            V3.ExrResult.Corrupt,
            "multipart still requires chunkCount");
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format validates flat sampling and UTF-8 channel byte order")]
    public void Case_V3Format_ValidatesFlatSamplingAndUtf8ChannelByteOrder()
    {
        byte[] sampledChannels = Channels(Channel("S", V3.PixelType.Half, xSampling: 2, ySampling: 2));
        ParsedPartIndex aligned = ParseMemory(BuildSingle(
            StandardAttributes(dataWindow: Box(-4, -6, 3, 3), channels: sampledChannels),
            offsetCount: 10).Bytes).Parts[0];
        Assert.AreEqual(-4, aligned.Header.DataWindow.MinX);
        Assert.AreEqual(-6, aligned.Header.DataWindow.MinY);

        foreach ((byte[] Window, int OffsetCount, string Name) invalid in new[]
        {
            (Box(-3, -6, 4, 3), 10, "minimum x"),
            (Box(-4, -5, 3, 4), 10, "minimum y"),
            (Box(-4, -6, 2, 3), 10, "width"),
            (Box(-4, -6, 3, 2), 9, "height"),
        })
        {
            AssertResult(
                BuildSingle(
                    StandardAttributes(dataWindow: invalid.Window, channels: sampledChannels),
                    invalid.OffsetCount).Bytes,
                V3.ExrResult.Corrupt,
                $"subsampled {invalid.Name} must align");
        }

        const string bmpPrivateUse = "\ue000";
        const string supplementary = "\U00010000";
        ParsedPartIndex utf8Ordered = ParseMemory(BuildSingle(
            StandardAttributes(channels: Channels(
                Channel(bmpPrivateUse, V3.PixelType.Half),
                Channel(supplementary, V3.PixelType.Half))),
            offsetCount: 1).Bytes).Parts[0];
        CollectionAssert.AreEqual(
            new[] { bmpPrivateUse, supplementary },
            utf8Ordered.Header.Channels.Select(channel => channel.Name).ToArray());

        AssertResult(
            BuildSingle(
                StandardAttributes(channels: Channels(
                    Channel(supplementary, V3.PixelType.Half),
                    Channel(bmpPrivateUse, V3.PixelType.Half))),
                offsetCount: 1).Bytes,
            V3.ExrResult.Corrupt,
            "UTF-8 byte-reversed channels");
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format resolves storage before ignored tile attributes")]
    public void Case_V3Format_ResolvesStorageBeforeIgnoredTileAttributes()
    {
        byte[] ignoredTileData = new byte[] { 9, 8, 7 };
        List<SyntheticAttribute> singleScanline = StandardAttributes();
        singleScanline.Add(new SyntheticAttribute("tiles", "ignored", ignoredTileData));
        ParsedPartIndex single = ParseMemory(BuildSingle(singleScanline, 1).Bytes).Parts[0];
        Assert.AreEqual(V3.PartType.Scanline, single.Header.PartType);
        Assert.IsNull(single.Header.Tiles);
        ParsedAttribute ignored = single.RawAttributes.Single(attribute => attribute.Value.Name == "tiles");
        Assert.AreEqual("ignored", ignored.Value.TypeName);
        CollectionAssert.AreEqual(ignoredTileData, ignored.Value.Data.ToArray());

        List<SyntheticAttribute> multipartA = MultipartScanlineAttributes("a", includeChunkCount: true);
        multipartA.Add(new SyntheticAttribute("tiles", "ignored", ignoredTileData));
        List<SyntheticAttribute> multipartB = MultipartScanlineAttributes("b", includeChunkCount: true);
        ParsedFile multipart = ParseMemory(BuildMultipart(
            new SyntheticPart(multipartA, 1),
            new SyntheticPart(multipartB, 1)));
        Assert.AreEqual(2, multipart.Parts.Count);
        Assert.IsNull(multipart.Parts[0].Header.Tiles);
        Assert.IsTrue(multipart.Parts[0].RawAttributes.Any(attribute => attribute.Value.Name == "tiles"));

        AssertResult(
            BuildSingle(StandardAttributes(), 1, flags: TiledFlag).Bytes,
            V3.ExrResult.Corrupt,
            "tiled storage requires tiles attribute");

        List<SyntheticAttribute> scanlineRandomY = StandardAttributes();
        Replace(scanlineRandomY, "lineOrder", new SyntheticAttribute("lineOrder", "lineOrder", new byte[] { 2 }));
        scanlineRandomY.Add(new SyntheticAttribute("tiles", "tiledesc", TileDescriptionBytes(1, 1)));
        AssertResult(
            BuildSingle(scanlineRandomY, 1).Bytes,
            V3.ExrResult.Corrupt,
            "ignored tiles must not enable RandomY");

        List<SyntheticAttribute> tiledRandomY = StandardAttributes();
        Replace(tiledRandomY, "lineOrder", new SyntheticAttribute("lineOrder", "lineOrder", new byte[] { 2 }));
        tiledRandomY.Add(new SyntheticAttribute("tiles", "tiledesc", TileDescriptionBytes(1, 1)));
        ParsedPartIndex tiled = ParseMemory(BuildSingle(tiledRandomY, 1, flags: TiledFlag).Bytes).Parts[0];
        Assert.AreEqual(V3.PartType.Tiled, tiled.Header.PartType);
        Assert.AreEqual(V3.LineOrder.RandomY, tiled.Header.LineOrder);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format rejects extreme geometry, header offsets, and invalid UTF-8")]
    public void Case_V3Format_RejectsExtremeGeometryHeaderOffsetsAndInvalidUtf8()
    {
        ParsedPartIndex ordinaryNegative = ParseMemory(BuildSingle(
            StandardAttributes(dataWindow: Box(-100, -100, -99, -99)),
            offsetCount: 2).Bytes).Parts[0];
        Assert.AreEqual(-100, ordinaryNegative.Header.DataWindow.MinX);

        int limit = int.MaxValue / 2;
        AssertWindowResult(
            dataWindow: Box(-limit, 0, -limit, 0),
            displayWindow: Box(0, 0, 0, 0),
            "extreme data minimum");
        AssertWindowResult(
            dataWindow: Box(0, limit, 0, limit),
            displayWindow: Box(0, 0, 0, 0),
            "extreme data maximum");
        AssertWindowResult(
            dataWindow: Box(0, 0, 0, 0),
            displayWindow: Box(0, -limit, 0, -limit),
            "extreme display minimum");
        AssertWindowResult(
            dataWindow: Box(0, 0, 0, 0),
            displayWindow: Box(limit, 0, limit, 0),
            "extreme display maximum");

        foreach ((uint TileX, uint TileY, string Name) invalidTile in new[]
        {
            ((uint)int.MaxValue + 1U, 1U, "tile x above int max"),
            (1U, (uint)int.MaxValue + 1U, "tile y above int max"),
        })
        {
            List<SyntheticAttribute> attributes = StandardAttributes();
            attributes.Add(new SyntheticAttribute(
                "tiles",
                "tiledesc",
                TileDescriptionBytes(invalidTile.TileX, invalidTile.TileY)));
            AssertResult(
                BuildSingle(attributes, 1, flags: TiledFlag).Bytes,
                V3.ExrResult.Corrupt,
                invalidTile.Name);
        }

        SyntheticFile headerOffset = BuildSingle(StandardAttributes(), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(
            headerOffset.Bytes.AsSpan(headerOffset.OffsetTableStart),
            8UL);
        AssertResult(headerOffset.Bytes, V3.ExrResult.Corrupt, "chunk offset points into header");

        byte[] invalidAttributeName = BuildSingle(StandardAttributes(), 1).Bytes;
        invalidAttributeName[8] = 0xff;
        AssertResult(invalidAttributeName, V3.ExrResult.Corrupt, "invalid UTF-8 attribute name");

        byte[] invalidAttributeType = BuildSingle(StandardAttributes(), 1).Bytes;
        invalidAttributeType[FindAttributeTypeOffset(invalidAttributeType, "channels")] = 0xff;
        AssertResult(invalidAttributeType, V3.ExrResult.Corrupt, "invalid UTF-8 attribute type");

        byte[] invalidChannelName = BuildSingle(StandardAttributes(), 1).Bytes;
        invalidChannelName[FindAttributeDataOffset(invalidChannelName, "channels")] = 0xff;
        AssertResult(invalidChannelName, V3.ExrResult.Corrupt, "invalid UTF-8 channel name");
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 format handles compression, long names, and malformed bounds")]
    public void Case_V3Format_ValidatesCompressionNamesAndMalformedBounds()
    {
        foreach (V3.Compression compression in Enum.GetValues(typeof(V3.Compression)))
        {
            int blockLines = LinesPerBlock(compression);
            SyntheticFile source = BuildSingle(
                StandardAttributes(compression, Box(0, -5, 0, blockLines - 5)),
                offsetCount: 2);
            ParsedFile file = ParseMemory(source.Bytes);
            Assert.AreEqual(compression, file.Parts[0].Header.Compression);
            Assert.AreEqual(2, file.Parts[0].Chunks.Count);
            Assert.AreEqual(blockLines, file.Parts[0].Chunks[0].Geometry.Region.Height);
            Assert.AreEqual(1L, file.Parts[0].Chunks[1].Geometry.Region.Height);
        }

        AssertResult(BuildWithCustomName(new string('a', 31), 0).Bytes, V3.ExrResult.Success, "short 31");
        AssertResult(BuildWithCustomName(new string('a', 32), 0).Bytes, V3.ExrResult.Corrupt, "short 32");
        AssertResult(BuildWithCustomName(new string('a', 255), LongNamesFlag).Bytes, V3.ExrResult.Success, "long 255");
        AssertResult(BuildWithCustomName(new string('a', 256), LongNamesFlag).Bytes, V3.ExrResult.Corrupt, "long 256");
        AssertResult(BuildWithCustomName(new string('色', 10), 0).Bytes, V3.ExrResult.Success, "UTF-8 30 bytes");
        AssertResult(BuildWithCustomName(new string('色', 11), 0).Bytes, V3.ExrResult.Corrupt, "UTF-8 33 bytes");
        AssertResult(BuildWithCustomName(new string('色', 11), LongNamesFlag).Bytes, V3.ExrResult.Success, "UTF-8 long name");

        ParsedFile onePartMultipart = ParseMemory(BuildOnePartMultipart(), "one-part multipart");
        Assert.IsTrue(onePartMultipart.Flags.Multipart);
        Assert.AreEqual(1, onePartMultipart.Parts.Count);
        Assert.AreEqual("only", onePartMultipart.Parts[0].Header.Name);

        foreach ((byte[] Source, V3.ExrResult Result, string Name) malformed in MalformedFiles())
        {
            AssertResult(malformed.Source, malformed.Result, malformed.Name);
        }
    }

    private static IEnumerable<(byte[] Source, V3.ExrResult Result, string Name)> MalformedFiles()
    {
        yield return (Array.Empty<byte>(), V3.ExrResult.InvalidFile, "truncated preamble");

        byte[] badMagic = BuildSingle(StandardAttributes(), 1).Bytes;
        badMagic[0] ^= 0xff;
        yield return (badMagic, V3.ExrResult.InvalidFile, "bad magic");

        byte[] futureVersion = BuildSingle(StandardAttributes(), 1).Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(futureVersion.AsSpan(4), 3);
        yield return (futureVersion, V3.ExrResult.Unsupported, "future version");

        byte[] futureFlag = BuildSingle(StandardAttributes(), 1).Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(futureFlag.AsSpan(4), 2U | (1U << 13));
        yield return (futureFlag, V3.ExrResult.Unsupported, "future flag");

        List<SyntheticAttribute> missing = StandardAttributes();
        missing.RemoveAll(attribute => attribute.Name == "channels");
        yield return (BuildSingle(missing, 1).Bytes, V3.ExrResult.Corrupt, "missing attribute");

        List<SyntheticAttribute> duplicate = StandardAttributes();
        duplicate.Add(new SyntheticAttribute("compression", "compression", new byte[] { 0 }));
        yield return (BuildSingle(duplicate, 1).Bytes, V3.ExrResult.Corrupt, "duplicate attribute");

        List<SyntheticAttribute> wrongType = StandardAttributes();
        Replace(wrongType, "compression", new SyntheticAttribute("compression", "int", new byte[] { 0 }));
        yield return (BuildSingle(wrongType, 1).Bytes, V3.ExrResult.Corrupt, "wrong standard type");

        List<SyntheticAttribute> futureCompression = StandardAttributes();
        Replace(futureCompression, "compression", new SyntheticAttribute("compression", "compression", new byte[] { 13 }));
        yield return (BuildSingle(futureCompression, 1).Bytes, V3.ExrResult.Unsupported, "future compression");

        List<SyntheticAttribute> badSampling = StandardAttributes(
            channels: Channels(Channel("Y", V3.PixelType.Float, xSampling: 0)));
        yield return (BuildSingle(badSampling, 1).Bytes, V3.ExrResult.Corrupt, "zero sampling");

        List<SyntheticAttribute> badLinear = StandardAttributes(
            channels: Channels(Channel("Y", V3.PixelType.Float, pLinearByte: 2)));
        yield return (BuildSingle(badLinear, 1).Bytes, V3.ExrResult.Corrupt, "bad pLinear");

        List<SyntheticAttribute> unsorted = StandardAttributes(
            channels: Channels(Channel("Z", V3.PixelType.Float), Channel("A", V3.PixelType.Float)));
        yield return (BuildSingle(unsorted, 1).Bytes, V3.ExrResult.Corrupt, "unsorted channels");

        List<SyntheticAttribute> wrongChunkCount = StandardAttributes();
        wrongChunkCount.Add(new SyntheticAttribute("chunkCount", "int", Int32(2)));
        yield return (BuildSingle(wrongChunkCount, 2).Bytes, V3.ExrResult.Corrupt, "chunk count mismatch");

        yield return (BuildSingle(StandardAttributes(dataWindow: Box(1, 0, 0, 0)), 1).Bytes, V3.ExrResult.Corrupt, "reversed window");

        SyntheticFile truncatedTable = BuildSingle(StandardAttributes(), 1);
        yield return (truncatedTable.Bytes.AsSpan(0, truncatedTable.Bytes.Length - 1).ToArray(), V3.ExrResult.Corrupt, "truncated table");

        SyntheticFile outOfRange = BuildSingle(StandardAttributes(), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(outOfRange.Bytes.AsSpan(outOfRange.OffsetTableStart), (ulong)outOfRange.Bytes.Length);
        yield return (outOfRange.Bytes, V3.ExrResult.Corrupt, "offset at eof");

        byte[] maliciousSize = BuildSingle(StandardAttributes(), 1).Bytes;
        BinaryPrimitives.WriteInt32LittleEndian(maliciousSize.AsSpan(FindAttributeSizeOffset(maliciousSize, "compression")), int.MaxValue);
        yield return (maliciousSize, V3.ExrResult.Corrupt, "malicious size");
    }

    private static ParsedFile ParseFile(string path)
    {
        return ParseMemory(File.ReadAllBytes(path), path);
    }

    private static ParsedFile ParseMemory(byte[] source, string? message = null)
    {
        Assert.AreEqual(V3.ExrResult.Success, ExrFormatParser.Parse(source.AsMemory(), out ParsedFile? parsed), message);
        Assert.IsNotNull(parsed, message);
        return parsed!;
    }

    private static void AssertResult(byte[] source, V3.ExrResult expected, string message)
    {
        Assert.AreEqual(expected, ExrFormatParser.Parse(source.AsMemory(), out ParsedFile? parsed), message);
        Assert.AreEqual(expected == V3.ExrResult.Success, parsed != null, message);
    }

    private static void AssertRawStandardAttributes(ParsedPartIndex part)
    {
        foreach (string name in new[]
        {
            "channels", "compression", "dataWindow", "displayWindow", "lineOrder",
            "pixelAspectRatio", "screenWindowCenter", "screenWindowWidth",
        })
        {
            ParsedAttribute attribute = part.RawAttributes.Single(value => value.Value.Name == name);
            Assert.IsTrue(attribute.AttributeStart < attribute.DataStart, name);
            Assert.AreEqual(attribute.DataStart + attribute.Value.ByteLength, attribute.AttributeEnd, name);
        }
    }

    private static List<SyntheticAttribute> StandardAttributes(
        V3.Compression compression = V3.Compression.None,
        byte[]? dataWindow = null,
        byte[]? channels = null)
    {
        byte[] window = dataWindow ?? Box(0, 0, 0, 0);
        return new List<SyntheticAttribute>
        {
            new SyntheticAttribute("channels", "chlist", channels ?? Channels(Channel("Y", V3.PixelType.Half))),
            new SyntheticAttribute("compression", "compression", new[] { (byte)compression }),
            new SyntheticAttribute("dataWindow", "box2i", window),
            new SyntheticAttribute("displayWindow", "box2i", (byte[])window.Clone()),
            new SyntheticAttribute("lineOrder", "lineOrder", new byte[] { 0 }),
            new SyntheticAttribute("pixelAspectRatio", "float", Single(1.0f)),
            new SyntheticAttribute("screenWindowCenter", "v2f", Single(0.0f).Concat(Single(0.0f)).ToArray()),
            new SyntheticAttribute("screenWindowWidth", "float", Single(1.0f)),
        };
    }

    private static List<SyntheticAttribute> DeepAttributes(
        bool tiled,
        byte[] dataWindow,
        int maxSamplesPerPixel)
    {
        List<SyntheticAttribute> attributes = StandardAttributes(dataWindow: dataWindow);
        attributes.Add(new SyntheticAttribute(
            "type",
            "string",
            Encoding.ASCII.GetBytes(tiled ? "deeptile" : "deepscanline")));
        attributes.Add(new SyntheticAttribute("version", "int", Int32(1)));
        attributes.Add(new SyntheticAttribute("maxSamplesPerPixel", "int", Int32(maxSamplesPerPixel)));
        if (tiled)
        {
            attributes.Add(new SyntheticAttribute("tiles", "tiledesc", TileDescriptionBytes(2, 2)));
        }

        return attributes;
    }

    private static List<SyntheticAttribute> MultipartScanlineAttributes(string name, bool includeChunkCount)
    {
        List<SyntheticAttribute> attributes = StandardAttributes();
        attributes.Add(new SyntheticAttribute("name", "string", Encoding.UTF8.GetBytes(name)));
        attributes.Add(new SyntheticAttribute("type", "string", Encoding.ASCII.GetBytes("scanlineimage")));
        if (includeChunkCount)
        {
            attributes.Add(new SyntheticAttribute("chunkCount", "int", Int32(1)));
        }

        return attributes;
    }

    private static void AssertWindowResult(byte[] dataWindow, byte[] displayWindow, string message)
    {
        List<SyntheticAttribute> attributes = StandardAttributes(dataWindow: dataWindow);
        Replace(attributes, "displayWindow", new SyntheticAttribute("displayWindow", "box2i", displayWindow));
        AssertResult(BuildSingle(attributes, 1).Bytes, V3.ExrResult.Corrupt, message);
    }

    private static SyntheticFile BuildWithCustomName(string name, uint flags)
    {
        List<SyntheticAttribute> attributes = StandardAttributes();
        attributes.Add(new SyntheticAttribute(name, "blob", Array.Empty<byte>()));
        return BuildSingle(attributes, 1, flags: flags);
    }

    private static SyntheticFile BuildSingle(
        List<SyntheticAttribute> attributes,
        int offsetCount,
        int payloadByteCount = 0,
        bool pointOffsetsAtPayload = false,
        uint flags = 0)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(ExrFormatParser.Magic);
        writer.Write((uint)ExrFormatParser.SupportedFileVersion | flags);
        WriteHeader(writer, attributes);
        int tableStart = checked((int)stream.Position);
        for (int i = 0; i < offsetCount; i++) writer.Write(0UL);
        int payloadStart = checked((int)stream.Position);
        writer.Write(new byte[payloadByteCount]);
        writer.Flush();
        byte[] result = stream.ToArray();
        if (pointOffsetsAtPayload)
        {
            for (int i = 0; i < offsetCount; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(tableStart + i * sizeof(ulong)), (ulong)payloadStart);
            }
        }

        return new SyntheticFile(result, tableStart, payloadStart);
    }

    private static byte[] BuildMultipart(params SyntheticPart[] parts)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(ExrFormatParser.Magic);
        writer.Write((uint)ExrFormatParser.SupportedFileVersion | MultipartFlag);
        foreach (SyntheticPart part in parts)
        {
            WriteHeader(writer, part.Attributes);
        }

        writer.Write((byte)0);
        foreach (SyntheticPart part in parts)
        {
            for (int i = 0; i < part.OffsetCount; i++)
            {
                writer.Write(0UL);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildOnePartMultipart()
    {
        List<SyntheticAttribute> attributes = StandardAttributes();
        attributes.Add(new SyntheticAttribute("name", "string", Encoding.ASCII.GetBytes("only")));
        attributes.Add(new SyntheticAttribute("type", "string", Encoding.ASCII.GetBytes("scanlineimage")));
        attributes.Add(new SyntheticAttribute("chunkCount", "int", Int32(1)));
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(ExrFormatParser.Magic);
        writer.Write((uint)ExrFormatParser.SupportedFileVersion | MultipartFlag);
        WriteHeader(writer, attributes);
        writer.Write((byte)0);
        writer.Write(0UL);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteHeader(BinaryWriter writer, IEnumerable<SyntheticAttribute> attributes)
    {
        foreach (SyntheticAttribute attribute in attributes)
        {
            WriteCString(writer, attribute.Name);
            WriteCString(writer, attribute.TypeName);
            writer.Write(attribute.Data.Length);
            writer.Write(attribute.Data);
        }
        writer.Write((byte)0);
    }

    private static byte[] Channels(params ChannelDescription[] channels)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (ChannelDescription channel in channels)
        {
            WriteCString(writer, channel.Name);
            writer.Write((int)channel.PixelType);
            writer.Write(channel.PLinear);
            writer.Write(new byte[3]);
            writer.Write(channel.XSampling);
            writer.Write(channel.YSampling);
        }
        writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteCString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    private static ChannelDescription Channel(
        string name,
        V3.PixelType pixelType,
        int xSampling = 1,
        int ySampling = 1,
        bool pLinear = false,
        byte? pLinearByte = null)
    {
        return new ChannelDescription(name, pixelType, xSampling, ySampling, pLinearByte ?? (pLinear ? (byte)1 : (byte)0));
    }

    private static byte[] Box(int minX, int minY, int maxX, int maxY)
    {
        byte[] result = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(result, minX);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), minY);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), maxX);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), maxY);
        return result;
    }

    private static byte[] TileDescriptionBytes(
        uint tileSizeX,
        uint tileSizeY,
        V3.TileLevelMode levelMode = V3.TileLevelMode.OneLevel,
        V3.TileRoundingMode roundingMode = V3.TileRoundingMode.RoundDown)
    {
        byte[] result = new byte[9];
        BinaryPrimitives.WriteUInt32LittleEndian(result, tileSizeX);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), tileSizeY);
        result[8] = (byte)((int)levelMode | ((int)roundingMode << 4));
        return result;
    }

    private static byte[] Chromaticities()
    {
        return new[] { 0.64f, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f, 0.3127f, 0.3290f }
            .SelectMany(Single)
            .ToArray();
    }

    private static byte[] Single(float value)
    {
        byte[] result = new byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(result, BitConverter.SingleToInt32Bits(value));
        return result;
    }

    private static byte[] Int32(int value)
    {
        byte[] result = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(result, value);
        return result;
    }

    private static void Replace(List<SyntheticAttribute> attributes, string name, SyntheticAttribute replacement)
    {
        int index = attributes.FindIndex(attribute => attribute.Name == name);
        Assert.IsTrue(index >= 0, name);
        attributes[index] = replacement;
    }

    private static int FindAttributeSizeOffset(byte[] source, string targetName)
    {
        int offset = 8;
        while (offset < source.Length)
        {
            int nameEnd = Array.IndexOf(source, (byte)0, offset);
            string name = Encoding.UTF8.GetString(source, offset, nameEnd - offset);
            offset = nameEnd + 1;
            int typeEnd = Array.IndexOf(source, (byte)0, offset);
            offset = typeEnd + 1;
            if (name == targetName) return offset;
            int size = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset));
            offset = checked(offset + sizeof(int) + size);
        }
        throw new AssertFailedException(targetName);
    }

    private static int FindAttributeTypeOffset(byte[] source, string targetName)
    {
        int offset = 8;
        while (offset < source.Length)
        {
            int nameEnd = Array.IndexOf(source, (byte)0, offset);
            string name = Encoding.UTF8.GetString(source, offset, nameEnd - offset);
            int typeOffset = nameEnd + 1;
            int typeEnd = Array.IndexOf(source, (byte)0, typeOffset);
            int sizeOffset = typeEnd + 1;
            if (name == targetName)
            {
                return typeOffset;
            }

            int size = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(sizeOffset));
            offset = checked(sizeOffset + sizeof(int) + size);
        }

        throw new AssertFailedException(targetName);
    }

    private static int FindAttributeDataOffset(byte[] source, string targetName)
    {
        return checked(FindAttributeSizeOffset(source, targetName) + sizeof(int));
    }

    private static int LinesPerBlock(V3.Compression compression)
    {
        return compression switch
        {
            V3.Compression.None or V3.Compression.RLE or V3.Compression.ZIPS => 1,
            V3.Compression.ZIP or V3.Compression.PXR24 => 16,
            V3.Compression.PIZ or V3.Compression.B44 or V3.Compression.B44A or
                V3.Compression.DWAA or V3.Compression.HTJ2K32 or V3.Compression.ZSTD => 32,
            V3.Compression.DWAB or V3.Compression.HTJ2K256 => 256,
            _ => throw new ArgumentOutOfRangeException(nameof(compression)),
        };
    }

    private sealed record SyntheticAttribute(string Name, string TypeName, byte[] Data);

    private sealed record SyntheticPart(List<SyntheticAttribute> Attributes, int OffsetCount);

    private readonly record struct ChannelDescription(
        string Name,
        V3.PixelType PixelType,
        int XSampling,
        int YSampling,
        byte PLinear);

    private readonly record struct SyntheticFile(byte[] Bytes, int OffsetTableStart, int PayloadStart);
}
