# TinyEXR.NET

`TinyEXR.NET` is a pure managed C# port of the tinyexr v1 API.

The library targets `net10.0`. The public API no longer exposes `TinyEXR.Native`, native handles, or `unsafe`-based lifecycle methods.

## Package

NuGet: [TinyEXR.NET](https://www.nuget.org/packages/TinyEXR.NET)

## Public API

The entry point is [`TinyEXR.Exr`](TinyEXR.NET/Exr.cs).

The managed facade provides:

- convenience wrappers such as `LoadEXR`, `LoadEXRWithLayer`, `SaveEXR`, `SaveEXRToMemory`
- spectral helpers such as `EXRFormatWavelength`, `EXRSpectralChannelName`, `EXRSetSpectralAttributes`
- helper classes [`SinglePartExrReader`](TinyEXR.NET/SinglePartExrReader.cs) and [`ScanlineExrWriter`](TinyEXR.NET/ScanlineExrWriter.cs)

The public data model is fully managed:

- [`ExrVersion`](TinyEXR.NET/ExrVersion.cs)
- [`ExrHeader`](TinyEXR.NET/ExrHeader.cs)
- [`ExrImage`](TinyEXR.NET/ExrImage.cs)
- [`ExrDeepImage`](TinyEXR.NET/ExrDeepImage.cs)
- [`ExrAttribute`](TinyEXR.NET/ExrAttribute.cs)
- [`ExrTileDescription`](TinyEXR.NET/ExrTileDescription.cs)
- [`ExrChannel`](TinyEXR.NET/ExrChannel.cs)

## Compression Support

Current v1 support is intentionally conservative:

- `None`, `ZIP`, `ZIPS`, `RLE`, `PIZ`, `PXR24`, `B44`, `B44A`: supported on `net10.0`
- `DWAA`, `DWAB`: recognized but not implemented yet, reported as `UnsupportedFeature`

For deep scanline loading, the supported compression set is narrower and follows the OpenEXR deep file layout:

- `None`, `RLE`, `ZIP`, `ZIPS`: supported
- `PIZ`, `PXR24`, `B44`, `B44A`, `DWAA`, `DWAB`: unsupported for deep images

This is an intentional divergence from the current vendored `tinyexr` source:

- `TinyEXR.NET` treats deep image compression as spec-bound behavior and rejects non-standard combinations such as deep scanline + `PIZ` with `UnsupportedFeature`
- the current `tinyexr` source tree kept in `TinyEXR.Native/tinyexr` is more permissive here and may attempt to decode deep scanline + `PIZ` when built with `TINYEXR_USE_PIZ`

In other words, this repository does not treat deep + `PIZ` as a missing ported feature. It treats it as a non-standard extension and keeps the managed implementation aligned with the OpenEXR deep file layout instead of mirroring that permissive upstream behavior.

## Quick Example

```csharp
using TinyEXR;

ResultCode result = Exr.LoadEXR("image.exr", out float[] rgba, out int width, out int height);
if (result != ResultCode.Success)
{
    throw new InvalidOperationException(result.ToString());
}

Console.WriteLine($"{width}x{height}, rgba={rgba.Length}");
```

```csharp
using TinyEXR;

float[] rgba =
{
    1.0f, 0.25f, 0.5f, 1.0f
};

ResultCode result = Exr.SaveEXRToMemory(rgba, 1, 1, 4, asFp16: false, out byte[] encoded);
if (result != ResultCode.Success)
{
    throw new InvalidOperationException(result.ToString());
}
```

## Tests

The test suite is split into:

- managed unit tests
- integration tests against `openexr-images`
- round-trip write/read tests

The integration samples are pinned to `openexr-images` commit `e38ffb0790f62f05a6f083a6fa4cac150b3b7452` and are expected under `.cache/openexr-images` by default.

Prepare them with:

```powershell
pwsh ./Scripts/prepare-openexr-images.ps1
```

You can override the sample location with `TINYEXR_OPENEXR_IMAGES_ROOT`.

Run the full suite with:

```powershell
dotnet test --solution TinyEXR.Test/TinyEXR.Test.sln --configuration Release --report-trx --report-trx-filename portv1.trx -p:SignAssemblyKey=false
```

Note: the repository still keeps the upstream `tinyexr` submodule for reference and regression samples used by the tests, but the shipped package itself is pure managed.

## Benchmark Snapshot

The benchmark harnesses live under [`Benchmark`](Benchmark/README.md). Both runners use the same sample manifest and only touch disk during setup; timed iterations operate on in-memory buffers only.

This snapshot was rerun on `2026-04-21` on the same Windows machine with:

- C++ baseline: Google Benchmark + Release build of vendored `tinyexr`
- C#: BenchmarkDotNet `ShortRun` + `MemoryDiagnoser` on `.NET 10.0.6`

Commands:

```powershell
.\Benchmark\baseline\build\Release\tinyexr_baseline_benchmark.exe --benchmark_filter="LoadEXRFromMemory/.+|SaveEXRToMemory/.+|LoadEXRImageFromMemory/.+|SaveEXRImageToMemory/.+|ParseEXRMultipartHeaderFromMemory/.+|LoadEXRMultipartImageFromMemory/.+|SaveEXRMultipartImageToMemory/.+" --benchmark_out=Benchmark\baseline\results.csv --benchmark_out_format=csv
dotnet run -c Release --project Benchmark\TinyEXR.Benchmark\TinyEXR.Benchmark.csproj -- --filter "*" --job short --exporters csv
```

Comparison of overlapping memory benchmarks:

| API | Sample | tinyexr C++ | TinyEXR.NET C# | C#/C++ |
| --- | --- | ---: | ---: | ---: |
| `LoadEXRFromMemory` | `desk_scanline` | `39.52 ms` | `62.43 ms` | `1.58x` |
| `SaveEXRToMemory` | `desk_scanline` | `164.06 ms` | `110.26 ms` | `0.67x` |
| `LoadEXRImageFromMemory` | `desk_scanline` | `29.76 ms` | `47.80 ms` | `1.61x` |
| `LoadEXRImageFromMemory` | `kapaa_multires` | `55.40 ms` | `50.17 ms` | `0.91x` |
| `SaveEXRImageToMemory` | `desk_scanline` | `34.93 ms` | `45.92 ms` | `1.31x` |
| `SaveEXRImageToMemory` | `kapaa_multires` | `213.54 ms` | `171.78 ms` | `0.80x` |
| `LoadEXRMultipartImageFromMemory` | `beachball_multipart_0001` | `125.00 ms` | `90.27 ms` | `0.72x` |
| `SaveEXRMultipartImageToMemory` | `beachball_multipart_0001` | `208.33 ms` | `214.09 ms` | `1.03x` |
| `LoadDeepImageFromMemory` | `balls_deep_scanline` | / | `14.79 ms` | / |

`C#/C++` is the ratio of managed mean time to baseline mean time, so values below `1.00x` are faster for `TinyEXR.NET` on this machine.

The current upstream `tinyexr` public API still does not expose a public deep memory decode entry point, so there is no like-for-like C++ baseline number for `LoadDeepImageFromMemory`.

## License

`TinyEXR.NET` is released under the MIT license.
