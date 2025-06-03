# Xbim.Geometry.Engine.Interop

Cross-platform P/Invoke interop layer for the Xbim Geometry Engine native library (`xbim_geometry_native`).

This project replaces the legacy C++/CLI mixed-mode interop with a pure C# P/Invoke approach, enabling support for both Windows x64 and Linux x64.

## Architecture

- **XbimGeometryNativeApi**: Static class with `[DllImport]` declarations for all native C API functions
- **SafeHandle subclasses**: `NativeContextHandle`, `NativeShapeHandle`, `NativeLocationHandle` for safe native resource management
- **NativeLibraryLoader**: RID-aware native library resolution (`runtimes/win-x64/native/` and `runtimes/linux-x64/native/`)
- **Factory classes**: C# implementations of `IXSolidFactory`, `IXProfileFactory`, etc. that extract IFC data and call native functions via P/Invoke

## NuGet Packaging

The NuGet package includes pre-built native libraries for each supported platform under the standard `runtimes/{rid}/native/` layout. The .NET SDK automatically copies these to the consuming project's output directory.

### Building the NuGet package

1. Build the native library:
   ```bash
   cd Xbim.Geometry.Engine.Native
   cmake --preset win-x64-release
   cmake --build build-vcpkg --config Release
   ```

2. Stage native binaries into the `runtimes/` directory:
   ```bash
   cmake --install build-vcpkg --config Release --prefix ../Xbim.Geometry.Engine.Interop
   ```

3. Pack the NuGet package:
   ```bash
   dotnet pack Xbim.Geometry.Engine.Interop -c Release
   ```

### Package structure

```
Xbim.Geometry.Engine.Interop.nupkg
├── lib/net8.0/                          # Managed assembly
├── runtimes/win-x64/native/             # Windows x64 native libraries
│   ├── xbim_geometry_native.dll
│   ├── TKernel.dll, TKMath.dll, ...     # OCCT dependencies
├── runtimes/linux-x64/native/           # Linux x64 native libraries (when built)
│   ├── libxbim_geometry_native.so
│   └── ...
└── buildTransitive/                     # MSBuild targets for transitive consumers
```
