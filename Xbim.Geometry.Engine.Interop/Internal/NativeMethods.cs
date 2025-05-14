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

        // ── Sweep operations (extruded area solids) ────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_extruded(
            NativeContextHandle ctx,
            NativeShapeHandle faceHandle,
            double dirX, double dirY, double dirZ,
            double depth,
            NativeLocationHandle locationHandle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_extruded_tapered(
            NativeContextHandle ctx,
            NativeShapeHandle startFaceHandle,
            NativeShapeHandle endFaceHandle,
            double dirX, double dirY, double dirZ,
            double depth,
            double precision,
            NativeLocationHandle locationHandle,
            out NativeShapeHandle outHandle);

        // ── Sweep operations (revolved area solids) ───────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_revolved(
            NativeContextHandle ctx,
            NativeShapeHandle faceHandle,
            double axisOriginX, double axisOriginY, double axisOriginZ,
            double axisDirX, double axisDirY, double axisDirZ,
            double angle,
            NativeLocationHandle locationHandle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_revolved_tapered(
            NativeContextHandle ctx,
            NativeShapeHandle faceHandle,
            NativeShapeHandle endFaceHandle,
            double axisOriginX, double axisOriginY, double axisOriginZ,
            double axisDirX, double axisDirY, double axisDirZ,
            double angle,
            double precision,
            NativeLocationHandle locationHandle,
            out NativeShapeHandle outHandle);

        // ── Sweep operations (swept disk and fixed reference swept) ────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_swept_disk(
            NativeContextHandle ctx,
            NativeShapeHandle directrixHandle,
            double radius,
            double innerRadius,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_fixed_reference_swept(
            NativeContextHandle ctx,
            NativeShapeHandle faceHandle,
            NativeShapeHandle directrixHandle,
            double refSurfaceOriginX, double refSurfaceOriginY, double refSurfaceOriginZ,
            double refSurfaceNormalX, double refSurfaceNormalY, double refSurfaceNormalZ,
            int isPlanarReferenceSurface,
            double precision,
            NativeLocationHandle locationHandle,
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

        // ── Structural profile primitives ──────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_ishape(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double overallWidth, double overallDepth,
            double webThickness, double flangeThickness,
            double filletRadius,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_lshape(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double depth, double width, double thickness,
            double filletRadius, double edgeRadius, double legSlope,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_tshape(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double depth, double flangeWidth,
            double webThickness, double flangeThickness,
            double filletRadius, double flangeEdgeRadius, double webEdgeRadius,
            double flangeSlope, double webSlope,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_ushape(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double depth, double flangeWidth,
            double webThickness, double flangeThickness,
            double filletRadius, double edgeRadius, double flangeSlope,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_zshape(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double depth, double flangeWidth,
            double webThickness, double flangeThickness,
            double filletRadius, double edgeRadius,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_cshape(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double depth, double width, double wallThickness,
            double girth, double internalFilletRadius,
            out NativeShapeHandle outHandle);

        // ── Trapezium and asymmetric I-shape profiles ───────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_trapezium(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double bottomXDim, double topXDim, double yDim, double topXOffset,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_asymmetric_ishape(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double bottomFlangeWidth, double overallDepth,
            double webThickness, double bottomFlangeThickness,
            double topFlangeWidth, double topFlangeThickness,
            double bottomFlangeFilletRadius, double topFlangeFilletRadius,
            double bottomFlangeEdgeRadius, double topFlangeEdgeRadius,
            double bottomFlangeSlope, double topFlangeSlope,
            out NativeShapeHandle outHandle);

        // ── Hollow profile primitives ─────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_rectangle_hollow(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double xDim, double yDim, double wallThickness,
            double innerFilletRadius, double outerFilletRadius,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_circle_hollow(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius, double wallThickness,
            out NativeShapeHandle outHandle);

        // ── Arbitrary / composite / derived profile primitives ──────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_arbitrary_closed(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] pointsX,
            [MarshalAs(UnmanagedType.LPArray)] double[] pointsY,
            int pointCount,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_arbitrary_open(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] pointsX,
            [MarshalAs(UnmanagedType.LPArray)] double[] pointsY,
            int pointCount,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_with_voids(
            NativeContextHandle ctx,
            NativeShapeHandle outerFaceHandle,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] innerWireHandles,
            int numInnerWires,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_composite(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] profileHandles,
            int numProfiles,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_derived(
            NativeContextHandle ctx,
            NativeShapeHandle parentHandle,
            double m00, double m01, double m02,
            double m10, double m11, double m12,
            int isNonUniformScale,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_profile_build_mirrored(
            NativeContextHandle ctx,
            NativeShapeHandle parentHandle,
            out NativeShapeHandle outHandle);

        // ── Boolean operations ──────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_boolean_union(
            NativeContextHandle ctx,
            NativeShapeHandle bodyHandle,
            NativeShapeHandle toolHandle,
            double fuzzyTolerance,
            out int outHasWarnings,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_boolean_cut(
            NativeContextHandle ctx,
            NativeShapeHandle bodyHandle,
            NativeShapeHandle toolHandle,
            double fuzzyTolerance,
            out int outHasWarnings,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_boolean_intersect(
            NativeContextHandle ctx,
            NativeShapeHandle bodyHandle,
            NativeShapeHandle toolHandle,
            double fuzzyTolerance,
            out int outHasWarnings,
            out NativeShapeHandle outHandle);

        // ── Half-space operations ──────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_halfspace_build(
            NativeContextHandle ctx,
            int surfaceType,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius,
            int agreementFlag,
            double oneMeter,
            double precision,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_halfspace_build_polygonal_bounded(
            NativeContextHandle ctx,
            double surfaceOriginX, double surfaceOriginY, double surfaceOriginZ,
            double surfaceZDirX, double surfaceZDirY, double surfaceZDirZ,
            double surfaceXDirX, double surfaceXDirY, double surfaceXDirZ,
            int agreementFlag,
            [MarshalAs(UnmanagedType.LPArray)] double[] boundaryPointsX,
            [MarshalAs(UnmanagedType.LPArray)] double[] boundaryPointsY,
            int boundaryPointCount,
            double boundaryOriginX, double boundaryOriginY, double boundaryOriginZ,
            double boundaryZDirX, double boundaryZDirY, double boundaryZDirZ,
            double boundaryXDirX, double boundaryXDirY, double boundaryXDirZ,
            double oneMeter,
            double precision,
            out NativeShapeHandle outHandle);

        // ── Compound operations ──────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_compound_make(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] shapeHandles,
            int numShapes,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_compound_sew(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] shapeHandles,
            int numShapes,
            double tolerance,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_compound_cut(
            NativeContextHandle ctx,
            NativeShapeHandle compoundHandle,
            NativeShapeHandle toolHandle,
            double fuzzyTolerance,
            out int outHasWarnings,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_halfspace_build_boxed(
            NativeContextHandle ctx,
            int surfaceType,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius,
            int agreementFlag,
            double oneMeter,
            double precision,
            out NativeShapeHandle outHandle);

        // ── Face construction and queries ──────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_build_from_surface(
            NativeContextHandle ctx,
            int surfaceType,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius,
            double tolerance,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_build_from_wire(
            NativeContextHandle ctx,
            NativeShapeHandle wireHandle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_build_advanced(
            NativeContextHandle ctx,
            int surfaceType,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius,
            NativeShapeHandle outerWireHandle,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] innerWireHandles,
            int numInnerWires,
            double tolerance,
            int sameSense,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_area(
            NativeShapeHandle faceHandle,
            out double outArea);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_normal(
            NativeShapeHandle faceHandle,
            double u,
            double v,
            out double outNormalX,
            out double outNormalY,
            out double outNormalZ);

        // ── Wire construction and query ─────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_build_from_edges(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] edgeHandles,
            int numEdges,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_build_polyline(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] pointsXYZ,
            int numPoints,
            double tolerance,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_build_polygon(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] pointsXYZ,
            int numPoints,
            int closed,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_is_closed(
            NativeShapeHandle wireHandle,
            double tolerance,
            out int outClosed);

        // ── Edge construction and query ─────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_build_line(
            NativeContextHandle ctx,
            double startX, double startY, double startZ,
            double endX, double endY, double endZ,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_build_from_curve(
            NativeContextHandle ctx,
            NativeShapeHandle curveEdgeHandle,
            double param1,
            double param2,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_build_circle_arc(
            NativeContextHandle ctx,
            double centerX, double centerY, double centerZ,
            double normalX, double normalY, double normalZ,
            double radius,
            double startAngle,
            double endAngle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_length(
            NativeShapeHandle edgeHandle,
            out double outLength);

        // ── Vertex construction and query ─────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_vertex_build(
            NativeContextHandle ctx,
            double x, double y, double z,
            double tolerance,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_vertex_point(
            NativeShapeHandle vertexHandle,
            out double outX, out double outY, out double outZ);

        // ── Shell construction and repair ────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shell_build_from_faces(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] faceHandles,
            int numFaces,
            double tolerance,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shell_sew(
            NativeContextHandle ctx,
            NativeShapeHandle shellHandle,
            double tolerance,
            out int outIsFixed,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shell_make_solid(
            NativeContextHandle ctx,
            NativeShapeHandle shellHandle,
            out NativeShapeHandle outHandle);

        // ── Curve construction ──────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_line_3d(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double dirX, double dirY, double dirZ,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_circle_3d(
            NativeContextHandle ctx,
            double centerX, double centerY, double centerZ,
            double normalX, double normalY, double normalZ,
            double radius,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_ellipse_3d(
            NativeContextHandle ctx,
            double centerX, double centerY, double centerZ,
            double normalX, double normalY, double normalZ,
            double majorRadius, double minorRadius,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_bspline(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] polesXYZ,
            int numPoles,
            [MarshalAs(UnmanagedType.LPArray)] double[] knots,
            int numKnots,
            [MarshalAs(UnmanagedType.LPArray)] int[] multiplicities,
            int degree,
            [MarshalAs(UnmanagedType.LPArray)] double[]? weights,
            out NativeCurveHandle outHandle);

        // ── Shape traversal (topology navigation) ───────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_count_subshapes(
            NativeShapeHandle handle,
            int subType,
            out int outCount);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_get_subshapes(
            NativeShapeHandle handle,
            int subType,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] outHandles,
            ref int count);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_outer_wire(
            NativeShapeHandle faceHandle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_inner_wires(
            NativeShapeHandle faceHandle,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] outHandles,
            ref int count);

        // ── Surface construction ────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_surface_build_plane(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double normalX, double normalY, double normalZ,
            out NativeSurfaceHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_surface_build_cylindrical(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius,
            out NativeSurfaceHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_surface_build_spherical(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double zDirX, double zDirY, double zDirZ,
            double xDirX, double xDirY, double xDirZ,
            double radius,
            out NativeSurfaceHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_surface_build_bspline(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] polesXYZ,
            int numPolesU,
            int numPolesV,
            [MarshalAs(UnmanagedType.LPArray)] double[] uKnots,
            int numUKnots,
            [MarshalAs(UnmanagedType.LPArray)] double[] vKnots,
            int numVKnots,
            [MarshalAs(UnmanagedType.LPArray)] int[] uMultiplicities,
            [MarshalAs(UnmanagedType.LPArray)] int[] vMultiplicities,
            int uDegree,
            int vDegree,
            [MarshalAs(UnmanagedType.LPArray)] double[]? weights,
            out NativeSurfaceHandle outHandle);
    }

    /// <summary>
    /// Managed delegate matching the native XbimLogCallback signature.
    /// Must be kept alive (prevent GC) while the native code may invoke it.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void XbimLogCallback(int level, [MarshalAs(UnmanagedType.LPStr)] string message);
}
