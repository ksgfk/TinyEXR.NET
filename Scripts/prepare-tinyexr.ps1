[CmdletBinding()]
param([string] $DestinationRoot = '')

$ErrorActionPreference = 'Stop'
$pin = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'tinyexr-source.json') | ConvertFrom-Json
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $repoRoot '.cache/tinyexr'
}
$DestinationRoot = [IO.Path]::GetFullPath($DestinationRoot)

function Invoke-GitChecked([string[]] $Arguments) {
    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
    return $output
}

if (Test-Path -LiteralPath $DestinationRoot) {
    if (-not (Test-Path -LiteralPath (Join-Path $DestinationRoot '.git'))) {
        throw "Existing cache is not a Git checkout: $DestinationRoot. Move it aside before preparing the source."
    }
    $actual = Invoke-GitChecked @('-C', $DestinationRoot, 'rev-parse', 'HEAD')
    if ($actual -ne $pin.commit) {
        throw "TinyEXR cache must be $($pin.commit), found $actual. Move it aside; it will not be overwritten."
    }
    $changes = Invoke-GitChecked @('-C', $DestinationRoot, 'status', '--porcelain', '--untracked-files=normal')
    if ($changes) { throw "TinyEXR cache has local changes: $DestinationRoot. They will not be overwritten." }
    Write-Host "Reusing pinned TinyEXR source at '$DestinationRoot' (no network access)."
    return
}

$parent = Split-Path -Parent $DestinationRoot
New-Item -ItemType Directory -Force -Path $parent | Out-Null
# A failed download stays in a distinct directory for diagnosis; the usable cache
# is published only after checkout succeeds. Never delete an existing checkout.
$staging = Join-Path $parent ('.tinyexr-download-' + [Guid]::NewGuid().ToString('N'))
Invoke-GitChecked @('init', '--quiet', $staging) | Out-Null
Invoke-GitChecked @('-C', $staging, 'remote', 'add', 'origin', $pin.repository) | Out-Null
Invoke-GitChecked @('-C', $staging, 'fetch', '--depth=1', 'origin', $pin.commit) | Out-Null
Invoke-GitChecked @('-C', $staging, 'checkout', '--quiet', '--detach', $pin.commit) | Out-Null
$resolvedStaging = [IO.Path]::GetFullPath($staging)
if (-not $resolvedStaging.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source staging directory is outside the cache parent.'
}
Move-Item -LiteralPath $resolvedStaging -Destination $DestinationRoot
Write-Host "Prepared TinyEXR at '$DestinationRoot' ($($pin.commit))."
