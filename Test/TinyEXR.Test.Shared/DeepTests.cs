using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace TinyEXR.Test;

[TestClass]
public sealed class DeepTests
{
    public static IEnumerable<object[]> SupportedDeepCompressions()
    {
        yield return new object[] { CompressionType.None };
        yield return new object[] { CompressionType.RLE };
        yield return new object[] { CompressionType.ZIPS };
        yield return new object[] { CompressionType.ZIP };
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Deep scanline compression matrix")]
    [DynamicData(nameof(SupportedDeepCompressions))]
    public void Case_Deep_supported_scanline_compressions(CompressionType compression)
    {
        DeepFixture fixture = CreateDeepFixture(compression);
        byte[] encoded = CreateDeepScanlineFile(fixture);

        string path = Path.Combine(Path.GetTempPath(), $"tinyexr-deep-{compression.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.exr");
        try
        {
            File.WriteAllBytes(path, encoded);

            Assert.AreEqual(ResultCode.Success, Exr.LoadDeepEXR(path, out ExrHeader header, out ExrDeepImage image), compression.ToString());
            AssertDeepFixture(fixture, header, image, compression.ToString());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] deepscanline.piz.exr|LoadDeep|RejectUnsupportedCompression")]
    public void Case_Deep_PIZ_scanline_rejected()
    {
        DeepFixture fixture = CreateDeepFixture(CompressionType.PIZ);
        byte[] encoded = CreateDeepScanlineFile(fixture, payloadCompressionOverride: CompressionType.RLE);

        string path = Path.Combine(Path.GetTempPath(), $"tinyexr-deep-piz-{Guid.NewGuid():N}.exr");
        try
        {
            File.WriteAllBytes(path, encoded);

            Assert.AreEqual(ResultCode.UnsupportedFeature, Exr.LoadDeepEXR(path, out _, out _));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] deeptile.exr|LoadDeep|RejectUnsupportedFeature")]
    public void Case_DeepTile_single_part_rejected()
    {
        byte[] encoded = CreateDeepScanlineFile(CreateDeepFixture(CompressionType.ZIP));
        ExrBinaryMutationHelper.ReplaceHeaderCStringAttributeValue(encoded, headerIndex: 0, "type", "string", "deeptile\0pad");
        encoded[5] = (byte)(encoded[5] | 0x2);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out ExrVersion version));
        Assert.IsTrue(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.IsTrue(version.Tiled);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader header));
        Assert.IsTrue(header.IsDeep);
        StringAssert.StartsWith(header.PartType ?? string.Empty, "deeptile");

        string path = Path.Combine(Path.GetTempPath(), $"tinyexr-deeptile-{Guid.NewGuid():N}.exr");
        try
        {
            File.WriteAllBytes(path, encoded);

            Assert.AreEqual(ResultCode.UnsupportedFeature, Exr.LoadDeepEXR(path, out _, out _));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static DeepFixture CreateDeepFixture(CompressionType compression)
    {
        return compression == CompressionType.ZIP
            ? new DeepFixture(
                compression,
                new ExrBox2i(10, 20, 12, 20),
                new ExrBox2i(8, 18, 14, 23),
                new[] { "Z", "A" },
                new[]
                {
                    new DeepRow(
                        new[]
                        {
                            Channel(Samples(2.0f, 2.5f), Samples(3.0f), Samples(4.0f)),
                            Channel(Samples(0.4f, 0.5f), Samples(0.6f), Samples(0.7f)),
                        }),
                })
            : new DeepFixture(
                compression,
                new ExrBox2i(10, 20, 12, 21),
                new ExrBox2i(8, 18, 14, 23),
                new[] { "Z", "A" },
                new[]
                {
                    new DeepRow(
                        new[]
                        {
                            Channel(Samples(0.5f), Samples(), Samples(1.25f, 1.5f)),
                            Channel(Samples(0.1f), Samples(), Samples(0.2f, 0.3f)),
                        }),
                    new DeepRow(
                        new[]
                        {
                            Channel(Samples(2.0f, 2.5f), Samples(3.0f), Samples(4.0f)),
                            Channel(Samples(0.4f, 0.5f), Samples(0.6f), Samples(0.7f)),
                        }),
                });
    }

    private static void AssertDeepFixture(DeepFixture fixture, ExrHeader header, ExrDeepImage image, string message)
    {
        Assert.IsTrue(header.IsDeep, message);
        Assert.AreEqual("deepscanline", header.PartType, message);
        Assert.AreEqual(fixture.Compression, header.Compression, message);
        Assert.AreEqual(fixture.DataWindow.MinX, header.DataWindow.MinX, message);
        Assert.AreEqual(fixture.DataWindow.MinY, header.DataWindow.MinY, message);
        Assert.AreEqual(fixture.DataWindow.MaxX, header.DataWindow.MaxX, message);
        Assert.AreEqual(fixture.DataWindow.MaxY, header.DataWindow.MaxY, message);
        Assert.AreEqual(fixture.DisplayWindow.MinX, header.DisplayWindow.MinX, message);
        Assert.AreEqual(fixture.DisplayWindow.MinY, header.DisplayWindow.MinY, message);
        Assert.AreEqual(fixture.DisplayWindow.MaxX, header.DisplayWindow.MaxX, message);
        Assert.AreEqual(fixture.DisplayWindow.MaxY, header.DisplayWindow.MaxY, message);
        Assert.AreEqual(fixture.Width, image.Width, message);
        Assert.AreEqual(fixture.Height, image.Height, message);
        Assert.AreEqual(fixture.ChannelNames.Length, header.Channels.Count, message);
        Assert.AreEqual(fixture.ChannelNames.Length, image.Channels.Count, message);
        CollectionAssert.AreEqual(fixture.ChannelNames, header.Channels.Select(static channel => channel.Name).ToArray(), message);
        CollectionAssert.AreEqual(fixture.ChannelNames, image.Channels.Select(static channel => channel.Name).ToArray(), message);

        for (int rowIndex = 0; rowIndex < fixture.Rows.Length; rowIndex++)
        {
            DeepRow row = fixture.Rows[rowIndex];
            int[] expectedCounts = GetSampleCounts(row);
            int[] expectedOffsets = ToCumulativeOffsets(expectedCounts);

            CollectionAssert.AreEqual(expectedOffsets, image.OffsetTable[rowIndex], $"{message}|row[{rowIndex}]|offsets");

            for (int channelIndex = 0; channelIndex < fixture.ChannelNames.Length; channelIndex++)
            {
                float[] actualSamples = image.Channels[channelIndex].Rows[rowIndex];
                for (int pixelIndex = 0; pixelIndex < expectedCounts.Length; pixelIndex++)
                {
                    float[] expectedPixelSamples = row.ChannelPixels[channelIndex][pixelIndex];
                    float[] actualPixelSamples = SlicePixelSamples(actualSamples, expectedOffsets, pixelIndex);
                    CollectionAssert.AreEqual(
                        expectedPixelSamples,
                        actualPixelSamples,
                        $"{message}|row[{rowIndex}]|channel[{fixture.ChannelNames[channelIndex]}]|pixel[{pixelIndex}]");
                }
            }
        }
    }

    private static byte[] CreateDeepScanlineFile(DeepFixture fixture, CompressionType? payloadCompressionOverride = null)
    {
        if (fixture.Rows.Length != fixture.Height)
        {
            throw new InvalidOperationException("Deep fixture row count must match the data window height.");
        }

        CompressionType payloadCompression = payloadCompressionOverride ?? fixture.Compression;
        List<byte[]> chunks = new List<byte[]>(fixture.Height);
        for (int rowIndex = 0; rowIndex < fixture.Rows.Length; rowIndex++)
        {
            DeepRow row = fixture.Rows[rowIndex];
            int[] sampleCounts = GetSampleCounts(row);
            int[] pixelOffsets = ToCumulativeOffsets(sampleCounts);
            byte[] offsetBytes = EncodeInt32Array(pixelOffsets);
            byte[] sampleBytes = EncodeDeepSampleBytes(row);
            byte[] packedOffsets = EncodeDeepPayload(payloadCompression, offsetBytes);
            byte[] packedSamples = EncodeDeepPayload(payloadCompression, sampleBytes);

            using MemoryStream chunk = new MemoryStream();
            chunk.Write(EncodeInt32(fixture.DataWindow.MinY + rowIndex));
            chunk.Write(EncodeInt64(packedOffsets.Length));
            chunk.Write(EncodeInt64(packedSamples.Length));
            chunk.Write(EncodeInt64(sampleBytes.Length));
            chunk.Write(packedOffsets);
            chunk.Write(packedSamples);
            chunks.Add(chunk.ToArray());
        }

        using MemoryStream header = new MemoryStream();
        WriteVersion(header, version: 2, flags: 0x8);
        WriteAttribute(header, "name", "string", EncodeCString("deep-part"));
        WriteAttribute(header, "type", "string", EncodeCString("deepscanline"));
        WriteAttribute(header, "channels", "chlist", EncodeChannels(fixture.ChannelNames.Select(static name => (name, ExrPixelType.Float))));
        WriteAttribute(header, "compression", "compression", new[] { (byte)fixture.Compression });
        WriteAttribute(header, "dataWindow", "box2i", EncodeBox(fixture.DataWindow.MinX, fixture.DataWindow.MinY, fixture.DataWindow.MaxX, fixture.DataWindow.MaxY));
        WriteAttribute(header, "displayWindow", "box2i", EncodeBox(fixture.DisplayWindow.MinX, fixture.DisplayWindow.MinY, fixture.DisplayWindow.MaxX, fixture.DisplayWindow.MaxY));
        WriteAttribute(header, "lineOrder", "lineOrder", new[] { (byte)LineOrderType.IncreasingY });
        WriteAttribute(header, "pixelAspectRatio", "float", EncodeSingle(1.0f));
        WriteAttribute(header, "screenWindowCenter", "v2f", EncodeVector2(0.0f, 0.0f));
        WriteAttribute(header, "screenWindowWidth", "float", EncodeSingle(1.0f));
        WriteAttribute(header, "chunkCount", "int", EncodeInt32(chunks.Count));
        header.WriteByte(0);

        long[] offsets = new long[chunks.Count];
        long chunkOffset = header.Length + checked(chunks.Count * sizeof(long));
        for (int index = 0; index < chunks.Count; index++)
        {
            offsets[index] = chunkOffset;
            chunkOffset += chunks[index].Length;
        }

        using MemoryStream output = new MemoryStream();
        output.Write(header.ToArray());
        for (int index = 0; index < offsets.Length; index++)
        {
            output.Write(EncodeInt64(offsets[index]));
        }

        foreach (byte[] chunk in chunks)
        {
            output.Write(chunk);
        }

        return output.ToArray();
    }

    private static int[] GetSampleCounts(DeepRow row)
    {
        if (row.ChannelPixels.Length == 0)
        {
            return Array.Empty<int>();
        }

        int pixelCount = row.ChannelPixels[0].Length;
        int[] sampleCounts = new int[pixelCount];
        for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
        {
            sampleCounts[pixelIndex] = row.ChannelPixels[0][pixelIndex].Length;
        }

        for (int channelIndex = 1; channelIndex < row.ChannelPixels.Length; channelIndex++)
        {
            if (row.ChannelPixels[channelIndex].Length != pixelCount)
            {
                throw new InvalidOperationException("All deep channels must have the same pixel count.");
            }

            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                if (row.ChannelPixels[channelIndex][pixelIndex].Length != sampleCounts[pixelIndex])
                {
                    throw new InvalidOperationException("All deep channels must agree on per-pixel sample counts.");
                }
            }
        }

        return sampleCounts;
    }

    private static int[] ToCumulativeOffsets(int[] sampleCounts)
    {
        int[] offsets = new int[sampleCounts.Length];
        int total = 0;
        for (int index = 0; index < sampleCounts.Length; index++)
        {
            total += sampleCounts[index];
            offsets[index] = total;
        }

        return offsets;
    }

    private static byte[] EncodeDeepSampleBytes(DeepRow row)
    {
        int[] sampleCounts = GetSampleCounts(row);
        int totalSamples = sampleCounts.Sum();
        byte[] encoded = new byte[checked(row.ChannelPixels.Length * totalSamples * sizeof(float))];
        int offset = 0;

        for (int channelIndex = 0; channelIndex < row.ChannelPixels.Length; channelIndex++)
        {
            foreach (float[] pixelSamples in row.ChannelPixels[channelIndex])
            {
                foreach (float sample in pixelSamples)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        encoded.AsSpan(offset, sizeof(float)),
                        BitConverter.SingleToInt32Bits(sample));
                    offset += sizeof(float);
                }
            }
        }

        return encoded;
    }

    private static float[] SlicePixelSamples(float[] rowSamples, int[] offsets, int pixelIndex)
    {
        int start = pixelIndex == 0 ? 0 : offsets[pixelIndex - 1];
        int count = offsets[pixelIndex] - start;
        float[] pixelSamples = new float[count];
        Array.Copy(rowSamples, start, pixelSamples, 0, count);
        return pixelSamples;
    }

    private static byte[] EncodeDeepPayload(CompressionType compression, ReadOnlySpan<byte> raw)
    {
        switch (compression)
        {
            case CompressionType.None:
                return raw.ToArray();
            case CompressionType.RLE:
                return EncodeDeepRlePayload(raw);
            case CompressionType.ZIPS:
            case CompressionType.ZIP:
                return EncodeDeepZipPayload(raw);
            default:
                throw new InvalidOperationException($"Compression '{compression}' is not supported by the deep test encoder.");
        }
    }

    private static void WriteVersion(Stream stream, byte version, byte flags)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 20000630);
        buffer[4] = version;
        buffer[5] = flags;
        buffer[6] = 0;
        buffer[7] = 0;
        stream.Write(buffer);
    }

    private static void WriteAttribute(Stream stream, string name, string type, byte[] value)
    {
        stream.Write(Encoding.UTF8.GetBytes(name));
        stream.WriteByte(0);
        stream.Write(Encoding.UTF8.GetBytes(type));
        stream.WriteByte(0);
        stream.Write(EncodeInt32(value.Length));
        stream.Write(value);
    }

    private static byte[] EncodeChannels(IEnumerable<(string Name, ExrPixelType Type)> channels)
    {
        using MemoryStream stream = new MemoryStream();
        foreach ((string name, ExrPixelType type) in channels)
        {
            stream.Write(Encoding.UTF8.GetBytes(name));
            stream.WriteByte(0);
            stream.Write(EncodeInt32((int)type));
            stream.WriteByte(1);
            stream.WriteByte(0);
            stream.WriteByte(0);
            stream.WriteByte(0);
            stream.Write(EncodeInt32(1));
            stream.Write(EncodeInt32(1));
        }

        stream.WriteByte(0);
        return stream.ToArray();
    }

    private static byte[] EncodeBox(int minX, int minY, int maxX, int maxY)
    {
        byte[] data = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0, 4), minX);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4, 4), minY);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8, 4), maxX);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12, 4), maxY);
        return data;
    }

    private static byte[] EncodeVector2(float x, float y)
    {
        byte[] data = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0, 4), BitConverter.SingleToInt32Bits(x));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4, 4), BitConverter.SingleToInt32Bits(y));
        return data;
    }

    private static byte[] EncodeSingle(float value)
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(data, BitConverter.SingleToInt32Bits(value));
        return data;
    }

    private static byte[] EncodeCString(string value)
    {
        return Encoding.UTF8.GetBytes(value + "\0");
    }

    private static byte[] EncodeInt32(int value)
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(data, value);
        return data;
    }

    private static byte[] EncodeInt64(long value)
    {
        byte[] data = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(data, value);
        return data;
    }

    private static byte[] EncodeInt32Array(int[] values)
    {
        byte[] data = new byte[checked(values.Length * sizeof(int))];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(index * sizeof(int), sizeof(int)), values[index]);
        }

        return data;
    }

    private static byte[] EncodeDeepZipPayload(ReadOnlySpan<byte> raw)
    {
        byte[] predicted = ApplyExrPredictorAndReorder(raw);

        using MemoryStream output = new MemoryStream();
        using (ZLibStream zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(predicted);
        }

        byte[] payload = output.ToArray();
        return payload.Length >= raw.Length ? raw.ToArray() : payload;
    }

    private static byte[] EncodeDeepRlePayload(ReadOnlySpan<byte> raw)
    {
        byte[] predicted = ApplyExrPredictorAndReorder(raw);
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

                int literalLength = index - literalStart;
                if (runLength >= 3 || literalLength >= 127)
                {
                    break;
                }

                index += runLength;
            }

            int count = index - literalStart;
            encoded.Add(unchecked((byte)(-count)));
            for (int i = literalStart; i < index; i++)
            {
                encoded.Add(predicted[i]);
            }
        }

        return encoded.ToArray();
    }

    private static byte[] ApplyExrPredictorAndReorder(ReadOnlySpan<byte> raw)
    {
        byte[] tmp = new byte[raw.Length];
        int half = (raw.Length + 1) / 2;
        int targetA = 0;
        int targetB = half;
        for (int i = 0; i < raw.Length; i += 2)
        {
            tmp[targetA++] = raw[i];
            if (i + 1 < raw.Length)
            {
                tmp[targetB++] = raw[i + 1];
            }
        }

        int previous = tmp.Length == 0 ? 0 : tmp[0];
        for (int i = 1; i < tmp.Length; i++)
        {
            int current = tmp[i];
            tmp[i] = unchecked((byte)(current - previous + 384));
            previous = current;
        }

        return tmp;
    }

    private static float[] Samples(params float[] values) => values;

    private static float[][] Channel(params float[][] pixels) => pixels;

    private readonly record struct DeepFixture(
        CompressionType Compression,
        ExrBox2i DataWindow,
        ExrBox2i DisplayWindow,
        string[] ChannelNames,
        DeepRow[] Rows)
    {
        public int Width => DataWindow.Width;

        public int Height => DataWindow.Height;
    }

    private readonly record struct DeepRow(float[][][] ChannelPixels);
}
