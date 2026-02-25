# Building & Testing on Linux (WSL)

## Prerequisites

```bash
sudo apt install cmake ninja-build g++
```

.NET 8 SDK: https://learn.microsoft.com/en-us/dotnet/core/install/linux

vcpkg:
```bash
git clone https://github.com/microsoft/vcpkg.git ~/vcpkg
~/vcpkg/bootstrap-vcpkg.sh
echo 'export VCPKG_ROOT=~/vcpkg' >> ~/.bashrc
source ~/.bashrc
```

---

## Quick Start

```bash
# Full build + test (native → stage → restore → build → test)
./scripts/build-linux.sh

# Native C++ only (cmake configure + build + install)
./scripts/build-linux.sh --native

# .NET tests only (assumes native already built)
./scripts/build-linux.sh --test

# Wipe all build artifacts
./scripts/build-linux.sh --clean
```

---

## Manual Steps

### 1. Build native library

```bash
cd src/Xbim.Geometry.Engine.Native
cmake --preset linux-x64-release
cmake --build build --config Release -j $(nproc)
```

### 2. Stage native binaries for .NET

```bash
cmake --install build --config Release --prefix ../Xbim.Geometry.Engine.Interop
```

This copies `libxbim_geometry_native.so` to `src/Xbim.Geometry.Engine.Interop/runtimes/linux-x64/native/`.

### 3. Build & test .NET

```bash
cd ~/XbimGeometry
dotnet restore Xbim.Geometry.Engine.sln
dotnet build Xbim.Geometry.Engine.sln -c Release --no-restore
dotnet test Xbim.Geometry.Engine.sln -c Release --no-build
```

---

## Copying Files from Windows to WSL

When editing on Windows and building in WSL, copy changed files from the mounted drive:

```bash
SRC=/mnt/d/Work/Xbim/Toolkit/Newfolder/XbimGeometry
DST=~/XbimGeometry

# Single file
cp "$SRC/path/to/file" "$DST/path/to/file"

# All .cs files in a directory
cp "$SRC/tests/Xbim.Geometry.IntegrationTests/*.cs" "$DST/tests/Xbim.Geometry.IntegrationTests/"

# Subdirectories too
cp "$SRC/tests/Xbim.Geometry.IntegrationTests/ModelGeometryServiceTests/*.cs" \
   "$DST/tests/Xbim.Geometry.IntegrationTests/ModelGeometryServiceTests/"
cp "$SRC/tests/Xbim.Geometry.IntegrationTests/IFC4x3Tests/*.cs" \
   "$DST/tests/Xbim.Geometry.IntegrationTests/IFC4x3Tests/"
```

---

## Troubleshooting

### `\r': No such file or directory` when running shell scripts

Windows line endings (`\r\n`) break bash scripts. Fix with:
```bash
sed -i 's/\r$//' ~/XbimGeometry/scripts/build-linux.sh
```

### `DllNotFoundException: Unable to load native library 'xbim_geometry_native'`

**Check the .so is in the right place:**
```bash
ls ~/XbimGeometry/tests/Xbim.Geometry.UnitTests/bin/Release/net8.0/runtimes/linux-x64/native/
```
Should contain `libxbim_geometry_native.so`. If not, make sure `cmake --install` ran and `dotnet build` was done (not just `--no-build`).

**Check for missing dependencies:**
```bash
ldd <path-to-libxbim_geometry_native.so>
```
All dependencies should show resolved paths. If any say "not found", those libraries need to be installed or placed alongside the .so.

**Get the actual dlopen error:**
```bash
python3 -c "import ctypes; ctypes.CDLL('<path-to-libxbim_geometry_native.so>')"
```
This prints the real `dlerror()` message that .NET's `NativeLibrary.TryLoad` swallows.

### `FileNotFoundException` with backslashes in path

Paths like `TestFiles\file.ifc` don't work on Linux — `\` is not a path separator. Use forward slashes in all test file paths: `TestFiles/file.ifc`. Forward slashes work on both Windows and Linux.

### Case-sensitive file paths

Linux filesystems are case-sensitive. `TestFiles/` and `testfiles/` are different directories. Make sure path strings match the actual directory casing.

### Rebuild after CMakeLists.txt changes

CMake auto-detects changes — just rebuild:
```bash
cmake --build build --config Release -j $(nproc)
```
No need to delete the cache. Only delete `build/CMakeCache.txt` if you changed presets or toolchain. Avoid `rm -rf build/` — that wipes the vcpkg cache and forces a full OCCT rebuild.

### GCC vs MSVC differences

Code that compiles on MSVC may fail on GCC:

- **Non-const lvalue reference to rvalue**: Store temporaries in local variables before passing by reference.
- **`static const` class members**: Use `static constexpr` instead — GCC requires out-of-class definitions for ODR-used `static const` members.
- **Dangling references**: `const T& x = temp().member()` — the temporary dies at the semicolon. Store by value: `const T x = ...`.
