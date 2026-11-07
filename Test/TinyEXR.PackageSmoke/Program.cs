using TinyEXR;
using TinyEXR.IO;

Part part = Spectral.CreateEmissivePart(1, 1, new[] { 550.0f }, new[] { 2.5f }, "radiance");
Image image = new(new[] { part });
WriterResult<byte[]> saved = ExrFile.SaveToMemory(image, Compression.ZIP);
if (!saved.IsSuccess || saved.Value is null) throw new Exception("encode failed");
ReaderResult<Image> loaded = ExrFile.LoadFromMemory(saved.Value);
if (!loaded.IsSuccess || loaded.Value is null || loaded.Value.Parts.Count != 1) throw new Exception("decode failed");
using ExrReader reader = ExrReader.OpenSource(new MemoryDataSource(saved.Value));
if (!reader.ParseHeader().IsSuccess) throw new Exception("I/O reader failed");
ReaderResult<SpectralImage> spectral = SpectralImage.LoadFromMemory(saved.Value);
if (!spectral.IsSuccess || spectral.Value?.SpectrumType != SpectralType.Emissive) throw new Exception("spectral decode failed");
if (Exr.EXRSetSpectralAttributes(part.Header, SpectralType.Emissive, "radiance", out Header? updated) != ExrResult.Success ||
    updated == null || Exr.EXRGetSpectrumType(updated) != SpectralType.Emissive) throw new Exception("convenience spectral failed");
foreach (Compression compression in Enum.GetValues<Compression>().Where(c => c != Compression.DWAA && c != Compression.DWAB))
{
    Part pixels = PartConversion.FromInterleavedFloat(Enumerable.Repeat(0.5f, 128 * 32 * 3).ToArray(), 128, 32, 3, PixelType.Half, compression);
    if (Exr.SaveEXRImageToMemory(pixels, out byte[] bytes) != ExrResult.Success) throw new Exception($"{compression} encode failed");
    if (Exr.LoadEXRImageFromMemory(bytes, out Part? decoded) != ExrResult.Success || decoded == null) throw new Exception($"{compression} decode failed");
    foreach (ChannelBuffer channel in pixels.Levels[0].Channels)
        if (!channel.Data.SequenceEqual(decoded.Levels[0].GetChannel(channel.Name).Data)) throw new Exception($"{compression} sample mismatch");
    using MemoryStream stream = new(new byte[13].Concat(bytes).ToArray());
    stream.Position = 13;
    if (Exr.LoadEXRFromStream(stream, out float[] rgba, out int width, out int height) != ExrResult.Success || stream.Position != 13 ||
        width != 128 || height != 32 || rgba[0] != 0.5f || rgba[3] != 1) throw new Exception("stream convenience failed");
}
if (ExrFile.ApiVersionMajor != 3) throw new Exception("upstream API version changed");
Console.WriteLine("TinyEXR.NET package smoke passed: V3 models, codecs, convenience API, IO and spectral");
