using System.Buffers.Binary;
using System.Globalization;
using TinyEXR.Codecs;

namespace TinyEXR.Interop;

[TestClass]
[TestCategory("OpenEXR")]
public sealed class CodecInteropTests : InteropTestBase
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (Compression compression in new[] { Compression.None, Compression.RLE, Compression.ZIPS,
            Compression.ZIP, Compression.PIZ, Compression.PXR24, Compression.B44, Compression.B44A })
            foreach ((int width, int height) in new[] { (2, 2), (8, 6), (128, 32) })
                foreach (bool sampling in new[] { false, true })
                    foreach (int pattern in new[] { 0, 1, 2, 3, 4, 5 })
                        yield return new object[] { compression, width, height, sampling, pattern };
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task CanonicalBlockMatchesOfficialCodec(Compression compression, int width, int height, bool sampling, int pattern)
    {
        Channel[] channels =
        {
            new("F", PixelType.Float),
            new("H", PixelType.Half, sampling ? 2 : 1, sampling ? 2 : 1, perceptuallyLinear: pattern == 3 || pattern == 5),
            new("U", PixelType.UInt),
        };
        Box2i region = new(-4, -2, width - 5, height - 3);
        CodecContext context = new(channels, region);
        byte[] raw = CreateRaw(context, pattern);
        string label = $"{compression} {width}x{height} sampled={sampling} pattern={pattern}";
        CodecWorkspace workspace = new();
        Assert.AreEqual(ExrResult.Success, BlockCodec.Encode(compression, context, raw, workspace, out byte[] managed), label + " encode");
        byte[] native = await InvokeCodecAsync("encode", compression, context, raw, raw.Length);
        byte[] decodedNative = new byte[raw.Length];
        Assert.AreEqual(ExrResult.Success, BlockCodec.Decode(compression, context, native, decodedNative, workspace), label + " native->managed");
        byte[] nativeDecodedNative = await InvokeCodecAsync("decode", compression, context, native, raw.Length);
        Equal(nativeDecodedNative, decodedNative, label + " native-file decode");
        byte[] decodedManaged = new byte[raw.Length];
        Assert.AreEqual(ExrResult.Success, BlockCodec.Decode(compression, context, managed, decodedManaged, workspace), label + " managed decode");
        Equal(await InvokeCodecAsync("decode", compression, context, managed, raw.Length), decodedManaged, label + " managed->native");
        bool lossless = compression != Compression.PXR24 && compression != Compression.B44 && compression != Compression.B44A;
        if (lossless)
        {
            Equal(raw, decodedNative, label);
            Equal(raw, decodedManaged, label);
        }
        else if (native.Length < raw.Length && managed.Length < raw.Length)
            Equal(decodedNative, decodedManaged, label + " quantization");
    }

    private async Task<byte[]> InvokeCodecAsync(string mode, Compression compression, CodecContext context, byte[] input, int rawSize)
    {
        string source = Path.Combine(Work, "input.bin"), target = Path.Combine(Work, "output.bin");
        await File.WriteAllBytesAsync(source, input);
        List<string> arguments = new() { mode, source, target };
        foreach (long value in new long[] { (int)compression, context.Region.MinX, context.Region.MinY,
            context.Region.Width, context.Region.Height, rawSize })
            arguments.Add(value.ToString(CultureInfo.InvariantCulture));
        foreach (Channel channel in context.Channels)
            arguments.Add(FormattableString.Invariant($"{(int)channel.PixelType}:{channel.XSampling}:{channel.YSampling}:{(channel.PerceptuallyLinear ? 1 : 0)}"));
        await OracleAsync("codec_oracle", arguments.ToArray());
        return await File.ReadAllBytesAsync(target);
    }

    private static byte[] CreateRaw(CodecContext context, int pattern)
    {
        byte[] raw = new byte[context.ByteCount];
        Random random = new(1234);
        int position = 0;
        for (long y = context.Region.MinY; y <= context.Region.MaxY; y++)
            for (int c = 0; c < context.Channels.Count; c++)
            {
                Channel channel = context.Channels[c];
                if (y % channel.YSampling != 0)
                    continue;
                for (int x = 0; x < context.Width(c); x++)
                {
                    uint bits = pattern switch
                    {
                        0 => 0,
                        1 => unchecked((uint)random.NextInt64(0, 1L << 32)),
                        _ => (uint)((x + y + 4) % 8)
                    };
                    if (channel.PixelType == PixelType.Half)
                    {
                        ushort half = pattern == 5 ? (ushort)0 : pattern == 4 ? new ushort[] { 0, 0x8000, 1, 0x3ff, 0x400, 0x7bff, 0xfbff, 0x7c00, 0xfc00, 0x7e00, 0xfe00, 0x7c01 }[x % 12] : pattern >= 2 ? (ushort)(0x3000 + bits * 1024) : (ushort)bits;
                        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(position), half);
                        position += 2;
                    }
                    else
                    {
                        if (channel.PixelType == PixelType.Float && pattern == 4)
                            bits = new uint[] { 0, 0x80000000, 1, 0x007fffff, 0x00800000, 0x7f7fffff, 0xff7fffff, 0x7f800000, 0xff800000, 0x7fc00000, 0xffc00000, 0x7f800001 }[x % 12];
                        else if (channel.PixelType == PixelType.Float && pattern >= 2)
                            bits = unchecked((uint)BitConverter.SingleToInt32Bits(bits / 7.0f));
                        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(position), bits);
                        position += 4;
                    }
                }
            }
        return raw;
    }
}
