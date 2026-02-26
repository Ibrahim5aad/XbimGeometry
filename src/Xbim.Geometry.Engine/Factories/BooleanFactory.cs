using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Factories
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
                throw new XbimGeometryServiceException(
                    $"Boolean result #{boolResult.EntityLabel} produced an empty shape.");

            // WrapShape takes ownership of the handle
            return NativeShapeWrapper.WrapShape(resultHandle);
        }

        private NativeShapeHandle BuildBooleanResult(IIfcBooleanResult boolResult)
        {
            // Try to flatten chains of UNION boolean results into a single
            // multi-body OCCT operation. This is critical for models with deeply nested
            // boolean trees (e.g. 200+ chained UNIONs) where sequential pairwise
            // operations become very slow as intermediate shapes grow in complexity.
            if (TryFlattenBooleanChain(boolResult,
                    out var chainOp, out var leftOperand, out var rightOperands))
            {
                return BuildFlattenedBooleanChain(boolResult, chainOp, leftOperand, rightOperands);
            }

            // Single-level boolean: build operands and perform pairwise operation.
            // secondHandle is always consumed; exactly one of firstHandle/outHandle
            // is returned to the caller, the other is disposed.
            var firstHandle = BuildOperand(boolResult.FirstOperand);
            NativeShapeHandle secondHandle = null;

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
                return firstHandle;

            double fuzzyTolerance = _modelService.Model.ModelFactors.PrecisionBoolean;
            int hasWarnings;
            int result;

            switch (boolResult.Operator)
            {
                case IfcBooleanOperator.UNION:
                    result = XbimGeometryNativeApi.xbim_boolean_union(
                        ContextHandle, firstHandle, secondHandle,
                        fuzzyTolerance, out hasWarnings, out var outUnion);
                    return FinishBoolean(boolResult, result, hasWarnings, firstHandle, outUnion);
                case IfcBooleanOperator.DIFFERENCE:
                    result = XbimGeometryNativeApi.xbim_boolean_cut(
                        ContextHandle, firstHandle, secondHandle,
                        fuzzyTolerance, out hasWarnings, out var outCut);
                    return FinishBoolean(boolResult, result, hasWarnings, firstHandle, outCut);
                case IfcBooleanOperator.INTERSECTION:
                    result = XbimGeometryNativeApi.xbim_boolean_intersect(
                        ContextHandle, firstHandle, secondHandle,
                        fuzzyTolerance, out hasWarnings, out var outIntersect);
                    return FinishBoolean(boolResult, result, hasWarnings, firstHandle, outIntersect);
                default:
                    firstHandle.Dispose();
                    throw new NotSupportedException(
                        $"Boolean operator {boolResult.Operator} is not supported.");
            }
        }

        /// <summary>
        /// Evaluates the result of a pairwise boolean operation. On success returns
        /// outHandle and disposes firstHandle. On failure returns firstHandle as
        /// fallback and disposes outHandle.
        /// </summary>
        private NativeShapeHandle FinishBoolean(
            IIfcBooleanResult boolResult, int result, int hasWarnings,
            NativeShapeHandle firstHandle, NativeShapeHandle outHandle)
        {
            if (hasWarnings != 0)
                _logger.LogDebug("Boolean {Operator} #{Label} issued warnings.",
                    boolResult.Operator, boolResult.EntityLabel);

            if (result != 0 || outHandle == null || outHandle.IsInvalid)
            {
                _logger.LogWarning(
                    "Boolean {Operator} #{Label} failed ({Error}), returning first operand.",
                    boolResult.Operator, boolResult.EntityLabel,
                    result != 0 ? XbimGeometryNativeApi.GetLastError() : "empty result");
                outHandle?.Dispose();
                return firstHandle;
            }

            firstHandle.Dispose();
            return outHandle;
        }

        /// <summary>
        /// Walks a chain of nested UNION boolean results, collecting all right-hand
        /// operands into a flat list. Returns false if the chain is only one level
        /// deep or uses an operator other than UNION.
        /// </summary>
        /// <remarks>
        /// Only UNION chains are flattened because OCCT 7.9.3's multi-body CUT
        /// can produce different topology than sequential pairwise cuts for complex
        /// shapes with self-intersections.
        /// </remarks>
        private static bool TryFlattenBooleanChain(
            IIfcBooleanResult root,
            out IfcBooleanOperator chainOperator,
            out IIfcBooleanOperand leftOperand,
            out List<IIfcBooleanOperand> rightOperands)
        {
            chainOperator = root.Operator;
            leftOperand = null;
            rightOperands = null;

            // Only flatten UNION chains
            if (root.Operator != IfcBooleanOperator.UNION)
                return false;

            // Must have at least 2 levels of same-operator results to benefit
            if (root.FirstOperand is not IIfcBooleanResult nested
                || nested is IIfcBooleanClippingResult
                || nested.Operator != IfcBooleanOperator.UNION
                || root.SecondOperand is IIfcHalfSpaceSolid)
                return false;

            rightOperands = new List<IIfcBooleanOperand>();
            var current = root;

            while (true)
            {
                rightOperands.Add(current.SecondOperand);

                if (current.FirstOperand is IIfcBooleanResult nextBool
                    && nextBool is not IIfcBooleanClippingResult
                    && nextBool.Operator == chainOperator
                    && nextBool.SecondOperand is not IIfcHalfSpaceSolid)
                {
                    current = nextBool;
                }
                else
                {
                    leftOperand = current.FirstOperand;
                    rightOperands.Reverse();
                    return true;
                }
            }
        }

        /// <summary>
        /// Builds all operands from a flattened boolean chain and executes them
        /// as a single multi-body OCCT boolean operation.
        /// </summary>
        private NativeShapeHandle BuildFlattenedBooleanChain(
            IIfcBooleanResult rootResult,
            IfcBooleanOperator op,
            IIfcBooleanOperand leftOperand,
            List<IIfcBooleanOperand> rightOperands)
        {
            _logger.LogDebug(
                "Flattening boolean {Operator} chain of {Count} operands for #{Label}.",
                op, rightOperands.Count + 1, rootResult.EntityLabel);

            NativeShapeHandle leftHandle = null;
            var toolHandles = new List<NativeShapeHandle>();

            try
            {
                leftHandle = BuildOperand(leftOperand);

                foreach (var operand in rightOperands)
                {
                    try
                    {
                        var handle = BuildOperand(operand);
                        if (handle != null && !handle.IsInvalid)
                        {
                            toolHandles.Add(handle);
                        }
                        else
                        {
                            handle?.Dispose();
                            _logger.LogWarning(
                                "Boolean chain #{Label}: operand produced invalid shape, skipping.",
                                rootResult.EntityLabel);
                        }
                    }
                    catch (NotSupportedException ex)
                    {
                        _logger.LogWarning(ex,
                            "Boolean chain #{Label}: unsupported operand, skipping.",
                            rootResult.EntityLabel);
                    }
                }

                // No valid tools — just return the left operand
                if (toolHandles.Count == 0)
                    return leftHandle;

                double fuzzyTolerance = _modelService.Model.ModelFactors.PrecisionBoolean;

                var bodyArr = new SafeHandle[] { leftHandle };
                var toolArr = toolHandles.Cast<SafeHandle>().ToArray();

                NativeShapeHandle outHandle;
                int hasWarnings;
                int result;

                using (var bodies = new NativeHandleArray(bodyArr))
                using (var tools = new NativeHandleArray(toolArr))
                {
                    result = op switch
                    {
                        IfcBooleanOperator.UNION =>
                            XbimGeometryNativeApi.xbim_boolean_union_multi(
                                ContextHandle, bodies.Ptrs, bodies.Length,
                                tools.Ptrs, tools.Length,
                                fuzzyTolerance, out hasWarnings, out outHandle),
                        IfcBooleanOperator.DIFFERENCE =>
                            XbimGeometryNativeApi.xbim_boolean_cut_multi(
                                ContextHandle, bodies.Ptrs, bodies.Length,
                                tools.Ptrs, tools.Length,
                                fuzzyTolerance, out hasWarnings, out outHandle),
                        IfcBooleanOperator.INTERSECTION =>
                            XbimGeometryNativeApi.xbim_boolean_intersect_multi(
                                ContextHandle, bodies.Ptrs, bodies.Length,
                                tools.Ptrs, tools.Length,
                                fuzzyTolerance, out hasWarnings, out outHandle),
                        _ => throw new NotSupportedException(
                            $"Boolean operator {op} is not supported.")
                    };
                }

                // Capture error message BEFORE disposing handles, because
                // xbim_shape_destroy calls xbim_clear_error() which would wipe it.
                string nativeError = result != 0 ? XbimGeometryNativeApi.GetLastError() : null;

                // Dispose input handles (NativeHandleArray refs already released)
                leftHandle.Dispose();
                leftHandle = null;
                foreach (var h in toolHandles) h.Dispose();
                toolHandles.Clear();

                if (hasWarnings != 0)
                    _logger.LogDebug("Boolean {Operator} chain #{Label} issued warnings.",
                        op, rootResult.EntityLabel);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Boolean {op} chain #{rootResult.EntityLabel} failed: {nativeError}");

                if (outHandle == null || outHandle.IsInvalid)
                    throw new XbimGeometryServiceException(
                        $"Boolean {op} chain #{rootResult.EntityLabel} returned an empty shape.");

                return outHandle;
            }
            catch
            {
                leftHandle?.Dispose();
                foreach (var h in toolHandles) h?.Dispose();
                throw;
            }
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
            if (shape is XbimShape xbimShape)
            {
                return xbimShape.DetachHandle();
            }

            throw new XbimGeometryServiceException(
                $"Boolean operand {operand.GetType().Name} produced a non-native shape.");
        }
    }
}
