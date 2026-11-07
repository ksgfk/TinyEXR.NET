# Compression benchmarks

Only TinyEXR.NET and **official OpenEXR 3.5.3** are measured. The native target is
`openexr_compression_benchmark`. Its library and fixed dependencies come from
`Scripts/cmake/OpenEXR.cmake`, shared with the correctness oracle. TinyEXR native
benchmarks and all historical three-library timing tables have been removed.

## Reproduce the comparison

```powershell
pwsh ./Benchmark/run-compression-benchmarks.ps1 -Job Default
```

The script uses Visual Studio clang-cl on Windows. `Default` uses ordinary
BenchmarkDotNet calibration and Google Benchmark's five repetitions with a
0.5-second minimum. `Short` and `Dry` are diagnostics, not performance claims.
On other systems build `Benchmark/baseline` directly with CMake and GCC/Clang.

Generated artifacts live under ignored `.cache` and `artifacts` directories:
`managed/results`, `openexr.json` and `comparison.csv` (20 managed and 24 official
rows). No TinyEXR native result is produced. The official DWA-only rows are labeled
unsupported on the managed side; LJ2K/ZSTD are outside this benchmark matrix.

## Timing contract

Both implementations process the same deterministic 1920x1080 RGBA HALF image,
one worker thread, in memory. Shared decode rows consume exactly the same managed
encoded input. DWA-only official rows consume official output. Encode sizes refer
to each implementation's own file. No compression-byte equality is assumed.

Encode includes allocating the result and producing a complete EXR. Decode
includes allocating and materializing all output pixels. Input preparation,
headers, fixture I/O, validation and result release are outside timing. OpenEXR
allocates fresh result buffers inside the timer, while reusing its stream adapter;
stream callbacks remain timed. Settings and the 15.82 MiB raw payload are shared.
Managed API context reuse is not enabled for this whole-image comparison.

## Optimization admission

Pure optimizations require at least 5% throughput improvement or 20% allocation
reduction in the target scenario, with no representative scenario consistently
more than 5% slower. These thresholds apply to controlled before/after runs,
never CI timing gates. Codec microbenchmarks use BenchmarkDotNet's own warmup,
iteration calibration, statistical reports and MemoryDiagnoser:

```powershell
dotnet build Benchmark/TinyEXR.Benchmark -c Release -p:SignAssemblyKey=false
dotnet run --project Benchmark/TinyEXR.Benchmark -c Release --no-build -- --filter '*CodecBenchmarks*' '*Htj2kDecodeBenchmarks*'
```

`CodecBenchmarks` covers sparse/dense HALF blocks at widths 128/512 for RLE/PIZ
encode and decode. `Htj2kDecodeBenchmarks` covers widths 128/512 and heights
32/256. Each benchmark owns its reusable warmed workspace; fixture preparation
and correctness checks happen in GlobalSetup. Add `--job Dry` to check execution.
These 20 microbenchmark cases are separate from the unchanged 44-row end-to-end
comparison. No hand-written timing loop or correctness test runner remains.
Fixture preparation is still available with `--prepare-v3-compression-fixtures`.

## V3.1 optimization measurements (2026-11-07)

Local Windows x64, Core i7-13700K, .NET 10.0.14, Release, tiered compilation off.
The RLE/PIZ comparison used the prior managed implementation (`e8e3642`) and the
new implementation in one process pinned to logical CPU 2, alternating order
across eleven 96-operation batches after 128 warmups. Medians are reported.
HTJ2K compared adjacent before/after processes with 64 warmups and nine batches
of 64 operations. This historical measurement predates the framework migration;
the temporary probes have been retired. New measurements use BenchmarkDotNet.
Raw historical results remain in ignored local artifacts.

| Change | Target measured benefit | Allocation per operation |
| --- | --- | --- |
| RLE Span packing and literal boundaries | Encode throughput +16.4% to +49.1% across four block scenarios | 2,976→1,048; 41,032→8,288; 12,240→4,120; 164,152→33,056 bytes |
| PIZ sparse Huffman/lookup and layout scratch | Decode throughput +62.0% to +143.3% | 848→0 bytes after workspace warmup |
| HTJ2K decode scratch/band reuse | Allocation reduction 60.5% to 83.3% | 52,096→20,552; 101,904→23,600; 89,080→21,016; 155,904→26,072 bytes |

Other RLE/PIZ probe scenarios stayed within a 2.1% throughput reduction; the
HTJ2K adjacent comparison stayed within a 1.6% time increase. These changes pass
the agreed local admission thresholds. The official/managed `Dry` comparison
only checks the benchmark pipeline and is not used for these performance claims.
