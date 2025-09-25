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
            throw new NotImplementedException(
                $"IfcPolynomialCurve #{curve.EntityLabel} requires IFC4x3 polynomial curve support.");
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
