[CmdletBinding()]
param(
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $Job = 'Default',
    [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$managedProject = Join-Path $repoRoot 'Benchmark/TinyEXR.Benchmark/TinyEXR.Benchmark.csproj'
$nativeBuild = Join-Path $repoRoot '.cache/benchmark-native-clang'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts/compression-benchmarks'
} elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}
$managedOutput = Join-Path $OutputDirectory 'managed'
New-Item -ItemType Directory -Force -Path $managedOutput | Out-Null
$comparisonPath = Join-Path $OutputDirectory 'comparison.csv'
# A failed rerun must not leave a previous success table looking current.
if (Test-Path -LiteralPath $comparisonPath) { Remove-Item -LiteralPath $comparisonPath }
if ($env:TINYEXR_BENCHMARK_COMPRESSION) {
    throw 'Unset TINYEXR_BENCHMARK_COMPRESSION before running the complete 64-item comparison.'
}
function Check-Exit([string] $Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed (exit $LASTEXITCODE)." }
}
function Native-Executable([string] $Name) {
    if ($IsWindows) { $Name += '.exe' }
    $path = Join-Path $nativeBuild $Name
    if (-not (Test-Path -LiteralPath $path)) { $path = Join-Path $nativeBuild "Release/$Name" }
    if (-not (Test-Path -LiteralPath $path)) { throw "Native tool missing: $path" }
    return $path
}
Push-Location $repoRoot
$previousNativeDir = $env:TINYEXR_BENCHMARK_NATIVE_DIR
try {
    & "$repoRoot/Scripts/prepare-tinyexr.ps1"
    & dotnet build $managedProject -c Release -p:SignAssemblyKey=false
    Check-Exit 'Managed benchmark build'
    if ($IsWindows) {
        $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
        $version = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Llvm.Clang -property installationVersion
        Check-Exit 'Visual Studio lookup'
        if (-not $version) { throw 'Install the Visual Studio clang-cl component.' }
        $major = [int]($version.Split('.')[0])
        $year = switch ($major) { 18 { '2026' } 17 { '2022' } default { throw "Unsupported Visual Studio $major" } }
        & cmake -S Benchmark/baseline -B $nativeBuild -G "Visual Studio $major $year" -A x64 -T ClangCL
    } else {
        & cmake -S Benchmark/baseline -B $nativeBuild -G Ninja -DCMAKE_BUILD_TYPE=Release
    }
    Check-Exit 'Native configure'
    & cmake --build $nativeBuild --config Release --target compression_benchmarks --parallel 4
    Check-Exit 'Native build'

    & (Native-Executable 'openexr_file_tool') prepare (Join-Path $repoRoot '.cache/compression-benchmarks/official')
    Check-Exit 'Official fixture preparation'
    Copy-Item -LiteralPath "$repoRoot/.cache/compression-benchmarks/official/manifest.csv" -Destination "$OutputDirectory/input-manifest.csv"
    & dotnet run -c Release --no-build --project $managedProject -- --prepare-v3-compression-fixtures
    Check-Exit 'Managed encode size preparation'
    $env:TINYEXR_BENCHMARK_NATIVE_DIR = $nativeBuild
    & dotnet test --project Test/TinyEXR.Benchmark.Test/TinyEXR.Benchmark.Test.csproj -c Release -p:SignAssemblyKey=false --minimum-expected-tests 85 --report-trx --report-trx-filename adapters.trx --results-directory "$OutputDirectory/tests"
    Check-Exit 'Whole-file adapter tests'

    $started = [DateTime]::UtcNow
    & dotnet run -c Release --no-build --project $managedProject -- --filter '*V3CompressionBenchmarks*' --job $Job --exporters JSON --artifacts $managedOutput
    Check-Exit 'Managed measurement'
    $reports = @(Get-ChildItem (Join-Path $managedOutput 'results') -Filter '*report-full-compressed.json' |
        Where-Object LastWriteTimeUtc -ge $started.AddSeconds(-1))
    if ($reports.Count -ne 1) { throw "Expected one current managed report, found $($reports.Count)." }

    $timing = switch ($Job) {
        'Dry' { @('--benchmark_min_time=1x', '--benchmark_repetitions=1') }
        'Short' { @('--benchmark_min_time=0.2s', '--benchmark_min_warmup_time=0.2', '--benchmark_repetitions=3') }
        'Default' { @('--benchmark_min_time=0.5s', '--benchmark_min_warmup_time=0.5', '--benchmark_repetitions=5') }
    }
    foreach ($name in @('tinyexr', 'openexr')) {
        & (Native-Executable "$($name)_compression_benchmark") @timing "--benchmark_out=$OutputDirectory/$name.json" --benchmark_out_format=json
        Check-Exit "$name measurement"
    }
    & dotnet run -c Release --no-build --project $managedProject -- --summarize-comparison $reports[0].FullName "$OutputDirectory/tinyexr.json" "$OutputDirectory/openexr.json" "$OutputDirectory/comparison.csv"
    Check-Exit 'Strict 64-item summary'

    [ordered]@{
        Job = $Job
        Scope = 'Whole-image memory API; 1920x1080 RGBA HALF; single part scanline; fresh state and result storage'
        Timing = 'Includes parsing, validation, allocation, codec and file assembly. Excludes disk I/O, preparation, validation of results, explicit caller release.'
        ManagedSettings = 'ZLibStream Optimal (.NET 10); normal SIMD and GC; no retained ExrContext'
        NativeSettings = 'See settings, compiler and optimization fields in native framework JSON; both use the same CMake toolchain'
        TinyEXR = (Get-Content Scripts/tinyexr-source.json -Raw | ConvertFrom-Json).commit
        OpenEXR = '3950290530c4fd4776667d0dc4d65a9d003eb936'
        Threads = 1
        Runtime = (& dotnet --info | Out-String)
        Environment = 'CPU and OS in framework JSON. Native allocated bytes are unmeasured, not zero.'
        Interpretation = if ($Job -eq 'Default') { 'Full benchmark job; compare only matching scope/settings.' } else { 'Pipeline/diagnostic run; not a performance conclusion.' }
    } | ConvertTo-Json -Depth 5 | Set-Content "$OutputDirectory/environment.json" -Encoding utf8
    Write-Host "Whole-image results: $OutputDirectory/comparison.csv (62 measurements, 2 Unsupported)"
}
finally {
    $env:TINYEXR_BENCHMARK_NATIVE_DIR = $previousNativeDir
    Pop-Location
}
