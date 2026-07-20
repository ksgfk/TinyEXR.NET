using System.Buffers.Binary;
using V3 = TinyEXR.V3;
using V3Codecs = TinyEXR.V3.Codecs;
using V3IO = TinyEXR.V3.IO;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3ReaderTests
{
    private const uint TiledFlag = 1U << 9;
    private const uint NonImageFlag = 1U << 11;
    private const uint MultipartFlag = 1U << 12;

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader opens lazily and gates metadata on ParseHeader")]
    public void Case_V3Reader_OpenIsLazyAndMetadataRequiresParse()
    {
        SyntheticFile file = BuildSingle(StandardAttributes(), offsetCount: 1);
        using SpySource source = new(file.Bytes);
        using V3.ExrReader reader = V3.ExrReader.OpenSource(source);

        Assert.AreEqual(V3.ReaderState.Created, reader.State);
        Assert.AreEqual(0, source.ReadCallCount);
        AssertThrows<InvalidOperationException>(() => _ = reader.NumParts);
        AssertThrows<InvalidOperationException>(() => reader.GetHeader(0));
        AssertThrows<ArgumentOutOfRangeException>(() => reader.GetHeader(-1));

        V3.ReaderResult parsed = reader.ParseHeader();
        Assert.AreEqual(V3.ExrResult.Success, parsed.Status);
        Assert.AreEqual(V3.ReaderState.Ready, reader.State);
        Assert.AreEqual(1, reader.NumParts);
        Assert.AreEqual(V3.PartType.Scanline, reader.GetHeader(0).PartType);
        int callsAfterParse = source.ReadCallCount;
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        Assert.AreEqual(callsAfterParse, source.ReadCallCount);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader parses real single multipart mip rip and deep fixtures")]
    public void Case_V3Reader_ParsesRealFixtureKindsFromMemory()
    {
        AssertFixture(
            TestPaths.OpenExr("Beachball/singlepart.0001.exr"),
            minimumParts: 1,
            expectedPartType: V3.PartType.Scanline);
        AssertFixture(
            TestPaths.OpenExr("Beachball/multipart.0001.exr"),
            minimumParts: 2,
            expectedPartType: null);
        AssertFixture(
            TestPaths.OpenExr("MultiResolution/Bonita.exr"),
            minimumParts: 1,
            expectedPartType: V3.PartType.Tiled,
            expectedLevelMode: V3.TileLevelMode.MipmapLevels);
        AssertFixture(
            TestPaths.OpenExr("MultiResolution/Kapaa.exr"),
            minimumParts: 1,
            expectedPartType: V3.PartType.Tiled,
            expectedLevelMode: V3.TileLevelMode.RipmapLevels);
        AssertFixture(
            TestPaths.DeepScanline,
            minimumParts: 1,
            expectedPartType: V3.PartType.DeepScanline);
        AssertFixture(
            Path.Combine(TestPaths.NativeDataRoot, "deep_tiled_sample.exr"),
            minimumParts: 1,
            expectedPartType: V3.PartType.DeepTiled);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader accepts a single-part multipart header")]
    public void Case_V3Reader_AcceptsSinglePartMultipartHeader()
    {
        byte[] bytes = BuildMultipart(
            new SyntheticPart(MultipartAttributes("only"), 1));
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(bytes);

        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        Assert.AreEqual(1, reader.NumParts);
        Assert.AreEqual("only", reader.GetHeader(0).Name);
        Assert.AreEqual(V3.PartType.Scanline, reader.GetHeader(0).PartType);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader handles seekable stream short reads")]
    public void Case_V3Reader_KnownStreamHandlesShortReads()
    {
        byte[] bytes = File.ReadAllBytes(TestPaths.OpenExr("Beachball/singlepart.0001.exr"));
        using ShortReadMemoryStream stream = new ShortReadMemoryStream(bytes, maximumRead: 3);
        using V3IO.StreamDataSource source = new V3IO.StreamDataSource(stream, leaveOpen: true);
        using V3.ExrReader reader = V3.ExrReader.OpenSource(source);

        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        Assert.IsTrue(stream.ReadCallCount > 20);
        Assert.AreEqual(1, reader.NumParts);
        Assert.IsTrue(reader.GetNumBlocks(0) > 0);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader resumes unknown input byte by byte with exact Pending")]
    public void Case_V3Reader_UnknownSourceResumesAtEveryMetadataBoundary()
    {
        List<SyntheticAttribute> first = MultipartAttributes("first");
        first.Add(new SyntheticAttribute("custom", "blob", Enumerable.Range(0, 37).Select(i => (byte)i).ToArray()));
        byte[] bytes = BuildMultipart(
            new SyntheticPart(first, 1),
            new SyntheticPart(MultipartAttributes("second"), 1));

        using V3IO.SuppliedDataSource source = V3IO.SuppliedDataSource.CreateUnknownLength(
            maximumRetainedBytes: bytes.Length,
            maximumSegmentCount: 16);
        using V3.ExrReader reader = V3.ExrReader.OpenSource(source);

        int supplied = 0;
        int wouldBlockCount = 0;
        while (true)
        {
            V3.ReaderResult result = reader.ParseHeader();
            if (result.Status == V3.ExrResult.Success)
            {
                break;
            }

            Assert.AreEqual(V3.ExrResult.WouldBlock, result.Status);
            Assert.IsTrue(result.Pending.HasValue);
            Assert.IsTrue(reader.Pending.HasValue);
            Assert.AreEqual(supplied, result.Pending.Value.Offset);
            Assert.AreEqual(result.Pending.Value.Offset, reader.Pending.Value.Offset);
            Assert.IsTrue(result.Pending.Value.Length > 0);
            Assert.IsTrue(result.Pending.Value.Length <= 64 * 1024);
            source.Supply(supplied, bytes.AsSpan(supplied, 1));
            supplied++;
            wouldBlockCount++;
            Assert.IsTrue(supplied <= bytes.Length);
        }

        Assert.AreEqual(bytes.Length, supplied);
        Assert.IsTrue(wouldBlockCount > 100);
        Assert.IsFalse(reader.Pending.HasValue);
        Assert.AreEqual(2, reader.NumParts);
        Assert.AreEqual("first", reader.GetHeader(0).Name);
        Assert.AreEqual("second", reader.GetHeader(1).Name);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader maps completed truncation by parse phase")]
    public void Case_V3Reader_CompletedUnknownSourceMapsTruncation()
    {
        SyntheticFile file = BuildSingle(StandardAttributes(), 1);
        AssertCompletedTruncation(file.Bytes, 4, V3.ExrResult.InvalidFile, V3.ReaderState.ReadingPrefix);
        AssertCompletedTruncation(file.Bytes, file.OffsetTableStart - 2, V3.ExrResult.Corrupt, V3.ReaderState.Faulted);
        AssertCompletedTruncation(file.Bytes, file.OffsetTableStart + 4, V3.ExrResult.Corrupt, V3.ReaderState.Faulted);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader never reads pixel bytes and bounds every request")]
    public void Case_V3Reader_ParseIsMetadataOnlyAndReadRequestsAreBounded()
    {
        List<SyntheticAttribute> attributes = StandardAttributes();
        attributes.Add(new SyntheticAttribute("largeCustom", "blob", new byte[150_000]));
        SyntheticFile file = BuildSingle(
            attributes,
            offsetCount: 1,
            payloadByteCount: 64,
            pointOffsetsAtPayload: true);
        V3.ReaderLimits limits = new V3.ReaderLimits(
            maximumAttributeByteCount: 200_000,
            maximumTotalAttributeByteCount: 300_000,
            maximumReadRequestByteCount: 4096);
        using SpySource source = new(file.Bytes);
        using V3.ExrReader reader = V3.ExrReader.OpenSource(
            source,
            new V3.ReaderOptions(limits));

        Assert.AreEqual(0, source.ReadCallCount);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        Assert.IsTrue(source.MaximumRequestLength <= 4096);
        Assert.IsTrue(source.MaximumRequestedEnd <= file.PayloadStart);
        Assert.AreEqual((long)file.PayloadStart, reader.GetBlockInfo(0, 0).FileOffset);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader enforces limits and checked offset range")]
    public void Case_V3Reader_LimitsAndOffsetRangeAreDeterministic()
    {
        List<SyntheticAttribute> attributes = StandardAttributes();
        attributes.Add(new SyntheticAttribute("custom", "blob", new byte[32]));
        SyntheticFile attributeFile = BuildSingle(attributes, 1);
        V3.ReaderLimits smallAttribute = new V3.ReaderLimits(
            maximumAttributeByteCount: 16,
            maximumTotalAttributeByteCount: 1024);
        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(
            attributeFile.Bytes,
            new V3.ReaderOptions(smallAttribute)))
        {
            V3.ReaderResult result = reader.ParseHeader();
            Assert.AreEqual(V3.ExrResult.Unsupported, result.Status);
            Assert.IsTrue(result.Error is V3.ReaderLimitExceededException);
            Assert.AreNotEqual(V3.ReaderState.Faulted, reader.State);
            Assert.AreEqual(result.Status, reader.ParseHeader().Status);
        }

        AssertLimit(
            BuildSingle(StandardAttributes(), 1).Bytes,
            new V3.ReaderLimits(maximumHeaderByteCount: 32));
        AssertLimit(
            BuildSingle(
                StandardAttributes(
                    compression: V3.Compression.None,
                    dataWindow: Box(0, 0, 0, 1)),
                2).Bytes,
            new V3.ReaderLimits(maximumBlocksPerPart: 1, maximumTotalBlocks: 1));
        AssertLimit(
            BuildSingle(
                StandardAttributes(
                    compression: V3.Compression.None,
                    dataWindow: Box(0, 0, 0, 1)),
                2).Bytes,
            new V3.ReaderLimits(maximumOffsetTableByteCount: 8));
        AssertLimit(
            BuildMultipart(
                new SyntheticPart(MultipartAttributes("a"), 1),
                new SyntheticPart(MultipartAttributes("b"), 1)),
            new V3.ReaderLimits(maximumParts: 1));

        SyntheticFile hugeOffset = BuildSingle(StandardAttributes(), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(
            hugeOffset.Bytes.AsSpan(hugeOffset.OffsetTableStart),
            (ulong)long.MaxValue + 1UL);
        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(hugeOffset.Bytes))
        {
            Assert.AreEqual(V3.ExrResult.Unsupported, reader.ParseHeader().Status);
            Assert.AreNotEqual(V3.ReaderState.Faulted, reader.State);
        }

        SyntheticFile headerOffset = BuildSingle(StandardAttributes(), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(
            headerOffset.Bytes.AsSpan(headerOffset.OffsetTableStart),
            8UL);
        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(headerOffset.Bytes))
        {
            Assert.AreEqual(V3.ExrResult.Corrupt, reader.ParseHeader().Status);
            Assert.AreEqual(V3.ReaderState.Faulted, reader.State);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader preflights header length and channel limits before large allocations")]
    public void Case_V3Reader_PreflightsAllocationLimits()
    {
        const int declaredPayloadByteCount = 1024 * 1024;
        byte[] declaredPayload = BuildDeclaredAttributePrefix(
            "custom",
            "blob",
            declaredPayloadByteCount);

        using (SpySource source = new SpySource(declaredPayload))
        using (V3.ExrReader reader = V3.ExrReader.OpenSource(
            source,
            new V3.ReaderOptions(new V3.ReaderLimits(
                maximumHeaderByteCount: 64,
                maximumAttributeByteCount: declaredPayloadByteCount,
                maximumTotalAttributeByteCount: declaredPayloadByteCount))))
        {
            V3.ReaderResult result = reader.ParseHeader();
            Assert.AreEqual(V3.ExrResult.Unsupported, result.Status);
            Assert.IsTrue(result.Error is V3.ReaderLimitExceededException);
            Assert.AreEqual(declaredPayload.Length, source.MaximumRequestedEnd);
            Assert.IsTrue(source.MaximumRequestLength <= sizeof(long));
        }

        using (SpySource source = new SpySource(declaredPayload))
        using (V3.ExrReader reader = V3.ExrReader.OpenSource(
            source,
            new V3.ReaderOptions(new V3.ReaderLimits(
                maximumHeaderByteCount: declaredPayloadByteCount * 2L,
                maximumAttributeByteCount: declaredPayloadByteCount,
                maximumTotalAttributeByteCount: declaredPayloadByteCount))))
        {
            Assert.AreEqual(V3.ExrResult.Corrupt, reader.ParseHeader().Status);
            Assert.AreEqual(declaredPayload.Length, source.MaximumRequestedEnd);
            Assert.IsTrue(source.MaximumRequestLength <= sizeof(long));
        }

        List<SyntheticAttribute> attributes = StandardAttributes();
        attributes[0] = new SyntheticAttribute("channels", "chlist", Channels("A", "B"));
        using V3.ExrReader channelReader = V3.ExrReader.OpenMemory(
            BuildSingle(attributes, 1).Bytes,
            new V3.ReaderOptions(new V3.ReaderLimits(maximumChannelsPerPart: 1)));
        V3.ReaderResult channelResult = channelReader.ParseHeader();
        Assert.AreEqual(V3.ExrResult.Unsupported, channelResult.Status);
        Assert.IsTrue(channelResult.Error is V3.ReaderLimitExceededException);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader derives compact scanline one mip and rip geometry")]
    public void Case_V3Reader_BlockInfoCoversNegativeScanlineAndTileLevels()
    {
        SyntheticFile scanline = BuildSingle(
            StandardAttributes(
                compression: V3.Compression.ZIP,
                dataWindow: Box(-4, -5, 3, 11)),
            offsetCount: 2);
        using (V3.ExrReader reader = Parse(scanline.Bytes))
        {
            V3.BlockInfo first = reader.GetBlockInfo(0, 0);
            V3.BlockInfo last = reader.GetBlockInfo(0, 1);
            Assert.AreEqual(-5, first.Region.MinY);
            Assert.AreEqual(10, first.Region.MaxY);
            Assert.AreEqual(11, last.Region.MinY);
            Assert.AreEqual(11, last.Region.MaxY);
            Assert.IsTrue(first.IsMissing);
            Assert.AreEqual(8, first.ChunkHeaderByteCount);
            Assert.AreEqual(256UL, first.UncompressedByteCount);
        }

        AssertTiledGeometry(V3.TileLevelMode.OneLevel, expectedBlocks: 4, expectedLastLevel: 0);
        AssertTiledGeometry(V3.TileLevelMode.MipmapLevels, expectedBlocks: 6, expectedLastLevel: 2);
        AssertTiledGeometry(V3.TileLevelMode.RipmapLevels, expectedBlocks: 16, expectedLastLevel: 2);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader dispose honors leaveOpen and invalidates operations")]
    public void Case_V3Reader_DisposeAndLeaveOpenContracts()
    {
        SyntheticFile file = BuildSingle(StandardAttributes(), 1);
        SpySource owned = new SpySource(file.Bytes);
        V3.ExrReader ownedReader = V3.ExrReader.OpenSource(
            owned,
            new V3.ReaderOptions(leaveOpen: false));
        ownedReader.Dispose();
        ownedReader.Dispose();
        Assert.IsTrue(owned.IsDisposed);
        Assert.AreEqual(V3.ReaderState.Disposed, ownedReader.State);
        AssertThrows<ObjectDisposedException>(() => ownedReader.ParseHeader());
        AssertThrows<ObjectDisposedException>(() => _ = ownedReader.NumParts);

        using SpySource borrowed = new SpySource(file.Bytes);
        V3.ExrReader borrowedReader = V3.ExrReader.OpenSource(borrowed);
        borrowedReader.Dispose();
        Assert.IsFalse(borrowed.IsDisposed);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader async path genuinely awaits and serializes concurrent parses")]
    public async Task Case_V3Reader_AsyncPathAwaitsAndSerializes()
    {
        SyntheticFile file = BuildSingle(StandardAttributes(), 1);
        using YieldingAsyncSource source = new YieldingAsyncSource(file.Bytes);
        await using V3.ExrReader reader = V3.ExrReader.OpenAsyncSource(source);

        AssertThrows<NotSupportedException>(() => reader.ParseHeader());
        ValueTask<V3.ReaderResult> operation = reader.ParseHeaderAsync();
        Assert.IsFalse(operation.IsCompleted);
        Task<V3.ReaderResult> concurrent = reader.ParseHeaderAsync().AsTask();
        V3.ReaderResult first = await operation;
        V3.ReaderResult second = await concurrent;
        Assert.AreEqual(V3.ExrResult.Success, first.Status);
        Assert.AreEqual(V3.ExrResult.Success, second.Status);
        Assert.AreEqual(1, source.MaximumConcurrentReads);
        Assert.IsTrue(source.ReadCallCount > 1);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader async cancellation leaves parser retryable")]
    public async Task Case_V3Reader_AsyncCancellationIsRetryable()
    {
        SyntheticFile file = BuildSingle(StandardAttributes(), 1);
        using YieldingAsyncSource source = new YieldingAsyncSource(file.Bytes);
        await using V3.ExrReader reader = V3.ExrReader.OpenAsyncSource(source);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await AssertThrowsAsync<OperationCanceledException>(async () =>
            await reader.ParseHeaderAsync(cancellation.Token));
        Assert.AreEqual(V3.ReaderState.Created, reader.State);
        Assert.AreEqual(V3.ExrResult.Success, (await reader.ParseHeaderAsync()).Status);
        Assert.AreEqual(V3.ReaderState.Ready, reader.State);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader source I/O failure leaves parser retryable")]
    public void Case_V3Reader_IoFailureIsRetryable()
    {
        SyntheticFile file = BuildSingle(StandardAttributes(), 1);
        using FailOnceSource source = new FailOnceSource(file.Bytes);
        using V3.ExrReader reader = V3.ExrReader.OpenSource(source);

        V3.ReaderResult failure = reader.ParseHeader();
        Assert.AreEqual(V3.ExrResult.IO, failure.Status);
        Assert.IsTrue(failure.Error is IOException);
        Assert.AreEqual(V3.ReaderState.ReadingPrefix, reader.State);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        Assert.AreEqual(V3.ReaderState.Ready, reader.State);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader decodes flat blocks atomically and validates chunk coordinates")]
    public void Case_V3Reader_DecodeBlockIsAtomicAndValidatesChunkHeader()
    {
        byte[] canonical = { 0x00, 0x3c };
        SyntheticFile file = BuildFlatScanlineBlockFile(
            StandardAttributes(V3.Compression.None),
            canonical,
            minimumY: 0);
        using V3.ExrReader reader = Parse(file.Bytes);
        byte[] destination = { 0xaa, 0xaa, 0x55 };
        V3.ReaderResult result = reader.DecodeBlock(0, 0, destination);
        Assert.AreEqual(V3.ExrResult.Success, result.Status);
        Assert.AreEqual(canonical.Length, result.BytesWritten);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x3c, 0x55 }, destination);

        byte[] tooShort = { 0xcc };
        AssertThrows<ArgumentException>(() => reader.DecodeBlock(0, 0, tooShort));
        Assert.AreEqual((byte)0xcc, tooShort[0]);

        SyntheticFile wrongCoordinate = BuildFlatScanlineBlockFile(
            StandardAttributes(V3.Compression.None),
            canonical,
            minimumY: 1);
        using V3.ExrReader corruptReader = Parse(wrongCoordinate.Bytes);
        byte[] untouched = { 0x77, 0x77 };
        V3.ReaderResult corrupt = corruptReader.DecodeBlock(0, 0, untouched);
        Assert.AreEqual(V3.ExrResult.Corrupt, corrupt.Status);
        CollectionAssert.AreEqual(new byte[] { 0x77, 0x77 }, untouched);
        Assert.AreEqual(V3.ReaderState.Ready, corruptReader.State);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader resumes a bounded block payload without touching the destination")]
    public void Case_V3Reader_DecodeBlockResumesUnknownSource()
    {
        byte[] canonical = new byte[64];
        for (int i = 0; i < canonical.Length; i++)
        {
            canonical[i] = (byte)(i * 17);
        }

        SyntheticFile file = BuildFlatScanlineBlockFile(
            StandardAttributes(
                V3.Compression.None,
                Box(0, 0, 31, 0)),
            canonical,
            minimumY: 0);
        int initiallyAvailable = file.PayloadStart + 8 + 8;
        using V3IO.SuppliedDataSource source = V3IO.SuppliedDataSource.CreateUnknownLength();
        source.Supply(0, file.Bytes.AsSpan(0, initiallyAvailable));
        using V3.ExrReader reader = V3.ExrReader.OpenSource(
            source,
            new V3.ReaderOptions(new V3.ReaderLimits(maximumReadRequestByteCount: 8)));
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);

        byte[] destination = Enumerable.Repeat((byte)0xa5, canonical.Length).ToArray();
        V3.ReaderResult pending = reader.DecodeBlock(0, 0, destination);
        Assert.AreEqual(V3.ExrResult.WouldBlock, pending.Status);
        Assert.IsTrue(pending.Pending.HasValue);
        Assert.AreEqual(initiallyAvailable, pending.Pending.Value.Offset);
        Assert.IsTrue(destination.All(static value => value == 0xa5));

        source.Supply(initiallyAvailable, file.Bytes.AsSpan(initiallyAvailable));
        source.Complete(file.Bytes.Length);
        V3.ReaderResult completed = reader.DecodeBlock(0, 0, destination);
        Assert.AreEqual(V3.ExrResult.Success, completed.Status);
        CollectionAssert.AreEqual(canonical, destination);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader decodes legacy lossless compression blocks")]
    public void Case_V3Reader_DecodeBlockUsesManagedLegacyCodecs()
    {
        CompressionType[] compressions =
        {
            CompressionType.RLE,
            CompressionType.ZIPS,
            CompressionType.ZIP,
            CompressionType.PIZ,
            CompressionType.PXR24,
        };

        const int width = 32;
        const int height = 32;
        byte[] plane = new byte[width * height * sizeof(ushort)];
        for (int i = 0; i < width * height; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                plane.AsSpan(i * sizeof(ushort)),
                (ushort)((i * 29) & 0x7bff));
        }

        foreach (CompressionType compression in compressions)
        {
            ExrChannel channel = new ExrChannel("Y", ExrPixelType.Half);
            ExrImage image = new ExrImage(
                width,
                height,
                new[] { new ExrImageChannel(channel, ExrPixelType.Half, (byte[])plane.Clone()) });
            ExrHeader header = new ExrHeader
            {
                Compression = compression,
                DataWindow = new ExrBox2i(0, 0, width - 1, height - 1),
                DisplayWindow = new ExrBox2i(0, 0, width - 1, height - 1),
            };
            header.Channels.Add(channel);
            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded), compression.ToString());

            using V3.ExrReader reader = Parse(encoded);
            V3.BlockInfo info = reader.GetBlockInfo(0, 0);
            byte[] decoded = new byte[checked((int)info.UncompressedByteCount!.Value)];
            V3.ReaderResult result = reader.DecodeBlock(0, 0, decoded);
            Assert.AreEqual(V3.ExrResult.Success, result.Status, compression.ToString());
            CollectionAssert.AreEqual(plane.AsSpan(0, decoded.Length).ToArray(), decoded, compression.ToString());
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader decodes ZSTD EXR blocks and genuinely awaits async input")]
    public async Task Case_V3Reader_DecodeBlockSupportsZstdAndAsync()
    {
        byte[] canonical = Enumerable.Repeat((byte)0x42, 64).ToArray();
        Assert.AreEqual(
            V3Codecs.ZstdFrameStatus.Success,
            V3Codecs.ZstdRawRleEncoder.GetEncodedSize(canonical, includeChecksum: true, out int encodedSize));
        byte[] payload = new byte[encodedSize];
        Assert.AreEqual(
            V3Codecs.ZstdFrameStatus.Success,
            V3Codecs.ZstdRawRleEncoder.Encode(
                canonical,
                payload,
                includeChecksum: true,
                out int bytesWritten));
        Assert.AreEqual(payload.Length, bytesWritten);

        SyntheticFile file = BuildFlatScanlineBlockFile(
            StandardAttributes(
                V3.Compression.ZSTD,
                Box(0, 0, 31, 0)),
            payload,
            minimumY: 0);
        using YieldingAsyncSource source = new YieldingAsyncSource(file.Bytes);
        await using V3.ExrReader reader = V3.ExrReader.OpenAsyncSource(source);
        Assert.AreEqual(V3.ExrResult.Success, (await reader.ParseHeaderAsync()).Status);
        byte[] destination = new byte[canonical.Length];
        TaskCompletionSource<bool> readGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.ReadGate = readGate.Task;
        ValueTask<V3.ReaderResult> operation = reader.DecodeBlockAsync(0, 0, destination);
        bool completedWithoutInput = operation.IsCompleted;
        source.ReadGate = null;
        readGate.SetResult(true);
        Assert.IsFalse(completedWithoutInput);
        V3.ReaderResult result = await operation;
        Assert.AreEqual(V3.ExrResult.Success, result.Status);
        CollectionAssert.AreEqual(canonical, destination);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader validates tiled and multipart chunk identities")]
    public void Case_V3Reader_DecodeBlockValidatesTiledAndMultipartIdentities()
    {
        byte[] canonical = { 0x00, 0x3c, 0x00, 0x40, 0x00, 0x42, 0x00, 0x44 };
        List<SyntheticAttribute> tiledAttributes = StandardAttributes(
            V3.Compression.None,
            Box(0, 0, 1, 1));
        tiledAttributes.Add(new SyntheticAttribute(
            "tiles",
            "tiledesc",
            TileDescription(2, 2, V3.TileLevelMode.OneLevel, V3.TileRoundingMode.RoundDown)));

        SyntheticFile tiled = BuildFlatTiledBlockFile(
            tiledAttributes,
            canonical,
            tileX: 0,
            tileY: 0,
            levelX: 0,
            levelY: 0);
        using (V3.ExrReader reader = Parse(tiled.Bytes))
        {
            byte[] destination = new byte[canonical.Length];
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeBlock(0, 0, destination).Status);
            CollectionAssert.AreEqual(canonical, destination);
        }

        SyntheticFile wrongTile = BuildFlatTiledBlockFile(
            tiledAttributes,
            canonical,
            tileX: 1,
            tileY: 0,
            levelX: 0,
            levelY: 0);
        using (V3.ExrReader reader = Parse(wrongTile.Bytes))
        {
            byte[] destination = Enumerable.Repeat((byte)0x5a, canonical.Length).ToArray();
            Assert.AreEqual(V3.ExrResult.Corrupt, reader.DecodeBlock(0, 0, destination).Status);
            Assert.IsTrue(destination.All(static value => value == 0x5a));
        }

        SyntheticFile multipart = BuildMultipartFlatScanlineBlockFile(
            MultipartAttributes("beauty"),
            new byte[] { 0x00, 0x3c },
            partNumber: 0,
            minimumY: 0);
        using (V3.ExrReader reader = Parse(multipart.Bytes))
        {
            byte[] destination = new byte[2];
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeBlock(0, 0, destination).Status);
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x3c }, destination);
        }

        SyntheticFile wrongPart = BuildMultipartFlatScanlineBlockFile(
            MultipartAttributes("beauty"),
            new byte[] { 0x00, 0x3c },
            partNumber: 1,
            minimumY: 0);
        using (V3.ExrReader reader = Parse(wrongPart.Bytes))
        {
            byte[] destination = { 0x6b, 0x6b };
            Assert.AreEqual(V3.ExrResult.Corrupt, reader.DecodeBlock(0, 0, destination).Status);
            CollectionAssert.AreEqual(new byte[] { 0x6b, 0x6b }, destination);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader accepts raw codec fallback and rejects oversized packed blocks")]
    public void Case_V3Reader_DecodeBlockEnforcesRawFallbackSizeRules()
    {
        byte[] canonical = { 0x00, 0x3c, 0x00, 0x40 };
        V3.Compression[] rawFallbackCodecs =
        {
            V3.Compression.ZIP,
            V3.Compression.ZSTD,
            V3.Compression.DWAA,
            V3.Compression.HTJ2K256,
        };

        foreach (V3.Compression compression in rawFallbackCodecs)
        {
            SyntheticFile file = BuildFlatScanlineBlockFile(
                StandardAttributes(compression, Box(0, 0, 1, 0)),
                canonical,
                minimumY: 0);
            using V3.ExrReader reader = Parse(file.Bytes);
            byte[] destination = new byte[canonical.Length];
            V3.ReaderResult result = reader.DecodeBlock(0, 0, destination);
            Assert.AreEqual(V3.ExrResult.Success, result.Status, compression.ToString());
            CollectionAssert.AreEqual(canonical, destination, compression.ToString());
        }

        SyntheticFile oversized = BuildFlatScanlineBlockFile(
            StandardAttributes(V3.Compression.ZIP, Box(0, 0, 1, 0)),
            new byte[canonical.Length + 1],
            minimumY: 0);
        using (V3.ExrReader reader = Parse(oversized.Bytes))
        {
            byte[] destination = Enumerable.Repeat((byte)0xcc, canonical.Length).ToArray();
            Assert.AreEqual(V3.ExrResult.Corrupt, reader.DecodeBlock(0, 0, destination).Status);
            Assert.IsTrue(destination.All(static value => value == 0xcc));
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 block reads resume after synchronous I/O and asynchronous cancellation")]
    public async Task Case_V3Reader_DecodeBlockRetriesIoAndCancellationAtomically()
    {
        byte[] canonical = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        SyntheticFile file = BuildFlatScanlineBlockFile(
            StandardAttributes(V3.Compression.None, Box(0, 0, 31, 0)),
            canonical,
            minimumY: 0);

        using (FailOnceSource source = new FailOnceSource(file.Bytes))
        using (V3.ExrReader reader = V3.ExrReader.OpenSource(source))
        {
            Assert.AreEqual(V3.ExrResult.IO, reader.ParseHeader().Status);
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            source.FailNextRead = true;
            byte[] destination = Enumerable.Repeat((byte)0xa5, canonical.Length).ToArray();
            Assert.AreEqual(V3.ExrResult.IO, reader.DecodeBlock(0, 0, destination).Status);
            Assert.IsTrue(destination.All(static value => value == 0xa5));
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeBlock(0, 0, destination).Status);
            CollectionAssert.AreEqual(canonical, destination);
        }

        using YieldingAsyncSource asyncSource = new YieldingAsyncSource(file.Bytes);
        await using V3.ExrReader asyncReader = V3.ExrReader.OpenAsyncSource(asyncSource);
        Assert.AreEqual(V3.ExrResult.Success, (await asyncReader.ParseHeaderAsync()).Status);
        asyncSource.CancelNextRead = true;
        byte[] asyncDestination = Enumerable.Repeat((byte)0x5a, canonical.Length).ToArray();
        await AssertThrowsAsync<OperationCanceledException>(
            () => asyncReader.DecodeBlockAsync(0, 0, asyncDestination).AsTask());
        Assert.IsTrue(asyncDestination.All(static value => value == 0x5a));
        Assert.AreEqual(
            V3.ExrResult.Success,
            (await asyncReader.DecodeBlockAsync(0, 0, asyncDestination)).Status);
        CollectionAssert.AreEqual(canonical, asyncDestination);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader lazily reconstructs zero chunk offsets atomically")]
    public void Case_V3Reader_ReconstructsZeroOffsetsAtomically()
    {
        byte[][] payloads =
        {
            new byte[] { 0x00, 0x3c },
            new byte[] { 0x00, 0x40 },
        };
        SyntheticFile valid = BuildFlatScanlineBlocksFile(
            StandardAttributes(V3.Compression.None, Box(0, 0, 0, 1)),
            payloads,
            new[] { 0, 1 },
            writeOffsets: false);
        using (V3.ExrReader reader = Parse(valid.Bytes))
        {
            Assert.IsTrue(reader.GetBlockInfo(0, 0).IsMissing);
            Assert.IsTrue(reader.GetBlockInfo(0, 1).IsMissing);
            byte[] destination = { 0xa5, 0xa5 };
            V3.ReaderResult result = reader.DecodeBlock(0, 0, destination);
            Assert.AreEqual(V3.ExrResult.Success, result.Status);
            CollectionAssert.AreEqual(payloads[0], destination);
            Assert.IsFalse(reader.GetBlockInfo(0, 0).IsMissing);
            Assert.IsFalse(reader.GetBlockInfo(0, 1).IsMissing);
        }

        SyntheticFile corrupt = BuildFlatScanlineBlocksFile(
            StandardAttributes(V3.Compression.None, Box(0, 0, 0, 1)),
            payloads,
            new[] { 0, 0 },
            writeOffsets: false);
        using (V3.ExrReader reader = Parse(corrupt.Bytes))
        {
            byte[] destination = { 0x5a, 0x5a };
            Assert.AreEqual(V3.ExrResult.Corrupt, reader.DecodeBlock(0, 0, destination).Status);
            CollectionAssert.AreEqual(new byte[] { 0x5a, 0x5a }, destination);
            Assert.IsTrue(reader.GetBlockInfo(0, 0).IsMissing);
            Assert.IsTrue(reader.GetBlockInfo(0, 1).IsMissing);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 chunk-offset reconstruction resumes without partial publication")]
    public void Case_V3Reader_ReconstructionResumesWouldBlockWithoutPublishing()
    {
        byte[][] payloads =
        {
            new byte[] { 0x00, 0x3c },
            new byte[] { 0x00, 0x40 },
        };
        SyntheticFile file = BuildFlatScanlineBlocksFile(
            StandardAttributes(V3.Compression.None, Box(0, 0, 0, 1)),
            payloads,
            new[] { 0, 1 },
            writeOffsets: false);
        int supplied = file.PayloadStart + 8 + payloads[0].Length + 3;
        using V3IO.SuppliedDataSource source = V3IO.SuppliedDataSource.CreateUnknownLength();
        source.Supply(0, file.Bytes.AsSpan(0, supplied));
        using V3.ExrReader reader = V3.ExrReader.OpenSource(source);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);

        byte[] destination = { 0xcc, 0xcc };
        V3.ReaderResult pending = reader.DecodeBlock(0, 0, destination);
        Assert.AreEqual(V3.ExrResult.WouldBlock, pending.Status);
        Assert.IsTrue(pending.Pending.HasValue);
        Assert.IsTrue(reader.GetBlockInfo(0, 0).IsMissing);
        Assert.IsTrue(reader.GetBlockInfo(0, 1).IsMissing);
        CollectionAssert.AreEqual(new byte[] { 0xcc, 0xcc }, destination);

        source.Supply(supplied, file.Bytes.AsSpan(supplied));
        source.Complete(file.Bytes.Length);
        Assert.AreEqual(V3.ExrResult.Success, reader.DecodeBlock(0, 0, destination).Status);
        CollectionAssert.AreEqual(payloads[0], destination);
        Assert.IsFalse(reader.GetBlockInfo(0, 0).IsMissing);
        Assert.IsFalse(reader.GetBlockInfo(0, 1).IsMissing);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 offset reconstruction maps tiled multipart and decreasing-Y chunks")]
    public void Case_V3Reader_ReconstructionMapsAllFlatChunkKinds()
    {
        byte[] tiledCanonical = { 0x00, 0x3c, 0x00, 0x40, 0x00, 0x42, 0x00, 0x44 };
        List<SyntheticAttribute> tiledAttributes = StandardAttributes(
            V3.Compression.None,
            Box(0, 0, 1, 1));
        tiledAttributes.Add(new SyntheticAttribute(
            "tiles",
            "tiledesc",
            TileDescription(2, 2, V3.TileLevelMode.OneLevel, V3.TileRoundingMode.RoundDown)));
        SyntheticFile tiled = BuildFlatTiledBlockFile(
            tiledAttributes,
            tiledCanonical,
            tileX: 0,
            tileY: 0,
            levelX: 0,
            levelY: 0,
            writeOffset: false);
        using (V3.ExrReader reader = Parse(tiled.Bytes))
        {
            byte[] destination = new byte[tiledCanonical.Length];
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeBlock(0, 0, destination).Status);
            CollectionAssert.AreEqual(tiledCanonical, destination);
            Assert.IsFalse(reader.GetBlockInfo(0, 0).IsMissing);
        }

        SyntheticFile multipart = BuildMultipartFlatScanlineBlockFile(
            MultipartAttributes("beauty"),
            new byte[] { 0x00, 0x3c },
            partNumber: 0,
            minimumY: 0,
            writeOffsets: false);
        using (V3.ExrReader reader = Parse(multipart.Bytes))
        {
            byte[] destination = new byte[2];
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeBlock(0, 0, destination).Status);
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x3c }, destination);
            Assert.IsFalse(reader.GetBlockInfo(0, 0).IsMissing);
            Assert.IsFalse(reader.GetBlockInfo(1, 0).IsMissing);
        }

        List<SyntheticAttribute> decreasingAttributes = StandardAttributes(
            V3.Compression.None,
            Box(0, 0, 0, 1));
        decreasingAttributes.RemoveAll(static attribute => attribute.Name == "lineOrder");
        decreasingAttributes.Add(new SyntheticAttribute("lineOrder", "lineOrder", new byte[] { 1 }));
        byte[][] decreasingPayloads =
        {
            new byte[] { 0x00, 0x40 },
            new byte[] { 0x00, 0x3c },
        };
        SyntheticFile decreasing = BuildFlatScanlineBlocksFile(
            decreasingAttributes,
            decreasingPayloads,
            new[] { 1, 0 },
            writeOffsets: false);
        using (V3.ExrReader reader = Parse(decreasing.Bytes))
        {
            byte[] destination = new byte[2];
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeBlock(0, 0, destination).Status);
            CollectionAssert.AreEqual(decreasingPayloads[1], destination);
            Assert.IsTrue(reader.GetBlockInfo(0, 0).FileOffset > reader.GetBlockInfo(0, 1).FileOffset);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 offset reconstruction retries synchronous I/O and async cancellation")]
    public async Task Case_V3Reader_ReconstructionRetriesIoAndCancellation()
    {
        byte[] canonical = { 0x00, 0x3c };
        SyntheticFile file = BuildFlatScanlineBlocksFile(
            StandardAttributes(V3.Compression.None),
            new[] { canonical },
            new[] { 0 },
            writeOffsets: false);

        using (FailOnceSource source = new FailOnceSource(file.Bytes))
        using (V3.ExrReader reader = V3.ExrReader.OpenSource(source))
        {
            Assert.AreEqual(V3.ExrResult.IO, reader.ParseHeader().Status);
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            source.FailNextRead = true;
            byte[] destination = { 0xa5, 0xa5 };
            Assert.AreEqual(V3.ExrResult.IO, reader.DecodeBlock(0, 0, destination).Status);
            Assert.IsTrue(reader.GetBlockInfo(0, 0).IsMissing);
            CollectionAssert.AreEqual(new byte[] { 0xa5, 0xa5 }, destination);
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeBlock(0, 0, destination).Status);
            CollectionAssert.AreEqual(canonical, destination);
        }

        using YieldingAsyncSource asyncSource = new YieldingAsyncSource(file.Bytes);
        await using V3.ExrReader asyncReader = V3.ExrReader.OpenAsyncSource(asyncSource);
        Assert.AreEqual(V3.ExrResult.Success, (await asyncReader.ParseHeaderAsync()).Status);
        asyncSource.CancelNextRead = true;
        byte[] asyncDestination = { 0x5a, 0x5a };
        await AssertThrowsAsync<OperationCanceledException>(
            () => asyncReader.DecodeBlockAsync(0, 0, asyncDestination).AsTask());
        Assert.IsTrue(asyncReader.GetBlockInfo(0, 0).IsMissing);
        CollectionAssert.AreEqual(new byte[] { 0x5a, 0x5a }, asyncDestination);
        Assert.AreEqual(
            V3.ExrResult.Success,
            (await asyncReader.DecodeBlockAsync(0, 0, asyncDestination)).Status);
        CollectionAssert.AreEqual(canonical, asyncDestination);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep block decode publishes row-reset counts and planar samples atomically")]
    public void Case_V3Reader_DeepBlockDecodesCountsThenSamples()
    {
        ChannelSpec[] channels =
        {
            new ChannelSpec("A", V3.PixelType.Half, 1, 1),
            new ChannelSpec("Z", V3.PixelType.Float, 1, 1),
        };
        int[] expectedCounts = { 1, 2, 0, 1 };
        DeepSyntheticFile file = BuildDeepBlockFile(
            V3.Compression.ZIP,
            new V3.Box2i(-1, 4, 0, 5),
            channels,
            expectedCounts);

        using V3.ExrReader reader = Parse(file.Bytes);
        int[] counts = Enumerable.Repeat(-1, expectedCounts.Length).ToArray();
        V3.ReaderResult countResult = reader.DecodeDeepCounts(0, 0, counts);
        Assert.AreEqual(V3.ExrResult.Success, countResult.Status, countResult.Error?.ToString());
        Assert.AreEqual(expectedCounts.Length * sizeof(int), countResult.BytesWritten);
        CollectionAssert.AreEqual(expectedCounts, counts);

        byte[][] actualChannels = file.ExpectedChannels
            .Select(static data => Enumerable.Repeat((byte)0xa5, data.Length).ToArray())
            .ToArray();
        V3.DeepChannelDestination[] destinations = channels
            .Select((channel, index) => new V3.DeepChannelDestination(channel.Name, actualChannels[index]))
            .ToArray();
        V3.ReaderResult sampleResult = reader.DecodeDeepSamples(0, 0, counts, destinations);
        Assert.AreEqual(V3.ExrResult.Success, sampleResult.Status, sampleResult.Error?.ToString());
        Assert.AreEqual(file.ExpectedChannels.Sum(static data => data.Length), sampleResult.BytesWritten);
        for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
        {
            CollectionAssert.AreEqual(
                file.ExpectedChannels[channelIndex],
                actualChannels[channelIndex],
                channels[channelIndex].Name);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep block supports raw RLE ZIP ZIPS and ZSTD payloads")]
    public void Case_V3Reader_DeepBlockCompressionMatrix()
    {
        V3.Compression[] compressions =
        {
            V3.Compression.None,
            V3.Compression.RLE,
            V3.Compression.ZIPS,
            V3.Compression.ZIP,
            V3.Compression.ZSTD,
        };
        ChannelSpec[] channels =
        {
            new ChannelSpec("A", V3.PixelType.Half, 1, 1),
            new ChannelSpec("Z", V3.PixelType.Float, 1, 1),
        };

        foreach (V3.Compression compression in compressions)
        {
            int[] expectedCounts = Enumerable.Range(0, 128)
                .Select(static index => index % 5 == 0 ? 0 : (index % 3) + 1)
                .ToArray();
            DeepSyntheticFile file = BuildDeepBlockFile(
                compression,
                new V3.Box2i(0, 0, 127, 0),
                channels,
                expectedCounts);
            long packedCountByteCount = BinaryPrimitives.ReadInt64LittleEndian(
                file.Bytes.AsSpan(file.ChunkStart + sizeof(int), sizeof(long)));
            long packedSampleByteCount = BinaryPrimitives.ReadInt64LittleEndian(
                file.Bytes.AsSpan(file.ChunkStart + sizeof(int) + sizeof(long), sizeof(long)));
            if (compression == V3.Compression.None)
            {
                Assert.AreEqual(expectedCounts.Length * sizeof(int), packedCountByteCount);
                Assert.AreEqual(file.ExpectedChannels.Sum(static data => data.Length), packedSampleByteCount);
            }
            else
            {
                Assert.AreNotEqual(expectedCounts.Length * sizeof(int), packedCountByteCount, compression.ToString());
                Assert.AreNotEqual(
                    file.ExpectedChannels.Sum(static data => data.Length),
                    packedSampleByteCount,
                    compression.ToString());
            }

            using V3.ExrReader reader = Parse(file.Bytes);
            int[] counts = new int[expectedCounts.Length];
            V3.ReaderResult countResult = reader.DecodeDeepCounts(0, 0, counts);
            Assert.AreEqual(V3.ExrResult.Success, countResult.Status, $"{compression}: {countResult.Error}");
            CollectionAssert.AreEqual(expectedCounts, counts, compression.ToString());

            byte[][] actualChannels = file.ExpectedChannels
                .Select(static data => new byte[data.Length])
                .ToArray();
            V3.DeepChannelDestination[] destinations = channels
                .Select((channel, index) => new V3.DeepChannelDestination(channel.Name, actualChannels[index]))
                .ToArray();
            V3.ReaderResult sampleResult = reader.DecodeDeepSamples(0, 0, counts, destinations);
            Assert.AreEqual(V3.ExrResult.Success, sampleResult.Status, $"{compression}: {sampleResult.Error}");
            for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                CollectionAssert.AreEqual(
                    file.ExpectedChannels[channelIndex],
                    actualChannels[channelIndex],
                    $"{compression}: {channels[channelIndex].Name}");
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep block rejects corrupt counts sizes limits and mismatched destinations atomically")]
    public void Case_V3Reader_DeepBlockFailureKindsAreAtomic()
    {
        ChannelSpec[] channels =
        {
            new ChannelSpec("A", V3.PixelType.Half, 1, 1),
            new ChannelSpec("Z", V3.PixelType.Float, 1, 1),
        };
        int[] expectedCounts = { 1, 2 };

        DeepSyntheticFile nonMonotonic = BuildDeepBlockFile(
            V3.Compression.None,
            new V3.Box2i(0, 0, 1, 0),
            channels,
            expectedCounts);
        BinaryPrimitives.WriteInt32LittleEndian(
            nonMonotonic.Bytes.AsSpan(nonMonotonic.CountPayloadStart + sizeof(int), sizeof(int)),
            0);
        using (V3.ExrReader reader = Parse(nonMonotonic.Bytes))
        {
            int[] counts = { -9, -9 };
            Assert.AreEqual(V3.ExrResult.Corrupt, reader.DecodeDeepCounts(0, 0, counts).Status);
            CollectionAssert.AreEqual(new[] { -9, -9 }, counts);
        }

        DeepSyntheticFile sizeMismatch = BuildDeepBlockFile(
            V3.Compression.RLE,
            new V3.Box2i(0, 0, 1, 0),
            channels,
            expectedCounts);
        long declared = BinaryPrimitives.ReadInt64LittleEndian(
            sizeMismatch.Bytes.AsSpan(sizeMismatch.UnpackedSizeOffset, sizeof(long)));
        BinaryPrimitives.WriteInt64LittleEndian(
            sizeMismatch.Bytes.AsSpan(sizeMismatch.UnpackedSizeOffset, sizeof(long)),
            declared + 1);
        using (V3.ExrReader reader = Parse(sizeMismatch.Bytes))
        {
            int[] counts = { -7, -7 };
            Assert.AreEqual(V3.ExrResult.Corrupt, reader.DecodeDeepCounts(0, 0, counts).Status);
            CollectionAssert.AreEqual(new[] { -7, -7 }, counts);
        }

        DeepSyntheticFile valid = BuildDeepBlockFile(
            V3.Compression.ZSTD,
            new V3.Box2i(0, 0, 1, 0),
            channels,
            expectedCounts);
        using (V3.ExrReader limited = V3.ExrReader.OpenMemory(
            valid.Bytes,
            new V3.ReaderOptions(new V3.ReaderLimits(maximumDeepSampleCount: 2))))
        {
            Assert.AreEqual(V3.ExrResult.Success, limited.ParseHeader().Status);
            int[] counts = { -5, -5 };
            V3.ReaderResult result = limited.DecodeDeepCounts(0, 0, counts);
            Assert.AreEqual(V3.ExrResult.Unsupported, result.Status);
            Assert.IsTrue(result.Error is V3.ReaderLimitExceededException);
            CollectionAssert.AreEqual(new[] { -5, -5 }, counts);
        }

        using (V3.ExrReader reader = Parse(valid.Bytes))
        {
            byte[] untouched = Enumerable.Repeat((byte)0xcc, valid.ExpectedChannels[0].Length).ToArray();
            V3.DeepChannelDestination[] destinations =
            {
                new V3.DeepChannelDestination("A", untouched),
                new V3.DeepChannelDestination("Z", new byte[valid.ExpectedChannels[1].Length]),
            };
            AssertThrows<InvalidOperationException>(
                () => reader.DecodeDeepSamples(0, 0, expectedCounts, destinations));

            int[] counts = new int[expectedCounts.Length];
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeDeepCounts(0, 0, counts).Status);
            AssertThrows<ArgumentException>(
                () => reader.DecodeDeepSamples(0, 0, new[] { 1, 1 }, destinations));
            AssertThrows<ArgumentException>(() => reader.DecodeDeepSamples(
                0,
                0,
                counts,
                new[]
                {
                    new V3.DeepChannelDestination("Z", new byte[valid.ExpectedChannels[1].Length]),
                    new V3.DeepChannelDestination("A", untouched),
                }));
            CollectionAssert.AreEqual(
                Enumerable.Repeat((byte)0xcc, untouched.Length).ToArray(),
                untouched);
        }

        DeepSyntheticFile corruptSamples = BuildDeepBlockFile(
            V3.Compression.ZSTD,
            new V3.Box2i(0, 0, 1, 0),
            channels,
            expectedCounts);
        corruptSamples.Bytes[corruptSamples.SamplePayloadStart] ^= 0xff;
        using (V3.ExrReader reader = Parse(corruptSamples.Bytes))
        {
            int[] counts = new int[expectedCounts.Length];
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeDeepCounts(0, 0, counts).Status);
            byte[][] actual = corruptSamples.ExpectedChannels
                .Select(static data => Enumerable.Repeat((byte)0x6d, data.Length).ToArray())
                .ToArray();
            V3.ReaderResult result = reader.DecodeDeepSamples(
                0,
                0,
                counts,
                channels.Select((channel, index) =>
                    new V3.DeepChannelDestination(channel.Name, actual[index])).ToArray());
            Assert.AreEqual(V3.ExrResult.Corrupt, result.Status);
            Assert.IsTrue(actual.All(static data => data.All(static value => value == 0x6d)));
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep block resumes WouldBlock I/O and async cancellation without partial writes")]
    public async Task Case_V3Reader_DeepBlockResumesTransientFailures()
    {
        ChannelSpec[] channels = { new ChannelSpec("Z", V3.PixelType.Float, 1, 1) };
        int[] expectedCounts = { 1, 2, 0, 1 };
        DeepSyntheticFile file = BuildDeepBlockFile(
            V3.Compression.ZSTD,
            new V3.Box2i(0, 0, 3, 0),
            channels,
            expectedCounts);

        int initiallyAvailable = file.CountPayloadStart + 1;
        using (V3IO.SuppliedDataSource source = V3IO.SuppliedDataSource.CreateUnknownLength())
        {
            source.Supply(0, file.Bytes.AsSpan(0, initiallyAvailable));
            using V3.ExrReader reader = V3.ExrReader.OpenSource(source);
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            int[] counts = Enumerable.Repeat(-1, expectedCounts.Length).ToArray();
            Assert.AreEqual(V3.ExrResult.WouldBlock, reader.DecodeDeepCounts(0, 0, counts).Status);
            Assert.IsTrue(counts.All(static count => count == -1));

            source.Supply(
                initiallyAvailable,
                file.Bytes.AsSpan(initiallyAvailable, file.SamplePayloadStart - initiallyAvailable));
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeDeepCounts(0, 0, counts).Status);
            CollectionAssert.AreEqual(expectedCounts, counts);

            byte[] samples = Enumerable.Repeat((byte)0xa5, file.ExpectedChannels[0].Length).ToArray();
            V3.DeepChannelDestination[] destinations =
            {
                new V3.DeepChannelDestination("Z", samples),
            };
            Assert.AreEqual(
                V3.ExrResult.WouldBlock,
                reader.DecodeDeepSamples(0, 0, counts, destinations).Status);
            Assert.IsTrue(samples.All(static value => value == 0xa5));
            source.Supply(file.SamplePayloadStart, file.Bytes.AsSpan(file.SamplePayloadStart));
            source.Complete(file.Bytes.Length);
            Assert.AreEqual(
                V3.ExrResult.Success,
                reader.DecodeDeepSamples(0, 0, counts, destinations).Status);
            CollectionAssert.AreEqual(file.ExpectedChannels[0], samples);
        }

        using (FailOnceSource source = new FailOnceSource(file.Bytes))
        using (V3.ExrReader reader = V3.ExrReader.OpenSource(source))
        {
            Assert.AreEqual(V3.ExrResult.IO, reader.ParseHeader().Status);
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            int[] counts = Enumerable.Repeat(-2, expectedCounts.Length).ToArray();
            source.FailNextRead = true;
            Assert.AreEqual(V3.ExrResult.IO, reader.DecodeDeepCounts(0, 0, counts).Status);
            Assert.IsTrue(counts.All(static count => count == -2));
            Assert.AreEqual(V3.ExrResult.Success, reader.DecodeDeepCounts(0, 0, counts).Status);
            byte[] samples = Enumerable.Repeat((byte)0xb6, file.ExpectedChannels[0].Length).ToArray();
            source.FailNextRead = true;
            Assert.AreEqual(
                V3.ExrResult.IO,
                reader.DecodeDeepSamples(
                    0,
                    0,
                    counts,
                    new[] { new V3.DeepChannelDestination("Z", samples) }).Status);
            Assert.IsTrue(samples.All(static value => value == 0xb6));
            Assert.AreEqual(
                V3.ExrResult.Success,
                reader.DecodeDeepSamples(
                    0,
                    0,
                    counts,
                    new[] { new V3.DeepChannelDestination("Z", samples) }).Status);
            CollectionAssert.AreEqual(file.ExpectedChannels[0], samples);
        }

        using YieldingAsyncSource asyncSource = new YieldingAsyncSource(file.Bytes);
        await using V3.ExrReader asyncReader = V3.ExrReader.OpenAsyncSource(asyncSource);
        Assert.AreEqual(V3.ExrResult.Success, (await asyncReader.ParseHeaderAsync()).Status);
        int[] asyncCounts = Enumerable.Repeat(-3, expectedCounts.Length).ToArray();
        asyncSource.CancelNextRead = true;
        await AssertThrowsAsync<OperationCanceledException>(
            () => asyncReader.DecodeDeepCountsAsync(0, 0, asyncCounts).AsTask());
        Assert.IsTrue(asyncCounts.All(static count => count == -3));
        Assert.AreEqual(
            V3.ExrResult.Success,
            (await asyncReader.DecodeDeepCountsAsync(0, 0, asyncCounts)).Status);
        byte[] asyncSamples = Enumerable.Repeat((byte)0xc7, file.ExpectedChannels[0].Length).ToArray();
        V3.DeepChannelDestination[] asyncDestinations =
        {
            new V3.DeepChannelDestination("Z", asyncSamples),
        };
        asyncSource.CancelNextRead = true;
        await AssertThrowsAsync<OperationCanceledException>(() => asyncReader.DecodeDeepSamplesAsync(
            0,
            0,
            asyncCounts,
            asyncDestinations).AsTask());
        Assert.IsTrue(asyncSamples.All(static value => value == 0xc7));
        Assert.AreEqual(
            V3.ExrResult.Success,
            (await asyncReader.DecodeDeepSamplesAsync(
                0,
                0,
                asyncCounts,
                asyncDestinations)).Status);
        CollectionAssert.AreEqual(file.ExpectedChannels[0], asyncSamples);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep tiled decode reconstructs a zero chunk offset")]
    public void Case_V3Reader_DeepTiledReconstructsZeroOffset()
    {
        ChannelSpec[] channels =
        {
            new ChannelSpec("A", V3.PixelType.Half, 1, 1),
            new ChannelSpec("Z", V3.PixelType.Float, 1, 1),
        };
        int[] expectedCounts = { 1, 0, 2, 1 };
        DeepSyntheticFile file = BuildDeepBlockFile(
            V3.Compression.ZIP,
            new V3.Box2i(-2, -1, -1, 0),
            channels,
            expectedCounts,
            tiled: true,
            writeOffset: false);

        using V3.ExrReader reader = Parse(file.Bytes);
        Assert.IsTrue(reader.GetBlockInfo(0, 0).IsMissing);
        int[] counts = new int[expectedCounts.Length];
        Assert.AreEqual(V3.ExrResult.Success, reader.DecodeDeepCounts(0, 0, counts).Status);
        CollectionAssert.AreEqual(expectedCounts, counts);
        Assert.IsFalse(reader.GetBlockInfo(0, 0).IsMissing);

        byte[][] actual = file.ExpectedChannels.Select(static data => new byte[data.Length]).ToArray();
        Assert.AreEqual(
            V3.ExrResult.Success,
            reader.DecodeDeepSamples(
                0,
                0,
                counts,
                channels.Select((channel, index) =>
                    new V3.DeepChannelDestination(channel.Name, actual[index])).ToArray()).Status);
        for (int channelIndex = 0; channelIndex < actual.Length; channelIndex++)
        {
            CollectionAssert.AreEqual(file.ExpectedChannels[channelIndex], actual[channelIndex]);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep counts match v1 on the real deep scanline fixture")]
    public void Case_V3Reader_DeepCountsMatchV1RealFixture()
    {
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadDeepEXR(TestPaths.DeepScanline, out ExrHeader expectedHeader, out ExrDeepImage expectedImage));
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(File.ReadAllBytes(TestPaths.DeepScanline));
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        for (int blockIndex = 0; blockIndex < reader.GetNumBlocks(0); blockIndex++)
        {
            V3.BlockInfo block = reader.GetBlockInfo(0, blockIndex);
            int[] actualCounts = new int[checked((int)(block.Region.Width * block.Region.Height))];
            V3.ReaderResult result = reader.DecodeDeepCounts(0, blockIndex, actualCounts);
            Assert.AreEqual(V3.ExrResult.Success, result.Status, $"block {blockIndex}: {result.Error}");

            int[] expectedCounts = new int[actualCounts.Length];
            int target = 0;
            for (int y = block.Region.MinY; y <= block.Region.MaxY; y++)
            {
                int[] offsets = expectedImage.OffsetTable[y - expectedHeader.DataWindow.MinY];
                int previous = 0;
                for (int x = 0; x < offsets.Length; x++)
                {
                    expectedCounts[target++] = offsets[x] - previous;
                    previous = offsets[x];
                }
            }

            CollectionAssert.AreEqual(expectedCounts, actualCounts, $"block {blockIndex} counts");
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep materialization scatters complete parts ranges and tiles")]
    public void Case_V3Reader_DeepMaterializationReadsPartRangeAndTile()
    {
        ChannelSpec[] channels =
        {
            new ChannelSpec("A", V3.PixelType.Half, 1, 1),
            new ChannelSpec("Z", V3.PixelType.Float, 1, 1),
        };
        V3.Box2i region = new V3.Box2i(-1, 4, 2, 21);
        int[] counts = Enumerable.Range(0, checked((int)(region.Width * region.Height)))
            .Select(static index => index % 7 == 0 ? 0 : (index % 3) + 1)
            .ToArray();
        DeepMaterializedSyntheticFile file = BuildDeepScanlineFile(
            V3.Compression.ZIP,
            region,
            channels,
            counts);

        using (V3.ExrReader reader = Parse(file.Bytes))
        {
            V3.ReaderResult<V3.Part> partResult = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Success, partResult.Status, partResult.Error?.ToString());
            Assert.IsNotNull(partResult.Value);
            Assert.IsTrue(partResult.Value.IsComplete);
            V3.DeepLevel level = (V3.DeepLevel)partResult.Value.GetLevel(0, 0);
            AssertDeepLevel(level, region, counts, channels, file.ExpectedChannels);
            Assert.AreEqual(
                checked((long)counts.Length * sizeof(int)) +
                    file.ExpectedChannels.Sum(static data => (long)data.Length),
                partResult.BytesWritten);

            const int rangeMinimumY = 19;
            const int rangeLineCount = 2;
            V3.ReaderResult<V3.Part> rangeResult = reader.ReadScanlines(
                0,
                rangeMinimumY,
                rangeLineCount);
            Assert.AreEqual(V3.ExrResult.Success, rangeResult.Status, rangeResult.Error?.ToString());
            Assert.IsNotNull(rangeResult.Value);
            Assert.IsFalse(rangeResult.Value.IsComplete);
            V3.Box2i rangeRegion = new V3.Box2i(
                region.MinX,
                rangeMinimumY,
                region.MaxX,
                rangeMinimumY + rangeLineCount - 1);
            int firstPixel = checked((rangeMinimumY - region.MinY) * (int)region.Width);
            int pixelCount = checked(rangeLineCount * (int)region.Width);
            int[] rangeCounts = counts.AsSpan(firstPixel, pixelCount).ToArray();
            int firstSample = counts.AsSpan(0, firstPixel).ToArray().Sum();
            int rangeSamples = rangeCounts.Sum();
            byte[][] rangeChannels = channels
                .Select((channel, channelIndex) => file.ExpectedChannels[channelIndex]
                    .AsSpan(
                        checked(firstSample * PixelTypeByteCount(channel.PixelType)),
                        checked(rangeSamples * PixelTypeByteCount(channel.PixelType)))
                    .ToArray())
                .ToArray();
            AssertDeepLevel(
                (V3.DeepLevel)rangeResult.Value.GetLevel(0, 0),
                rangeRegion,
                rangeCounts,
                channels,
                rangeChannels);
        }

        int[] tileCounts = { 1, 0, 2, 1 };
        DeepSyntheticFile tile = BuildDeepBlockFile(
            V3.Compression.ZSTD,
            new V3.Box2i(-2, -1, -1, 0),
            channels,
            tileCounts,
            tiled: true,
            writeOffset: false);
        using (V3.ExrReader reader = Parse(tile.Bytes))
        {
            V3.ReaderResult<V3.Part> result = reader.ReadTile(0, 0, 0);
            Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
            Assert.IsNotNull(result.Value);
            Assert.IsTrue(result.Value.IsComplete);
            AssertDeepLevel(
                (V3.DeepLevel)result.Value.GetLevel(0, 0),
                new V3.Box2i(-2, -1, -1, 0),
                tileCounts,
                channels,
                tile.ExpectedChannels);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 async partial reads materialize flat ranges and deep tiles atomically")]
    public async Task Case_V3Reader_AsyncPartialReadsMaterializeFlatRangesAndDeepTiles()
    {
        ChannelSpec[] flatChannels =
        {
            new ChannelSpec("A", V3.PixelType.Half, 2, 2),
            new ChannelSpec("B", V3.PixelType.Float, 1, 1),
        };
        V3.Box2i flatWindow = new V3.Box2i(-2, -2, 3, 17);
        List<SyntheticAttribute> flatAttributes = StandardAttributes(
            V3.Compression.ZIP,
            Box(flatWindow.MinX, flatWindow.MinY, flatWindow.MaxX, flatWindow.MaxY));
        SetChannels(flatAttributes, flatChannels);
        V3.Box2i firstRegion = new V3.Box2i(-2, -2, 3, 13);
        V3.Box2i secondRegion = new V3.Box2i(-2, 14, 3, 17);
        SyntheticFile flatFile = BuildFlatScanlineBlocksFile(
            flatAttributes,
            new[]
            {
                CreateCanonicalBlock(firstRegion, flatChannels),
                CreateCanonicalBlock(secondRegion, flatChannels),
            },
            new[] { -2, 14 },
            writeOffsets: true);

        using (YieldingAsyncSource source = new YieldingAsyncSource(flatFile.Bytes))
        await using (V3.ExrReader reader = V3.ExrReader.OpenAsyncSource(source))
        {
            Assert.AreEqual(V3.ExrResult.Success, (await reader.ParseHeaderAsync()).Status);
            source.CancelNextRead = true;
            await AssertThrowsAsync<OperationCanceledException>(() =>
                reader.ReadScanlinesAsync(0, 12, 4).AsTask());

            V3.ReaderResult<V3.Part> result = await reader.ReadScanlinesAsync(0, 12, 4);
            Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
            Assert.IsNotNull(result.Value);
            Assert.IsFalse(result.Value.IsComplete);
            V3.FlatLevel level = (V3.FlatLevel)result.Value.GetLevel(0, 0);
            Assert.AreEqual(new V3.Box2i(-2, 12, 3, 15), level.Region);
            AssertFlatChannels(level, flatChannels);
        }

        ChannelSpec[] deepChannels =
        {
            new ChannelSpec("A", V3.PixelType.Half, 1, 1),
            new ChannelSpec("Z", V3.PixelType.Float, 1, 1),
        };
        int[] counts = { 1, 0, 2, 1 };
        DeepSyntheticFile deepFile = BuildDeepBlockFile(
            V3.Compression.ZSTD,
            new V3.Box2i(-2, -1, -1, 0),
            deepChannels,
            counts,
            tiled: true,
            writeOffset: false);
        using (YieldingAsyncSource source = new YieldingAsyncSource(deepFile.Bytes))
        await using (V3.ExrReader reader = V3.ExrReader.OpenAsyncSource(source))
        {
            Assert.AreEqual(V3.ExrResult.Success, (await reader.ParseHeaderAsync()).Status);
            V3.ReaderResult<V3.Part> result = await reader.ReadTileAsync(0, 0, 0);
            Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
            Assert.IsNotNull(result.Value);
            Assert.IsTrue(result.Value.IsComplete);
            AssertDeepLevel(
                (V3.DeepLevel)result.Value.GetLevel(0, 0),
                new V3.Box2i(-2, -1, -1, 0),
                counts,
                deepChannels,
                deepFile.ExpectedChannels);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep materialization preflights samples and resumes transient input")]
    public async Task Case_V3Reader_DeepMaterializationPreflightsAndResumes()
    {
        ChannelSpec[] channels = { new ChannelSpec("Z", V3.PixelType.Float, 1, 1) };
        int[] counts = { 1, 2, 0, 1 };
        DeepSyntheticFile file = BuildDeepBlockFile(
            V3.Compression.ZSTD,
            new V3.Box2i(0, 0, 3, 0),
            channels,
            counts);

        using (SpySource source = new SpySource(file.Bytes))
        using (V3.ExrReader reader = V3.ExrReader.OpenSource(
            source,
            new V3.ReaderOptions(new V3.ReaderLimits(
                maximumMaterializedByteCount: counts.Length * sizeof(int)))))
        {
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            V3.ReaderResult<V3.Part> result = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Unsupported, result.Status);
            Assert.IsTrue(result.Error is V3.ReaderLimitExceededException);
            Assert.IsNull(result.Value);
            Assert.IsTrue(source.MaximumRequestedEnd <= file.SamplePayloadStart);
        }

        using (V3IO.SuppliedDataSource source = V3IO.SuppliedDataSource.CreateUnknownLength())
        {
            source.Supply(0, file.Bytes.AsSpan(0, file.SamplePayloadStart));
            using V3.ExrReader reader = V3.ExrReader.OpenSource(source);
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            V3.ReaderResult<V3.Part> pending = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.WouldBlock, pending.Status);
            Assert.IsNull(pending.Value);

            source.Supply(file.SamplePayloadStart, file.Bytes.AsSpan(file.SamplePayloadStart));
            source.Complete(file.Bytes.Length);
            V3.ReaderResult<V3.Part> completed = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Success, completed.Status, completed.Error?.ToString());
            Assert.IsNotNull(completed.Value);
            AssertDeepLevel(
                (V3.DeepLevel)completed.Value.GetLevel(0, 0),
                new V3.Box2i(0, 0, 3, 0),
                counts,
                channels,
                file.ExpectedChannels);
        }

        using YieldingAsyncSource asyncSource = new YieldingAsyncSource(file.Bytes);
        await using V3.ExrReader asyncReader = V3.ExrReader.OpenAsyncSource(asyncSource);
        Assert.AreEqual(V3.ExrResult.Success, (await asyncReader.ParseHeaderAsync()).Status);
        asyncSource.CancelNextRead = true;
        await AssertThrowsAsync<OperationCanceledException>(() => asyncReader.ReadPartAsync(0).AsTask());
        V3.ReaderResult<V3.Part> asyncResult = await asyncReader.ReadPartAsync(0);
        Assert.AreEqual(V3.ExrResult.Success, asyncResult.Status, asyncResult.Error?.ToString());
        Assert.IsNotNull(asyncResult.Value);
        AssertDeepLevel(
            (V3.DeepLevel)asyncResult.Value.GetLevel(0, 0),
            new V3.Box2i(0, 0, 3, 0),
            counts,
            channels,
            file.ExpectedChannels);

        using (FailOnceSource source = new FailOnceSource(file.Bytes))
        using (V3.ExrReader retryReader = V3.ExrReader.OpenSource(source))
        {
            Assert.AreEqual(V3.ExrResult.IO, retryReader.ParseHeader().Status);
            Assert.AreEqual(V3.ExrResult.Success, retryReader.ParseHeader().Status);
            source.FailNextRead = true;
            V3.ReaderResult<V3.Part> failed = retryReader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.IO, failed.Status);
            Assert.IsNull(failed.Value);
            V3.ReaderResult<V3.Part> retried = retryReader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Success, retried.Status, retried.Error?.ToString());
            Assert.IsNotNull(retried.Value);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep ReadPart matches block API and v1 counts on the real fixture")]
    public void Case_V3Reader_DeepReadPartMatchesBlockApiAndV1Counts()
    {
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadDeepEXR(TestPaths.DeepScanline, out _, out ExrDeepImage expectedImage));
        byte[] encoded = File.ReadAllBytes(TestPaths.DeepScanline);
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.ReaderResult<V3.Part> result = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
        Assert.IsNotNull(result.Value);
        Assert.IsTrue(result.Value.IsComplete);
        V3.DeepLevel level = (V3.DeepLevel)result.Value.GetLevel(0, 0);

        int[] expectedCounts = expectedImage.OffsetTable
            .SelectMany(static offsets =>
            {
                int previous = 0;
                int[] counts = new int[offsets.Length];
                for (int index = 0; index < offsets.Length; index++)
                {
                    counts[index] = offsets[index] - previous;
                    previous = offsets[index];
                }

                return counts;
            })
            .ToArray();
        CollectionAssert.AreEqual(expectedCounts, level.SampleCounts.ToArray());

        V3.Header header = result.Value.Header;
        using V3.ExrReader blockReader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, blockReader.ParseHeader().Status);
        MemoryStream[] expectedChannelStreams = header.Channels
            .Select(static _ => new MemoryStream())
            .ToArray();
        for (int blockIndex = 0; blockIndex < blockReader.GetNumBlocks(0); blockIndex++)
        {
            V3.BlockInfo block = blockReader.GetBlockInfo(0, blockIndex);
            int[] blockCounts = new int[checked((int)(block.Region.Width * block.Region.Height))];
            Assert.AreEqual(
                V3.ExrResult.Success,
                blockReader.DecodeDeepCounts(0, blockIndex, blockCounts).Status);
            int blockSamples = blockCounts.Sum();
            byte[][] blockChannels = header.Channels
                .Select(channel => new byte[checked(
                    blockSamples * PixelTypeByteCount(channel.PixelType))])
                .ToArray();
            Assert.AreEqual(
                V3.ExrResult.Success,
                blockReader.DecodeDeepSamples(
                    0,
                    blockIndex,
                    blockCounts,
                    header.Channels.Select((channel, channelIndex) =>
                        new V3.DeepChannelDestination(channel.Name, blockChannels[channelIndex])).ToArray()).Status);
            for (int channelIndex = 0; channelIndex < blockChannels.Length; channelIndex++)
            {
                expectedChannelStreams[channelIndex].Write(blockChannels[channelIndex]);
            }
        }

        for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
        {
            V3.Channel channel = header.Channels[channelIndex];
            CollectionAssert.AreEqual(
                expectedChannelStreams[channelIndex].ToArray(),
                level.GetChannel(channel.Name).Data.ToArray(),
                channel.Name);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep tiled ReadPart matches every block sample")]
    public void Case_V3Reader_DeepTiledReadPartMatchesBlockApi()
    {
        string path = Path.Combine(TestPaths.NativeDataRoot, "deep_tiled_sample.exr");
        byte[] encoded = File.ReadAllBytes(path);
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.ReaderResult<V3.Part> result = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
        Assert.IsNotNull(result.Value);
        Assert.IsTrue(result.Value.IsComplete);

        V3.Header header = result.Value.Header;
        for (int blockIndex = 0; blockIndex < reader.GetNumBlocks(0); blockIndex++)
        {
            V3.BlockInfo block = reader.GetBlockInfo(0, blockIndex);
            int[] blockCounts = new int[checked((int)(block.Region.Width * block.Region.Height))];
            Assert.AreEqual(
                V3.ExrResult.Success,
                reader.DecodeDeepCounts(0, blockIndex, blockCounts).Status,
                $"block {blockIndex} counts");
            int totalSamples = blockCounts.Sum();
            byte[][] blockChannels = header.Channels
                .Select(channel => new byte[checked(
                    totalSamples * PixelTypeByteCount(channel.PixelType))])
                .ToArray();
            Assert.AreEqual(
                V3.ExrResult.Success,
                reader.DecodeDeepSamples(
                    0,
                    blockIndex,
                    blockCounts,
                    header.Channels.Select((channel, channelIndex) =>
                        new V3.DeepChannelDestination(channel.Name, blockChannels[channelIndex])).ToArray()).Status,
                $"block {blockIndex} samples");

            V3.DeepLevel level = (V3.DeepLevel)result.Value.GetLevel(block.LevelX, block.LevelY);
            int levelWidth = checked((int)level.Region.Width);
            int blockWidth = checked((int)block.Region.Width);
            int sourcePixel = 0;
            int sourceSample = 0;
            for (int y = block.Region.MinY; y <= block.Region.MaxY; y++)
            {
                for (int x = block.Region.MinX; x <= block.Region.MaxX; x++)
                {
                    int count = blockCounts[sourcePixel++];
                    int targetPixel = checked(
                        checked(y - level.Region.MinY) * levelWidth +
                        checked(x - level.Region.MinX));
                    Assert.AreEqual(count, level.SampleCounts[targetPixel], $"block {blockIndex} pixel {targetPixel}");
                    for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
                    {
                        V3.Channel channel = header.Channels[channelIndex];
                        int elementSize = PixelTypeByteCount(channel.PixelType);
                        CollectionAssert.AreEqual(
                            blockChannels[channelIndex]
                                .AsSpan(sourceSample * elementSize, count * elementSize)
                                .ToArray(),
                            level.GetSamples(channel.Name, targetPixel).ToArray(),
                            $"block {blockIndex} channel {channel.Name} pixel {targetPixel}");
                    }

                    sourceSample = checked(sourceSample + count);
                }
            }

            Assert.AreEqual(blockWidth * checked((int)block.Region.Height), sourcePixel);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader materializes sampled scanline ranges and complete parts")]
    public void Case_V3Reader_ReadScanlinesAndPartScatterCanonicalPlanes()
    {
        ChannelSpec[] channels =
        {
            new ChannelSpec("A", V3.PixelType.Half, 2, 2),
            new ChannelSpec("B", V3.PixelType.Float, 1, 1),
        };
        V3.Box2i dataWindow = new V3.Box2i(-2, -2, 3, 17);
        List<SyntheticAttribute> attributes = StandardAttributes(
            V3.Compression.ZIP,
            Box(dataWindow.MinX, dataWindow.MinY, dataWindow.MaxX, dataWindow.MaxY));
        SetChannels(attributes, channels);
        V3.Box2i firstRegion = new V3.Box2i(-2, -2, 3, 13);
        V3.Box2i secondRegion = new V3.Box2i(-2, 14, 3, 17);
        SyntheticFile file = BuildFlatScanlineBlocksFile(
            attributes,
            new[]
            {
                CreateCanonicalBlock(firstRegion, channels),
                CreateCanonicalBlock(secondRegion, channels),
            },
            new[] { -2, 14 },
            writeOffsets: true);

        using V3.ExrReader reader = Parse(file.Bytes);
        V3.ReaderResult<V3.Part> rangeResult = reader.ReadScanlines(0, 12, 4);
        Assert.AreEqual(V3.ExrResult.Success, rangeResult.Status);
        Assert.IsNotNull(rangeResult.Value);
        Assert.IsFalse(rangeResult.Value.IsComplete);
        V3.FlatLevel range = (V3.FlatLevel)rangeResult.Value.GetLevel(0, 0);
        Assert.AreEqual(new V3.Box2i(-2, 12, 3, 15), range.Region);
        AssertFlatChannels(range, channels);

        V3.ReaderResult<V3.Part> partResult = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, partResult.Status);
        Assert.IsNotNull(partResult.Value);
        Assert.IsTrue(partResult.Value.IsComplete);
        V3.FlatLevel full = (V3.FlatLevel)partResult.Value.GetLevel(0, 0);
        Assert.AreEqual(dataWindow, full.Region);
        AssertFlatChannels(full, channels);
        Assert.AreEqual(
            full.Channels.Sum(static channel => (long)channel.ByteLength),
            partResult.BytesWritten);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 reader materializes edge tiles and every mipmap level")]
    public void Case_V3Reader_ReadTileAndTiledPartMaterializeAllLevels()
    {
        ChannelSpec[] channels =
        {
            new ChannelSpec("A", V3.PixelType.Half, 1, 1),
            new ChannelSpec("B", V3.PixelType.UInt, 1, 1),
        };
        V3.Box2i dataWindow = new V3.Box2i(-2, -1, 2, 2);
        List<SyntheticAttribute> tiledAttributes = StandardAttributes(
            V3.Compression.None,
            Box(dataWindow.MinX, dataWindow.MinY, dataWindow.MaxX, dataWindow.MaxY));
        SetChannels(tiledAttributes, channels);
        tiledAttributes.Add(new SyntheticAttribute(
            "tiles",
            "tiledesc",
            TileDescription(3, 3, V3.TileLevelMode.OneLevel, V3.TileRoundingMode.RoundDown)));
        SyntheticTileBlock[] tiles =
        {
            Tile(0, 0, 0, 0, new V3.Box2i(-2, -1, 0, 1), channels),
            Tile(1, 0, 0, 0, new V3.Box2i(1, -1, 2, 1), channels),
            Tile(0, 1, 0, 0, new V3.Box2i(-2, 2, 0, 2), channels),
            Tile(1, 1, 0, 0, new V3.Box2i(1, 2, 2, 2), channels),
        };
        SyntheticFile tiled = BuildFlatTiledBlocksFile(tiledAttributes, tiles);
        using (V3.ExrReader reader = Parse(tiled.Bytes))
        {
            V3.ReaderResult<V3.Part> tileResult = reader.ReadTile(0, 1, 1);
            Assert.AreEqual(V3.ExrResult.Success, tileResult.Status);
            Assert.IsNotNull(tileResult.Value);
            Assert.IsFalse(tileResult.Value.IsComplete);
            V3.FlatLevel edge = (V3.FlatLevel)tileResult.Value.GetLevel(0, 0);
            Assert.AreEqual(new V3.Box2i(1, 2, 2, 2), edge.Region);
            AssertFlatChannels(edge, channels);

            V3.ReaderResult<V3.Part> partResult = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Success, partResult.Status);
            Assert.IsNotNull(partResult.Value);
            Assert.IsTrue(partResult.Value.IsComplete);
            V3.FlatLevel full = (V3.FlatLevel)partResult.Value.GetLevel(0, 0);
            Assert.AreEqual(dataWindow, full.Region);
            AssertFlatChannels(full, channels);
        }

        ChannelSpec[] mipChannels = { new ChannelSpec("Y", V3.PixelType.Half, 1, 1) };
        List<SyntheticAttribute> mipAttributes = StandardAttributes(
            V3.Compression.None,
            Box(0, 0, 3, 3));
        SetChannels(mipAttributes, mipChannels);
        mipAttributes.Add(new SyntheticAttribute(
            "tiles",
            "tiledesc",
            TileDescription(2, 2, V3.TileLevelMode.MipmapLevels, V3.TileRoundingMode.RoundDown)));
        SyntheticTileBlock[] mipTiles =
        {
            Tile(0, 0, 0, 0, new V3.Box2i(0, 0, 1, 1), mipChannels),
            Tile(1, 0, 0, 0, new V3.Box2i(2, 0, 3, 1), mipChannels),
            Tile(0, 1, 0, 0, new V3.Box2i(0, 2, 1, 3), mipChannels),
            Tile(1, 1, 0, 0, new V3.Box2i(2, 2, 3, 3), mipChannels),
            Tile(0, 0, 1, 1, new V3.Box2i(0, 0, 1, 1), mipChannels),
            Tile(0, 0, 2, 2, new V3.Box2i(0, 0, 0, 0), mipChannels),
        };
        SyntheticFile mip = BuildFlatTiledBlocksFile(mipAttributes, mipTiles);
        using (V3.ExrReader reader = Parse(mip.Bytes))
        {
            V3.ReaderResult<V3.Part> result = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Success, result.Status);
            Assert.IsNotNull(result.Value);
            Assert.IsTrue(result.Value.IsComplete);
            Assert.AreEqual(3, result.Value.Levels.Count);
            AssertFlatChannels((V3.FlatLevel)result.Value.GetLevel(0, 0), mipChannels);
            AssertFlatChannels((V3.FlatLevel)result.Value.GetLevel(1, 1), mipChannels);
            AssertFlatChannels((V3.FlatLevel)result.Value.GetLevel(2, 2), mipChannels);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 flat materialization resumes atomically and enforces its budget before pixel I/O")]
    public void Case_V3Reader_ReadPartResumesWouldBlockAndPreflightsBudget()
    {
        ChannelSpec[] channels = { new ChannelSpec("Y", V3.PixelType.Half, 1, 1) };
        List<SyntheticAttribute> attributes = StandardAttributes(
            V3.Compression.None,
            Box(0, 0, 0, 1));
        SetChannels(attributes, channels);
        byte[] first = CreateCanonicalBlock(new V3.Box2i(0, 0, 0, 0), channels);
        byte[] second = CreateCanonicalBlock(new V3.Box2i(0, 1, 0, 1), channels);
        SyntheticFile file = BuildFlatScanlineBlocksFile(
            attributes,
            new[] { first, second },
            new[] { 0, 1 },
            writeOffsets: true);
        int supplied = file.PayloadStart + 8 + first.Length;
        using (V3IO.SuppliedDataSource source = V3IO.SuppliedDataSource.CreateUnknownLength())
        {
            source.Supply(0, file.Bytes.AsSpan(0, supplied));
            using V3.ExrReader reader = V3.ExrReader.OpenSource(source);
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            V3.ReaderResult<V3.Part> pending = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.WouldBlock, pending.Status);
            Assert.IsNull(pending.Value);

            source.Supply(supplied, file.Bytes.AsSpan(supplied));
            source.Complete(file.Bytes.Length);
            V3.ReaderResult<V3.Part> completed = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Success, completed.Status);
            Assert.IsNotNull(completed.Value);
            AssertFlatChannels((V3.FlatLevel)completed.Value.GetLevel(0, 0), channels);
        }

        using SpySource spy = new SpySource(file.Bytes);
        using V3.ExrReader limited = V3.ExrReader.OpenSource(
            spy,
            new V3.ReaderOptions(new V3.ReaderLimits(maximumMaterializedByteCount: 3)));
        Assert.AreEqual(V3.ExrResult.Success, limited.ParseHeader().Status);
        int callsAfterHeader = spy.ReadCallCount;
        V3.ReaderResult<V3.Part> limitedResult = limited.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Unsupported, limitedResult.Status);
        Assert.IsTrue(limitedResult.Error is V3.ReaderLimitExceededException);
        Assert.IsNull(limitedResult.Value);
        Assert.AreEqual(callsAfterHeader, spy.ReadCallCount);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 async flat materialization resumes cancellation and publishes only success")]
    public async Task Case_V3Reader_ReadPartAsyncResumesCancellation()
    {
        ChannelSpec[] channels = { new ChannelSpec("Y", V3.PixelType.Half, 1, 1) };
        List<SyntheticAttribute> attributes = StandardAttributes(
            V3.Compression.None,
            Box(0, 0, 0, 1));
        SetChannels(attributes, channels);
        SyntheticFile file = BuildFlatScanlineBlocksFile(
            attributes,
            new[]
            {
                CreateCanonicalBlock(new V3.Box2i(0, 0, 0, 0), channels),
                CreateCanonicalBlock(new V3.Box2i(0, 1, 0, 1), channels),
            },
            new[] { 0, 1 },
            writeOffsets: true);
        using YieldingAsyncSource source = new YieldingAsyncSource(file.Bytes);
        await using V3.ExrReader reader = V3.ExrReader.OpenAsyncSource(source);
        Assert.AreEqual(V3.ExrResult.Success, (await reader.ParseHeaderAsync()).Status);
        source.CancelNextRead = true;
        await AssertThrowsAsync<OperationCanceledException>(() => reader.ReadPartAsync(0).AsTask());

        ValueTask<V3.ReaderResult<V3.Part>> operation = reader.ReadPartAsync(0);
        Assert.IsFalse(operation.IsCompleted);
        V3.ReaderResult<V3.Part> result = await operation;
        Assert.AreEqual(V3.ExrResult.Success, result.Status);
        Assert.IsNotNull(result.Value);
        AssertFlatChannels((V3.FlatLevel)result.Value.GetLevel(0, 0), channels);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 flat materialization matches v1 planar output on real files")]
    public void Case_V3Reader_ReadPartMatchesV1RealFixtures()
    {
        string[] paths =
        {
            TestPaths.Regression("2by2.exr"),
            TestPaths.Regression("issue-160-piz-decode.exr"),
            TestPaths.OpenExr("MultiResolution/Bonita.exr"),
        };

        foreach (string path in paths)
        {
            (_, _, ExrImage expected) = ExrTestHelper.LoadSinglePart(path);
            using V3.ExrReader reader = V3.ExrReader.OpenMemory(File.ReadAllBytes(path));
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status, path);
            V3.ReaderResult<V3.Part> result = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Success, result.Status, $"{path}: {result.Error}");
            Assert.IsNotNull(result.Value, path);
            Assert.IsTrue(result.Value.IsComplete, path);
            Assert.AreEqual(expected.Levels.Count, result.Value.Levels.Count, path);

            foreach (ExrImageLevel expectedLevel in expected.Levels)
            {
                V3.FlatLevel actualLevel = (V3.FlatLevel)result.Value.GetLevel(
                    expectedLevel.LevelX,
                    expectedLevel.LevelY);
                Assert.AreEqual(expectedLevel.Width, actualLevel.Width, path);
                Assert.AreEqual(expectedLevel.Height, actualLevel.Height, path);
                foreach (ExrImageChannel expectedChannel in expectedLevel.Channels)
                {
                    V3.ChannelBuffer actualChannel = actualLevel.GetChannel(expectedChannel.Channel.Name);
                    Assert.AreEqual(
                        (V3.PixelType)(int)expectedChannel.DataType,
                        actualChannel.PixelType,
                        $"{path}: {expectedChannel.Channel.Name}");
                    CollectionAssert.AreEqual(
                        expectedChannel.Data,
                        actualChannel.Data.ToArray(),
                        $"{path}: level ({expectedLevel.LevelX}, {expectedLevel.LevelY}) channel {expectedChannel.Channel.Name}");
                }
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 flat materialization separates corrupt unsupported and argument failures")]
    public void Case_V3Reader_FlatMaterializationFailureKindsAreDeterministic()
    {
        byte[] payload = { 0x00, 0x3c };
        SyntheticFile corrupt = BuildFlatScanlineBlocksFile(
            StandardAttributes(V3.Compression.None, Box(0, 0, 0, 1)),
            new[] { payload, payload },
            new[] { 0, 0 },
            writeOffsets: true);
        using (V3.ExrReader reader = Parse(corrupt.Bytes))
        {
            V3.ReaderResult<V3.Part> result = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Corrupt, result.Status);
            Assert.IsNull(result.Value);
            AssertThrows<ArgumentException>(() => reader.ReadTile(0, 0, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => reader.ReadScanlines(0, 2, 1));
        }

    }

    private static void AssertFixture(
        string path,
        int minimumParts,
        V3.PartType? expectedPartType,
        V3.TileLevelMode? expectedLevelMode = null)
    {
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(File.ReadAllBytes(path));
        V3.ReaderResult result = reader.ParseHeader();
        Assert.AreEqual(V3.ExrResult.Success, result.Status, path);
        Assert.IsTrue(reader.NumParts >= minimumParts, path);
        Assert.IsTrue(reader.GetNumBlocks(0) > 0, path);
        V3.Header header = reader.GetHeader(0);
        if (expectedPartType.HasValue)
        {
            Assert.AreEqual(expectedPartType.Value, header.PartType, path);
        }

        if (expectedLevelMode.HasValue)
        {
            Assert.IsNotNull(header.Tiles, path);
            Assert.AreEqual(expectedLevelMode.Value, header.Tiles.LevelMode, path);
        }

        V3.BlockInfo first = reader.GetBlockInfo(0, 0);
        Assert.AreEqual(0, first.BlockIndex, path);
        Assert.AreEqual(0, first.PartIndex, path);
    }

    private static void AssertCompletedTruncation(
        byte[] complete,
        int length,
        V3.ExrResult expected,
        V3.ReaderState expectedState)
    {
        using V3IO.SuppliedDataSource source = V3IO.SuppliedDataSource.CreateUnknownLength();
        source.Supply(0, complete.AsSpan(0, length));
        source.Complete(length);
        using V3.ExrReader reader = V3.ExrReader.OpenSource(source);
        V3.ReaderResult result = reader.ParseHeader();
        Assert.AreEqual(expected, result.Status);
        Assert.AreEqual(expectedState, reader.State);
    }

    private static void AssertLimit(byte[] bytes, V3.ReaderLimits limits)
    {
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(
            bytes,
            new V3.ReaderOptions(limits));
        V3.ReaderResult result = reader.ParseHeader();
        Assert.AreEqual(V3.ExrResult.Unsupported, result.Status);
        Assert.IsTrue(result.Error is V3.ReaderLimitExceededException);
        Assert.AreNotEqual(V3.ReaderState.Faulted, reader.State);
    }

    private static V3.ExrReader Parse(byte[] bytes)
    {
        V3.ExrReader reader = V3.ExrReader.OpenMemory(bytes);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        return reader;
    }

    private static void AssertTiledGeometry(
        V3.TileLevelMode mode,
        int expectedBlocks,
        int expectedLastLevel)
    {
        List<SyntheticAttribute> attributes = StandardAttributes(dataWindow: Box(-2, -3, 1, 0));
        attributes.Add(new SyntheticAttribute(
            "tiles",
            "tiledesc",
            TileDescription(2, 2, mode, V3.TileRoundingMode.RoundDown)));
        SyntheticFile file = BuildSingle(attributes, expectedBlocks, flags: TiledFlag);
        using V3.ExrReader reader = Parse(file.Bytes);
        Assert.AreEqual(expectedBlocks, reader.GetNumBlocks(0));
        V3.BlockInfo first = reader.GetBlockInfo(0, 0);
        V3.BlockInfo last = reader.GetBlockInfo(0, expectedBlocks - 1);
        Assert.IsTrue(first.IsTiled);
        Assert.AreEqual(-2, first.Region.MinX);
        Assert.AreEqual(-3, first.Region.MinY);
        Assert.AreEqual(expectedLastLevel, last.LevelY);
        if (mode != V3.TileLevelMode.RipmapLevels)
        {
            Assert.AreEqual(expectedLastLevel, last.LevelX);
        }
    }

    private static void AssertFlatChannels(V3.FlatLevel level, IReadOnlyList<ChannelSpec> channels)
    {
        foreach (ChannelSpec expected in channels)
        {
            V3.ChannelBuffer actual = level.GetChannel(expected.Name);
            Assert.AreEqual(expected.PixelType, actual.PixelType, expected.Name);
            CollectionAssert.AreEqual(
                CreatePlanarChannel(level.Region, expected),
                actual.Data.ToArray(),
                expected.Name);
        }
    }

    private static void AssertDeepLevel(
        V3.DeepLevel level,
        V3.Box2i region,
        IReadOnlyList<int> counts,
        IReadOnlyList<ChannelSpec> channels,
        IReadOnlyList<byte[]> expectedChannels)
    {
        Assert.AreEqual(region.MinX, level.Region.MinX);
        Assert.AreEqual(region.MinY, level.Region.MinY);
        Assert.AreEqual(region.MaxX, level.Region.MaxX);
        Assert.AreEqual(region.MaxY, level.Region.MaxY);
        CollectionAssert.AreEqual(counts.ToArray(), level.SampleCounts.ToArray());
        Assert.AreEqual((ulong)counts.Sum(), level.TotalSamples);
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            ChannelSpec channel = channels[channelIndex];
            V3.ChannelBuffer actual = level.GetChannel(channel.Name);
            Assert.AreEqual(channel.PixelType, actual.PixelType, channel.Name);
            CollectionAssert.AreEqual(
                expectedChannels[channelIndex],
                actual.Data.ToArray(),
                channel.Name);
        }
    }

    private static SyntheticTileBlock Tile(
        int tileX,
        int tileY,
        int levelX,
        int levelY,
        V3.Box2i region,
        IReadOnlyList<ChannelSpec> channels)
    {
        return new SyntheticTileBlock(
            tileX,
            tileY,
            levelX,
            levelY,
            CreateCanonicalBlock(region, channels));
    }

    private static DeepSyntheticFile BuildDeepBlockFile(
        V3.Compression compression,
        V3.Box2i region,
        IReadOnlyList<ChannelSpec> channels,
        IReadOnlyList<int> counts,
        bool tiled = false,
        bool writeOffset = true)
    {
        int width = checked((int)region.Width);
        int height = checked((int)region.Height);
        if (counts.Count != checked(width * height))
        {
            throw new ArgumentException("A deep fixture requires one count per pixel.", nameof(counts));
        }

        long totalSamplesLong = 0;
        for (int index = 0; index < counts.Count; index++)
        {
            if (counts[index] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(counts));
            }

            totalSamplesLong = checked(totalSamplesLong + counts[index]);
        }

        int totalSamples = checked((int)totalSamplesLong);
        byte[][] expectedChannels = new byte[channels.Count][];
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            int byteCount = checked(totalSamples * PixelTypeByteCount(channels[channelIndex].PixelType));
            byte[] data = new byte[byteCount];
            for (int byteIndex = 0; byteIndex < data.Length; byteIndex++)
            {
                data[byteIndex] = unchecked((byte)(17 + (channelIndex * 67) + (byteIndex * 29)));
            }

            expectedChannels[channelIndex] = data;
        }

        byte[] countBytes = new byte[checked(counts.Count * sizeof(int))];
        int countIndex = 0;
        for (int row = 0; row < height; row++)
        {
            int cumulative = 0;
            for (int x = 0; x < width; x++)
            {
                cumulative = checked(cumulative + counts[countIndex]);
                BinaryPrimitives.WriteInt32LittleEndian(
                    countBytes.AsSpan(countIndex * sizeof(int), sizeof(int)),
                    cumulative);
                countIndex++;
            }
        }

        using MemoryStream sampleStream = new MemoryStream();
        countIndex = 0;
        int sourceSampleOffset = 0;
        for (int row = 0; row < height; row++)
        {
            int rowSamples = 0;
            for (int x = 0; x < width; x++)
            {
                rowSamples = checked(rowSamples + counts[countIndex++]);
            }

            for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
            {
                int elementSize = PixelTypeByteCount(channels[channelIndex].PixelType);
                sampleStream.Write(
                    expectedChannels[channelIndex],
                    checked(sourceSampleOffset * elementSize),
                    checked(rowSamples * elementSize));
            }

            sourceSampleOffset = checked(sourceSampleOffset + rowSamples);
        }

        byte[] sampleBytes = sampleStream.ToArray();
        byte[] packedCounts = EncodeDeepPayload(compression, countBytes);
        byte[] packedSamples = EncodeDeepPayload(compression, sampleBytes);

        List<SyntheticAttribute> attributes = StandardAttributes(
            compression,
            Box(region.MinX, region.MinY, region.MaxX, region.MaxY));
        SetChannels(attributes, channels);
        attributes.Add(new SyntheticAttribute(
            "name",
            "string",
            System.Text.Encoding.ASCII.GetBytes("deep")));
        attributes.Add(new SyntheticAttribute(
            "type",
            "string",
            System.Text.Encoding.ASCII.GetBytes(tiled ? "deeptile" : "deepscanline")));
        attributes.Add(new SyntheticAttribute("version", "int", Int32(1)));
        attributes.Add(new SyntheticAttribute("chunkCount", "int", Int32(1)));
        attributes.Add(new SyntheticAttribute("maxSamplesPerPixel", "int", Int32(counts.Max())));
        if (tiled)
        {
            attributes.Add(new SyntheticAttribute(
                "tiles",
                "tiledesc",
                TileDescription(
                    checked((uint)width),
                    checked((uint)height),
                    V3.TileLevelMode.OneLevel,
                    V3.TileRoundingMode.RoundDown)));
        }

        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(
            stream,
            System.Text.Encoding.UTF8,
            leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U | NonImageFlag);
        WriteHeader(writer, attributes);
        int offsetTableStart = checked((int)stream.Position);
        writer.Write(0UL);
        int chunkStart = checked((int)stream.Position);
        if (tiled)
        {
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        }
        else
        {
            writer.Write(region.MinY);
        }

        writer.Write((long)packedCounts.Length);
        writer.Write((long)packedSamples.Length);
        int unpackedSizeOffset = checked((int)stream.Position);
        writer.Write((long)sampleBytes.Length);
        int countPayloadStart = checked((int)stream.Position);
        writer.Write(packedCounts);
        int samplePayloadStart = checked((int)stream.Position);
        writer.Write(packedSamples);

        byte[] bytes = stream.ToArray();
        if (writeOffset)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(offsetTableStart, sizeof(ulong)),
                (ulong)chunkStart);
        }

        return new DeepSyntheticFile(
            bytes,
            offsetTableStart,
            chunkStart,
            countPayloadStart,
            samplePayloadStart,
            unpackedSizeOffset,
            expectedChannels);
    }

    private static DeepMaterializedSyntheticFile BuildDeepScanlineFile(
        V3.Compression compression,
        V3.Box2i region,
        IReadOnlyList<ChannelSpec> channels,
        IReadOnlyList<int> counts,
        bool writeOffsets = true)
    {
        int width = checked((int)region.Width);
        int height = checked((int)region.Height);
        if (counts.Count != checked(width * height))
        {
            throw new ArgumentException("A deep fixture requires one count per pixel.", nameof(counts));
        }

        int[] sampleOffsets = new int[counts.Count];
        int totalSamples = 0;
        for (int pixelIndex = 0; pixelIndex < counts.Count; pixelIndex++)
        {
            if (counts[pixelIndex] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(counts));
            }

            sampleOffsets[pixelIndex] = totalSamples;
            totalSamples = checked(totalSamples + counts[pixelIndex]);
        }

        byte[][] expectedChannels = new byte[channels.Count][];
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            byte[] data = new byte[checked(
                totalSamples * PixelTypeByteCount(channels[channelIndex].PixelType))];
            for (int byteIndex = 0; byteIndex < data.Length; byteIndex++)
            {
                data[byteIndex] = unchecked((byte)(17 + (channelIndex * 67) + (byteIndex * 29)));
            }

            expectedChannels[channelIndex] = data;
        }

        int linesPerBlock = DeepLinesPerBlock(compression);
        int blockCount = checked((height + linesPerBlock - 1) / linesPerBlock);
        DeepChunkPayload[] chunks = new DeepChunkPayload[blockCount];
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            int firstRow = checked(blockIndex * linesPerBlock);
            int blockHeight = Math.Min(linesPerBlock, height - firstRow);
            byte[] countBytes = new byte[checked(width * blockHeight * sizeof(int))];
            int localCountIndex = 0;
            for (int rowOffset = 0; rowOffset < blockHeight; rowOffset++)
            {
                int cumulative = 0;
                int globalPixel = checked((firstRow + rowOffset) * width);
                for (int x = 0; x < width; x++)
                {
                    cumulative = checked(cumulative + counts[globalPixel + x]);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        countBytes.AsSpan(localCountIndex * sizeof(int), sizeof(int)),
                        cumulative);
                    localCountIndex++;
                }
            }

            using MemoryStream sampleStream = new MemoryStream();
            for (int rowOffset = 0; rowOffset < blockHeight; rowOffset++)
            {
                int globalPixel = checked((firstRow + rowOffset) * width);
                int rowSamples = 0;
                for (int x = 0; x < width; x++)
                {
                    rowSamples = checked(rowSamples + counts[globalPixel + x]);
                }

                int globalSampleOffset = sampleOffsets[globalPixel];
                for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
                {
                    int elementSize = PixelTypeByteCount(channels[channelIndex].PixelType);
                    sampleStream.Write(
                        expectedChannels[channelIndex],
                        checked(globalSampleOffset * elementSize),
                        checked(rowSamples * elementSize));
                }
            }

            byte[] rawSamples = sampleStream.ToArray();
            chunks[blockIndex] = new DeepChunkPayload(
                checked(region.MinY + firstRow),
                EncodeDeepPayload(compression, countBytes),
                EncodeDeepPayload(compression, rawSamples),
                rawSamples.Length);
        }

        List<SyntheticAttribute> attributes = StandardAttributes(
            compression,
            Box(region.MinX, region.MinY, region.MaxX, region.MaxY));
        SetChannels(attributes, channels);
        attributes.Add(new SyntheticAttribute(
            "name",
            "string",
            System.Text.Encoding.ASCII.GetBytes("deep")));
        attributes.Add(new SyntheticAttribute(
            "type",
            "string",
            System.Text.Encoding.ASCII.GetBytes("deepscanline")));
        attributes.Add(new SyntheticAttribute("version", "int", Int32(1)));
        attributes.Add(new SyntheticAttribute("chunkCount", "int", Int32(blockCount)));
        attributes.Add(new SyntheticAttribute("maxSamplesPerPixel", "int", Int32(counts.Max())));

        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(
            stream,
            System.Text.Encoding.UTF8,
            leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U | NonImageFlag);
        WriteHeader(writer, attributes);
        int offsetTableStart = checked((int)stream.Position);
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            writer.Write(0UL);
        }

        long[] chunkOffsets = new long[blockCount];
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            DeepChunkPayload chunk = chunks[blockIndex];
            chunkOffsets[blockIndex] = stream.Position;
            writer.Write(chunk.MinimumY);
            writer.Write((long)chunk.PackedCounts.Length);
            writer.Write((long)chunk.PackedSamples.Length);
            writer.Write((long)chunk.UnpackedSampleByteCount);
            writer.Write(chunk.PackedCounts);
            writer.Write(chunk.PackedSamples);
        }

        byte[] bytes = stream.ToArray();
        if (writeOffsets)
        {
            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(
                    bytes.AsSpan(offsetTableStart + (blockIndex * sizeof(ulong)), sizeof(ulong)),
                    checked((ulong)chunkOffsets[blockIndex]));
            }
        }

        return new DeepMaterializedSyntheticFile(bytes, expectedChannels);
    }

    private static int DeepLinesPerBlock(V3.Compression compression)
    {
        switch (compression)
        {
            case V3.Compression.None:
            case V3.Compression.RLE:
            case V3.Compression.ZIPS:
                return 1;
            case V3.Compression.ZIP:
            case V3.Compression.PXR24:
                return 16;
            case V3.Compression.PIZ:
            case V3.Compression.B44:
            case V3.Compression.B44A:
            case V3.Compression.DWAA:
            case V3.Compression.HTJ2K32:
            case V3.Compression.ZSTD:
                return 32;
            case V3.Compression.DWAB:
            case V3.Compression.HTJ2K256:
                return 256;
            default:
                throw new ArgumentOutOfRangeException(nameof(compression));
        }
    }

    private static byte[] EncodeDeepPayload(V3.Compression compression, ReadOnlySpan<byte> raw)
    {
        switch (compression)
        {
            case V3.Compression.None:
                return raw.ToArray();
            case V3.Compression.RLE:
                return EncodeDeepRlePayload(raw);
            case V3.Compression.ZIPS:
            case V3.Compression.ZIP:
                return EncodeDeepZipPayload(raw);
            case V3.Compression.ZSTD:
                V3Codecs.ZstdFrameStatus sizeStatus = V3Codecs.ZstdRawRleEncoder.GetEncodedSize(
                    raw,
                    includeChecksum: true,
                    out int encodedSize);
                if (sizeStatus != V3Codecs.ZstdFrameStatus.Success)
                {
                    throw new InvalidOperationException($"Could not size a ZSTD fixture ({sizeStatus}).");
                }

                byte[] encoded = new byte[encodedSize];
                V3Codecs.ZstdFrameStatus encodeStatus = V3Codecs.ZstdRawRleEncoder.Encode(
                    raw,
                    encoded,
                    includeChecksum: true,
                    out int written);
                if (encodeStatus != V3Codecs.ZstdFrameStatus.Success || written != encoded.Length)
                {
                    throw new InvalidOperationException($"Could not encode a ZSTD fixture ({encodeStatus}).");
                }

                return encoded;
            default:
                throw new InvalidOperationException(
                    $"Compression '{compression}' is not supported by the deep fixture builder.");
        }
    }

    private static byte[] EncodeDeepZipPayload(ReadOnlySpan<byte> raw)
    {
        byte[] predicted = ApplyDeepPredictorAndReorder(raw);
        using MemoryStream output = new MemoryStream();
        using (System.IO.Compression.ZLibStream zlib = new System.IO.Compression.ZLibStream(
            output,
            System.IO.Compression.CompressionLevel.Optimal,
            leaveOpen: true))
        {
            zlib.Write(predicted);
        }

        byte[] payload = output.ToArray();
        return payload.Length >= raw.Length ? raw.ToArray() : payload;
    }

    private static byte[] EncodeDeepRlePayload(ReadOnlySpan<byte> raw)
    {
        byte[] predicted = ApplyDeepPredictorAndReorder(raw);
        List<byte> encoded = new List<byte>(predicted.Length + 8);
        int index = 0;
        while (index < predicted.Length)
        {
            int runLength = 1;
            while (index + runLength < predicted.Length &&
                predicted[index + runLength] == predicted[index] &&
                runLength < 128)
            {
                runLength++;
            }

            if (runLength >= 3)
            {
                encoded.Add((byte)(runLength - 1));
                encoded.Add(predicted[index]);
                index += runLength;
                continue;
            }

            int literalStart = index;
            index += runLength;
            while (index < predicted.Length)
            {
                runLength = 1;
                while (index + runLength < predicted.Length &&
                    predicted[index + runLength] == predicted[index] &&
                    runLength < 128)
                {
                    runLength++;
                }

                if (runLength >= 3 || index - literalStart >= 127)
                {
                    break;
                }

                index += runLength;
            }

            int literalCount = index - literalStart;
            encoded.Add(unchecked((byte)(-literalCount)));
            for (int literalIndex = literalStart; literalIndex < index; literalIndex++)
            {
                encoded.Add(predicted[literalIndex]);
            }
        }

        return encoded.ToArray();
    }

    private static byte[] ApplyDeepPredictorAndReorder(ReadOnlySpan<byte> raw)
    {
        byte[] reordered = new byte[raw.Length];
        int half = (raw.Length + 1) / 2;
        int first = 0;
        int second = half;
        for (int index = 0; index < raw.Length; index += 2)
        {
            reordered[first++] = raw[index];
            if (index + 1 < raw.Length)
            {
                reordered[second++] = raw[index + 1];
            }
        }

        int previous = reordered.Length == 0 ? 0 : reordered[0];
        for (int index = 1; index < reordered.Length; index++)
        {
            int current = reordered[index];
            reordered[index] = unchecked((byte)(current - previous + 384));
            previous = current;
        }

        return reordered;
    }

    private static int PixelTypeByteCount(V3.PixelType pixelType)
    {
        return pixelType == V3.PixelType.Half ? sizeof(ushort) : sizeof(uint);
    }

    private static float[] DecodeDeepSamplesAsFloat(byte[] raw, V3.PixelType pixelType)
    {
        int elementSize = PixelTypeByteCount(pixelType);
        float[] samples = new float[raw.Length / elementSize];
        if (pixelType == V3.PixelType.Half)
        {
            ushort[] bits = new ushort[samples.Length];
            for (int index = 0; index < bits.Length; index++)
            {
                bits[index] = BinaryPrimitives.ReadUInt16LittleEndian(
                    raw.AsSpan(index * sizeof(ushort), sizeof(ushort)));
            }

            V3.PixelConversion.HalfToFloat(bits, samples);
            return samples;
        }

        for (int index = 0; index < samples.Length; index++)
        {
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(index * sizeof(uint), sizeof(uint)));
            samples[index] = pixelType == V3.PixelType.Float
                ? BitConverter.Int32BitsToSingle(unchecked((int)bits))
                : bits;
        }

        return samples;
    }

    private static List<SyntheticAttribute> StandardAttributes(
        V3.Compression compression = V3.Compression.None,
        byte[]? dataWindow = null)
    {
        byte[] window = dataWindow ?? Box(0, 0, 0, 0);
        return new List<SyntheticAttribute>
        {
            new SyntheticAttribute("channels", "chlist", Channels()),
            new SyntheticAttribute("compression", "compression", new[] { (byte)compression }),
            new SyntheticAttribute("dataWindow", "box2i", window),
            new SyntheticAttribute("displayWindow", "box2i", window),
            new SyntheticAttribute("lineOrder", "lineOrder", new byte[] { 0 }),
            new SyntheticAttribute("pixelAspectRatio", "float", Float32(1.0f)),
            new SyntheticAttribute("screenWindowCenter", "v2f", new byte[8]),
            new SyntheticAttribute("screenWindowWidth", "float", Float32(1.0f)),
        };
    }

    private static List<SyntheticAttribute> MultipartAttributes(string name)
    {
        List<SyntheticAttribute> attributes = StandardAttributes();
        attributes.Add(new SyntheticAttribute("name", "string", System.Text.Encoding.UTF8.GetBytes(name)));
        attributes.Add(new SyntheticAttribute("type", "string", System.Text.Encoding.ASCII.GetBytes("scanlineimage")));
        attributes.Add(new SyntheticAttribute("chunkCount", "int", Int32(1)));
        return attributes;
    }

    private static SyntheticFile BuildSingle(
        List<SyntheticAttribute> attributes,
        int offsetCount,
        uint flags = 0,
        int payloadByteCount = 0,
        bool pointOffsetsAtPayload = false)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U | flags);
        WriteHeader(writer, attributes);
        int offsetTableStart = checked((int)stream.Position);
        for (int i = 0; i < offsetCount; i++)
        {
            writer.Write(0UL);
        }

        int payloadStart = checked((int)stream.Position);
        writer.Write(new byte[payloadByteCount]);
        byte[] bytes = stream.ToArray();
        if (pointOffsetsAtPayload)
        {
            for (int i = 0; i < offsetCount; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(
                    bytes.AsSpan(offsetTableStart + (i * sizeof(ulong))),
                    (ulong)payloadStart);
            }
        }

        return new SyntheticFile(bytes, offsetTableStart, payloadStart);
    }

    private static SyntheticFile BuildFlatScanlineBlockFile(
        List<SyntheticAttribute> attributes,
        byte[] payload,
        int minimumY)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U);
        WriteHeader(writer, attributes);
        int offsetTableStart = checked((int)stream.Position);
        writer.Write(0UL);
        int chunkStart = checked((int)stream.Position);
        writer.Write(minimumY);
        writer.Write(payload.Length);
        writer.Write(payload);

        byte[] bytes = stream.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(offsetTableStart, sizeof(ulong)),
            (ulong)chunkStart);
        return new SyntheticFile(bytes, offsetTableStart, chunkStart);
    }

    private static SyntheticFile BuildFlatScanlineBlocksFile(
        List<SyntheticAttribute> attributes,
        IReadOnlyList<byte[]> payloads,
        IReadOnlyList<int> minimumYs,
        bool writeOffsets)
    {
        if (payloads.Count != minimumYs.Count)
        {
            throw new ArgumentException("Each payload requires one scanline coordinate.");
        }

        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U);
        WriteHeader(writer, attributes);
        int offsetTableStart = checked((int)stream.Position);
        for (int i = 0; i < payloads.Count; i++)
        {
            writer.Write(0UL);
        }

        int firstChunkStart = checked((int)stream.Position);
        long[] chunkStarts = new long[payloads.Count];
        for (int i = 0; i < payloads.Count; i++)
        {
            chunkStarts[i] = stream.Position;
            writer.Write(minimumYs[i]);
            writer.Write(payloads[i].Length);
            writer.Write(payloads[i]);
        }

        byte[] bytes = stream.ToArray();
        if (writeOffsets)
        {
            for (int i = 0; i < chunkStarts.Length; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(
                    bytes.AsSpan(offsetTableStart + (i * sizeof(ulong)), sizeof(ulong)),
                    (ulong)chunkStarts[i]);
            }
        }

        return new SyntheticFile(bytes, offsetTableStart, firstChunkStart);
    }

    private static SyntheticFile BuildFlatTiledBlockFile(
        List<SyntheticAttribute> attributes,
        byte[] payload,
        int tileX,
        int tileY,
        int levelX,
        int levelY,
        bool writeOffset = true)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U | TiledFlag);
        WriteHeader(writer, attributes);
        int offsetTableStart = checked((int)stream.Position);
        writer.Write(0UL);
        int chunkStart = checked((int)stream.Position);
        writer.Write(tileX);
        writer.Write(tileY);
        writer.Write(levelX);
        writer.Write(levelY);
        writer.Write(payload.Length);
        writer.Write(payload);

        byte[] bytes = stream.ToArray();
        if (writeOffset)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(offsetTableStart, sizeof(ulong)),
                (ulong)chunkStart);
        }

        return new SyntheticFile(bytes, offsetTableStart, chunkStart);
    }

    private static SyntheticFile BuildFlatTiledBlocksFile(
        List<SyntheticAttribute> attributes,
        IReadOnlyList<SyntheticTileBlock> blocks,
        bool writeOffsets = true)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U | TiledFlag);
        WriteHeader(writer, attributes);
        int offsetTableStart = checked((int)stream.Position);
        for (int i = 0; i < blocks.Count; i++)
        {
            writer.Write(0UL);
        }

        int firstChunkStart = checked((int)stream.Position);
        long[] chunkStarts = new long[blocks.Count];
        for (int i = 0; i < blocks.Count; i++)
        {
            SyntheticTileBlock block = blocks[i];
            chunkStarts[i] = stream.Position;
            writer.Write(block.TileX);
            writer.Write(block.TileY);
            writer.Write(block.LevelX);
            writer.Write(block.LevelY);
            writer.Write(block.Payload.Length);
            writer.Write(block.Payload);
        }

        byte[] bytes = stream.ToArray();
        if (writeOffsets)
        {
            for (int i = 0; i < chunkStarts.Length; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(
                    bytes.AsSpan(offsetTableStart + (i * sizeof(ulong)), sizeof(ulong)),
                    (ulong)chunkStarts[i]);
            }
        }

        return new SyntheticFile(bytes, offsetTableStart, firstChunkStart);
    }

    private static SyntheticFile BuildMultipartFlatScanlineBlockFile(
        List<SyntheticAttribute> attributes,
        byte[] payload,
        int partNumber,
        int minimumY,
        bool writeOffsets = true)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U | MultipartFlag);
        WriteHeader(writer, attributes);
        WriteHeader(writer, MultipartAttributes("auxiliary"));
        writer.Write((byte)0);
        int offsetTableStart = checked((int)stream.Position);
        writer.Write(0UL);
        writer.Write(0UL);
        int chunkStart = checked((int)stream.Position);
        writer.Write(partNumber);
        writer.Write(minimumY);
        writer.Write(payload.Length);
        writer.Write(payload);
        int secondChunkStart = checked((int)stream.Position);
        writer.Write(1);
        writer.Write(0);
        writer.Write(2);
        writer.Write(new byte[] { 0x00, 0x00 });

        byte[] bytes = stream.ToArray();
        if (writeOffsets)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(offsetTableStart, sizeof(ulong)),
                (ulong)chunkStart);
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(offsetTableStart + sizeof(ulong), sizeof(ulong)),
                (ulong)secondChunkStart);
        }

        return new SyntheticFile(bytes, offsetTableStart, chunkStart);
    }

    private static byte[] BuildDeclaredAttributePrefix(
        string name,
        string typeName,
        int declaredPayloadByteCount)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U);
        WriteCString(writer, name);
        WriteCString(writer, typeName);
        writer.Write(declaredPayloadByteCount);
        return stream.ToArray();
    }

    private static byte[] BuildMultipart(params SyntheticPart[] parts)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(20_000_630U);
        writer.Write(2U | MultipartFlag);
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

    private static void WriteCString(BinaryWriter writer, string value)
    {
        writer.Write(System.Text.Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    private static byte[] Channels()
    {
        return Channels(new[] { "Y" });
    }

    private static byte[] Channels(params string[] names)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        foreach (string name in names)
        {
            WriteCString(writer, name);
            writer.Write((int)V3.PixelType.Half);
            writer.Write((byte)0);
            writer.Write(new byte[3]);
            writer.Write(1);
            writer.Write(1);
        }

        writer.Write((byte)0);
        return stream.ToArray();
    }

    private static void SetChannels(
        List<SyntheticAttribute> attributes,
        IReadOnlyList<ChannelSpec> channels)
    {
        attributes.RemoveAll(static attribute => attribute.Name == "channels");
        attributes.Add(new SyntheticAttribute("channels", "chlist", ChannelList(channels)));
    }

    private static byte[] ChannelList(IReadOnlyList<ChannelSpec> channels)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        foreach (ChannelSpec channel in channels)
        {
            WriteCString(writer, channel.Name);
            writer.Write((int)channel.PixelType);
            writer.Write((byte)0);
            writer.Write(new byte[3]);
            writer.Write(channel.XSampling);
            writer.Write(channel.YSampling);
        }

        writer.Write((byte)0);
        return stream.ToArray();
    }

    private static byte[] CreateCanonicalBlock(
        V3.Box2i region,
        IReadOnlyList<ChannelSpec> channels)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        for (long y = region.MinY; y <= region.MaxY; y++)
        {
            foreach (ChannelSpec channel in channels)
            {
                if (y % channel.YSampling != 0)
                {
                    continue;
                }

                for (long x = region.MinX; x <= region.MaxX; x++)
                {
                    if (x % channel.XSampling == 0)
                    {
                        WriteSample(writer, channel, (int)x, (int)y);
                    }
                }
            }
        }

        return stream.ToArray();
    }

    private static byte[] CreatePlanarChannel(V3.Box2i region, ChannelSpec channel)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        for (long y = region.MinY; y <= region.MaxY; y++)
        {
            if (y % channel.YSampling != 0)
            {
                continue;
            }

            for (long x = region.MinX; x <= region.MaxX; x++)
            {
                if (x % channel.XSampling == 0)
                {
                    WriteSample(writer, channel, (int)x, (int)y);
                }
            }
        }

        return stream.ToArray();
    }

    private static void WriteSample(BinaryWriter writer, ChannelSpec channel, int x, int y)
    {
        uint value = unchecked(
            ((uint)x * 2_654_435_761U) ^
            ((uint)y * 2_246_822_519U) ^
            ((uint)channel.Name[0] * 32_771U));
        if (channel.PixelType == V3.PixelType.Half)
        {
            writer.Write((ushort)value);
        }
        else
        {
            writer.Write(value);
        }
    }

    private static byte[] Box(int minX, int minY, int maxX, int maxY)
    {
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, minX);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), minY);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), maxX);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), maxY);
        return bytes;
    }

    private static byte[] TileDescription(
        uint sizeX,
        uint sizeY,
        V3.TileLevelMode levelMode,
        V3.TileRoundingMode roundingMode)
    {
        byte[] bytes = new byte[9];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, sizeX);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), sizeY);
        bytes[8] = (byte)((int)levelMode | ((int)roundingMode << 4));
        return bytes;
    }

    private static byte[] Int32(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Float32(float value)
    {
        return Int32(BitConverter.SingleToInt32Bits(value));
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            Assert.Fail($"Expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }

    private sealed class SpySource : V3IO.IExactDataSource, IDisposable
    {
        private readonly byte[] _data;

        public SpySource(byte[] data)
        {
            _data = data;
        }

        public bool HasKnownLength => true;

        public long Length => _data.Length;

        public int ReadCallCount { get; private set; }

        public int MaximumRequestLength { get; private set; }

        public long MaximumRequestedEnd { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool TryGetLength(out long length)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(SpySource));
            }

            length = Length;
            return true;
        }

        public V3IO.DataTransferResult ReadExactly(long offset, Span<byte> destination)
        {
            if (IsDisposed)
            {
                return V3IO.DataTransferResult.Disposed(0, new ObjectDisposedException(nameof(SpySource)));
            }

            ReadCallCount++;
            MaximumRequestLength = Math.Max(MaximumRequestLength, destination.Length);
            MaximumRequestedEnd = Math.Max(MaximumRequestedEnd, checked(offset + destination.Length));
            if (offset < 0 || offset + destination.Length > _data.Length)
            {
                return V3IO.DataTransferResult.EndOfSource(0);
            }

            _data.AsSpan(checked((int)offset), destination.Length).CopyTo(destination);
            return V3IO.DataTransferResult.Success(destination.Length);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class ShortReadMemoryStream : MemoryStream
    {
        private readonly int _maximumRead;

        public ShortReadMemoryStream(byte[] data, int maximumRead)
            : base(data, writable: false)
        {
            _maximumRead = maximumRead;
        }

        public int ReadCallCount { get; private set; }

        public override int Read(Span<byte> buffer)
        {
            ReadCallCount++;
            return base.Read(buffer.Slice(0, Math.Min(buffer.Length, _maximumRead)));
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCallCount++;
            return base.Read(buffer, offset, Math.Min(count, _maximumRead));
        }
    }

    private sealed class FailOnceSource : V3IO.IExactDataSource, IDisposable
    {
        private readonly byte[] _data;

        public FailOnceSource(byte[] data)
        {
            _data = data;
        }

        public bool HasKnownLength => true;

        public long Length => _data.Length;

        public bool FailNextRead { get; set; } = true;

        public bool TryGetLength(out long length)
        {
            length = Length;
            return true;
        }

        public V3IO.DataTransferResult ReadExactly(long offset, Span<byte> destination)
        {
            if (FailNextRead)
            {
                FailNextRead = false;
                return V3IO.DataTransferResult.IoError(0, new IOException("Injected retryable failure."));
            }

            if (offset < 0 || offset + destination.Length > _data.Length)
            {
                return V3IO.DataTransferResult.EndOfSource(0);
            }

            _data.AsSpan(checked((int)offset), destination.Length).CopyTo(destination);
            return V3IO.DataTransferResult.Success(destination.Length);
        }

        public void Dispose()
        {
        }
    }

    private sealed class YieldingAsyncSource : V3IO.IAsyncExactDataSource, IDisposable
    {
        private readonly byte[] _data;
        private int _activeReads;

        public YieldingAsyncSource(byte[] data)
        {
            _data = data;
        }

        public bool HasKnownLength => true;

        public long Length => _data.Length;

        public int ReadCallCount { get; private set; }

        public int MaximumConcurrentReads { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool CancelNextRead { get; set; }

        public Task? ReadGate { get; set; }

        public bool TryGetLength(out long length)
        {
            length = Length;
            return true;
        }

        public async ValueTask<V3IO.DataTransferResult> ReadExactlyAsync(
            long offset,
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            int active = Interlocked.Increment(ref _activeReads);
            MaximumConcurrentReads = Math.Max(MaximumConcurrentReads, active);
            ReadCallCount++;
            try
            {
                Task? readGate = ReadGate;
                if (readGate == null)
                {
                    await Task.Yield();
                }
                else
                {
                    await readGate.ConfigureAwait(false);
                }

                if (CancelNextRead)
                {
                    CancelNextRead = false;
                    return V3IO.DataTransferResult.Canceled(
                        0,
                        new OperationCanceledException(cancellationToken));
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return V3IO.DataTransferResult.Canceled(
                        0,
                        new OperationCanceledException(cancellationToken));
                }

                if (IsDisposed)
                {
                    return V3IO.DataTransferResult.Disposed(
                        0,
                        new ObjectDisposedException(nameof(YieldingAsyncSource)));
                }

                if (offset < 0 || offset + destination.Length > _data.Length)
                {
                    return V3IO.DataTransferResult.EndOfSource(0);
                }

                _data.AsMemory(checked((int)offset), destination.Length).CopyTo(destination);
                return V3IO.DataTransferResult.Success(destination.Length);
            }
            finally
            {
                Interlocked.Decrement(ref _activeReads);
            }
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class SyntheticAttribute
    {
        public SyntheticAttribute(string name, string typeName, byte[] data)
        {
            Name = name;
            TypeName = typeName;
            Data = data;
        }

        public string Name { get; }

        public string TypeName { get; }

        public byte[] Data { get; }
    }

    private sealed class ChannelSpec
    {
        public ChannelSpec(
            string name,
            V3.PixelType pixelType,
            int xSampling,
            int ySampling)
        {
            Name = name;
            PixelType = pixelType;
            XSampling = xSampling;
            YSampling = ySampling;
        }

        public string Name { get; }

        public V3.PixelType PixelType { get; }

        public int XSampling { get; }

        public int YSampling { get; }
    }

    private readonly struct SyntheticTileBlock
    {
        public SyntheticTileBlock(
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            byte[] payload)
        {
            TileX = tileX;
            TileY = tileY;
            LevelX = levelX;
            LevelY = levelY;
            Payload = payload;
        }

        public int TileX { get; }

        public int TileY { get; }

        public int LevelX { get; }

        public int LevelY { get; }

        public byte[] Payload { get; }
    }

    private sealed class SyntheticPart
    {
        public SyntheticPart(List<SyntheticAttribute> attributes, int offsetCount)
        {
            Attributes = attributes;
            OffsetCount = offsetCount;
        }

        public List<SyntheticAttribute> Attributes { get; }

        public int OffsetCount { get; }
    }

    private readonly struct SyntheticFile
    {
        public SyntheticFile(byte[] bytes, int offsetTableStart, int payloadStart)
        {
            Bytes = bytes;
            OffsetTableStart = offsetTableStart;
            PayloadStart = payloadStart;
        }

        public byte[] Bytes { get; }

        public int OffsetTableStart { get; }

        public int PayloadStart { get; }
    }

    private readonly struct DeepSyntheticFile
    {
        public DeepSyntheticFile(
            byte[] bytes,
            int offsetTableStart,
            int chunkStart,
            int countPayloadStart,
            int samplePayloadStart,
            int unpackedSizeOffset,
            byte[][] expectedChannels)
        {
            Bytes = bytes;
            OffsetTableStart = offsetTableStart;
            ChunkStart = chunkStart;
            CountPayloadStart = countPayloadStart;
            SamplePayloadStart = samplePayloadStart;
            UnpackedSizeOffset = unpackedSizeOffset;
            ExpectedChannels = expectedChannels;
        }

        public byte[] Bytes { get; }

        public int OffsetTableStart { get; }

        public int ChunkStart { get; }

        public int CountPayloadStart { get; }

        public int SamplePayloadStart { get; }

        public int UnpackedSizeOffset { get; }

        public byte[][] ExpectedChannels { get; }
    }

    private readonly struct DeepMaterializedSyntheticFile
    {
        public DeepMaterializedSyntheticFile(byte[] bytes, byte[][] expectedChannels)
        {
            Bytes = bytes;
            ExpectedChannels = expectedChannels;
        }

        public byte[] Bytes { get; }

        public byte[][] ExpectedChannels { get; }
    }

    private sealed class DeepChunkPayload
    {
        public DeepChunkPayload(
            int minimumY,
            byte[] packedCounts,
            byte[] packedSamples,
            int unpackedSampleByteCount)
        {
            MinimumY = minimumY;
            PackedCounts = packedCounts;
            PackedSamples = packedSamples;
            UnpackedSampleByteCount = unpackedSampleByteCount;
        }

        public int MinimumY { get; }

        public byte[] PackedCounts { get; }

        public byte[] PackedSamples { get; }

        public int UnpackedSampleByteCount { get; }
    }
}
