param(
	[Parameter(Mandatory = $true, Position = 0)]
	[ValidateNotNullOrEmpty()]
	[string]$Platform
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }
function Write-Warn($msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Write-Err($msg)  { Write-Host "[ERROR] $msg" -ForegroundColor Red }

$repoRoot   = Split-Path -Parent $PSCommandPath
$nativeDir  = Join-Path $repoRoot 'TinyEXR.Native'
$buildDir   = Join-Path $nativeDir 'build'
$assetsRoot = Join-Path (Join-Path $repoRoot 'TinyEXR.NET') 'Assets'

$supported = @('win-x64','win-arm','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')
if (-not ($supported -contains $Platform)) {
	throw "Unsupported platform '$Platform'. Supported: $($supported -join ', ')"
}

Write-Info "Platform: $Platform"

switch -Regex ($Platform) {
	'^win-'   { $sharedName = 'TinyEXRNative.dll' }
	'^linux-' { $sharedName = 'libTinyEXRNative.so' }
	'^osx-'   { $sharedName = 'TinyEXRNative.dylib' }
	default   { throw "Unrecognized platform: $Platform" }
}
if (Test-Path $buildDir) {
    Remove-Item -Path $buildDir -Recurse -Force
}
if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory -Path $buildDir | Out-Null }

function Invoke-CMakeConfigure {
	param(
		[string[]]$BaseArgs,
		[string]$Platform
	)

	$isMac = $Platform.StartsWith('osx-')

	$extraArgs = @()
	if ($isMac) {
		$arch = switch ($Platform) {
			'osx-x64'   { 'x86_64' }
			'osx-arm64' { 'arm64' }
			default     { $null }
		}
		if ($arch) {
			$extraArgs += ("-DCMAKE_OSX_ARCHITECTURES=$arch")
		}
	}

	$args = @() + $BaseArgs + $extraArgs
	Write-Info "Configuring with: cmake $($args -join ' ')"
	& cmake @args
	if ($LASTEXITCODE -ne 0) {
		throw "CMake configure failed"
	}
}

function Invoke-CMakeBuild {
	param(
		[string]$BuildDir
	)
	$buildArgs = @('--build', $BuildDir, '--target', 'TinyEXRNative', '--config', 'Release', '--parallel')
	Write-Info "Building with: cmake $($buildArgs -join ' ')"
	& cmake @buildArgs
	if ($LASTEXITCODE -ne 0) { throw "CMake build failed" }
}

function Find-Artifact {
	param(
		[string]$Root,
		[string]$FileName
	)
	$file = Get-ChildItem -Path $Root -Recurse -File -Filter $FileName -ErrorAction SilentlyContinue | Select-Object -First 1
	if (-not $file) {
		throw "Could not find build artifact '$FileName' under '$Root'"
	}
	return $file.FullName
}

$configureBase = @(
	'-S', $nativeDir,
	'-B', $buildDir,
	'-DCMAKE_BUILD_TYPE=Release'
)

Invoke-CMakeConfigure -BaseArgs $configureBase -Platform $Platform
Invoke-CMakeBuild -BuildDir $buildDir

$artifactPath = $null
try {
	$artifactPath = Find-Artifact -Root $buildDir -FileName $sharedName
} catch {
	# On macOS, some toolchains may still emit lib-prefixed name; try fallback and rename
	if ($Platform.StartsWith('osx-')) {
		$fallback = 'lib' + $sharedName
		try {
			$fallbackPath = Find-Artifact -Root $buildDir -FileName $fallback
			if ($fallbackPath) {
				$newPath = Join-Path (Split-Path $fallbackPath -Parent) $sharedName
				Write-Warn "Fallback found: $fallbackPath; renaming to $newPath"
				Rename-Item -Path $fallbackPath -NewName (Split-Path $newPath -Leaf) -Force
				$artifactPath = $newPath
			}
		} catch {}
	}
	if (-not $artifactPath) { throw }
}
Write-Info "Artifact located: $artifactPath"

$destDir = Join-Path (Join-Path $assetsRoot 'runtimes') (Join-Path $Platform 'native')
if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }

$destPath = Join-Path $destDir (Split-Path $artifactPath -Leaf)
Copy-Item -Path $artifactPath -Destination $destPath -Force
Write-Info "Copied to: $destPath"

Write-Host "Done." -ForegroundColor Green
