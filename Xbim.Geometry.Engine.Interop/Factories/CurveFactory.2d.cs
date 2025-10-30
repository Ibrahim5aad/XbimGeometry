using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
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
            // TODO: CURVE-R002 — Implement 2D basic curves (Line2d, Circle2d, Ellipse2d)
            if (curve is IIfcLine)
                throw new NotSupportedException(
                    $"2D IfcLine #{curve.EntityLabel} is not yet supported. See CURVE-R002.");

            if (curve is IIfcCircle)
                throw new NotSupportedException(
                    $"2D IfcCircle #{curve.EntityLabel} is not yet supported. See CURVE-R002.");

            if (curve is IIfcEllipse)
                throw new NotSupportedException(
                    $"2D IfcEllipse #{curve.EntityLabel} is not yet supported. See CURVE-R002.");

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
    }
}
