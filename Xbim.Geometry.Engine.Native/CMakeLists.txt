cmake_minimum_required(VERSION 3.20)
project(xbim_geometry_native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Build as shared library
set(BUILD_SHARED_LIBS ON)

# Platform-specific export macros
if(WIN32)
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

# Debug-only: OCCT visualization for shape inspection
set(XBIM_OCCT_VIZ_LIBS
    TKService
    TKOpenGl
    TKV3d
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

target_link_libraries(xbim_geometry_native PRIVATE ${XBIM_OCCT_LIBS})

# Link visualization libs only in Debug builds
target_link_libraries(xbim_geometry_native PRIVATE
    $<$<CONFIG:Debug>:${XBIM_OCCT_VIZ_LIBS}>
)
target_compile_definitions(xbim_geometry_native PRIVATE
    $<$<CONFIG:Debug>:XBIM_DEBUG_VIZ>
)

# Output naming
set_target_properties(xbim_geometry_native PROPERTIES
    OUTPUT_NAME "xbim_geometry_native"
    PREFIX ""
    DEBUG_POSTFIX ""
)

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

# GCC/Clang settings for future Linux support
if(CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
    target_compile_options(xbim_geometry_native PRIVATE
        -Wall
        -Wextra
        -fvisibility=hidden
    )
endif()

# ── Install (stage native binaries for NuGet packaging) ─────────────────────
# Usage: cmake --install build --config Release --prefix ../Xbim.Geometry.Engine.Interop
# This stages native DLLs into runtimes/{rid}/native/ for NuGet package inclusion.
if(WIN32)
    set(XBIM_RID "win-x64")
    set(XBIM_NATIVE_GLOB "*.dll")
else()
    set(XBIM_RID "linux-x64")
    set(XBIM_NATIVE_GLOB "*.so*")
endif()

install(TARGETS xbim_geometry_native
    RUNTIME DESTINATION "runtimes/${XBIM_RID}/native"
    LIBRARY DESTINATION "runtimes/${XBIM_RID}/native"
)

# Also install OCCT runtime dependencies alongside our native library.
# vcpkg copies dependent DLLs to the build output directory; we need them
# in the runtimes/ layout too for NuGet packaging.
install(CODE "
    file(GLOB _build_dlls \"$<TARGET_FILE_DIR:xbim_geometry_native>/${XBIM_NATIVE_GLOB}\")
    list(FILTER _build_dlls EXCLUDE REGEX \"test_[^/]*$\")
    list(FILTER _build_dlls EXCLUDE REGEX \"xbim_geometry_native\")
    file(INSTALL \${_build_dlls} DESTINATION \"\${CMAKE_INSTALL_PREFIX}/runtimes/${XBIM_RID}/native\")
")

