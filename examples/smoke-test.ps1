#
# Smoke test for the xbim example projects.
#
# Builds all three projects and optionally runs the converter on a test IFC file.
#
# Usage:
#   .\examples\smoke-test.ps1                    # build only
#   .\examples\smoke-test.ps1 -Convert           # build + run converter on SampleHouse4.ifc
#   .\examples\smoke-test.ps1 -Convert -Clean    # clean first, then build + convert
#

param(
    [switch]$Convert,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$RepoRoot   = (Resolve-Path "$PSScriptRoot\..").Path
$ExamplesDir = "$RepoRoot\examples"
$SlnPath     = "$ExamplesDir\Xbim.Examples.sln"
$TestData   = "$ExamplesDir\testdata"
$TestIfc    = "$RepoRoot\tests\Xbim.Geometry.IntegrationTests\TestFiles\IfcExamples\SampleHouse4.ifc"

function Info  ($msg) { Write-Host "[info]  $msg" -ForegroundColor Cyan }
function Ok    ($msg) { Write-Host "[ok]    $msg" -ForegroundColor Green }
function Fail  ($msg) { Write-Host "[fail]  $msg" -ForegroundColor Red; exit 1 }

# ── Clean ─────────────────────────────────────────────────────────────────────
if ($Clean) {
    Info "Cleaning example build artifacts..."
    Get-ChildItem $ExamplesDir -Include bin, obj -Recurse -Directory | Remove-Item -Recurse -Force
    Ok "Clean complete."
}

# ── Build ─────────────────────────────────────────────────────────────────────
Info "Building example solution..."
dotnet build $SlnPath -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { Fail "Build failed." }
Ok "All three example projects built successfully."

# ── Verify testdata ──────────────────────────────────────────────────────────
$wexbim = "$TestData\SampleHouse4.wexbim"
if (Test-Path $wexbim) {
    $size = (Get-Item $wexbim).Length
    Ok "Test data exists: SampleHouse4.wexbim ($([math]::Round($size / 1024)) KB)"
} else {
    Info "No test data at $wexbim — run with -Convert to generate."
}

# ── Convert ──────────────────────────────────────────────────────────────────
if ($Convert) {
    if (-not (Test-Path $TestIfc)) {
        Fail "Test IFC not found: $TestIfc"
    }

    $outputWexbim = "$TestData\SampleHouse4-converted.wexbim"

    Info "Running converter: SampleHouse4.ifc -> SampleHouse4-converted.wexbim..."
    dotnet run --project "$ExamplesDir\Xbim.Examples.WexBimConverter" --no-build -- $TestIfc $outputWexbim
    if ($LASTEXITCODE -ne 0) { Fail "Converter failed." }

    if (-not (Test-Path $outputWexbim)) {
        Fail "Converter produced no output file."
    }

    $outSize = (Get-Item $outputWexbim).Length
    if ($outSize -eq 0) { Fail "Converter output is empty." }

    Ok "Converter produced $([math]::Round($outSize / 1024)) KB WexBIM file."

    # Clean up the converted file (the pre-generated one in testdata is the canonical copy)
    Remove-Item $outputWexbim
    Ok "Cleaned up temporary converted file."
}

# ── Done ──────────────────────────────────────────────────────────────────────
Ok "Smoke test passed."
