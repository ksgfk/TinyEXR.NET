using BenchmarkDotNet.Attributes;

namespace TinyEXR.Benchmark;

[MemoryDiagnoser]
public class ConvenienceRgbaBenchmarks
{
    private SampleBuffer _sample = null!;
    private RgbaPrepared _rgba = null!;

    [ParamsSource(nameof(SampleIds))]
    public string SampleId { get; set; } = string.Empty;

    public IEnumerable<string> SampleIds => new[] { "desk_scanline" };

    [GlobalSetup]
    public void Setup()
    {
        _sample = SampleRepository.Instance.GetBuffer(SampleId);
        _rgba = SampleRepository.Instance.GetRgba(SampleId);
    }

    [Benchmark]
    public int LoadEXRFromMemory()
    {
        BenchmarkGuard.Success(
            Exr.LoadEXRFromMemory(_sample.Bytes, out float[] rgba, out int width, out int height),
            $"LoadEXRFromMemory benchmark failed for {SampleId}");

        return rgba.Length + width + height;
    }

    [Benchmark]
    public int SaveEXRToMemory()
    {
        BenchmarkGuard.Success(
            Exr.SaveEXRToMemory(_rgba.Rgba, _rgba.Width, _rgba.Height, _rgba.Components, _rgba.AsFp16, out byte[] encoded),
            $"SaveEXRToMemory benchmark failed for {SampleId}");

        return encoded.Length;
    }
}

[MemoryDiagnoser]
public class SinglePartImageBenchmarks
{
    private SampleBuffer _sample = null!;
    private SinglePartPrepared _prepared = null!;

    [ParamsSource(nameof(SampleIds))]
    public string SampleId { get; set; } = string.Empty;

    public IEnumerable<string> SampleIds => BenchmarkSamples.SinglePartSampleIds;

    [GlobalSetup]
    public void Setup()
    {
        _sample = SampleRepository.Instance.GetBuffer(SampleId);
        _prepared = SampleRepository.Instance.GetSinglePart(SampleId);
    }

    [Benchmark]
    public int LoadEXRImageFromMemory()
    {
        BenchmarkGuard.Success(
            Exr.LoadEXRImageFromMemory(_sample.Bytes, _prepared.Header, out ExrImage image),
            $"LoadEXRImageFromMemory benchmark failed for {SampleId}");

        return image.Width + image.Height + image.Channels.Count + image.Levels.Count;
    }

    [Benchmark]
    public int SaveEXRImageToMemory()
    {
        BenchmarkGuard.Success(
            Exr.SaveEXRImageToMemory(_prepared.Image, _prepared.Header, out byte[] encoded),
            $"SaveEXRImageToMemory benchmark failed for {SampleId}");

        return encoded.Length;
    }
}

[MemoryDiagnoser]
public class MultipartBenchmarks
{
    private SampleBuffer _sample = null!;
    private MultipartPrepared _prepared = null!;

    [ParamsSource(nameof(SampleIds))]
    public string SampleId { get; set; } = string.Empty;

    public IEnumerable<string> SampleIds => BenchmarkSamples.MultipartSampleIds;

    [GlobalSetup]
    public void Setup()
    {
        _sample = SampleRepository.Instance.GetBuffer(SampleId);
        _prepared = SampleRepository.Instance.GetMultipart(SampleId);
    }

    [Benchmark]
    public int LoadEXRMultipartImageFromMemory()
    {
        BenchmarkGuard.Success(
            Exr.LoadEXRMultipartImageFromMemory(_sample.Bytes, _prepared.Headers, out ExrMultipartImage images),
            $"LoadEXRMultipartImageFromMemory benchmark failed for {SampleId}");

        return images.Images.Count;
    }

    [Benchmark]
    public int SaveEXRMultipartImageToMemory()
    {
        BenchmarkGuard.Success(
            Exr.SaveEXRMultipartImageToMemory(_prepared.Images, _prepared.Headers, out byte[] encoded),
            $"SaveEXRMultipartImageToMemory benchmark failed for {SampleId}");

        return encoded.Length;
    }
}

[MemoryDiagnoser]
public class DeepBenchmarks
{
    private SampleBuffer _sample = null!;

    [ParamsSource(nameof(SampleIds))]
    public string SampleId { get; set; } = string.Empty;

    public IEnumerable<string> SampleIds => BenchmarkSamples.DeepSampleIds;

    [GlobalSetup]
    public void Setup()
    {
        _sample = SampleRepository.Instance.GetBuffer(SampleId);
    }

    [Benchmark]
    public int LoadDeepImageFromMemory()
    {
        BenchmarkGuard.Success(
            Exr.TryReadDeepImage(_sample.Bytes, out ExrHeader header, out ExrDeepImage image),
            $"LoadDeepImageFromMemory benchmark failed for {SampleId}");

        return header.Channels.Count + image.Channels.Count + image.OffsetTable.Length + image.Width + image.Height;
    }
}
