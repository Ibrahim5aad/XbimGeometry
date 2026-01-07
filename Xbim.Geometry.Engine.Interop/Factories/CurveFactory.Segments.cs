using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    internal partial class CurveFactory
    {
        #region Gradient Curve

        private XbimCurve BuildGradientCurve(IfcGradientCurve ifcGradient)
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
                    throw new XbimGeometryServiceException(
                        $"IfcGradientCurve #{ifcGradient.EntityLabel}: no valid height function segments.");

                // Step 3: Join height segments into composite 2D B-spline
                using var nativeHeightSegs = new NativeHandleArray(heightSegmentHandles.ToArray());
                int compositeResult = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                    ContextHandle, nativeHeightSegs.Ptrs, nativeHeightSegs.Length,
                    _modelService.MinimumGap,
                    out var heightFunctionHandle);

                if (compositeResult != 0)
                    throw new XbimGeometryServiceException(
                        $"IfcGradientCurve #{ifcGradient.EntityLabel}: failed to build height function: {XbimGeometryNativeApi.GetLastError()}");

                // Step 4: Align the height function's X origin with horizontal projection
                AlignHeightFunction(horizontalHandle, heightFunctionHandle);

                // Step 5: Handle EndPoint (optional trailing segment)
                if (ifcGradient.EndPoint is IIfcAxis2Placement2D endPt)
                    AppendEndPointSegment(heightFunctionHandle, endPt);

                // Step 6: Create gradient curve from horizontal + height
                int gradientResult = XbimGeometryNativeApi.xbim_curve_build_gradient(
                    ContextHandle, horizontalHandle, heightFunctionHandle,
                    out var curveHandle);

                if (gradientResult != 0)
                    throw new XbimGeometryServiceException(
                        $"IfcGradientCurve #{ifcGradient.EntityLabel}: failed to build gradient curve: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimGradientCurve(curveHandle, ContextHandle, _modelService.Precision);
            }
            finally
            {
                foreach (var h in heightSegmentHandles)
                    h.Dispose();
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
                        throw new XbimGeometryServiceException(
                            $"IfcGradientCurve #{ifcGradient.EntityLabel}: BaseCurve has no valid segments.");

                    using var nativeSegs = new NativeHandleArray(segHandles.ToArray());
                    int result = XbimGeometryNativeApi.xbim_curve2d_build_composite_bspline(
                        ContextHandle, nativeSegs.Ptrs, nativeSegs.Length,
                        _modelService.MinimumGap,
                        out var compositeHandle);

                    if (result != 0)
                        throw new XbimGeometryServiceException(
                            $"IfcGradientCurve #{ifcGradient.EntityLabel}: failed to build BaseCurve composite: {XbimGeometryNativeApi.GetLastError()}");

                    return compositeHandle;
                }
                finally
                {
                    foreach (var h in segHandles)
                        h.Dispose();
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
                    throw new XbimGeometryServiceException(
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
                var handle = BuildSpiralInternal(spiral, startParam, endParam);
                if (handle != null && !handle.IsInvalid)
                    XbimGeometryNativeApi.xbim_curve2d_align_to_origin(handle);
                return handle;
            }

            // Polynomial curves: build 2D native polynomial and move to origin
            if (parentCurve is IfcPolynomialCurve polyCurve)
            {
                var handle = BuildPolynomialCurve(polyCurve, startParam, endParam);
                if (handle != null && !handle.IsInvalid)
                    XbimGeometryNativeApi.xbim_curve2d_align_to_origin(handle);
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

            return BuildGenericCurveSegment2d(segment, parentCurve, startParam, endParam, length);
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
            XbimGeometryNativeApi.xbim_curve2d_align_to_origin(handle);
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

            XbimGeometryNativeApi.xbim_curve2d_align_to_origin(arcHandle);
            return arcHandle;
        }

        private NativeCurve2dHandle? BuildGenericCurveSegment2d(
            IfcCurveSegment segment, IIfcCurve parentCurve,
            double startParam, double endParam, double length)
        {
            using var curve2d = (XbimCurve2d)BuildCurve2d(parentCurve);

            if (parentCurve is IIfcEllipse)
            {
                curve2d.Dispose();
                throw new NotSupportedException(
                    $"IfcEllipse is not supported as CurveSegment parent (#{segment.EntityLabel}).");
            }

            bool sameSense = length >= 0;

            int trimResult = XbimGeometryNativeApi.xbim_curve2d_build_trimmed(
                ContextHandle, curve2d.Handle, startParam, endParam, sameSense ? 1 : 0,
                out var trimHandle);

            if (trimResult != 0)
            {
                _logger.LogWarning("Failed to trim generic curve segment #{Label}: {Error}",
                    segment.EntityLabel, XbimGeometryNativeApi.GetLastError());
                return null;
            }

            int alignResult = XbimGeometryNativeApi.xbim_curve2d_align_to_origin(trimHandle);
            if (alignResult != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to align curve to origin: {XbimGeometryNativeApi.GetLastError()}");

            return trimHandle;
        }

        private void AlignHeightFunction(
            NativeCurve2dHandle horizontalHandle,
            NativeCurve2dHandle heightFunctionHandle)
        {
            // The height function X axis represents distance-along.
            // If the horizontal projection doesn't start at 0, or the height function's
            // start X doesn't match, translate the height function.
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

        #region Segmented Reference Curve

        private XbimCurve BuildSegmentedReferenceCurve(IfcSegmentedReferenceCurve ifcSegRef)
        {
            // Step 1: Build the base gradient curve (via cache to avoid redundant construction)
            if (ifcSegRef.BaseCurve is not IfcGradientCurve ifcGradient)
                throw new XbimGeometryServiceException(
                    $"IfcSegmentedReferenceCurve #{ifcSegRef.EntityLabel}: BaseCurve must be an IfcGradientCurve.");

            var gradientCurve = GetOrBuildCached(ifcGradient.EntityLabel, () => BuildGradientCurve(ifcGradient));
            var gradientHandle = gradientCurve.Handle;

            // Step 2: Build superelevation segments (2D rate-of-change curves + placement locations)
            var segCurveHandles = new List<NativeCurve2dHandle>();
            var segLocationHandles = new List<NativeLocationHandle>();

            try
            {
                foreach (var segment in ifcSegRef.Segments)
                {
                    if (segment is not IfcCurveSegment curveSegment)
                        continue;

                    // Build the 2D rate-of-change curve for this superelevation segment
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

                    segCurveHandles.Add(segHandle);

                    // Build location from the segment's placement
                    var locHandle = BuildSegmentLocation(curveSegment.Placement);
                    segLocationHandles.Add(locHandle);
                }

                // Step 3: Build the optional endpoint location
                NativeLocationHandle endPointHandle = NativeLocationHandle.NullHandle;
                if (ifcSegRef.EndPoint is IIfcPlacement endPlacement)
                    endPointHandle = BuildPlacementLocation(endPlacement);

                // Step 4: Create the segmented reference curve
                using var nativeCurves = new NativeHandleArray(segCurveHandles.ToArray());
                using var nativeLocs = new NativeHandleArray(segLocationHandles.ToArray());

                int result = XbimGeometryNativeApi.xbim_curve_build_segmented_reference(
                    ContextHandle,
                    gradientHandle,
                    nativeCurves.Ptrs,
                    nativeLocs.Ptrs,
                    nativeCurves.Length,
                    endPointHandle,
                    out var outHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"IfcSegmentedReferenceCurve #{ifcSegRef.EntityLabel}: failed to build: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimCurve(outHandle, XCurveType.IfcSegmentedReferenceCurve);
            }
            finally
            {
                foreach (var h in segCurveHandles)
                    h.Dispose();
                foreach (var h in segLocationHandles)
                    h.Dispose();
                gradientCurve?.Dispose();
            }
        }

        private NativeLocationHandle BuildSegmentLocation(IIfcPlacement placement)
        {
            if (placement is IIfcAxis2Placement3D axis3d)
            {
                double ox = axis3d.Location.Coordinates[0];
                double oy = axis3d.Location.Coordinates.Count > 1 ? axis3d.Location.Coordinates[1] : 0.0;
                double oz = axis3d.Location.Coordinates.Count > 2 ? axis3d.Location.Coordinates[2] : 0.0;

                double zx = 0, zy = 0, zz = 1;
                if (axis3d.Axis != null)
                {
                    zx = axis3d.Axis.DirectionRatios[0];
                    zy = axis3d.Axis.DirectionRatios[1];
                    zz = axis3d.Axis.DirectionRatios[2];
                }

                double xx = 1, xy = 0, xz = 0;
                if (axis3d.RefDirection != null)
                {
                    xx = axis3d.RefDirection.DirectionRatios[0];
                    xy = axis3d.RefDirection.DirectionRatios[1];
                    xz = axis3d.RefDirection.DirectionRatios[2];
                }

                int r = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                    ox, oy, oz, zx, zy, zz, xx, xy, xz, out var locHandle);
                if (r != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to create location from Axis2Placement3D: {XbimGeometryNativeApi.GetLastError()}");
                return locHandle;
            }

            if (placement is IIfcAxis2Placement2D axis2d)
            {
                double ox = axis2d.Location.Coordinates[0];
                double oy = axis2d.Location.Coordinates.Count > 1 ? axis2d.Location.Coordinates[1] : 0.0;

                double dx = 1, dy = 0;
                if (axis2d.RefDirection != null)
                {
                    dx = axis2d.RefDirection.DirectionRatios[0];
                    dy = axis2d.RefDirection.DirectionRatios[1];
                }

                // Map 2D placement to 3D: XY plane with Z-up, rotation encodes cant tilt
                int r = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                    ox, oy, 0.0, 0.0, 0.0, 1.0, dx, dy, 0.0, out var locHandle);
                if (r != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to create location from Axis2Placement2D: {XbimGeometryNativeApi.GetLastError()}");
                return locHandle;
            }

            // Fallback: identity location
            XbimGeometryNativeApi.xbim_location_create_identity(out var identityHandle);
            return identityHandle;
        }

        private NativeLocationHandle BuildPlacementLocation(IIfcPlacement placement)
        {
            return BuildSegmentLocation(placement);
        }

        #endregion

        #region Spirals and Polynomials

        public IXCurve BuildSpiral(IfcSpiral spiral, double startParam, double endParam)
        {
            var handle = BuildSpiralInternal(spiral, startParam, endParam) ??
                         throw new XbimGeometryServiceException("Failed to build spiral");

            if (XbimGeometryNativeApi.xbim_curve2d_align_to_origin(handle) != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to align spiral to origin: {XbimGeometryNativeApi.GetLastError()}");
            return new XbimCurve2d(handle, GetSpiralCurveType(spiral));
        }

        private static XCurveType GetSpiralCurveType(IfcSpiral spiral)
        {
            return spiral switch
            {
                IfcClothoid => XCurveType.IfcClothoid,
                IfcSineSpiral => XCurveType.IfcSineSpiral,
                IfcCosineSpiral => XCurveType.IfcCosineSpiral,
                IfcSecondOrderPolynomialSpiral => XCurveType.IfcSecondOrderPolynomialSpiral,
                IfcThirdOrderPolynomialSpiral => XCurveType.IfcThirdOrderPolynomialSpiral,
                IfcSeventhOrderPolynomialSpiral => XCurveType.IfcSeventhOrderPolynomialSpiral,
                _ => throw new NotSupportedException($"Unsupported spiral type {spiral.GetType().Name}")
            };
        }

        private NativeCurve2dHandle? BuildSpiralInternal(IfcSpiral spiral, double startParam, double endParam)
        {
            ExtractSpiralPlacement2d(spiral.Position,
                out double px, out double py, out double dx, out double dy);

            NativeCurve2dHandle? handle;
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

        public IXCurve BuildPolynomialCurve2d(IfcPolynomialCurve curve, double startParam, double endParam)
        {
            var handle = BuildPolynomialCurve(curve, startParam, endParam);
            if (handle == null)
                throw new XbimGeometryServiceException("Failed to build polynomial curve");
            int alignResult = XbimGeometryNativeApi.xbim_curve2d_align_to_origin(handle);
            if (alignResult != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to align polynomial curve to origin: {XbimGeometryNativeApi.GetLastError()}");
            return new XbimCurve2d(handle, XCurveType.IfcPolynomialCurve);
        }

        private NativeCurve2dHandle? BuildPolynomialCurve(
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

        #endregion
    }
}
