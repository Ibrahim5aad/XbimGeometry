set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE static)
set(VCPKG_CMAKE_SYSTEM_NAME Linux)
set(VCPKG_BUILD_TYPE debug)

# Static libs are linked into a .so loaded via dlopen() by .NET.
# Default initial-exec TLS model exhausts the static TLS block.
set(VCPKG_C_FLAGS "-ftls-model=global-dynamic")
set(VCPKG_CXX_FLAGS "-ftls-model=global-dynamic")
