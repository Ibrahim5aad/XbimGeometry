# Tell vcpkg to pass Emscripten environment variables to subprocesses.
# The DOTNET_EMSCRIPTEN_* variables are needed when using .NET's bundled Emscripten.
set(VCPKG_ENV_PASSTHROUGH_UNTRACKED
    EMSCRIPTEN_ROOT EMSDK PATH EM_CONFIG EM_CACHE FROZEN_CACHE EMSDK_PATH EMSDK_PYTHON
    DOTNET_EMSCRIPTEN_LLVM_ROOT DOTNET_EMSCRIPTEN_NODE_JS DOTNET_EMSCRIPTEN_BINARYEN_ROOT)

# Locate the Emscripten toolchain.
if(NOT DEFINED ENV{EMSCRIPTEN_ROOT})
    find_path(EMSCRIPTEN_ROOT "emcc")
else()
    set(EMSCRIPTEN_ROOT "$ENV{EMSCRIPTEN_ROOT}")
endif()

if(NOT EMSCRIPTEN_ROOT)
    if(NOT DEFINED ENV{EMSDK})
        message(FATAL_ERROR "The emcc compiler not found in PATH and EMSDK is not set.")
    endif()
    set(EMSCRIPTEN_ROOT "$ENV{EMSDK}/upstream/emscripten")
endif()

if(NOT EXISTS "${EMSCRIPTEN_ROOT}/cmake/Modules/Platform/Emscripten.cmake")
    message(FATAL_ERROR "Emscripten.cmake toolchain file not found at ${EMSCRIPTEN_ROOT}")
endif()

set(VCPKG_TARGET_ARCHITECTURE wasm32)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE static)
set(VCPKG_CMAKE_SYSTEM_NAME Emscripten)
# Use a wrapper toolchain that applies VCPKG_CXX_FLAGS to CMAKE_*_FLAGS_INIT
# before including the real Emscripten toolchain.  vcpkg has no built-in
# Emscripten toolchain, so without this wrapper the triplet flags are ignored.
set(VCPKG_CHAINLOAD_TOOLCHAIN_FILE "${CMAKE_CURRENT_LIST_DIR}/emscripten-wrapper.cmake")
set(VCPKG_BUILD_TYPE release)

# jemalloc does not support Emscripten — OCCT falls back to its native
# allocator which uses Emscripten's dlmalloc.
if(PORT STREQUAL "jemalloc")
    message(STATUS "Skipping jemalloc on Emscripten (unsupported)")
endif()

# All ports: enable pthreads so that native code can use threading primitives
# when linked into a multi-threaded .NET WASM runtime (WasmEnableThreads=true).
# All .a archives must be compiled with matching -pthread to link correctly.
set(VCPKG_CXX_FLAGS "-pthread")
set(VCPKG_C_FLAGS "-pthread")

# OCCT additionally needs native WASM exception handling to match .NET 9's runtime.
if(PORT STREQUAL "opencascade")
    string(APPEND VCPKG_CXX_FLAGS " -fwasm-exceptions")
    string(APPEND VCPKG_C_FLAGS " -fwasm-exceptions")
endif()
