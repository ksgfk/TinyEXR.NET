using BenchmarkDotNet.Attributes;
using TinyEXR.Codecs;

namespace TinyEXR.Benchmark;

[MemoryDiagnoser]
public class Htj2kDecodeBenchmarks
{
    [Params(128, 512)]
    public int Width { get; set; }

    [Params(32, 256)]
    public int Height { get; set; }

    private Header _header = null!;
    private Htj2kDecoder.Htj2kBufferPool _pool = null!;
    private byte[] _packed = null!, _output = null!;

    [GlobalSetup]
    public void Setup()
    {
        _header = new Header(PartType.Scanline, new Box2i(0, 0, Width - 1, Height - 1),
            new[] { new Channel("H", PixelType.Half) }, Height == 32 ? Compression.HTJ2K32 : Compression.HTJ2K256);
        _pool = new Htj2kDecoder.Htj2kBufferPool();
        byte[] raw = new byte[Width * Height * 2];
        _output = new byte[raw.Length];
        for (int i = 0; i < raw.Length; i++) raw[i] = (byte)((i / 64) % 4);
        if (Htj2kDecoder.Encode(_header, _header.DataWindow, raw, _pool, out _packed, out string? error) != Htj2kEncodeStatus.Success)
            throw new InvalidOperationException(error);
        if (Decode() != (int)Htj2kDecodeStatus.Success || !raw.AsSpan().SequenceEqual(_output))
            throw new InvalidOperationException("The HTJ2K benchmark fixture did not round trip.");
    }

    [Benchmark]
    public int Decode() => (int)Htj2kDecoder.Decode(_header, _header.DataWindow, _packed, _output, _pool, out _);

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();
}
