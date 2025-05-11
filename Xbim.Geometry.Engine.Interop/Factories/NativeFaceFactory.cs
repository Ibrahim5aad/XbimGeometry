using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds face shapes from surface geometry and boundary wires.
    /// Handles planar faces from wires and bounded faces from surface + wire combinations.
    /// </summary>
    internal class NativeFaceFactory : IXFaceFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeFaceFactory(NativeModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXFace BuildFace(IXSurface surface, IXWire[] wires)
        {
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));
            if (wires == null || wires.Length == 0)
                throw new ArgumentException("At least one wire is required.", nameof(wires));

            // The outer wire is the first, inner wires are the rest
            var outerWire = wires[0] as NativeWire
                ?? throw new ArgumentException("Expected NativeWire for outer boundary.");

            if (surface is NativePlane nativePlane)
            {
                // For planes, build from the wire directly
                if (wires.Length == 1)
                {
                    int result = NativeMethods.xbim_face_build_from_wire(
                        ContextHandle,
                        outerWire.Handle,
                        out var faceHandle);

                    if (result != 0)
                        throw new InvalidOperationException(
                            $"Failed to build planar face from wire: {NativeMethods.GetLastError()}");

                    return new NativeFace(faceHandle);
                }

                // With inner wires (voids), use the advanced face builder
                return BuildAdvancedFace(nativePlane, wires);
            }

            if (surface is NativeSurface nativeSurface)
                return BuildAdvancedFace(nativeSurface, wires);

            throw new NotSupportedException(
                $"Cannot build face from surface type {surface.GetType().Name}.");
        }

        private NativeFace BuildAdvancedFace(NativeSurface surface, IXWire[] wires)
        {
            var outerWire = (NativeWire)wires[0];

            // Determine surface type code for the native API
            int surfaceType = GetNativeSurfaceType(surface.SurfaceType);

            // Extract surface placement for the native API
            double ox = 0, oy = 0, oz = 0;
            double zx = 0, zy = 0, zz = 1;
            double xx = 1, xy = 0, xz = 0;
            double radius = 0;

            if (surface is NativePlane plane)
            {
                ox = plane.Location.X; oy = plane.Location.Y; oz = plane.Location.Z;
                zx = plane.Axis.X; zy = plane.Axis.Y; zz = plane.Axis.Z;
                xx = plane.RefDirection.X; xy = plane.RefDirection.Y; xz = plane.RefDirection.Z;
            }

            // Collect inner wire handles
            IntPtr[] innerWireHandles;
            if (wires.Length > 1)
            {
                innerWireHandles = wires
                    .Skip(1)
                    .Cast<NativeWire>()
                    .Select(w => w.Handle.DangerousGetHandle())
                    .ToArray();
            }
            else
            {
                innerWireHandles = Array.Empty<IntPtr>();
            }

            int result = NativeMethods.xbim_face_build_advanced(
                ContextHandle,
                surfaceType,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                radius,
                outerWire.Handle,
                innerWireHandles,
                innerWireHandles.Length,
                _modelService.Precision,
                1, // sameSense = true
                out var faceHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build advanced face: {NativeMethods.GetLastError()}");

            return new NativeFace(faceHandle);
        }

        /// <summary>
        /// Maps an XSurfaceType to the native surface type code used by the C API.
        /// </summary>
        private static int GetNativeSurfaceType(XSurfaceType surfaceType)
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
