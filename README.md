# TinyEXR.NET

[![Test](https://github.com/ksgfk/TinyEXR.NET/actions/workflows/test.yml/badge.svg)](https://github.com/ksgfk/TinyEXR.NET/actions/workflows/test.yml)

`TinyEXR.NET` is a pure C# port library of [tinyexr](https://github.com/syoyo/tinyexr)

The target frameworks are `net8.0`, `netstandard2.1`

## Download

`TinyEXR.NET` can be found on NuGet [![NuGet](https://img.shields.io/nuget/v/TinyEXR.NET)](https://www.nuget.org/packages/TinyEXR.NET) (← click it !)

## Dependencies

Both targets use [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) for TinyEXR v3-compatible ZSTD encoding. The decoder remains an independent managed implementation in this repository.

The `netstandard2.1` target additionally depends on [SharpZipLib](https://github.com/icsharpcode/SharpZipLib).

## Features

- [x] full NativeAOT support!
- [x] Single-part EXR read/write for scanline images.
- [x] Single-part EXR read/write for tiled images, including one-level tiles and multi-resolution mipmap/ripmap layouts.
- [x] Multipart image EXR parse/load/save for flat image parts, including single-entry multipart containers.
- [x] Deep single-part scanline and one-level tiled EXR load through `LoadDeepEXR`.
- [x] Regular image compression support for `NONE`, `RLE`, `ZIP`, `ZIPS`, `PIZ`, `PXR24`, `B44`, `B44A`, `HTJ2K32`, `HTJ2K256`, and `ZSTD`.
- [x] V3 deep compression support for `NONE`, `RLE`, `ZIPS`, `ZIP`, and `ZSTD`.
- [x] Layer- and multiview-aware helpers such as `EXRLayers` and `LoadEXRWithLayer`, including RGBA expansion for subsampled channels in the convenience load path.
- [x] Managed header/image models that preserve EXR metadata needed by tools and inspectors, including data/display windows, tile descriptions, custom attributes, channel sampling, line order, and long names.
- [x] Stateful `TinyEXR.V3` reader/writer APIs for multipart, mip/rip, flat/deep, partial block reads, bounded-memory streaming writes, synchronous/asynchronous data sources, cancellation, and `WouldBlock` resume.
- [x] V3 ZSTD flat/deep decode and encode, including entropy-compressed frames and canonical raw fallback when compression does not reduce the payload.
- [x] V3 HTJ2K32/HTJ2K256 flat decode and genuine encode through a safe managed JPEG 2000 Part 15 implementation.
- [x] Safe SIMD paths for pixel conversion, RGB color matrices, and ZIP/RLE byte reorder and prediction, with scalar parity fallbacks.
- [x] V3 spectral wavelength cubes and CPU image utilities: typed pixel conversion, whole-image and streaming resize, tone mapping, color/transfer transforms, `.cube` 3D LUTs, planar/interleaved bridges, and luminance-chroma reconstruction.
- [x] V3 whole-part decode for mixed flat/deep multipart files. The v1-compatible flat multipart facade returns `UnsupportedFeature` when a part is deep.

## Usage

The `TinyEXR` namespace keeps the public facade close to tinyexr v1. The
`TinyEXR.V3` namespace exposes the new stateful object, partial-I/O, deep, and
streaming model introduced by tinyexr v3. See
[TinyEXR v3 API notes](docs/tinyexr-v3.md) for the upstream differences, managed
type mapping, migration status, and codec support matrix.

V1-compatible facade:

```csharp
ResultCode load = Exr.LoadEXR(inputPath, out float[] rgba, out int width, out int height);
if (load != ResultCode.Success)
{
    throw new InvalidOperationException($"LoadEXR failed: {load}");
}
```

Direct v3 API:

```csharp
using TinyEXR.V3;

ReaderResult<Image> load = ExrFile.LoadFromFile(inputPath);
if (!load.IsSuccess || load.Value is not Image image)
{
    throw new InvalidOperationException($"EXR load failed: {load.Status}", load.Error);
}

Part firstPart = image.Parts[0];
PartLevel baseLevel = firstPart.GetLevel(0, 0);

Console.WriteLine(
    $"{firstPart.Header.PartType}: {baseLevel.Width}x{baseLevel.Height}, " +
    $"{baseLevel.Channels.Count} channels");

WriterResult save = ExrFile.SaveToFile(image, outputPath, Compression.ZSTD);
if (!save.IsSuccess)
{
    throw new InvalidOperationException($"EXR save failed: {save.Status}", save.Error);
}
```

## Samples

The repository currently includes `TinyEXR.Viewer`, an Avalonia sample built on top of `TinyEXR.NET`.

The viewer is intended for manual EXR inspection: it can open EXR files from the file picker, drag and drop, or a command-line path, preview single-part images and pure-image multipart files, switch between parts/layers/levels when decoded image data is available, and display metadata such as version flags, windows, tile information, channels, custom attributes, and deep-image statistics.

See `Samples/TinyEXR.Viewer/README.md` for run instructions and the current feature boundaries.

## Test

The current test suite covers the main supported surface of the library across both target outputs: the default target path and the `netstandard2.1` fallback path run the same shared test cases.

See `Test/README.md` for the current test layout and execution details.

## Benchmark

The current compression benchmark compares TinyEXR.NET v3, the complete
vendored TinyEXR v3 C library, and OpenEXR 3.4.13 on the same deterministic
1920x1080 RGBA HALF image. The 2026-07-21 run used BenchmarkDotNet's normal
`DefaultJob` and clang-cl 22.1.3 native builds. Every timed operation includes
result allocation and complete in-memory encode/decode, while preparation,
validation, and result release are excluded.

Representative means are `encode ms / decode ms`:

| Compression | TinyEXR.NET v3 | TinyEXR v3 C | OpenEXR 3.4.13 |
| --- | ---: | ---: | ---: |
| ZIPS | 16.75 / 9.29 | 24.36 / 9.41 | 35.36 / 7.93 |
| ZIP | 11.11 / 9.73 | 19.02 / 6.38 | 21.88 / 4.55 |
| PIZ | 53.49 / 39.66 | 47.76 / 24.27 | 41.43 / 14.92 |
| PXR24 | 14.25 / 14.84 | 16.42 / 7.51 | 18.37 / 5.16 |
| HTJ2K256 | 105.44 / 102.34 | 47.43 / 33.71 | 29.39 / 23.61 |
| HTJ2K32 | 111.64 / 102.51 | 38.64 / 24.55 | 52.69 / 40.03 |
| ZSTD | 6.05 / 12.88 | 8.62 / 4.57 | - |

Managed ZIPS, ZIP, PXR24, and ZSTD encode outperform TinyEXR v3 C in this
workload; native implementations remain substantially faster for B44 and
HTJ2K decode. See [`Benchmark/README.md`](Benchmark/README.md) for the complete
68-row timing, throughput, allocation, encoded-size, build, fairness, and MSVC
compatibility report.

## Versioning

Starting with `v1.0`, `TinyEXR.NET` is a pure C# implementation of the `tinyexr`-compatible API surface.

The legacy `v0.3.x` line is kept as a maintenance branch. It may continue to receive compatibility fixes and follow `tinyexr` updates when needed, but no new features will be added to `v0.3.x`.

The main branch moves forward with `v1.0+`.

For new development, prefer the mainline `v1.0+` branch. Use the `v0.3.x` maintenance branch only if you need the legacy native-wrapper line for compatibility reasons.

### Upgrade from v0.3.x

- High-level RGBA helpers such as `LoadEXR`, `LoadEXRFromMemory`, `SaveEXR`, `SaveEXRToMemory`, `LoadEXRWithLayer`, and `EXRLayers` are still the recommended entry points, so code that only uses these helpers usually needs little or no change.
- `IsExr` and `IsExrFromMemory` are now `IsEXR` and `IsEXRFromMemory`.
- `TinyEXR.Native.*` and `TinyEXR.Native.EXRNative` are gone. The library is now a pure managed C# implementation, so the old native-runtime, P/Invoke, and static-link workflow from `v0.3.x` no longer applies.
- Native-style structs are replaced by managed types such as `ExrVersion`, `ExrHeader`, `ExrImage`, `ExrMultipartHeader`, `ExrMultipartImage`, `ExrDeepImage`, and `ExrBox2i`.
- Low-level read/write calls are now managed `out`-based APIs instead of `ref`-based native mutation. For example, `ParseEXRHeaderFromFile(path, ref version, ref header)` becomes `ParseEXRHeaderFromFile(path, out ExrVersion version, out ExrHeader header)`, and `LoadEXRImageFromFile(ref image, ref header, path)` becomes `LoadEXRImageFromFile(path, header, out ExrImage image)`.
- `SaveEXRImageToMemory` also changed shape: `v0.3.x` returned `byte[]?`, while the current API returns `ResultCode` and writes the payload to `out byte[] encoded`.

## Known Limitation

The V3 reader materializes mixed flat/deep multipart files. The v1-compatible `LoadEXRMultipartImage*` model can represent only flat `ExrImage` parts, so it returns `UnsupportedFeature` when any part is deep; use `TinyEXR.V3.ExrReader` for those files. Likewise, v1 `ExrDeepImage` has no mip/rip level dimension, so `LoadDeepEXR*` accepts one-level deep tiles and rejects multilevel deep tiles.

The V3 reader and writer decode and genuinely encode flat HTJ2K32/HTJ2K256 payloads. Compressed deep HTJ2K data and compressed DWAA/DWAB remain intentionally unsupported, matching upstream v3 policy.

## License

`TinyEXR.NET` is under MIT license
