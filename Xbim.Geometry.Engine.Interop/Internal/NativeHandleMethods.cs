using System.Runtime.InteropServices;

namespace Xbim.Geometry.Engine.Interop.Internal
{
    /// <summary>
    /// P/Invoke declarations for native handle destruction functions.
    /// These are separated from the main NativeMethods class so that
    /// SafeHandle subclasses can call destroy without circular dependencies.
    /// </summary>
    internal static partial class NativeHandleMethods
    {
        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int xbim_context_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int xbim_shape_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.StdCall)]
        internal static extern int xbim_location_destroy(IntPtr handle);
    }
}
