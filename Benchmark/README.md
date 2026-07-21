# TinyEXR v3 Benchmarks

This directory contains three v3 compression benchmark entry points:

- `TinyEXR.Benchmark`: BenchmarkDotNet coverage for TinyEXR.NET v3.
- `baseline/tinyexr_compression_benchmark`: the vendored pure-C11 TinyEXR v3
  library with a Google Benchmark driver.
- `baseline/openexr_compression_benchmark`: OpenEXR 3.4.13 with the same image
  and timing boundary.

The former v1 `baseline/src/main.cpp` benchmark has been removed. Generated
fixtures, dependencies, binaries, and reports remain under `.cache`, `bin`,
`obj`, `artifacts`, or `BenchmarkDotNet.Artifacts` and are not committed.

## Run

Run the complete comparison from PowerShell 7:

```powershell
pwsh .\Benchmark\run-compression-benchmarks.ps1 -Job Default
```

`Default` is also the script default. BenchmarkDotNet chooses its normal pilot,
warmup, and measurement counts. Native cases use a 0.5-second minimum and five
Google Benchmark repetitions. `Short` and `Dry` remain available for local
iteration and smoke testing, but the report below uses only `Default` data.

Outputs are written to `artifacts/compression-benchmarks`:

- `managed/results`: BenchmarkDotNet JSON, CSV, and Markdown reports.
- `tinyexr.json`: TinyEXR v3 C Google Benchmark results and build context.
- `openexr.json`: OpenEXR Google Benchmark results and build context.
- `comparison.csv`: 68 normalized result rows across all implementations.

## Build

The upstream TinyEXR `CMakeLists.txt` explicitly exports only the legacy v1
`tinyexr.cc + miniz` compile-test target. The baseline therefore defines a
local `TinyEXR::V3` static target that matches upstream `make lib`: all 35
`src/*.c` files, `deps/zstd/tinyexr_zstd.c`, and the eight vendored libdeflate
sources used by the default `DEFLATE=auto` configuration. Benchmark code is a
separate executable and is not compiled into the codec library.

TinyEXR v3 does not require a system zlib library. ZIP, ZIPS, and PXR24 always
have the in-tree zlib-stream implementation; the default build additionally
links vendored libdeflate and selects it at runtime at compression level 4.

The CMake project does not reject MSVC, GCC, or Clang and uses ordinary CMake
Release flags except for clang-cl, where `/O2 /Ob3 -march=native` and supported
IPO are enabled. The convenience report script is Windows/clang-cl-specific.
On non-Windows systems, configure the CMake project directly with GCC or Clang.

Native MSVC 19.51 is currently not supported by the upstream v3 C sources.
MSVC's C frontend does not implement the required C11 `_Atomic`, and
`src/exr_jph_simd.c` also emits an unresolved `__builtin_clz`. No benchmark-only
compatibility shim is applied because that would make the measured library
differ from upstream. The verified Windows native toolchain is clang-cl.

## Fair Timing Boundary

All implementations process the same deterministic 1920x1080 scanline image
with four HALF channels (`A`, `B`, `G`, `R`), a 15.82 MiB raw payload, one
worker thread, and memory-only I/O. Each library receives the image in its
native planar or interleaved layout.

The timed work is the same semantic unit for every implementation:

- Encode starts with result-buffer allocation and ends when the complete
  in-memory EXR is available.
- Decode starts with result-image allocation and ends when all output pixels
  are materialized.
- Source construction, headers, fixture I/O, validation, counters, and result
  release are outside the timing boundary.

TinyEXR.NET returns the complete `byte[]` or v3 `Image`. TinyEXR C uses manual
wall time around `exr_save_to_memory` or `exr_load_from_memory`; their result
allocations occur inside those calls. OpenEXR has no equivalent memory helper,
so its benchmark reuses only the stream adapter object: each encode allocates
a fresh exact-capacity result buffer inside the timer, and each decode allocates
a fresh RGBA result. Required OpenEXR stream callbacks remain timed. This avoids
counting adapter construction, vector growth, buffer clearing, validation, or
destruction as codec work while still charging every implementation for a
complete result.

For every mutually supported codec, both native implementations decode the
exact EXR produced by TinyEXR.NET v3. ZSTD is shared by TinyEXR.NET and TinyEXR
v3 C. DWAA/DWAB exist only in OpenEXR and therefore use OpenEXR's own output.
Encode sizes always describe each implementation's own file.

| Compression | TinyEXR.NET v3 | TinyEXR v3 C | OpenEXR 3.4.13 |
| --- | --- | --- | --- |
| None, RLE, ZIPS, ZIP, PIZ, PXR24, B44, B44A | Encode/decode | Encode/decode | Encode/decode |
| DWAA, DWAB | - | - | Encode/decode |
| HTJ2K256, HTJ2K32 | Encode/decode | Encode/decode | Encode/decode |
| ZSTD | Encode/decode | Encode/decode | - |

## Default Results (2026-07-21)

These are same-machine results, not cross-machine performance claims:

- CPU: Intel Core i7-13700K, 16 physical cores / 24 logical processors.
- OS: Windows 11 25H2, build 10.0.26200.8875, x64.
- Managed: .NET SDK 10.0.302, .NET 10.0.10, BenchmarkDotNet 0.15.8,
  concurrent workstation GC, `DefaultJob`. Workload warmup ranged from 6 to
  16 iterations and retained measurement samples ranged from 12 to 100.
- Native: clang-cl 22.1.3, `/O2 /Ob3 -march=native`, loop/SLP vectorization,
  IPO, Google Benchmark 1.9.5, five repetitions with a 0.5-second minimum.
- Libraries: TinyEXR `v3.2.0-38-g1b10661`, vendored libdeflate level 4, and
  OpenEXR 3.4.13.

Each timing cell is `mean milliseconds / raw MiB per second`. Managed allocation
is binary MiB per operation. Lower time and higher throughput are better.

### Encode

| Compression | TinyEXR.NET v3 ms / MiB/s | Managed alloc MiB | TinyEXR v3 C ms / MiB/s | OpenEXR ms / MiB/s |
| --- | ---: | ---: | ---: | ---: |
| None | 11.20 / 1412.66 | 96.99 | 6.44 / 2456.99 | 6.83 / 2315.25 |
| RLE | 19.33 / 818.30 | 78.79 | 16.78 / 942.54 | 16.38 / 966.12 |
| ZIPS | 16.75 / 944.55 | 34.94 | 24.36 / 649.47 | 35.36 / 447.46 |
| ZIP | 11.11 / 1424.55 | 32.94 | 19.02 / 831.69 | 21.88 / 723.18 |
| PIZ | 53.49 / 295.78 | 97.55 | 47.76 / 331.22 | 41.43 / 381.91 |
| PXR24 | 14.25 / 1110.20 | 32.87 | 16.42 / 969.15 | 18.37 / 861.13 |
| B44 | 40.05 / 394.98 | 96.54 | 16.57 / 954.50 | 16.71 / 947.07 |
| B44A | 33.76 / 468.62 | 69.65 | 16.06 / 985.06 | 15.55 / 1017.21 |
| DWAA | - | - | - | 60.89 / 259.84 |
| DWAB | - | - | - | 45.46 / 348.04 |
| HTJ2K256 | 105.44 / 150.05 | 26.38 | 47.43 / 333.58 | 29.39 / 538.40 |
| HTJ2K32 | 111.64 / 141.70 | 27.59 | 38.64 / 409.45 | 52.69 / 300.25 |
| ZSTD | 6.05 / 2614.18 | 33.39 | 8.62 / 1835.94 | - |

### Decode

All shared rows use the TinyEXR.NET-produced bytes described in the size table.

| Compression | TinyEXR.NET v3 ms / MiB/s | Managed alloc MiB | TinyEXR v3 C ms / MiB/s | OpenEXR ms / MiB/s |
| --- | ---: | ---: | ---: | ---: |
| None | 5.05 / 3134.17 | 48.11 | 4.26 / 3715.88 | 3.15 / 5017.87 |
| RLE | 9.26 / 1707.62 | 52.74 | 7.92 / 1996.68 | 15.28 / 1035.15 |
| ZIPS | 9.29 / 1703.09 | 49.00 | 9.41 / 1680.50 | 7.93 / 1994.40 |
| ZIP | 9.73 / 1626.72 | 47.96 | 6.38 / 2478.50 | 4.55 / 3478.10 |
| PIZ | 39.66 / 398.89 | 83.24 | 24.27 / 651.76 | 14.92 / 1060.53 |
| PXR24 | 14.84 / 1065.81 | 48.01 | 7.51 / 2108.11 | 5.16 / 3066.49 |
| B44 | 21.76 / 726.99 | 55.01 | 10.03 / 1577.12 | 9.07 / 1744.19 |
| B44A | 16.99 / 931.14 | 52.29 | 8.57 / 1847.08 | 8.25 / 1917.39 |
| DWAA | - | - | - | 14.63 / 1081.23 |
| DWAB | - | - | - | 19.21 / 823.73 |
| HTJ2K256 | 102.34 / 154.58 | 22.01 | 33.71 / 469.29 | 23.61 / 670.10 |
| HTJ2K32 | 102.51 / 154.33 | 23.49 | 24.55 / 644.46 | 40.03 / 395.25 |
| ZSTD | 12.88 / 1228.69 | 32.76 | 4.57 / 3462.52 | - |

### Encoded Output

Each cell is `encoded MiB / raw-to-encoded ratio`.

| Compression | TinyEXR.NET v3 | TinyEXR v3 C | OpenEXR 3.4.13 |
| --- | ---: | ---: | ---: |
| None | 15.837 / 1.00x | 15.837 / 1.00x | 15.837 / 1.00x |
| RLE | 4.199 / 3.77x | 4.199 / 3.77x | 4.199 / 3.77x |
| ZIPS | 0.143 / 110.82x | 0.143 / 110.26x | 0.143 / 110.26x |
| ZIP | 0.076 / 208.27x | 0.085 / 185.78x | 0.085 / 185.78x |
| PIZ | 0.730 / 21.67x | 0.732 / 21.61x | 0.730 / 21.67x |
| PXR24 | 0.062 / 256.98x | 0.064 / 247.86x | 0.064 / 247.86x |
| B44 | 6.922 / 2.29x | 6.922 / 2.29x | 6.922 / 2.29x |
| B44A | 4.203 / 3.76x | 4.203 / 3.76x | 4.203 / 3.76x |
| DWAA | - | - | 0.141 / 111.82x |
| DWAB | - | - | 0.120 / 131.35x |
| HTJ2K256 | 0.647 / 24.45x | 0.647 / 24.45x | 0.647 / 24.45x |
| HTJ2K32 | 0.727 / 21.76x | 0.727 / 21.76x | 0.728 / 21.74x |
| ZSTD | 0.138 / 114.99x | 0.138 / 114.99x | - |

### Findings

- Managed ZIPS and ZIP encode take 69%/58% of TinyEXR v3 C time and
  47%/51% of OpenEXR time. Managed PXR24 encode is also faster, taking
  87% of TinyEXR v3 C time and 78% of OpenEXR time.
- Managed ZSTD encode is 1.42x faster than TinyEXR v3 C, while native ZSTD
  decode is 2.82x faster than managed.
- Managed B44/B44A encode takes 2.42x/2.10x the TinyEXR v3 C time and
  2.40x/2.17x the OpenEXR time. Decode shows a similar roughly 2x gap.
- Managed HTJ2K256 encode/decode takes 2.22x/3.04x the TinyEXR v3 C time;
  HTJ2K32 takes 2.89x/4.18x. Relative to OpenEXR, the corresponding gaps are
  3.59x/4.33x and 2.12x/2.56x.
- Managed RLE decode is 17% slower than TinyEXR v3 C but takes only 61% of
  OpenEXR time. Managed and TinyEXR v3 C ZIPS decode are within 2%.

## Verification

The report run completed all 68 expected rows without failures. Additional
verification on the same revision:

- clang-cl built the complete v3 target and both benchmark executables with no
  warnings after the final harness cleanup.
- TinyEXR native smoke: 22/22 encode/decode cases passed.
- OpenEXR native smoke: 24/24 encode/decode cases passed.
- Default managed test host: 274/274 passed.
- `netstandard2.1` fallback host: 274/274 passed.
- Full solution build: 0 warnings, 0 errors.

Direct native clang-cl commands for Visual Studio 2026 are:

```powershell
cmake -S .\Benchmark\baseline -B .\.cache\benchmark-native-clang -G "Visual Studio 18 2026" -A x64 -T ClangCL
cmake --build .\.cache\benchmark-native-clang --config Release --target compression_benchmarks --parallel
```

On GCC or Clang single-config generators:

```sh
cmake -S Benchmark/baseline -B .cache/benchmark-native -DCMAKE_BUILD_TYPE=Release
cmake --build .cache/benchmark-native --target compression_benchmarks --parallel
```
