#!/usr/bin/env bash
#
# Build the xbim geometry engine for WebAssembly using Emscripten.
#
# By default, uses the Emscripten bundled with the .NET wasm-tools workload
# so that the compiled .a files are ABI-compatible with the Blazor WASM linker.
# Falls back to a standalone EMSDK if the .NET workload isn't installed.
#
# Usage:
#   ./scripts/build-wasm.sh              # configure + build
#   ./scripts/build-wasm.sh --install    # build and install .a files for .NET consumption
#   ./scripts/build-wasm.sh --clean      # wipe build-wasm/ and rebuild from scratch
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NATIVE_DIR="$REPO_ROOT/src/Xbim.Geometry.Engine.Native"
INTEROP_DIR="$REPO_ROOT/src/Xbim.Geometry.Engine"
BUILD_DIR="$NATIVE_DIR/build-wasm"
PRESET="wasm32-release"

# ── Colours ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
info()  { echo -e "${CYAN}[info]${NC}  $*"; }
ok()    { echo -e "${GREEN}[ok]${NC}    $*"; }
fail()  { echo -e "${RED}[fail]${NC}  $*"; exit 1; }

# ── Discover .NET's bundled Emscripten ───────────────────────────────────────
# The wasm-tools workload installs Emscripten as NuGet packs under the dotnet
# packs directory. We prefer this over a standalone EMSDK to guarantee the
# compiled .a files link cleanly with dotnet's native relinker.
setup_dotnet_emscripten() {
    local dotnet_root
    if [[ -n "${DOTNET_ROOT:-}" ]]; then
        dotnet_root="$DOTNET_ROOT"
    elif [[ -d "/c/Program Files/dotnet" ]]; then
        dotnet_root="/c/Program Files/dotnet"
    elif [[ -d "/usr/share/dotnet" ]]; then
        dotnet_root="/usr/share/dotnet"
    elif [[ -d "/usr/local/share/dotnet" ]]; then
        dotnet_root="/usr/local/share/dotnet"
    else
        return 1
    fi

    local packs_dir="$dotnet_root/packs"
    [[ -d "$packs_dir" ]] || return 1

    # Determine the host RID suffix (e.g. win-x64, linux-x64, osx-x64)
    local os_name arch_name rid
    case "$(uname -s)" in
        MINGW*|MSYS*|CYGWIN*|*NT*) os_name="win" ;;
        Linux)                      os_name="linux" ;;
        Darwin)                     os_name="osx" ;;
        *)                          return 1 ;;
    esac
    case "$(uname -m)" in
        x86_64|amd64) arch_name="x64" ;;
        aarch64|arm64) arch_name="arm64" ;;
        *)             return 1 ;;
    esac
    rid="${os_name}-${arch_name}"

    # Find the newest Emscripten SDK pack
    local sdk_pack
    sdk_pack=$(ls -d "$packs_dir"/Microsoft.NET.Runtime.Emscripten.*.Sdk.${rid}/ 2>/dev/null \
               | sort -V | tail -1) || true
    [[ -n "$sdk_pack" ]] || return 1

    # Resolve version subdirectory (e.g. 9.0.13/)
    local version_dir
    version_dir=$(ls -d "$sdk_pack"/*/ 2>/dev/null | sort -V | tail -1) || true
    [[ -n "$version_dir" ]] || return 1

    local tools_dir="$version_dir/tools"
    [[ -d "$tools_dir/emscripten" ]] || return 1

    # Extract the Emscripten version from the pack name (e.g. 3.1.56)
    local em_version
    em_version=$(basename "$sdk_pack" | sed -n 's/Microsoft.NET.Runtime.Emscripten.\(.*\)\.Sdk\..*/\1/p')

    # Find matching Python, Node, and Cache packs
    local python_pack node_pack cache_pack
    python_pack=$(ls -d "$packs_dir"/Microsoft.NET.Runtime.Emscripten.${em_version}.Python.${rid}/*/ 2>/dev/null \
                  | sort -V | tail -1) || true
    node_pack=$(ls -d "$packs_dir"/Microsoft.NET.Runtime.Emscripten.${em_version}.Node.${rid}/*/ 2>/dev/null \
                | sort -V | tail -1) || true
    cache_pack=$(ls -d "$packs_dir"/Microsoft.NET.Runtime.Emscripten.${em_version}.Cache.${rid}/*/ 2>/dev/null \
                 | sort -V | tail -1) || true

    # Set environment variables that .emscripten config expects
    export EMSCRIPTEN_ROOT="$tools_dir/emscripten"
    export EMSDK_PATH="$tools_dir/"
    export DOTNET_EMSCRIPTEN_LLVM_ROOT="$tools_dir/bin"
    export DOTNET_EMSCRIPTEN_BINARYEN_ROOT="$tools_dir/"
    export EM_CONFIG="$tools_dir/emscripten/.emscripten"

    # The .NET Emscripten packs are installed in Program Files (read-only).
    # If a pre-built Cache pack exists, use it as a frozen cache.
    # Otherwise, redirect EM_CACHE to a user-writable directory so emcc
    # can build cache entries (e.g. libc, libdlmalloc) on first run.
    if [[ -n "$cache_pack" && -d "$cache_pack/tools/emscripten/cache" ]]; then
        export EM_CACHE="$cache_pack/tools/emscripten/cache"
        export FROZEN_CACHE=True
    else
        export EM_CACHE="${HOME}/.emscripten_cache"
        export FROZEN_CACHE=""
        mkdir -p "$EM_CACHE"
        info "No Emscripten Cache pack found — using writable cache at $EM_CACHE"
    fi

    if [[ -n "$python_pack" ]]; then
        local python_exe="$python_pack/tools/python3"
        [[ -f "$python_exe" ]] || python_exe="$python_pack/tools/python.exe"
        [[ -f "$python_exe" ]] || python_exe="$python_pack/tools/python"
        if [[ -f "$python_exe" ]]; then
            export EMSDK_PYTHON="$python_exe"
        fi
    fi

    if [[ -n "$node_pack" ]]; then
        local node_exe="$node_pack/tools/bin/node"
        [[ -f "$node_exe" ]] || node_exe="$node_pack/tools/bin/node.exe"
        if [[ -f "$node_exe" ]]; then
            export DOTNET_EMSCRIPTEN_NODE_JS="$node_exe"
        fi
    fi

    # Add emcc, clang, and node to PATH
    export PATH="$tools_dir/emscripten:$tools_dir/bin:$PATH"
    if [[ -n "${EMSDK_PYTHON:-}" ]]; then
        export PATH="$(dirname "$EMSDK_PYTHON"):$PATH"
    fi
    if [[ -n "${DOTNET_EMSCRIPTEN_NODE_JS:-}" ]]; then
        export PATH="$(dirname "$DOTNET_EMSCRIPTEN_NODE_JS"):$PATH"
    fi

    info "Using .NET Emscripten ${em_version} from: $tools_dir"
    return 0
}

# ── Discover standalone EMSDK (fallback) ─────────────────────────────────────
setup_standalone_emsdk() {
    if [[ -z "${EMSDK:-}" ]]; then
        return 1
    fi
    export EMSCRIPTEN_ROOT="$EMSDK/upstream/emscripten"
    if [[ ! -d "$EMSCRIPTEN_ROOT" ]]; then
        return 1
    fi
    info "Using standalone EMSDK from: $EMSDK"
    return 0
}

# ── Prerequisite checks ─────────────────────────────────────────────────────
check_prereqs() {
    # Try .NET's Emscripten first, fall back to standalone EMSDK
    if ! setup_dotnet_emscripten; then
        if ! setup_standalone_emsdk; then
            fail "No Emscripten toolchain found.\n" \
                 "  Install the .NET wasm-tools workload:\n" \
                 "    dotnet workload install wasm-tools\n" \
                 "  Or source a standalone EMSDK:\n" \
                 "    source \$EMSDK/emsdk_env.sh"
        fi
    fi

    local missing=()
    command -v cmake >/dev/null || missing+=(cmake)
    command -v emcc  >/dev/null || missing+=("emcc")

    if [[ -z "${VCPKG_ROOT:-}" ]]; then
        fail "VCPKG_ROOT is not set. Clone vcpkg and export VCPKG_ROOT=<path>"
    fi

    # Ninja: try PATH first, then vcpkg's bundled copy (CMake often can't
    # find ninja via PATH alone on Windows).
    if ! command -v ninja >/dev/null 2>&1; then
        local vcpkg_ninja
        vcpkg_ninja=$(find "$VCPKG_ROOT/downloads/tools" -name "ninja*" -type f 2>/dev/null | head -1) || true
        if [[ -n "$vcpkg_ninja" ]]; then
            NINJA_PATH="$vcpkg_ninja"
            info "Using vcpkg-bundled ninja: $NINJA_PATH"
        else
            missing+=(ninja)
        fi
    fi

    if [[ ${#missing[@]} -gt 0 ]]; then
        fail "Missing tools: ${missing[*]}"
    fi
}

# ── Native C++ build (Emscripten → static archive) ──────────────────────────
build_native() {
    if [[ ! -f "${BUILD_DIR}/CMakeCache.txt" ]]; then
        info "Configuring native build (preset: ${PRESET})..."
        local cmake_extra=()
        if [[ -n "${NINJA_PATH:-}" ]]; then
            cmake_extra+=("-DCMAKE_MAKE_PROGRAM=$NINJA_PATH")
        fi
        cmake --preset "$PRESET" -S "$NATIVE_DIR" "${cmake_extra[@]}"
    else
        info "Build already configured (use --clean to reconfigure)"
    fi

    info "Building native library..."
    cmake --build "$BUILD_DIR" --config Release

    local archive="$BUILD_DIR/xbim_geometry_native.a"
    if [[ -f "$archive" ]]; then
        local size
        size=$(du -h "$archive" | cut -f1)
        ok "Native build complete: $archive (${size})"
    else
        fail "Expected output not found at $archive"
    fi
}

# ── Stage static archives for .NET NativeFileReference consumption ───────────
stage_native() {
    info "Staging WASM archives..."
    cmake --install "$BUILD_DIR" --config Release --prefix "$INTEROP_DIR"
    ok "Staged to $INTEROP_DIR/wasm/native/"
}

# ── Clean ────────────────────────────────────────────────────────────────────
clean() {
    info "Cleaning WASM build artifacts..."
    rm -rf "$BUILD_DIR"
    rm -rf "$INTEROP_DIR/wasm"
    ok "Clean complete."
}

# ── Main ─────────────────────────────────────────────────────────────────────
main() {
    case "${1:-}" in
        --clean)
            clean
            exit 0
            ;;
        --help|-h)
            echo "Usage: $0 [--clean]"
            echo "  (default)    Configure, build, and install to src/Xbim.Geometry.Engine/wasm/native/"
            echo "  --clean      Wipe build-wasm/ and staged files"
            exit 0
            ;;
    esac

    check_prereqs

    info "Emscripten: $(emcc --version | head -1)"
    info "vcpkg:      ${VCPKG_ROOT}"
    info "Build dir:  ${BUILD_DIR}"

    build_native
    stage_native
}

main "$@"
