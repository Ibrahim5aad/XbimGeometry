Branch | Build Status  | MyGet | NuGet
------ | ------- | --- | --- |
Master | [![Build Status](https://dev.azure.com/xBIMTeam/xBIMToolkit/_apis/build/status/xBimTeam.XbimGeometry?branchName=master)](https://dev.azure.com/xBIMTeam/xBIMToolkit/_build/latest?definitionId=3&branchName=master) | ![master](https://img.shields.io/myget/xbim-master/v/Xbim.Geometry.svg) | ![](https://img.shields.io/nuget/v/Xbim.Geometry.svg)
Develop | [![Build Status](https://dev.azure.com/xBIMTeam/xBIMToolkit/_apis/build/status/xBimTeam.XbimGeometry?branchName=develop)](https://dev.azure.com/xBIMTeam/xBIMToolkit/_build/latest?definitionId=3&branchName=develop) | ![](https://img.shields.io/myget/xbim-develop/vpre/Xbim.Geometry.svg) | -


# XbimGeometry

XbimGeometry is part of the [Xbim Toolkit](https://github.com/xBimTeam). 

It contains the the Geometry Engine and Scene processing, which provide geometric and topological operations 
to enable users to visualise models in 3D models, typically as a Tesselated scene or mesh.

The native Geometry Engine is built around the open source [Open Cascade library](https://www.opencascade.com/content/overview)
which performs much of the boolean operations involved in generating 3D solids. 
This technology is included under a licence which permits the use as part of a larger work, compatible with our open source CDDL licence.

## Getting started

Before using this library you should register the Geometry Engine with the xbim ServiceProvider.

```csharp
	// Either configure the internal Services
	XbimServices.Current.ConfigureServices(opt => opt.AddXbimToolkit(conf => 
		conf.AddGeometryServices()
		));

	// or configure your services and register the provider with xbim:
	
	services.AddXbimToolkit(conf => conf.AddGeometryServices());
	// Once the DI container is built
	XbimServices.Current.UseExternalServiceProvider(serviceProvider);
```


## Compilation

### Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| [Visual Studio 2022](https://visualstudio.microsoft.com/downloads/) | 17.x | Community Edition is fine. Install the **Desktop development with C++** workload. |
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0+ | For the managed projects. |
| [CMake](https://cmake.org/download/) | 3.20+ | For the native library. Included with VS 2022 C++ workload. |
| [vcpkg](https://github.com/microsoft/vcpkg) | latest | C++ package manager for OpenCASCADE and other native dependencies. |
| [Git](https://git-scm.com/) | any | Required by vcpkg internally. |

### Setting up vcpkg

```bash
git clone https://github.com/microsoft/vcpkg.git
cd vcpkg && bootstrap-vcpkg.bat   # Windows
cd vcpkg && ./bootstrap-vcpkg.sh  # Linux
```

Set the `VCPKG_ROOT` environment variable to your vcpkg installation directory. The CMake presets
reference this variable to locate the vcpkg toolchain file.

### Building the Native Library

The `Xbim.Geometry.Engine.Native` project is a CMake-based C/C++ shared library that uses vcpkg
for dependency management (OpenCASCADE 7.9.3, FreeType, etc.).

**Windows (x64):**

```bash
cd src/Xbim.Geometry.Engine.Native
cmake --preset win-x64-release
cmake --build build --config Release
```

**Linux (x64):**

```bash
cd src/Xbim.Geometry.Engine.Native
cmake --preset linux-x64-release
cmake --build build --config Release
```

On Linux, you can also use system-installed OCCT packages (e.g. `apt install libocct-*-dev` on Ubuntu)
instead of vcpkg. The `find_package(OpenCASCADE)` call works with both vcpkg and system installs.

On first configure, vcpkg will automatically download and build OpenCASCADE and its dependencies. This
can take 30+ minutes on the first run but is cached for subsequent builds.

### Building the Managed Solution

```bash
dotnet build Xbim.Geometry.Engine.sln
```

No C++ toolchain is needed to build the managed projects — they use P/Invoke to call the native library.
NuGet packages are restored automatically. The [nuget.config](nuget.config) file adds the xbim MyGet
feeds for *master* and *develop* builds.

### Running Tests

```bash
dotnet test Xbim.Geometry.Engine.sln
```


## Acknowledgements
We'd like to acknowledge OpenCascade for the use of their library, which is permitted under clause 6 of [their
Licence](https://www.opencascade.com/content/licensing). 

The XbimTeam wishes to thank [JetBrains](https://www.jetbrains.com/) for supporting the XbimToolkit project 
with free open source [Resharper](https://www.jetbrains.com/resharper/) licenses.

Thanks also to Microsoft Azure DevOps for the use of [Azure Pipelines](https://azure.microsoft.com/en-us/services/devops/pipelines/) 
to automate our builds.

## Getting Involved

If you'd like to get involved and contribute to this project, please read the [CONTRIBUTING ](https://github.com/xBimTeam/XbimEssentials/blob/master/CONTRIBUTING.md)
page or contact any member of the @xbimTeam
