using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    internal partial class CurveFactory
    {
        /// <summary>
        /// Routes an IFC curve entity to the appropriate 2D curve builder based on its type.
        /// Returns a <see cref="XbimCurve2d"/> wrapper with Is3d=false.
        /// </summary>
        internal IXCurve BuildCurve2d(IIfcCurve curve)
        {
            if (curve is IIfcLine ifcLine)
                return BuildLine2d(ifcLine);

            if (curve is IIfcCircle ifcCircle)
                return BuildCircle2d(ifcCircle);

            if (curve is IIfcEllipse ifcEllipse)
                return BuildEllipse2d(ifcEllipse);

            if (curve is IIfcTrimmedCurve ifcTrimmed)
                return BuildTrimmedCurve2d(ifcTrimmed);

            if (curve is IIfcBSplineCurveWithKnots ifcBSpline)
                return BuildBSpline2d(ifcBSpline);

            if (curve is IIfcIndexedPolyCurve ifcIndexedPoly)
                return BuildIndexedPolyCurve2d(ifcIndexedPoly);

            if (curve is IIfcPolyline ifcPolyline)
                return BuildPolyline2d(ifcPolyline);

            if (curve is IIfcCompositeCurve ifcComposite)
            {
                if (curve is Ifc4x3.GeometryResource.IfcCompositeCurve composite4x3)
                {
                    var handle = Build4x3CompositeCurve(null, composite4x3);
                    return new XbimBoundedCurve2d(handle, XCurveType.IfcCompositeCurve);
                }
                return BuildCompositeCurve2d(ifcComposite);
            }

            if (curve is IIfcOffsetCurve2D ifcOffset2D)
                return BuildOffsetCurve2d(ifcOffset2D);

            throw new NotSupportedException(
                $"2D curve type {curve.ExpressType.ExpressName} #{curve.EntityLabel} is not yet supported.");
        }

        #region XbimLine2d

        /// <summary>
        /// Builds an unbounded 2D line from an IFC line entity.
        /// The line's direction magnitude is stored on the returned XbimLine2d
        /// for use in trim parameter conversion.
        /// </summary>
        private XbimLine2d BuildLine2d(IIfcLine ifcLine)
        {
            var coords = ifcLine.Pnt.Coordinates;
            double ox = coords[0];
            double oy = coords[1];

            var dirRatios = ifcLine.Dir.Orientation.DirectionRatios;
            double dx = dirRatios[0];
            double dy = dirRatios[1];

            // Normalize direction
            double mag = Math.Sqrt(dx * dx + dy * dy);
            if (mag < 1e-15)
                throw new XbimGeometryServiceException(
                    $"IIfcLine #{ifcLine.EntityLabel} has zero-length direction vector.");
            dx /= mag;
            dy /= mag;

            int result = XbimGeometryNativeApi.xbim_curve2d_build_unbounded_line(
                ContextHandle,
                ox, oy,
                dx, dy,
                out var nativeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build 2D line #{ifcLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimLine2d(nativeHandle,
                new XPoint(ox, oy),
                new XVector(dx, dy),
                ifcLine.Dir.Magnitude);
        }

        #endregion

        #region XbimCircle2d

        /// <summary>
        /// Builds a 2D circle from an IFC circle entity with center, radius, and
        /// reference direction extracted from a 2D axis placement.
        /// </summary>
        private XbimCircle2d BuildCircle2d(IIfcCircle ifcCircle)
        {
            if (ifcCircle.Radius <= 0)
                throw new XbimGeometryServiceException(
                    $"IIfcCircle #{ifcCircle.EntityLabel} has invalid radius {ifcCircle.Radius}. Radius must be greater than zero.");

            if (ifcCircle.Position is not IIfcAxis2Placement2D axis2d)
                throw new XbimGeometryServiceException(
                    $"IIfcCircle #{ifcCircle.EntityLabel} (Dim=2) has no valid 2D placement.");

            double cx = axis2d.Location.Coordinates[0];
            double cy = axis2d.Location.Coordinates[1];

            double refDirX = 1, refDirY = 0;
            if (axis2d.RefDirection != null)
            {
                refDirX = axis2d.RefDirection.DirectionRatios[0];
                refDirY = axis2d.RefDirection.DirectionRatios[1];
                double mag = Math.Sqrt(refDirX * refDirX + refDirY * refDirY);
                if (mag > 1e-15) { refDirX /= mag; refDirY /= mag; }
                else { refDirX = 1; refDirY = 0; }
            }

            int result = XbimGeometryNativeApi.xbim_curve2d_build_circle(
                ContextHandle,
                cx, cy,
                ifcCircle.Radius,
                refDirX, refDirY,
                out var nativeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build 2D circle #{ifcCircle.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var position = new XAxis2Placement2d(
                new XPoint(cx, cy),
                new XDirection(refDirX, refDirY));
            return new XbimCircle2d(nativeHandle, ifcCircle.Radius, position);
        }

        #endregion

        #region XbimEllipse2d

        /// <summary>
        /// Builds a 2D ellipse from an IFC ellipse entity with center, semi-axes, and
        /// reference direction extracted from a 2D axis placement. The native function
        /// handles automatic semi-axis swapping when SemiAxis1 &lt; SemiAxis2.
        /// </summary>
        private XbimEllipse2d BuildEllipse2d(IIfcEllipse ifcEllipse)
        {
            if (ifcEllipse.Position is not IIfcAxis2Placement2D axis2d)
                throw new XbimGeometryServiceException(
                    $"IIfcEllipse #{ifcEllipse.EntityLabel} (Dim=2) has no valid 2D placement.");

            double cx = axis2d.Location.Coordinates[0];
            double cy = axis2d.Location.Coordinates[1];

            double refDirX = 1, refDirY = 0;
            if (axis2d.RefDirection != null)
            {
                refDirX = axis2d.RefDirection.DirectionRatios[0];
                refDirY = axis2d.RefDirection.DirectionRatios[1];
                double mag = Math.Sqrt(refDirX * refDirX + refDirY * refDirY);
                if (mag > 1e-15) { refDirX /= mag; refDirY /= mag; }
                else { refDirX = 1; refDirY = 0; }
            }

            int result = XbimGeometryNativeApi.xbim_curve2d_build_ellipse(
                ContextHandle,
                cx, cy,
                ifcEllipse.SemiAxis1, ifcEllipse.SemiAxis2,
                refDirX, refDirY,
                out var nativeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build 2D ellipse #{ifcEllipse.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var position = new XAxis2Placement2d(
                new XPoint(cx, cy),
                new XDirection(refDirX, refDirY));
            return new XbimEllipse2d(nativeHandle, ifcEllipse.SemiAxis1, ifcEllipse.SemiAxis2, position);
        }

        #endregion

        #region BSpline2d

        /// <summary>
        /// Builds a 2D B-spline curve from an IFC B-spline entity. Extracts 2D control points,
        /// knots, multiplicities, degree, and optional weights (for rational B-splines).
        /// </summary>
        private XbimBSplineCurve2d BuildBSpline2d(IIfcBSplineCurveWithKnots ifcBSpline)
        {
            var controlPoints = ifcBSpline.ControlPointsList;
            int numPoles = controlPoints.Count;
            var polesXY = new double[numPoles * 2];

            for (int i = 0; i < numPoles; i++)
            {
                var cp = controlPoints[i];
                polesXY[i * 2 + 0] = cp.Coordinates[0];
                polesXY[i * 2 + 1] = cp.Coordinates[1];
            }

            var knotValues = ifcBSpline.Knots.ToArray();
            int numKnots = knotValues.Length;
            var knots = new double[numKnots];
            for (int i = 0; i < numKnots; i++)
                knots[i] = knotValues[i];

            var multValues = ifcBSpline.KnotMultiplicities.ToArray();
            var multiplicities = new int[multValues.Length];
            for (int i = 0; i < multValues.Length; i++)
                multiplicities[i] = (int)multValues[i];

            int degree = (int)ifcBSpline.Degree;

            double[]? weights = null;
            if (ifcBSpline is IIfcRationalBSplineCurveWithKnots rational)
            {
                var weightValues = rational.WeightsData;
                weights = new double[weightValues.Count];
                for (int i = 0; i < weightValues.Count; i++)
                    weights[i] = weightValues[i];
            }

            int result = XbimGeometryNativeApi.xbim_curve2d_build_bspline(
                ContextHandle,
                polesXY, numPoles,
                knots, numKnots,
                multiplicities,
                degree,
                weights,
                out var nativeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build 2D B-spline curve #{ifcBSpline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var curveType = ifcBSpline is IIfcRationalBSplineCurveWithKnots
                ? XCurveType.IfcRationalBSplineCurveWithKnots
                : XCurveType.IfcBSplineCurveWithKnots;

            bool isRational = ifcBSpline is IIfcRationalBSplineCurveWithKnots;
            return new XbimBSplineCurve2d(nativeHandle, curveType,
                XGeometricContinuity.GeomAbs_CN, false, isRational);
        }

        #endregion

        #region XbimTrimmedCurve2d

        /// <summary>
        /// Builds a 2D trimmed curve from an IFC trimmed curve entity.
        /// Parses Trim1/Trim2 values (Cartesian and/or parametric), respects MasterRepresentation
        /// and SenseAgreement, and dispatches to the appropriate native 2D trim builder.
        /// For circle basis curves, uses arc-of-circle construction; for ellipse, arc-of-ellipse;
        /// for other curves, generic parametric trimming.
        /// </summary>
        private XbimTrimmedCurve2d BuildTrimmedCurve2d(IIfcTrimmedCurve ifcTrimmed)
        {
            // Formal proposition: NoTrimOfBoundedCurves — stricter in 2D (throw, not warn)
            if (ifcTrimmed.BasisCurve is IIfcBoundedCurve)
                throw new XbimGeometryServiceException(
                    $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Formal Proposition NoTrimOfBoundedCurves — " +
                    "already bounded curves shall not be trimmed.");

            // Build the 2D basis curve — ownership transfers to XbimTrimmedCurve2d on success
            var basisCurve = (XbimCurve2d)BuildCurve2d(ifcTrimmed.BasisCurve);
            try
            {
                bool isConic = ifcTrimmed.BasisCurve is IIfcConic;
                bool isCircle = ifcTrimmed.BasisCurve is IIfcCircle;
                bool isEllipse = ifcTrimmed.BasisCurve is IIfcEllipse;
                bool sense = ifcTrimmed.SenseAgreement;

                ExtractTrimParameters(ifcTrimmed, isConic,
                    out double u1, out double u2, ref sense,
                    out bool useCartesian, out var cp1, out var cp2);

                if (useCartesian)
                {
                    double px1 = cp1!.Coordinates[0];
                    double py1 = cp1.Coordinates[1];
                    double px2 = cp2!.Coordinates[0];
                    double py2 = cp2.Coordinates[1];

                    int r1 = XbimGeometryNativeApi.xbim_curve2d_project_point(
                        ContextHandle, basisCurve.Handle,
                        px1, py1, _modelService.MinimumGap, out u1);
                    if (r1 != 0)
                        throw new XbimGeometryServiceException(
                            $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Trim Point1 is not on the 2D basis curve.");

                    int r2 = XbimGeometryNativeApi.xbim_curve2d_project_point(
                        ContextHandle, basisCurve.Handle,
                        px2, py2, _modelService.MinimumGap, out u2);
                    if (r2 != 0)
                        throw new XbimGeometryServiceException(
                            $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Trim Point2 is not on the 2D basis curve.");

                    // Sanity check
                    if (double.IsNegativeInfinity(u1) || double.IsPositiveInfinity(u2))
                        throw new XbimGeometryServiceException(
                            $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: error converting trim points.");

                    HandleEqualTrimParams(ifcTrimmed, isConic, ref u1, ref u2, ref sense);
                }

                // Build trimmed curve using specialized native functions based on basis type
                int trimResult;
                NativeCurve2dHandle trimHandle;

                if (isCircle)
                {
                    trimResult = XbimGeometryNativeApi.xbim_curve2d_build_arc_of_circle(
                        ContextHandle, basisCurve.Handle, u1, u2, sense ? 1 : 0, out trimHandle);
                }
                else if (isEllipse)
                {
                    trimResult = XbimGeometryNativeApi.xbim_curve2d_build_arc_of_ellipse(
                        ContextHandle, basisCurve.Handle, u1, u2, sense ? 1 : 0, out trimHandle);
                }
                else
                {
                    trimResult = XbimGeometryNativeApi.xbim_curve2d_build_trimmed(
                        ContextHandle, basisCurve.Handle, u1, u2, sense ? 1 : 0, out trimHandle);
                }

                if (trimResult != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build 2D trimmed curve #{ifcTrimmed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimTrimmedCurve2d(trimHandle, basisCurve);
            }
            catch
            {
                basisCurve.Dispose();
                throw;
            }
        }

        #endregion

        #region Polyline2d

        /// <summary>
        /// Builds a 2D polyline from an IFC polyline entity. Two-point polylines produce a single
        /// trimmed line segment. Multi-point polylines are built as individual trimmed line segments
        /// joined into a composite B-spline, with degenerate (zero-length) segments skipped.
        /// </summary>
        private XbimBoundedCurve2d BuildPolyline2d(IIfcPolyline ifcPolyline)
        {
            var (px, py) = ExtractPolylinePoints2d(ifcPolyline);
            int pointCount = px.Length;

            if (pointCount < 2)
                throw new XbimGeometryServiceException(
                    $"IIfcPolyline #{ifcPolyline.EntityLabel} has fewer than 2 points.");

            double precision = _modelService.Precision;

            if (pointCount == 2)
            {
                double dist = Math.Sqrt(
                    (px[1] - px[0]) * (px[1] - px[0]) +
                    (py[1] - py[0]) * (py[1] - py[0]));

                if (dist < precision)
                {
                    _logger.LogInformation(
                        "IIfcPolyline #{Label}: only 2 identical points — ignored.",
                        ifcPolyline.EntityLabel);
                    throw new XbimGeometryServiceException(
                        $"IIfcPolyline #{ifcPolyline.EntityLabel} has only 2 identical points.");
                }

                int lineResult = XbimGeometryNativeApi.xbim_curve2d_build_line(
                    ContextHandle, px[0], py[0], px[1], py[1], out var lineHandle);

                if (lineResult != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build 2D polyline line segment #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimBoundedCurve2d(lineHandle, XCurveType.IfcPolyline);
            }

            // 3+ points: build individual 2D lines, skip degenerate segments
            var segments = new List<NativeCurve2dHandle>();
            try
            {
                int lastIdx = 0;
                for (int i = 1; i < pointCount; i++)
                {
                    double dx = px[i] - px[lastIdx];
                    double dy = py[i] - py[lastIdx];
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist < precision)
                        continue; // skip degenerate segment

                    int lineResult = XbimGeometryNativeApi.xbim_curve2d_build_line(
                        ContextHandle, px[lastIdx], py[lastIdx], px[i], py[i], out var lineHandle);

                    if (lineResult != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build 2D polyline segment #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    segments.Add(lineHandle);
                    lastIdx = i;
                }

                if (segments.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcPolyline #{ifcPolyline.EntityLabel} has no non-degenerate 2D segments.");

                if (segments.Count == 1)
                {
                    var singleHandle = segments[0];
                    segments.Clear();
                    return new XbimBoundedCurve2d(singleHandle, XCurveType.IfcPolyline);
                }

                using var nativeSegments = new NativeHandleArray(
                    segments.Select(s => (SafeHandle)s).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    _modelService.MinimumGap,
                    out var compositeHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build 2D polyline #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimBoundedCurve2d(compositeHandle, XCurveType.IfcPolyline);
            }
            finally
            {
                foreach (var s in segments)
                    s.Dispose();
            }
        }

        #endregion

        #region CompositeCurve2d

        /// <summary>
        /// Builds a 2D composite curve by building each segment as a 2D curve, applying
        /// SameSense reversal, and joining all segments into a single composite B-spline.
        /// Skips consecutive duplicate segments (ArchiCAD bug workaround).
        /// </summary>
        private XbimBoundedCurve2d BuildCompositeCurve2d(IIfcCompositeCurve ifcComposite)
        {
            var segmentCurves = BuildCompositeCurveSegments2d(ifcComposite);
            try
            {
                if (segmentCurves.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid 2D segments.");

                using var nativeSegments = new NativeHandleArray(
                    segmentCurves.Select(c => (SafeHandle)c.Handle).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    _modelService.MinimumGap,
                    out var compositeHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build 2D composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimBoundedCurve2d(compositeHandle, XCurveType.IfcCompositeCurve);
            }
            finally
            {
                foreach (var c in segmentCurves)
                    c.Dispose();
            }
        }

        #endregion

        #region IndexedPolyCurve2d

        /// <summary>
        /// Builds a 2D indexed poly curve from an IFC indexed poly curve entity.
        /// Handles ArcIndex segments (circle arc through 3 points with collinear fallback),
        /// LineIndex segments (consecutive point-to-point lines), and the no-segments case
        /// (sequential lines through all points). Joins segments into a single composite B-spline.
        /// </summary>
        private XbimBoundedCurve2d BuildIndexedPolyCurve2d(IIfcIndexedPolyCurve ifcIndexed)
        {
            var segments = BuildIndexedPolyCurveSegments2d(ifcIndexed);
            try
            {
                if (segments.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel} has no valid 2D segments.");

                // Single segment — no need for composite joining
                if (segments.Count == 1)
                {
                    var singleHandle = segments[0];
                    segments.Clear(); // prevent dispose of the returned handle
                    return new XbimBoundedCurve2d(singleHandle, XCurveType.IfcIndexedPolyCurve);
                }

                // Join all segments into a single B-spline
                using var nativeSegments = new NativeHandleArray(
                    segments.Select(s => (SafeHandle)s).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    _modelService.MinimumGap,
                    out var compositeHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to join 2D IndexedPolyCurve segments #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimBoundedCurve2d(compositeHandle, XCurveType.IfcIndexedPolyCurve);
            }
            finally
            {
                foreach (var s in segments)
                    s.Dispose();
            }
        }

        /// <summary>
        /// Builds the individual 2D curve segments for an indexed poly curve without joining them.
        /// The caller is responsible for disposing the returned handles.
        /// </summary>
        internal List<NativeCurve2dHandle> BuildIndexedPolyCurveSegments2d(IIfcIndexedPolyCurve ifcIndexed)
        {
            var points = ExtractIndexedPoints2d(ifcIndexed);
            var segments = new List<NativeCurve2dHandle>();

            if (ifcIndexed.Segments != null && ifcIndexed.Segments.Any())
            {
                foreach (var segment in ifcIndexed.Segments)
                {
                    if (segment is IfcArcIndex arcIndex)
                    {
                        var indices = (System.Collections.IList)arcIndex.Value;
                        if (indices.Count != 3)
                            throw new XbimGeometryServiceException(
                                $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: ArcIndex must have exactly 3 indices.");

                        int i1 = (int)(long)((IfcPositiveInteger)indices[0]!).Value - 1;
                        int i2 = (int)(long)((IfcPositiveInteger)indices[1]!).Value - 1;
                        int i3 = (int)(long)((IfcPositiveInteger)indices[2]!).Value - 1;

                        var (sx, sy) = points[i1];
                        var (mx, my) = points[i2];
                        var (ex, ey) = points[i3];

                        // Native function handles collinear fallback internally (returns line segment)
                        int arcResult = XbimGeometryNativeApi.xbim_curve2d_build_arc_3pt(
                            ContextHandle, sx, sy, mx, my, ex, ey, out var arcHandle);

                        if (arcResult != 0)
                            throw new XbimGeometryServiceException(
                                $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to build 2D arc segment: {XbimGeometryNativeApi.GetLastError()}");

                        segments.Add(arcHandle);
                    }
                    else if (segment is IfcLineIndex lineIndex)
                    {
                        var indices = (System.Collections.IList)lineIndex.Value;
                        if (indices.Count < 2)
                            throw new XbimGeometryServiceException(
                                $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: LineIndex must have at least 2 indices.");

                        for (int p = 0; p < indices.Count - 1; p++)
                        {
                            int idx1 = (int)(long)((IfcPositiveInteger)indices[p]!).Value - 1;
                            int idx2 = (int)(long)((IfcPositiveInteger)indices[p + 1]!).Value - 1;

                            var (x1, y1) = points[idx1];
                            var (x2, y2) = points[idx2];

                            int lineResult = XbimGeometryNativeApi.xbim_curve2d_build_line(
                                ContextHandle, x1, y1, x2, y2, out var lineHandle);

                            if (lineResult != 0)
                                throw new XbimGeometryServiceException(
                                    $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to build 2D line segment: {XbimGeometryNativeApi.GetLastError()}");

                            segments.Add(lineHandle);
                        }
                    }
                }
            }
            else
            {
                // No segments — connect all points sequentially with straight lines
                for (int p = 0; p < points.Count - 1; p++)
                {
                    var (x1, y1) = points[p];
                    var (x2, y2) = points[p + 1];

                    int lineResult = XbimGeometryNativeApi.xbim_curve2d_build_line(
                        ContextHandle, x1, y1, x2, y2, out var lineHandle);

                    if (lineResult != 0)
                        throw new XbimGeometryServiceException(
                            $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to build 2D sequential line segment: {XbimGeometryNativeApi.GetLastError()}");

                    segments.Add(lineHandle);
                }
            }

            return segments;
        }

        #endregion

        #region OffsetCurve2d

        /// <summary>
        /// Builds a 2D offset curve from an IFC offset curve 2D entity.
        /// Offsets the basis curve by the specified distance in the 2D plane.
        /// </summary>
        private XbimCurve2d BuildOffsetCurve2d(IIfcOffsetCurve2D ifcOffset)
        {
            using var basisCurve = (XbimCurve2d)BuildCurve2d(ifcOffset.BasisCurve);

            int result = XbimGeometryNativeApi.xbim_curve2d_build_offset(
                ContextHandle, basisCurve.Handle,
                ifcOffset.Distance,
                out var offsetHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build 2D offset curve #{ifcOffset.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimCurve2d(offsetHandle, XCurveType.IfcOffsetCurve2D);
        }

        #endregion
    }
}
