using System.Buffers.Binary;
using System.Text;
using V3 = TinyEXR.V3;
using V3Codecs = TinyEXR.V3.Codecs;
using V3Format = TinyEXR.V3.Format;
using V3IO = TinyEXR.V3.IO;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3WriterTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer round-trips out-of-order sampled scanline blocks")]
    public void Case_V3Writer_RoundTripsOutOfOrderSampledScanlineBlocks()
    {
        V3.Box2i dataWindow = new(-2, 4, 3, 23);
        V3.Chromaticities chromaticities = new(
            0.64f, 0.33f,
            0.30f, 0.60f,
            0.15f, 0.06f,
            0.3127f, 0.3290f);
        V3.Header header = new(
            V3.PartType.Scanline,
            dataWindow,
            new[]
            {
                new V3.Channel("Z", V3.PixelType.Float),
                new V3.Channel("A", V3.PixelType.Half),
                new V3.Channel("Y", V3.PixelType.Float, 2, 2),
            },
            compression: V3.Compression.ZIP,
            chromaticities: chromaticities,
            attributes: new[]
            {
                new V3.HeaderAttribute("comments", "string", Encoding.UTF8.GetBytes("v3 writer")),
            });

        using MemoryStream stream = new();
        using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        Assert.AreEqual(0, writer.AddPart(header));
        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        Assert.AreEqual(V3.WriterState.Streaming, writer.State);
        Assert.AreEqual(2, writer.GetNumBlocks(0));

        V3.BlockInfo second = writer.GetBlockInfo(0, 1);
        V3.BlockInfo first = writer.GetBlockInfo(0, 0);
        Assert.AreEqual(
            V3.ExrResult.Success,
            writer.WriteScanlineBlock(0, second.Region.MinY, CreateChannels(header, second.Region)).Status);
        Assert.AreEqual(
            V3.ExrResult.Success,
            writer.WriteScanlineBlock(0, first.Region.MinY, CreateChannels(header, first.Region)).Status);
        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        Assert.AreEqual(V3.WriterState.Complete, writer.State);
        Assert.AreEqual(stream.Length, stream.Position);

        byte[] encoded = stream.ToArray();
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        Assert.IsTrue(reader.GetBlockInfo(0, 1).FileOffset < reader.GetBlockInfo(0, 0).FileOffset);
        V3.Header decodedHeader = reader.GetHeader(0);
        Assert.AreEqual(V3.Compression.ZIP, decodedHeader.Compression);
        Assert.IsTrue(decodedHeader.Chromaticities.HasValue);
        Assert.AreEqual(1, decodedHeader.Attributes.Count);
        Assert.AreEqual("comments", decodedHeader.Attributes[0].Name);
        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("v3 writer"),
            decodedHeader.Attributes[0].Data.ToArray());

        V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
        Assert.IsNotNull(decoded.Value);
        Assert.IsTrue(decoded.Value.IsComplete);
        AssertPartMatches(decoded.Value);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(
            encoded,
            out ExrVersion v1Version,
            out ExrHeader v1Header));
        Assert.AreEqual(2, v1Version.Version);
        Assert.AreEqual(CompressionType.ZIP, v1Header.Compression);
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(
            encoded,
            v1Header,
            out ExrImage v1Image));
        Assert.AreEqual(dataWindow.Width, v1Image.Width);
        Assert.AreEqual(dataWindow.Height, v1Image.Height);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer explicitly frames one part as multipart")]
    public void Case_V3Writer_ForceMultipartRoundTripsOnePart()
    {
        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 3, 1),
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            compression: V3.Compression.ZIP,
            name: "only");
        using MemoryStream stream = new();
        using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(
            sink,
            new V3.WriterOptions(forceMultipart: true)))
        {
            writer.AddPart(header);
            Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
            for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(0); blockIndex++)
            {
                V3.BlockInfo block = writer.GetBlockInfo(0, blockIndex);
                Assert.AreEqual(
                    V3.ExrResult.Success,
                    writer.WriteScanlineBlock(
                        0,
                        block.Region.MinY,
                        CreateChannels(header, block.Region)).Status);
            }

            Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        }

        byte[] encoded = stream.ToArray();
        Assert.AreNotEqual(0U, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4)) & (1U << 12));
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        Assert.AreEqual(1, reader.NumParts);
        Assert.AreEqual("only", reader.GetHeader(0).Name);
        Assert.IsTrue(reader.GetRawHeaderAttributes(0).Any(
            static attribute => attribute.Name == "chunkCount"));
        V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
        Assert.IsNotNull(decoded.Value);
        AssertPartMatches(decoded.Value);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer round-trips equal-sized B44 FLOAT payloads")]
    public void Case_V3Writer_RoundTripsEqualSizedB44FloatPayloads()
    {
        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 31, 31),
            new[]
            {
                new V3.Channel("B", V3.PixelType.Float),
                new V3.Channel("G", V3.PixelType.Float),
                new V3.Channel("R", V3.PixelType.Float),
            },
            compression: V3.Compression.B44);

        using MemoryStream stream = new();
        using (V3IO.StreamDataSink sink = new(stream, leaveOpen: true))
        using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink))
        {
            writer.AddPart(header);
            Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
            for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(0); blockIndex++)
            {
                V3.BlockInfo block = writer.GetBlockInfo(0, blockIndex);
                Assert.AreEqual(
                    V3.ExrResult.Success,
                    writer.WriteScanlineBlock(
                        0,
                        block.Region.MinY,
                        CreateChannels(header, block.Region)).Status);
            }

            Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        }

        using V3.ExrReader reader = V3.ExrReader.OpenMemory(stream.ToArray());
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
        Assert.IsNotNull(decoded.Value);
        AssertPartMatches(decoded.Value);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer emits entropy-compressed ZSTD frames")]
    public void Case_V3Writer_EmitsEntropyCompressedZstdFrames()
    {
        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 1023, 31),
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            compression: V3.Compression.ZSTD);
        byte[] expected = CreateRepeatingFloatBytes(checked((int)(header.DataWindow.Width * header.DataWindow.Height)));

        using MemoryStream stream = new();
        using (V3IO.StreamDataSink sink = new(stream, leaveOpen: true))
        using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink))
        {
            writer.AddPart(header);
            Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
            Assert.AreEqual(1, writer.GetNumBlocks(0));
            V3.BlockInfo block = writer.GetBlockInfo(0, 0);
            Assert.AreEqual(
                V3.ExrResult.Success,
                writer.WriteScanlineBlock(
                    0,
                    block.Region.MinY,
                    new[] { new V3.ChannelBuffer("R", V3.PixelType.Float, expected) }).Status);
            Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        }

        byte[] encoded = stream.ToArray();
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.BlockInfo encodedBlock = reader.GetBlockInfo(0, 0);
        int rawSize = checked((int)encodedBlock.UncompressedByteCount!.Value);
        int chunkOffset = checked((int)encodedBlock.FileOffset);
        int packedSize = BinaryPrimitives.ReadInt32LittleEndian(
            encoded.AsSpan(chunkOffset + sizeof(int), sizeof(int)));
        Assert.IsTrue(packedSize < rawSize, $"Expected {packedSize} packed bytes to be smaller than {rawSize} raw bytes.");

        ReadOnlySpan<byte> payload = encoded.AsSpan(
            chunkOffset + encodedBlock.ChunkHeaderByteCount,
            packedSize);
        Assert.IsTrue(ContainsEntropyCompressedZstdBlock(payload));
        byte[] decodedPayload = new byte[rawSize];
        Assert.AreEqual(
            V3Codecs.ZstdFrameStatus.Success,
            V3Codecs.ZstdFrameDecoder.Decode(
                payload,
                decodedPayload,
                out int consumed,
                out int written,
                out _));
        Assert.AreEqual(payload.Length, consumed);
        Assert.AreEqual(decodedPayload.Length, written);
        CollectionAssert.AreEqual(expected, decodedPayload);

        V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
        Assert.IsNotNull(decoded.Value);
        CollectionAssert.AreEqual(
            expected,
            decoded.Value.GetLevel(0, 0).GetChannel("R").Data.ToArray());

        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader v1Header));
        Assert.AreEqual(CompressionType.ZSTD, v1Header.Compression);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRImageFromMemory(encoded, v1Header, out ExrImage v1Image));
        CollectionAssert.AreEqual(expected, v1Image.GetChannel("R").Data);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer stores non-beneficial ZSTD blocks raw")]
    public void Case_V3Writer_StoresNonBeneficialZstdBlocksRaw()
    {
        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 1, 0),
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            compression: V3.Compression.ZSTD);
        byte[] expected = CreateRepeatingFloatBytes(2);

        using MemoryStream stream = new();
        using (V3IO.StreamDataSink sink = new(stream, leaveOpen: true))
        using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink))
        {
            writer.AddPart(header);
            Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
            V3.BlockInfo block = writer.GetBlockInfo(0, 0);
            Assert.AreEqual(
                V3.ExrResult.Success,
                writer.WriteScanlineBlock(
                    0,
                    block.Region.MinY,
                    new[] { new V3.ChannelBuffer("R", V3.PixelType.Float, expected) }).Status);
            Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        }

        byte[] encoded = stream.ToArray();
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.BlockInfo encodedBlock = reader.GetBlockInfo(0, 0);
        int chunkOffset = checked((int)encodedBlock.FileOffset);
        int packedSize = BinaryPrimitives.ReadInt32LittleEndian(
            encoded.AsSpan(chunkOffset + sizeof(int), sizeof(int)));
        Assert.AreEqual(expected.Length, packedSize);
        CollectionAssert.AreEqual(
            expected,
            encoded.AsSpan(chunkOffset + encodedBlock.ChunkHeaderByteCount, packedSize).ToArray());

        V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
        Assert.IsNotNull(decoded.Value);
        CollectionAssert.AreEqual(
            expected,
            decoded.Value.GetLevel(0, 0).GetChannel("R").Data.ToArray());
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep writer compresses ZSTD count and sample payloads")]
    public void Case_V3DeepWriter_CompressesZstdCountAndSamplePayloads()
    {
        V3.Header header = new(
            V3.PartType.DeepScanline,
            new V3.Box2i(0, 0, 1023, 31),
            new[] { new V3.Channel("Z", V3.PixelType.Float) },
            compression: V3.Compression.ZSTD);
        int pixelCount = checked((int)(header.DataWindow.Width * header.DataWindow.Height));
        int[] counts = Enumerable.Repeat(2, pixelCount).ToArray();
        byte[] samples = CreateRepeatingFloatBytes(checked(pixelCount * 2));

        using MemoryStream stream = new();
        using (V3IO.StreamDataSink sink = new(stream, leaveOpen: true))
        using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink))
        {
            writer.AddPart(header);
            Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
            Assert.AreEqual(1, writer.GetNumBlocks(0));
            V3.BlockInfo block = writer.GetBlockInfo(0, 0);
            Assert.AreEqual(
                V3.ExrResult.Success,
                writer.WriteDeepScanlineBlock(
                    0,
                    block.Region.MinY,
                    counts,
                    new[] { new V3.ChannelBuffer("Z", V3.PixelType.Float, samples) }).Status);
            Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        }

        byte[] encoded = stream.ToArray();
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.BlockInfo encodedBlock = reader.GetBlockInfo(0, 0);
        int chunkOffset = checked((int)encodedBlock.FileOffset);
        int packedCountSize = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
            encoded.AsSpan(chunkOffset + sizeof(int), sizeof(ulong))));
        int packedSampleSize = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
            encoded.AsSpan(chunkOffset + sizeof(int) + sizeof(ulong), sizeof(ulong))));
        int unpackedSampleSize = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
            encoded.AsSpan(chunkOffset + sizeof(int) + (2 * sizeof(ulong)), sizeof(ulong))));
        int unpackedCountSize = checked(pixelCount * sizeof(int));
        Assert.IsTrue(packedCountSize < unpackedCountSize);
        Assert.IsTrue(packedSampleSize < unpackedSampleSize);
        Assert.AreEqual(samples.Length, unpackedSampleSize);

        int payloadOffset = chunkOffset + encodedBlock.ChunkHeaderByteCount;
        ReadOnlySpan<byte> packedCounts = encoded.AsSpan(payloadOffset, packedCountSize);
        ReadOnlySpan<byte> packedSamples = encoded.AsSpan(
            payloadOffset + packedCountSize,
            packedSampleSize);
        Assert.IsTrue(ContainsEntropyCompressedZstdBlock(packedCounts));
        Assert.IsTrue(ContainsEntropyCompressedZstdBlock(packedSamples));

        byte[] decodedCounts = new byte[unpackedCountSize];
        Assert.AreEqual(
            V3Codecs.ZstdFrameStatus.Success,
            V3Codecs.ZstdFrameDecoder.Decode(
                packedCounts,
                decodedCounts,
                out int countConsumed,
                out int countWritten,
                out _));
        Assert.AreEqual(packedCounts.Length, countConsumed);
        Assert.AreEqual(decodedCounts.Length, countWritten);
        CollectionAssert.AreEqual(CreateCumulativeDeepCounts(header.DataWindow, counts), decodedCounts);

        byte[] decodedSamples = new byte[unpackedSampleSize];
        Assert.AreEqual(
            V3Codecs.ZstdFrameStatus.Success,
            V3Codecs.ZstdFrameDecoder.Decode(
                packedSamples,
                decodedSamples,
                out int sampleConsumed,
                out int sampleWritten,
                out _));
        Assert.AreEqual(packedSamples.Length, sampleConsumed);
        Assert.AreEqual(decodedSamples.Length, sampleWritten);
        CollectionAssert.AreEqual(samples, decodedSamples);

        V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
        Assert.IsNotNull(decoded.Value);
        V3.DeepLevel level = (V3.DeepLevel)decoded.Value.GetLevel(0, 0);
        CollectionAssert.AreEqual(counts, level.SampleCounts.ToArray());
        CollectionAssert.AreEqual(samples, level.GetChannel("Z").Data.ToArray());
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep writer genuinely compresses RLE and ZIP payloads")]
    public void Case_V3DeepWriter_CompressesRleAndZipPayloads()
    {
        foreach (V3.Compression compression in new[]
        {
            V3.Compression.RLE,
            V3.Compression.ZIPS,
            V3.Compression.ZIP,
        })
        {
            V3.Header header = new(
                V3.PartType.DeepScanline,
                new V3.Box2i(0, 0, 1023, 0),
                new[] { new V3.Channel("Z", V3.PixelType.Float) },
                compression: compression);
            int[] counts = Enumerable.Repeat(1, 1024).ToArray();
            byte[] samples = new byte[checked(counts.Length * sizeof(float))];

            using MemoryStream stream = new();
            using (V3IO.StreamDataSink sink = new(stream, leaveOpen: true))
            using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink))
            {
                writer.AddPart(header);
                Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
                V3.BlockInfo block = writer.GetBlockInfo(0, 0);
                Assert.AreEqual(
                    V3.ExrResult.Success,
                    writer.WriteDeepScanlineBlock(
                        0,
                        block.Region.MinY,
                        counts,
                        new[] { new V3.ChannelBuffer("Z", V3.PixelType.Float, samples) }).Status);
                Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
            }

            byte[] encoded = stream.ToArray();
            using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            V3.BlockInfo encodedBlock = reader.GetBlockInfo(0, 0);
            int chunkOffset = checked((int)encodedBlock.FileOffset);
            int packedCountSize = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
                encoded.AsSpan(chunkOffset + sizeof(int), sizeof(ulong))));
            int packedSampleSize = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
                encoded.AsSpan(chunkOffset + sizeof(int) + sizeof(ulong), sizeof(ulong))));
            int unpackedSampleSize = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
                encoded.AsSpan(chunkOffset + sizeof(int) + (2 * sizeof(ulong)), sizeof(ulong))));
            Assert.IsTrue(
                packedCountSize < checked(counts.Length * sizeof(int)),
                $"{compression} stored the deep count table as raw fallback.");
            Assert.IsTrue(
                packedSampleSize < unpackedSampleSize,
                $"{compression} stored the deep sample payload as raw fallback.");

            V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
            Assert.IsNotNull(decoded.Value);
            V3.DeepLevel level = (V3.DeepLevel)decoded.Value.GetLevel(0, 0);
            CollectionAssert.AreEqual(counts, level.SampleCounts.ToArray());
            CollectionAssert.AreEqual(samples, level.GetChannel("Z").Data.ToArray());
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer round-trips multipart mip and rip tiles")]
    public void Case_V3Writer_RoundTripsMultipartMipAndRipTiles()
    {
        V3.Header scanline = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 2, 1),
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            compression: V3.Compression.None,
            name: "beauty");
        V3.Header mip = new(
            V3.PartType.Tiled,
            new V3.Box2i(5, -2, 9, 0),
            new[]
            {
                new V3.Channel("B", V3.PixelType.Half),
                new V3.Channel("G", V3.PixelType.Half),
            },
            compression: V3.Compression.PIZ,
            tiles: new V3.TileDescription(
                3,
                2,
                V3.TileLevelMode.MipmapLevels,
                V3.TileRoundingMode.RoundDown),
            name: "mip");
        V3.Header rip = new(
            V3.PartType.Tiled,
            new V3.Box2i(-3, 2, 1, 4),
            new[] { new V3.Channel("Z", V3.PixelType.Float) },
            compression: V3.Compression.ZSTD,
            lineOrder: V3.LineOrder.RandomY,
            tiles: new V3.TileDescription(
                2,
                2,
                V3.TileLevelMode.RipmapLevels,
                V3.TileRoundingMode.RoundUp),
            name: "rip");
        V3.Header[] headers = { scanline, mip, rip };

        using MemoryStream stream = new();
        using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        for (int i = 0; i < headers.Length; i++)
        {
            Assert.AreEqual(i, writer.AddPart(headers[i]));
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        for (int partIndex = headers.Length - 1; partIndex >= 0; partIndex--)
        {
            for (int blockIndex = writer.GetNumBlocks(partIndex) - 1; blockIndex >= 0; blockIndex--)
            {
                V3.BlockInfo info = writer.GetBlockInfo(partIndex, blockIndex);
                IReadOnlyList<V3.ChannelBuffer> channels = CreateChannels(headers[partIndex], info.Region);
                V3.WriterResult result = info.IsTiled
                    ? writer.WriteTile(
                        partIndex,
                        info.TileX,
                        info.TileY,
                        info.LevelX,
                        info.LevelY,
                        channels)
                    : writer.WriteScanlineBlock(partIndex, info.Region.MinY, channels);
                Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
            }
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(stream.ToArray());
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        Assert.AreEqual(headers.Length, reader.NumParts);
        for (int partIndex = 0; partIndex < headers.Length; partIndex++)
        {
            Assert.AreEqual(headers[partIndex].Name, reader.GetHeader(partIndex).Name);
            Assert.AreEqual(headers[partIndex].PartType, reader.GetHeader(partIndex).PartType);
            Assert.AreEqual(headers[partIndex].Compression, reader.GetHeader(partIndex).Compression);
            V3.ReaderResult<V3.Part> decoded = reader.ReadPart(partIndex);
            Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
            Assert.IsNotNull(decoded.Value);
            Assert.IsTrue(decoded.Value.IsComplete);
            AssertPartMatches(decoded.Value);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer round-trips out-of-order deep scanline blocks")]
    public void Case_V3Writer_RoundTripsOutOfOrderDeepScanlineBlocks()
    {
        V3.Header header = new(
            V3.PartType.DeepScanline,
            new V3.Box2i(-1, 3, 2, 21),
            new[]
            {
                new V3.Channel("Z", V3.PixelType.Float),
                new V3.Channel("A", V3.PixelType.Half),
            },
            compression: V3.Compression.ZIP);

        using MemoryStream stream = new();
        using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        writer.AddPart(header);
        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        Assert.AreEqual(2, writer.GetNumBlocks(0));

        for (int blockIndex = writer.GetNumBlocks(0) - 1; blockIndex >= 0; blockIndex--)
        {
            V3.BlockInfo info = writer.GetBlockInfo(0, blockIndex);
            int[] counts = CreateDeepCounts(info.Region, info.LevelX, info.LevelY);
            V3.WriterResult result = writer.WriteDeepScanlineBlock(
                0,
                info.Region.MinY,
                counts,
                CreateDeepChannels(header, info.Region, counts, info.LevelX, info.LevelY));
            Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        byte[] encoded = stream.ToArray();
        int[] expectedCounts = CreateDeepCounts(header.DataWindow);
        AssertMaximumSamples(encoded, 0, expectedCounts.Max());

        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded))
        {
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            Assert.IsTrue(
                reader.GetBlockInfo(0, reader.GetNumBlocks(0) - 1).FileOffset <
                reader.GetBlockInfo(0, 0).FileOffset);
            V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
            Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
            Assert.IsNotNull(decoded.Value);
            AssertDeepLevelMatches(header, (V3.DeepLevel)decoded.Value.GetLevel(0, 0));
        }

    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep writer output loads through the V1 API")]
    public void Case_V3DeepWriter_OutputLoadsThroughV1Api()
    {
        V3.Header header = new(
            V3.PartType.DeepScanline,
            new V3.Box2i(-1, 3, 2, 5),
            new[] { new V3.Channel("Z", V3.PixelType.Float) },
            compression: V3.Compression.ZIPS);

        using MemoryStream stream = new();
        using (V3IO.StreamDataSink sink = new(stream, leaveOpen: true))
        using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink))
        {
            writer.AddPart(header);
            Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
            for (int blockIndex = writer.GetNumBlocks(0) - 1; blockIndex >= 0; blockIndex--)
            {
                V3.BlockInfo info = writer.GetBlockInfo(0, blockIndex);
                int[] counts = CreateDeepCounts(info.Region);
                Assert.AreEqual(
                    V3.ExrResult.Success,
                    writer.WriteDeepScanlineBlock(
                        0,
                        info.Region.MinY,
                        counts,
                        CreateDeepChannels(header, info.Region, counts)).Status);
            }

            Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        }

        byte[] encoded = stream.ToArray();
        int[] expectedCounts = CreateDeepCounts(header.DataWindow);
        AssertMaximumSamples(encoded, 0, expectedCounts.Max());
        using MemoryStream v1Stream = new(encoded, writable: false);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadDeepEXRFromStream(v1Stream, out ExrHeader v1Header, out ExrDeepImage v1Image));
        Assert.AreEqual(header.DataWindow.Width, v1Image.Width);
        Assert.AreEqual(header.DataWindow.Height, v1Image.Height);
        Assert.AreEqual(header.DataWindow.MinX, v1Header.DataWindow.MinX);
        Assert.AreEqual(header.DataWindow.MinY, v1Header.DataWindow.MinY);
        Assert.AreEqual(header.DataWindow.MaxX, v1Header.DataWindow.MaxX);
        Assert.AreEqual(header.DataWindow.MaxY, v1Header.DataWindow.MaxY);

        int countIndex = 0;
        for (int row = 0; row < v1Image.OffsetTable.Length; row++)
        {
            int cumulative = 0;
            List<float> expectedSamples = new();
            int y = header.DataWindow.MinY + row;
            for (int xIndex = 0; xIndex < v1Image.OffsetTable[row].Length; xIndex++)
            {
                int count = expectedCounts[countIndex++];
                cumulative += count;
                Assert.AreEqual(cumulative, v1Image.OffsetTable[row][xIndex]);
                int x = header.DataWindow.MinX + xIndex;
                for (int sample = 0; sample < count; sample++)
                {
                    int seed = unchecked(
                        (header.Channels[0].Name[0] * 257) +
                        (x * 17) +
                        (y * 31) +
                        (sample * 53));
                    expectedSamples.Add(seed * 0.125f);
                }
            }

            CollectionAssert.AreEqual(
                expectedSamples.ToArray(),
                v1Image.Channels[0].Rows[row]);
        }

        Assert.AreEqual(expectedCounts.Length, countIndex);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer round-trips multipart deep mip and rip tiles")]
    public void Case_V3Writer_RoundTripsMultipartDeepMipAndRipTiles()
    {
        V3.Header mip = new(
            V3.PartType.DeepTiled,
            new V3.Box2i(1, -2, 5, 1),
            new[]
            {
                new V3.Channel("A", V3.PixelType.Half),
                new V3.Channel("Z", V3.PixelType.Float),
            },
            compression: V3.Compression.ZSTD,
            tiles: new V3.TileDescription(
                3,
                2,
                V3.TileLevelMode.MipmapLevels,
                V3.TileRoundingMode.RoundDown),
            name: "deep-mip");
        V3.Header rip = new(
            V3.PartType.DeepTiled,
            new V3.Box2i(-3, 4, 1, 6),
            new[] { new V3.Channel("ID", V3.PixelType.UInt) },
            compression: V3.Compression.HTJ2K256,
            lineOrder: V3.LineOrder.RandomY,
            tiles: new V3.TileDescription(
                2,
                2,
                V3.TileLevelMode.RipmapLevels,
                V3.TileRoundingMode.RoundUp),
            name: "deep-rip");
        V3.Header[] headers = { mip, rip };
        int[] maximumSamples = new int[headers.Length];

        using MemoryStream stream = new();
        using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        foreach (V3.Header header in headers)
        {
            writer.AddPart(header);
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        for (int partIndex = headers.Length - 1; partIndex >= 0; partIndex--)
        {
            for (int blockIndex = writer.GetNumBlocks(partIndex) - 1; blockIndex >= 0; blockIndex--)
            {
                V3.BlockInfo info = writer.GetBlockInfo(partIndex, blockIndex);
                int[] counts = CreateDeepCounts(info.Region, info.LevelX, info.LevelY);
                maximumSamples[partIndex] = Math.Max(maximumSamples[partIndex], counts.Max());
                V3.WriterResult result = writer.WriteDeepTile(
                    partIndex,
                    info.TileX,
                    info.TileY,
                    info.LevelX,
                    info.LevelY,
                    counts,
                    CreateDeepChannels(
                        headers[partIndex],
                        info.Region,
                        counts,
                        info.LevelX,
                        info.LevelY));
                Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
            }
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        byte[] encoded = stream.ToArray();
        for (int partIndex = 0; partIndex < headers.Length; partIndex++)
        {
            AssertMaximumSamples(encoded, partIndex, maximumSamples[partIndex]);
        }

        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        for (int partIndex = 0; partIndex < headers.Length; partIndex++)
        {
            V3.ReaderResult<V3.Part> decoded = reader.ReadPart(partIndex);
            Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
            Assert.IsNotNull(decoded.Value);
            Assert.IsTrue(decoded.Value.Levels.Count > 1);
            foreach (V3.PartLevel level in decoded.Value.Levels)
            {
                AssertDeepLevelMatches(headers[partIndex], (V3.DeepLevel)level);
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep writer retries exact partial writes")]
    public void Case_V3DeepWriter_RetriesExactPartialWrites()
    {
        V3.Header header = new(
            V3.PartType.DeepScanline,
            new V3.Box2i(0, 0, 1, 0),
            new[] { new V3.Channel("Z", V3.PixelType.Float) },
            compression: V3.Compression.RLE);
        using RetryDataSink sink = new();
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(
            sink,
            new V3.WriterOptions(leaveOpen: true));
        writer.AddPart(header);
        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);

        V3.BlockInfo info = writer.GetBlockInfo(0, 0);
        int[] originalCounts = { 2, 1 };
        IReadOnlyList<V3.ChannelBuffer> originalChannels = CreateDeepChannels(
            header,
            info.Region,
            originalCounts);
        sink.FailNextWriteAfter = 13;
        V3.WriterResult failed = writer.WriteDeepScanlineBlock(
            0,
            info.Region.MinY,
            originalCounts,
            originalChannels);
        Assert.AreEqual(V3.ExrResult.IO, failed.Status);
        Assert.AreEqual(13L, failed.BytesWritten);
        Assert.AreEqual(V3.WriterState.WritingBlock, writer.State);

        Assert.AreEqual(
            V3.ExrResult.Success,
            writer.WriteDeepScanlineBlock(
                0,
                info.Region.MinY,
                ReadOnlySpan<int>.Empty,
                Array.Empty<V3.ChannelBuffer>()).Status);

        sink.FailNextWriteAfter = 2;
        Assert.AreEqual(V3.ExrResult.IO, writer.End().Status);
        Assert.AreEqual(V3.WriterState.Ending, writer.State);
        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);

        byte[] encoded = sink.ToArray();
        AssertMaximumSamples(encoded, 0, 2);
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
        Assert.IsNotNull(decoded.Value);
        V3.DeepLevel level = (V3.DeepLevel)decoded.Value.GetLevel(0, 0);
        CollectionAssert.AreEqual(originalCounts, level.SampleCounts.ToArray());
        CollectionAssert.AreEqual(
            originalChannels[0].Data.ToArray(),
            level.Channels[0].Data.ToArray());
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep writer resumes asynchronous WouldBlock output")]
    public async Task Case_V3DeepWriter_ResumesAsyncWouldBlockOutput()
    {
        V3.Header scanline = new(
            V3.PartType.DeepScanline,
            new V3.Box2i(0, 0, 1, 0),
            new[] { new V3.Channel("Z", V3.PixelType.Float) },
            name: "scan");
        V3.Header tiled = new(
            V3.PartType.DeepTiled,
            new V3.Box2i(-1, 2, 0, 3),
            new[] { new V3.Channel("A", V3.PixelType.Half) },
            compression: V3.Compression.ZSTD,
            tiles: new V3.TileDescription(2, 2),
            name: "tile");
        V3.Header[] headers = { scanline, tiled };

        await using RetryDataSink sink = new();
        await using V3.ExrWriter writer = V3.ExrWriter.OpenAsyncSink(
            sink,
            new V3.WriterOptions(leaveOpen: true));
        writer.AddPart(scanline);
        writer.AddPart(tiled);
        Assert.AreEqual(V3.ExrResult.Success, (await writer.BeginAsync()).Status);

        V3.BlockInfo scanlineInfo = writer.GetBlockInfo(0, 0);
        int[] scanlineCounts = CreateDeepCounts(scanlineInfo.Region);
        sink.BlockNextWrite = true;
        V3.WriterResult blocked = await writer.WriteDeepScanlineBlockAsync(
            0,
            scanlineInfo.Region.MinY,
            scanlineCounts,
            CreateDeepChannels(scanline, scanlineInfo.Region, scanlineCounts));
        Assert.AreEqual(V3.ExrResult.WouldBlock, blocked.Status);
        Assert.IsTrue(blocked.Pending.HasValue);
        Assert.AreEqual(
            V3.ExrResult.Success,
            (await writer.WriteDeepScanlineBlockAsync(
                0,
                scanlineInfo.Region.MinY,
                ReadOnlyMemory<int>.Empty,
                Array.Empty<V3.ChannelBuffer>())).Status);

        V3.BlockInfo tileInfo = writer.GetBlockInfo(1, 0);
        int[] tileCounts = CreateDeepCounts(tileInfo.Region, tileInfo.LevelX, tileInfo.LevelY);
        Assert.AreEqual(
            V3.ExrResult.Success,
            (await writer.WriteDeepTileAsync(
                1,
                tileInfo.TileX,
                tileInfo.TileY,
                tileInfo.LevelX,
                tileInfo.LevelY,
                tileCounts,
                CreateDeepChannels(tiled, tileInfo.Region, tileCounts))).Status);

        sink.BlockNextWrite = true;
        Assert.AreEqual(V3.ExrResult.WouldBlock, (await writer.EndAsync()).Status);
        Assert.AreEqual(V3.ExrResult.Success, (await writer.EndAsync()).Status);

        using V3.ExrReader reader = V3.ExrReader.OpenMemory(sink.ToArray());
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        for (int partIndex = 0; partIndex < headers.Length; partIndex++)
        {
            V3.ReaderResult<V3.Part> decoded = reader.ReadPart(partIndex);
            Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
            Assert.IsNotNull(decoded.Value);
            AssertDeepLevelMatches(headers[partIndex], (V3.DeepLevel)decoded.Value.GetLevel(0, 0));
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer retries exact synchronous partial writes")]
    public void Case_V3Writer_RetriesExactSynchronousPartialWrites()
    {
        V3.Header header = SimpleHeader(V3.Compression.ZIP);
        using RetryDataSink sink = new();
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink, new V3.WriterOptions(leaveOpen: true));
        writer.AddPart(header);

        sink.FailNextWriteAfter = 5;
        V3.WriterResult beginFailure = writer.Begin();
        Assert.AreEqual(V3.ExrResult.IO, beginFailure.Status);
        Assert.AreEqual(5L, beginFailure.BytesWritten);
        Assert.AreEqual(V3.WriterState.Beginning, writer.State);
        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);

        V3.BlockInfo block = writer.GetBlockInfo(0, 0);
        IReadOnlyList<V3.ChannelBuffer> original = CreateChannels(header, block.Region);
        sink.FailNextWriteAfter = 3;
        V3.WriterResult blockFailure = writer.WriteScanlineBlock(0, block.Region.MinY, original);
        Assert.AreEqual(V3.ExrResult.IO, blockFailure.Status);
        Assert.AreEqual(3L, blockFailure.BytesWritten);
        Assert.AreEqual(V3.WriterState.WritingBlock, writer.State);

        IReadOnlyList<V3.ChannelBuffer> ignoredRetryData = new[]
        {
            new V3.ChannelBuffer("R", V3.PixelType.Float, new byte[original[0].ByteLength]),
        };
        Assert.AreEqual(
            V3.ExrResult.Success,
            writer.WriteScanlineBlock(0, block.Region.MinY, ignoredRetryData).Status);

        sink.FailNextWriteAfter = 4;
        V3.WriterResult endFailure = writer.End();
        Assert.AreEqual(V3.ExrResult.IO, endFailure.Status);
        Assert.AreEqual(4L, endFailure.BytesWritten);
        Assert.AreEqual(V3.WriterState.Ending, writer.State);
        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);

        using V3.ExrReader reader = V3.ExrReader.OpenMemory(sink.ToArray());
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
        Assert.IsNotNull(decoded.Value);
        AssertPartMatches(decoded.Value);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer resumes async cancellation and WouldBlock")]
    public async Task Case_V3Writer_ResumesAsyncCancellationAndWouldBlock()
    {
        V3.Header header = SimpleHeader(V3.Compression.ZSTD);
        await using RetryDataSink sink = new();
        await using V3.ExrWriter writer = V3.ExrWriter.OpenAsyncSink(
            sink,
            new V3.WriterOptions(leaveOpen: true));
        writer.AddPart(header);

        sink.CancelNextWriteAfter = 7;
        await AssertThrowsAsync<OperationCanceledException>(() => writer.BeginAsync().AsTask());
        Assert.AreEqual(V3.WriterState.Beginning, writer.State);
        Assert.AreEqual(V3.ExrResult.Success, (await writer.BeginAsync()).Status);

        V3.BlockInfo block = writer.GetBlockInfo(0, 0);
        sink.BlockNextWrite = true;
        V3.WriterResult blocked = await writer.WriteScanlineBlockAsync(
            0,
            block.Region.MinY,
            CreateChannels(header, block.Region));
        Assert.AreEqual(V3.ExrResult.WouldBlock, blocked.Status);
        Assert.AreEqual(V3.WriterState.WritingBlock, writer.State);
        Assert.IsTrue(blocked.Pending.HasValue);
        Assert.AreEqual(
            V3.ExrResult.Success,
            (await writer.WriteScanlineBlockAsync(
                0,
                block.Region.MinY,
                CreateChannels(header, block.Region))).Status);
        Assert.AreEqual(V3.ExrResult.Success, (await writer.EndAsync()).Status);

        using V3.ExrReader reader = V3.ExrReader.OpenMemory(sink.ToArray());
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.ReaderResult<V3.Part> decoded = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, decoded.Status, decoded.Error?.ToString());
        Assert.IsNotNull(decoded.Value);
        AssertPartMatches(decoded.Value);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer rejects missing duplicate invalid and ambiguous output")]
    public void Case_V3Writer_RejectsMissingDuplicateInvalidAndAmbiguousOutput()
    {
        V3.Header header = SimpleHeader(V3.Compression.None);
        using (MemoryStream stream = new())
        using (V3IO.StreamDataSink sink = new(stream, leaveOpen: true))
        using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink))
        {
            writer.AddPart(header);
            Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
            Assert.AreEqual(V3.ExrResult.InvalidArgument, writer.End().Status);
            V3.BlockInfo block = writer.GetBlockInfo(0, 0);
            IReadOnlyList<V3.ChannelBuffer> channels = CreateChannels(header, block.Region);
            Assert.AreEqual(
                V3.ExrResult.Success,
                writer.WriteScanlineBlock(0, block.Region.MinY, channels).Status);
            Assert.AreEqual(
                V3.ExrResult.InvalidArgument,
                writer.WriteScanlineBlock(0, block.Region.MinY, channels).Status);
            V3.BlockInfo remaining = writer.GetBlockInfo(0, 1);
            Assert.AreEqual(
                V3.ExrResult.Success,
                writer.WriteScanlineBlock(
                    0,
                    remaining.Region.MinY,
                    CreateChannels(header, remaining.Region)).Status);
            Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
            Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        }

        V3.Header deep = new(
            V3.PartType.DeepScanline,
            new V3.Box2i(0, 0, 0, 0),
            new[] { new V3.Channel("Z", V3.PixelType.Float) });
        using (MemoryStream stream = new())
        using (V3IO.StreamDataSink sink = new(stream, leaveOpen: true))
        using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink))
        {
            writer.AddPart(deep);
            Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
            V3.BlockInfo info = writer.GetBlockInfo(0, 0);
            Assert.AreEqual(
                V3.ExrResult.Unsupported,
                writer.WriteScanlineBlock(
                    0,
                    info.Region.MinY,
                    new[] { new V3.ChannelBuffer("Z", V3.PixelType.Float, new byte[4]) }).Status);
            Assert.AreEqual(
                V3.ExrResult.InvalidArgument,
                writer.WriteDeepScanlineBlock(
                    0,
                    info.Region.MinY,
                    ReadOnlySpan<int>.Empty,
                    Array.Empty<V3.ChannelBuffer>()).Status);
            Assert.AreEqual(
                V3.ExrResult.InvalidArgument,
                writer.WriteDeepScanlineBlock(
                    0,
                    info.Region.MinY,
                    new[] { -1 },
                    new[] { new V3.ChannelBuffer("Z", V3.PixelType.Float, Array.Empty<byte>()) }).Status);

            int[] counts = { 2 };
            IReadOnlyList<V3.ChannelBuffer> channels = CreateDeepChannels(deep, info.Region, counts);
            Assert.AreEqual(
                V3.ExrResult.Success,
                writer.WriteDeepScanlineBlock(0, info.Region.MinY, counts, channels).Status);
            Assert.AreEqual(
                V3.ExrResult.InvalidArgument,
                writer.WriteDeepScanlineBlock(0, info.Region.MinY, counts, channels).Status);
            Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        }

        using (MemoryStream stream = new())
        using (V3IO.StreamDataSink sink = new(stream, leaveOpen: true))
        using (V3.ExrWriter writer = V3.ExrWriter.OpenSink(
            sink,
            new V3.WriterOptions(
                new V3.WriterLimits(maximumDeepSampleCount: 1))))
        {
            writer.AddPart(deep);
            Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
            V3.BlockInfo info = writer.GetBlockInfo(0, 0);
            V3.WriterResult limited = writer.WriteDeepScanlineBlock(
                0,
                info.Region.MinY,
                new[] { 2 },
                CreateDeepChannels(deep, info.Region, new[] { 2 }));
            Assert.AreEqual(V3.ExrResult.Unsupported, limited.Status);
            Assert.IsInstanceOfType<V3.WriterLimitExceededException>(limited.Error);
        }

        using RetryDataSink ambiguous = new();
        using V3.ExrWriter faulted = V3.ExrWriter.OpenSink(
            ambiguous,
            new V3.WriterOptions(leaveOpen: true));
        faulted.AddPart(header);
        ambiguous.AmbiguousFailureNextWrite = true;
        Assert.AreEqual(V3.ExrResult.IO, faulted.Begin().Status);
        Assert.AreEqual(V3.WriterState.Faulted, faulted.State);
        Assert.AreEqual(V3.ExrResult.IO, faulted.Begin().Status);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 writer rejects DWA and lossy deep compression")]
    public void Case_V3Writer_RejectsUnsupportedCompressionPolicies()
    {
        foreach (V3.Compression compression in new[] { V3.Compression.DWAA, V3.Compression.DWAB })
        {
            using MemoryStream stream = new();
            using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
            using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
            writer.AddPart(SimpleHeader(compression));
            V3.WriterResult result = writer.Begin();
            Assert.AreEqual(V3.ExrResult.Unsupported, result.Status);
            Assert.IsInstanceOfType<NotSupportedException>(result.Error);
            Assert.AreEqual(0, stream.Length);
        }

        foreach (V3.Compression compression in new[]
        {
            V3.Compression.PIZ,
            V3.Compression.PXR24,
            V3.Compression.B44,
            V3.Compression.B44A,
        })
        {
            V3.Header header = new(
                V3.PartType.DeepScanline,
                new V3.Box2i(0, 0, 0, 0),
                new[] { new V3.Channel("Z", V3.PixelType.Float) },
                compression: compression);
            using MemoryStream stream = new();
            using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
            using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
            writer.AddPart(header);
            V3.WriterResult result = writer.Begin();
            Assert.AreEqual(V3.ExrResult.Unsupported, result.Status);
            Assert.IsInstanceOfType<NotSupportedException>(result.Error);
            Assert.AreEqual(0, stream.Length);
        }
    }

    private static V3.Header SimpleHeader(V3.Compression compression)
    {
        return new V3.Header(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 3, 1),
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            compression);
    }

    private static IReadOnlyList<V3.ChannelBuffer> CreateChannels(V3.Header header, V3.Box2i region)
    {
        List<V3.ChannelBuffer> result = new(header.Channels.Count);
        for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
        {
            V3.Channel channel = header.Channels[channelIndex];
            int elementSize = channel.PixelType == V3.PixelType.Half ? 2 : 4;
            int sampleCount = checked((int)CountSamples(region, channel.XSampling, channel.YSampling));
            byte[] data = new byte[checked(sampleCount * elementSize)];
            int offset = 0;
            for (long y = region.MinY; y <= region.MaxY; y++)
            {
                if (y % channel.YSampling != 0)
                {
                    continue;
                }

                for (long x = region.MinX; x <= region.MaxX; x++)
                {
                    if (x % channel.XSampling != 0)
                    {
                        continue;
                    }

                    int seed = checked((channel.Name[0] * 257) + ((int)x * 17) + ((int)y * 31));
                    if (channel.PixelType == V3.PixelType.Half)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            data.AsSpan(offset, 2),
                            unchecked((ushort)seed));
                    }
                    else if (channel.PixelType == V3.PixelType.UInt)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            data.AsSpan(offset, 4),
                            unchecked((uint)seed));
                    }
                    else
                    {
                        float value = seed * 0.125f;
                        BinaryPrimitives.WriteInt32LittleEndian(
                            data.AsSpan(offset, 4),
                            BitConverter.SingleToInt32Bits(value));
                    }

                    offset += elementSize;
                }
            }

            Assert.AreEqual(data.Length, offset);
            result.Add(new V3.ChannelBuffer(channel.Name, channel.PixelType, data));
        }

        return result;
    }

    private static byte[] CreateRepeatingFloatBytes(int sampleCount)
    {
        byte[] data = new byte[checked(sampleCount * sizeof(float))];
        for (int index = 0; index < sampleCount; index++)
        {
            float value = (index % 64) * 0.25f;
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(value));
        }

        return data;
    }

    private static byte[] CreateCumulativeDeepCounts(V3.Box2i region, ReadOnlySpan<int> counts)
    {
        int width = checked((int)region.Width);
        int height = checked((int)region.Height);
        Assert.AreEqual(checked(width * height), counts.Length);
        byte[] cumulativeCounts = new byte[checked(counts.Length * sizeof(int))];
        int source = 0;
        int target = 0;
        for (int row = 0; row < height; row++)
        {
            int cumulative = 0;
            for (int x = 0; x < width; x++)
            {
                cumulative = checked(cumulative + counts[source++]);
                BinaryPrimitives.WriteInt32LittleEndian(
                    cumulativeCounts.AsSpan(target, sizeof(int)),
                    cumulative);
                target += sizeof(int);
            }
        }

        Assert.AreEqual(cumulativeCounts.Length, target);
        return cumulativeCounts;
    }

    private static bool ContainsEntropyCompressedZstdBlock(ReadOnlySpan<byte> frame)
    {
        Assert.AreEqual(
            V3Codecs.ZstdFrameStatus.Success,
            V3Codecs.ZstdFrameHeaderParser.Parse(
                frame,
                out V3Codecs.ZstdFrameHeader header,
                out int headerBytes));
        Assert.AreEqual(header.HeaderSize, headerBytes);

        int offset = headerBytes;
        bool containsCompressedBlock = false;
        while (true)
        {
            Assert.IsTrue(frame.Length - offset >= 3, "The ZSTD frame ended before its block header.");
            uint blockHeader = (uint)(frame[offset]
                | (frame[offset + 1] << 8)
                | (frame[offset + 2] << 16));
            offset += 3;

            bool isLast = (blockHeader & 1U) != 0;
            int blockType = (int)((blockHeader >> 1) & 0x03U);
            int blockSize = checked((int)(blockHeader >> 3));
            Assert.AreNotEqual(3, blockType, "A reserved ZSTD block type was emitted.");
            containsCompressedBlock |= blockType == 2;

            int payloadSize = blockType == 1 ? 1 : blockSize;
            Assert.IsTrue(
                frame.Length - offset >= payloadSize,
                "The ZSTD frame ended inside a block payload.");
            offset += payloadSize;
            if (!isLast)
            {
                continue;
            }

            if (header.HasChecksum)
            {
                Assert.IsTrue(frame.Length - offset >= sizeof(uint));
                offset += sizeof(uint);
            }

            Assert.AreEqual(frame.Length, offset);
            return containsCompressedBlock;
        }
    }

    private static int[] CreateDeepCounts(V3.Box2i region, int levelX = 0, int levelY = 0)
    {
        int[] counts = new int[checked((int)(region.Width * region.Height))];
        int target = 0;
        for (int y = region.MinY; y <= region.MaxY; y++)
        {
            for (int x = region.MinX; x <= region.MaxX; x++)
            {
                long seed = checked(
                    ((long)x * 17L) +
                    ((long)y * 31L) +
                    ((long)levelX * 7L) +
                    ((long)levelY * 11L));
                int count = (int)(seed % 5L);
                counts[target++] = count < 0 ? count + 5 : count;
            }
        }

        Assert.AreEqual(counts.Length, target);
        return counts;
    }

    private static IReadOnlyList<V3.ChannelBuffer> CreateDeepChannels(
        V3.Header header,
        V3.Box2i region,
        ReadOnlySpan<int> counts,
        int levelX = 0,
        int levelY = 0)
    {
        Assert.AreEqual(checked((int)(region.Width * region.Height)), counts.Length);
        int totalSamples = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            totalSamples = checked(totalSamples + counts[i]);
        }

        List<V3.ChannelBuffer> result = new(header.Channels.Count);
        foreach (V3.Channel channel in header.Channels)
        {
            int elementSize = channel.PixelType == V3.PixelType.Half ? 2 : 4;
            byte[] data = new byte[checked(totalSamples * elementSize)];
            int sourcePixel = 0;
            int target = 0;
            for (int y = region.MinY; y <= region.MaxY; y++)
            {
                for (int x = region.MinX; x <= region.MaxX; x++)
                {
                    int count = counts[sourcePixel++];
                    for (int sample = 0; sample < count; sample++)
                    {
                        int seed = unchecked(
                            (channel.Name[0] * 257) +
                            (x * 17) +
                            (y * 31) +
                            (levelX * 43) +
                            (levelY * 47) +
                            (sample * 53));
                        if (channel.PixelType == V3.PixelType.Half)
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(
                                data.AsSpan(target, sizeof(ushort)),
                                unchecked((ushort)seed));
                        }
                        else if (channel.PixelType == V3.PixelType.UInt)
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                data.AsSpan(target, sizeof(uint)),
                                unchecked((uint)seed));
                        }
                        else
                        {
                            float value = seed * 0.125f;
                            BinaryPrimitives.WriteInt32LittleEndian(
                                data.AsSpan(target, sizeof(float)),
                                BitConverter.SingleToInt32Bits(value));
                        }

                        target += elementSize;
                    }
                }
            }

            Assert.AreEqual(data.Length, target);
            result.Add(new V3.ChannelBuffer(channel.Name, channel.PixelType, data));
        }

        return result;
    }

    private static void AssertDeepLevelMatches(V3.Header header, V3.DeepLevel level)
    {
        int[] expectedCounts = CreateDeepCounts(level.Region, level.LevelX, level.LevelY);
        CollectionAssert.AreEqual(expectedCounts, level.SampleCounts.ToArray());
        IReadOnlyList<V3.ChannelBuffer> expectedChannels = CreateDeepChannels(
            header,
            level.Region,
            expectedCounts,
            level.LevelX,
            level.LevelY);
        Assert.AreEqual(expectedChannels.Count, level.Channels.Count);
        for (int channelIndex = 0; channelIndex < expectedChannels.Count; channelIndex++)
        {
            Assert.AreEqual(expectedChannels[channelIndex].Name, level.Channels[channelIndex].Name);
            Assert.AreEqual(expectedChannels[channelIndex].PixelType, level.Channels[channelIndex].PixelType);
            CollectionAssert.AreEqual(
                expectedChannels[channelIndex].Data.ToArray(),
                level.Channels[channelIndex].Data.ToArray());
        }
    }

    private static void AssertMaximumSamples(byte[] encoded, int partIndex, int expected)
    {
        Assert.AreEqual(
            V3.ExrResult.Success,
            V3Format.ExrFormatParser.Parse(encoded, out V3Format.ParsedFile? parsed));
        Assert.IsNotNull(parsed);
        V3Format.ParsedAttribute attribute = parsed.Parts[partIndex].RawAttributes.Single(
            static candidate => candidate.Value.Name == "maxSamplesPerPixel");
        Assert.AreEqual("int", attribute.Value.TypeName);
        Assert.AreEqual(sizeof(int), attribute.Value.Data.Length);
        Assert.AreEqual(expected, BinaryPrimitives.ReadInt32LittleEndian(attribute.Value.Data));
    }

    private static ulong CountSamples(V3.Box2i region, int xSampling, int ySampling)
    {
        ulong count = 0;
        for (long y = region.MinY; y <= region.MaxY; y++)
        {
            if (y % ySampling != 0)
            {
                continue;
            }

            for (long x = region.MinX; x <= region.MaxX; x++)
            {
                if (x % xSampling == 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void AssertPartMatches(V3.Part part)
    {
        foreach (V3.PartLevel level in part.Levels)
        {
            IReadOnlyList<V3.ChannelBuffer> expected = CreateChannels(part.Header, level.Region);
            Assert.AreEqual(expected.Count, level.Channels.Count);
            for (int channelIndex = 0; channelIndex < expected.Count; channelIndex++)
            {
                Assert.AreEqual(expected[channelIndex].Name, level.Channels[channelIndex].Name);
                CollectionAssert.AreEqual(
                    expected[channelIndex].Data.ToArray(),
                    level.Channels[channelIndex].Data.ToArray(),
                    $"{part.Header.Name}:{level.LevelX},{level.LevelY}:{expected[channelIndex].Name}");
            }
        }
    }

    private static async Task AssertThrowsAsync<T>(Func<Task> action)
        where T : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected {typeof(T).Name}.");
        }
        catch (T)
        {
        }
    }

    private sealed class RetryDataSink : V3IO.ISeekableDataSink, V3IO.IAsyncSeekableDataSink
    {
        private readonly MemoryStream _stream = new();
        private bool _disposed;

        public int? FailNextWriteAfter { get; set; }

        public int? CancelNextWriteAfter { get; set; }

        public bool BlockNextWrite { get; set; }

        public bool AmbiguousFailureNextWrite { get; set; }

        public long Position => _stream.Position;

        public byte[] ToArray() => _stream.ToArray();

        public V3IO.DataTransferResult Write(ReadOnlySpan<byte> source)
        {
            if (_disposed)
            {
                return V3IO.DataTransferResult.Disposed(
                    0,
                    new ObjectDisposedException(nameof(RetryDataSink)));
            }

            if (AmbiguousFailureNextWrite)
            {
                AmbiguousFailureNextWrite = false;
                int count = Math.Min(1, source.Length);
                _stream.Write(source.Slice(0, count));
                return V3IO.DataTransferResult.IoError(
                    0,
                    new IOException("ambiguous"),
                    isByteCountExact: false);
            }

            if (BlockNextWrite)
            {
                BlockNextWrite = false;
                return V3IO.DataTransferResult.WouldBlock(new V3IO.DataRange(_stream.Position, 1));
            }

            if (CancelNextWriteAfter.HasValue)
            {
                int count = Math.Min(CancelNextWriteAfter.Value, source.Length);
                CancelNextWriteAfter = null;
                _stream.Write(source.Slice(0, count));
                return V3IO.DataTransferResult.Canceled(
                    count,
                    new OperationCanceledException());
            }

            if (FailNextWriteAfter.HasValue)
            {
                int count = Math.Min(FailNextWriteAfter.Value, source.Length);
                FailNextWriteAfter = null;
                _stream.Write(source.Slice(0, count));
                return V3IO.DataTransferResult.IoError(count, new IOException("retry"));
            }

            _stream.Write(source);
            return V3IO.DataTransferResult.Success(source.Length);
        }

        public V3IO.DataTransferResult Seek(long offset)
        {
            if (_disposed)
            {
                return V3IO.DataTransferResult.Disposed(
                    0,
                    new ObjectDisposedException(nameof(RetryDataSink)));
            }

            _stream.Position = offset;
            return V3IO.DataTransferResult.Success(0);
        }

        public V3IO.DataTransferResult Flush()
        {
            return _disposed
                ? V3IO.DataTransferResult.Disposed(
                    0,
                    new ObjectDisposedException(nameof(RetryDataSink)))
                : V3IO.DataTransferResult.Success(0);
        }

        public ValueTask<V3IO.DataTransferResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<V3IO.DataTransferResult>(
                    V3IO.DataTransferResult.Canceled(
                        0,
                        new OperationCanceledException(cancellationToken)));
            }

            return new ValueTask<V3IO.DataTransferResult>(Write(source.Span));
        }

        public ValueTask<V3IO.DataTransferResult> SeekAsync(
            long offset,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<V3IO.DataTransferResult>(
                    V3IO.DataTransferResult.Canceled(
                        0,
                        new OperationCanceledException(cancellationToken)));
            }

            return new ValueTask<V3IO.DataTransferResult>(Seek(offset));
        }

        public ValueTask<V3IO.DataTransferResult> FlushAsync(
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<V3IO.DataTransferResult>(
                    V3IO.DataTransferResult.Canceled(
                        0,
                        new OperationCanceledException(cancellationToken)));
            }

            return new ValueTask<V3IO.DataTransferResult>(Flush());
        }

        public void Dispose()
        {
            _disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
