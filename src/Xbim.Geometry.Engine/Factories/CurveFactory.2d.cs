using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Primitives;
using Xbim.Geometry.Engine.Rules;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;

namespace Xbim.Geometry.Engine.Factories
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

            double ifcMag = ifcLine.Dir.Magnitude;
            return new XbimLine2d(nativeHandle,
                new XPoint(ox, oy),
                XVector.Create2d(dx, dy, ifcMag),
                ifcMag);
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

            double semi1 = ifcEllipse.SemiAxis1;
            double semi2 = ifcEllipse.SemiAxis2;
            bool swapped = semi1 < semi2;
            double majorR = swapped ? semi2 : semi1;
            double minorR = swapped ? semi1 : semi2;

            // When axes are swapped, the major axis direction is rotated 90° from refDir
            double posRefX = swapped ? -refDirY : refDirX;
            double posRefY = swapped ? refDirX : refDirY;

            var position = new XAxis2Placement2d(
                new XPoint(cx, cy),
                new XDirection(posRefX, posRefY));
            return new XbimEllipse2d(nativeHandle, majorR, minorR, position);
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
            CurveRules.WR41_TrimValuesNotEqual(ifcTrimmed);
            // WR42 (NoTrimOfBoundedCurves): many real-world files violate this — warn and continue
            if (ifcTrimmed.BasisCurve is IIfcBoundedCurve)
                _logger.LogWarning("IIfcTrimmedCurve #{Label} violates WR42 (NoTrimOfBoundedCurves): " +
                    "basis curve is already bounded. Processing continues.", ifcTrimmed.EntityLabel);

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
                        px1, py1, out u1);
                    if (r1 != 0)
                        throw new XbimGeometryServiceException(
                            $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Trim Point1 is not on the 2D basis curve.");

                    int r2 = XbimGeometryNativeApi.xbim_curve2d_project_point(
                        ContextHandle, basisCurve.Handle,
                        px2, py2, out u2);
                    if (r2 != 0)
                        throw new XbimGeometryServiceException(
                            $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Trim Point2 is not on the 2D basis curve.");

                    // Sanity check
                    if (double.IsNegativeInfinity(u1) || double.IsPositiveInfinity(u2))
                        throw new XbimGeometryServiceException(
                            $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: error converting trim points.");

                    HandleEqualTrimParams(ifcTrimmed, isConic, ref u1, ref u2, ref sense);
                }

                // When the basis ellipse has SemiAxis1 < SemiAxis2, the 2D native builder
                // rotates the reference direction by +PI/2 to satisfy OCCT's major >= minor
                // constraint. IFC parametric trim values must be shifted by -PI/2 to match
                // the rotated OCCT parameterization.
                if (!useCartesian && isEllipse &&
                    ifcTrimmed.BasisCurve is IIfcEllipse basisEllipse &&
                    basisEllipse.SemiAxis1 < basisEllipse.SemiAxis2)
                {
                    u1 -= Math.PI / 2.0;
                    u2 -= Math.PI / 2.0;
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
            // Fast path: if all segments are polylines/trimmed lines, build a single
            // degree-1 B-spline directly from the flattened 2D points.
            if (TryBuildAllPolylineComposite2d(ifcComposite, out var fastResult))
                return fastResult;

            // Batch path: marshal all segments into flat arrays and make one native call
            return BuildCompositeCurveBatch2d(ifcComposite);
        }

        /// <summary>
        /// Marshals all segments of a 2D composite curve into flat arrays and builds
        /// the composite B-spline via a single native call. Lines, trimmed circles, and
        /// polylines are marshalled as data; everything else falls back to a pre-built handle.
        /// </summary>
        private XbimBoundedCurve2d BuildCompositeCurveBatch2d(IIfcCompositeCurve ifcComposite)
        {
            CurveRules.Validate(ifcComposite);

            var types = new List<int>();
            var sameSense = new List<int>();
            var dataList = new List<double>();
            var offsets = new List<int>();
            var prebuiltHandles = new List<XbimCurve2d>();

            try
            {
                int lastLabel = -1;
                foreach (var segment in ifcComposite.Segments)
                {
                    if (segment.EntityLabel == lastLabel)
                    {
                        _logger.LogInformation(
                            "IIfcCompositeCurve #{Label}: skipping duplicate segment #{SegLabel} (ArchiCAD bug).",
                            ifcComposite.EntityLabel, segment.EntityLabel);
                        continue;
                    }
                    lastLabel = segment.EntityLabel;

                    if (segment is IIfcReparametrisedCompositeCurveSegment reparam
                        && (double)reparam.ParamLength != 1.0)
                        throw new XbimGeometryServiceException(
                            $"IIfcReparametrisedCompositeCurveSegment #{segment.EntityLabel} is currently unsupported (ParamLength != 1).");

                    if (segment.ParentCurve == null)
                        continue;

                    int sense = segment.SameSense ? 1 : 0;

                    try
                    {
                        MarshalSegment2d(segment.ParentCurve, sense,
                            types, sameSense, offsets, dataList, prebuiltHandles);
                    }
                    catch (IfcRuleViolationException ex)
                    {
                        _logger.LogWarning(
                            "IIfcCompositeCurve #{Label}: skipping segment #{SegLabel} ({Rule}).",
                            ifcComposite.EntityLabel, segment.EntityLabel, ex.Message);
                        continue;
                    }
                }
                offsets.Add(dataList.Count); // sentinel

                if (types.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments.");

                IntPtr[]? prebuiltPtrs = null;
                NativeHandleArray nativePrebuilt = default;
                try
                {
                    if (prebuiltHandles.Count > 0)
                    {
                        nativePrebuilt = new NativeHandleArray(
                            prebuiltHandles.Select(c => (SafeHandle)c.Handle).ToArray());
                        prebuiltPtrs = nativePrebuilt.Ptrs;
                    }

                    int result = XbimGeometryNativeApi.xbim_curve2d_build_composite(
                        ContextHandle,
                        types.Count,
                        types.ToArray(),
                        sameSense.ToArray(),
                        dataList.ToArray(),
                        offsets.ToArray(),
                        prebuiltPtrs,
                        prebuiltHandles.Count,
                        out var compositeHandle);

                    if (result != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return new XbimBoundedCurve2d(compositeHandle, XCurveType.IfcCompositeCurve);
                }
                finally
                {
                    if (prebuiltHandles.Count > 0)
                        nativePrebuilt.Dispose();
                }
            }
            finally
            {
                foreach (var c in prebuiltHandles)
                    c.Dispose();
            }
        }

        /// <summary>
        /// Classifies a 2D parent curve as LINE, CIRCLE_TRIM, POLYLINE, or HANDLE
        /// and appends the appropriate data to the flat arrays.
        /// </summary>
        private void MarshalSegment2d(
            IIfcCurve parentCurve, int sense,
            List<int> types, List<int> sameSenseList,
            List<int> offsets, List<double> data,
            List<XbimCurve2d> prebuiltHandles)
        {
            // Trimmed line → single LINE (4 doubles: sx, sy, ex, ey)
            if (parentCurve is IIfcTrimmedCurve trimmed && trimmed.BasisCurve is IIfcLine)
            {
                bool trimSense = trimmed.SenseAgreement;
                ExtractTrimParameters(trimmed, false,
                    out double u1, out double u2, ref trimSense,
                    out bool useCartesian, out var cp1, out var cp2);

                double sx, sy, ex, ey;
                bool canMarshal = true;
                if (useCartesian && cp1 != null && cp2 != null)
                {
                    var c1 = cp1.Coordinates;
                    var c2 = cp2.Coordinates;
                    sx = c1[0]; sy = c1.Count > 1 ? c1[1] : 0.0;
                    ex = c2[0]; ey = c2.Count > 1 ? c2[1] : 0.0;
                }
                else
                {
                    sx = sy = ex = ey = 0;
                    canMarshal = false;
                }

                if (canMarshal)
                {
                    if (!trimmed.SenseAgreement)
                    {
                        (sx, ex) = (ex, sx);
                        (sy, ey) = (ey, sy);
                    }

                    offsets.Add(data.Count);
                    sameSenseList.Add(sense);
                    types.Add(XbimGeometryNativeApi.CSegLine);
                    data.AddRange(new[] { sx, sy, ex, ey });
                    return;
                }
                // Fall through to HANDLE if can't extract points
            }

            // Polyline → single POLYLINE segment (N*2 doubles)
            if (parentCurve is IIfcPolyline polyline && polyline.Points.Count >= 2)
            {
                var (px, py) = ExtractPolylinePoints2d(polyline);
                offsets.Add(data.Count);
                sameSenseList.Add(sense);
                types.Add(XbimGeometryNativeApi.CSegPolyline);
                for (int i = 0; i < px.Length; i++)
                {
                    data.Add(px[i]);
                    data.Add(py[i]);
                }
                return;
            }

            // Trimmed circle → CIRCLE_TRIM (8 doubles)
            if (MarshalCircleArc2d(parentCurve, sense, types, sameSenseList, offsets, data))
                return;

            // Fallback: build individually via P/Invoke
            offsets.Add(data.Count);
            sameSenseList.Add(sense);
            types.Add(XbimGeometryNativeApi.CSegHandle);
            var curve = (XbimCurve2d)BuildCurve2d(parentCurve);
            prebuiltHandles.Add(curve);
        }

        /// <summary>
        /// Tries to marshal a trimmed circle arc as flat 2D placement + trim data.
        /// Returns true and appends 8 doubles on success:
        /// cx, cy, refDirX, refDirY, radius, u1, u2, senseAgreement.
        /// </summary>
        private bool MarshalCircleArc2d(
            IIfcCurve parentCurve, int segSense,
            List<int> types, List<int> sameSenseList,
            List<int> offsets, List<double> data)
        {
            if (!(parentCurve is IIfcTrimmedCurve trimmed && trimmed.BasisCurve is IIfcCircle circle))
                return false;

            if (circle.Radius <= 0)
                return false;

            if (circle.Position is not IIfcAxis2Placement2D axis2d)
                return false;

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

            bool trimSense = trimmed.SenseAgreement;
            ExtractTrimParameters(trimmed, true,
                out double u1, out double u2, ref trimSense,
                out bool useCartesian, out var cp1, out var cp2);

            if (useCartesian && cp1 != null && cp2 != null)
            {
                u1 = ProjectPointOnCircle2d(cp1, cx, cy, refDirX, refDirY);
                u2 = ProjectPointOnCircle2d(cp2, cx, cy, refDirX, refDirY);
                HandleEqualTrimParams(trimmed, true, ref u1, ref u2, ref trimSense);
            }

            offsets.Add(data.Count);
            sameSenseList.Add(segSense);
            types.Add(XbimGeometryNativeApi.CSegCircleTrim);
            data.AddRange(new double[]
            {
                cx, cy,
                refDirX, refDirY,
                (double)circle.Radius,
                u1, u2,
                trimSense ? 1.0 : 0.0
            });
            return true;
        }

        /// <summary>
        /// Projects a Cartesian point onto a 2D circle defined by center and reference direction,
        /// returning the OCCT parameter (angle in radians from the reference direction).
        /// </summary>
        private static double ProjectPointOnCircle2d(
            IIfcCartesianPoint cp,
            double cx, double cy,
            double refDirX, double refDirY)
        {
            var coords = cp.Coordinates;
            double px = coords[0];
            double py = coords.Count > 1 ? coords[1] : 0.0;

            // Vector from circle center to point
            double dx = px - cx;
            double dy = py - cy;

            // Project into circle's local frame (refDir = X axis, perpendicular = Y axis)
            // Y axis = (-refDirY, refDirX) for counterclockwise rotation
            double localX = dx * refDirX + dy * refDirY;
            double localY = -dx * refDirY + dy * refDirX;

            double angle = Math.Atan2(localY, localX);
            if (angle < 0) angle += 2.0 * Math.PI;
            return angle;
        }

        /// <summary>
        /// Checks whether all segments of a composite curve are polylines or trimmed lines.
        /// If so, flattens all 2D points and builds a single degree-1 B-spline directly.
        /// </summary>
        private bool TryBuildAllPolylineComposite2d(IIfcCompositeCurve ifcComposite, out XbimBoundedCurve2d result)
        {
            result = null!;

            // Collect all 2D points from all segments
            var allXY = new List<double>();
            int lastLabel = -1;

            foreach (var segment in ifcComposite.Segments)
            {
                if (segment.EntityLabel == lastLabel) continue;
                lastLabel = segment.EntityLabel;

                if (segment is IIfcReparametrisedCompositeCurveSegment reparam
                    && (double)reparam.ParamLength != 1.0)
                    return false; // unsupported segment type

                var parentCurve = segment.ParentCurve;
                if (parentCurve == null) continue;

                if (parentCurve is IIfcPolyline polyline && polyline.Points.Count >= 2)
                {
                    // Collect 2D points from polyline
                    var pts = new List<(double x, double y)>();
                    foreach (var pt in polyline.Points)
                    {
                        var coords = pt.Coordinates;
                        pts.Add((coords[0], coords.Count > 1 ? coords[1] : 0.0));
                    }

                    bool shouldReverse = !segment.SameSense;

                    // Connectivity check: compare forward vs reversed gap to last point
                    if (allXY.Count >= 2 && pts.Count >= 2)
                    {
                        double prevX = allXY[allXY.Count - 2];
                        double prevY = allXY[allXY.Count - 1];
                        var first = pts[0];
                        var last = pts[pts.Count - 1];
                        double gapFwd = (first.x - prevX) * (first.x - prevX) + (first.y - prevY) * (first.y - prevY);
                        double gapRev = (last.x - prevX) * (last.x - prevX) + (last.y - prevY) * (last.y - prevY);

                        if (shouldReverse && gapRev > gapFwd)
                            shouldReverse = false;
                        else if (!shouldReverse && gapFwd > gapRev)
                            shouldReverse = true;
                    }

                    if (shouldReverse)
                        pts.Reverse();

                    // Append, deduplicating at junction
                    foreach (var (x, y) in pts)
                    {
                        if (allXY.Count >= 2)
                        {
                            double prevX = allXY[allXY.Count - 2];
                            double prevY = allXY[allXY.Count - 1];
                            double dx = x - prevX, dy = y - prevY;
                            if (dx * dx + dy * dy < 1e-20) // ~Precision::Confusion²
                                continue;
                        }
                        allXY.Add(x);
                        allXY.Add(y);
                    }
                }
                else if (parentCurve is IIfcTrimmedCurve trimmed && trimmed.BasisCurve is IIfcLine)
                {
                    // Trimmed line: extract start and end points
                    bool trimSense = trimmed.SenseAgreement;
                    ExtractTrimParameters(trimmed, false,
                        out double u1, out double u2, ref trimSense,
                        out bool useCartesian, out var cp1, out var cp2);

                    double sx, sy, ex, ey;
                    if (useCartesian && cp1 != null && cp2 != null)
                    {
                        var coords1 = cp1.Coordinates;
                        var coords2 = cp2.Coordinates;
                        sx = coords1[0]; sy = coords1.Count > 1 ? coords1[1] : 0.0;
                        ex = coords2[0]; ey = coords2.Count > 1 ? coords2[1] : 0.0;
                    }
                    else
                    {
                        return false; // can't extract points — fall back to general path
                    }

                    if (!trimmed.SenseAgreement)
                    {
                        (sx, ex) = (ex, sx);
                        (sy, ey) = (ey, sy);
                    }

                    bool shouldReverseLine = !segment.SameSense;

                    // Connectivity check: compare forward vs reversed gap to last point
                    if (allXY.Count >= 2)
                    {
                        double prevX = allXY[allXY.Count - 2];
                        double prevY = allXY[allXY.Count - 1];
                        double gapFwd = (sx - prevX) * (sx - prevX) + (sy - prevY) * (sy - prevY);
                        double gapRev = (ex - prevX) * (ex - prevX) + (ey - prevY) * (ey - prevY);

                        if (shouldReverseLine && gapRev > gapFwd)
                            shouldReverseLine = false;
                        else if (!shouldReverseLine && gapFwd > gapRev)
                            shouldReverseLine = true;
                    }

                    if (shouldReverseLine)
                    {
                        (sx, ex) = (ex, sx);
                        (sy, ey) = (ey, sy);
                    }

                    // Add start point (dedup at junction)
                    if (allXY.Count >= 2)
                    {
                        double prevX = allXY[allXY.Count - 2];
                        double prevY = allXY[allXY.Count - 1];
                        double dx = sx - prevX, dy = sy - prevY;
                        if (dx * dx + dy * dy >= 1e-20)
                        {
                            allXY.Add(sx);
                            allXY.Add(sy);
                        }
                    }
                    else
                    {
                        allXY.Add(sx);
                        allXY.Add(sy);
                    }

                    // Add end point
                    allXY.Add(ex);
                    allXY.Add(ey);
                }
                else
                {
                    return false; // non-linear segment — fall back to general path
                }
            }

            int numPoints = allXY.Count / 2;
            if (numPoints < 2)
                return false;

            int nativeResult = XbimGeometryNativeApi.xbim_curve2d_build_polyline_bspline(
                ContextHandle, allXY.ToArray(), numPoints, out var curveHandle);

            if (nativeResult != 0)
                return false; // fall back to general path

            result = new XbimBoundedCurve2d(curveHandle, XCurveType.IfcCompositeCurve);
            return true;
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
