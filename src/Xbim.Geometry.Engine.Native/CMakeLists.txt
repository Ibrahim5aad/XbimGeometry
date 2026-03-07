cmake_minimum_required(VERSION 3.20)
project(xbim_geometry_native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Emscripten requires static libraries (no dlopen in WASM).
# All other platforms build shared libraries.
if(EMSCRIPTEN)
    set(BUILD_SHARED_LIBS OFF)
else()
    set(BUILD_SHARED_LIBS ON)
endif()

# Platform-specific export macros
if(EMSCRIPTEN)
    add_definitions(-DXBIM_STATIC_BUILD)
elseif(WIN32)
    add_definitions(-DXBIM_BUILD_DLL)
endif()

# ── OpenCASCADE (via vcpkg or system install) ─────────────────────────────────
# When using vcpkg, set CMAKE_TOOLCHAIN_FILE to vcpkg.cmake (handled by
# CMakePresets.json). On Linux, system-installed OCCT (e.g. libocct-*-dev)
# also works — find_package resolves both.
find_package(OpenCASCADE REQUIRED CONFIG)

# The specific OCCT toolkit libraries we need.
set(XBIM_OCCT_LIBS
    # Foundation
    TKernel
    TKMath
    # Modeling Data
    TKG2d
    TKG3d
    TKGeomBase
    TKBRep
    # Modeling Algorithms
    TKTopAlgo
    TKGeomAlgo
    TKShHealing
    TKBO
    TKBool
    TKPrim
    TKMesh
    TKOffset
    TKFillet
    TKHLR
    # Data Exchange
    TKXSBase
    TKDESTEP
)

# Collect sources
file(GLOB_RECURSE NATIVE_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp"
)

file(GLOB_RECURSE NATIVE_HEADERS
    "${CMAKE_CURRENT_SOURCE_DIR}/include/*.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.h"
)

add_library(xbim_geometry_native ${NATIVE_SOURCES} ${NATIVE_HEADERS})

target_include_directories(xbim_geometry_native
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
        ${OpenCASCADE_INCLUDE_DIR}
)

# ── Manifold (mesh boolean library, via vcpkg) ────────────────────────────────
find_package(manifold REQUIRED CONFIG)

target_link_libraries(xbim_geometry_native PRIVATE ${XBIM_OCCT_LIBS} manifold::manifold)

# Output naming
set_target_properties(xbim_geometry_native PROPERTIES
    OUTPUT_NAME "xbim_geometry_native"
    DEBUG_POSTFIX ""
)

# On Windows, suppress the default "lib" prefix that MinGW would add.
# On WASM, suppress it so the archive name matches the DllImport library name
# ("xbim_geometry_native") — the .NET WASM P/Invoke table generator uses the
# filename as the module key and requires an exact match.
# On Linux/macOS the default "lib" prefix is correct (libxbim_geometry_native.so).
if(WIN32 OR EMSCRIPTEN)
    set_target_properties(xbim_geometry_native PROPERTIES PREFIX "")
endif()

# MSVC-specific settings
if(MSVC)
    target_compile_options(xbim_geometry_native PRIVATE
        /W3
        /MP
    )
    target_compile_definitions(xbim_geometry_native PRIVATE
        _CRT_SECURE_NO_WARNINGS
        OCCT_NO_PLUGINS
    )
endif()

# GCC/Clang settings
if(EMSCRIPTEN)
    target_compile_options(xbim_geometry_native PRIVATE
        -Wall
        -Wextra
        -fwasm-exceptions
        -pthread
    )
    target_compile_definitions(xbim_geometry_native PRIVATE
        OCCT_NO_PLUGINS
    )
elseif(CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
    target_compile_options(xbim_geometry_native PRIVATE
        -Wall
        -Wextra
        -fvisibility=hidden
        -ftls-model=global-dynamic
    )
endif()

# ── Install (stage native binaries for NuGet packaging) ─────────────────────
# Usage: cmake --install build --config Release --prefix ../Xbim.Geometry.Engine
# This stages native DLLs into runtimes/{rid}/native/ for NuGet package inclusion.
# WASM produces static archives (.a) — consumers link them via <NativeFileReference>,
# so we install to a flat directory instead of the runtimes/ layout.
if(EMSCRIPTEN)
    install(TARGETS xbim_geometry_native
        ARCHIVE DESTINATION "wasm/native"
    )
    # Install only the OCCT and third-party static archives we actually link.
    # This avoids shipping ~40 unused OCCT modules (OCAF, viewer, extra data
    # exchange formats) that would bloat the WASM package.
    set(_wasm_deps
        # OCCT — must match XBIM_OCCT_LIBS above
        libTKernel.a libTKMath.a libTKG2d.a libTKG3d.a libTKGeomBase.a
        libTKBRep.a libTKTopAlgo.a libTKGeomAlgo.a libTKShHealing.a
        libTKBO.a libTKBool.a libTKPrim.a libTKMesh.a libTKOffset.a
        libTKFillet.a libTKHLR.a libTKXSBase.a libTKDESTEP.a
        # OCCT transitive deps needed at link time
        libTKDE.a
        # Manifold
        libmanifold.a
        # Manifold transitive deps
        libClipper2.a libClipper2Z.a
    )
    set(_vcpkg_lib_dir "${CMAKE_CURRENT_BINARY_DIR}/vcpkg_installed/${VCPKG_TARGET_TRIPLET}/lib")
    install(CODE "
        set(_libs \"${_wasm_deps}\")
        foreach(_lib \${_libs})
            set(_path \"${_vcpkg_lib_dir}/\${_lib}\")
            if(EXISTS \"\${_path}\")
                file(INSTALL \"\${_path}\" DESTINATION \"\${CMAKE_INSTALL_PREFIX}/wasm/native\")
            else()
                message(WARNING \"WASM dependency not found: \${_path}\")
            endif()
        endforeach()
    ")
elseif(WIN32)
    set(XBIM_RID "win-x64")
    set(XBIM_NATIVE_GLOB "*.dll")
else()
    set(XBIM_RID "linux-x64")
    set(XBIM_NATIVE_GLOB "*.so*")
endif()

# The remaining install steps apply only to desktop platforms (shared libraries).
# WASM installs are handled above.
if(NOT EMSCRIPTEN)
    # On Linux/macOS, look for dependencies in the same directory as the library.
    if(NOT WIN32)
        set_target_properties(xbim_geometry_native PROPERTIES
            INSTALL_RPATH "$ORIGIN"
        )
    endif()

    install(TARGETS xbim_geometry_native
        RUNTIME DESTINATION "runtimes/${XBIM_RID}/native"
        LIBRARY DESTINATION "runtimes/${XBIM_RID}/native"
    )

    # Also install OCCT runtime dependencies alongside our native library.
    if(WIN32)
        # On Windows, vcpkg copies dependent DLLs to the build output directory.
        install(CODE "
            file(GLOB _build_dlls \"$<TARGET_FILE_DIR:xbim_geometry_native>/${XBIM_NATIVE_GLOB}\")
            list(FILTER _build_dlls EXCLUDE REGEX \"test_[^/]*$\")
            list(FILTER _build_dlls EXCLUDE REGEX \"xbim_geometry_native\")
            file(INSTALL \${_build_dlls} DESTINATION \"\${CMAKE_INSTALL_PREFIX}/runtimes/${XBIM_RID}/native\")
        ")
    else()
        # On Linux, vcpkg keeps .so files in its own lib directory — they are not
        # copied to the build output like Windows DLLs. Copy them from vcpkg.
        install(CODE "
            file(GLOB _vcpkg_libs \"${CMAKE_CURRENT_BINARY_DIR}/vcpkg_installed/${VCPKG_TARGET_TRIPLET}/lib/libTK*.so*\")
            file(INSTALL \${_vcpkg_libs} DESTINATION \"\${CMAKE_INSTALL_PREFIX}/runtimes/${XBIM_RID}/native\")
        ")
    endif()
endif()

