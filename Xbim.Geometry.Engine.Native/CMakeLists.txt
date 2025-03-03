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
