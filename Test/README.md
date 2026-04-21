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
