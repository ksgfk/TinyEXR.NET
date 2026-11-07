# TinyEXR.NET

[![Test](https://github.com/ksgfk/TinyEXR.NET/actions/workflows/test.yml/badge.svg)](https://github.com/ksgfk/TinyEXR.NET/actions/workflows/test.yml)

`TinyEXR.NET` is a pure C# port library of [tinyexr](https://github.com/syoyo/tinyexr)

The target frameworks are `net8.0`, `netstandard2.1`

## Download

`TinyEXR.NET` can be found on NuGet [![NuGet](https://img.shields.io/nuget/v/TinyEXR.NET)](https://www.nuget.org/packages/TinyEXR.NET) (← click it !)

## Dependencies

The `net8.0` target has no third-party runtime dependencies. The `netstandard2.1` target depends on [SharpZipLib](https://github.com/icsharpcode/SharpZipLib).

## Features

- [x] full NativeAOT support!
- [x] Single-part EXR read/write for scanline images.
- [x] Single-part EXR read/write for tiled images, including one-level tiles and multi-resolution mipmap/ripmap layouts.
- [x] Multipart EXR parse/load/save for flat, deep and mixed parts, including single-entry multipart containers.
- [x] Deep scanline and tiled EXR read/write, including every mip/rip level.
- [x] Regular image compression support for `NONE`, `RLE`, `ZIP`, `ZIPS`, `PIZ`, `PXR24`, `B44`, `B44A`, `HTJ2K32`, and `HTJ2K256`.
- [x] Deep compression support for `NONE`, `RLE`, and `ZIPS`, following official OpenEXR.
- [x] Layer- and multiview-aware helpers such as `EXRLayers` and `LoadEXRWithLayer`, including RGBA expansion for subsampled channels in the convenience load path.
- [x] Managed header/image models that preserve EXR metadata needed by tools and inspectors, including data/display windows, tile descriptions, custom attributes, channel sampling, line order, and long names.
- [x] Stateful `TinyEXR` reader/writer APIs for multipart, mip/rip, flat/deep, partial block reads, bounded-memory streaming writes, synchronous/asynchronous data sources, cancellation, and `WouldBlock` resume.
- [x] HTJ2K32/HTJ2K256 flat decode and genuine encode through a safe managed JPEG 2000 Part 15 implementation.
- [x] Safe SIMD paths for pixel conversion, RGB color matrices, and ZIP/RLE byte reorder and prediction, with scalar parity fallbacks.
- [x] Spectral wavelength cubes and CPU image utilities: typed pixel conversion, whole-image and streaming resize, tone mapping, color/transfer transforms, `.cube` 3D LUTs, planar/interleaved bridges, and luminance-chroma reconstruction.
- [x] Reusable exclusive `ExrContext`, file/image budgets, typed custom attributes and immutable RGBA8 previews.
- [x] One V3 reader, writer and codec layer for every public entry point; no legacy fallback.

## Usage

The `TinyEXR` namespace exposes the immutable V3 models and the `Exr` convenience
methods. Data sources and sinks live in `TinyEXR.IO`. All I/O uses `ExrReader`
and `ExrWriter`. The former V1 models, status codes and implementation have been
removed; this is a **breaking source and binary change**. The package version
remains `1.2.0` during this refactor; release versioning is a separate decision.
See the [V3-only migration guide](docs/migration-v3-only.md) and
[V3.1 migration guide](docs/migration-v3.1.md), and [codec port provenance](docs/codec-port.md).

RGBA convenience loading:

```csharp
using TinyEXR;

ExrResult load = Exr.LoadEXR(inputPath, out float[] rgba, out int width, out int height);
if (load != ExrResult.Success)
{
    throw new InvalidOperationException($"LoadEXR failed: {load}");
}
```

Modern managed API (based on upstream v3):

```csharp
using TinyEXR;

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

WriterResult save = ExrFile.SaveToFile(image, outputPath);
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

Benchmarks compare the managed implementation with official OpenEXR **3.5.3**,
using the same pinned build as the correctness oracle. TinyEXR native timings
and their historical comparison tables have been retired. See
[Benchmark/README.md](Benchmark/README.md) for reproducible timing boundaries,
commands and optimization acceptance criteria.

## Versioning

Starting with `v1.0`, `TinyEXR.NET` is a pure C# implementation of the `tinyexr`-compatible API surface.

The legacy `v0.3.x` line is kept as a maintenance branch. It may continue to receive compatibility fixes and follow `tinyexr` updates when needed, but no new features will be added to `v0.3.x`.

The main branch moves forward with `v1.0+`.

For new development, prefer the mainline `v1.0+` branch. Use the `v0.3.x` maintenance branch only if you need the legacy native-wrapper line for compatibility reasons.

### Migrating existing code

Use the [namespace migration table](docs/migration-1.2.0.md) when migrating from
`TinyEXR.V3`. Use the [V3-only migration guide](docs/migration-v3-only.md) for the
removed V1 types and changed `Exr` signatures. Rebuild all dependent assemblies.
`Exr` retains method names and `out` style, while accepting `Part` / `Image` and
returning `ExrResult`. Native-wrapper consumers from `v0.3.x` must also replace
native structs and P/Invoke calls with these managed models.

## Known limitations

DWAA/DWAB accept only raw fallback blocks that official OpenEXR can read;
compressed decoding and all encoding requests return `Unsupported`.
`LJ2K = 12` and `ZSTD = 13` use official compression IDs and are not implemented.
Deep ZIP, PIZ, B44, DWA and HTJ2K are invalid combinations, including raw blocks.
Tiled/deep sampling must be one; flat scanline sampling must align to window
origins and dimensions. See the [support matrix](docs/tinyexr-v3.md).

## License

Original managed code is MIT licensed. The V3 codec ports retain upstream BSD-3-Clause/BSD-2-Clause notices; see [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES).
