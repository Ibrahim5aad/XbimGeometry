using System;
using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Internal
{
    /// <summary>
    /// P/Invoke declarations for all xbim_geometry_native exported functions.
    /// Destroy functions live in <see cref="NativeHandleMethods"/> to avoid
    /// circular dependencies with SafeHandle subclasses.
    /// </summary>
    internal static partial class XbimGeometryNativeApi
    {
        private const string Lib = NativeLibraryLoader.LibraryName;
        private const CallingConvention CC = CallingConvention.StdCall;

        static XbimGeometryNativeApi()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        #region Error Handling

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

        #endregion

        #region Context Lifecycle

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

        #endregion

        #region Shape Queries

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_type(
            NativeShapeHandle handle,
            out int outType);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_is_null(NativeShapeHandle handle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_is_valid(NativeShapeHandle handle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_is_closed(NativeShapeHandle handle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_reversed(
            NativeShapeHandle handle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_is_same(
            NativeShapeHandle a,
            NativeShapeHandle b,
            out int outSame);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_hash_code(
            NativeShapeHandle handle,
            out int outHash);

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

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_write_stl(
            NativeShapeHandle handle,
            [MarshalAs(UnmanagedType.LPStr)] string filePath,
            double deflection);

        #endregion

        #region Location

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

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_location_get_transform(
            NativeLocationHandle handle,
            out double outM11, out double outM12, out double outM13,
            out double outM21, out double outM22, out double outM23,
            out double outM31, out double outM32, out double outM33,
            out double outOffsetX, out double outOffsetY, out double outOffsetZ,
            out double outScale);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_location_invert(
            NativeLocationHandle handle,
            out NativeLocationHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_location_translated(
            NativeLocationHandle handle,
            double tx, double ty, double tz,
            out NativeLocationHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_location_scaled(
            NativeLocationHandle handle,
            double scaleFactor,
            out NativeLocationHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_get_location(
            NativeShapeHandle shapeHandle,
            out NativeLocationHandle outHandle,
            out double outM11, out double outM12, out double outM13,
            out double outM21, out double outM22, out double outM23,
            out double outM31, out double outM32, out double outM33,
            out double outOffsetX, out double outOffsetY, out double outOffsetZ,
            out double outScale);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_moved(
            NativeShapeHandle NativeShapeHandle,
            NativeLocationHandle locationHandle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_gtransform(
            NativeShapeHandle shapeHandle,
            double m11, double m12, double m13, double offsetX,
            double m21, double m22, double m23, double offsetY,
            double m31, double m32, double m33, double offsetZ,
            double scaleX, double scaleY, double scaleZ,
            out NativeShapeHandle outHandle);

        #endregion

        #region CSG Solid Primitives

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

        #endregion

        #region Sweep Operations

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

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_sectioned_spine(
            NativeContextHandle ctx,
            NativeShapeHandle spineHandle,
            [In] IntPtr[] sectionHandles,
            int numSections,
            double precision,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_solid_build_surface_curve_swept(
            NativeContextHandle ctx,
            NativeShapeHandle faceHandle,
            NativeShapeHandle directrixHandle,
            NativeSurfaceHandle surfaceHandle,
            int isPlanarReferenceSurface,
            double precision,
            NativeLocationHandle locationHandle,
            out NativeShapeHandle outHandle);

        #endregion

        #region Profiles

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

        #endregion

        #region Boolean Operations

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

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_boolean_section(
            NativeContextHandle ctx,
            NativeShapeHandle bodyHandle,
            NativeShapeHandle faceHandle,
            double tolerance,
            out NativeShapeHandle outHandle);

        #endregion

        #region Half-Space Operations

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

        #endregion

        #region Compound Operations

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
        internal static extern int xbim_compound_add(
            NativeShapeHandle compoundHandle,
            NativeShapeHandle childHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_compound_child_count(
            NativeShapeHandle handle,
            out int outCount);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_compound_get_children(
            NativeShapeHandle handle,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] outHandles,
            ref int count);

        #endregion

        #region Vertex

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

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_vertex_tolerance(
            NativeShapeHandle vertexHandle,
            out double outTolerance);

        #endregion

        #region Edge

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
        internal static extern int xbim_edge_build_circle_arc_3pt(
            NativeContextHandle ctx,
            double p1X, double p1Y, double p1Z,
            double p2X, double p2Y, double p2Z,
            double p3X, double p3Y, double p3Z,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_build_from_curve_handle(
            NativeContextHandle ctx,
            NativeCurveHandle curveHandle,
            double startX, double startY, double startZ,
            double endX, double endY, double endZ,
            int sameSense,
            double tolerance,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_build_from_curve2d_handle(
            NativeContextHandle ctx,
            NativeCurve2dHandle curve2dHandle,
            double startX, double startY,
            double endX, double endY,
            int sameSense,
            double tolerance,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_from_curve_handle(
            NativeContextHandle ctx,
            NativeCurveHandle curveHandle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_from_curve2d_handle(
            NativeContextHandle ctx,
            NativeCurve2dHandle curve2dHandle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_length(
            NativeShapeHandle edgeHandle,
            out double outLength);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_tolerance(
            NativeShapeHandle edgeHandle,
            out double outTolerance);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_vertices(
            NativeShapeHandle edgeHandle,
            out NativeShapeHandle outStart,
            out NativeShapeHandle outEnd);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_edge_get_curve(
            NativeShapeHandle edgeHandle,
            out NativeCurveHandle outCurve,
            out double outParam1,
            out double outParam2);

        #endregion

        #region Wire

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_build_from_edges(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] edgeHandles,
            int numEdges,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_build_from_curves(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] curveHandles,
            int numCurves,
            double tolerance,
            double gapSize,
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

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_length(
            NativeShapeHandle wireHandle,
            out double outLength);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_contour_area(
            NativeShapeHandle wireHandle,
            out double outArea);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_get_parameter(
            NativeShapeHandle wireHandle,
            double pointX,
            double pointY,
            double pointZ,
            double tolerance,
            out double outParam);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_build_trimmed(
            NativeContextHandle ctx,
            NativeShapeHandle wireHandle,
            double u1,
            double u2,
            int sameSense,
            double tolerance,
            double radianFactor,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_build_trimmed_by_length(
            NativeContextHandle ctx,
            NativeShapeHandle wireHandle,
            double arcStart,
            double arcEnd,
            double tolerance,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_build_trimmed_by_points(
            NativeContextHandle ctx,
            NativeShapeHandle wireHandle,
            double p1X, double p1Y, double p1Z,
            double p2X, double p2Y, double p2Z,
            double u1,
            double u2,
            int preferCartesian,
            int sameSense,
            double tolerance,
            double radianFactor,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_fillet(
            NativeContextHandle ctx,
            NativeShapeHandle wireHandle,
            double filletRadius,
            double tolerance,
            out NativeShapeHandle outHandle);

        #endregion

        #region Face

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
        internal static extern int xbim_face_build_advanced_with_surface(
            NativeContextHandle ctx,
            NativeSurfaceHandle surfaceHandle,
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
        internal static extern int xbim_face_perimeter(
            NativeShapeHandle faceHandle,
            out double outPerimeter);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_normal(
            NativeShapeHandle faceHandle,
            double u,
            double v,
            out double outNormalX,
            out double outNormalY,
            out double outNormalZ);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_normal_at_point(
            NativeShapeHandle faceHandle,
            double pointX,
            double pointY,
            double pointZ,
            double precision,
            double tolerance,
            out double outNormalX,
            out double outNormalY,
            out double outNormalZ);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_is_facing_away(
            NativeShapeHandle faceHandle,
            double dirX,
            double dirY,
            double dirZ);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_tolerance(
            NativeShapeHandle faceHandle,
            out double outTolerance);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_get_surface(
            NativeShapeHandle faceHandle,
            out NativeSurfaceHandle outSurface,
            out int outSurfaceType);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_outer_wire(
            NativeShapeHandle faceHandle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_inner_wires(
            NativeShapeHandle faceHandle,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] outHandles,
            ref int count);

        #endregion

        #region Shell

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

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shell_build_closed_shell(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] faceHandles,
            int numFaces,
            double tolerance,
            out NativeShapeHandle outHandle);

        #endregion

        #region Shape Traversal

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

        #endregion

        #region Curve Construction

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
            double xDirX, double xDirY, double xDirZ,
            double radius,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_ellipse_3d(
            NativeContextHandle ctx,
            double centerX, double centerY, double centerZ,
            double normalX, double normalY, double normalZ,
            double xDirX, double xDirY, double xDirZ,
            double semiAxis1, double semiAxis2,
            out int outRotated,
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

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_trimmed_3d(
            NativeContextHandle ctx,
            NativeCurveHandle basisHandle,
            double u1, double u2,
            int sense,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_trimmed_line_3d(
            NativeContextHandle ctx,
            double x1, double y1, double z1,
            double x2, double y2, double z2,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_circle_3pt_3d(
            NativeContextHandle ctx,
            double x1, double y1, double z1,
            double x2, double y2, double z2,
            double x3, double y3, double z3,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_arc_of_circle_3d(
            NativeContextHandle ctx,
            NativeCurveHandle circleHandle,
            double u1, double u2,
            int sense,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_parameters(
            NativeCurveHandle handle,
            out double outFirst,
            out double outLast);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_length(
            NativeCurveHandle handle,
            out double outLength);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_parameter_at_length(
            NativeCurveHandle handle,
            double arcLength,
            double tolerance,
            out double outParameter);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_value(
            NativeCurveHandle handle,
            double u,
            out double outX, out double outY, out double outZ);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_d1(
            NativeCurveHandle handle,
            double u,
            out double outPx, out double outPy, out double outPz,
            out double outDx, out double outDy, out double outDz);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_d2(
            NativeCurveHandle handle,
            double u,
            out double outPx, out double outPy, out double outPz,
            out double outD1x, out double outD1y, out double outD1z,
            out double outD2x, out double outD2y, out double outD2z);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_is_closed(
            NativeCurveHandle handle,
            double tolerance);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_project_point_3d(
            NativeContextHandle ctx,
            NativeCurveHandle curveHandle,
            double px, double py, double pz,
            double tolerance,
            out double outParam);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_reverse(NativeCurveHandle handle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_composite_bspline(
            NativeContextHandle ctx,
            [In] IntPtr[] curves,
            int numCurves,
            double tolerance,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_offset_3d(
            NativeContextHandle ctx,
            NativeCurveHandle basisHandle,
            double offset,
            double refDirX, double refDirY, double refDirZ,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_clothoid(
            NativeContextHandle ctx,
            double clothoidConstant,
            double startParam, double endParam,
            double placementX, double placementY,
            double dirX, double dirY,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_sine_spiral(
            NativeContextHandle ctx,
            double sineTerm, double linearTerm, double constantTerm,
            double startParam, double endParam,
            double placementX, double placementY,
            double dirX, double dirY,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_cosine_spiral(
            NativeContextHandle ctx,
            double cosineTerm, double constantTerm,
            double startParam, double endParam,
            double placementX, double placementY,
            double dirX, double dirY,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_polynomial_spiral(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] coefficients,
            [MarshalAs(UnmanagedType.LPArray)] int[] coefficientPresent,
            int numCoefficients,
            double startParam, double endParam,
            double placementX, double placementY,
            double dirX, double dirY,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_clothoid(
            NativeContextHandle ctx,
            double clothoidConstant,
            double startParam, double endParam,
            double placementX, double placementY,
            double dirX, double dirY,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_sine_spiral(
            NativeContextHandle ctx,
            double sineTerm, double linearTerm, double constantTerm,
            double startParam, double endParam,
            double placementX, double placementY,
            double dirX, double dirY,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_cosine_spiral(
            NativeContextHandle ctx,
            double cosineTerm, double constantTerm,
            double startParam, double endParam,
            double placementX, double placementY,
            double dirX, double dirY,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_polynomial_spiral(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] coefficients,
            [MarshalAs(UnmanagedType.LPArray)] int[] coefficientPresent,
            int numCoefficients,
            double startParam, double endParam,
            double placementX, double placementY,
            double dirX, double dirY,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_bspline(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] polesXY,
            int numPoles,
            [MarshalAs(UnmanagedType.LPArray)] double[] knots,
            int numKnots,
            [MarshalAs(UnmanagedType.LPArray)] int[] multiplicities,
            int degree,
            [MarshalAs(UnmanagedType.LPArray)] double[]? weights,
            out NativeCurve2dHandle outHandle);

        #endregion

        #region Surface Construction

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_surface_build_plane(
            NativeContextHandle ctx,
            double originX, double originY, double originZ,
            double normalX, double normalY, double normalZ,
            double refDirX, double refDirY, double refDirZ,
            out NativeSurfaceHandle outHandle,
            out double outRefDirX, out double outRefDirY, out double outRefDirZ);

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

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_surface_build_sectioned(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] pointsXYZ,
            int numSections,
            int numPointsPerSection,
            [In] IntPtr[] locations,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_surface_build_revolution(
            NativeContextHandle ctx,
            NativeCurveHandle curveHandle,
            double axisOriginX, double axisOriginY, double axisOriginZ,
            double axisDirX, double axisDirY, double axisDirZ,
            out NativeSurfaceHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_surface_build_linear_extrusion(
            NativeContextHandle ctx,
            NativeCurveHandle curveHandle,
            double dirX, double dirY, double dirZ,
            double posOX, double posOY, double posOZ,
            double posZX, double posZY, double posZZ,
            double posXX, double posXY, double posXZ,
            int hasPosition,
            out NativeSurfaceHandle outHandle);

        #endregion

        #region Mesh / WexBim

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_triangulate(
            NativeShapeHandle shapeHandle,
            double linearDeflection,
            double angularDeflection,
            int relative);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_mesh_create_wexbim(
            NativeContextHandle ctx,
            NativeShapeHandle NativeShapeHandle,
            double tolerance,
            double linearDeflection,
            double angularDeflection,
            double scale,
            int checkEdges,
            out IntPtr outBuffer,
            out int outBufferSize,
            out int outHasCurves,
            out double outMinX, out double outMinY, out double outMinZ,
            out double outMaxX, out double outMaxY, out double outMaxZ);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_mesh_get_bounding_box(
            NativeContextHandle ctx,
            NativeShapeHandle NativeShapeHandle,
            double tolerance,
            double linearDeflection,
            double angularDeflection,
            double scale,
            out double outMinX, out double outMinY, out double outMinZ,
            out double outMaxX, out double outMaxY, out double outMaxZ);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern void xbim_buffer_free(IntPtr buffer);

        #endregion

        #region BRep Serialization

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_to_brep_string(
            NativeShapeHandle handle,
            out IntPtr outBrepStr,
            out int outStrLen);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern NativeShapeHandle xbim_shape_from_brep_string(
            [MarshalAs(UnmanagedType.LPStr)] string brepStr,
            int strLen);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern void xbim_string_free(IntPtr str);

        #endregion

        #region Binary Shape Serialization

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_to_binary(
            NativeShapeHandle handle,
            int withTriangles,
            int withNormals,
            out IntPtr outBuffer,
            out int outSize);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern NativeShapeHandle xbim_shape_from_binary(
            IntPtr buffer,
            int size);

        #endregion

        #region Shape Repair / Domain

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_shape_unify_domain(
            NativeShapeHandle shapeHandle,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_add_wires(
            NativeShapeHandle faceHandle,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] wireHandles,
            int wireCount,
            out NativeShapeHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_face_fix(
            NativeShapeHandle faceHandle,
            double tolerance,
            out NativeShapeHandle outHandle);

        #endregion

        #region XbimCurve2d Construction

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_line(
            NativeContextHandle ctx,
            double x1, double y1,
            double x2, double y2,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_unbounded_line(
            NativeContextHandle ctx,
            double originX, double originY,
            double dirX, double dirY,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_circle(
            NativeContextHandle ctx,
            double cx, double cy,
            double radius,
            double refDirX, double refDirY,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_ellipse(
            NativeContextHandle ctx,
            double cx, double cy,
            double majorRadius, double minorRadius,
            double refDirX, double refDirY,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_trimmed(
            NativeContextHandle ctx,
            NativeCurve2dHandle basisHandle,
            double u1, double u2,
            int sense,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_arc_of_circle(
            NativeContextHandle ctx,
            NativeCurve2dHandle circleHandle,
            double u1, double u2,
            int sense,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_arc_of_ellipse(
            NativeContextHandle ctx,
            NativeCurve2dHandle ellipseHandle,
            double u1, double u2,
            int sense,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_arc_3pt(
            NativeContextHandle ctx,
            double x1, double y1,
            double x2, double y2,
            double x3, double y3,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_polynomial(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] coeffsX,
            int numCoeffsX,
            [MarshalAs(UnmanagedType.LPArray)] double[] coeffsY,
            int numCoeffsY,
            double placementX, double placementY,
            double dirX, double dirY,
            double firstParam, double lastParam,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_polynomial(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] double[] coeffsX,
            int numCoeffsX,
            [MarshalAs(UnmanagedType.LPArray)] double[] coeffsY,
            int numCoeffsY,
            double placementX, double placementY,
            double dirX, double dirY,
            double firstParam, double lastParam,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_parameters(
            NativeCurve2dHandle handle,
            out double outFirst,
            out double outLast);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_length(
            NativeCurve2dHandle handle,
            out double outLength);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_value(
            NativeCurve2dHandle handle,
            double u,
            out double outX, out double outY);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_d1(
            NativeCurve2dHandle handle,
            double u,
            out double outPx, out double outPy,
            out double outDx, out double outDy);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_d2(
            NativeCurve2dHandle handle,
            double u,
            out double outPx, out double outPy,
            out double outD1x, out double outD1y,
            out double outD2x, out double outD2y);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_project_point(
            NativeContextHandle ctx,
            NativeCurve2dHandle curveHandle,
            double px, double py,
            double tolerance,
            out double outParam);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_reverse(NativeCurve2dHandle handle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_transform(
            NativeCurve2dHandle handle,
            double placementX, double placementY,
            double dirX, double dirY);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_move_to_origin(
            NativeCurve2dHandle handle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_align_to_origin(
            NativeCurve2dHandle handle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_translate_start_to_x(
            NativeCurve2dHandle handle,
            double targetX);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_composite_bspline(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] curves,
            int numCurves,
            double tolerance,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve2d_build_offset(
            NativeContextHandle ctx,
            NativeCurve2dHandle basisHandle,
            double offset,
            out NativeCurve2dHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_gradient(
            NativeContextHandle ctx,
            NativeCurve2dHandle horizontalHandle,
            NativeCurve2dHandle heightFunctionHandle,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_build_segmented_reference(
            NativeContextHandle ctx,
            NativeCurveHandle gradientCurveHandle,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] segmentCurves,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] segmentLocations,
            int numSegments,
            NativeLocationHandle endPointLocation,
            out NativeCurveHandle outHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_curve_get_superelevation_and_tilt(
            NativeCurveHandle curveHandle,
            double parameter,
            out double outSuperElevation,
            out double outCantTilt);

        #endregion

        #region Wire from 2D Curves

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_wire_build_from_2d_curves(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] curves,
            int numCurves,
            double tolerance,
            double gapSize,
            out NativeShapeHandle outWire);

        #endregion

        #region Advanced BRep Builder

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_advanced_brep_create(
            NativeContextHandle ctx,
            double tolerance,
            out NativeAdvancedBrepBuilderHandle outBuilder);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_advanced_brep_add_vertex(
            NativeAdvancedBrepBuilderHandle builder,
            int vertexLabel,
            double x, double y, double z);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_advanced_brep_add_edge_curve(
            NativeAdvancedBrepBuilderHandle builder,
            int edgeLabel,
            NativeCurveHandle curveHandle);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_advanced_brep_begin_face(
            NativeAdvancedBrepBuilderHandle builder,
            NativeSurfaceHandle surfaceHandle,
            int sameSense);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_advanced_brep_begin_bound(
            NativeAdvancedBrepBuilderHandle builder,
            int isOuter,
            int orientation);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_advanced_brep_add_bound_edge(
            NativeAdvancedBrepBuilderHandle builder,
            int edgeLabel,
            int startVertexLabel,
            int endVertexLabel,
            int sameSense);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_advanced_brep_end_bound(
            NativeAdvancedBrepBuilderHandle builder);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_advanced_brep_end_face(
            NativeAdvancedBrepBuilderHandle builder);

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_advanced_brep_build(
            NativeAdvancedBrepBuilderHandle builder,
            out NativeShapeHandle outHandle);

        #endregion

        #region Grid Operations

        [DllImport(Lib, CallingConvention = CC)]
        internal static extern int xbim_grid_create(
            NativeContextHandle ctx,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] uCurves, int uCount,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] vCurves, int vCount,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] wCurves, int wCount,
            double precision,
            double oneMillimeter,
            out NativeShapeHandle outHandle);

        #endregion
    }

    /// <summary>
    /// Managed delegate matching the native XbimLogCallback signature.
    /// Must be kept alive (prevent GC) while the native code may invoke it.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void XbimLogCallback(int level, [MarshalAs(UnmanagedType.LPStr)] string message);
}
