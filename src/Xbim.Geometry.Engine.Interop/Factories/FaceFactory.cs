using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds face shapes from surface geometry and boundary wires.
    /// Handles planar faces from wires and bounded faces from surface + wire combinations.
    /// </summary>
    internal class FaceFactory : IXFaceFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public FaceFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXFace BuildFace(IXSurface surface, IXWire[] wires)
        {
            ArgumentNullException.ThrowIfNull(surface);
            if (wires == null || wires.Length == 0)
                throw new ArgumentException("At least one wire is required.", nameof(wires));

            // The outer wire is the first, inner wires are the rest
            var outerWire = wires[0] as XbimWire
                ?? throw new ArgumentException("Expected XbimWire for outer boundary.");

            if (surface is Plane Plane)
            {
                // For planes, build from the wire directly
                if (wires.Length == 1)
                {
                    int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                        ContextHandle,
                        outerWire.Handle,
                        out var faceHandle);

                    if (result != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build planar face from wire: {XbimGeometryNativeApi.GetLastError()}");

                    return new XbimFace(faceHandle);
                }

                // With inner wires (voids), use the advanced face builder
                return BuildAdvancedFace(Plane, wires);
            }

            if (surface is Surface Surface)
                return BuildAdvancedFace(Surface, wires);

            throw new NotSupportedException(
                $"Cannot build face from surface type {surface.GetType().Name}.");
        }

        private XbimFace BuildAdvancedFace(Surface surface, IXWire[] wires)
        {
            var outerWire = (XbimWire)wires[0];

            // Determine surface type code for the native API
            int surfaceType = GetSurfaceType(surface.SurfaceType);

            // Extract surface placement for the native API
            double ox = 0, oy = 0, oz = 0;
            double zx = 0, zy = 0, zz = 1;
            double xx = 1, xy = 0, xz = 0;
            double radius = 0;

            if (surface is Plane plane)
            {
                ox = plane.Location.X; oy = plane.Location.Y; oz = plane.Location.Z;
                zx = plane.Axis.X; zy = plane.Axis.Y; zz = plane.Axis.Z;
                xx = plane.RefDirection.X; xy = plane.RefDirection.Y; xz = plane.RefDirection.Z;
            }

            // Collect inner wire handles
            var innerWireSources = wires.Length > 1
                ? wires.Skip(1).Cast<XbimWire>().Select(w => w.Handle).ToArray()
                : Array.Empty<NativeShapeHandle>();
            using var innerWireHandles = new NativeHandleArray(innerWireSources);

            int result = XbimGeometryNativeApi.xbim_face_build_advanced(
                ContextHandle,
                surfaceType,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                radius,
                outerWire.Handle,
                innerWireHandles.Ptrs,
                innerWireHandles.Length,
                1, // sameSense = true
                out var faceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build advanced face: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimFace(faceHandle);
        }

        /// <summary>
        /// Maps an XSurfaceType to the native surface type code used by the C API.
        /// </summary>
        private static int GetSurfaceType(XSurfaceType surfaceType)
        {
            // These codes must match the native xbim_face.cpp surface type enum
            return surfaceType switch
            {
                XSurfaceType.IfcPlane => 0,
                XSurfaceType.IfcCylindricalSurface => 1,
                XSurfaceType.IfcSphericalSurface => 2,
                _ => 0 // default to plane
            };
        }
    }
}
