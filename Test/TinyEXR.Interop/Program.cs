using System.Buffers.Binary;
using System.Diagnostics;
using TinyEXR;
using TinyEXR.Codecs;

if (args.Length < 1 || args.Length > 2) throw new ArgumentException("Provide the native reference build directory.");
string nativeDirectory = Path.GetFullPath(args[0]);
string oracle = Path.Combine(nativeDirectory, OperatingSystem.IsWindows() ? "codec_oracle.exe" : "codec_oracle");
if (!File.Exists(oracle)) throw new FileNotFoundException("Build Test/NativeReference first.", oracle);
string work = Path.Combine(Path.GetTempPath(), "TinyEXR-interop-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    int cases = 0;
    if (args.Length == 1)
    foreach (Compression compression in new[] { Compression.None, Compression.RLE, Compression.ZIPS,
        Compression.ZIP, Compression.PIZ, Compression.PXR24, Compression.B44, Compression.B44A })
    {
        foreach ((int width, int height) in new[] { (1, 1), (7, 5), (128, 32) })
            foreach (bool sampling in new[] { false, true })
                foreach (int pattern in new[] { 0, 1, 2, 3, 4, 5 })
                {
                    Channel[] channels = {
                new("F", PixelType.Float),
                new("H", PixelType.Half, sampling ? 2 : 1, sampling ? 2 : 1, perceptuallyLinear: pattern == 3 || pattern == 5),
                new("U", PixelType.UInt),
            };
                    Box2i region = new(-4, -2, width - 5, height - 3);
                    CodecContext context = new(channels, region);
                    byte[] raw = CreateRaw(context, pattern);
                    string label = $"{compression} {width}x{height} sampled={sampling} pattern={pattern}";
                    var workspace = new CodecWorkspace();
                    Require(BlockCodec.Encode(compression, context, raw, workspace, out byte[] managed) == ExrResult.Success, label + " encode");
                    byte[] native = RunOracle("encode", compression, context, raw, raw.Length);
                    byte[] decodedNative = new byte[raw.Length];
                    Require(BlockCodec.Decode(compression, context, native, decodedNative, workspace) == ExrResult.Success, label + " native->managed");
                    byte[] nativeDecodedNative = RunOracle("decode", compression, context, native, raw.Length);
                    Equal(nativeDecodedNative, decodedNative, label + " native-file decode");
                    byte[] decodedManaged = new byte[raw.Length];
                    Require(BlockCodec.Decode(compression, context, managed, decodedManaged, workspace) == ExrResult.Success, label + " managed decode");
                    Equal(RunOracle("decode", compression, context, managed, raw.Length), decodedManaged, label + " managed->native");
                    bool lossless = compression != Compression.PXR24 && compression != Compression.B44 && compression != Compression.B44A;
                    if (lossless)
                    {
                        Equal(raw, decodedNative, label);
                        Equal(raw, decodedManaged, label);
                    }
                    else if (native.Length < raw.Length && managed.Length < raw.Length)
                        Equal(decodedNative, decodedManaged, label + " quantization");
                    cases++;
                }
        Console.WriteLine($"{compression}: native/managed cross-check passed");
    }
    if (cases > 0)
        Console.WriteLine($"Passed {cases} codec cases against the pinned native V3.");
    FileChecks.Run(nativeDirectory, work);
}
finally
{
    Directory.Delete(work, recursive: true);
}

byte[] RunOracle(string mode, Compression compression, CodecContext context, byte[] input, int rawSize)
{
    string source = Path.Combine(work, "input.bin"), target = Path.Combine(work, "output.bin");
    File.WriteAllBytes(source, input);
    ProcessStartInfo start = new(oracle)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach (string argument in new[] { mode, source, target, ((int)compression).ToString(), context.Region.MinX.ToString(), context.Region.MinY.ToString(),
        context.Region.Width.ToString(), context.Region.Height.ToString(), rawSize.ToString() })
        start.ArgumentList.Add(argument);
    foreach (Channel channel in context.Channels)
        start.ArgumentList.Add($"{(int)channel.PixelType}:{channel.XSampling}:{channel.YSampling}:{(channel.PerceptuallyLinear ? 1 : 0)}");
    using Process process = Process.Start(start)!;
    string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new Exception($"Native {mode} {compression} failed ({process.ExitCode}): {output} {error}");
    return File.ReadAllBytes(target);
}

static byte[] CreateRaw(CodecContext context, int pattern)
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

static void Equal(byte[] expected, byte[] actual, string label)
{
    if (expected.AsSpan().SequenceEqual(actual))
        return;
    int i = 0;
    while (i < Math.Min(expected.Length, actual.Length) && expected[i] == actual[i])
        i++;
    throw new Exception($"{label}: byte mismatch at {i}, lengths {expected.Length}/{actual.Length}");
}

static void Require(bool condition, string label)
{
    if (!condition)
        throw new Exception(label);
}
