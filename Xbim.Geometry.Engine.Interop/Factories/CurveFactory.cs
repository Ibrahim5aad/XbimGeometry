using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds 3D curve geometry from IFC curve entities. Handles lines, circles,
    /// ellipses, B-spline curves, and directrix extraction for sweep operations.
    /// </summary>
    internal class CurveFactory : IXCurveFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public CurveFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXCurve Build(IIfcCurve curve)
        {
            if (curve is IfcGradientCurve ifcGradient)
                return BuildGradientCurve(ifcGradient);

            if (curve is IIfcLine ifcLine)
                return BuildLine(ifcLine);

            if (curve is IIfcCircle ifcCircle)
                return BuildCircle(ifcCircle);

            if (curve is IIfcEllipse ifcEllipse)
                return BuildEllipse(ifcEllipse);

            if (curve is IIfcBSplineCurveWithKnots ifcBSpline)
                return BuildBSpline(ifcBSpline);

            if (curve is IIfcTrimmedCurve ifcTrimmed)
                return Build(ifcTrimmed.BasisCurve);

            if (curve is IIfcPolyline ifcPolyline)
                return BuildPolylineAsBSpline(ifcPolyline);

            if (curve is IIfcCompositeCurve ifcComposite)
                return BuildCompositeCurve(ifcComposite);

            if (curve is IIfcIndexedPolyCurve ifcIndexed)
                return BuildIndexedPolyCurve(ifcIndexed);

            throw new NotSupportedException(
                $"Curve type {curve.ExpressType.ExpressName} #{curve.EntityLabel} is not yet supported.");
        }

        public IXCurve BuildDirectrix(IIfcCurve curve, double? startParam, double? endParam)
        {
            // Build the full curve, trimming will be handled at the sweep level
            return Build(curve);
        }

        public IXCurve BuildSpiral(IfcSpiral curve, double startParam, double endParam)
        {
            ExtractSpiralPlacement2d(curve.Position,
                out double placementX, out double placementY,
                out double dirX, out double dirY);

            if (curve is IfcClothoid clothoid)
                return BuildClothoid(clothoid, startParam, endParam,
                    placementX, placementY, dirX, dirY);

            if (curve is IfcSineSpiral sineSpiral)
                return BuildSineSpiral(sineSpiral, startParam, endParam,
                    placementX, placementY, dirX, dirY);

            if (curve is IfcCosineSpiral cosineSpiral)
                return BuildCosineSpiral(cosineSpiral, startParam, endParam,
                    placementX, placementY, dirX, dirY);

            if (curve is IfcSecondOrderPolynomialSpiral secondOrder)
                return BuildPolynomialSpiral(secondOrder, startParam, endParam,
                    placementX, placementY, dirX, dirY);

            if (curve is IfcThirdOrderPolynomialSpiral thirdOrder)
                return BuildPolynomialSpiral(thirdOrder, startParam, endParam,
                    placementX, placementY, dirX, dirY);

            if (curve is IfcSeventhOrderPolynomialSpiral seventhOrder)
                return BuildPolynomialSpiral(seventhOrder, startParam, endParam,
                    placementX, placementY, dirX, dirY);

            throw new NotSupportedException(
                $"Spiral type {curve.GetType().Name} #{curve.EntityLabel} is not supported.");
        }

        public IXCurve BuildPolynomialCurve2d(IfcPolynomialCurve curve, double startParam, double endParam)
        {
            if (curve.CoefficientsX == null || curve.CoefficientsY == null)
                throw new InvalidOperationException(
                    $"IfcPolynomialCurve #{curve.EntityLabel}: CoefficientsX and CoefficientsY must be defined.");

            // Extract placement (IfcPolynomialCurve.Position is IfcPlacement, not IfcAxis2Placement)
            double placementX = 0.0, placementY = 0.0, dirX = 1.0, dirY = 0.0;
            if (curve.Position is IIfcAxis2Placement2D axis2d)
            {
                placementX = axis2d.Location.Coordinates[0];
                placementY = axis2d.Location.Coordinates[1];

                if (axis2d.RefDirection != null)
                {
                    double rdx = axis2d.RefDirection.DirectionRatios[0];
                    double rdy = axis2d.RefDirection.DirectionRatios[1];
                    double mag = Math.Sqrt(rdx * rdx + rdy * rdy);
                    if (mag > 1e-15) { dirX = rdx / mag; dirY = rdy / mag; }
                }
            }

            // Extract coefficient arrays
            var coeffsX = curve.CoefficientsX.Select(c => (double)c.Value).ToArray();
            var coeffsY = curve.CoefficientsY.Select(c => (double)c.Value).ToArray();

            int result = XbimGeometryNativeApi.xbim_curve_build_polynomial(
                ContextHandle,
                coeffsX, coeffsX.Length,
                coeffsY, coeffsY.Length,
                placementX, placementY,
                dirX, dirY,
                startParam, endParam,
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build polynomial curve #{curve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(curveHandle, XCurveType.IfcPolynomialCurve);
        }

        #region Line

        private Curve BuildLine(IIfcLine ifcLine)
        {
            var origin = GeometryFactory.BuildPoint3d(ifcLine.Pnt);
            if (!GeometryFactory.BuildDirection3d(ifcLine.Dir.Orientation,
                    out double dirX, out double dirY, out double dirZ))
                throw new InvalidOperationException(
                    $"IIfcLine #{ifcLine.EntityLabel} has invalid direction.");

            int result = XbimGeometryNativeApi.xbim_curve_build_line_3d(
                ContextHandle,
                origin.X, origin.Y, origin.Z,
                dirX, dirY, dirZ,
                out var NativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build line curve #{ifcLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(NativeCurveHandle, XCurveType.IfcLine);
        }

        #endregion

        #region Circle

        private Curve BuildCircle(IIfcCircle ifcCircle)
        {
            GeometryFactory.BuildAxis2PlacementAs3d(
                ifcCircle.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out _, out _, out _);

            int result = XbimGeometryNativeApi.xbim_curve_build_circle_3d(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                ifcCircle.Radius,
                out var NativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build circle curve #{ifcCircle.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(NativeCurveHandle, XCurveType.IfcCircle);
        }

        #endregion

        #region Ellipse

        private Curve BuildEllipse(IIfcEllipse ifcEllipse)
        {
            GeometryFactory.BuildAxis2PlacementAs3d(
                ifcEllipse.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out _, out _, out _);

            int result = XbimGeometryNativeApi.xbim_curve_build_ellipse_3d(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                ifcEllipse.SemiAxis1, ifcEllipse.SemiAxis2,
                out var NativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build ellipse curve #{ifcEllipse.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(NativeCurveHandle, XCurveType.IfcEllipse);
        }

        #endregion

        #region BSpline

        private Curve BuildBSpline(IIfcBSplineCurveWithKnots ifcBSpline)
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
                out var NativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build B-spline curve #{ifcBSpline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var curveType = ifcBSpline is IIfcRationalBSplineCurveWithKnots
                ? XCurveType.IfcRationalBSplineCurveWithKnots
                : XCurveType.IfcBSplineCurveWithKnots;

            return new Curve(NativeCurveHandle, curveType);
        }

        #endregion

        #region Polyline

        private Curve BuildPolylineAsBSpline(IIfcPolyline ifcPolyline)
        {
            // Build polyline as a degree-1 B-spline (piecewise linear)
            var points = ifcPolyline.Points;
            int numPoles = points.Count;

            if (numPoles < 2)
                throw new InvalidOperationException(
                    $"IIfcPolyline #{ifcPolyline.EntityLabel} has fewer than 2 points.");

            var polesXYZ = new double[numPoles * 3];
            for (int i = 0; i < numPoles; i++)
            {
                var cp = points[i];
                polesXYZ[i * 3 + 0] = cp.Coordinates[0];
                polesXYZ[i * 3 + 1] = cp.Coordinates[1];
                polesXYZ[i * 3 + 2] = (int)cp.Dim == 3 ? (double)cp.Coordinates[2] : 0.0;
            }

            // Degree 1 (linear segments), knots = {0, 1} with multiplicities {numPoles, numPoles}...
            // Actually for a polyline with N points, build as line segments.
            // Use the native line builder for the first segment as a simple approximation,
            // or build as a bspline of degree 1.
            var knots = new double[numPoles];
            var multiplicities = new int[numPoles];
            for (int i = 0; i < numPoles; i++)
            {
                knots[i] = (double)i / (numPoles - 1);
                multiplicities[i] = 1;
            }
            multiplicities[0] = 2; // clamp start
            multiplicities[numPoles - 1] = 2; // clamp end

            int result = XbimGeometryNativeApi.xbim_curve_build_bspline(
                ContextHandle,
                polesXYZ, numPoles,
                knots, numPoles,
                multiplicities,
                1, // degree 1
                null,
                out var NativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build polyline as B-spline #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(NativeCurveHandle, XCurveType.IfcPolyline);
        }

        #endregion

        #region Composite / Indexed

        private Curve BuildCompositeCurve(IIfcCompositeCurve ifcComposite)
        {
            // Build each segment, then compose.
            // For now, build the first valid segment as a simplification.
            // Full composite curve support requires wire-level composition.
            foreach (var segment in ifcComposite.Segments)
            {
                if (segment.ParentCurve != null)
                    return (Curve)Build(segment.ParentCurve);
            }

            throw new InvalidOperationException(
                $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments.");
        }

        private Curve BuildIndexedPolyCurve(IIfcIndexedPolyCurve ifcIndexed)
        {
            // Extract points from the coordinate list
            var coordList = ifcIndexed.Points;
            if (coordList is IIfcCartesianPointList3D pointList3D)
            {
                var coordData = pointList3D.CoordList;
                int numPoints = coordData.Count;
                var polesXYZ = new double[numPoints * 3];

                for (int i = 0; i < numPoints; i++)
                {
                    var coords = coordData[i];
                    polesXYZ[i * 3 + 0] = coords[0];
                    polesXYZ[i * 3 + 1] = coords[1];
                    polesXYZ[i * 3 + 2] = coords.Count > 2 ? coords[2] : 0.0;
                }

                // Build as degree-1 B-spline
                var knots = new double[numPoints];
                var multiplicities = new int[numPoints];
                for (int i = 0; i < numPoints; i++)
                {
                    knots[i] = (double)i / (numPoints - 1);
                    multiplicities[i] = 1;
                }
                multiplicities[0] = 2;
                multiplicities[numPoints - 1] = 2;

                int result = XbimGeometryNativeApi.xbim_curve_build_bspline(
                    ContextHandle,
                    polesXYZ, numPoints,
                    knots, numPoints,
                    multiplicities,
                    1,
                    null,
                    out var NativeCurveHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve(NativeCurveHandle, XCurveType.IfcIndexedPolyCurve);
            }

            if (coordList is IIfcCartesianPointList2D pointList2D)
            {
                var coordData = pointList2D.CoordList;
                int numPoints = coordData.Count;
                var polesXYZ = new double[numPoints * 3];

                for (int i = 0; i < numPoints; i++)
                {
                    var coords = coordData[i];
                    polesXYZ[i * 3 + 0] = coords[0];
                    polesXYZ[i * 3 + 1] = coords[1];
                    polesXYZ[i * 3 + 2] = 0.0;
                }

                var knots = new double[numPoints];
                var multiplicities = new int[numPoints];
                for (int i = 0; i < numPoints; i++)
                {
                    knots[i] = (double)i / (numPoints - 1);
                    multiplicities[i] = 1;
                }
                multiplicities[0] = 2;
                multiplicities[numPoints - 1] = 2;

                int result = XbimGeometryNativeApi.xbim_curve_build_bspline(
                    ContextHandle,
                    polesXYZ, numPoints,
                    knots, numPoints,
                    multiplicities,
                    1,
                    null,
                    out var NativeCurveHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build indexed poly curve 2D #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve(NativeCurveHandle, XCurveType.IfcIndexedPolyCurve);
            }

            throw new NotSupportedException(
                $"Unsupported point list type in IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}.");
        }

        #endregion

        #region Gradient Curve

        private Curve BuildGradientCurve(IfcGradientCurve ifcGradient)
        {
            // Step 1: Build the horizontal projection (BaseCurve) as a 2D composite curve
            var horizontalHandle = BuildBaseCurve2d(ifcGradient);

            // Step 2: Build height function from gradient segments
            var heightSegmentHandles = new List<NativeCurve2dHandle>();

            try
            {
                foreach (var segment in ifcGradient.Segments)
                {
                    if (segment is not IfcCurveSegment curveSegment)
                        continue;

                    var segHandle = BuildCurveSegment2d(curveSegment);
                    if (segHandle == null || segHandle.IsInvalid)
                        continue;

                    // Transform the segment with its placement
                    if (curveSegment.Placement is IIfcAxis2Placement2D axis2d)
                    {
                        double px = axis2d.Location.Coordinates[0];
                        double py = axis2d.Location.Coordinates[1];
                        double dx = 1.0, dy = 0.0;
                        if (axis2d.RefDirection != null)
                        {
                            dx = axis2d.RefDirection.DirectionRatios[0];
                            dy = axis2d.RefDirection.DirectionRatios[1];
                            double mag = Math.Sqrt(dx * dx + dy * dy);
                            if (mag > 1e-15) { dx /= mag; dy /= mag; }
                        }

                        XbimGeometryNativeApi.xbim_curve2d_transform(segHandle, px, py, dx, dy);
                    }

                    heightSegmentHandles.Add(segHandle);
                }

                if (heightSegmentHandles.Count == 0)
                    throw new InvalidOperationException(
                        $"IfcGradientCurve #{ifcGradient.EntityLabel}: no valid height function segments.");

                // Step 3: Join height segments into composite 2D B-spline
                var segPtrs = heightSegmentHandles.Select(h => h.DangerousGetHandle()).ToArray();
                int compositeResult = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                    ContextHandle, segPtrs, segPtrs.Length,
                    _modelService.MinimumGap,
                    out var heightFunctionHandle);

                if (compositeResult != 0)
                    throw new InvalidOperationException(
                        $"IfcGradientCurve #{ifcGradient.EntityLabel}: failed to build height function: {XbimGeometryNativeApi.GetLastError()}");

                // Step 4: Align the height function's X origin with horizontal projection
                // The height function's X axis represents distance-along; align it with
                // the horizontal projection's start distance
                AlignHeightFunction(horizontalHandle, heightFunctionHandle);

                // Step 5: Handle EndPoint (optional trailing segment)
                if (ifcGradient.EndPoint is IIfcAxis2Placement2D endPt)
                    AppendEndPointSegment(heightFunctionHandle, endPt);

                // Step 6: Create gradient curve from horizontal + height
                int gradientResult = XbimGeometryNativeApi.xbim_curve_build_gradient(
                    ContextHandle, horizontalHandle, heightFunctionHandle,
                    out var curveHandle);

                if (gradientResult != 0)
                    throw new InvalidOperationException(
                        $"IfcGradientCurve #{ifcGradient.EntityLabel}: failed to build gradient curve: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve(curveHandle, XCurveType.IfcGradientCurve);
            }
            finally
            {
                // Keep segment handles alive until composite is built
                // (DangerousGetHandle pattern — handles must not be GC'd while native holds pointers)
                GC.KeepAlive(heightSegmentHandles);
            }
        }

        private NativeCurve2dHandle BuildBaseCurve2d(IfcGradientCurve ifcGradient)
        {
            var baseCurve = ifcGradient.BaseCurve;

            // If BaseCurve is a composite curve (typical), build each segment as 2D and join
            if (baseCurve is IfcCompositeCurve composite)
            {
                var segHandles = new List<NativeCurve2dHandle>();
                try
                {
                    foreach (var segment in composite.Segments)
                    {
                        if (segment is not IfcCurveSegment curveSegment)
                            continue;

                        var segHandle = BuildCurveSegment2d(curveSegment);
                        if (segHandle == null || segHandle.IsInvalid)
                            continue;

                        if (curveSegment.Placement is IIfcAxis2Placement2D axis2d)
                        {
                            double px = axis2d.Location.Coordinates[0];
                            double py = axis2d.Location.Coordinates[1];
                            double dx = 1.0, dy = 0.0;
                            if (axis2d.RefDirection != null)
                            {
                                dx = axis2d.RefDirection.DirectionRatios[0];
                                dy = axis2d.RefDirection.DirectionRatios[1];
                                double mag = Math.Sqrt(dx * dx + dy * dy);
                                if (mag > 1e-15) { dx /= mag; dy /= mag; }
                            }

                            XbimGeometryNativeApi.xbim_curve2d_transform(segHandle, px, py, dx, dy);
                        }

                        segHandles.Add(segHandle);
                    }

                    if (segHandles.Count == 0)
                        throw new InvalidOperationException(
                            $"IfcGradientCurve #{ifcGradient.EntityLabel}: BaseCurve has no valid segments.");

                    var segPtrs = segHandles.Select(h => h.DangerousGetHandle()).ToArray();
                    int result = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                        ContextHandle, segPtrs, segPtrs.Length,
                        _modelService.MinimumGap,
                        out var compositeHandle);

                    if (result != 0)
                        throw new InvalidOperationException(
                            $"IfcGradientCurve #{ifcGradient.EntityLabel}: failed to build BaseCurve composite: {XbimGeometryNativeApi.GetLastError()}");

                    return compositeHandle;
                }
                finally
                {
                    GC.KeepAlive(segHandles);
                }
            }

            // For a simple line BaseCurve, build directly as 2D
            if (baseCurve is IIfcLine line)
            {
                var origin = line.Pnt;
                double ox = origin.Coordinates[0];
                double oy = origin.Coordinates[1];

                double vx = line.Dir.Orientation.DirectionRatios[0];
                double vy = line.Dir.Orientation.DirectionRatios[1];
                double mag = Math.Sqrt(vx * vx + vy * vy);
                if (mag > 1e-15) { vx /= mag; vy /= mag; }

                // Build as a long line segment (use a large length)
                double length = line.Dir.Magnitude * 10000;
                int result = XbimGeometryNativeApi.xbim_curve2d_build_line(
                    ContextHandle,
                    ox, oy,
                    ox + vx * length, oy + vy * length,
                    out var lineHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"IfcGradientCurve #{ifcGradient.EntityLabel}: failed to build BaseCurve line: {XbimGeometryNativeApi.GetLastError()}");

                return lineHandle;
            }

            throw new NotSupportedException(
                $"IfcGradientCurve #{ifcGradient.EntityLabel}: unsupported BaseCurve type {baseCurve.GetType().Name}.");
        }

        private NativeCurve2dHandle? BuildCurveSegment2d(IfcCurveSegment segment)
        {
            double startParam = (double)segment.SegmentStart.Value;
            double length = (double)segment.SegmentLength.Value;
            double endParam = startParam + length;

            if (Math.Abs(length) < 1e-15)
                return null;

            var parentCurve = segment.ParentCurve;

            // Spirals: build 2D native spiral and move to origin
            if (parentCurve is IfcSpiral spiral)
            {
                var handle = BuildSpiral2d(spiral, startParam, endParam);
                if (handle != null && !handle.IsInvalid)
                    XbimGeometryNativeApi.xbim_curve2d_move_to_origin(handle);
                return handle;
            }

            // Polynomial curves: build 2D native polynomial and move to origin
            if (parentCurve is IfcPolynomialCurve polyCurve)
            {
                var handle = BuildPolynomialCurve2dSegment(polyCurve, startParam, endParam);
                if (handle != null && !handle.IsInvalid)
                    XbimGeometryNativeApi.xbim_curve2d_move_to_origin(handle);
                return handle;
            }

            // Lines: build 2D line segment, trim, and move to origin
            if (parentCurve is IIfcLine line)
            {
                return BuildLineCurveSegment2d(line, startParam, endParam, length);
            }

            // Circles: build 2D arc
            if (parentCurve is IIfcCircle circle)
            {
                return BuildCircleCurveSegment2d(circle, startParam, endParam, length);
            }

            _logger.LogWarning("Unsupported curve segment parent type {Type} for #{Label}",
                parentCurve.GetType().Name, segment.EntityLabel);
            return null;
        }

        private NativeCurve2dHandle? BuildSpiral2d(IfcSpiral spiral, double startParam, double endParam)
        {
            ExtractSpiralPlacement2d(spiral.Position,
                out double px, out double py, out double dx, out double dy);

            NativeCurve2dHandle? handle = null;
            int result;

            if (spiral is IfcClothoid clothoid)
            {
                result = XbimGeometryNativeApi.xbim_curve2d_build_clothoid(
                    ContextHandle, (double)clothoid.ClothoidConstant,
                    startParam, endParam, px, py, dx, dy,
                    out handle);
            }
            else if (spiral is IfcSineSpiral sine)
            {
                result = XbimGeometryNativeApi.xbim_curve2d_build_sine_spiral(
                    ContextHandle,
                    (double)sine.SineTerm,
                    sine.LinearTerm.HasValue ? (double)sine.LinearTerm.Value : 0.0,
                    sine.ConstantTerm.HasValue ? (double)sine.ConstantTerm.Value : 0.0,
                    startParam, endParam, px, py, dx, dy,
                    out handle);
            }
            else if (spiral is IfcCosineSpiral cosine)
            {
                result = XbimGeometryNativeApi.xbim_curve2d_build_cosine_spiral(
                    ContextHandle,
                    (double)cosine.CosineTerm,
                    cosine.ConstantTerm.HasValue ? (double)cosine.ConstantTerm.Value : 0.0,
                    startParam, endParam, px, py, dx, dy,
                    out handle);
            }
            else if (spiral is IfcSecondOrderPolynomialSpiral sec)
            {
                var c = new double[8]; var p = new int[8];
                if (sec.ConstantTerm.HasValue) { c[0] = (double)sec.ConstantTerm.Value; p[0] = 1; }
                if (sec.LinearTerm.HasValue) { c[1] = (double)sec.LinearTerm.Value; p[1] = 1; }
                c[2] = (double)sec.QuadraticTerm; p[2] = 1;
                result = XbimGeometryNativeApi.xbim_curve2d_build_polynomial_spiral(
                    ContextHandle, c, p, 8, startParam, endParam, px, py, dx, dy, out handle);
            }
            else if (spiral is IfcThirdOrderPolynomialSpiral third)
            {
                var c = new double[8]; var p = new int[8];
                if (third.ConstantTerm.HasValue) { c[0] = (double)third.ConstantTerm.Value; p[0] = 1; }
                if (third.LinearTerm.HasValue) { c[1] = (double)third.LinearTerm.Value; p[1] = 1; }
                if (third.QuadraticTerm.HasValue) { c[2] = (double)third.QuadraticTerm.Value; p[2] = 1; }
                c[3] = (double)third.CubicTerm; p[3] = 1;
                result = XbimGeometryNativeApi.xbim_curve2d_build_polynomial_spiral(
                    ContextHandle, c, p, 8, startParam, endParam, px, py, dx, dy, out handle);
            }
            else if (spiral is IfcSeventhOrderPolynomialSpiral sev)
            {
                var c = new double[8]; var p = new int[8];
                if (sev.ConstantTerm.HasValue) { c[0] = (double)sev.ConstantTerm.Value; p[0] = 1; }
                if (sev.LinearTerm.HasValue) { c[1] = (double)sev.LinearTerm.Value; p[1] = 1; }
                if (sev.QuadraticTerm.HasValue) { c[2] = (double)sev.QuadraticTerm.Value; p[2] = 1; }
                if (sev.CubicTerm.HasValue) { c[3] = (double)sev.CubicTerm.Value; p[3] = 1; }
                if (sev.QuarticTerm.HasValue) { c[4] = (double)sev.QuarticTerm.Value; p[4] = 1; }
                if (sev.QuinticTerm.HasValue) { c[5] = (double)sev.QuinticTerm.Value; p[5] = 1; }
                if (sev.SexticTerm.HasValue) { c[6] = (double)sev.SexticTerm.Value; p[6] = 1; }
                c[7] = (double)sev.SepticTerm; p[7] = 1;
                result = XbimGeometryNativeApi.xbim_curve2d_build_polynomial_spiral(
                    ContextHandle, c, p, 8, startParam, endParam, px, py, dx, dy, out handle);
            }
            else
            {
                _logger.LogWarning("Unsupported spiral type {Type}", spiral.GetType().Name);
                return null;
            }

            if (result != 0)
            {
                _logger.LogWarning("Failed to build 2D spiral segment: {Error}", XbimGeometryNativeApi.GetLastError());
                return null;
            }

            return handle;
        }

        private NativeCurve2dHandle? BuildPolynomialCurve2dSegment(
            IfcPolynomialCurve polyCurve, double startParam, double endParam)
        {
            if (polyCurve.CoefficientsX == null || polyCurve.CoefficientsY == null)
                return null;

            double px = 0.0, py = 0.0, dx = 1.0, dy = 0.0;
            if (polyCurve.Position is IIfcAxis2Placement2D axis2d)
            {
                px = axis2d.Location.Coordinates[0];
                py = axis2d.Location.Coordinates[1];
                if (axis2d.RefDirection != null)
                {
                    dx = axis2d.RefDirection.DirectionRatios[0];
                    dy = axis2d.RefDirection.DirectionRatios[1];
                    double mag = Math.Sqrt(dx * dx + dy * dy);
                    if (mag > 1e-15) { dx /= mag; dy /= mag; }
                }
            }

            var coeffsX = polyCurve.CoefficientsX.Select(c => (double)c.Value).ToArray();
            var coeffsY = polyCurve.CoefficientsY.Select(c => (double)c.Value).ToArray();

            int result = XbimGeometryNativeApi.xbim_curve2d_build_polynomial(
                ContextHandle,
                coeffsX, coeffsX.Length,
                coeffsY, coeffsY.Length,
                px, py, dx, dy,
                startParam, endParam,
                out var handle);

            if (result != 0)
            {
                _logger.LogWarning("Failed to build 2D polynomial segment: {Error}", XbimGeometryNativeApi.GetLastError());
                return null;
            }

            return handle;
        }

        private NativeCurve2dHandle? BuildLineCurveSegment2d(
            IIfcLine line, double startParam, double endParam, double length)
        {
            var origin = line.Pnt;
            double ox = origin.Coordinates[0];
            double oy = origin.Coordinates.Count > 1 ? origin.Coordinates[1] : 0.0;

            double vx = line.Dir.Orientation.DirectionRatios[0];
            double vy = line.Dir.Orientation.DirectionRatios.Count > 1
                ? line.Dir.Orientation.DirectionRatios[1] : 0.0;

            double mag = Math.Sqrt(vx * vx + vy * vy);
            if (mag > 1e-15) { vx /= mag; vy /= mag; }

            // Build trimmed line as segment
            double x1 = ox + vx * startParam;
            double y1 = oy + vy * startParam;
            double x2 = ox + vx * endParam;
            double y2 = oy + vy * endParam;

            int result = XbimGeometryNativeApi.xbim_curve2d_build_line(
                ContextHandle, x1, y1, x2, y2, out var handle);

            if (result != 0)
            {
                _logger.LogWarning("Failed to build 2D line segment: {Error}", XbimGeometryNativeApi.GetLastError());
                return null;
            }

            // Move to origin for consistent joining
            XbimGeometryNativeApi.xbim_curve2d_move_to_origin(handle);
            return handle;
        }

        private NativeCurve2dHandle? BuildCircleCurveSegment2d(
            IIfcCircle circle, double startParam, double endParam, double length)
        {
            double cx = 0, cy = 0, dx = 1.0, dy = 0.0;
            if (circle.Position is IIfcAxis2Placement2D axis2d)
            {
                cx = axis2d.Location.Coordinates[0];
                cy = axis2d.Location.Coordinates[1];
                if (axis2d.RefDirection != null)
                {
                    dx = axis2d.RefDirection.DirectionRatios[0];
                    dy = axis2d.RefDirection.DirectionRatios[1];
                    double mag = Math.Sqrt(dx * dx + dy * dy);
                    if (mag > 1e-15) { dx /= mag; dy /= mag; }
                }
            }

            // Build circle
            int circResult = XbimGeometryNativeApi.xbim_curve2d_build_circle(
                ContextHandle, cx, cy, circle.Radius, dx, dy,
                out var circleHandle);
            if (circResult != 0) return null;

            // Convert distance-along parameters to angular parameters
            double r = circle.Radius;
            double u1 = startParam / r;
            double u2 = endParam / r;
            bool sameSense = length >= 0;

            int arcResult = XbimGeometryNativeApi.xbim_curve2d_build_arc_of_circle(
                ContextHandle, circleHandle, u1, u2, sameSense ? 1 : 0,
                out var arcHandle);
            if (arcResult != 0) return null;

            XbimGeometryNativeApi.xbim_curve2d_move_to_origin(arcHandle);
            return arcHandle;
        }

        private void AlignHeightFunction(
            NativeCurve2dHandle horizontalHandle,
            NativeCurve2dHandle heightFunctionHandle)
        {
            // The height function X axis represents distance-along.
            // If the horizontal projection doesn't start at 0, or the height function's
            // start X doesn't match, translate the height function.
            // This matches the legacy TranslateCurveStartPointToX logic.
            XbimGeometryNativeApi.xbim_curve2d_translate_start_to_x(heightFunctionHandle, 0.0);
        }

        private static void AppendEndPointSegment(
            NativeCurve2dHandle heightFunctionHandle,
            IIfcAxis2Placement2D endPoint)
        {
            // The EndPoint is typically not used in most files.
            // When present, it defines a trailing constant-height segment.
            // For now, skip this — the gradient curve is valid without it.
        }

        #endregion

        #region Spirals

        private static void ExtractSpiralPlacement2d(IfcAxis2Placement placement,
            out double x, out double y, out double dirX, out double dirY)
        {
            x = 0.0;
            y = 0.0;
            dirX = 1.0;
            dirY = 0.0;

            if (placement is IIfcAxis2Placement2D axis2d)
            {
                x = axis2d.Location.Coordinates[0];
                y = axis2d.Location.Coordinates[1];

                if (axis2d.RefDirection != null)
                {
                    double rdx = axis2d.RefDirection.DirectionRatios[0];
                    double rdy = axis2d.RefDirection.DirectionRatios[1];
                    double mag = Math.Sqrt(rdx * rdx + rdy * rdy);
                    if (mag > 1e-15)
                    {
                        dirX = rdx / mag;
                        dirY = rdy / mag;
                    }
                }
            }
            else if (placement is IIfcAxis2Placement3D axis3d)
            {
                x = axis3d.Location.Coordinates[0];
                y = axis3d.Location.Coordinates[1];

                if (axis3d.RefDirection != null)
                {
                    dirX = axis3d.RefDirection.DirectionRatios[0];
                    dirY = axis3d.RefDirection.DirectionRatios[1];
                    double mag = Math.Sqrt(dirX * dirX + dirY * dirY);
                    if (mag > 1e-15) { dirX /= mag; dirY /= mag; }
                }
            }
        }

        private Curve BuildClothoid(IfcClothoid clothoid, double startParam, double endParam,
            double placementX, double placementY, double dirX, double dirY)
        {
            int result = XbimGeometryNativeApi.xbim_curve_build_clothoid(
                ContextHandle,
                (double)clothoid.ClothoidConstant,
                startParam, endParam,
                placementX, placementY,
                dirX, dirY,
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build clothoid #{clothoid.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(curveHandle, XCurveType.IfcClothoid);
        }

        private Curve BuildSineSpiral(IfcSineSpiral spiral, double startParam, double endParam,
            double placementX, double placementY, double dirX, double dirY)
        {
            int result = XbimGeometryNativeApi.xbim_curve_build_sine_spiral(
                ContextHandle,
                (double)spiral.SineTerm,
                spiral.LinearTerm.HasValue ? (double)spiral.LinearTerm.Value : 0.0,
                spiral.ConstantTerm.HasValue ? (double)spiral.ConstantTerm.Value : 0.0,
                startParam, endParam,
                placementX, placementY,
                dirX, dirY,
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build sine spiral #{spiral.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(curveHandle, XCurveType.IfcSineSpiral);
        }

        private Curve BuildCosineSpiral(IfcCosineSpiral spiral, double startParam, double endParam,
            double placementX, double placementY, double dirX, double dirY)
        {
            int result = XbimGeometryNativeApi.xbim_curve_build_cosine_spiral(
                ContextHandle,
                (double)spiral.CosineTerm,
                spiral.ConstantTerm.HasValue ? (double)spiral.ConstantTerm.Value : 0.0,
                startParam, endParam,
                placementX, placementY,
                dirX, dirY,
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build cosine spiral #{spiral.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(curveHandle, XCurveType.IfcCosineSpiral);
        }

        private Curve BuildPolynomialSpiral(IfcSecondOrderPolynomialSpiral spiral,
            double startParam, double endParam,
            double placementX, double placementY, double dirX, double dirY)
        {
            // Second order: A0=ConstantTerm, A1=LinearTerm, A2=QuadraticTerm
            var coefficients = new double[8];
            var present = new int[8];

            if (spiral.ConstantTerm.HasValue) { coefficients[0] = (double)spiral.ConstantTerm.Value; present[0] = 1; }
            if (spiral.LinearTerm.HasValue) { coefficients[1] = (double)spiral.LinearTerm.Value; present[1] = 1; }
            coefficients[2] = (double)spiral.QuadraticTerm; present[2] = 1;

            return BuildPolynomialSpiralCore(coefficients, present, startParam, endParam,
                placementX, placementY, dirX, dirY,
                XCurveType.IfcSecondOrderPolynomialSpiral, spiral.EntityLabel);
        }

        private Curve BuildPolynomialSpiral(IfcThirdOrderPolynomialSpiral spiral,
            double startParam, double endParam,
            double placementX, double placementY, double dirX, double dirY)
        {
            // Third order: A0=ConstantTerm, A1=LinearTerm, A2=QuadraticTerm, A3=CubicTerm
            var coefficients = new double[8];
            var present = new int[8];

            if (spiral.ConstantTerm.HasValue) { coefficients[0] = (double)spiral.ConstantTerm.Value; present[0] = 1; }
            if (spiral.LinearTerm.HasValue) { coefficients[1] = (double)spiral.LinearTerm.Value; present[1] = 1; }
            if (spiral.QuadraticTerm.HasValue) { coefficients[2] = (double)spiral.QuadraticTerm.Value; present[2] = 1; }
            coefficients[3] = (double)spiral.CubicTerm; present[3] = 1;

            return BuildPolynomialSpiralCore(coefficients, present, startParam, endParam,
                placementX, placementY, dirX, dirY,
                XCurveType.IfcThirdOrderPolynomialSpiral, spiral.EntityLabel);
        }

        private Curve BuildPolynomialSpiral(IfcSeventhOrderPolynomialSpiral spiral,
            double startParam, double endParam,
            double placementX, double placementY, double dirX, double dirY)
        {
            // Seventh order: A0..A7
            var coefficients = new double[8];
            var present = new int[8];

            if (spiral.ConstantTerm.HasValue) { coefficients[0] = (double)spiral.ConstantTerm.Value; present[0] = 1; }
            if (spiral.LinearTerm.HasValue) { coefficients[1] = (double)spiral.LinearTerm.Value; present[1] = 1; }
            if (spiral.QuadraticTerm.HasValue) { coefficients[2] = (double)spiral.QuadraticTerm.Value; present[2] = 1; }
            if (spiral.CubicTerm.HasValue) { coefficients[3] = (double)spiral.CubicTerm.Value; present[3] = 1; }
            if (spiral.QuarticTerm.HasValue) { coefficients[4] = (double)spiral.QuarticTerm.Value; present[4] = 1; }
            if (spiral.QuinticTerm.HasValue) { coefficients[5] = (double)spiral.QuinticTerm.Value; present[5] = 1; }
            if (spiral.SexticTerm.HasValue) { coefficients[6] = (double)spiral.SexticTerm.Value; present[6] = 1; }
            coefficients[7] = (double)spiral.SepticTerm; present[7] = 1;

            return BuildPolynomialSpiralCore(coefficients, present, startParam, endParam,
                placementX, placementY, dirX, dirY,
                XCurveType.IfcSeventhOrderPolynomialSpiral, spiral.EntityLabel);
        }

        private Curve BuildPolynomialSpiralCore(double[] coefficients, int[] present,
            double startParam, double endParam,
            double placementX, double placementY, double dirX, double dirY,
            XCurveType curveType, int entityLabel)
        {
            int result = XbimGeometryNativeApi.xbim_curve_build_polynomial_spiral(
                ContextHandle,
                coefficients, present, 8,
                startParam, endParam,
                placementX, placementY,
                dirX, dirY,
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build polynomial spiral #{entityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(curveHandle, curveType);
        }

        #endregion
    }
}
