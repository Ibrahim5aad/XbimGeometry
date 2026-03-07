using System.Runtime.InteropServices;

namespace Xbim.Geometry.Engine.Internal
{
    /// <summary>
    /// Provides platform detection for determining the current OS and CPU architecture,
    /// the corresponding runtime identifier, and the native library file name.
    /// </summary>
    internal static class PlatformInfo
    {
        /// <summary>
        /// Gets the runtime identifier string for the current platform (e.g. "win-x64", "linux-x64", "linux-arm64").
        /// </summary>
        internal static string RuntimeIdentifier { get; } = BuildRuntimeIdentifier();

        /// <summary>
        /// Gets the platform-specific file name for the native geometry library.
        /// </summary>
        internal static string NativeLibraryName { get; } = BuildNativeLibraryName();

        /// <summary>
        /// Gets whether the current platform is Windows.
        /// </summary>
        internal static bool IsWindows { get; } = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Gets whether the current platform is Linux.
        /// </summary>
        internal static bool IsLinux { get; } = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        /// <summary>
        /// Gets whether the current platform is macOS.
        /// </summary>
        internal static bool IsMacOS { get; } = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// <summary>
        /// Gets the processor architecture (e.g. X64, Arm64).
        /// </summary>
        internal static Architecture ProcessArchitecture { get; } = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;

        private static string BuildRuntimeIdentifier()
        {
            string os;
            if (OperatingSystem.IsBrowser())
                os = "browser";
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                os = "win";
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                os = "linux";
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                os = "osx";
            else
                os = "unknown";

            string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
            };

            return $"{os}-{arch}";
        }

        private static string BuildNativeLibraryName()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "xbim_geometry_native.dll";
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "libxbim_geometry_native.so";
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "libxbim_geometry_native.dylib";

            return NativeLibraryLoader.LibraryName;
        }
    }
}
