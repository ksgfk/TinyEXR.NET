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
| `LoadEXRFromMemory` | `desk_scanline` | `39.79 ms` | `15.73 MB` | `40.44 ms` | `0.98x` |
| `SaveEXRToMemory` | `desk_scanline` | `109.25 ms` | `58.10 MB` | `160.16 ms` | `0.68x` |
| `LoadEXRImageFromMemory` | `desk_scanline` | `23.36 ms` | `7.15 MB` | `41.19 ms` | `0.57x` |
| `SaveEXRImageToMemory` | `desk_scanline` | `44.08 ms` | `80.89 MB` | `59.03 ms` | `0.75x` |
| `LoadEXRImageFromMemory` | `kapaa_multires` | `42.83 ms` | `21.27 MB` | `72.92 ms` | `0.59x` |
| `SaveEXRImageToMemory` | `kapaa_multires` | `170.12 ms` | `66.92 MB` | `208.33 ms` | `0.82x` |
| `LoadEXRMultipartImageFromMemory` | `beachball_multipart_0001` | `84.24 ms` | `30.65 MB` | `132.81 ms` | `0.63x` |
| `SaveEXRMultipartImageToMemory` | `beachball_multipart_0001` | `222.58 ms` | `83.21 MB` | `218.75 ms` | `1.02x` |
| `LoadDeepImageFromMemory` | `balls_deep_scanline` | `14.06 ms` | `5.48 MB` | `N/A` | `N/A` |

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

## License

`TinyEXR.NET` is under MIT license
