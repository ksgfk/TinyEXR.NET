# Whole-image memory throughput benchmarks

The main matrix compares **TinyEXR.NET**, **TinyEXR V3** at
`eb0ba07f44a33e1ce5bc0fb74142a3a6558f0661`, and **official OpenEXR 3.5.3** at
`3950290530c4fd4776667d0dc4d65a9d003eb936`. Official OpenEXR alone determines
correctness. TinyEXR uses its bundled default libdeflate backend; OpenEXR and
its dependencies use the same pins as the interoperability oracle.

## Run

    pwsh ./Benchmark/run-compression-benchmarks.ps1 -Job Default
    pwsh ./Benchmark/run-compression-benchmarks.ps1 -Job Dry -OutputDirectory artifacts/whole-image-dry

The script prepares pinned TinyEXR source, builds both frameworks, creates official
input files, runs MSTest adapter tests, measures, and validates the complete report.
It uses Visual Studio clang-cl on Windows, or Ninja and the selected CMake compiler
on other platforms. Both native libraries use one toolchain and Release flags.
A correct `.cache/tinyexr` checkout is reused without network access; an incorrect
or modified cache is rejected without overwriting it. Standalone CMake requires
`pwsh Scripts/prepare-tinyexr.ps1` first. Sources and dependencies remain outside
the package. Private libdeflate symbol names prevent one native backend from
silently replacing the other.

`Default` uses BenchmarkDotNet calibration and Google Benchmark warmup,
calibration and five repetitions (0.5-second minimum). `Short` is diagnostic;
`Dry` checks execution only. Neither supports a performance conclusion.
There is no Codec/File suite switch. Old block or differently scoped measurements
must not be combined with these results.

## Timing contract

Each operation processes the same deterministic **1920 × 1080 RGBA HALF**,
single-part scanline image, with increasing Y and canonical A/B/G/R channels.
All three run on one CPU thread with their normal SIMD optimizations and default
compression settings. Settings, compiler, CPU, runtime and source commits are
recorded in the framework JSON and environment.json.

| Operation | Starting state | Completed result |
| --- | --- | --- |
| Decode | Complete official EXR bytes already in memory | All four HALF planes materialized |
| Encode | Prepared image, channels and pixels | Complete EXR with independently owned memory |

Timed work includes opening memory input, parsing headers and indexes, format
validation, codec work, necessary copies, internal allocations and file assembly.
Disk I/O, preparation, verification, reporting and explicit caller result release
are outside timing. Native benchmark processes call their libraries directly;
no subprocess starts inside a measured operation.

Managed calls use ExrFile.LoadFromMemory/SaveToMemory. TinyEXR uses its public V3
memory APIs with a null context. OpenEXR uses InputFile/OutputFile, planar HALF
frame buffers and memory streams; no RGBA conversion is added. Every call creates
new read/write state and result storage. OpenEXR output grows from an empty vector;
it neither reserves a known encoded length nor reuses previous capacity. Internal
allocations stay timed. Managed GC remains part of the framework measurement and
is not represented as pure codec computation.

The preparation tool encodes all 12 complete files with official OpenEXR. Every
Decode uses those exact bytes. The manifest contains image layout, source version,
lengths and FNV-1a-64 integrity checks for files and fully decoded samples. These
checks run outside timing. Encode sizes are each library's own output; Decode
sizes are the common official input. Compression levels with the same number in
different backends are not assumed equivalent.

## Results and unsupported cases

The summary always has **64 items: 62 measured, 2 Unsupported**:

- TinyEXR.NET: 20 measured.
- TinyEXR: 18 measured; official HTJ2K32/256 Decode returns Unsupported (-3) for
  multiple tile-parts. These two entries retain the diagnostic and have empty
  time and throughput fields. TinyEXR HTJ2K Encode is measured and officially validated.
- OpenEXR: 24 measured, including its exclusive DWAA/DWAB entries.

The shared compressions are NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44, B44A,
HTJ2K256 and HTJ2K32. LJ2K/ZSTD are outside the matrix. NONE is the uncompressed
baseline. Any other failure, missing key or duplicate makes reporting fail.

Output contains managed framework reports, tinyexr.json, openexr.json,
environment.json, input-manifest.csv, MSTest TRX/attachments, and comparison.csv. The primary metric
is **raw pixel MiB/s = 16,588,800 / mean seconds / 1,048,576**. Also reported:
images/second, milliseconds/image, complete EXR bytes, raw/file compression ratio
and managed allocated bytes. Native allocations are unavailable (empty), not zero.
Interpret speed together with output size and compression settings.

## Correctness and separate diagnostics

    dotnet test --project Test/TinyEXR.Benchmark.Test/TinyEXR.Benchmark.Test.csproj -c Release -p:SignAssemblyKey=false --report-trx

Tests require prepared fixtures and file_fixture_tool; they never download or
build dependencies. Set TINYEXR_BENCHMARK_NATIVE_DIR for another build directory
(the Release subdirectory is also recognized). Each test has a separate work
directory. Native stdout/stderr are drained concurrently; calls time out after
60 seconds. Failures retain input and log attachments. Lossless samples compare
byte-for-byte; lossy samples compare official and tested decoding of the same file.
The two HTJ2K rejection tests assert the real Unsupported status.

Existing RLE/PIZ/HTJ2K microbenchmarks remain independent optimization diagnostics:

    dotnet run --project Benchmark/TinyEXR.Benchmark -c Release --no-build -- --filter `*CodecBenchmarks*` `*Htj2kDecodeBenchmarks*` --job Dry

These 20 cases may reuse warmed codec workspaces and are never mixed into the
whole-image table. Pure optimizations still require at least 5% target throughput
improvement or 20% allocation reduction, without stable representative regressions
over 5%. These are controlled experiment criteria, not CI timing gates.
