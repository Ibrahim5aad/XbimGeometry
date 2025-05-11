using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds surface geometry from IFC surface entities. Handles planes,
    /// cylindrical surfaces, spherical surfaces, and B-spline surfaces.
    /// </summary>
    internal class NativeSurfaceFactory : IXSurfaceFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeSurfaceFactory(NativeModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXSurface Build(IIfcSurface surface)
        {
            if (surface is IIfcPlane ifcPlane)
                return BuildPlane(ifcPlane);

            if (surface is IIfcCylindricalSurface ifcCylinder)
                return BuildCylindricalSurface(ifcCylinder);

            if (surface is IIfcSphericalSurface ifcSphere)
                return BuildSphericalSurface(ifcSphere);

            if (surface is IIfcBSplineSurfaceWithKnots ifcBSpline)
                return BuildBSplineSurface(ifcBSpline);

            throw new NotSupportedException(
                $"Surface type {surface.ExpressType.ExpressName} #{surface.EntityLabel} is not yet supported.");
        }

        public IXPlane BuildPlane(IXPoint origin, IXDirection normal)
        {
            int result = NativeMethods.xbim_surface_build_plane(
                ContextHandle,
                origin.X, origin.Y, origin.Z,
                normal.X, normal.Y, normal.Z,
                out var surfaceHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build plane: {NativeMethods.GetLastError()}");

            // Default reference direction: perpendicular to normal
            var refDir = ComputeRefDirection(normal);
            return new NativePlane(surfaceHandle, origin, normal, refDir);
        }

        #region Plane

        private IXPlane BuildPlane(IIfcPlane ifcPlane)
        {
            NativeGeometryFactory.BuildAxis2Placement3d(ifcPlane.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = NativeMethods.xbim_surface_build_plane(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                out var surfaceHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build plane #{ifcPlane.EntityLabel}: {NativeMethods.GetLastError()}");

            var origin = new XPoint(ox, oy, oz);
            var normal = new XDirection(zx, zy, zz);
            var refDir = new XDirection(xx, xy, xz);
            return new NativePlane(surfaceHandle, origin, normal, refDir);
        }

        #endregion

        #region Cylindrical

        private NativeSurface BuildCylindricalSurface(IIfcCylindricalSurface ifcCylinder)
        {
            NativeGeometryFactory.BuildAxis2Placement3d(ifcCylinder.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = NativeMethods.xbim_surface_build_cylindrical(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                ifcCylinder.Radius,
                out var surfaceHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build cylindrical surface #{ifcCylinder.EntityLabel}: {NativeMethods.GetLastError()}");

            return new NativeSurface(surfaceHandle, XSurfaceType.IfcCylindricalSurface);
        }

        #endregion

        #region Spherical

        private NativeSurface BuildSphericalSurface(IIfcSphericalSurface ifcSphere)
        {
            NativeGeometryFactory.BuildAxis2Placement3d(ifcSphere.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = NativeMethods.xbim_surface_build_spherical(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                ifcSphere.Radius,
                out var surfaceHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build spherical surface #{ifcSphere.EntityLabel}: {NativeMethods.GetLastError()}");

            return new NativeSurface(surfaceHandle, XSurfaceType.IfcSphericalSurface);
        }

        #endregion

        #region BSpline Surface

        private NativeSurface BuildBSplineSurface(IIfcBSplineSurfaceWithKnots ifcBSpline)
        {
            // Extract control points (2D grid: UUpper x VUpper)
            var controlPointGrid = ifcBSpline.ControlPointsList;
            int numU = controlPointGrid.Count;
            int numV = controlPointGrid[0].Count;

            var polesXYZ = new double[numU * numV * 3];
            for (int u = 0; u < numU; u++)
            {
                var row = controlPointGrid[u];
                for (int v = 0; v < numV; v++)
                {
                    var cp = row[v];
                    int idx = (u * numV + v) * 3;
                    polesXYZ[idx + 0] = cp.Coordinates[0];
                    polesXYZ[idx + 1] = cp.Coordinates[1];
                    polesXYZ[idx + 2] = (int)cp.Dim == 3 ? (double)cp.Coordinates[2] : 0.0;
                }
            }

            // Extract U knots
            var uKnotValues = ifcBSpline.UKnots.ToArray();
            var uKnots = new double[uKnotValues.Length];
            for (int i = 0; i < uKnotValues.Length; i++)
                uKnots[i] = uKnotValues[i];

            // Extract V knots
            var vKnotValues = ifcBSpline.VKnots.ToArray();
            var vKnots = new double[vKnotValues.Length];
            for (int i = 0; i < vKnotValues.Length; i++)
                vKnots[i] = vKnotValues[i];

            // Extract U multiplicities
            var uMultValues = ifcBSpline.UMultiplicities.ToArray();
            var uMultiplicities = new int[uMultValues.Length];
            for (int i = 0; i < uMultValues.Length; i++)
                uMultiplicities[i] = (int)uMultValues[i];

            // Extract V multiplicities
            var vMultValues = ifcBSpline.VMultiplicities.ToArray();
            var vMultiplicities = new int[vMultValues.Length];
            for (int i = 0; i < vMultValues.Length; i++)
                vMultiplicities[i] = (int)vMultValues[i];

            int uDegree = (int)ifcBSpline.UDegree;
            int vDegree = (int)ifcBSpline.VDegree;

            // Extract weights for rational B-spline surfaces
            double[]? weights = null;
            if (ifcBSpline is IIfcRationalBSplineSurfaceWithKnots rational)
            {
                var weightGrid = rational.WeightsData;
                weights = new double[numU * numV];
                for (int u = 0; u < numU; u++)
                {
                    var row = weightGrid[u];
                    for (int v = 0; v < numV; v++)
                        weights[u * numV + v] = row[v];
                }
            }

            int result = NativeMethods.xbim_surface_build_bspline(
                ContextHandle,
                polesXYZ, numU, numV,
                uKnots, uKnots.Length,
                vKnots, vKnots.Length,
                uMultiplicities, vMultiplicities,
                uDegree, vDegree,
                weights,
                out var surfaceHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build B-spline surface #{ifcBSpline.EntityLabel}: {NativeMethods.GetLastError()}");

            var surfaceType = ifcBSpline is IIfcRationalBSplineSurfaceWithKnots
                ? XSurfaceType.IfcRationalBSplineSurfaceWithKnots
                : XSurfaceType.IfcBSplineSurfaceWithKnots;

            return new NativeSurface(surfaceHandle, surfaceType);
        }

        #endregion

        #region Helpers

        private static XDirection ComputeRefDirection(IXDirection normal)
        {
            // Find a direction perpendicular to the normal
            double nx = normal.X, ny = normal.Y, nz = normal.Z;
            double ax, ay, az;

            if (Math.Abs(nz) < 0.9)
            {
                // Cross product with (0, 0, 1)
                ax = ny;
                ay = -nx;
                az = 0;
            }
            else
            {
                // Cross product with (1, 0, 0)
                ax = 0;
                ay = nz;
                az = -ny;
            }

            return new XDirection(ax, ay, az);
        }

        #endregion
    }
}
