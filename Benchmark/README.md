# TinyEXR Benchmarks

This directory contains two benchmark entry points that use the same sample manifest and the same memory-only case matrix.

- `baseline`: pure C++ Google Benchmark against vendored `tinyexr`
- `TinyEXR.Benchmark`: C# BenchmarkDotNet project against `TinyEXR.NET`

Both runners assume `.cache/openexr-images` already exists and contains the samples referenced by [`sample-manifest.csv`](sample-manifest.csv). Sample files are read once during setup; timed iterations only operate on in-memory buffers and the API-owned allocations required by each benchmark.

## Samples

The shared manifest currently maps these sample ids:

- `desk_scanline` => `ScanLines/Desk.exr`
- `kapaa_multires` => `MultiResolution/Kapaa.exr`
- `beachball_multipart_0001` => `Beachball/multipart.0001.exr`
- `balls_deep_scanline` => `v2/LowResLeftView/Balls.exr`

## Case Matrix

- C++ `baseline`: `LoadEXRFromMemory`, `SaveEXRToMemory`, `LoadEXRImageFromMemory`, `SaveEXRImageToMemory`, `LoadEXRMultipartImageFromMemory`, `SaveEXRMultipartImageToMemory`
- C# `TinyEXR.Benchmark`: the same matrix plus managed-only `LoadDeepImageFromMemory`

## C++ Baseline

Configure and build:

```powershell
cmake -S Benchmark/baseline -B Benchmark/baseline/build
cmake --build Benchmark/baseline/build --config Release
```

Run a smoke benchmark:

```powershell
.\Benchmark\baseline\build\Release\tinyexr_baseline_benchmark.exe --benchmark_filter="SaveEXRToMemory/desk_scanline|LoadEXRImageFromMemory/desk_scanline|LoadEXRMultipartImageFromMemory/beachball_multipart_0001|SaveEXRMultipartImageToMemory/beachball_multipart_0001"
```

The upstream tinyexr public deep API is file-based only, so the baseline currently has no dedicated deep memory benchmark. It does not add a non-public deep decode path.

## C# BenchmarkDotNet

Build or run from the repository root:

```powershell
dotnet run -c Release --project Benchmark/TinyEXR.Benchmark/TinyEXR.Benchmark.csproj -- --filter "*" --job short --exporters csv
```

BenchmarkDotNet writes reports to `BenchmarkDotNet.Artifacts`.
