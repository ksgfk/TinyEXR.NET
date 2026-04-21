param(
    [string]$DestinationRoot = "",
    [string]$Commit = "e38ffb0790f62f05a6f083a6fa4cac150b3b7452"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $repoRoot ".cache\openexr-images"
}

$DestinationRoot = [System.IO.Path]::GetFullPath($DestinationRoot)
$destinationParent = Split-Path -Parent $DestinationRoot
New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null

if (-not (Test-Path (Join-Path $DestinationRoot ".git"))) {
    if (Test-Path $DestinationRoot) {
        Remove-Item -LiteralPath $DestinationRoot -Recurse -Force
    }

    git clone https://github.com/openexr/openexr-images.git $DestinationRoot
    if ($LASTEXITCODE -ne 0) {
        throw "git clone failed."
    }
}

Push-Location $DestinationRoot
try {
    git fetch --tags origin
    if ($LASTEXITCODE -ne 0) {
        throw "git fetch failed."
    }

    git checkout --force $Commit
    if ($LASTEXITCODE -ne 0) {
        throw "git checkout failed."
    }
}
finally {
    Pop-Location
}

Write-Host "Prepared openexr-images at '$DestinationRoot' (commit $Commit)."
