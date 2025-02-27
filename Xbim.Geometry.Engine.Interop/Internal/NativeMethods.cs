using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Internal
{
    /// <summary>
    /// P/Invoke declarations for all xbim_geometry_native exported functions.
    /// Destroy functions live in <see cref="NativeHandleMethods"/> to avoid
    /// circular dependencies with SafeHandle subclasses.
    /// </summary>
    internal static partial class NativeMethods
    {
        private const string Lib = NativeLibraryLoader.LibraryName;
        private const CallingConvention CC = CallingConvention.StdCall;

        static NativeMethods()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        // ── Error handling ────────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern IntPtr xbim_get_last_error();

        /// <summary>
        /// Returns the last error message as a managed string.
        /// The native pointer is only valid until the next xbim_* call on the same thread.
        /// </summary>
        internal static string GetLastError()
        {
            IntPtr ptr = xbim_get_last_error();
            return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }

        // ── Context lifecycle ─────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_context_create(
            double precision,
            double oneMeter,
            double oneFoot,
            double oneMillimeter,
            double radianFactor,
            double timeout,
            double minimumGap,
            XbimLogCallback? logCallback,
            out NativeContextHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_context_set_logger(
            NativeContextHandle handle,
            XbimLogCallback? logCallback);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_context_log(
            NativeContextHandle handle,
            int level,
            [MarshalAs(UnmanagedType.LPStr)] string message);

        // ── Shape queries ─────────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_type(
            NativeShapeHandle handle,
            out int outType);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_is_valid(NativeShapeHandle handle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_is_closed(NativeShapeHandle handle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_bounding_box(
            NativeShapeHandle handle,
            out double minX, out double minY, out double minZ,
            out double maxX, out double maxY, out double maxZ);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_volume(
            NativeShapeHandle handle,
            out double outVolume);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_surface_area(
            NativeShapeHandle handle,
            out double outArea);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_write_brep(
            NativeShapeHandle handle,
            [MarshalAs(UnmanagedType.LPStr)] string filePath);

        // ── Location lifecycle ────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_location_create_from_axis2(
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            out NativeLocationHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_location_create_identity(
            out NativeLocationHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_location_compose(
            NativeLocationHandle loc1,
            NativeLocationHandle loc2,
            out NativeLocationHandle outHandle);

        // ── Shape + Location ──────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_moved(
            NativeShapeHandle shapeHandle,
            NativeLocationHandle locationHandle,
            out NativeShapeHandle outHandle);

        // ── CSG solid primitives ─────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_block(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double xLen, double yLen, double zLen,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_sphere(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_right_circular_cylinder(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius, double height,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_right_circular_cone(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius, double height,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_rectangular_pyramid(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double xLen, double yLen, double height,
            out NativeShapeHandle outHandle);

        // ── Parametric profile primitives ────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_rectangle(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double xDim, double yDim,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_circle(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_ellipse(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double semiAxis1, double semiAxis2,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_rounded_rectangle(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double xDim, double yDim, double roundingRadius,
            out NativeShapeHandle outHandle);
    }

    /// <summary>
    /// Managed delegate matching the native XbimLogCallback signature.
    /// Must be kept alive (prevent GC) while the native code may invoke it.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void XbimLogCallback(int level, [MarshalAs(UnmanagedType.LPStr)] string message);
}
