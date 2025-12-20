using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds edge shapes from IFC curve entities and coordinate data.
    /// Handles line edges, curve-based edges, and circular arc edges.
    /// </summary>
    internal class EdgeFactory : IXEdgeFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public EdgeFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXEdge Build(IXPoint start, IXPoint end)
        {
            int result = XbimGeometryNativeApi.xbim_edge_build_line(
                ContextHandle,
                start.X, start.Y, start.Z,
                end.X, end.Y, end.Z,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build line edge: {XbimGeometryNativeApi.GetLastError()}");

            return new Edge(NativeShapeHandle);
        }

        public IXEdge Build(IIfcCurve curve)
        {
            var xCurve = _modelService.CurveFactory.Build(curve);
            return Build(xCurve);
        }

        public IXEdge Build(IXCurve curve)
        {
            int result;
            NativeShapeHandle shapeHandle;

            if (curve.Is3d)
            {
                var nativeCurve = (Curve)curve;
                result = XbimGeometryNativeApi.xbim_edge_from_curve_handle(
                    ContextHandle, nativeCurve.Handle, out shapeHandle);
            }
            else
            {
                var nativeCurve2d = (Curve2d)curve;
                result = XbimGeometryNativeApi.xbim_edge_from_curve2d_handle(
                    ContextHandle, nativeCurve2d.Handle, out shapeHandle);
            }

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build edge from curve: {XbimGeometryNativeApi.GetLastError()}");

            return new Edge(shapeHandle);
        }
    }
}
