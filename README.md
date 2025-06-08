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

**Visual Studio 2022 is recommended.**
Prior versions of Visual Studio are unlikely to work on this solution.

The [free VS 2022 Community Edition](https://visualstudio.microsoft.com/downloads/) will be fine.

The managed solution (`.sln`) requires .NET 8.0 SDK. No C++ toolchain is needed to build the managed
projects — they use P/Invoke to call the pre-built native library.

The XBIM toolkit [uses the NuGet](https://www.nuget.org/packages/Xbim.Geometry/) for the management of our published packages.
We have custom MyGet feeds for the *master* and *develop* branches of the solution which are automatically
updated during our CI builds. The [nuget.config](nuget.config) file should automatically add these feeds for you.

### Building the Native Library (cross-platform)

The `Xbim.Geometry.Engine.Native` project is a standalone CMake-based C/C++ shared library that uses
[vcpkg](https://github.com/microsoft/vcpkg) for dependency management (OpenCASCADE, FreeType, etc.).

**Prerequisites:**

1. Install [vcpkg](https://github.com/microsoft/vcpkg):
   ```bash
   git clone https://github.com/microsoft/vcpkg.git
   cd vcpkg && bootstrap-vcpkg.bat   # Windows
   cd vcpkg && ./bootstrap-vcpkg.sh  # Linux
   ```
2. Set the `VCPKG_ROOT` environment variable to your vcpkg installation directory.
3. CMake 3.20+ and a C++17 compiler (MSVC 2022 on Windows, GCC/Clang on Linux).

**Windows (x64):**

```bash
cd Xbim.Geometry.Engine.Native
cmake --preset win-x64-release
cmake --build build-vcpkg --config Release
```

**Linux (x64):**

```bash
cd Xbim.Geometry.Engine.Native
cmake --preset linux-x64-release
cmake --build build-vcpkg --config Release
```

On Linux, you can also use system-installed OCCT packages (e.g. `apt install libocct-*-dev` on Ubuntu)
instead of vcpkg. The `find_package(OpenCASCADE)` call works with both vcpkg and system installs.

On first configure, vcpkg will automatically download and build OpenCASCADE and its dependencies. This
can take 30+ minutes on the first run but is cached for subsequent builds.


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
