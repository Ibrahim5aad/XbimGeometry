using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine
{
    /// <summary>
    /// Conventions for locating platform-specific native binaries.
    /// </summary>
    internal class XbimArchitectureConventions
    {
        /// <summary>
        /// Gets the runtime folder name for native binaries (e.g. "win-x64", "linux-x64").
        /// </summary>
        public static string Runtime => PlatformInfo.RuntimeIdentifier;

        /// <summary>
        /// Gets the platform-specific native library file name.
        /// </summary>
        public static string ModuleDllName => PlatformInfo.NativeLibraryName;

        /// <summary>
        /// Gets the base library name without extension.
        /// </summary>
        public static string ModuleName => NativeLibraryLoader.LibraryName;
    }
}
