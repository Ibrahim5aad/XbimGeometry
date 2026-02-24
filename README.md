[![Build & Test](https://github.com/xBimTeam/XbimGeometry/actions/workflows/ci.yml/badge.svg?branch=native-engine)](https://github.com/xBimTeam/XbimGeometry/actions/workflows/ci.yml)
[![Publish NuGet](https://github.com/xBimTeam/XbimGeometry/actions/workflows/publish.yml/badge.svg)](https://github.com/xBimTeam/XbimGeometry/actions/workflows/publish.yml)
[![Version](https://img.shields.io/github/v/tag/xBimTeam/XbimGeometry?filter=v*&label=version&sort=semver)](https://github.com/xBimTeam/XbimGeometry/packages)
[![License: CDDL-1.0](https://img.shields.io/badge/license-CDDL--1.0-blue)](LICENCE.md)

# XbimGeometry

XbimGeometry is part of the [Xbim Toolkit](https://github.com/xBimTeam).
It provides geometric and topological operations for IFC building models — boolean operations, tessellation, and 3D scene generation — built on top of [OpenCASCADE](https://www.opencascade.com/content/overview).

---

## Installation

Pre-release packages are published to **GitHub Packages**. GitHub requires authentication
even for public packages, so you need a Personal Access Token.

1. [Create a PAT](https://github.com/settings/tokens) with the **`read:packages`** scope.

2. Add the xBimTeam feed:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/xBimTeam/index.json" \
  --name xbim-github \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text
```

3. Install the package:

```bash
dotnet add package Xbim.Geometry.Engine.Interop --prerelease
```

Native binaries for Windows and Linux are pulled in automatically via runtime packages.

> **Note:** `--store-password-in-clear-text` is required on Linux/macOS. On Windows you can omit it.

<details>
<summary><b>Alternative: nuget.config</b></summary>

```xml
<configuration>
  <packageSources>
    <add key="xbim-github" value="https://nuget.pkg.github.com/xBimTeam/index.json" />
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

Register the Geometry Engine with the xbim service provider:

```csharp
// Option 1: use the built-in service container
XbimServices.Current.ConfigureServices(opt =>
    opt.AddXbimToolkit(conf => conf.AddGeometryServices()));

// Option 2: use your own DI container
services.AddXbimToolkit(conf => conf.AddGeometryServices());
// after building the container:
XbimServices.Current.UseExternalServiceProvider(serviceProvider);
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

### Native Library

The native engine is a CMake C/C++ shared library using vcpkg for dependencies
(OpenCASCADE 7.9.3, FreeType, etc.).

**Windows:**
```bash
cd src/Xbim.Geometry.Engine.Native
cmake --preset win-x64-release
cmake --build build --config Release
```

**Linux:**
```bash
cd src/Xbim.Geometry.Engine.Native
cmake --preset linux-x64-release
cmake --build build --config Release
```

> On first run, vcpkg downloads and builds OpenCASCADE — this can take 30+ minutes but
> is cached for subsequent builds.

### Managed Solution

```bash
dotnet build Xbim.Geometry.Engine.sln
```

No C++ toolchain is needed for the managed projects — they call the native library via P/Invoke.

### Tests

```bash
dotnet test Xbim.Geometry.Engine.sln
```

---

## Acknowledgements

We'd like to acknowledge OpenCascade for the use of their library, which is permitted under clause 6 of [their
Licence](https://www.opencascade.com/content/licensing).

The XbimTeam wishes to thank [JetBrains](https://www.jetbrains.com/) for supporting the XbimToolkit project
with free open source [Resharper](https://www.jetbrains.com/resharper/) licenses.

Thanks also to [GitHub Actions](https://github.com/features/actions) for automating our builds and package publishing.

---

## Contributing

If you'd like to get involved, please read [CONTRIBUTING](https://github.com/xBimTeam/XbimEssentials/blob/master/CONTRIBUTING.md)
or contact any member of the [@xbimTeam](https://github.com/xBimTeam).
