string(REPLACE "." "_" VERSION_STR "V${VERSION}")
vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO Open-Cascade-SAS/OCCT
    REF "${VERSION_STR}"
    SHA512 65935a2f46021e2b9a7dd2a218515c06925454855a8cc952fcbd1cccbfd5c8d605ed8f1d930d2aec87ef172ded551d0a237fad128319fb9cbcabdc755aa0aa67
    HEAD_REF master
    PATCHES
        fix-install-prefix-path.patch
        drop-bin-letter-d.patch
        dependencies.patch
        install-include-dir.patch
        remove-vcpkg-enabling.patch
        disable-signal-conversion-emscripten.patch
        wasm-exceptions.patch
        noop-mutex-emscripten.patch
)

if (VCPKG_LIBRARY_LINKAGE STREQUAL "dynamic")
    set(BUILD_TYPE "Shared")
else()
    set(BUILD_TYPE "Static")
endif()

vcpkg_check_features(OUT_FEATURE_OPTIONS FEATURE_OPTIONS
    FEATURES
        freeimage   USE_FREEIMAGE
        freetype    USE_FREETYPE
        rapidjson   USE_RAPIDJSON
        samples     INSTALL_SAMPLES
        tbb         USE_TBB
        vtk         USE_VTK
)

# USE_MMGR_TYPE is a string option, not boolean — handle separately.
# Emscripten does not support jemalloc — use native allocator (dlmalloc).
if(VCPKG_CMAKE_SYSTEM_NAME STREQUAL "Emscripten")
    list(APPEND FEATURE_OPTIONS "-DUSE_MMGR_TYPE=NATIVE")
elseif("jemalloc" IN_LIST FEATURES)
    list(APPEND FEATURE_OPTIONS "-DUSE_MMGR_TYPE=JEMALLOC")
endif()

# Build only the OCCT modules we actually use:
#   FoundationClasses, ModelingData, ModelingAlgorithms,
#   DataExchange, ApplicationFramework
# DataExchange (TKDESTEP) depends on ApplicationFramework (TKCAF, TKLCAF, TKXCAF),
# which transitively depends on TKService/TKV3d from Visualization.
# We disable the Visualization module but those toolkits are still built as
# transitive deps. USE_XLIB=OFF and USE_OPENGL=OFF prevent X11/GL headers
# being required for TKService on Linux.
# Disabled modules:
#   Draw               — requires TCL
#   DETools            — development/debugging tools
#   Visualization      — OpenGL/X11 windowing
vcpkg_cmake_configure(
    SOURCE_PATH "${SOURCE_PATH}"
    OPTIONS
        ${FEATURE_OPTIONS}
        -DBUILD_LIBRARY_TYPE=${BUILD_TYPE}
        -DBUILD_MODULE_Draw=OFF
        -DBUILD_MODULE_DETools=OFF
        -DBUILD_MODULE_Visualization=OFF
        -DUSE_XLIB=OFF
        -DUSE_OPENGL=OFF
        -DBUILD_DOC_Overview=OFF
        -DINSTALL_DIR_LAYOUT=Unix
        -DINSTALL_DIR_DOC=share/trash
        -DINSTALL_DIR_SCRIPT=share/trash # not relocatable
        -DINSTALL_TEST_CASES=OFF
        -DUSE_TK=OFF
    OPTIONS_DEBUG
        -DINSTALL_SAMPLES=OFF
)

vcpkg_cmake_install()

vcpkg_cmake_config_fixup(CONFIG_PATH lib/cmake/opencascade)

#make occt includes relative to source_file
file(GLOB extra_headers
    LIST_DIRECTORIES false
    RELATIVE "${CURRENT_PACKAGES_DIR}/include/opencascade"
    "${CURRENT_PACKAGES_DIR}/include/opencascade/*.h"
)
list(JOIN extra_headers "|" extra_headers)
file(GLOB files "${CURRENT_PACKAGES_DIR}/include/opencascade/*.[hgl]xx")
foreach(file_name IN LISTS files)
    vcpkg_replace_string("${file_name}" "(# *include) <([a-zA-Z0-9_]*[.][hgl]xx|${extra_headers})>" [[\1 "\2"]] REGEX IGNORE_UNCHANGED)
endforeach()

if(VCPKG_LIBRARY_LINKAGE STREQUAL "static")
    vcpkg_replace_string("${CURRENT_PACKAGES_DIR}/include/opencascade/Standard_Macro.hxx" "defined(OCCT_STATIC_BUILD)" "(1)")
endif()

file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/debug/include")
file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/debug/share")
file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/share/opencascade/samples/qt")
file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/share/trash")

vcpkg_install_copyright(
    FILE_LIST
        "${SOURCE_PATH}/LICENSE_LGPL_21.txt"
        "${SOURCE_PATH}/OCCT_LGPL_EXCEPTION.txt"
)
