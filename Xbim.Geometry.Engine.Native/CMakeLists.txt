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

# ── OpenCASCADE 7.8.1 (from Xbim.Geometry.Occt NuGet package) ──────────────
# The OCCT headers and pre-built libraries live in the NuGet packages folder.
# OCCT_ROOT can be overridden on the command line; default resolves relative
# to this CMakeLists.txt (repo root / packages / ...).
if(NOT DEFINED OCCT_ROOT)
    set(OCCT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../packages/Xbim.Geometry.Occt.7.8.1")
endif()

set(OCCT_INCLUDE_DIR "${OCCT_ROOT}/build/native/include")

if(WIN32)
    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
        set(OCCT_LIB_DIR "${OCCT_ROOT}/lib/native/v143/win-x64/Release")
    else()
        set(OCCT_LIB_DIR "${OCCT_ROOT}/lib/native/v143/win-x86/Release")
    endif()
else()
    # Linux: expect OCCT_LIB_DIR to be set externally
    if(NOT DEFINED OCCT_LIB_DIR)
        message(FATAL_ERROR "OCCT_LIB_DIR must be set for non-Windows builds")
    endif()
endif()

# Verify the paths exist
if(NOT EXISTS "${OCCT_INCLUDE_DIR}")
    message(FATAL_ERROR "OCCT include directory not found: ${OCCT_INCLUDE_DIR}\n"
                        "Set OCCT_ROOT to the Xbim.Geometry.Occt package root.")
endif()
if(NOT EXISTS "${OCCT_LIB_DIR}")
    message(FATAL_ERROR "OCCT library directory not found: ${OCCT_LIB_DIR}\n"
                        "Set OCCT_ROOT to the Xbim.Geometry.Occt package root.")
endif()

# OCCT libraries required by this project.
# Grouped by OCCT module for clarity.
set(OCCT_LIBRARIES
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
    TKBOOL
    TKPrim
    TKMesh
    TKOffset
    TKFillet
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
        ${OCCT_INCLUDE_DIR}
)

# Link OCCT libraries
foreach(_lib ${OCCT_LIBRARIES})
    if(WIN32)
        target_link_libraries(xbim_geometry_native PRIVATE "${OCCT_LIB_DIR}/${_lib}.lib")
    else()
        target_link_libraries(xbim_geometry_native PRIVATE "${OCCT_LIB_DIR}/lib${_lib}.so")
    endif()
endforeach()

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

# Copy OCCT DLLs to output directory for runtime loading
if(WIN32)
    file(GLOB OCCT_DLLS "${OCCT_LIB_DIR}/*.dll")
    foreach(_dll ${OCCT_DLLS})
        add_custom_command(TARGET xbim_geometry_native POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_dll}" "$<TARGET_FILE_DIR:xbim_geometry_native>"
            COMMENT "Copying ${_dll}"
        )
    endforeach()
endif()
