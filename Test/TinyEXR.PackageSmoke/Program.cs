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
ExrHeader legacy = new();
legacy.Channels.Add(new ExrChannel("S0.550,000000nm", ExrPixelType.Float));
if (Exr.EXRSetSpectralAttributes(legacy, SpectrumType.Emissive, "radiance") != ResultCode.Success || Exr.EXRGetSpectrumType(legacy) != SpectrumType.Emissive) throw new Exception("legacy enum changed");
if (ExrFile.ApiVersionMajor != 3) throw new Exception("upstream API version changed");
Console.WriteLine("TinyEXR.NET 1.2.0 package smoke passed: root API, IO, spectral, legacy facade");
