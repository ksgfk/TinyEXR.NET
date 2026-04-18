param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dynamic', 'source')]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [string]$RuntimeIdentifier,

    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$libraryProject = Join-Path $repoRoot 'TinyEXR.NET\TinyEXR.NET.csproj'
$smokeProject = Join-Path $repoRoot 'TinyEXR.Test\TinyEXR.NativeAot.Smoke\TinyEXR.NativeAot.Smoke.csproj'
$packagesDir = Join-Path $repoRoot 'artifacts\packages'
$nugetPackagesDir = Join-Path $repoRoot 'artifacts\nuget-packages'
$publishDir = Join-Path $repoRoot "artifacts\nativeaot\$Mode\$RuntimeIdentifier"

[xml]$projectXml = Get-Content -LiteralPath $libraryProject
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
$packageVersion = if ($null -ne $versionNode) { $versionNode.InnerText } else { $null }

if ([string]::IsNullOrWhiteSpace($packageVersion))
{
    throw "Failed to resolve TinyEXR.NET package version from $libraryProject."
}

$cachedPackageDir = Join-Path (Join-Path $nugetPackagesDir 'tinyexr.net') $packageVersion
if (Test-Path -LiteralPath $cachedPackageDir)
{
    Remove-Item -LiteralPath $cachedPackageDir -Recurse -Force
}

if (Test-Path -LiteralPath $publishDir)
{
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

dotnet build $libraryProject -c $Configuration
dotnet pack $libraryProject -c $Configuration --no-build -o $packagesDir

$publishArgs = @(
    'publish',
    $smokeProject,
    '-c', $Configuration,
    '-r', $RuntimeIdentifier,
    '-p:PublishAot=true',
    "-p:TinyEXRPackageVersion=$packageVersion",
    '-o', $publishDir
)

if ($Mode -eq 'source')
{
    $publishArgs += '-p:TinyEXRStaticLinkMode=source'
}

dotnet @publishArgs

$nativeLibraryName =
    if ($RuntimeIdentifier.StartsWith('win-'))
    {
        'TinyEXRNative.dll'
    }
    elseif ($RuntimeIdentifier.StartsWith('linux-'))
    {
        'libTinyEXRNative.so'
    }
    elseif ($RuntimeIdentifier.StartsWith('osx-'))
    {
        'libTinyEXRNative.dylib'
    }
    else
    {
        throw "Unsupported RuntimeIdentifier: $RuntimeIdentifier"
    }

$nativeLibraryPath = Join-Path $publishDir $nativeLibraryName
$nativeLibraryCandidates = @()
if (Test-Path -LiteralPath $publishDir)
{
    $nativeLibraryCandidates = @(Get-ChildItem -Path $publishDir -Recurse -File -Filter $nativeLibraryName -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
}

if ($Mode -eq 'dynamic')
{
    if ($nativeLibraryCandidates.Count -gt 0)
    {
        Write-Host "Found packaged native library at: $($nativeLibraryCandidates[0])"
    }
    else
    {
        Write-Warning "Packaged native library '$nativeLibraryName' was not found under '$publishDir'. Continuing with executable validation."
    }
}
elseif ($nativeLibraryCandidates.Count -gt 0 -or (Test-Path -LiteralPath $nativeLibraryPath))
{
    throw "Source-static publish should not contain '$nativeLibraryName'."
}

$executableName =
    if ($RuntimeIdentifier.StartsWith('win-'))
    {
        'TinyEXR.NativeAot.Smoke.exe'
    }
    else
    {
        'TinyEXR.NativeAot.Smoke'
    }

$executablePath = Join-Path $publishDir $executableName
if (-not (Test-Path -LiteralPath $executablePath))
{
    throw "Published executable '$executableName' was not found."
}

$output = & $executablePath
if ($LASTEXITCODE -ne 0)
{
    throw "Published executable exited with code $LASTEXITCODE."
}

$outputText = ($output | Out-String).Trim()
if ($outputText -ne 'ok')
{
    throw "Unexpected executable output: '$outputText'"
}

Write-Host "NativeAOT smoke test passed for mode '$Mode' and RID '$RuntimeIdentifier'."
