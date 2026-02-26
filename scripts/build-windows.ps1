#
# Build and test the xbim geometry engine on Windows.
#
# Usage:
#   .\scripts\build-windows.ps1              # full build + test
#   .\scripts\build-windows.ps1 -Native      # native C++ only
#   .\scripts\build-windows.ps1 -Test        # .NET test only (assumes native already built)
#   .\scripts\build-windows.ps1 -Clean       # wipe build artifacts and start fresh
#   .\scripts\build-windows.ps1 -Config Debug  # build Debug instead of Release
#
param(
    [switch]$Native,
    [switch]$Test,
    [switch]$Clean,
    [ValidateSet("Release", "Debug")]
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"

$RepoRoot   = (Resolve-Path "$PSScriptRoot\..").Path
$NativeDir  = "$RepoRoot\src\Xbim.Geometry.Engine.Native"
$InteropDir = "$RepoRoot\src\Xbim.Geometry.Engine"
$BuildDir   = "$NativeDir\build"

# ── Colours ──────────────────────────────────────────────────────────────────
function Info  ($msg) { Write-Host "[info]  $msg" -ForegroundColor Cyan }
function Ok    ($msg) { Write-Host "[ok]    $msg" -ForegroundColor Green }
function Fail  ($msg) { Write-Host "[fail]  $msg" -ForegroundColor Red; exit 1 }

# ── Prerequisite checks ─────────────────────────────────────────────────────
function Check-Prereqs {
    $missing = @()
    if (-not (Get-Command cmake  -ErrorAction SilentlyContinue)) { $missing += "cmake" }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { $missing += "dotnet" }

    if (-not $env:VCPKG_ROOT) {
        Fail "VCPKG_ROOT is not set. Clone vcpkg and set the environment variable."
    }

    if ($missing.Count -gt 0) {
        Fail "Missing tools: $($missing -join ', ')"
    }
}

# ── Native C++ build ────────────────────────────────────────────────────────
function Build-Native {
    $configLower = $Config.ToLower()
    $preset      = "win-x64-$configLower"
    $dir         = if ($Config -eq "Release") { $BuildDir } else { "$NativeDir\build-debug" }

    if (-not (Test-Path $dir)) {
        Info "Configuring native build ($Config)..."
        cmake --preset $preset -S $NativeDir
        if ($LASTEXITCODE -ne 0) { Fail "CMake configure failed." }
    } else {
        Info "Build directory exists, skipping configure (use -Clean to reconfigure)."
    }

    Info "Building native library ($Config)..."
    cmake --build $dir --config $Config
    if ($LASTEXITCODE -ne 0) { Fail "CMake build failed." }

    Ok "Native build complete: $dir"
}

# ── Stage native binaries into runtimes/win-x64/native/ ─────────────────────
function Stage-Native {
    $dir = if ($Config -eq "Release") { $BuildDir } else { "$NativeDir\build-debug" }
    Info "Staging native binaries for .NET..."
    cmake --install $dir --config $Config --prefix $InteropDir
    Ok "Staged to $InteropDir\runtimes\win-x64\native\"
}

# ── .NET restore + test ─────────────────────────────────────────────────────
function Run-Tests {
    Info "Restoring .NET solution..."
    dotnet restore "$RepoRoot\Xbim.Geometry.Engine.sln"

    Info "Building .NET solution..."
    dotnet build "$RepoRoot\Xbim.Geometry.Engine.sln" -c $Config --no-restore

    Info "Running tests..."
    dotnet test "$RepoRoot\Xbim.Geometry.Engine.sln" `
        -c $Config --no-build --logger "console;verbosity=normal"

    Ok "All tests passed."
}

# ── Clean ────────────────────────────────────────────────────────────────────
function Clean-All {
    Info "Cleaning build artifacts..."
    if (Test-Path $BuildDir)   { Remove-Item $BuildDir -Recurse -Force }
    $DebugBuildDir = "$NativeDir\build-debug"
    if (Test-Path $DebugBuildDir) { Remove-Item $DebugBuildDir -Recurse -Force }
    $runtimeDir = "$InteropDir\runtimes\win-x64"
    if (Test-Path $runtimeDir) { Remove-Item $runtimeDir -Recurse -Force }
    Get-ChildItem $RepoRoot -Include bin, obj -Recurse -Directory | Remove-Item -Recurse -Force
    Ok "Clean complete."
}

# ── Main ─────────────────────────────────────────────────────────────────────
Check-Prereqs

if ($Clean) {
    Clean-All
} elseif ($Native) {
    Build-Native
    Stage-Native
} elseif ($Test) {
    Run-Tests
} else {
    Build-Native
    Stage-Native
    Run-Tests
}
