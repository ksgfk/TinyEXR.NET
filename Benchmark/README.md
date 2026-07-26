# TinyEXR v3 Benchmarks

This directory contains three v3 compression benchmark entry points:

- `TinyEXR.Benchmark`: BenchmarkDotNet coverage for TinyEXR.NET v3.
- `baseline/tinyexr_compression_benchmark`: the vendored pure-C11 TinyEXR v3
  library with a Google Benchmark driver.
- `baseline/openexr_compression_benchmark`: OpenEXR 3.4.13 with the same image
  and timing boundary.

Generated fixtures, dependencies, binaries, and reports live under `.cache`,
`bin`, `obj`, `artifacts`, or `BenchmarkDotNet.Artifacts` and are not committed.

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
- `comparison.csv`: 64 normalized result rows across all implementations.

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
exact EXR produced by TinyEXR.NET v3. DWAA/DWAB exist only in OpenEXR and
therefore use OpenEXR's own output.
Encode sizes always describe each implementation's own file.

| Compression | TinyEXR.NET v3 | TinyEXR v3 C | OpenEXR 3.4.13 |
| --- | --- | --- | --- |
| None, RLE, ZIPS, ZIP, PIZ, PXR24, B44, B44A | Encode/decode | Encode/decode | Encode/decode |
| DWAA, DWAB | - | - | Encode/decode |
| HTJ2K256, HTJ2K32 | Encode/decode | Encode/decode | Encode/decode |

## Default Results (2026-07-26)

These are same-machine results, not cross-machine performance claims:

- CPU: Intel Core i7-13700K, 16 physical cores / 24 logical processors.
- OS: Windows 11 25H2, build 10.0.26200.8875, x64.
- Managed: .NET SDK 10.0.302, .NET 10.0.10, BenchmarkDotNet 0.15.8,
  concurrent workstation GC, `DefaultJob`. Workload warmup ranged from 6 to
  12 iterations and every case retained 15 measurement samples.
- Native: clang-cl 22.1.3, `/O2 /Ob3 -march=native`, loop/SLP vectorization,
  IPO, Google Benchmark 1.9.5, five repetitions with a 0.5-second minimum.
- Libraries: TinyEXR `v3.2.0-38-g1b10661`, vendored libdeflate level 4, and
  OpenEXR 3.4.13.

Each timing cell is `mean milliseconds / raw MiB per second`. Managed allocation
is binary MiB per operation. Lower time and higher throughput are better.

### Encode

| Compression | TinyEXR.NET v3 ms / MiB/s | Managed alloc MiB | TinyEXR v3 C ms / MiB/s | OpenEXR ms / MiB/s |
| --- | ---: | ---: | ---: | ---: |
| None | 5.89 / 2686.55 | 33.33 | 6.71 / 2356.26 | 6.82 / 2320.76 |
| RLE | 16.54 / 956.57 | 21.72 | 17.07 / 926.87 | 16.22 / 975.14 |
| ZIPS | 17.88 / 884.94 | 33.94 | 24.72 / 639.87 | 37.06 / 427.15 |
| ZIP | 11.08 / 1427.57 | 33.21 | 19.64 / 805.35 | 22.75 / 695.36 |
| PIZ | 38.99 / 405.80 | 22.25 | 49.72 / 318.21 | 40.49 / 390.75 |
| PXR24 | 15.91 / 994.41 | 33.18 | 16.17 / 978.74 | 19.63 / 805.77 |
| B44 | 29.26 / 540.66 | 25.73 | 16.67 / 949.20 | 16.92 / 935.02 |
| B44A | 31.04 / 509.67 | 22.89 | 18.41 / 865.73 | 15.91 / 994.54 |
| DWAA | - | - | - | 63.33 / 249.82 |
| DWAB | - | - | - | 46.94 / 337.05 |
| HTJ2K256 | 104.28 / 151.71 | 54.09 | 47.77 / 331.16 | 30.35 / 521.28 |
| HTJ2K32 | 104.95 / 150.74 | 27.76 | 39.31 / 402.41 | 52.44 / 301.72 |

### Decode

All shared rows use the TinyEXR.NET-produced bytes described in the size table.

| Compression | TinyEXR.NET v3 ms / MiB/s | Managed alloc MiB | TinyEXR v3 C ms / MiB/s | OpenEXR ms / MiB/s |
| --- | ---: | ---: | ---: | ---: |
| None | 3.46 / 4577.67 | 16.28 | 4.44 / 3566.27 | 3.28 / 4828.24 |
| RLE | 7.37 / 2146.88 | 16.30 | 8.03 / 1969.28 | 14.74 / 1073.55 |
| ZIPS | 7.54 / 2097.36 | 16.62 | 9.45 / 1674.98 | 7.70 / 2054.39 |
| ZIP | 5.45 / 2904.55 | 16.69 | 6.43 / 2460.06 | 4.55 / 3473.82 |
| PIZ | 28.59 / 553.43 | 18.46 | 24.71 / 640.30 | 15.01 / 1053.96 |
| PXR24 | 12.69 / 1247.01 | 16.69 | 7.60 / 2082.61 | 5.26 / 3009.88 |
| B44 | 17.23 / 918.24 | 17.62 | 11.44 / 1419.70 | 9.27 / 1706.89 |
| B44A | 13.89 / 1138.89 | 17.49 | 8.44 / 1875.35 | 8.54 / 1852.64 |
| DWAA | - | - | - | 15.38 / 1028.57 |
| DWAB | - | - | - | 19.47 / 812.46 |
| HTJ2K256 | 82.23 / 192.40 | 47.28 | 33.53 / 471.83 | 24.07 / 657.37 |
| HTJ2K32 | 81.37 / 194.43 | 26.03 | 24.45 / 647.10 | 39.86 / 396.88 |

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

### Findings

- Managed encode is the fastest of the three implementations for ZIP, ZIPS,
  PIZ, and None. ZIP takes 56% of TinyEXR v3 C time and 49% of OpenEXR time;
  ZIPS takes 72% and 48%; PIZ takes 78% and 96%; None takes 88% and 86%.
  PXR24 and RLE encode are within a few percent of both native libraries.
- Managed decode leads on RLE and ZIPS. RLE takes 92% of TinyEXR v3 C time and
  50% of OpenEXR time; ZIPS takes 80% and 98%. None and ZIP decode beat
  TinyEXR v3 C but remain behind OpenEXR.
- B44/B44A are the largest non-HTJ2K gap: encode takes 1.76x/1.69x the
  TinyEXR v3 C time and 1.73x/1.95x the OpenEXR time, with decode at
  1.51x/1.65x and 1.86x/1.63x.
- PXR24 and PIZ decode trail both native libraries. PXR24 decode takes 1.67x
  the TinyEXR v3 C time and 2.41x the OpenEXR time; PIZ decode takes 1.16x
  and 1.90x. The remaining PIZ gap traces to the `FastHufDecoder` structure in
  OpenEXR `internal_huf.c`.
- HTJ2K is the weakest area. HTJ2K256 encode/decode takes 2.18x/2.45x the
  TinyEXR v3 C time and 3.44x/3.42x the OpenEXR time; HTJ2K32 takes 2.67x/3.33x
  and 2.00x/2.04x.

## Managed Allocation

Block decode and encode run through instance-scoped pools and codec workspaces,
as described in `docs/tinyexr-v3.md`. Non-HTJ2K decode allocates 16.28 to
18.46 MiB against the 15.82 MiB materialized payload, so allocation is close to
the result itself. Encode ranges from 21.72 to 33.94 MiB.

HTJ2K is bounded by its scalar entropy and transform code rather than by
allocation, and its per-operation pool cannot amortize across separate calls the
way a process-wide pool would. It allocates 54.09/47.28 MiB for HTJ2K256
encode/decode and 27.76/26.03 MiB for HTJ2K32.

Per-operation allocation can also be measured directly with
`--profile-v3-compression <op> <codec> 30`.

## Verification

The report run completed all 64 expected comparison rows without failures: 20
managed, 20 TinyEXR v3 C, and 24 OpenEXR. Additional verification on the same
revision:

- clang-cl built the complete v3 target and both benchmark executables.
- Default managed test host: 248/248 passed.
- `netstandard2.1` fallback host: 248/248 passed.
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
