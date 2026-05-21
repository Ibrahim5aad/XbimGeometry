# Wrapper toolchain that applies vcpkg flags before the Emscripten toolchain.
# vcpkg has no built-in Emscripten toolchain, so VCPKG_CXX_FLAGS/VCPKG_C_FLAGS
# are passed as -D defines but never applied to CMAKE_*_FLAGS_INIT.
# This wrapper bridges the gap.

if(DEFINED VCPKG_CXX_FLAGS AND NOT VCPKG_CXX_FLAGS STREQUAL "")
    string(APPEND CMAKE_CXX_FLAGS_INIT " ${VCPKG_CXX_FLAGS} ")
endif()
if(DEFINED VCPKG_C_FLAGS AND NOT VCPKG_C_FLAGS STREQUAL "")
    string(APPEND CMAKE_C_FLAGS_INIT " ${VCPKG_C_FLAGS} ")
endif()
if(DEFINED VCPKG_CXX_FLAGS_RELEASE AND NOT VCPKG_CXX_FLAGS_RELEASE STREQUAL "")
    string(APPEND CMAKE_CXX_FLAGS_RELEASE_INIT " ${VCPKG_CXX_FLAGS_RELEASE} ")
endif()
if(DEFINED VCPKG_C_FLAGS_RELEASE AND NOT VCPKG_C_FLAGS_RELEASE STREQUAL "")
    string(APPEND CMAKE_C_FLAGS_RELEASE_INIT " ${VCPKG_C_FLAGS_RELEASE} ")
endif()
if(DEFINED VCPKG_CXX_FLAGS_DEBUG AND NOT VCPKG_CXX_FLAGS_DEBUG STREQUAL "")
    string(APPEND CMAKE_CXX_FLAGS_DEBUG_INIT " ${VCPKG_CXX_FLAGS_DEBUG} ")
endif()
if(DEFINED VCPKG_C_FLAGS_DEBUG AND NOT VCPKG_C_FLAGS_DEBUG STREQUAL "")
    string(APPEND CMAKE_C_FLAGS_DEBUG_INIT " ${VCPKG_C_FLAGS_DEBUG} ")
endif()

# Locate the real Emscripten toolchain from the environment.
# EMSCRIPTEN_ROOT is set by the build script and passed through via
# VCPKG_ENV_PASSTHROUGH_UNTRACKED in the triplet.
if(NOT DEFINED ENV{EMSCRIPTEN_ROOT})
    message(FATAL_ERROR "EMSCRIPTEN_ROOT environment variable is not set")
endif()
include("$ENV{EMSCRIPTEN_ROOT}/cmake/Modules/Platform/Emscripten.cmake")
