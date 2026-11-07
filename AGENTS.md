# Repository Guidelines

## Project Structure & Module Organization

`TinyEXR.NET/` contains the managed library; `Compression/` holds the shared V3 codecs; `Format/`, `Reader/`, and `Writer/` own format validation and streaming I/O. The library targets `net8.0` and `netstandard2.1`. Shared MSTest cases live in `Test/TinyEXR.Test.Shared/` and are imported by the default and fallback test hosts under `Test/`. `samples/TinyEXR.Viewer/` is the Avalonia inspection app, while `Benchmark/` contains BenchmarkDotNet and C++ baseline projects. `Scripts/` prepares external fixtures. Pinned upstream TinyEXR source and regression data are prepared in .cache/tinyexr; never modify that checkout. Official OpenEXR is the sole correctness oracle. The three-way Benchmark uses whole-image memory APIs, with 62 measured items and two explicit TinyEXR HTJ2K Decode Unsupported results.

Do not commit generated directories such as `.cache/`, `bin/`, `obj/`, `TestResults/`, `artifacts/`, or `BenchmarkDotNet.Artifacts/`.

## Build, Test, and Development Commands

Use the .NET 10 SDK for the full solution and prepare cached regression sources after cloning:

```powershell
pwsh .\Scripts\prepare-tinyexr.ps1
dotnet restore .\TinyEXR.NET.sln
dotnet build .\TinyEXR.NET.sln -p:SignAssemblyKey=false
```

Prepare pinned OpenEXR fixtures with `pwsh .\Scripts\prepare-openexr-images.ps1`. Then run both compatibility paths explicitly:

```powershell
dotnet test --project .\Test\TinyEXR.Test\TinyEXR.Test.csproj -p:SignAssemblyKey=false
dotnet test --project .\Test\TinyEXR.NetStandardFallback.Test\TinyEXR.NetStandardFallback.Test.csproj -p:SignAssemblyKey=false
```

Launch the sample with `dotnet run --project .\samples\TinyEXR.Viewer\TinyEXR.Viewer.csproj`. Release builds require a private strong-name key unless signing is disabled as above.

## Coding Style & Naming Conventions

Follow existing C# style: four-space indentation, braces on new lines, nullable annotations enabled, and implicit usings. Use PascalCase for types and members, camelCase for locals and parameters, and match primary type and filename. Keep public API changes consistent with the documented V3 contract and official OpenEXR format rules and avoid unsafe code. No repository-specific formatter configuration is checked in; preserve nearby formatting and keep warnings clean.

## Testing Guidelines

The suite uses MSTest 4.4.1 / Microsoft.Testing.Platform. Correctness belongs in discoverable tests, never hand-written runners. Test/TinyEXR.Benchmark.Test validates whole-file adapters through separate `openexr_file_tool` and `tinyexr_file_tool` executables; never link both EXR libraries into one tool. Prepare both tools and official inputs with the Benchmark script before running it. Add reusable cases to `Test/TinyEXR.Test.Shared/` so both target outputs receive identical coverage. Name test files `*Tests.cs`; use descriptive method names and `DisplayName` values that identify the fixture and operation. There is no numeric coverage gate, but fixes should include regression tests and pass both hosts.

## Commit & Pull Request Guidelines

History favors short, imperative subjects such as `Fix scanline decode lineOrder semantics`. Keep commits focused. Pull requests should explain behavior and compatibility impact, link relevant issues, list commands run, and update tests or README documentation for API changes. Include screenshots only for viewer-facing changes.
