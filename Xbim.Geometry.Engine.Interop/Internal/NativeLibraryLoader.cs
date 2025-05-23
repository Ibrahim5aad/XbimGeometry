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
            string basePath = Path.Combine(AppContext.BaseDirectory, PlatformInfo.NativeLibraryName);
            if (NativeLibrary.TryLoad(basePath, out handle))
                return handle;

            // Try the assembly directory (in case it differs from AppContext.BaseDirectory)
            string? assemblyDir = Path.GetDirectoryName(typeof(NativeLibraryLoader).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                string asmPath = Path.Combine(assemblyDir, PlatformInfo.NativeLibraryName);
                if (NativeLibrary.TryLoad(asmPath, out handle))
                    return handle;
            }

            // Let the default loader try (LD_LIBRARY_PATH on Linux, PATH on Windows, etc.)
            if (NativeLibrary.TryLoad(LibraryName, assembly, searchPath, out handle))
                return handle;

            throw new DllNotFoundException(
                $"Unable to load native library '{LibraryName}'. " +
                $"Expected at: {ridPath ?? "(unknown RID path)"} or {basePath}. " +
                $"Platform: {PlatformInfo.RuntimeIdentifier}, " +
                $"Architecture: {PlatformInfo.ProcessArchitecture}");
        }

        /// <summary>
        /// Returns the full path to the native library under the NuGet runtimes/ layout,
        /// or null if the path cannot be determined.
        /// </summary>
        private static string? GetRidNativePath()
        {
            string rid = PlatformInfo.RuntimeIdentifier;
            string libName = PlatformInfo.NativeLibraryName;

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
    }
}
