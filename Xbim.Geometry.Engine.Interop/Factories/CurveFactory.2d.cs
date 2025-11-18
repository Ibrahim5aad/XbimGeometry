using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    internal partial class CurveFactory
    {
        /// <summary>
        /// Routes an IFC curve entity to the appropriate 2D curve builder based on its type.
        /// Returns a <see cref="Curve2d"/> wrapper with Is3d=false.
        /// </summary>
        private IXCurve BuildCurve2d(IIfcCurve curve)
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
                return BuildCompositeCurve2d(ifcComposite);

            // TODO: CURVE-R011 — Implement 2D OffsetCurve
            if (curve is IIfcOffsetCurve2D)
                throw new NotSupportedException(
                    $"2D IfcOffsetCurve2D #{curve.EntityLabel} is not yet supported. See CURVE-R011.");

            throw new NotSupportedException(
                $"2D curve type {curve.ExpressType.ExpressName} #{curve.EntityLabel} is not yet supported.");
        }

        #region Line2d

        /// <summary>
        /// Builds a 2D line from an IFC line entity. Computes the endpoint from the
        /// origin plus direction scaled by magnitude.
        /// </summary>
        private Curve2d BuildLine2d(IIfcLine ifcLine)
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
                throw new InvalidOperationException(
                    $"IIfcLine #{ifcLine.EntityLabel} has zero-length direction vector.");
            dx /= mag;
            dy /= mag;

            // Compute endpoint: origin + direction * magnitude
            double magnitude = ifcLine.Dir.Magnitude;
            double ex = ox + dx * magnitude;
            double ey = oy + dy * magnitude;

            int result = XbimGeometryNativeApi.xbim_curve2d_build_line(
                ContextHandle,
                ox, oy,
                ex, ey,
                out var nativeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build 2D line #{ifcLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve2d(nativeHandle, XCurveType.IfcLine);
        }

        #endregion

        #region Circle2d

        /// <summary>
        /// Builds a 2D circle from an IFC circle entity with center, radius, and
        /// reference direction extracted from a 2D axis placement.
        /// </summary>
        private Curve2d BuildCircle2d(IIfcCircle ifcCircle)
        {
            if (ifcCircle.Radius <= 0)
                throw new InvalidOperationException(
                    $"IIfcCircle #{ifcCircle.EntityLabel} has invalid radius {ifcCircle.Radius}. Radius must be greater than zero.");

            if (ifcCircle.Position is not IIfcAxis2Placement2D axis2d)
                throw new InvalidOperationException(
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
                throw new InvalidOperationException(
                    $"Failed to build 2D circle #{ifcCircle.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve2d(nativeHandle, XCurveType.IfcCircle);
        }

        #endregion

        #region Ellipse2d

        /// <summary>
        /// Builds a 2D ellipse from an IFC ellipse entity with center, semi-axes, and
        /// reference direction extracted from a 2D axis placement. The native function
        /// handles automatic semi-axis swapping when SemiAxis1 &lt; SemiAxis2.
        /// </summary>
        private Curve2d BuildEllipse2d(IIfcEllipse ifcEllipse)
        {
            if (ifcEllipse.Position is not IIfcAxis2Placement2D axis2d)
                throw new InvalidOperationException(
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
                throw new InvalidOperationException(
                    $"Failed to build 2D ellipse #{ifcEllipse.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve2d(nativeHandle, XCurveType.IfcEllipse);
        }

        #endregion

        #region BSpline2d

        /// <summary>
        /// Builds a 2D B-spline curve from an IFC B-spline entity. Extracts 2D control points,
        /// knots, multiplicities, degree, and optional weights (for rational B-splines).
        /// </summary>
        private Curve2d BuildBSpline2d(IIfcBSplineCurveWithKnots ifcBSpline)
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
                throw new InvalidOperationException(
                    $"Failed to build 2D B-spline curve #{ifcBSpline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var curveType = ifcBSpline is IIfcRationalBSplineCurveWithKnots
                ? XCurveType.IfcRationalBSplineCurveWithKnots
                : XCurveType.IfcBSplineCurveWithKnots;

            return new Curve2d(nativeHandle, curveType);
        }

        #endregion

        #region TrimmedCurve2d

        /// <summary>
        /// Builds a 2D trimmed curve from an IFC trimmed curve entity.
        /// Parses Trim1/Trim2 values (Cartesian and/or parametric), respects MasterRepresentation
        /// and SenseAgreement, and dispatches to the appropriate native 2D trim builder.
        /// For circle basis curves, uses arc-of-circle construction; for ellipse, arc-of-ellipse;
        /// for other curves, generic parametric trimming.
        /// </summary>
        private Curve2d BuildTrimmedCurve2d(IIfcTrimmedCurve ifcTrimmed)
        {
            // Formal proposition: NoTrimOfBoundedCurves — stricter in 2D (throw, not warn)
            if (ifcTrimmed.BasisCurve is IIfcBoundedCurve)
                throw new InvalidOperationException(
                    $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Formal Proposition NoTrimOfBoundedCurves — " +
                    "already bounded curves shall not be trimmed.");

            // Build the 2D basis curve
            var basisCurve = (Curve2d)BuildCurve2d(ifcTrimmed.BasisCurve);

            bool isConic = ifcTrimmed.BasisCurve is IIfcConic;
            bool isCircle = ifcTrimmed.BasisCurve is IIfcCircle;
            bool isEllipse = ifcTrimmed.BasisCurve is IIfcEllipse;
            bool sense = ifcTrimmed.SenseAgreement;
            bool preferCartesian = ifcTrimmed.MasterRepresentation == IfcTrimmingPreference.CARTESIAN;

            // Parse trim selects
            double u1 = double.NegativeInfinity;
            double u2 = double.PositiveInfinity;
            IIfcCartesianPoint? cp1 = null;
            IIfcCartesianPoint? cp2 = null;

            foreach (var trim in ifcTrimmed.Trim1)
            {
                if (trim is IIfcCartesianPoint pt)
                    cp1 = pt;
                else if (trim is Xbim.Ifc4.MeasureResource.IfcParameterValue pv)
                    u1 = (double)pv;
            }

            foreach (var trim in ifcTrimmed.Trim2)
            {
                if (trim is IIfcCartesianPoint pt)
                    cp2 = pt;
                else if (trim is Xbim.Ifc4.MeasureResource.IfcParameterValue pv)
                    u2 = (double)pv;
            }

            // Resolve trim parameters
            if ((preferCartesian && cp1 != null && cp2 != null) ||
                (cp1 != null && cp2 != null &&
                 (double.IsNegativeInfinity(u1) || double.IsPositiveInfinity(u2))))
            {
                // Use Cartesian points projected onto the 2D basis curve
                double px1 = cp1.Coordinates[0];
                double py1 = cp1.Coordinates[1];
                double px2 = cp2.Coordinates[0];
                double py2 = cp2.Coordinates[1];

                int r1 = XbimGeometryNativeApi.xbim_curve2d_project_point(
                    ContextHandle, basisCurve.Handle,
                    px1, py1, _modelService.MinimumGap, out u1);
                if (r1 != 0)
                {
                    basisCurve.Dispose();
                    throw new InvalidOperationException(
                        $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Trim Point1 is not on the 2D basis curve.");
                }

                int r2 = XbimGeometryNativeApi.xbim_curve2d_project_point(
                    ContextHandle, basisCurve.Handle,
                    px2, py2, _modelService.MinimumGap, out u2);
                if (r2 != 0)
                {
                    basisCurve.Dispose();
                    throw new InvalidOperationException(
                        $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Trim Point2 is not on the 2D basis curve.");
                }
            }
            else if (double.IsNegativeInfinity(u1) || double.IsPositiveInfinity(u2))
            {
                basisCurve.Dispose();
                throw new InvalidOperationException(
                    $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: TrimValuesConsistent — " +
                    "either a single value is specified for Trim, or the two trimming values are of different type.");
            }
            else
            {
                // Use parametric values, adjusting for conics
                if (isConic)
                {
                    u1 *= _modelService.RadianFactor;
                    u2 *= _modelService.RadianFactor;
                }
            }

            // Sanity check
            if (double.IsNegativeInfinity(u1) || double.IsPositiveInfinity(u2))
            {
                basisCurve.Dispose();
                throw new InvalidOperationException(
                    $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: error converting trim points.");
            }

            // Handle equal parameters
            if (Math.Abs(u1 - u2) < _modelService.Precision)
            {
                if (isConic)
                {
                    // Equal params on a conic → build a full circle/ellipse
                    u1 = 0.0;
                    u2 = Math.PI * 2.0;
                    sense = true;
                }
                else
                {
                    basisCurve.Dispose();
                    _logger.LogInformation("IIfcTrimmedCurve #{Label}: parametric trim points are equal on non-conic — empty curve.",
                        ifcTrimmed.BasisCurve.EntityLabel);
                    throw new InvalidOperationException(
                        $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: trim parameters are equal on a non-conic basis, resulting in an empty curve.");
                }
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

            basisCurve.Dispose();

            if (trimResult != 0)
                throw new InvalidOperationException(
                    $"Failed to build 2D trimmed curve #{ifcTrimmed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve2d(trimHandle, XCurveType.IfcTrimmedCurve);
        }

        #endregion

        #region Polyline2d

        /// <summary>
        /// Builds a 2D polyline from an IFC polyline entity. Two-point polylines produce a single
        /// trimmed line segment. Multi-point polylines are built as individual trimmed line segments
        /// joined into a composite B-spline, with degenerate (zero-length) segments skipped.
        /// </summary>
        private Curve2d BuildPolyline2d(IIfcPolyline ifcPolyline)
        {
            var ifcPoints = ifcPolyline.Points;
            int pointCount = ifcPoints.Count;

            if (pointCount < 2)
                throw new InvalidOperationException(
                    $"IIfcPolyline #{ifcPolyline.EntityLabel} has fewer than 2 points.");

            double precision = _modelService.Precision;

            // Extract 2D coordinates
            var pts = new (double X, double Y)[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                var coords = ifcPoints[i].Coordinates;
                pts[i] = (coords[0], coords[1]);
            }

            if (pointCount == 2)
            {
                double dist = Math.Sqrt(
                    (pts[1].X - pts[0].X) * (pts[1].X - pts[0].X) +
                    (pts[1].Y - pts[0].Y) * (pts[1].Y - pts[0].Y));

                if (dist < precision)
                {
                    _logger.LogInformation(
                        "IIfcPolyline #{Label}: only 2 identical points — ignored.",
                        ifcPolyline.EntityLabel);
                    throw new InvalidOperationException(
                        $"IIfcPolyline #{ifcPolyline.EntityLabel} has only 2 identical points.");
                }

                int lineResult = XbimGeometryNativeApi.xbim_curve2d_build_line(
                    ContextHandle, pts[0].X, pts[0].Y, pts[1].X, pts[1].Y, out var lineHandle);

                if (lineResult != 0)
                    throw new InvalidOperationException(
                        $"Failed to build 2D polyline line segment #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve2d(lineHandle, XCurveType.IfcPolyline);
            }

            // 3+ points: build individual 2D lines, skip degenerate segments
            var segments = new List<NativeCurve2dHandle>();
            try
            {
                int lastIdx = 0;
                for (int i = 1; i < pointCount; i++)
                {
                    var start = pts[lastIdx];
                    var end = pts[i];

                    double dist = Math.Sqrt(
                        (end.X - start.X) * (end.X - start.X) +
                        (end.Y - start.Y) * (end.Y - start.Y));

                    if (dist < precision)
                        continue; // skip degenerate segment

                    int lineResult = XbimGeometryNativeApi.xbim_curve2d_build_line(
                        ContextHandle, start.X, start.Y, end.X, end.Y, out var lineHandle);

                    if (lineResult != 0)
                        throw new InvalidOperationException(
                            $"Failed to build 2D polyline segment #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    segments.Add(lineHandle);
                    lastIdx = i;
                }

                if (segments.Count == 0)
                    throw new InvalidOperationException(
                        $"IIfcPolyline #{ifcPolyline.EntityLabel} has no non-degenerate 2D segments.");

                if (segments.Count == 1)
                {
                    var singleHandle = segments[0];
                    segments.Clear();
                    return new Curve2d(singleHandle, XCurveType.IfcPolyline);
                }

                using var nativeSegments = new NativeHandleArray(
                    segments.Select(s => (SafeHandle)s).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    _modelService.MinimumGap,
                    out var compositeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build 2D polyline #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve2d(compositeHandle, XCurveType.IfcPolyline);
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
        private Curve2d BuildCompositeCurve2d(IIfcCompositeCurve ifcComposite)
        {
            var segmentCurves = new List<Curve2d>();

            try
            {
                int lastLabel = -1;
                foreach (var segment in ifcComposite.Segments)
                {
                    // ArchiCAD bug workaround: skip consecutive duplicate segments (same EntityLabel)
                    if (segment.EntityLabel == lastLabel)
                    {
                        _logger.LogInformation(
                            "IIfcCompositeCurve #{Label}: skipping duplicate segment #{SegLabel} (ArchiCAD bug).",
                            ifcComposite.EntityLabel, segment.EntityLabel);
                        continue;
                    }
                    lastLabel = segment.EntityLabel;

                    if (segment.ParentCurve == null)
                        continue;

                    var segCurve = (Curve2d)BuildCurve2d(segment.ParentCurve);

                    if (!segment.SameSense)
                    {
                        int reverseResult = XbimGeometryNativeApi.xbim_curve2d_reverse(segCurve.Handle);
                        if (reverseResult != 0)
                            throw new InvalidOperationException(
                                $"Failed to reverse 2D composite curve segment: {XbimGeometryNativeApi.GetLastError()}");
                    }

                    segmentCurves.Add(segCurve);
                }

                if (segmentCurves.Count == 0)
                    throw new InvalidOperationException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid 2D segments.");

                using var nativeSegments = new NativeHandleArray(
                    segmentCurves.Select(c => (SafeHandle)c.Handle).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    _modelService.MinimumGap,
                    out var compositeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build 2D composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve2d(compositeHandle, XCurveType.IfcCompositeCurve);
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
        private Curve2d BuildIndexedPolyCurve2d(IIfcIndexedPolyCurve ifcIndexed)
        {
            var points = ExtractPoints2d(ifcIndexed);
            var segments = new List<NativeCurve2dHandle>();

            try
            {
                if (ifcIndexed.Segments != null && ifcIndexed.Segments.Any())
                {
                    foreach (var segment in ifcIndexed.Segments)
                    {
                        if (segment is IfcArcIndex arcIndex)
                        {
                            var indices = (System.Collections.IList)arcIndex.Value;
                            if (indices.Count != 3)
                                throw new InvalidOperationException(
                                    $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: ArcIndex must have exactly 3 indices.");

                            int i1 = (int)(long)indices[0]! - 1;
                            int i2 = (int)(long)indices[1]! - 1;
                            int i3 = (int)(long)indices[2]! - 1;

                            var (sx, sy) = points[i1];
                            var (mx, my) = points[i2];
                            var (ex, ey) = points[i3];

                            // Native function handles collinear fallback internally (returns line segment)
                            int arcResult = XbimGeometryNativeApi.xbim_curve2d_build_arc_3pt(
                                ContextHandle, sx, sy, mx, my, ex, ey, out var arcHandle);

                            if (arcResult != 0)
                                throw new InvalidOperationException(
                                    $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to build 2D arc segment: {XbimGeometryNativeApi.GetLastError()}");

                            segments.Add(arcHandle);
                        }
                        else if (segment is IfcLineIndex lineIndex)
                        {
                            var indices = (System.Collections.IList)lineIndex.Value;
                            if (indices.Count < 2)
                                throw new InvalidOperationException(
                                    $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: LineIndex must have at least 2 indices.");

                            for (int p = 0; p < indices.Count - 1; p++)
                            {
                                int idx1 = (int)(long)indices[p]! - 1;
                                int idx2 = (int)(long)indices[p + 1]! - 1;

                                var (x1, y1) = points[idx1];
                                var (x2, y2) = points[idx2];

                                int lineResult = XbimGeometryNativeApi.xbim_curve2d_build_line(
                                    ContextHandle, x1, y1, x2, y2, out var lineHandle);

                                if (lineResult != 0)
                                    throw new InvalidOperationException(
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
                            throw new InvalidOperationException(
                                $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to build 2D sequential line segment: {XbimGeometryNativeApi.GetLastError()}");

                        segments.Add(lineHandle);
                    }
                }

                if (segments.Count == 0)
                    throw new InvalidOperationException(
                        $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel} has no valid 2D segments.");

                // Single segment — no need for composite joining
                if (segments.Count == 1)
                {
                    var singleHandle = segments[0];
                    segments.Clear(); // prevent dispose of the returned handle
                    return new Curve2d(singleHandle, XCurveType.IfcIndexedPolyCurve);
                }

                // Join all segments into a single B-spline
                using var nativeSegments = new NativeHandleArray(
                    segments.Select(s => (SafeHandle)s).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    _modelService.MinimumGap,
                    out var compositeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to join 2D IndexedPolyCurve segments #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve2d(compositeHandle, XCurveType.IfcIndexedPolyCurve);
            }
            finally
            {
                foreach (var s in segments)
                    s.Dispose();
            }
        }

        /// <summary>
        /// Extracts 2D point coordinates from an IIfcIndexedPolyCurve's point list.
        /// </summary>
        private static List<(double x, double y)> ExtractPoints2d(IIfcIndexedPolyCurve ifcIndexed)
        {
            var coordList = ifcIndexed.Points;

            if (coordList is IIfcCartesianPointList2D pointList2D)
            {
                var points = new List<(double, double)>(pointList2D.CoordList.Count);
                foreach (var coords in pointList2D.CoordList)
                    points.Add((coords[0], coords[1]));
                return points;
            }

            throw new NotSupportedException(
                $"2D IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel} requires IIfcCartesianPointList2D, " +
                $"but found {coordList?.GetType().Name ?? "null"}.");
        }

        #endregion
    }
}
