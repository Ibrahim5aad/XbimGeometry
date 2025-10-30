using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Ifc4.Interfaces;

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
                return Build(ifcTrimmed.BasisCurve);

            if (curve is IIfcPolyline ifcPolyline)
                return BuildPolylineAsBSpline(ifcPolyline);

            if (curve is IIfcCompositeCurve ifcComposite)
                return BuildCompositeCurve(ifcComposite);

            if (curve is IIfcIndexedPolyCurve ifcIndexed)
                return BuildIndexedPolyCurve(ifcIndexed);

            throw new NotSupportedException(
                $"3D curve type {curve.ExpressType.ExpressName} #{curve.EntityLabel} is not yet supported.");
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
                out var nativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build line curve #{ifcLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(nativeCurveHandle, XCurveType.IfcLine);
        }

        #endregion

        #region Circle

        private Curve BuildCircle(IIfcCircle ifcCircle)
        {
            if (ifcCircle.Radius <= 0)
                throw new InvalidOperationException(
                    $"IIfcCircle #{ifcCircle.EntityLabel} has invalid radius {ifcCircle.Radius}. Radius must be greater than zero.");

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
                out var nativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build circle curve #{ifcCircle.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(nativeCurveHandle, XCurveType.IfcCircle);
        }

        #endregion

        #region Ellipse

        private Curve BuildEllipse(IIfcEllipse ifcEllipse)
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
                out _,
                out var nativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build ellipse curve #{ifcEllipse.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(nativeCurveHandle, XCurveType.IfcEllipse);
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
                out var nativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build B-spline curve #{ifcBSpline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var curveType = ifcBSpline is IIfcRationalBSplineCurveWithKnots
                ? XCurveType.IfcRationalBSplineCurveWithKnots
                : XCurveType.IfcBSplineCurveWithKnots;

            return new Curve(nativeCurveHandle, curveType);
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
                out var nativeCurveHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build polyline as B-spline #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Curve(nativeCurveHandle, XCurveType.IfcPolyline);
        }

        #endregion

        #region Composite / Indexed

        private Curve BuildCompositeCurve(IIfcCompositeCurve ifcComposite)
        {
            var segmentCurves = new List<Curve>();

            try
            {
                foreach (var segment in ifcComposite.Segments)
                {
                    if (segment.ParentCurve == null)
                        continue;

                    var segCurve = (Curve)Build(segment.ParentCurve);

                    if (!segment.SameSense)
                    {
                        int reverseResult = XbimGeometryNativeApi.xbim_curve_reverse(segCurve.Handle);
                        if (reverseResult != 0)
                            throw new InvalidOperationException(
                                $"Failed to reverse composite curve segment: {XbimGeometryNativeApi.GetLastError()}");
                    }

                    segmentCurves.Add(segCurve);
                }

                if (segmentCurves.Count == 0)
                    throw new InvalidOperationException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments.");

                using var nativeSegments = new NativeHandleArray(
                    segmentCurves.Select(c => c.Handle).ToArray());
                int result = XbimGeometryNativeApi.xbim_curve_build_composite_bspline(
                    ContextHandle, nativeSegments.Ptrs, nativeSegments.Length,
                    _modelService.MinimumGap,
                    out var compositeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve(compositeHandle, XCurveType.IfcCompositeCurve);
            }
            finally
            {
                foreach (var c in segmentCurves)
                    c.Dispose();
            }
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
                    out var nativeCurveHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve(nativeCurveHandle, XCurveType.IfcIndexedPolyCurve);
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
                    out var nativeCurveHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build indexed poly curve 2D #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve(nativeCurveHandle, XCurveType.IfcIndexedPolyCurve);
            }

            throw new NotSupportedException(
                $"Unsupported point list type in IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}.");
        }

        #endregion
    }
}
