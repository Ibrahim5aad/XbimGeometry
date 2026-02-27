set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_CMAKE_SYSTEM_NAME Linux)

# OCCT links jemalloc_pic (the static PIC variant) regardless of its own
# linkage type. Keep jemalloc as a static library.
if(PORT STREQUAL "jemalloc")
    set(VCPKG_LIBRARY_LINKAGE static)
endif()


if(PORT STREQUAL "opencascade")
    set(VCPKG_CXX_FLAGS "-ftls-model=global-dynamic")
    set(VCPKG_C_FLAGS "-ftls-model=global-dynamic")
endif()
