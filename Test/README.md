# Test Layout

This directory validates that the two target framework outputs of `TinyEXR.NET` behave consistently in this repository.

The current test strategy is shaped by these constraints:

- Test cases must stay identical across targets.
- The repository currently uses `MSTest 4`.
- The `MSTest 4` test host itself runs on `net10.0`.
- We need separate coverage for the default high-version implementation of `TinyEXR.NET` and the `netstandard2.1` fallback implementation.

Given those constraints, the current layout uses two test projects backed by one shared test codebase.

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

All actual test cases live here and are imported into both test projects through `.projitems`, ensuring that both sides execute the same test logic.

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
- The regression samples bundled with `TinyEXR.Native/tinyexr`

The `openexr-images` repository must be prepared under:

- `.cache/openexr-images`

Preparation script:

```powershell
pwsh .\Scripts\prepare-openexr-images.ps1
```

The script pins the data repository to a specific commit so test inputs stay stable.

The suite uses three complementary kinds of test input:

- Pinned EXR files from `openexr-images` and `TinyEXR.Native/tinyexr` for
  decoder, metadata, multipart, multi-resolution, deep, HTJ2K, damaged-file,
  and round-trip compatibility tests.
- Valid EXR files generated in memory by the managed writer for writer,
  facade, ZSTD, deep, multipart, streaming, and asynchronous scenarios.
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

## Known Coverage Gaps

- There is no independently generated static ZSTD fixture matrix covering
  scanline, tiled, deep, and multipart EXR files. Current ZSTD coverage uses
  managed-writer output plus frame-level golden and malformed vectors. This is
  strong format coverage, but it does not independently rule out a symmetric
  managed encoder/decoder error.
- Managed HTJ2K and ZSTD encoder output has been checked manually with the
  upstream TinyEXR v3 C reader, but that cross-implementation check is not yet
  an automated MSTest or CI job. An integration job should build the upstream
  `parse_harness` and `compare_exr` tools, decode managed output, and compare it
  with an uncompressed reference.
- ARM64 SIMD implementations are not executed by the normal x64 test hosts.
  The suite verifies scalar parity and dispatch behavior available on the
  current architecture, but an ARM64 runner is required to execute the ARM64
  intrinsics themselves.
- The repository has no numeric coverage gate and no maintained public
  API-to-test traceability matrix. The suite covers the main supported behavior
  and regression surface, but a passing run is not proof that every public
  overload, platform branch, or hostile-input path has been exercised.
- Not every feature should require a standalone image fixture. Object-model
  validation, streaming state machines, error mapping, SIMD kernels, color
  transforms, resize filters, tone mapping, and LUT evaluation are primarily
  algorithmic and are intentionally tested with focused buffers. What remains
  missing is independent file-level coverage where interoperability matters,
  especially for newly encoded compression formats.

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
