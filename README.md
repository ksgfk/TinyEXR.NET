# TinyEXR.NET

[![Test](https://github.com/ksgfk/TinyEXR.NET/actions/workflows/test.yml/badge.svg)](https://github.com/ksgfk/TinyEXR.NET/actions/workflows/test.yml)

`TinyEXR.NET` is a pure C# port library of [tinyexr](https://github.com/syoyo/tinyexr)

The target frameworks are `net8.0`, `netstandard2.1`

## Download

`TinyEXR.NET` can be found on NuGet [![NuGet](https://img.shields.io/nuget/v/TinyEXR.NET)](https://www.nuget.org/packages/TinyEXR.NET) (← click it !)

## Dependencies

The `net8.0` target has no additional runtime dependencies.

The `netstandard2.1` target depends on [SharpZipLib](https://github.com/icsharpcode/SharpZipLib).

## Features

- [x] full NativeAOT support!
- [x] Single-part EXR read/write for scanline images.
- [x] Single-part EXR read/write for tiled images, including one-level tiles and multi-resolution mipmap/ripmap layouts.
- [x] Multipart image EXR parse/load/save for image parts.
- [x] Deep single-part scanline EXR load through `LoadDeepEXR`.
- [x] Regular image compression support for `NONE`, `RLE`, `ZIP`, `ZIPS`, `PIZ`, `PXR24`, `B44`, and `B44A`.
- [x] Deep scanline compression support for `NONE`, `RLE`, `ZIPS`, and `ZIP`.
- [x] Layer- and multiview-aware helpers such as `EXRLayers` and `LoadEXRWithLayer`, including RGBA expansion for subsampled channels in the convenience load path.
- [x] Managed header/image models that preserve EXR metadata needed by tools and inspectors, including data/display windows, tile descriptions, custom attributes, channel sampling, line order, and long names.
- [ ] Full image decode for mixed multipart files that include deep or non-image parts. Current behavior is metadata-only for those parts.

## Usage

`TinyEXR.NET` keeps its public API almost identical to `tinyexr` v1, so most functions and data structures can be mapped directly from the original library. The main differences are a small number of C#-oriented interfaces designed to avoid raw pointer-based usage and provide a more natural managed API surface.

Example:

```csharp
ResultCode load = Exr.LoadEXR(inputPath, out float[] rgba, out int width, out int height);
if (load != ResultCode.Success)
{
    throw new InvalidOperationException($"LoadEXR failed: {load}");
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

The repository includes memory-only benchmarks for the main read/write paths. Sample files are loaded during setup, so the timed results reflect decode/encode work on in-memory buffers rather than filesystem IO.

Updated on `2026-05-03`. C# results use BenchmarkDotNet `Mean`; C++ baseline results use the Google Benchmark `CPU` column.

* CPU: `13th Gen Intel Core i7-13700K 3.40GHz`
* OS: `Windows 11 25H2 10.0.26200.8246`
* .NET SDK/runtime: `10.0.203` / `10.0.7`
* C# benchmark: `BenchmarkDotNet 0.15.8`, default job, workstation GC
* C++ compiler: `MSVC 19.50.35730`, `Visual Studio 2026 18.5.2`

| Method | Sample | C# mean | C# allocated | C++ baseline | Managed / baseline |
| --- | --- | ---: | ---: | ---: | ---: |
| `LoadEXRFromMemory` | `desk_scanline` | `40.05 ms` | `15.73 MB` | `38.65 ms` | `1.04x` |
| `SaveEXRToMemory` | `desk_scanline` | `108.77 ms` | `46.56 MB` | `164.06 ms` | `0.66x` |
| `LoadEXRImageFromMemory` | `desk_scanline` | `23.43 ms` | `7.15 MB` | `28.65 ms` | `0.82x` |
| `SaveEXRImageToMemory` | `desk_scanline` | `43.48 ms` | `80.90 MB` | `41.36 ms` | `1.05x` |
| `LoadEXRImageFromMemory` | `kapaa_multires` | `42.94 ms` | `21.27 MB` | `55.40 ms` | `0.78x` |
| `SaveEXRImageToMemory` | `kapaa_multires` | `169.49 ms` | `53.54 MB` | `213.54 ms` | `0.79x` |
| `LoadEXRMultipartImageFromMemory` | `beachball_multipart_0001` | `82.74 ms` | `30.65 MB` | `127.60 ms` | `0.65x` |
| `SaveEXRMultipartImageToMemory` | `beachball_multipart_0001` | `213.06 ms` | `78.32 MB` | `223.96 ms` | `0.95x` |
| `LoadDeepImageFromMemory` | `balls_deep_scanline` | `14.19 ms` | `5.47 MB` | `N/A` | `N/A` |

See `Benchmark/README.md` for more details.

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

Mixed multipart files that include deep or non-image parts are currently metadata-only for those parts; full image decode is supported for image parts.

## License

`TinyEXR.NET` is under MIT license
