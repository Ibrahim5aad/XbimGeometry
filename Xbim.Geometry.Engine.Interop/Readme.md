# Xbim.Geometry.Engine.Interop

Cross-platform P/Invoke interop layer for the Xbim Geometry Engine native library (`xbim_geometry_native`).

This project replaces the legacy C++/CLI mixed-mode interop with a pure C# P/Invoke approach, enabling support for both Windows x64 and Linux x64.

## Architecture

- **NativeMethods**: Static class with `[DllImport]` declarations for all native C API functions
- **SafeHandle subclasses**: `NativeContextHandle`, `NativeShapeHandle`, `NativeLocationHandle` for safe native resource management
- **NativeLibraryLoader**: RID-aware native library resolution (`runtimes/win-x64/native/` and `runtimes/linux-x64/native/`)
- **Factory classes**: C# implementations of `IXSolidFactory`, `IXProfileFactory`, etc. that extract IFC data and call native functions via P/Invoke
