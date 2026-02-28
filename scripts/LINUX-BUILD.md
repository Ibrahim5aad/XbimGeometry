# Notes from working with Linux (WSL)  


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
This prints the real `dlerror()` message if .NET's `NativeLibrary.TryLoad` swallows it.



**Some common errors:**

1. `cannot allocate memory in static TLS block`

   This happens when OCCT shared libraries (e.g. `libTKernel.so.7.9`) are loaded too late for the system to allocate thread-local storage. Fix by preloading the native library before running your command e.g. (dotnet test):

   ```bash
   LD_PRELOAD=/path/to/runtimes/linux-x64/native/libxbim_geometry_native.so dotnet test
   ```

2. `FileNotFoundException` with backslashes in path

   Paths like `TestFiles\file.ifc` don't work on Linux — `\` is not a path separator. Use forward slashes in all test file paths: `TestFiles/file.ifc`. Forward slashes work on both Windows and Linux.

---

### Case-sensitive file paths

Linux filesystems are case-sensitive. `TestFiles/` and `testfiles/` are different directories. Make sure path strings match the actual directory casing.

---

### Rebuild after CMakeLists.txt changes

CMake auto-detects changes — just rebuild:
```bash
cmake --build build --config Release
```
No need to delete the cache. Only delete `build/CMakeCache.txt` if you changed presets or toolchain. Avoid `rm -rf build/` — that wipes the vcpkg cache and forces a full OCCT rebuild.

---

### GCC vs MSVC differences

Code that compiles on MSVC may fail on GCC:

- **Non-const lvalue reference to rvalue**: Store temporaries in local variables before passing by reference.
- **`static const` class members**: Use `static constexpr` instead — GCC requires out-of-class definitions for ODR-used `static const` members.
- **Dangling references**: `const T& x = temp().member()` — the temporary dies at the semicolon. Store by value: `const T x = ...`.
