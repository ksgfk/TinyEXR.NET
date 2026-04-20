using System.Buffers.Binary;
using System.Text;

namespace TinyEXR.Test;

[TestClass]
public sealed class DeepTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] deepscanline.rle.exr|LoadDeep")]
    public void Case_Deep_RLE_scanline_decode()
    {
        byte[] encoded = CreateDeepScanlineFile(
            CompressionType.RLE,
            new[] { 1, 2, 2, 2 },
            new[] { 0.0f, 0.0f });

        string path = Path.Combine(Path.GetTempPath(), $"tinyexr-deep-rle-{Guid.NewGuid():N}.exr");
        try
        {
            File.WriteAllBytes(path, encoded);

            Assert.AreEqual(ResultCode.Success, Exr.LoadDeepEXR(path, out ExrHeader header, out ExrDeepImage image));

            Assert.IsTrue(header.IsDeep);
            Assert.AreEqual("deepscanline", header.PartType);
            Assert.AreEqual(CompressionType.RLE, header.Compression);

            Assert.AreEqual(4, image.Width);
            Assert.AreEqual(1, image.Height);
            Assert.AreEqual(1, image.Channels.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 2 }, image.OffsetTable[0]);
            CollectionAssert.AreEqual(new[] { 0.0f, 0.0f }, image.Channels[0].Rows[0]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static byte[] CreateDeepScanlineFile(CompressionType compression, int[] pixelOffsets, float[] samples)
    {
        const int width = 4;
        const int height = 1;

        byte[] offsetBytes = new byte[pixelOffsets.Length * sizeof(int)];
        for (int i = 0; i < pixelOffsets.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(offsetBytes.AsSpan(i * sizeof(int), sizeof(int)), pixelOffsets[i]);
        }

        byte[] sampleBytes = new byte[samples.Length * sizeof(float)];
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                sampleBytes.AsSpan(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(samples[i]));
        }

        byte[] packedOffsets = EncodeDeepRlePayload(offsetBytes);
        byte[] packedSamples = EncodeDeepRlePayload(sampleBytes);

        using MemoryStream header = new MemoryStream();
        WriteVersion(header, version: 2, flags: 0x8);
        WriteAttribute(header, "name", "string", EncodeCString("deep-part"));
        WriteAttribute(header, "type", "string", EncodeCString("deepscanline"));
        WriteAttribute(header, "channels", "chlist", EncodeChannels(new[] { ("Z", ExrPixelType.Float) }));
        WriteAttribute(header, "compression", "compression", new[] { (byte)compression });
        WriteAttribute(header, "dataWindow", "box2i", EncodeBox(0, 0, width - 1, height - 1));
        WriteAttribute(header, "displayWindow", "box2i", EncodeBox(0, 0, width - 1, height - 1));
        WriteAttribute(header, "lineOrder", "lineOrder", new[] { (byte)LineOrderType.IncreasingY });
        WriteAttribute(header, "pixelAspectRatio", "float", EncodeSingle(1.0f));
        WriteAttribute(header, "screenWindowCenter", "v2f", EncodeVector2(0.0f, 0.0f));
        WriteAttribute(header, "screenWindowWidth", "float", EncodeSingle(1.0f));
        WriteAttribute(header, "chunkCount", "int", EncodeInt32(1));
        header.WriteByte(0);

        int chunkOffset = checked((int)header.Length + sizeof(long));
        using MemoryStream output = new MemoryStream();
        output.Write(header.ToArray());
        output.Write(EncodeInt64(chunkOffset));
        output.Write(EncodeInt32(0));
        output.Write(EncodeInt64(packedOffsets.Length));
        output.Write(EncodeInt64(packedSamples.Length));
        output.Write(EncodeInt64(sampleBytes.Length));
        output.Write(packedOffsets);
        output.Write(packedSamples);
        return output.ToArray();
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
}
