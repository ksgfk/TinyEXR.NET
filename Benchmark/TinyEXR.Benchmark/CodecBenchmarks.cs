using BenchmarkDotNet.Attributes;
using TinyEXR.Codecs;

namespace TinyEXR.Benchmark;

[MemoryDiagnoser]
public class CodecBenchmarks
{
    [Params(Compression.RLE, Compression.PIZ)]
    public Compression Compression { get; set; }

    [Params(128, 512)]
    public int Width { get; set; }

    [Params(true, false)]
    public bool Sparse { get; set; }

    private CodecContext _context;
    private CodecWorkspace _workspace = null!;
    private byte[] _raw = null!, _packed = null!, _output = null!;

    [GlobalSetup]
    public void Setup()
    {
        _context = new CodecContext(new[] { new Channel("H", PixelType.Half) }, new Box2i(0, 0, Width - 1, 31));
        _workspace = new CodecWorkspace();
        _raw = new byte[_context.ByteCount];
        _output = new byte[_raw.Length];
        if (!Sparse) new Random(1234).NextBytes(_raw);
        else for (int i = 0; i < _raw.Length; i++) _raw[i] = (byte)((i / 64) % 4);
        // Measure codec work even when the container would choose raw fallback.
        _packed = Encode();
        if (Decode() != ExrResult.Success || !_raw.AsSpan().SequenceEqual(_output))
            throw new InvalidOperationException("The codec benchmark fixture did not round trip.");
    }

    [Benchmark]
    public byte[] Encode() => Compression == Compression.RLE
        ? RleCodec.Encode(_raw, _workspace) : PizCodec.Encode(_context, _raw, _workspace);

    [Benchmark]
    public ExrResult Decode() => Compression == Compression.RLE
        ? RleCodec.Decode(_packed, _output, _workspace) : PizCodec.Decode(_context, _packed, _output, _workspace);
}

