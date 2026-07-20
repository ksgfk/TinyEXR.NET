using System.Buffers.Binary;
using System.Reflection;
using V3 = TinyEXR.V3;
using V3Codecs = TinyEXR.V3.Codecs;
using V3IO = TinyEXR.V3.IO;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3Htj2kTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 genuinely encodes HTJ2K32 HALF RGB scanline blocks")]
    public void Case_V3Htj2k_EncodesHalfRgbScanlineBlocks()
    {
        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 127, 31),
            new[]
            {
                new V3.Channel("A", V3.PixelType.Half),
                new V3.Channel("B", V3.PixelType.Half),
                new V3.Channel("G", V3.PixelType.Half),
                new V3.Channel("R", V3.PixelType.Half),
            },
            compression: V3.Compression.HTJ2K32);
        Dictionary<string, byte[]> expected = new()
        {
            ["A"] = CreateHalfSamples(128 * 32, 0x3c00),
            ["B"] = CreateHalfSamples(128 * 32, 0x3400),
            ["G"] = CreateHalfSamples(128 * 32, 0x3800),
            ["R"] = CreateHalfSamples(128 * 32, 0x3a00),
        };

        byte[] encoded = EncodeSingleBlock(header, expected, tiled: false);
        AssertGenuinePayload(encoded, expected.Values.Sum(value => value.Length));
        AssertEncodedChannels(encoded, expected);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 genuinely encodes HTJ2K256 mixed 32-bit tiled blocks")]
    public void Case_V3Htj2k_EncodesMixed32BitTiledBlocks()
    {
        V3.Header header = new(
            V3.PartType.Tiled,
            new V3.Box2i(0, 0, 63, 63),
            new[]
            {
                new V3.Channel("ID", V3.PixelType.UInt),
                new V3.Channel("Z", V3.PixelType.Float),
            },
            compression: V3.Compression.HTJ2K256,
            tiles: new V3.TileDescription(64, 64));
        Dictionary<string, byte[]> expected = new()
        {
            ["ID"] = CreateUIntSamples(64 * 64),
            ["Z"] = CreateFloatSamples(64 * 64),
        };

        byte[] encoded = EncodeSingleBlock(header, expected, tiled: true);
        AssertGenuinePayload(encoded, expected.Values.Sum(value => value.Length));
        AssertEncodedChannels(encoded, expected);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 encodes and decodes all-zero HTJ2K blocks without codeblocks")]
    public void Case_V3Htj2k_EncodesAllZeroBlocks()
    {
        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 127, 31),
            new[] { new V3.Channel("Y", V3.PixelType.Half) },
            compression: V3.Compression.HTJ2K32);
        Dictionary<string, byte[]> expected = new()
        {
            ["Y"] = new byte[128 * 32 * sizeof(ushort)],
        };

        byte[] encoded = EncodeSingleBlock(header, expected, tiled: false);
        AssertGenuinePayload(encoded, expected["Y"].Length);
        AssertEncodedChannels(encoded, expected);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 genuinely encodes sampled HTJ2K scanline components")]
    public void Case_V3Htj2k_EncodesSampledScanlineComponents()
    {
        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 127, 31),
            new[]
            {
                new V3.Channel("A", V3.PixelType.Half, 2, 1),
                new V3.Channel("B", V3.PixelType.Half),
            },
            compression: V3.Compression.HTJ2K32);
        Dictionary<string, byte[]> expected = new()
        {
            ["A"] = CreateHalfSamples(64 * 32, 0x3c00),
            ["B"] = CreateHalfSamples(128 * 32, 0x3800),
        };

        byte[] encoded = EncodeSingleBlock(header, expected, tiled: false);
        AssertGenuinePayload(encoded, expected.Values.Sum(value => value.Length));
        AssertEncodedChannels(encoded, expected);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 HTJ2K codec covers upstream RCT and full-width UINT profiles")]
    public void Case_V3Htj2k_CodecCoversUpstreamEdgeProfiles()
    {
        V3.Header rgbFloat = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 4, 3),
            new[]
            {
                new V3.Channel("B", V3.PixelType.Float),
                new V3.Channel("G", V3.PixelType.Float),
                new V3.Channel("R", V3.PixelType.Float),
            },
            compression: V3.Compression.HTJ2K32);
        AssertCodecRoundTrip(
            rgbFloat,
            new Dictionary<string, byte[]>
            {
                ["B"] = CreateProfileFloatSamples(20, index => (20 - index) * 0.07f),
                ["G"] = CreateProfileFloatSamples(20, index => ((index * 7) % 13) * 0.3f),
                ["R"] = CreateProfileFloatSamples(20, index => index * 0.1f - 0.5f),
            });

        V3.Header fullWidthUInt = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 2, 1),
            new[] { new V3.Channel("Z", V3.PixelType.UInt) },
            compression: V3.Compression.HTJ2K32);
        uint[] values = { 0, 1, 0x7fffffff, 0x80000000, 0xffffffff, 0xdeadbeef };
        byte[] uintBytes = new byte[values.Length * sizeof(uint)];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                uintBytes.AsSpan(i * sizeof(uint), sizeof(uint)),
                values[i]);
        }

        AssertCodecRoundTrip(
            fullWidthUInt,
            new Dictionary<string, byte[]> { ["Z"] = uintBytes });
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 decodes genuine HTJ2K32 scanline and HTJ2K256 tiled fixtures")]
    public void Case_V3Htj2k_DecodesScanlineAndTiledFixtures()
    {
        AssertDecoded(
            TestPaths.Regression("2by2_htj2k32.exr"),
            new Dictionary<string, byte[]>
            {
                ["A"] = new byte[] { 0x00, 0x3c, 0x00, 0x3c, 0x04, 0x34, 0x00, 0x3c },
                ["B"] = new byte[] { 0x00, 0x3c, 0x00, 0x00, 0x00, 0x3c, 0x00, 0x00 },
                ["G"] = new byte[] { 0x00, 0x3c, 0x00, 0x00, 0x27, 0x37, 0x00, 0x00 },
                ["R"] = new byte[] { 0x00, 0x3c, 0x00, 0x3c, 0x00, 0x00, 0x00, 0x00 },
            });
        AssertDecoded(
            TestPaths.Regression("tiled_htj2k256.exr"),
            new Dictionary<string, byte[]>
            {
                ["A"] = new byte[] { 0x00, 0x3c },
            });
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 HTJ2K tables retain all 16-bit VLC fields")]
    public void Case_V3Htj2k_VlcTablesMatchTinyExrV3()
    {
        Type decoder = typeof(V3.ExrReader).Assembly.GetType(
            "TinyEXR.V3.Codecs.Htj2kDecoder",
            throwOnError: true)!;
        object tables = decoder.GetField("Tables", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

        Assert.AreEqual(0x19b976b25ad1bfebUL, HashTable(tables, "Vlc0"));
        Assert.AreEqual(0xf2d14bc357c48ec2UL, HashTable(tables, "Vlc1"));
        Assert.AreEqual(0x92918a4fef1d858bUL, HashTable(tables, "Uvlc0"));
        Assert.AreEqual(0x66817cbf52cf855bUL, HashTable(tables, "Uvlc1"));
        Assert.AreEqual(0x8db3bb23ebeb7783UL, HashTable(tables, "UvlcBias"));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 rejects a malformed HTJ2K payload")]
    public void Case_V3Htj2k_RejectsMalformedPayload()
    {
        byte[] source = File.ReadAllBytes(TestPaths.Regression("2by2_htj2k32.exr"));
        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(source))
        {
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            V3.BlockInfo info = reader.GetBlockInfo(0, 0);
            int payloadOffset = checked((int)info.FileOffset + info.ChunkHeaderByteCount);
            source[payloadOffset] ^= 0xff;
        }

        using V3.ExrReader corrupted = V3.ExrReader.OpenMemory(source);
        Assert.AreEqual(V3.ExrResult.Success, corrupted.ParseHeader().Status);
        V3.BlockInfo block = corrupted.GetBlockInfo(0, 0);
        byte[] destination = new byte[checked((int)block.UncompressedByteCount!.Value)];
        V3.ReaderResult result = corrupted.DecodeBlock(0, 0, destination);
        Assert.AreEqual(V3.ExrResult.Corrupt, result.Status);
    }

    private static void AssertDecoded(string path, IReadOnlyDictionary<string, byte[]> expectedChannels)
    {
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(File.ReadAllBytes(path));
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status, path);

        V3.ReaderResult<V3.Part> result = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString() ?? path);
        Assert.IsNotNull(result.Value, path);
        V3.PartLevel level = result.Value.GetLevel(0, 0);
        foreach (KeyValuePair<string, byte[]> expected in expectedChannels)
        {
            CollectionAssert.AreEqual(
                expected.Value,
                level.GetChannel(expected.Key).Data.ToArray(),
                $"{path}: {expected.Key}");
        }
    }

    private static byte[] EncodeSingleBlock(
        V3.Header header,
        IReadOnlyDictionary<string, byte[]> channels,
        bool tiled)
    {
        using MemoryStream stream = new();
        using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        writer.AddPart(header);
        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        Assert.AreEqual(1, writer.GetNumBlocks(0));
        V3.BlockInfo block = writer.GetBlockInfo(0, 0);
        V3.ChannelBuffer[] buffers = header.Channels
            .Select(channel => new V3.ChannelBuffer(
                channel.Name,
                channel.PixelType,
                channels[channel.Name]))
            .ToArray();
        V3.WriterResult result = tiled
            ? writer.WriteTile(0, block.TileX, block.TileY, block.LevelX, block.LevelY, buffers)
            : writer.WriteScanlineBlock(0, block.Region.MinY, buffers);
        Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        return stream.ToArray();
    }

    private static void AssertGenuinePayload(byte[] encoded, int rawLength)
    {
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.BlockInfo block = reader.GetBlockInfo(0, 0);
        int chunkOffset = checked((int)block.FileOffset);
        int packedSize = BinaryPrimitives.ReadInt32LittleEndian(
            encoded.AsSpan(chunkOffset + block.ChunkHeaderByteCount - sizeof(int), sizeof(int)));
        Assert.IsTrue(packedSize < rawLength, $"HTJ2K stored {packedSize} raw bytes instead of a compressed payload.");
        ReadOnlySpan<byte> payload = encoded.AsSpan(
            chunkOffset + block.ChunkHeaderByteCount,
            packedSize);
        Assert.IsTrue(payload.Length >= 12);
        Assert.AreEqual((byte)'H', payload[0]);
        Assert.AreEqual((byte)'T', payload[1]);
        int wrapperPayloadSize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(2, 4)));
        int codestreamOffset = checked(6 + wrapperPayloadSize);
        Assert.IsTrue(codestreamOffset <= payload.Length - 2);
        Assert.AreEqual(0xff, payload[codestreamOffset]);
        Assert.AreEqual(0x4f, payload[codestreamOffset + 1]);
    }

    private static void AssertEncodedChannels(
        byte[] encoded,
        IReadOnlyDictionary<string, byte[]> expected)
    {
        using V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
        V3.ReaderResult<V3.Part> result = reader.ReadPart(0);
        Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
        Assert.IsNotNull(result.Value);
        V3.PartLevel level = result.Value.GetLevel(0, 0);
        foreach (KeyValuePair<string, byte[]> channel in expected)
        {
            CollectionAssert.AreEqual(
                channel.Value,
                level.GetChannel(channel.Key).Data.ToArray(),
                channel.Key);
        }
    }

    private static void AssertCodecRoundTrip(
        V3.Header header,
        IReadOnlyDictionary<string, byte[]> channels)
    {
        byte[] raw = GatherCanonicalBlock(header, header.DataWindow, channels);
        Assert.AreEqual(
            V3Codecs.Htj2kEncodeStatus.Success,
            V3Codecs.Htj2kDecoder.Encode(
                header,
                header.DataWindow,
                raw,
                out byte[] payload,
                out string? encodeError),
            encodeError);
        Assert.IsTrue(payload.Length > 2);
        Assert.AreEqual((byte)'H', payload[0]);
        Assert.AreEqual((byte)'T', payload[1]);

        byte[] decoded = new byte[raw.Length];
        Assert.AreEqual(
            V3Codecs.Htj2kDecodeStatus.Success,
            V3Codecs.Htj2kDecoder.Decode(
                header,
                header.DataWindow,
                payload,
                decoded,
                out string? decodeError),
            decodeError);
        CollectionAssert.AreEqual(raw, decoded);
    }

    private static byte[] GatherCanonicalBlock(
        V3.Header header,
        V3.Box2i region,
        IReadOnlyDictionary<string, byte[]> channels)
    {
        byte[] result = new byte[channels.Values.Sum(value => value.Length)];
        int[] sourceOffsets = new int[header.Channels.Count];
        int destinationOffset = 0;
        for (int y = region.MinY; y <= region.MaxY; y++)
        {
            for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
            {
                V3.Channel channel = header.Channels[channelIndex];
                if (y % channel.YSampling != 0)
                {
                    continue;
                }

                int samples = 0;
                for (int x = region.MinX; x <= region.MaxX; x++)
                {
                    if (x % channel.XSampling == 0)
                    {
                        samples++;
                    }
                }

                int elementSize = channel.PixelType == V3.PixelType.Half ? 2 : 4;
                int rowBytes = checked(samples * elementSize);
                byte[] source = channels[channel.Name];
                source.AsSpan(sourceOffsets[channelIndex], rowBytes)
                    .CopyTo(result.AsSpan(destinationOffset));
                sourceOffsets[channelIndex] += rowBytes;
                destinationOffset += rowBytes;
            }
        }

        Assert.AreEqual(result.Length, destinationOffset);
        return result;
    }

    private static byte[] CreateHalfSamples(int count, ushort baseValue)
    {
        byte[] result = new byte[checked(count * sizeof(ushort))];
        for (int i = 0; i < count; i++)
        {
            ushort value = checked((ushort)(baseValue + ((i / 16) & 3)));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2, 2), value);
        }

        return result;
    }

    private static byte[] CreateUIntSamples(int count)
    {
        byte[] result = new byte[checked(count * sizeof(uint))];
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(i * sizeof(uint), sizeof(uint)),
                checked((uint)(42 + ((i / 32) & 1))));
        }

        return result;
    }

    private static byte[] CreateFloatSamples(int count)
    {
        byte[] result = new byte[checked(count * sizeof(float))];
        for (int i = 0; i < count; i++)
        {
            float value = ((i / 64) & 1) == 0 ? 1.5f : -2.25f;
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(value));
        }

        return result;
    }

    private static byte[] CreateProfileFloatSamples(int count, Func<int, float> valueFactory)
    {
        byte[] result = new byte[checked(count * sizeof(float))];
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                result.AsSpan(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(valueFactory(i)));
        }

        return result;
    }

    private static ulong HashTable(object tables, string propertyName)
    {
        Array values = (Array)tables.GetType().GetProperty(propertyName)!.GetValue(tables)!;
        ulong hash = 1469598103934665603UL;
        foreach (object item in values)
        {
            uint value = Convert.ToUInt32(item);
            int byteCount = item is ushort ? 2 : 1;
            for (int byteIndex = 0; byteIndex < byteCount; byteIndex++)
            {
                hash ^= (byte)(value >> (8 * byteIndex));
                hash *= 1099511628211UL;
            }
        }

        return hash;
    }
}
