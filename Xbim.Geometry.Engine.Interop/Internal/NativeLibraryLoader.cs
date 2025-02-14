using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Xbim.Geometry.Engine.Interop.Internal
{
    /// <summary>
    /// Handles RID-aware loading of the xbim_geometry_native shared library.
    /// Registers a <see cref="DllImportResolver"/> so that P/Invoke calls to
    /// "xbim_geometry_native" resolve to the correct platform binary.
    /// </summary>
    internal static class NativeLibraryLoader
    {
        /// <summary>
        /// The library name used in [DllImport] declarations throughout the interop layer.
        /// </summary>
        internal const string LibraryName = "xbim_geometry_native";

        private static int _initialized;

        /// <summary>
        /// Ensures the DLL import resolver is registered exactly once.
        /// Safe to call from multiple threads.
        /// </summary>
        internal static void EnsureLoaded()
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
                return;

            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, ResolveDllImport);
        }

        private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != LibraryName)
                return IntPtr.Zero;

            // Try the RID-specific runtime path first (NuGet package layout)
            string? ridPath = GetRidNativePath();
            if (ridPath != null && NativeLibrary.TryLoad(ridPath, out IntPtr handle))
                return handle;

            // Try the application base directory (flat publish / dev builds)
            string basePath = Path.Combine(AppContext.BaseDirectory, GetPlatformLibraryName());
            if (NativeLibrary.TryLoad(basePath, out handle))
                return handle;

            // Try the assembly directory (in case it differs from AppContext.BaseDirectory)
            string? assemblyDir = Path.GetDirectoryName(typeof(NativeLibraryLoader).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                string asmPath = Path.Combine(assemblyDir, GetPlatformLibraryName());
                if (NativeLibrary.TryLoad(asmPath, out handle))
                    return handle;
            }

            // Let the default loader try (LD_LIBRARY_PATH on Linux, PATH on Windows, etc.)
            if (NativeLibrary.TryLoad(LibraryName, assembly, searchPath, out handle))
                return handle;

            throw new DllNotFoundException(
                $"Unable to load native library '{LibraryName}'. " +
                $"Expected at: {ridPath ?? "(unknown RID path)"} or {basePath}. " +
                $"Platform: {RuntimeInformation.RuntimeIdentifier}, " +
                $"Architecture: {RuntimeInformation.ProcessArchitecture}");
        }

        /// <summary>
        /// Returns the full path to the native library under the NuGet runtimes/ layout,
        /// or null if the path cannot be determined.
        /// </summary>
        private static string? GetRidNativePath()
        {
            string rid = GetRuntimeIdentifier();
            string libName = GetPlatformLibraryName();

            // Check relative to the application base directory
            // Layout: runtimes/{rid}/native/{libname}
            string path = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", libName);
            if (File.Exists(path))
                return path;

            // Check relative to assembly location
            string? assemblyDir = Path.GetDirectoryName(typeof(NativeLibraryLoader).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                path = Path.Combine(assemblyDir, "runtimes", rid, "native", libName);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Returns the platform-specific library file name.
        /// </summary>
        internal static string GetPlatformLibraryName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "xbim_geometry_native.dll";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "libxbim_geometry_native.so";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "libxbim_geometry_native.dylib";

            return LibraryName;
        }

        /// <summary>
        /// Returns the runtime identifier string for the current platform.
        /// </summary>
        internal static string GetRuntimeIdentifier()
        {
            string os;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                os = "win";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                os = "linux";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                os = "osx";
            else
                os = "unknown";

            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
            };

            return $"{os}-{arch}";
        }
    }
}
