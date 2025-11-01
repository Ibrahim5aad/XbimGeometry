using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
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

            // TODO: CURVE-R005 — Implement 2D TrimmedCurve
            if (curve is IIfcTrimmedCurve)
                throw new NotSupportedException(
                    $"2D IfcTrimmedCurve #{curve.EntityLabel} is not yet supported. See CURVE-R005.");

            // TODO: CURVE-R009 — Implement 2D BSpline
            if (curve is IIfcBSplineCurveWithKnots)
                throw new NotSupportedException(
                    $"2D IfcBSplineCurveWithKnots #{curve.EntityLabel} is not yet supported. See CURVE-R009.");

            // TODO: CURVE-R007 — Implement 2D IndexedPolyCurve
            if (curve is IIfcIndexedPolyCurve)
                throw new NotSupportedException(
                    $"2D IfcIndexedPolyCurve #{curve.EntityLabel} is not yet supported. See CURVE-R007.");

            // TODO: CURVE-R008 — Implement 2D Polyline
            if (curve is IIfcPolyline)
                throw new NotSupportedException(
                    $"2D IfcPolyline #{curve.EntityLabel} is not yet supported. See CURVE-R008.");

            // TODO: CURVE-R010 — Implement 2D CompositeCurve
            if (curve is IIfcCompositeCurve)
                throw new NotSupportedException(
                    $"2D IfcCompositeCurve #{curve.EntityLabel} is not yet supported. See CURVE-R010.");

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
    }
}
