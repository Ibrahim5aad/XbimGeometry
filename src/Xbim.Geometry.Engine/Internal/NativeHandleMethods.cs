using System.Runtime.InteropServices;

namespace Xbim.Geometry.Engine.Internal
{
    /// <summary>
    /// P/Invoke declarations for native handle destruction functions.
    /// These are separated from the main XbimGeometryNativeApi class so that
    /// SafeHandle subclasses can call destroy without circular dependencies.
    /// </summary>
    internal static partial class NativeHandleMethods
    {
        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xbim_context_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xbim_shape_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xbim_location_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xbim_curve_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xbim_curve2d_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xbim_surface_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xbim_advanced_brep_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void xbim_mesh_free(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int xbim_manifold_mesh_destroy(IntPtr handle);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void xbim_buffer_free_float(IntPtr buffer);

        [DllImport(NativeLibraryLoader.LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void xbim_buffer_free_uint(IntPtr buffer);
    }
}
