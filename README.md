[![Build & Test](https://github.com/xBimTeam/XbimGeometry/actions/workflows/ci.yml/badge.svg?branch=native-engine)](https://github.com/xBimTeam/XbimGeometry/actions/workflows/ci.yml)
[![Publish NuGet](https://github.com/xBimTeam/XbimGeometry/actions/workflows/publish.yml/badge.svg)](https://github.com/xBimTeam/XbimGeometry/actions/workflows/publish.yml)
[![Version](https://img.shields.io/github/v/tag/xBimTeam/XbimGeometry?filter=v*&label=version&sort=semver)](https://github.com/xBimTeam/XbimGeometry/packages)
[![License: CDDL-1.0](https://img.shields.io/badge/license-CDDL--1.0-blue)](LICENCE.md)

# XbimGeometry

XbimGeometry is part of the [Xbim Toolkit](https://github.com/xBimTeam).
It provides geometric and topological operations for IFC building models — boolean operations, tessellation, and 3D scene generation — built on top of [OpenCASCADE](https://www.opencascade.com/content/overview).

---

## Packages

| Package | Description |
|---------|-------------|
| **Xbim.Geometry.Engine** | Core geometry engine — boolean operations, solid/shell/face building, tessellation. Automatically pulls in the native runtime packages for your platform. |
| **Xbim.Geometry.Scene** | 3D scene construction from IFC models — `Xbim3DModelContext`, WexBIM export, placement trees, mesh layers. |
| **Xbim.Tessellator** | Standalone managed tessellator for pre-meshed IFC representations (`IfcTriangulatedFaceSet`, `IfcFacetedBrep`, etc.) without requiring the native engine. |
| **Xbim.Geometry.Abstractions** | Interfaces and abstractions (`IXShape`, `IXSolid`, `IXCurve`, etc.) shared across geometry packages. |

For most use cases, reference **Xbim.Geometry.Scene** — it transitively brings in the Engine, Tessellator, and Abstractions.

## Installation

Pre-release packages are published to **GitHub Packages**. GitHub requires authentication
even for public packages, so you need a Personal Access Token.

1. [Create a PAT](https://github.com/settings/tokens) with the **`read:packages`** scope.

2. Add the xBimTeam feed:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/ibrahim5aad/index.json" \
  --name ibrahim-saad-github \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text
```

3. Install the packages:

```bash
# Full scene support (most common — includes Engine, Tessellator, and Abstractions)
dotnet add package Xbim.Geometry.Scene --prerelease

# Or just the engine if you don't need scene/WexBIM support
dotnet add package Xbim.Geometry.Engine --prerelease
```

Native binaries for Windows and Linux are pulled in automatically via runtime packages.

> **Note:** `--store-password-in-clear-text` is required on Linux/macOS. On Windows you can omit it.

<details>
<summary><b>Alternative: nuget.config</b></summary>

```xml
<configuration>
  <packageSources>
    <add key="xbim-github" value="https://nuget.pkg.github.com/Ibrahim5aad/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <xbim-github>
      <add key="Username" value="YOUR_GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="YOUR_GITHUB_PAT" />
    </xbim-github>
  </packageSourceCredentials>
</configuration>
```

</details>

---

## Usage

### Service Registration

Register the geometry engine with the xbim service provider:

```csharp
// Option 1: use the built-in service container
XbimServices.Current.ConfigureServices(opt =>
    opt.AddXbimToolkit(conf => conf.AddGeometryServices()));

// Option 2: use your own DI container
services.AddXbimToolkit(conf => conf.AddGeometryServices());
// after building the container:
XbimServices.Current.UseExternalServiceProvider(serviceProvider);
```

### Opening a Model and Creating Geometry

```csharp
using var model = IfcStore.Open("SampleHouse.ifc");

var loggerFactory = new LoggerFactory();
var geomEngine = new XbimGeometryEngine(model, loggerFactory);
var logger = loggerFactory.CreateLogger("MyApp");

// Build a solid from an IFC element
var extrudedSolid = model.Instances.OfType<IIfcExtrudedAreaSolid>().First();
IXbimSolid solid = geomEngine.CreateSolid(extrudedSolid, logger);

Console.WriteLine($"Volume: {solid.Volume}");
Console.WriteLine($"Faces:  {solid.Faces.Count}");
```

### Tessellation

```csharp
// Tessellate a geometry object into a triangulated mesh
XbimShapeGeometry mesh = geomEngine.CreateShapeGeometry(
    solid,
    model.ModelFactors.Precision,
    model.ModelFactors.DeflectionTolerance,
    model.ModelFactors.DeflectionAngle,
    XbimGeometryType.PolyhedronBinary,
    logger);

XbimRect3D boundingBox = mesh.BoundingBox;
```

### Full Scene Export to WexBIM

The most common workflow — convert an entire IFC model into a WexBIM file
for web or desktop viewing:

```csharp
using var model = IfcStore.Open("SampleHouse.ifc");

var context = new Xbim3DModelContext(model);
context.CreateContext();   // builds model scene

using var fs = File.Create("output.wexbim");
using var bw = new BinaryWriter(fs);
model.SaveAsWexBim(bw);
```

By default, `CreateContext()` sets `generateBREPs: false`. In this mode, IFC shapes
that are already tessellated (`IfcTriangulatedFaceSet`, `IfcPolygonalFaceSet`,
`IfcFaceBasedSurfaceModel`, `IfcShellBasedSurfaceModel`, `IfcFacetedBrep`, etc.)
are meshed directly by the built-in `XbimTessellator` without constructing full BREP
solids through the geometry engine. This is significantly faster for models that
consist mostly of pre-tessellated geometry.

Pass `generateBREPs: true` to force all shapes through the geometry engine's full
BREP pipeline before tessellation. This produces higher-fidelity meshes at the cost
of longer processing times:

```csharp
context.CreateContext(generateBREPs: true);
```

---

## Building from Source

<details>
<summary><b>Prerequisites</b></summary>

| Tool | Version | Notes |
|------|---------|-------|
| [Visual Studio 2022](https://visualstudio.microsoft.com/downloads/) | 17.x | Community Edition is fine. Install the **Desktop development with C++** workload. |
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0+ | For the managed projects. |
| [CMake](https://cmake.org/download/) | 3.20+ | Included with the VS 2022 C++ workload. |
| [vcpkg](https://github.com/microsoft/vcpkg) | latest | C++ package manager for OpenCASCADE and other native dependencies. |
| [Git](https://git-scm.com/) | any | Required by vcpkg. |

</details>

### Setting up vcpkg

```bash
git clone https://github.com/microsoft/vcpkg.git
cd vcpkg && bootstrap-vcpkg.bat   # Windows
cd vcpkg && ./bootstrap-vcpkg.sh  # Linux
```

Set the `VCPKG_ROOT` environment variable to the vcpkg directory. The CMake presets
reference it to locate the toolchain file.

### Build Scripts

Helper scripts handle the full native + managed + test pipeline:

**Windows (PowerShell):**
```powershell
.\scripts\build-windows.ps1              # full build + test
.\scripts\build-windows.ps1 -Native      # native C++ only
.\scripts\build-windows.ps1 -Test        # .NET test only (assumes native already built)
.\scripts\build-windows.ps1 -Clean       # wipe build artifacts and start fresh
.\scripts\build-windows.ps1 -Config Debug  # build Debug instead of Release
```

**Linux:**
```bash
./scripts/build-linux.sh                 # full build + test
./scripts/build-linux.sh --native        # native C++ only
./scripts/build-linux.sh --test          # .NET test only
./scripts/build-linux.sh --clean         # wipe build artifacts and start fresh
```

> On first run, vcpkg downloads and builds OpenCASCADE — this can take 30+ minutes but
> is cached for subsequent builds.

### Manual Build

If you prefer running the steps yourself:

**1. Build the native library:**

```bash
cd src/Xbim.Geometry.Engine.Native

# Windows
cmake --preset win-x64-release
cmake --build build --config Release

# Linux
cmake --preset linux-x64-release
cmake --build build --config Release -j $(nproc)
```

For Debug builds, use the `win-x64-debug` / `linux-x64-debug` preset (output goes to `build-debug/`).

**2. Stage native binaries for .NET:**

```bash
cmake --install build --config Release --prefix ../Xbim.Geometry.Engine
```

This copies the native library and its OCCT dependencies into
`src/Xbim.Geometry.Engine/runtimes/{rid}/native/`. The Engine project's
`Content` items pick them up from there — both for `dotnet pack` and for
transitive copy to test project output directories via `ProjectReference`.

**3. Build and test the managed solution:**

```bash
cd ../..   # back to repo root
dotnet build Xbim.Geometry.Engine.sln
dotnet test Xbim.Geometry.Engine.sln
```

---

## Examples

The `examples/` directory contains apps that demonstrate the geometry engine:

- **Xbim.Examples.QuickStart** — Minimal console app that exercises the README code samples: opens an IFC model, creates geometry, tessellates, and exports to WexBIM.
- **Xbim.Examples.WexBimConverter** — CLI tool that converts IFC files to the compact WexBIM binary format.
- **Xbim.Examples.Viewer** — Avalonia desktop app that opens and renders WexBIM files with OpenGL.

### Quick Start

```bash
# Build the example projects
dotnet build examples/Xbim.Examples.sln

# Convert an IFC file to WexBIM (requires native engine built and staged)
dotnet run --project examples/Xbim.Examples.WexBimConverter -- path/to/model.ifc

# Open the viewer
dotnet run --project examples/Xbim.Examples.Viewer.Desktop
```

A pre-generated `SampleHouse4.wexbim` is included in `examples/testdata/` for testing the viewer without needing to build the native engine.

### Smoke Test

```bash
# Windows
.\examples\smoke-test.ps1

# Linux / macOS
./examples/smoke-test.sh
```

Pass `--convert` (or `-Convert` on Windows) to also run the converter on a test IFC file.

---

## Acknowledgements

We'd like to acknowledge OpenCascade for the use of their library, which is permitted under clause 6 of [their Licence](https://www.opencascade.com/content/licensing).

The XbimTeam wishes to thank [JetBrains](https://www.jetbrains.com/) for supporting the XbimToolkit project with free open source [Resharper](https://www.jetbrains.com/resharper/) licenses.

Thanks also to [GitHub Actions](https://github.com/features/actions) for automating our builds and package publishing.

---

## Contributing

If you'd like to get involved, please read [CONTRIBUTING](https://github.com/xBimTeam/XbimEssentials/blob/master/CONTRIBUTING.md)
or contact any member of the [@xbimTeam](https://github.com/xBimTeam).
