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
    internal class NativeCurveFactory : IXCurveFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeCurveFactory(NativeModelGeometryService modelService, ILogger logger)
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
            throw new NotImplementedException(
                $"IfcSpiral #{curve.EntityLabel} requires IFC4x3 spiral support.");
        }

        public IXCurve BuildPolynomialCurve2d(IfcPolynomialCurve curve, double startParam, double endParam)
        {
            throw new NotImplementedException(
                $"IfcPolynomialCurve #{curve.EntityLabel} requires IFC4x3 polynomial curve support.");
        }

        #region Line

        private NativeCurve BuildLine(IIfcLine ifcLine)
        {
            var origin = NativeGeometryFactory.BuildPoint3d(ifcLine.Pnt);
            if (!NativeGeometryFactory.BuildDirection3d(ifcLine.Dir.Orientation,
                    out double dirX, out double dirY, out double dirZ))
                throw new InvalidOperationException(
                    $"IIfcLine #{ifcLine.EntityLabel} has invalid direction.");

            int result = XbimGeometryNativeApi.xbim_curve_build_line_3d(
                ContextHandle,
                origin.X, origin.Y, origin.Z,
                dirX, dirY, dirZ,
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build line curve #{ifcLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeCurve(curveHandle, XCurveType.IfcLine);
        }

        #endregion

        #region Circle

        private NativeCurve BuildCircle(IIfcCircle ifcCircle)
        {
            NativeGeometryFactory.BuildAxis2PlacementAs3d(
                ifcCircle.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out _, out _, out _);

            int result = XbimGeometryNativeApi.xbim_curve_build_circle_3d(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                ifcCircle.Radius,
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build circle curve #{ifcCircle.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeCurve(curveHandle, XCurveType.IfcCircle);
        }

        #endregion

        #region Ellipse

        private NativeCurve BuildEllipse(IIfcEllipse ifcEllipse)
        {
            NativeGeometryFactory.BuildAxis2PlacementAs3d(
                ifcEllipse.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out _, out _, out _);

            int result = XbimGeometryNativeApi.xbim_curve_build_ellipse_3d(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                ifcEllipse.SemiAxis1, ifcEllipse.SemiAxis2,
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build ellipse curve #{ifcEllipse.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeCurve(curveHandle, XCurveType.IfcEllipse);
        }

        #endregion

        #region BSpline

        private NativeCurve BuildBSpline(IIfcBSplineCurveWithKnots ifcBSpline)
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
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build B-spline curve #{ifcBSpline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var curveType = ifcBSpline is IIfcRationalBSplineCurveWithKnots
                ? XCurveType.IfcRationalBSplineCurveWithKnots
                : XCurveType.IfcBSplineCurveWithKnots;

            return new NativeCurve(curveHandle, curveType);
        }

        #endregion

        #region Polyline

        private NativeCurve BuildPolylineAsBSpline(IIfcPolyline ifcPolyline)
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
                out var curveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build polyline as B-spline #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeCurve(curveHandle, XCurveType.IfcPolyline);
        }

        #endregion

        #region Composite / Indexed

        private NativeCurve BuildCompositeCurve(IIfcCompositeCurve ifcComposite)
        {
            // Build each segment, then compose.
            // For now, build the first valid segment as a simplification.
            // Full composite curve support requires wire-level composition.
            foreach (var segment in ifcComposite.Segments)
            {
                if (segment.ParentCurve != null)
                    return (NativeCurve)Build(segment.ParentCurve);
            }

            throw new InvalidOperationException(
                $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments.");
        }

        private NativeCurve BuildIndexedPolyCurve(IIfcIndexedPolyCurve ifcIndexed)
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
                    out var curveHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new NativeCurve(curveHandle, XCurveType.IfcIndexedPolyCurve);
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
                    out var curveHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build indexed poly curve 2D #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new NativeCurve(curveHandle, XCurveType.IfcIndexedPolyCurve);
            }

            throw new NotSupportedException(
                $"Unsupported point list type in IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}.");
        }

        #endregion
    }
}
