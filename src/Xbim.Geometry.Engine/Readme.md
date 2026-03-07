# Xbim.Geometry.Engine

Cross-platform geometry engine for IFC models, built on OpenCASCADE Technology (OCCT).

Part of the [xBIM Toolkit](https://docs.xbim.net/) for working with Industry Foundation Classes (IFC) in .NET.

## Features

- IFC 2x3, IFC 4, and IFC 4x3 geometry support
- Solid modelling: extrusions, revolutions, booleans, CSG, swept solids
- Advanced and faceted BRep construction
- Tessellation of triangulated and polygonal face sets
- Profile and curve processing (composite curves, trimmed curves, B-splines)
- Full model geometry context creation via `Xbim3DModelContext`

## Installation

This package contains the managed engine only. You must also install the native runtime package for your target platform:

**Windows x64**
```
dotnet add package Xbim.Geometry.Engine
dotnet add package Xbim.Geometry.Engine.Native.runtime.win-x64
```

**Linux x64**
```
dotnet add package Xbim.Geometry.Engine
dotnet add package Xbim.Geometry.Engine.Native.runtime.linux-x64
```

**Blazor WebAssembly**
```
dotnet add package Xbim.Geometry.Engine
dotnet add package Xbim.Geometry.Engine.Native.runtime.browser-wasm
```

## Usage

```csharp
using Xbim.Geometry.Scene;
using Xbim.IO.Memory;

using var model = MemoryModel.OpenRead("model.ifc");
var context = new Xbim3DModelContext(model);
context.CreateContext();
```

## License

See the [xBIM licence](https://docs.xbim.net/license/license.html) for details.
