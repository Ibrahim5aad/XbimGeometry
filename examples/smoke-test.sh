#!/usr/bin/env bash
#
# Smoke test for the xbim example projects.
#
# Builds all three projects and optionally runs the converter on a test IFC file.
#
# Usage:
#   ./examples/smoke-test.sh                 # build only
#   ./examples/smoke-test.sh --convert       # build + run converter on SampleHouse4.ifc
#   ./examples/smoke-test.sh --clean         # clean first, then build
#

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
EXAMPLES_DIR="$REPO_ROOT/examples"
SLN_PATH="$EXAMPLES_DIR/Xbim.Examples.sln"
TEST_DATA="$EXAMPLES_DIR/testdata"
TEST_IFC="$REPO_ROOT/tests/Xbim.Geometry.IntegrationTests/TestFiles/IfcExamples/SampleHouse4.ifc"

DO_CONVERT=false
DO_CLEAN=false

for arg in "$@"; do
    case "$arg" in
        --convert) DO_CONVERT=true ;;
        --clean)   DO_CLEAN=true ;;
        *)         echo "Unknown argument: $arg"; exit 1 ;;
    esac
done

info()  { echo -e "\033[36m[info]  $1\033[0m"; }
ok()    { echo -e "\033[32m[ok]    $1\033[0m"; }
fail()  { echo -e "\033[31m[fail]  $1\033[0m"; exit 1; }

# ── Clean ─────────────────────────────────────────────────────────────────────
if $DO_CLEAN; then
    info "Cleaning example build artifacts..."
    find "$EXAMPLES_DIR" -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null || true
    ok "Clean complete."
fi

# ── Build ─────────────────────────────────────────────────────────────────────
info "Building example solution..."
dotnet build "$SLN_PATH" -p:NuGetAudit=false
ok "All three example projects built successfully."

# ── Verify testdata ──────────────────────────────────────────────────────────
WEXBIM="$TEST_DATA/SampleHouse4.wexbim"
if [ -f "$WEXBIM" ]; then
    SIZE=$(du -k "$WEXBIM" | cut -f1)
    ok "Test data exists: SampleHouse4.wexbim (${SIZE} KB)"
else
    info "No test data at $WEXBIM — run with --convert to generate."
fi

# ── Convert ──────────────────────────────────────────────────────────────────
if $DO_CONVERT; then
    if [ ! -f "$TEST_IFC" ]; then
        fail "Test IFC not found: $TEST_IFC"
    fi

    OUTPUT_WEXBIM="$TEST_DATA/SampleHouse4-converted.wexbim"

    info "Running converter: SampleHouse4.ifc -> SampleHouse4-converted.wexbim..."
    dotnet run --project "$EXAMPLES_DIR/Xbim.Examples.WexBimConverter" --no-build -- "$TEST_IFC" "$OUTPUT_WEXBIM"

    if [ ! -f "$OUTPUT_WEXBIM" ]; then
        fail "Converter produced no output file."
    fi

    OUT_SIZE=$(du -k "$OUTPUT_WEXBIM" | cut -f1)
    if [ "$OUT_SIZE" -eq 0 ]; then
        fail "Converter output is empty."
    fi

    ok "Converter produced ${OUT_SIZE} KB WexBIM file."

    # Clean up the converted file
    rm -f "$OUTPUT_WEXBIM"
    ok "Cleaned up temporary converted file."
fi

# ── Done ──────────────────────────────────────────────────────────────────────
ok "Smoke test passed."
