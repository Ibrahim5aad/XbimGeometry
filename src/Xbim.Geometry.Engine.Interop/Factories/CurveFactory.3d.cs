using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Rules;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    internal partial class CurveFactory
    {
        /// <summary>
        /// Routes an IFC curve entity to the appropriate 3D curve builder based on its type.
        /// </summary>
        private IXCurve BuildCurve3d(IIfcCurve curve)
        {
            if (curve is IIfcLine ifcLine)
                return BuildLine(ifcLine);

            if (curve is IIfcCircle ifcCircle)
                return BuildCircle(ifcCircle);

            if (curve is IIfcEllipse ifcEllipse)
                return BuildEllipse(ifcEllipse);

            if (curve is IIfcBSplineCurveWithKnots ifcBSpline)
                return BuildBSpline(ifcBSpline);

            if (curve is IIfcTrimmedCurve ifcTrimmed)
                return BuildTrimmedCurve3d(ifcTrimmed);

            if (curve is IIfcPolyline ifcPolyline)
                return BuildPolylineAsBSpline(ifcPolyline);

            if (curve is IIfcCompositeCurve ifcComposite)
                return BuildCompositeCurve(ifcComposite);

            if (curve is IIfcIndexedPolyCurve ifcIndexed)
                return BuildIndexedPolyCurve(ifcIndexed);

            if (curve is IIfcOffsetCurve3D ifcOffset3D)
                return BuildOffsetCurve3d(ifcOffset3D);

            if (curve is IIfcOffsetCurve2D ifcOffset2Dto3D)
                return BuildOffsetCurve2dAs3d(ifcOffset2Dto3D);

            throw new NotSupportedException(
                $"3D curve type {curve.ExpressType.ExpressName} #{curve.EntityLabel} is not yet supported.");
        }

        #region Line

        private XbimLine3d BuildLine(IIfcLine ifcLine)
        {
            var origin = GeometryFactory.BuildPoint3d(ifcLine.Pnt);
            if (!GeometryFactory.BuildDirection3d(ifcLine.Dir.Orientation,
                    out double dirX, out double dirY, out double dirZ))
                throw new XbimGeometryServiceException(
                    $"IIfcLine #{ifcLine.EntityLabel} has invalid direction.");

            int result = XbimGeometryNativeApi.xbim_curve_build_line_3d(
                ContextHandle,
                origin.X, origin.Y, origin.Z,
                dirX, dirY, dirZ,
                out var nativeCurveHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build line curve #{ifcLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            double ifcMag = ifcLine.Dir.Magnitude;
            return new XbimLine3d(nativeCurveHandle,
                new XPoint(origin.X, origin.Y, origin.Z),
                XVector.Create3d(dirX, dirY, dirZ, ifcMag),
                ifcMag);
        }

        #endregion

        #region Circle

        private XbimCircle3d BuildCircle(IIfcCircle ifcCircle)
        {
            if (ifcCircle.Radius <= 0)
                throw new XbimGeometryServiceException(
                    $"IIfcCircle #{ifcCircle.EntityLabel} has invalid radius {ifcCircle.Radius}. Radius must be greater than zero.");

            GeometryFactory.BuildAxis2PlacementAs3d(
                ifcCircle.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_curve_build_circle_3d(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                ifcCircle.Radius,
                out var nativeCurveHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build circle curve #{ifcCircle.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var position = new XAxis2Placement3d(
                new XPoint(ox, oy, oz),
                new XDirection(zx, zy, zz),
                new XDirection(xx, xy, xz));
            return new XbimCircle3d(nativeCurveHandle, ifcCircle.Radius, position);
        }

        #endregion

        #region Ellipse

        private XbimEllipse3d BuildEllipse(IIfcEllipse ifcEllipse)
        {
            GeometryFactory.BuildAxis2PlacementAs3d(
                ifcEllipse.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_curve_build_ellipse_3d(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                ifcEllipse.SemiAxis1, ifcEllipse.SemiAxis2,
                out int rotated,
                out var nativeCurveHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build ellipse curve #{ifcEllipse.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            double semi1 = ifcEllipse.SemiAxis1;
            double semi2 = ifcEllipse.SemiAxis2;
            bool swapped = rotated != 0;
            double majorR = swapped ? semi2 : semi1;
            double minorR = swapped ? semi1 : semi2;

            var position = new XAxis2Placement3d(
                new XPoint(ox, oy, oz),
                new XDirection(zx, zy, zz),
                new XDirection(xx, xy, xz));
            return new XbimEllipse3d(nativeCurveHandle, majorR, minorR, position);
        }

        #endregion

        #region BSpline

        private XbimBSplineCurve3d BuildBSpline(IIfcBSplineCurveWithKnots ifcBSpline)
        {
            // Extract control points
            var controlPoints = ifcBSpline.ControlPointsList;
            int numPoles = controlPoints.Count;
            var polesXYZ = new double[numPoles * 3];

            for (int i = 0; i < numPoles; i++)
            {
                var cp = controlPoints[i];
                polesXYZ[i * 3 + 0] = cp.Coordinates[0];
                polesXYZ[i * 3 + 1] = cp.Coordinates[1];
                polesXYZ[i * 3 + 2] = (int)cp.Dim == 3 ? (double)cp.Coordinates[2] : 0.0;
            }

            // Extract knots
            var knotValues = ifcBSpline.Knots.ToArray();
            int numKnots = knotValues.Length;
            var knots = new double[numKnots];
            for (int i = 0; i < numKnots; i++)
                knots[i] = knotValues[i];

            // Extract multiplicities
            var multValues = ifcBSpline.KnotMultiplicities.ToArray();
            var multiplicities = new int[multValues.Length];
            for (int i = 0; i < multValues.Length; i++)
                multiplicities[i] = (int)multValues[i];

            int degree = (int)ifcBSpline.Degree;

            // Extract weights for rational B-splines
            double[]? weights = null;
            if (ifcBSpline is IIfcRationalBSplineCurveWithKnots rational)
            {
                var weightValues = rational.WeightsData;
                weights = new double[weightValues.Count];
                for (int i = 0; i < weightValues.Count; i++)
                    weights[i] = weightValues[i];
            }

            int result = XbimGeometryNativeApi.xbim_curve_build_bspline(
                ContextHandle,
                polesXYZ, numPoles,
                knots, numKnots,
                multiplicities,
                degree,
                weights,
                out var nativeCurveHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build B-spline curve #{ifcBSpline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var curveType = ifcBSpline is IIfcRationalBSplineCurveWithKnots
                ? XCurveType.IfcRationalBSplineCurveWithKnots
                : XCurveType.IfcBSplineCurveWithKnots;

            bool isRational = ifcBSpline is IIfcRationalBSplineCurveWithKnots;
            return new XbimBSplineCurve3d(nativeCurveHandle, curveType,
                XGeometricContinuity.GeomAbs_CN, false, isRational);
        }

        #endregion

        #region Polyline

        /// <summary>
        /// Builds a polyline from an IFC polyline entity. Two-point polylines produce a single
        /// trimmed line segment. Multi-point polylines are built as individual trimmed line segments
        /// joined into a composite B-spline, with degenerate (zero-length) segments skipped.
        /// </summary>
        private XbimBoundedCurve3d BuildPolylineAsBSpline(IIfcPolyline ifcPolyline)
        {
            var (px, py, pz) = ExtractPolylinePoints3d(ifcPolyline);
            int pointCount = px.Length;

            if (pointCount < 2)
                throw new XbimGeometryServiceException(
                    $"IIfcPolyline #{ifcPolyline.EntityLabel} has fewer than 2 points.");

            double precision = _modelService.Precision;

            if (pointCount == 2)
            {
                double dist = Math.Sqrt(
                    (px[1] - px[0]) * (px[1] - px[0]) +
                    (py[1] - py[0]) * (py[1] - py[0]) +
                    (pz[1] - pz[0]) * (pz[1] - pz[0]));

                if (dist < precision)
                {
                    _logger.LogInformation(
                        "IIfcPolyline #{Label}: only 2 identical points — ignored.",
                        ifcPolyline.EntityLabel);
                    throw new XbimGeometryServiceException(
                        $"IIfcPolyline #{ifcPolyline.EntityLabel} has only 2 identical points.");
                }

                int lineResult = XbimGeometryNativeApi.xbim_curve_build_trimmed_line_3d(
                    ContextHandle, px[0], py[0], pz[0], px[1], py[1], pz[1], out var lineHandle);

                if (lineResult != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build polyline line segment #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimBoundedCurve3d(lineHandle, XCurveType.IfcPolyline);
            }

            // 3+ points: build individual trimmed lines, skip degenerate segments
            var segments = new List<NativeCurveHandle>();
            try
            {

                int lastIdx = 0;
                for (int i = 1; i < pointCount; i++)
                {
                    double dx = px[i] - px[lastIdx];
                    double dy = py[i] - py[lastIdx];
                    double dz = pz[i] - pz[lastIdx];
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                    if (dist < precision)
                        continue; // skip degenerate segment

                    int lineResult = XbimGeometryNativeApi.xbim_curve_build_trimmed_line_3d(
                        ContextHandle, px[lastIdx], py[lastIdx], pz[lastIdx], px[i], py[i], pz[i],
                        out var lineHandle);

                    if (lineResult != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build polyline segment #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    segments.Add(lineHandle);
                    lastIdx = i;
                }

                if (segments.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcPolyline #{ifcPolyline.EntityLabel} has no non-degenerate segments.");

                if (segments.Count == 1)
                {
                    var singleHandle = segments[0];
                    segments.Clear();
                    return new XbimBoundedCurve3d(singleHandle, XCurveType.IfcPolyline);
                }

                using var nativeSegments = new NativeHandleArray(
                    segments.Select(s => (SafeHandle)s).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    out var compositeHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build polyline #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimBoundedCurve3d(compositeHandle, XCurveType.IfcPolyline);
            }
            finally
            {
                foreach (var s in segments)
                    s.Dispose();
            }
        }

        #endregion

        #region TrimmedCurve

        /// <summary>
        /// Builds a trimmed curve from an IFC trimmed curve entity.
        /// Parses Trim1/Trim2 values (Cartesian and/or parametric), respects MasterRepresentation
        /// and SenseAgreement, and dispatches to the appropriate native trim builder.
        /// </summary>
        private XbimTrimmedCurve3d BuildTrimmedCurve3d(IIfcTrimmedCurve ifcTrimmed)
        {
            CurveRules.WR41_TrimValuesNotEqual(ifcTrimmed);
            // WR42 (NoTrimOfBoundedCurves): many real-world files violate this — warn and continue
            if (ifcTrimmed.BasisCurve is IIfcBoundedCurve)
                _logger.LogWarning("IIfcTrimmedCurve #{Label} violates WR42 (NoTrimOfBoundedCurves): " +
                    "basis curve is already bounded. Processing continues.", ifcTrimmed.EntityLabel);

            // Build the basis curve — ownership transfers to XbimTrimmedCurve3d on success
            var basisCurve = (XbimCurve)BuildCurve3d(ifcTrimmed.BasisCurve);
            try
            {
                bool isConic = ifcTrimmed.BasisCurve is IIfcConic;
                bool sense = ifcTrimmed.SenseAgreement;

                ExtractTrimParameters(ifcTrimmed, isConic,
                    out double u1, out double u2, ref sense,
                    out bool useCartesian, out var cp1, out var cp2);

                if (useCartesian)
                {
                    var p1 = GeometryFactory.BuildPoint3d(cp1!);
                    var p2 = GeometryFactory.BuildPoint3d(cp2!);

                    int r1 = XbimGeometryNativeApi.xbim_curve_project_point_3d(
                        ContextHandle, basisCurve.Handle,
                        p1.X, p1.Y, p1.Z, out u1);
                    if (r1 != 0)
                        throw new XbimGeometryServiceException(
                            $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Trim Point1 is not on the basis curve.");

                    int r2 = XbimGeometryNativeApi.xbim_curve_project_point_3d(
                        ContextHandle, basisCurve.Handle,
                        p2.X, p2.Y, p2.Z, out u2);
                    if (r2 != 0)
                        throw new XbimGeometryServiceException(
                            $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: Trim Point2 is not on the basis curve.");

                    // Sanity check
                    if (double.IsNegativeInfinity(u1) || double.IsPositiveInfinity(u2))
                        throw new XbimGeometryServiceException(
                            $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: error converting trim points.");

                    HandleEqualTrimParams(ifcTrimmed, isConic, ref u1, ref u2, ref sense);
                }

                // Build trimmed curve — convert IFC params only for parametric trim values,
                // not for Cartesian-projected values which are already in OCCT space
                int trimResult = XbimGeometryNativeApi.xbim_curve_build_trimmed_3d(
                    ContextHandle, basisCurve.Handle, u1, u2, sense ? 1 : 0,
                    useCartesian ? 0 : 1, out var trimHandle);

                if (trimResult != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build trimmed curve #{ifcTrimmed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimTrimmedCurve3d(trimHandle, basisCurve);
            }
            catch
            {
                basisCurve.Dispose();
                throw;
            }
        }

        #endregion

        #region Composite / Indexed

        private XbimBoundedCurve3d BuildCompositeCurve(IIfcCompositeCurve ifcComposite)
        {
            var segmentCurves = BuildCompositeCurveSegments3d(ifcComposite);
            try
            {
                if (segmentCurves.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments.");

                using var nativeSegments = new NativeHandleArray(
                    segmentCurves.Select(c => c.Handle).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    out var compositeHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimBoundedCurve3d(compositeHandle, XCurveType.IfcCompositeCurve);
            }
            finally
            {
                foreach (var c in segmentCurves)
                    c.Dispose();
            }
        }

        private XbimBoundedCurve3d BuildIndexedPolyCurve(IIfcIndexedPolyCurve ifcIndexed)
        {
            // Extract 3D points from the coordinate list (IFC indices are 1-based)
            var points = ExtractIndexedPoints3d(ifcIndexed);
            var segments = new List<NativeCurveHandle>();

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
                                throw new XbimGeometryServiceException(
                                    $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: ArcIndex must have exactly 3 indices.");

                            int i1 = (int)(IfcPositiveInteger)indices[0]! - 1;
                            int i2 = (int)(IfcPositiveInteger)indices[1]! - 1;
                            int i3 = (int)(IfcPositiveInteger)indices[2]! - 1;

                            var (sx, sy, sz) = points[i1];
                            var (mx, my, mz) = points[i2];
                            var (ex, ey, ez) = points[i3];

                            // Try to build a circle through 3 points
                            int circResult = XbimGeometryNativeApi.xbim_curve_build_circle_3pt_3d(
                                ContextHandle, sx, sy, sz, mx, my, mz, ex, ey, ez,
                                out var circleHandle);

                            if (circResult == 0)
                            {
                                // Project start and end points onto the circle to get trim parameters
                                int r1 = XbimGeometryNativeApi.xbim_curve_project_point_3d(
                                    ContextHandle, circleHandle, sx, sy, sz, out double u1);
                                int r2 = XbimGeometryNativeApi.xbim_curve_project_point_3d(
                                    ContextHandle, circleHandle, ex, ey, ez, out double u2);

                                if (r1 != 0 || r2 != 0)
                                {
                                    circleHandle.Dispose();
                                    throw new XbimGeometryServiceException(
                                        $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to project arc endpoints onto circle.");
                                }

                                int trimResult = XbimGeometryNativeApi.xbim_curve_build_trimmed_3d(
                                    ContextHandle, circleHandle, u1, u2, 1,
                                    0, // params from point projection, already OCCT space
                                    out var arcHandle);

                                circleHandle.Dispose();

                                if (trimResult != 0)
                                    throw new XbimGeometryServiceException(
                                        $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to trim arc segment: {XbimGeometryNativeApi.GetLastError()}");

                                segments.Add(arcHandle);
                            }
                            else
                            {
                                // Collinear points — fallback to a line from start to end
                                _logger.LogInformation(
                                    "IIfcIndexedPolyCurve #{Label}: ArcIndex handled as LineIndex (collinear points).",
                                    ifcIndexed.EntityLabel);

                                int lineResult = XbimGeometryNativeApi.xbim_curve_build_trimmed_line_3d(
                                    ContextHandle, sx, sy, sz, ex, ey, ez, out var lineHandle);

                                if (lineResult != 0)
                                    throw new XbimGeometryServiceException(
                                        $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to build fallback line for ArcIndex: {XbimGeometryNativeApi.GetLastError()}");

                                segments.Add(lineHandle);
                            }
                        }
                        else if (segment is IfcLineIndex lineIndex)
                        {
                            var indices = (System.Collections.IList)lineIndex.Value;
                            if (indices.Count < 2)
                                throw new XbimGeometryServiceException(
                                    $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: LineIndex must have at least 2 indices.");

                            for (int p = 0; p < indices.Count - 1; p++)
                            {
                                int idx1 = (int)(IfcPositiveInteger)indices[p]! - 1;
                                int idx2 = (int)(IfcPositiveInteger)indices[p + 1]! - 1;

                                var (x1, y1, z1) = points[idx1];
                                var (x2, y2, z2) = points[idx2];

                                int lineResult = XbimGeometryNativeApi.xbim_curve_build_trimmed_line_3d(
                                    ContextHandle, x1, y1, z1, x2, y2, z2, out var lineHandle);

                                if (lineResult != 0)
                                    throw new XbimGeometryServiceException(
                                        $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to build line segment: {XbimGeometryNativeApi.GetLastError()}");

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
                        var (x1, y1, z1) = points[p];
                        var (x2, y2, z2) = points[p + 1];

                        int lineResult = XbimGeometryNativeApi.xbim_curve_build_trimmed_line_3d(
                            ContextHandle, x1, y1, z1, x2, y2, z2, out var lineHandle);

                        if (lineResult != 0)
                            throw new XbimGeometryServiceException(
                                $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}: failed to build sequential line segment: {XbimGeometryNativeApi.GetLastError()}");

                        segments.Add(lineHandle);
                    }
                }

                if (segments.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel} has no valid segments.");

                // Single segment — no need for composite joining
                if (segments.Count == 1)
                {
                    var singleHandle = segments[0];
                    segments.Clear(); // prevent dispose of the returned handle
                    return new XbimBoundedCurve3d(singleHandle, XCurveType.IfcIndexedPolyCurve);
                }

                // Join all segments into a single B-spline via CompCurveToBSplineCurve
                using var nativeSegments = new NativeHandleArray(
                    segments.Select(s => (SafeHandle)s).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    out var compositeHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to join IndexedPolyCurve segments #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimBoundedCurve3d(compositeHandle, XCurveType.IfcIndexedPolyCurve);
            }
            finally
            {
                foreach (var s in segments)
                    s.Dispose();
            }
        }

        #endregion

        #region OffsetCurve

        /// <summary>
        /// Builds a 3D offset curve from an IFC offset curve 3D entity.
        /// Offsets the basis curve by the specified distance in the direction
        /// defined by the reference direction vector.
        /// </summary>
        private XbimCurve BuildOffsetCurve3d(IIfcOffsetCurve3D ifcOffset)
        {
            using var basisCurve = (XbimCurve)BuildCurve3d(ifcOffset.BasisCurve);

            if (!GeometryFactory.BuildDirection3d(ifcOffset.RefDirection,
                    out double refDirX, out double refDirY, out double refDirZ))
                throw new XbimGeometryServiceException(
                    $"IIfcOffsetCurve3D #{ifcOffset.EntityLabel}: RefDirection is invalid.");

            int result = XbimGeometryNativeApi.xbim_curve_build_offset_3d(
                ContextHandle, basisCurve.Handle,
                ifcOffset.Distance,
                refDirX, refDirY, refDirZ,
                out var offsetHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build 3D offset curve #{ifcOffset.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimCurve(offsetHandle, XCurveType.IfcOffsetCurve3D);
        }

        /// <summary>
        /// Builds a 2D offset curve as a 3D curve in the XY plane.
        /// Uses gp::DZ() (0,0,1) as the reference direction since the curve lies in 2D.
        /// </summary>
        private XbimCurve BuildOffsetCurve2dAs3d(IIfcOffsetCurve2D ifcOffset)
        {
            using var basisCurve = (XbimCurve)BuildCurve3d(ifcOffset.BasisCurve);

            int result = XbimGeometryNativeApi.xbim_curve_build_offset_3d(
                ContextHandle, basisCurve.Handle,
                ifcOffset.Distance,
                0.0, 0.0, 1.0,
                out var offsetHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build 2D-as-3D offset curve #{ifcOffset.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimCurve(offsetHandle, XCurveType.IfcOffsetCurve2D);
        }

        #endregion
    }
}
