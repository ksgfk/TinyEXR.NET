# Test Layout

This directory validates that the two target framework outputs of `TinyEXR.NET` behave consistently in this repository.

The current test strategy is shaped by these constraints:

- Test cases must stay identical across targets.
- All correctness tests use `MSTest.Sdk 4.4.1` and Microsoft.Testing.Platform, pinned in `global.json`.
- The `MSTest 4` test host itself runs on `net10.0`.
- We need separate coverage for the default high-version implementation of `TinyEXR.NET` and the `netstandard2.1` fallback implementation.

The shared library suite uses two test projects backed by one shared codebase.
OpenEXR interoperability, whole-file benchmark adapters and package/NativeAOT consumption have separate MSTest
projects. Test entry points and results are provided by the framework; no custom
console test runner is used. Performance measurements live under `Benchmark/`.

## Projects

### `TinyEXR.Test`

- Target framework: `net10.0`
- Reference style: regular `ProjectReference`
- Purpose: validate the default target framework output of `TinyEXR.NET`

This is the primary test entry point. It consumes the higher target framework output of `TinyEXR.NET` through the normal SDK / NuGet asset selection rules.

### `TinyEXR.NetStandardFallback.Test`

- Target framework: `net10.0`
- Reference style: `ProjectReference` forced to the `netstandard2.1` target of `TinyEXR.NET`
- Purpose: validate the `netstandard2.1` fallback implementation of `TinyEXR.NET`

This project uses the following reference shape:

```xml
<ProjectReference Include="..\..\TinyEXR.NET\TinyEXR.NET.csproj"
                  SetTargetFramework="TargetFramework=netstandard2.1"
                  SkipGetTargetFrameworkProperties="true" />
```

This is an engineering workaround for the current constraints, not the standard way to consume multiple target frameworks in tests.

This project also declares an explicit dependency on:

```xml
<PackageReference Include="SharpZipLib" Version="1.4.2" />
```

The reason is that `TinyEXR.NET` only depends on `SharpZipLib` under `netstandard2.1`. With the current `ProjectReference` approach that force-selects a child target framework, the test project's restore / runtime dependency graph does not reliably bring that dependency along automatically. It is declared explicitly here so the test host does not miss the compression library required by the `netstandard2.1` branch.

### `TinyEXR.Test.Shared`

- Type: shared test source
- Purpose: imported by both test projects

All shared library test cases live here and are imported into both test projects through `.projitems`, ensuring that both sides execute the same test logic.

## Why Two Test Projects

The repository does not currently use a single multi-targeted test project for both low and high framework consumers. The reason is not test sharing, but the current toolchain choice:

- `MSTest 4`
- `Microsoft.Testing.Platform`

With that combination, the test host is not set up to cover both the lower-version consumer we need and the `net10.0` consumer at the same time. As a result, the repository uses:

- `TinyEXR.Test` for the default high-version output
- `TinyEXR.NetStandardFallback.Test` for the `netstandard2.1` fallback output

In other words, the current strategy does not try to validate behavior differences between test host frameworks. It validates that the same test suite passes against both target framework implementations of the library.

## Test Data

The tests depend on two external data sources:

- The official OpenEXR sample repository `openexr-images`
- The regression samples bundled with `.cache/tinyexr`

The `openexr-images` repository must be prepared under:

- `.cache/openexr-images`

Preparation script:

```powershell
pwsh .\Scripts\prepare-openexr-images.ps1
pwsh .\Scripts\prepare-tinyexr.ps1
```

Both scripts pin their source repositories so test inputs stay stable. The TinyEXR pin is centralized in Scripts/tinyexr-source.json; tests only read the prepared cache.

The suite uses three complementary kinds of test input:

- Pinned EXR files from `openexr-images` and `.cache/tinyexr` for
  decoder, metadata, multipart, multi-resolution, deep, HTJ2K, damaged-file,
  and round-trip compatibility tests.
- Valid EXR files generated in memory by the managed writer for writer,
  facade, deep, multipart, streaming, and asynchronous scenarios.
- Focused byte buffers and mock data sources for frame parsing, malformed
  input, limits, cancellation, `WouldBlock`, SIMD parity, and image-processing
  algorithms where a standalone EXR file would not improve the assertion.

Passing the suite therefore does not mean that every feature has a dedicated
static `.exr` fixture. Runtime-generated files and lower-level vectors are used
where they provide more precise coverage.

## How To Run

Prepare the test data first:

```powershell
pwsh .\Scripts\prepare-openexr-images.ps1
pwsh .\Scripts\prepare-tinyexr.ps1
```

Run the default target framework tests:

```powershell
dotnet test --project .\Test\TinyEXR.Test\TinyEXR.Test.csproj
```

Run the `netstandard2.1` fallback tests:

```powershell
dotnet test --project .\Test\TinyEXR.NetStandardFallback.Test\TinyEXR.NetStandardFallback.Test.csproj
```

If both need to run, it is better to execute them explicitly instead of only running the solution. That makes failures easier to localize and makes it clearer whether the regression is in the default implementation or the fallback branch.

## Coverage and limits

Official OpenEXR 3.5.3 independently generates and decodes HTJ2K, every supported
codec, multipart/multilevel files and complete deep sample arrays. The CI oracle
matrix includes x64/ARM64, both managed targets and a hardware-intrinsics-disabled
run. Shared SIMD parity tests also execute on the six-platform test matrix.
No native TinyEXR result or known-failure exemption determines correctness.

Resource regressions include context occupancy, total multipart/deep budgets,
unknown-length and partial sources, exact file extents, damaged Preview data,
spectral selection and expansion, malformed headers, chunk identity and lengths.
The structural tests reject legacy exports, V1 source references and unsafe code.
There is no numeric coverage gate; passing tests do not prove every hostile-input
path has been explored.

## Known Tradeoffs

- The current fallback test strategy depends on the `SetTargetFramework` trick on `ProjectReference`, which is less maintainable than a standard multi-target test project.
- `TinyEXR.NetStandardFallback.Test` must declare `SharpZipLib` explicitly, or the test host may miss the dependency required by the `netstandard2.1` branch.
- This strategy focuses on whether the two library outputs behave consistently. It is not the same as validating natural asset selection on multiple real consumer runtimes.

## Summary

The intent of the current test structure is straightforward:

- One shared test suite
- Two separate test hosts
- Separate coverage for the default `TinyEXR.NET` output and the `netstandard2.1` fallback output

If both pass, the two main target framework implementations in this repository are consistent within the current test coverage.

## Package and NativeAOT smoke test

`TinyEXR.PackageSmoke` is a standalone consumer of the packed `TinyEXR.NET`
1.2.0 package, not a project reference. It intentionally stays outside the
solution so ordinary solution restore does not require an unpublished package.
Its 14 independently reported cases check models and I/O, spectral helpers, all
ten supported flat codecs, stream-origin restoration, contexts, metadata, file
budgets and the managed API 3.1 constant. The same source-generated test methods
and explicit DataRows run in ordinary .NET and NativeAOT; there is no manual Main.

```sh
dotnet pack TinyEXR.NET/TinyEXR.NET.csproj -c Release -p:SignAssemblyKey=false -p:GeneratePackageOnBuild=false -o artifacts/packages
dotnet restore Test/TinyEXR.PackageSmoke/TinyEXR.PackageSmoke.csproj -r linux-x64 -p:PublishAot=true --configfile Test/TinyEXR.PackageSmoke/NuGet.config --packages artifacts/consumer-packages
dotnet publish Test/TinyEXR.PackageSmoke/TinyEXR.PackageSmoke.csproj -c Release -r linux-x64 -p:PublishAot=true --no-restore -o artifacts/aot-smoke -p:RestorePackagesPath="$PWD/artifacts/consumer-packages"
dotnet test --project Test/TinyEXR.PackageSmoke/TinyEXR.PackageSmoke.csproj -c Release -r linux-x64 --no-restore -p:RestorePackagesPath="$PWD/artifacts/consumer-packages" --report-trx --results-directory artifacts/test-results/package
dotnet test --test-modules ./artifacts/aot-smoke/TinyEXR.PackageSmoke --report-trx --results-directory artifacts/test-results/aot
```

The `package-aot-smoke` CI job runs this on Ubuntu with the NativeAOT compiler
prerequisites. The smoke-specific NuGet configuration maps `TinyEXR.NET` only
to the locally generated package feed; use a fresh isolated consumer cache when rebuilding the unchanged package version,
so a previous local 1.2.0 package is not reused. On Windows use `win-x64` and the
NativeAOT `.exe` path instead. All test runs produce TRX reports. These unsigned checks do not validate the publisher's private
strong-name signing key or signed release artifacts.

## Pinned official OpenEXR interoperability

`NativeReference` builds OpenEXR 3.5.3 at
`3950290530c4fd4776667d0dc4d65a9d003eb936` using the same dependency configuration
as Benchmark. See [codec provenance](../docs/codec-port.md) for build commands.
`TinyEXR.Interop` discovers 427 MSTest cases: 288 canonical codec cases, 133
full-file/fixture cases, two exhaustive B44 tables, one rejected tile fixture and
three oracle-infrastructure cases. Every matrix row is separately filterable and
reported; the two table cases cover all 131,072 entries. The native adapter dumps every part, level,
channel, deep count and sample. Lossless bytes must match; lossy checks compare
same-file dual decoding and official quantization. B44 also covers all HALF inputs.

Retained TinyEXR fixtures are checked against official decoding. The oversized
`tiled_htj2k256.exr` fixture is now a rejection regression: both OpenEXR and the
managed reader reject its 139-byte packed tile for a 2-byte canonical tile.
There are no allowances for the old deep encoder's bugs. Multirow, multichannel
deep tiles are checked in both directions using only legal NONE/RLE/ZIPS tags.

```sh
cmake -S Test/NativeReference -B .cache/openexr-reference -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build .cache/openexr-reference --parallel 4
dotnet test --project Test/TinyEXR.Interop/TinyEXR.Interop.csproj -c Release -p:SignAssemblyKey=false --report-trx --results-directory artifacts/test-results/interop
dotnet test --project Test/TinyEXR.Interop/TinyEXR.Interop.csproj -c Release -p:SignAssemblyKey=false -p:LibraryFramework=netstandard2.1 --report-trx --results-directory artifacts/test-results/interop-fallback
```

Set `DOTNET_EnableHWIntrinsic=0` to repeat the net8.0 matrix using scalar code.
Native dependencies are not shipped with the library. Package/AOT smoke checks
also exercise V3.1 context reuse, Preview metadata, file limits and API version.

The default oracle directory is `.cache/openexr-reference` under the repository;
its `Release` child is also supported. Set `TINYEXR_OPENEXR_REFERENCE_DIR` to
override it (relative paths are resolved from the repository). Missing tools fail
tests rather than triggering a build or a skip. Build native dependencies before
running interoperability, including when using `dotnet test --solution`.

Cases run serially with separate working directories. Oracle stdout/stderr are
captured concurrently and each invocation has a 60-second timeout. Failed case
files and invocation logs are attached to TRX results; successful workspaces are
removed. CI uploads reports and failure attachments for each matrix entry.

Discover tests or select a method with the framework:

```sh
dotnet test --project Test/TinyEXR.Interop/TinyEXR.Interop.csproj -c Release -p:SignAssemblyKey=false --list-tests
dotnet test --project Test/TinyEXR.Interop/TinyEXR.Interop.csproj -c Release -p:SignAssemblyKey=false --filter "Name=OversizedHtjTileIsRejectedByBothReaders"
```

For one parameterized row, use MTP's `--filter-uid` with the row's `testId` from
a previous TRX report (MSTest's Name filter addresses the method, not its arguments):

```powershell
$report = [xml](Get-Content artifacts/test-results/interop/*.trx -Raw)
$caseId = ($report.TestRun.Results.UnitTestResult | Where-Object testName -eq 'CanonicalBlockMatchesOfficialCodec (RLE,2,2,False,0)').testId
dotnet test --project Test/TinyEXR.Interop/TinyEXR.Interop.csproj -c Release -p:SignAssemblyKey=false --filter-uid $caseId --minimum-expected-tests 1
```

## Whole-image benchmark adapter tests

Prepare source with 'pwsh Scripts/prepare-tinyexr.ps1'. The cached checkout supplies
the same retained TinyEXR regression samples for both managed hosts and official
interop tests; tests do not download or compile it. Missing files fail with a
preparation hint. Official OpenEXR remains the only source of expected samples.

The independent TinyEXR.Benchmark.Test project uses MSTest 4.4.1. Run
'pwsh Benchmark/run-compression-benchmarks.ps1 -Job Dry' to build the native tools,
prepare official whole-file inputs, run adapter tests and verify 64 report items.
After preparation:

    dotnet test --project Test/TinyEXR.Benchmark.Test/TinyEXR.Benchmark.Test.csproj -c Release -p:SignAssemblyKey=false --report-trx

All 32 library/compression pairs have separate Encode and Decode tests. TinyEXR
official-input HTJ2K32/256 Decode must return Unsupported (-3); other errors fail.
Native calls use separate `openexr_file_tool` and `tinyexr_file_tool` executables,
each linked only to its own library and dependencies. Only the OpenEXR tool can
prepare the shared official fixtures. MSTest checks tool selection, missing tools,
and rejects the old runtime backend selector; the combined tool is no longer used.
Tests verify complete HALF planes, shared input hashes, official reads of saved
files, corrupt input, missing tools, process errors/timeouts and report integrity.
Failed cases attach inputs and concurrently captured stdout/stderr; calls have a
60-second deadline. See [Benchmark timing contract](../Benchmark/README.md).
