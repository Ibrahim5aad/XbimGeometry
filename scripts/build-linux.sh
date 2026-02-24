#!/usr/bin/env bash
#
# Build and test the xbim geometry engine on Linux (WSL or native).
#
# Usage:
#   ./scripts/build-linux.sh              # full build + test
#   ./scripts/build-linux.sh --native     # native C++ only
#   ./scripts/build-linux.sh --test       # .NET test only (assumes native already built)
#   ./scripts/build-linux.sh --clean      # wipe build artifacts and start fresh
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NATIVE_DIR="$REPO_ROOT/src/Xbim.Geometry.Engine.Native"
INTEROP_DIR="$REPO_ROOT/src/Xbim.Geometry.Engine.Interop"
BUILD_DIR="$NATIVE_DIR/build"
BUILD_CONFIG="${BUILD_CONFIG:-Release}"

# ── Colours ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
info()  { echo -e "${CYAN}[info]${NC}  $*"; }
ok()    { echo -e "${GREEN}[ok]${NC}    $*"; }
fail()  { echo -e "${RED}[fail]${NC}  $*"; exit 1; }

# ── Prerequisite checks ─────────────────────────────────────────────────────
check_prereqs() {
    local missing=()
    command -v cmake  >/dev/null || missing+=(cmake)
    command -v ninja  >/dev/null || missing+=(ninja-build)
    command -v g++    >/dev/null || missing+=(g++)
    command -v dotnet >/dev/null || missing+=(dotnet-sdk-8.0)

    if [[ -z "${VCPKG_ROOT:-}" ]]; then
        fail "VCPKG_ROOT is not set. Clone vcpkg and export VCPKG_ROOT=<path>"
    fi

    if [[ ${#missing[@]} -gt 0 ]]; then
        fail "Missing tools: ${missing[*]}\n       Install with: sudo apt install ${missing[*]}"
    fi
}

# ── Native C++ build ────────────────────────────────────────────────────────
build_native() {
    info "Configuring native build (${BUILD_CONFIG})..."
    cmake --preset linux-x64-release -S "$NATIVE_DIR"

    info "Building native library..."
    cmake --build "$BUILD_DIR" --config "$BUILD_CONFIG" -j "$(nproc)"

    ok "Native build complete: $BUILD_DIR"
}

# ── Stage native binaries into runtimes/linux-x64/native/ ───────────────────
stage_native() {
    info "Staging native binaries for .NET..."
    cmake --install "$BUILD_DIR" --config "$BUILD_CONFIG" --prefix "$INTEROP_DIR"
    ok "Staged to $INTEROP_DIR/runtimes/linux-x64/native/"
}

# ── .NET restore + test ─────────────────────────────────────────────────────
run_tests() {
    info "Restoring .NET solution..."
    dotnet restore "$REPO_ROOT/Xbim.Geometry.Engine.sln"

    info "Building .NET solution..."
    dotnet build "$REPO_ROOT/Xbim.Geometry.Engine.sln" -c "$BUILD_CONFIG" --no-restore

    info "Running unit tests..."
    dotnet test "$REPO_ROOT/tests/Xbim.Geometry.UnitTests/Xbim.Geometry.UnitTests.csproj" \
        -c "$BUILD_CONFIG" --no-build --logger "console;verbosity=normal"

    info "Running integration tests..."
    dotnet test "$REPO_ROOT/tests/Xbim.Geometry.IntegrationTests/Xbim.Geometry.IntegrationTests.csproj" \
        -c "$BUILD_CONFIG" --no-build --logger "console;verbosity=normal"

    ok "All tests passed."
}

# ── Clean ────────────────────────────────────────────────────────────────────
clean() {
    info "Cleaning build artifacts..."
    rm -rf "$BUILD_DIR"
    rm -rf "$INTEROP_DIR/runtimes/linux-x64"
    find "$REPO_ROOT" -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null || true
    ok "Clean complete."
}

# ── Main ─────────────────────────────────────────────────────────────────────
main() {
    check_prereqs

    case "${1:-}" in
        --native)
            build_native
            stage_native
            ;;
        --test)
            run_tests
            ;;
        --clean)
            clean
            ;;
        *)
            build_native
            stage_native
            run_tests
            ;;
    esac
}

main "$@"
