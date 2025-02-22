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
    /// <summary>
    /// Cross-platform implementation of <see cref="IXSolidFactory"/> porting the
    /// IFC data extraction from the C++/CLI SolidFactory. Extracts geometric parameters
    /// from IFC entities in C# and delegates solid construction to native P/Invoke calls.
    /// </summary>
    internal class NativeSolidFactory : IXSolidFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeSolidFactory(NativeModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        #region CSG Primitives

        public IXSolid Build(IIfcCsgPrimitive3D ifcCsgPrimitive)
        {
            if (!Enum.TryParse<XCsgPrimitive3dType>(ifcCsgPrimitive.ExpressType.ExpressName, out var csgType))
                throw new NotSupportedException(
                    $"Unsupported CsgPrimitive3D type: {ifcCsgPrimitive.ExpressType.ExpressName}");

            return csgType switch
            {
                XCsgPrimitive3dType.IfcBlock => BuildBlock((IIfcBlock)ifcCsgPrimitive),
                XCsgPrimitive3dType.IfcSphere => BuildSphere((IIfcSphere)ifcCsgPrimitive),
                XCsgPrimitive3dType.IfcRightCircularCylinder => BuildRightCircularCylinder((IIfcRightCircularCylinder)ifcCsgPrimitive),
                XCsgPrimitive3dType.IfcRightCircularCone => BuildRightCircularCone((IIfcRightCircularCone)ifcCsgPrimitive),
                XCsgPrimitive3dType.IfcRectangularPyramid => BuildRectangularPyramid((IIfcRectangularPyramid)ifcCsgPrimitive),
                _ => throw new NotSupportedException($"Unhandled CsgPrimitive3D type: {csgType}")
            };
        }

        private IXSolid BuildBlock(IIfcBlock ifcBlock)
        {
            NativeGeometryFactory.BuildAxis2Placement3d(ifcBlock.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcBlock.XLength <= 0 || ifcBlock.YLength <= 0 || ifcBlock.ZLength <= 0)
                throw new InvalidOperationException(
                    $"CSG Block #{ifcBlock.EntityLabel} has zero or negative dimensions.");

            int result = NativeMethods.xbim_solid_build_block(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcBlock.XLength, ifcBlock.YLength, ifcBlock.ZLength,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CSG Block #{ifcBlock.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapSolid(shapeHandle);
        }

        private IXSolid BuildSphere(IIfcSphere ifcSphere)
        {
            NativeGeometryFactory.BuildAxis2Placement3d(ifcSphere.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcSphere.Radius <= 0)
                throw new InvalidOperationException(
                    $"CSG Sphere #{ifcSphere.EntityLabel} has zero or negative radius.");

            int result = NativeMethods.xbim_solid_build_sphere(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcSphere.Radius,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CSG Sphere #{ifcSphere.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapSolid(shapeHandle);
        }

        private IXSolid BuildRightCircularCylinder(IIfcRightCircularCylinder ifcCylinder)
        {
            NativeGeometryFactory.BuildAxis2Placement3d(ifcCylinder.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcCylinder.Radius <= 0 || ifcCylinder.Height <= 0)
                throw new InvalidOperationException(
                    $"CSG RightCircularCylinder #{ifcCylinder.EntityLabel} has zero or negative dimensions.");

            int result = NativeMethods.xbim_solid_build_right_circular_cylinder(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcCylinder.Radius, ifcCylinder.Height,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CSG RightCircularCylinder #{ifcCylinder.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapSolid(shapeHandle);
        }

        private IXSolid BuildRightCircularCone(IIfcRightCircularCone ifcCone)
        {
            NativeGeometryFactory.BuildAxis2Placement3d(ifcCone.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcCone.BottomRadius <= 0 || ifcCone.Height <= 0)
                throw new InvalidOperationException(
                    $"CSG RightCircularCone #{ifcCone.EntityLabel} has zero or negative dimensions.");

            int result = NativeMethods.xbim_solid_build_right_circular_cone(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcCone.BottomRadius, ifcCone.Height,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CSG RightCircularCone #{ifcCone.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapSolid(shapeHandle);
        }

        private IXSolid BuildRectangularPyramid(IIfcRectangularPyramid ifcPyramid)
        {
            NativeGeometryFactory.BuildAxis2Placement3d(ifcPyramid.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcPyramid.XLength <= 0 || ifcPyramid.YLength <= 0 || ifcPyramid.Height <= 0)
                throw new InvalidOperationException(
                    $"CSG RectangularPyramid #{ifcPyramid.EntityLabel} has zero or negative dimensions.");

            int result = NativeMethods.xbim_solid_build_rectangular_pyramid(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcPyramid.XLength, ifcPyramid.YLength, ifcPyramid.Height,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CSG RectangularPyramid #{ifcPyramid.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapSolid(shapeHandle);
        }

        #endregion

        #region Not Yet Implemented (future features)

        public IXShape Build(IIfcSolidModel ifcSolid)
        {
            throw new NotImplementedException(
                "Build(IIfcSolidModel) requires sweep and boolean support (SWEEP-004, BOOL-003).");
        }

        public IXShape Build(IIfcFacetedBrep ifcBrep)
        {
            throw new NotImplementedException(
                "Build(IIfcFacetedBrep) requires face/shell factory support (TOPO-001, TOPO-004).");
        }

        public IXShape Build(IIfcFaceBasedSurfaceModel ifcSurfaceModel)
        {
            throw new NotImplementedException(
                "Build(IIfcFaceBasedSurfaceModel) requires face factory support (TOPO-001).");
        }

        public IXSolid Build(IIfcHalfSpaceSolid ifcHalfSpaceSolid)
        {
            throw new NotImplementedException(
                "Build(IIfcHalfSpaceSolid) requires boolean half-space support (BOOL-002).");
        }

        public IXShape Build(IIfcShellBasedSurfaceModel ifcSurfaceModel)
        {
            throw new NotImplementedException(
                "Build(IIfcShellBasedSurfaceModel) requires shell factory support (TOPO-004).");
        }

        public IXShape Build(IIfcTessellatedItem ifcTessellatedItem)
        {
            throw new NotImplementedException(
                "Build(IIfcTessellatedItem) requires tessellation support.");
        }

        public IXShape Build(IIfcSectionedSpine ifcSectionedSpine)
        {
            throw new NotImplementedException(
                "Build(IIfcSectionedSpine) requires sweep factory support (SWEEP-003).");
        }

        #endregion
    }
}
