using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    internal class NativeBooleanFactory : IXBooleanFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeBooleanFactory(NativeModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXShape Build(IIfcBooleanResult boolResult)
        {
            var resultHandle = BuildBooleanResult(boolResult);

            if (resultHandle == null || resultHandle.IsInvalid)
                throw new InvalidOperationException(
                    $"Boolean result #{boolResult.EntityLabel} produced an empty shape.");

            // WrapShape takes ownership of the handle
            return NativeShapeFactory.WrapShape(resultHandle);
        }

        private NativeShapeHandle BuildBooleanResult(IIfcBooleanResult boolResult)
        {
            var firstHandle = BuildOperand(boolResult.FirstOperand);
            NativeShapeHandle? secondHandle = null;

            try
            {
                secondHandle = BuildOperand(boolResult.SecondOperand);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Boolean result #{Label}: second operand failed, returning first operand unchanged.",
                    boolResult.EntityLabel);
                return firstHandle;
            }

            // Null or invalid second operand → return first operand unchanged
            // (common in IFC models with degenerate half-spaces)
            if (secondHandle == null || secondHandle.IsInvalid)
            {
                secondHandle?.Dispose();
                return firstHandle;
            }

            double fuzzyTolerance = _modelService.Model.ModelFactors.PrecisionBoolean;

            NativeShapeHandle outHandle;
            int hasWarnings;
            int result;

            switch (boolResult.Operator)
            {
                case IfcBooleanOperator.UNION:
                    result = XbimGeometryNativeApi.xbim_boolean_union(
                        ContextHandle, firstHandle, secondHandle,
                        fuzzyTolerance, out hasWarnings, out outHandle);
                    break;
                case IfcBooleanOperator.DIFFERENCE:
                    result = XbimGeometryNativeApi.xbim_boolean_cut(
                        ContextHandle, firstHandle, secondHandle,
                        fuzzyTolerance, out hasWarnings, out outHandle);
                    break;
                case IfcBooleanOperator.INTERSECTION:
                    result = XbimGeometryNativeApi.xbim_boolean_intersect(
                        ContextHandle, firstHandle, secondHandle,
                        fuzzyTolerance, out hasWarnings, out outHandle);
                    break;
                default:
                    firstHandle.Dispose();
                    secondHandle.Dispose();
                    throw new NotSupportedException(
                        $"Boolean operator {boolResult.Operator} is not supported.");
            }

            firstHandle.Dispose();
            secondHandle.Dispose();

            if (hasWarnings != 0)
                _logger.LogDebug("Boolean {Operator} #{Label} issued warnings.",
                    boolResult.Operator, boolResult.EntityLabel);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Boolean {boolResult.Operator} #{boolResult.EntityLabel} failed: {XbimGeometryNativeApi.GetLastError()}");

            if (outHandle == null || outHandle.IsInvalid)
                throw new InvalidOperationException(
                    $"Boolean {boolResult.Operator} #{boolResult.EntityLabel} returned an empty shape.");

            return outHandle;
        }

        private NativeShapeHandle BuildOperand(IIfcBooleanOperand operand)
        {
            // 1. Nested boolean result (recursive)
            if (operand is IIfcBooleanResult nestedBool)
                return BuildBooleanResult(nestedBool);

            // 2. Solid model (extruded, revolved, CSG primitive via SolidFactory, etc.)
            if (operand is IIfcSolidModel solidModel)
            {
                var shape = _modelService.SolidFactory.Build(solidModel);
                return ExtractHandle(shape, operand);
            }

            // 3. Half-space solid (clipping plane)
            if (operand is IIfcHalfSpaceSolid halfSpace)
            {
                var shape = BuildHalfSpace(halfSpace);
                return ExtractHandle(shape, operand);
            }

            // 4. CSG primitive (block, sphere, cylinder, cone, pyramid)
            if (operand is IIfcCsgPrimitive3D csgPrim)
            {
                var shape = _modelService.SolidFactory.Build(csgPrim);
                return ExtractHandle(shape, operand);
            }

            throw new NotSupportedException(
                $"Boolean operand type {operand.GetType().Name} is not supported.");
        }

        private IXShape BuildHalfSpace(IIfcHalfSpaceSolid halfSpace)
        {
            var elementarySurface = halfSpace.BaseSurface as IIfcElementarySurface;
            if (elementarySurface == null)
                throw new InvalidOperationException(
                    $"Half-space #{halfSpace.EntityLabel}: only elementary surfaces are supported.");

            // Determine surface type and extract placement
            int surfaceType;
            double radius = 0;

            if (elementarySurface is IIfcPlane)
            {
                surfaceType = 0; // XBIM_SURFACE_PLANE
            }
            else if (elementarySurface is IIfcCylindricalSurface cylSurf)
            {
                surfaceType = 1; // XBIM_SURFACE_CYLINDRICAL
                radius = cylSurf.Radius;
            }
            else if (elementarySurface is IIfcSphericalSurface sphSurf)
            {
                surfaceType = 2; // XBIM_SURFACE_SPHERICAL
                radius = sphSurf.Radius;
            }
            else
            {
                throw new NotSupportedException(
                    $"Half-space #{halfSpace.EntityLabel}: surface type {elementarySurface.GetType().Name} is not supported.");
            }

            // Extract surface placement
            NativeGeometryFactory.BuildAxis2Placement3d(elementarySurface.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int agreementFlag = halfSpace.AgreementFlag ? 1 : 0;
            double oneMeter = _modelService.OneMeter;
            double precision = _modelService.Precision;

            // Check for polygonal bounded half-space
            if (halfSpace is IIfcPolygonalBoundedHalfSpace polyBounded)
                return BuildPolygonalBoundedHalfSpace(polyBounded,
                    ox, oy, oz, zx, zy, zz, xx, xy, xz,
                    agreementFlag, oneMeter, precision);

            // Basic or boxed half-space (boxed is treated identically per IFC spec)
            int result;
            NativeShapeHandle outHandle;

            if (halfSpace is IIfcBoxedHalfSpace)
            {
                result = XbimGeometryNativeApi.xbim_halfspace_build_boxed(
                    ContextHandle, surfaceType,
                    ox, oy, oz, zx, zy, zz, xx, xy, xz,
                    radius, agreementFlag, oneMeter, precision,
                    out outHandle);
            }
            else
            {
                result = XbimGeometryNativeApi.xbim_halfspace_build(
                    ContextHandle, surfaceType,
                    ox, oy, oz, zx, zy, zz, xx, xy, xz,
                    radius, agreementFlag, oneMeter, precision,
                    out outHandle);
            }

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build half-space #{halfSpace.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapShape(outHandle);
        }

        private IXShape BuildPolygonalBoundedHalfSpace(
            IIfcPolygonalBoundedHalfSpace polyBounded,
            double surfOx, double surfOy, double surfOz,
            double surfZx, double surfZy, double surfZz,
            double surfXx, double surfXy, double surfXz,
            int agreementFlag, double oneMeter, double precision)
        {
            // The polygonal boundary must be 2D and the base surface must be planar
            if (!(polyBounded.BaseSurface is IIfcPlane))
                throw new InvalidOperationException(
                    $"Polygonal bounded half-space #{polyBounded.EntityLabel}: base surface must be planar.");

            // Extract boundary points from the polyline
            var polyline = polyBounded.PolygonalBoundary as IIfcPolyline;
            if (polyline == null)
                throw new NotSupportedException(
                    $"Polygonal bounded half-space #{polyBounded.EntityLabel}: only polyline boundaries are supported.");

            int pointCount = polyline.Points.Count;
            if (pointCount < 3)
                throw new InvalidOperationException(
                    $"Polygonal bounded half-space #{polyBounded.EntityLabel}: boundary needs at least 3 points.");

            var xCoords = new double[pointCount];
            var yCoords = new double[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                xCoords[i] = polyline.Points[i].Coordinates[0];
                yCoords[i] = polyline.Points[i].Coordinates[1];
            }

            // Extract boundary position (coordinate system of the polygonal boundary)
            double bOx = 0, bOy = 0, bOz = 0;
            double bZx = 0, bZy = 0, bZz = 1;
            double bXx = 1, bXy = 0, bXz = 0;
            if (polyBounded.Position != null)
            {
                NativeGeometryFactory.BuildAxis2Placement3d(polyBounded.Position,
                    out bOx, out bOy, out bOz,
                    out bZx, out bZy, out bZz,
                    out bXx, out bXy, out bXz);
            }

            int result = XbimGeometryNativeApi.xbim_halfspace_build_polygonal_bounded(
                ContextHandle,
                surfOx, surfOy, surfOz, surfZx, surfZy, surfZz, surfXx, surfXy, surfXz,
                agreementFlag,
                xCoords, yCoords, pointCount,
                bOx, bOy, bOz, bZx, bZy, bZz, bXx, bXy, bXz,
                oneMeter, precision,
                out var outHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build polygonal bounded half-space #{polyBounded.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapShape(outHandle);
        }

        private static NativeShapeHandle ExtractHandle(IXShape shape, IIfcBooleanOperand operand)
        {
            if (shape is NativeShape nativeShape)
            {
                // Transfer ownership: we take the handle out and the NativeShape wrapper
                // should not dispose it. We use the handle directly.
                return nativeShape.TakeHandle();
            }

            throw new InvalidOperationException(
                $"Boolean operand {operand.GetType().Name} produced a non-native shape.");
        }
    }
}
