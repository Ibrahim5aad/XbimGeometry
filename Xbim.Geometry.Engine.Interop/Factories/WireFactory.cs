using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds wire shapes from IFC curve entities, profile definitions, and point arrays.
    /// Wires represent connected sequences of edges forming open or closed paths.
    /// </summary>
    internal class WireFactory : IXWireFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public WireFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXWire BuildWire(IXPoint[] points)
        {
            if (points == null || points.Length < 2)
                throw new ArgumentException("Wire requires at least 2 points.", nameof(points));

            var pointsXYZ = new double[points.Length * 3];
            for (int i = 0; i < points.Length; i++)
            {
                pointsXYZ[i * 3 + 0] = points[i].X;
                pointsXYZ[i * 3 + 1] = points[i].Y;
                pointsXYZ[i * 3 + 2] = points[i].Z;
            }

            int result = XbimGeometryNativeApi.xbim_wire_build_polyline(
                ContextHandle,
                pointsXYZ, points.Length,
                _modelService.Precision,
                out var wireHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build wire from {points.Length} points: {XbimGeometryNativeApi.GetLastError()}");

            return new Wire(wireHandle);
        }

        public IXWire Build(IIfcCurve ifcCurve)
        {
            if (ifcCurve is IIfcPolyline ifcPolyline)
                return BuildFromPolyline(ifcPolyline);

            if (ifcCurve is IIfcIndexedPolyCurve ifcIndexed)
                return BuildFromIndexedPolyCurve(ifcIndexed);

            if (ifcCurve is IIfcCompositeCurve ifcComposite)
                return BuildFromCompositeCurve(ifcComposite);

            if (ifcCurve is IIfcTrimmedCurve ifcTrimmed)
                return BuildFromTrimmedCurve(ifcTrimmed);

            if (ifcCurve is IIfcLine ifcLine)
                return BuildFromLine(ifcLine);

            if (ifcCurve is IIfcCircle ifcCircle)
                return BuildFromCircle(ifcCircle);

            if (ifcCurve is IIfcEllipse ifcEllipse)
                return BuildFromEllipse(ifcEllipse);

            if (ifcCurve is IIfcBSplineCurveWithKnots ifcBSpline)
                return BuildFromBSpline(ifcBSpline);

            if (ifcCurve is IIfcOffsetCurve2D || ifcCurve is IIfcOffsetCurve3D)
                return BuildFromOffsetCurve(ifcCurve);

            throw new NotSupportedException(
                $"Wire from curve type {ifcCurve.ExpressType.ExpressName} #{ifcCurve.EntityLabel} is not yet supported.");
        }

        public IXWire Build(IIfcProfileDef ifcProfileDef)
        {
            // Open profiles have no face — build wire directly from their curve
            if (ifcProfileDef is IIfcCenterLineProfileDef centerLine)
                return BuildCenterLineProfileWire(centerLine);

            if (ifcProfileDef is IIfcArbitraryOpenProfileDef openProfile)
                return BuildOpenProfileWire(openProfile);

            // All other profile types: build face, extract outer wire
            var face = (Face)_modelService.ProfileFactory.BuildFace(ifcProfileDef);
            try
            {
                return face.OuterBound;
            }
            finally
            {
                face.Dispose();
            }
        }

        #region Polyline

        private IXWire BuildFromPolyline(IIfcPolyline ifcPolyline)
        {
            var points = ifcPolyline.Points;
            if (points.Count < 2)
                throw new InvalidOperationException(
                    $"IIfcPolyline #{ifcPolyline.EntityLabel} has fewer than 2 points.");

            var pointsXYZ = new double[points.Count * 3];
            for (int i = 0; i < points.Count; i++)
            {
                var cp = points[i];
                pointsXYZ[i * 3 + 0] = cp.Coordinates[0];
                pointsXYZ[i * 3 + 1] = cp.Coordinates[1];
                pointsXYZ[i * 3 + 2] = (int)cp.Dim == 3 ? (double)cp.Coordinates[2] : 0.0;
            }

            int result = XbimGeometryNativeApi.xbim_wire_build_polyline(
                ContextHandle,
                pointsXYZ, points.Count,
                _modelService.Precision,
                out var wireHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build wire from polyline #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Wire(wireHandle);
        }

        #endregion

        #region IndexedPolyCurve

        private IXWire BuildFromIndexedPolyCurve(IIfcIndexedPolyCurve ifcIndexed)
        {
            // Extract all points from the coordinate list
            List<double> allPointsXYZ;

            if (ifcIndexed.Points is IIfcCartesianPointList3D pl3D)
            {
                var coords = pl3D.CoordList;
                allPointsXYZ = new List<double>(coords.Count * 3);
                foreach (var pt in coords)
                {
                    allPointsXYZ.Add(pt[0]);
                    allPointsXYZ.Add(pt[1]);
                    allPointsXYZ.Add(pt.Count > 2 ? pt[2] : 0.0);
                }
            }
            else if (ifcIndexed.Points is IIfcCartesianPointList2D pl2D)
            {
                var coords = pl2D.CoordList;
                allPointsXYZ = new List<double>(coords.Count * 3);
                foreach (var pt in coords)
                {
                    allPointsXYZ.Add(pt[0]);
                    allPointsXYZ.Add(pt[1]);
                    allPointsXYZ.Add(0.0);
                }
            }
            else
            {
                throw new NotSupportedException(
                    $"Unsupported point list type in IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}.");
            }

            // If no segments specified, build a polyline through all points in order
            if (ifcIndexed.Segments == null || !ifcIndexed.Segments.Any())
            {
                int numPoints = allPointsXYZ.Count / 3;
                int result = XbimGeometryNativeApi.xbim_wire_build_polyline(
                    ContextHandle,
                    allPointsXYZ.ToArray(), numPoints,
                    _modelService.Precision,
                    out var wireHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Wire(wireHandle);
            }

            // Build individual edges from segments.
            // Segments are IIfcSegmentIndexSelect — concrete types are
            // IfcLineIndex (IList<long>) and IfcArcIndex (IList<long>) from Xbim.Ifc4.GeometryResource.
            // We handle them generically via IList<long> casting.
            var edgeHandles = new List<NativeShapeHandle>();
            try
            {
                foreach (var segment in ifcIndexed.Segments)
                {
                    // Each segment is a list of 1-based point indices
                    var indices = ((System.Collections.IEnumerable)segment)
                        .Cast<long>()
                        .ToArray();

                    if (indices.Length == 3)
                    {
                        // Arc segment (3 indices: start, mid, end) — build circular arc through all 3 points
                        int startIdx = (int)(indices[0] - 1);
                        int midIdx = (int)(indices[1] - 1);
                        int endIdx = (int)(indices[2] - 1);

                        int r = XbimGeometryNativeApi.xbim_edge_build_circle_arc_3pt(
                            ContextHandle,
                            allPointsXYZ[startIdx * 3], allPointsXYZ[startIdx * 3 + 1], allPointsXYZ[startIdx * 3 + 2],
                            allPointsXYZ[midIdx * 3], allPointsXYZ[midIdx * 3 + 1], allPointsXYZ[midIdx * 3 + 2],
                            allPointsXYZ[endIdx * 3], allPointsXYZ[endIdx * 3 + 1], allPointsXYZ[endIdx * 3 + 2],
                            out var edgeHandle);

                        if (r != 0)
                        {
                            // Collinear points — fall back to a straight line from start to end
                            _logger.LogInformation("Arc segment in IndexedPolyCurve #{EntityLabel} has collinear points, using line fallback",
                                ifcIndexed.EntityLabel);
                            r = XbimGeometryNativeApi.xbim_edge_build_line(
                                ContextHandle,
                                allPointsXYZ[startIdx * 3], allPointsXYZ[startIdx * 3 + 1], allPointsXYZ[startIdx * 3 + 2],
                                allPointsXYZ[endIdx * 3], allPointsXYZ[endIdx * 3 + 1], allPointsXYZ[endIdx * 3 + 2],
                                out edgeHandle);
                            if (r != 0)
                                throw new InvalidOperationException(
                                    $"Failed to build fallback line edge in indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");
                        }

                        edgeHandles.Add(edgeHandle);
                    }
                    else
                    {
                        // Line segment (N indices forming a polyline)
                        var segPointsXYZ = new double[indices.Length * 3];
                        for (int i = 0; i < indices.Length; i++)
                        {
                            int idx = (int)(indices[i] - 1); // 1-based to 0-based
                            segPointsXYZ[i * 3 + 0] = allPointsXYZ[idx * 3 + 0];
                            segPointsXYZ[i * 3 + 1] = allPointsXYZ[idx * 3 + 1];
                            segPointsXYZ[i * 3 + 2] = allPointsXYZ[idx * 3 + 2];
                        }

                        for (int i = 0; i < indices.Length - 1; i++)
                        {
                            int r = XbimGeometryNativeApi.xbim_edge_build_line(
                                ContextHandle,
                                segPointsXYZ[i * 3], segPointsXYZ[i * 3 + 1], segPointsXYZ[i * 3 + 2],
                                segPointsXYZ[(i + 1) * 3], segPointsXYZ[(i + 1) * 3 + 1], segPointsXYZ[(i + 1) * 3 + 2],
                                out var edgeHandle);
                            if (r != 0)
                                throw new InvalidOperationException(
                                    $"Failed to build line edge in indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");
                            edgeHandles.Add(edgeHandle);
                        }
                    }
                }

                // Build wire from edges
                using var nativeEdges = new NativeHandleArray(edgeHandles.ToArray());
                int buildResult = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                    ContextHandle,
                    nativeEdges.Ptrs, nativeEdges.Length,
                    out var resultWireHandle);

                if (buildResult != 0)
                    throw new InvalidOperationException(
                        $"Failed to build wire from edges in indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Wire(resultWireHandle);
            }
            finally
            {
                foreach (var h in edgeHandles)
                    h.Dispose();
            }
        }

        #endregion

        #region Composite Curve

        private IXWire BuildFromCompositeCurve(IIfcCompositeCurve ifcComposite)
        {
            var segmentWires = new List<Wire>();
            var allEdgeHandles = new List<NativeShapeHandle>();
            try
            {
                foreach (var segment in ifcComposite.Segments)
                {
                    var parentCurve = segment.ParentCurve;
                    if (parentCurve == null) continue;

                    // Build each segment as a wire
                    var segWire = (Wire)Build(parentCurve);
                    segmentWires.Add(segWire);

                    // Extract edges from the segment wire
                    var segEdges = segWire.GetSubShapeHandles(XShapeType.Edge);

                    if (!segment.SameSense)
                    {
                        // Reverse edge order and reverse each edge orientation
                        var reversedEdges = new List<NativeShapeHandle>();
                        for (int i = segEdges.Length - 1; i >= 0; i--)
                        {
                            int rr = XbimGeometryNativeApi.xbim_shape_reversed(
                                segEdges[i], out var reversedEdge);
                            if (rr != 0)
                                throw new InvalidOperationException(
                                    $"Failed to reverse edge in composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");
                            reversedEdges.Add(reversedEdge);
                            segEdges[i].Dispose();
                        }
                        allEdgeHandles.AddRange(reversedEdges);
                    }
                    else
                    {
                        allEdgeHandles.AddRange(segEdges);
                    }
                }

                if (allEdgeHandles.Count == 0)
                    throw new InvalidOperationException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments.");

                // Build combined wire from all edges
                using var nativeEdges = new NativeHandleArray(allEdgeHandles.ToArray());
                int buildResult = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                    ContextHandle,
                    nativeEdges.Ptrs, nativeEdges.Length,
                    out var wireHandle);

                if (buildResult != 0)
                    throw new InvalidOperationException(
                        $"Failed to build wire from composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Wire(wireHandle);
            }
            finally
            {
                foreach (var h in allEdgeHandles)
                    h.Dispose();
                foreach (var w in segmentWires)
                    w.Dispose();
            }
        }

        #endregion

        #region Single-Curve Wires

        private IXWire WrapEdgeAsWire(NativeShapeHandle edgeHandle, string curveDescription)
        {
            using var nativeEdges = new NativeHandleArray(new[] { edgeHandle });
            int result = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                ContextHandle,
                nativeEdges.Ptrs, 1,
                out var wireHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build wire from {curveDescription}: {XbimGeometryNativeApi.GetLastError()}");

            return new Wire(wireHandle);
        }

        private IXWire BuildFromTrimmedCurve(IIfcTrimmedCurve ifcTrimmed)
        {
            var builtCurve = _modelService.CurveFactory.Build(ifcTrimmed);

            if (builtCurve.Is3d)
            {
                var curve3d = (Curve)builtCurve;
                try
                {
                    var startPt = curve3d.GetPoint(curve3d.FirstParameter);
                    var endPt = curve3d.GetPoint(curve3d.LastParameter);

                    int r = XbimGeometryNativeApi.xbim_edge_build_from_curve_handle(
                        ContextHandle,
                        curve3d.Handle,
                        startPt.X, startPt.Y, startPt.Z,
                        endPt.X, endPt.Y, endPt.Z,
                        1, _modelService.Precision,
                        out var edgeHandle);

                    if (r != 0)
                        throw new InvalidOperationException(
                            $"Failed to build edge from trimmed curve #{ifcTrimmed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return WrapEdgeAsWire(edgeHandle, $"trimmed curve #{ifcTrimmed.EntityLabel}");
                }
                finally
                {
                    curve3d.Dispose();
                }
            }
            else
            {
                var curve2d = (Curve2d)builtCurve;
                NativeCurve2dHandle curveHandle = null;
                try
                {
                    curveHandle = curve2d.DetachHandle();
                    using var curveArray = new NativeHandleArray(new SafeHandle[] { curveHandle });
                    int r = XbimGeometryNativeApi.xbim_wire_build_from_2d_curves(
                        ContextHandle,
                        curveArray.Ptrs, 1,
                        _modelService.Precision,
                        _modelService.MinimumGap,
                        out var wireHandle);

                    if (r != 0)
                        throw new InvalidOperationException(
                            $"Failed to build wire from 2D trimmed curve #{ifcTrimmed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return new Wire(wireHandle);
                }
                finally
                {
                    curveHandle?.Dispose();
                }
            }
        }

        private IXWire BuildFromLine(IIfcLine ifcLine)
        {
            var edge = (Edge)_modelService.EdgeFactory.Build(ifcLine);
            return WrapEdgeAsWire(edge.Handle, $"line #{ifcLine.EntityLabel}");
        }

        private IXWire BuildFromCircle(IIfcCircle ifcCircle)
        {
            var edge = (Edge)_modelService.EdgeFactory.Build(ifcCircle);
            return WrapEdgeAsWire(edge.Handle, $"circle #{ifcCircle.EntityLabel}");
        }

        private IXWire BuildFromEllipse(IIfcEllipse ifcEllipse)
        {
            var edge = (Edge)_modelService.EdgeFactory.Build(ifcEllipse);
            return WrapEdgeAsWire(edge.Handle, $"ellipse #{ifcEllipse.EntityLabel}");
        }

        private IXWire BuildFromBSpline(IIfcBSplineCurveWithKnots ifcBSpline)
        {
            var edge = (Edge)_modelService.EdgeFactory.Build(ifcBSpline);
            return WrapEdgeAsWire(edge.Handle, $"B-spline #{ifcBSpline.EntityLabel}");
        }

        private IXWire BuildFromOffsetCurve(IIfcCurve ifcCurve)
        {
            var builtCurve = _modelService.CurveFactory.Build(ifcCurve);

            if (builtCurve.Is3d)
            {
                var curve3d = (Curve)builtCurve;
                try
                {
                    var startPt = curve3d.GetPoint(curve3d.FirstParameter);
                    var endPt = curve3d.GetPoint(curve3d.LastParameter);

                    int r = XbimGeometryNativeApi.xbim_edge_build_from_curve_handle(
                        ContextHandle,
                        curve3d.Handle,
                        startPt.X, startPt.Y, startPt.Z,
                        endPt.X, endPt.Y, endPt.Z,
                        1, _modelService.Precision,
                        out var edgeHandle);

                    if (r != 0)
                        throw new InvalidOperationException(
                            $"Failed to build edge from offset curve #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return WrapEdgeAsWire(edgeHandle, $"offset curve #{ifcCurve.EntityLabel}");
                }
                finally
                {
                    curve3d.Dispose();
                }
            }
            else
            {
                var curve2d = (Curve2d)builtCurve;
                NativeCurve2dHandle curveHandle = null;
                try
                {
                    curveHandle = curve2d.DetachHandle();
                    using var curveArray = new NativeHandleArray(new SafeHandle[] { curveHandle });
                    int r = XbimGeometryNativeApi.xbim_wire_build_from_2d_curves(
                        ContextHandle,
                        curveArray.Ptrs, 1,
                        _modelService.Precision,
                        _modelService.MinimumGap,
                        out var wireHandle);

                    if (r != 0)
                        throw new InvalidOperationException(
                            $"Failed to build wire from 2D offset curve #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return new Wire(wireHandle);
                }
                finally
                {
                    curveHandle?.Dispose();
                }
            }
        }

        #endregion

        #region Profile Wires

        private IXWire BuildOpenProfileWire(IIfcArbitraryOpenProfileDef openProfile)
        {
            if (openProfile.ProfileType != Xbim.Ifc4.Interfaces.IfcProfileTypeEnum.CURVE)
                throw new InvalidOperationException(
                    $"IfcArbitraryOpenProfileDef #{openProfile.EntityLabel} must have ProfileType=CURVE.");

            var curve = openProfile.Curve;
            if (curve == null)
                throw new InvalidOperationException(
                    $"IfcArbitraryOpenProfileDef #{openProfile.EntityLabel} has no Curve.");

            return Build(curve);
        }

        private IXWire BuildCenterLineProfileWire(IIfcCenterLineProfileDef centerLine)
        {
            if (centerLine.Thickness <= 0)
                throw new InvalidOperationException(
                    $"IfcCenterLineProfileDef #{centerLine.EntityLabel} has invalid thickness.");

            var curveFactory = (CurveFactory)_modelService.CurveFactory;

            // Build center line as 2D curve
            var centre = (Curve2d)curveFactory.BuildCurve2d(centerLine.Curve);
            NativeCurve2dHandle aCurveHandle = null;
            NativeCurve2dHandle bCurveHandle = null;
            NativeCurve2dHandle lineAEndToBEnd = null;
            NativeCurve2dHandle lineBStartToAStart = null;

            try
            {
                // Verify center line is open (not closed)
                var centreStart = centre.GetPoint(centre.FirstParameter);
                var centreEnd = centre.GetPoint(centre.LastParameter);
                double dist = Math.Sqrt(
                    (centreStart.X - centreEnd.X) * (centreStart.X - centreEnd.X) +
                    (centreStart.Y - centreEnd.Y) * (centreStart.Y - centreEnd.Y));

                if (dist < _modelService.Precision)
                    throw new InvalidOperationException(
                        $"IfcCenterLineProfileDef #{centerLine.EntityLabel} must have an open curve for the centre line.");

                // Build two offset curves at ±thickness/2
                double halfThickness = centerLine.Thickness / 2.0;

                int rA = XbimGeometryNativeApi.xbim_curve2d_build_offset(
                    ContextHandle, centre.Handle, halfThickness, out aCurveHandle);
                if (rA != 0)
                    throw new InvalidOperationException(
                        $"Failed to build offset curve A for IfcCenterLineProfileDef #{centerLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                int rB = XbimGeometryNativeApi.xbim_curve2d_build_offset(
                    ContextHandle, centre.Handle, -halfThickness, out bCurveHandle);
                if (rB != 0)
                    throw new InvalidOperationException(
                        $"Failed to build offset curve B for IfcCenterLineProfileDef #{centerLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                // Evaluate endpoints of both offset curves
                int rAParams = XbimGeometryNativeApi.xbim_curve2d_parameters(aCurveHandle, out double aFirst, out double aLast);
                if (rAParams != 0) throw new InvalidOperationException($"Failed to get offset curve A parameters: {XbimGeometryNativeApi.GetLastError()}");

                int rBParams = XbimGeometryNativeApi.xbim_curve2d_parameters(bCurveHandle, out double bFirst, out double bLast);
                if (rBParams != 0) throw new InvalidOperationException($"Failed to get offset curve B parameters: {XbimGeometryNativeApi.GetLastError()}");

                XbimGeometryNativeApi.xbim_curve2d_value(aCurveHandle, aFirst, out double aStartX, out double aStartY);
                XbimGeometryNativeApi.xbim_curve2d_value(aCurveHandle, aLast, out double aEndX, out double aEndY);
                XbimGeometryNativeApi.xbim_curve2d_value(bCurveHandle, bFirst, out double bStartX, out double bStartY);
                XbimGeometryNativeApi.xbim_curve2d_value(bCurveHandle, bLast, out double bEndX, out double bEndY);

                // Build connecting lines
                int rLine1 = XbimGeometryNativeApi.xbim_curve2d_build_line(
                    ContextHandle, aEndX, aEndY, bEndX, bEndY, out lineAEndToBEnd);
                if (rLine1 != 0)
                    throw new InvalidOperationException(
                        $"Failed to build connecting line for IfcCenterLineProfileDef #{centerLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                int rLine2 = XbimGeometryNativeApi.xbim_curve2d_build_line(
                    ContextHandle, bStartX, bStartY, aStartX, aStartY, out lineBStartToAStart);
                if (rLine2 != 0)
                    throw new InvalidOperationException(
                        $"Failed to build connecting line for IfcCenterLineProfileDef #{centerLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                // Reverse bCurve so it goes bEnd→bStart (completing the closed loop)
                XbimGeometryNativeApi.xbim_curve2d_reverse(bCurveHandle);

                // Assemble closed wire: aCurve → line(aEnd→bEnd) → bCurve(reversed) → line(bStart→aStart)
                var allCurves = new SafeHandle[] { aCurveHandle, lineAEndToBEnd, bCurveHandle, lineBStartToAStart };
                using var nativeCurves = new NativeHandleArray(allCurves);

                int rWire = XbimGeometryNativeApi.xbim_wire_build_from_2d_curves(
                    ContextHandle,
                    nativeCurves.Ptrs, 4,
                    _modelService.Precision,
                    _modelService.MinimumGap,
                    out var wireHandle);

                if (rWire != 0)
                    throw new InvalidOperationException(
                        $"Failed to build wire for IfcCenterLineProfileDef #{centerLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Wire(wireHandle);
            }
            finally
            {
                centre.Dispose();
                aCurveHandle?.Dispose();
                bCurveHandle?.Dispose();
                lineAEndToBEnd?.Dispose();
                lineBStartToAStart?.Dispose();
            }
        }

        #endregion

        #region Directrix

        /// <summary>
        /// Builds a wire from a curve with optional parametric trimming, suitable for
        /// use as a sweep directrix. Handles the polyline trim (0:1) workaround for
        /// authoring tools that incorrectly set trim values.
        /// </summary>
        internal IXWire BuildDirectrixWire(IIfcCurve ifcCurve, double? startParam, double? endParam)
        {
            double start = startParam ?? double.NaN;
            double end = endParam ?? double.NaN;

            // Workaround: some authoring tools set polyline trim to (0, 1) meaning "entire line"
            if (ifcCurve is IIfcPolyline &&
                !double.IsNaN(start) && Math.Abs(start) < _modelService.Precision &&
                !double.IsNaN(end) && Math.Abs(end - 1.0) < _modelService.Precision)
            {
                var modelFactors = _modelService.Model.ModelFactors as XbimModelFactors;
                if (modelFactors != null && modelFactors.ApplyWorkAround("#PolylineTrimLengthOneForEntireLine"))
                {
                    _logger.LogDebug("Polyline trim (0:1) does not comply with schema, expanding to entire length");
                    end = double.NaN;
                }
            }

            // Composite and indexed poly curves need per-segment trimming (WIRE-010)
            if (ifcCurve is IIfcCompositeCurve || ifcCurve is IIfcIndexedPolyCurve)
                throw new NotImplementedException(
                    $"Directrix trimming for {ifcCurve.ExpressType.ExpressName} #{ifcCurve.EntityLabel} is not yet implemented (WIRE-010).");

            // Unbounded curves (IIfcLine) can't be built into a wire directly.
            // Build the trimmed curve via CurveFactory, then wrap as wire.
            if (ifcCurve is IIfcLine)
                return BuildDirectrixFromCurveFactory(ifcCurve, start, end);

            // Build full wire from the bounded curve
            var wire = (Wire)Build(ifcCurve);

            // If no trimming needed, return the wire as-is
            if (double.IsNaN(start) && double.IsNaN(end))
                return wire;

            try
            {
                // trim
                int result = XbimGeometryNativeApi.xbim_wire_build_trimmed(
                    ContextHandle,
                    wire.Handle,
                    double.IsNaN(start) ? 0.0 : start,
                    double.IsNaN(end) ? double.MaxValue : end,
                    1, // sameSense
                    _modelService.Precision,
                    _modelService.RadianFactor,
                    out var trimmedHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to trim directrix wire for #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Wire(trimmedHandle);
            }
            finally
            {
                wire.Dispose();
            }
        }

        /// <summary>
        /// Builds a directrix wire by first constructing the trimmed curve geometry
        /// via CurveFactory, then wrapping the result as an edge and wire.
        /// Used for unbounded curves that can't be directly built into a wire.
        /// </summary>
        private IXWire BuildDirectrixFromCurveFactory(IIfcCurve ifcCurve, double start, double end)
        {
            double? sp = double.IsNaN(start) ? null : start;
            double? ep = double.IsNaN(end) ? null : end;
            var builtCurve = _modelService.CurveFactory.BuildDirectrix(ifcCurve, sp, ep);

            if (builtCurve.Is3d)
            {
                var curve3d = (Curve)builtCurve;
                try
                {
                    var startPt = curve3d.GetPoint(curve3d.FirstParameter);
                    var endPt = curve3d.GetPoint(curve3d.LastParameter);

                    int r = XbimGeometryNativeApi.xbim_edge_build_from_curve_handle(
                        ContextHandle,
                        curve3d.Handle,
                        startPt.X, startPt.Y, startPt.Z,
                        endPt.X, endPt.Y, endPt.Z,
                        1, _modelService.Precision,
                        out var edgeHandle);

                    if (r != 0)
                        throw new InvalidOperationException(
                            $"Failed to build edge from directrix curve #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return WrapEdgeAsWire(edgeHandle, $"directrix #{ifcCurve.EntityLabel}");
                }
                finally
                {
                    curve3d.Dispose();
                }
            }
            else
            {
                var curve2d = (Curve2d)builtCurve;
                NativeCurve2dHandle curveHandle = null;
                try
                {
                    curveHandle = curve2d.DetachHandle();
                    using var curveArray = new NativeHandleArray(new SafeHandle[] { curveHandle });
                    int r = XbimGeometryNativeApi.xbim_wire_build_from_2d_curves(
                        ContextHandle,
                        curveArray.Ptrs, 1,
                        _modelService.Precision,
                        _modelService.MinimumGap,
                        out var wireHandle);

                    if (r != 0)
                        throw new InvalidOperationException(
                            $"Failed to build wire from 2D directrix curve #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return new Wire(wireHandle);
                }
                finally
                {
                    curveHandle?.Dispose();
                }
            }
        }

        #endregion
    }
}
