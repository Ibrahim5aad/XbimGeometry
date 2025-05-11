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
    internal class NativeEdgeFactory : IXEdgeFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeEdgeFactory(NativeModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXEdge Build(IXPoint start, IXPoint end)
        {
            int result = NativeMethods.xbim_edge_build_line(
                ContextHandle,
                start.X, start.Y, start.Z,
                end.X, end.Y, end.Z,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build line edge: {NativeMethods.GetLastError()}");

            return new NativeEdge(shapeHandle);
        }

        public IXEdge Build(IIfcCurve curve)
        {
            // Build an edge from an IFC curve by first building the curve geometry,
            // then wrapping it as an edge via the wire/edge builder.
            // For simple cases (line, circle), we can build directly.
            var expressName = curve.ExpressType.ExpressName;

            if (curve is IIfcLine ifcLine)
                return BuildFromLine(ifcLine);

            if (curve is IIfcCircle ifcCircle)
                return BuildFromCircle(ifcCircle);

            if (curve is IIfcEllipse ifcEllipse)
                return BuildFromEllipse(ifcEllipse);

            // For complex curves, build via CurveFactory then wrap as edge
            var curveFactory = (NativeCurveFactory)_modelService.CurveFactory;
            var xCurve = curveFactory.Build(curve);
            return Build(xCurve);
        }

        public IXEdge Build(IXCurve curve)
        {
            // Building an edge from a standalone IXCurve requires native API support
            // for creating edges from curve handles (not yet exposed via C API).
            // Use Build(IIfcCurve) or Build(IXPoint, IXPoint) instead.
            throw new NotImplementedException(
                "Building an edge from an IXCurve requires native curve-to-edge API support.");
        }

        private IXEdge BuildFromLine(IIfcLine ifcLine)
        {
            var origin = NativeGeometryFactory.BuildPoint3d(ifcLine.Pnt);
            if (!NativeGeometryFactory.BuildDirection3d(ifcLine.Dir.Orientation,
                    out double dirX, out double dirY, out double dirZ))
                throw new InvalidOperationException(
                    $"IIfcLine #{ifcLine.EntityLabel} has invalid direction.");

            double magnitude = ifcLine.Dir.Magnitude;
            double endX = origin.X + dirX * magnitude;
            double endY = origin.Y + dirY * magnitude;
            double endZ = origin.Z + dirZ * magnitude;

            int result = NativeMethods.xbim_edge_build_line(
                ContextHandle,
                origin.X, origin.Y, origin.Z,
                endX, endY, endZ,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build line edge #{ifcLine.EntityLabel}: {NativeMethods.GetLastError()}");

            return new NativeEdge(shapeHandle);
        }

        private IXEdge BuildFromCircle(IIfcCircle ifcCircle)
        {
            NativeGeometryFactory.BuildAxis2Placement3d(
                (IIfcAxis2Placement3D)ifcCircle.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out _, out _, out _);

            int result = NativeMethods.xbim_edge_build_circle_arc(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                ifcCircle.Radius,
                0.0, 2.0 * Math.PI,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build circle edge #{ifcCircle.EntityLabel}: {NativeMethods.GetLastError()}");

            return new NativeEdge(shapeHandle);
        }

        private IXEdge BuildFromEllipse(IIfcEllipse ifcEllipse)
        {
            // Build ellipse as a curve, then wrap as edge
            var curveFactory = (NativeCurveFactory)_modelService.CurveFactory;
            var curve = curveFactory.Build(ifcEllipse);
            return Build(curve);
        }
    }
}
