[CmdletBinding()]
param(
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $Job = 'Default',

    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$managedProject = Join-Path $repoRoot 'Benchmark\TinyEXR.Benchmark\TinyEXR.Benchmark.csproj'
$nativeSource = Join-Path $repoRoot 'Benchmark\baseline'
$nativeBuild = Join-Path $repoRoot '.cache\benchmark-native-clang'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\compression-benchmarks'
} elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$managedOutput = Join-Path $OutputDirectory 'managed'
$tinyExrOutput = Join-Path $OutputDirectory 'tinyexr.json'
$openExrOutput = Join-Path $OutputDirectory 'openexr.json'
New-Item -ItemType Directory -Force -Path $managedOutput | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Installer vswhere.exe was not found.'
}

$installationPath = & $vswhere `
    -latest `
    -products '*' `
    -requires Microsoft.VisualStudio.Component.VC.Llvm.Clang `
    -property installationPath
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installationPath)) {
    throw 'A Visual Studio installation with the clang-cl component was not found.'
}

$installationVersion = & $vswhere `
    -latest `
    -products '*' `
    -requires Microsoft.VisualStudio.Component.VC.Llvm.Clang `
    -property installationVersion
$visualStudioMajor = [int]($installationVersion.Split('.')[0])
$visualStudioYear = switch ($visualStudioMajor) {
    18 { '2026' }
    17 { '2022' }
    16 { '2019' }
    default { throw "Unsupported Visual Studio major version: $visualStudioMajor" }
}
$generator = "Visual Studio $visualStudioMajor $visualStudioYear"

$clangPath = Join-Path $installationPath 'VC\Tools\Llvm\x64\bin\clang-cl.exe'
if (-not (Test-Path -LiteralPath $clangPath)) {
    throw "The Visual Studio clang-cl executable was not found: $clangPath"
}

Push-Location $repoRoot
try {
    Write-Host "Building TinyEXR.NET benchmarks ($Job)..."
    & dotnet build $managedProject -c Release -p:SignAssemblyKey=false
    if ($LASTEXITCODE -ne 0) {
        throw 'The managed benchmark build failed.'
    }

    Write-Host 'Preparing shared TinyEXR.NET V3 fixtures...'
    & dotnet run -c Release --no-build --project $managedProject -- `
        --prepare-v3-compression-fixtures
    if ($LASTEXITCODE -ne 0) {
        throw 'V3 compression fixture preparation failed.'
    }

    Write-Host "Configuring native benchmarks with $generator and clang-cl..."
    & cmake -S $nativeSource -B $nativeBuild -G $generator -A x64 -T ClangCL
    if ($LASTEXITCODE -ne 0) {
        throw 'The native benchmark configuration failed.'
    }

    Write-Host 'Building the TinyEXR and OpenEXR benchmarks...'
    & cmake --build $nativeBuild `
        --config Release `
        --target compression_benchmarks `
        --parallel
    if ($LASTEXITCODE -ne 0) {
        throw 'The native benchmark build failed.'
    }

    Write-Host 'Running TinyEXR.NET V3 benchmarks...'
    $managedRunStartedUtc = [DateTime]::UtcNow
    & dotnet run -c Release --no-build --project $managedProject -- `
        --filter '*V3CompressionBenchmarks*' `
        --job $Job `
        --exporters JSON `
        --artifacts $managedOutput
    if ($LASTEXITCODE -ne 0) {
        throw 'The managed compression benchmark failed.'
    }

    $nativeExecutable = Join-Path $nativeBuild 'Release\openexr_compression_benchmark.exe'
    $nativeTimingArguments = switch ($Job) {
        'Dry' { @('--benchmark_min_time=1x', '--benchmark_repetitions=1') }
        'Short' { @('--benchmark_min_time=0.2s', '--benchmark_repetitions=3') }
        'Default' { @('--benchmark_min_time=0.5s', '--benchmark_repetitions=5') }
    }

    $tinyExrExecutable = Join-Path $nativeBuild 'Release\tinyexr_compression_benchmark.exe'
    Write-Host 'Running TinyEXR v3 C benchmarks...'
    & $tinyExrExecutable `
        @nativeTimingArguments `
        "--benchmark_out=$tinyExrOutput" `
        --benchmark_out_format=json
    if ($LASTEXITCODE -ne 0) {
        throw 'The TinyEXR v3 C compression benchmark failed.'
    }

    Write-Host 'Running OpenEXR benchmarks...'
    & $nativeExecutable `
        @nativeTimingArguments `
        "--benchmark_out=$openExrOutput" `
        --benchmark_out_format=json
    if ($LASTEXITCODE -ne 0) {
        throw 'The OpenEXR compression benchmark failed.'
    }

    $managedJson = Get-ChildItem `
        (Join-Path $managedOutput 'results') `
        -Filter '*report-full-compressed.json' |
        Where-Object { $_.LastWriteTimeUtc -ge $managedRunStartedUtc.AddSeconds(-1) } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $managedJson) {
        throw 'The managed benchmark JSON report was not produced.'
    }

    $rawByteCount = 1920 * 1080 * 4 * 2
    $fixtureDirectory = Join-Path $repoRoot '.cache\compression-benchmarks\v3-managed'
    $sharedCompressions = @(
        'None', 'RLE', 'ZIPS', 'ZIP', 'PIZ', 'PXR24', 'B44', 'B44A',
        'HTJ2K256', 'HTJ2K32', 'ZSTD'
    )
    $comparison = [Collections.Generic.List[object]]::new()
    $managedReport = Get-Content -Raw -LiteralPath $managedJson.FullName | ConvertFrom-Json
    $managedBenchmarks = @($managedReport.Benchmarks)
    $failedManagedBenchmarks = @($managedBenchmarks | Where-Object {
        $null -eq $_.Statistics -or [double]$_.Statistics.Mean -le 0
    })
    if ($managedBenchmarks.Count -ne 22 -or $failedManagedBenchmarks.Count -gt 0) {
        throw "The managed report is incomplete: $($managedBenchmarks.Count) rows, " +
            "$($failedManagedBenchmarks.Count) failed rows; expected 22 successful rows."
    }

    foreach ($benchmark in $managedBenchmarks) {
        $compression = $benchmark.Parameters.Substring('Compression='.Length)
        $fixturePath = Join-Path $fixtureDirectory "$($compression.ToLowerInvariant()).exr"
        $encodedBytes = (Get-Item -LiteralPath $fixturePath).Length
        $meanMilliseconds = [double]$benchmark.Statistics.Mean / 1e6
        $comparison.Add([pscustomobject][ordered]@{
            Implementation = 'TinyEXR.NET V3'
            Operation = $benchmark.Method
            Compression = $compression
            MeanMilliseconds = $meanMilliseconds
            RawMiBPerSecond = $rawByteCount / ($meanMilliseconds / 1000.0) / 1MB
            EncodedBytes = $encodedBytes
            CompressionRatio = $rawByteCount / $encodedBytes
            AllocatedBytes = $benchmark.Memory.BytesAllocatedPerOperation
            SharedDecodeInput = if ($benchmark.Method -eq 'Decode') {
                $sharedCompressions -contains $compression
            } else {
                $null
            }
        })
    }

    $nativeSuites = @(
        [pscustomobject]@{
            Path = $tinyExrOutput
            Prefix = 'TinyEXR'
            Implementation = 'TinyEXR v3 C'
            ExpectedRows = 22
        },
        [pscustomobject]@{
            Path = $openExrOutput
            Prefix = 'OpenEXR'
            Implementation = 'OpenEXR 3.4.13'
            ExpectedRows = 24
        }
    )
    foreach ($nativeSuite in $nativeSuites) {
        $nativeReport = Get-Content -Raw -LiteralPath $nativeSuite.Path | ConvertFrom-Json
        $nativeRows = @($nativeReport.benchmarks | Where-Object {
            $_.run_type -eq 'aggregate' -and $_.aggregate_name -eq 'mean'
        })
        if ($nativeRows.Count -eq 0) {
            $nativeRows = @($nativeReport.benchmarks | Where-Object {
                $_.run_type -eq 'iteration'
            })
        }
        $failedNativeRows = @($nativeRows | Where-Object {
            $_.error_occurred -eq $true -or [double]$_.real_time -le 0
        })
        if ($nativeRows.Count -ne $nativeSuite.ExpectedRows -or
            $failedNativeRows.Count -gt 0) {
            throw "$($nativeSuite.Implementation) report is incomplete: " +
                "$($nativeRows.Count) rows, $($failedNativeRows.Count) failed rows; " +
                "expected $($nativeSuite.ExpectedRows) successful rows."
        }

        $benchmarkPattern = '^' + [regex]::Escape($nativeSuite.Prefix) +
            '/(Encode|Decode)/([^/]+)'
        foreach ($benchmark in $nativeRows) {
            if ($benchmark.name -notmatch $benchmarkPattern) {
                continue
            }

            $operation = $Matches[1]
            $compression = $Matches[2]
            $meanMilliseconds = switch ($benchmark.time_unit) {
                'ns' { [double]$benchmark.real_time / 1e6 }
                'us' { [double]$benchmark.real_time / 1e3 }
                'ms' { [double]$benchmark.real_time }
                's' { [double]$benchmark.real_time * 1e3 }
                default { throw "Unknown Google Benchmark time unit: $($benchmark.time_unit)" }
            }
            $comparison.Add([pscustomobject][ordered]@{
                Implementation = $nativeSuite.Implementation
                Operation = $operation
                Compression = $compression
                MeanMilliseconds = $meanMilliseconds
                RawMiBPerSecond = [double]$benchmark.bytes_per_second / 1MB
                EncodedBytes = [long][Math]::Round([double]$benchmark.EncodedBytes)
                CompressionRatio = [double]$benchmark.CompressionRatio
                AllocatedBytes = $null
                SharedDecodeInput = if ($operation -eq 'Decode') {
                    [bool]$benchmark.SharedInput
                } else {
                    $null
                }
            })
        }
    }

    if ($comparison.Count -ne 68) {
        throw "The combined comparison has $($comparison.Count) rows; expected 68."
    }

    $comparisonPath = Join-Path $OutputDirectory 'comparison.csv'
    $comparison |
        Sort-Object Operation, Compression, Implementation |
        Export-Csv -NoTypeInformation -UseQuotes AsNeeded -LiteralPath $comparisonPath

    Write-Host "Managed results: $managedOutput"
    Write-Host "TinyEXR v3 C results: $tinyExrOutput"
    Write-Host "OpenEXR results: $openExrOutput"
    Write-Host "Combined results: $comparisonPath"
}
finally {
    Pop-Location
}
