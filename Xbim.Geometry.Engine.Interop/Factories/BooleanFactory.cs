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
    internal class BooleanFactory : IXBooleanFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public BooleanFactory(ModelGeometryService modelService, ILogger logger)
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
            return NativeShapeWrapper.WrapShape(resultHandle);
        }

        private NativeShapeHandle BuildBooleanResult(IIfcBooleanResult boolResult)
        {
            var firstHandle = BuildOperand(boolResult.FirstOperand);
            NativeShapeHandle secondHandle;

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

            // 3. Half-space solid
            if (operand is IIfcHalfSpaceSolid halfSpace)
            {
                var shape = _modelService.SolidFactory.Build(halfSpace);
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

        private static NativeShapeHandle ExtractHandle(IXShape shape, IIfcBooleanOperand operand)
        {
            if (shape is Shape Shape)
            {
                // Transfer ownership: we take the handle out and the Shape wrapper
                // should not dispose it. We use the handle directly.
                return Shape.DetachHandle();
            }

            throw new InvalidOperationException(
                $"Boolean operand {operand.GetType().Name} produced a non-native shape.");
        }
    }
}
