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
    /// Builds 3D solids from IFC solid model entities (CSG primitives, extruded/revolved
    /// sweeps, tapered variants). Extracts geometric parameters from IFC entities and
    /// delegates solid construction to native OCCT operations.
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

        #region Swept Area Solids

        public IXShape Build(IIfcSolidModel ifcSolid)
        {
            if (!Enum.TryParse<XSolidModelType>(ifcSolid.ExpressType.ExpressName, out var solidType))
                throw new NotSupportedException(
                    $"Unsupported solid model type: {ifcSolid.ExpressType.ExpressName}");

            return solidType switch
            {
                XSolidModelType.IfcCsgSolid => BuildCsgSolid((IIfcCsgSolid)ifcSolid),
                XSolidModelType.IfcExtrudedAreaSolid => BuildExtrudedAreaSolid((IIfcExtrudedAreaSolid)ifcSolid),
                XSolidModelType.IfcExtrudedAreaSolidTapered => BuildExtrudedAreaSolidTapered((IIfcExtrudedAreaSolidTapered)ifcSolid),
                XSolidModelType.IfcRevolvedAreaSolid => BuildRevolvedAreaSolid((IIfcRevolvedAreaSolid)ifcSolid),
                XSolidModelType.IfcRevolvedAreaSolidTapered => BuildRevolvedAreaSolidTapered((IIfcRevolvedAreaSolidTapered)ifcSolid),
                XSolidModelType.IfcSweptDiskSolid => BuildSweptDiskSolid((IIfcSweptDiskSolid)ifcSolid),
                XSolidModelType.IfcSweptDiskSolidPolygonal => BuildSweptDiskSolid((IIfcSweptDiskSolid)ifcSolid),
                XSolidModelType.IfcFixedReferenceSweptAreaSolid => BuildFixedReferenceSweptAreaSolid((IIfcFixedReferenceSweptAreaSolid)ifcSolid),
                XSolidModelType.IfcSurfaceCurveSweptAreaSolid => BuildSurfaceCurveSweptAreaSolid((IIfcSurfaceCurveSweptAreaSolid)ifcSolid),
                _ => throw new NotSupportedException(
                    $"Solid model type {solidType} is not yet implemented in the P/Invoke layer.")
            };
        }

        private IXShape BuildExtrudedAreaSolid(IIfcExtrudedAreaSolid extrudedSolid)
        {
            if (extrudedSolid.Depth <= 0)
                throw new InvalidOperationException(
                    $"Extruded area solid #{extrudedSolid.EntityLabel} has depth <= 0.");

            if (!NativeGeometryFactory.BuildDirection3d(extrudedSolid.ExtrudedDirection,
                    out double dirX, out double dirY, out double dirZ))
                throw new InvalidOperationException(
                    $"Extruded area solid #{extrudedSolid.EntityLabel} has invalid extrusion direction.");

            // Build the swept profile face
            var profileFace = (NativeFace)_modelService.ProfileFactory.BuildFace(extrudedSolid.SweptArea);

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (extrudedSolid.Position != null)
            {
                locationHandle = ((NativeGeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(extrudedSolid.Position).Handle;
            }

            int result = NativeMethods.xbim_solid_build_extruded(
                ContextHandle,
                profileFace.Handle,
                dirX, dirY, dirZ,
                extrudedSolid.Depth,
                locationHandle,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build extruded area solid #{extrudedSolid.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapSolid(shapeHandle);
        }

        private IXShape BuildExtrudedAreaSolidTapered(IIfcExtrudedAreaSolidTapered extrudedTapered)
        {
            if (extrudedTapered.Depth <= 0)
                throw new InvalidOperationException(
                    $"Extruded area solid tapered #{extrudedTapered.EntityLabel} has depth <= 0.");

            if (!NativeGeometryFactory.BuildDirection3d(extrudedTapered.ExtrudedDirection,
                    out double dirX, out double dirY, out double dirZ))
                throw new InvalidOperationException(
                    $"Extruded area solid tapered #{extrudedTapered.EntityLabel} has invalid extrusion direction.");

            // Build start and end profile faces
            var startFace = (NativeFace)_modelService.ProfileFactory.BuildFace(extrudedTapered.SweptArea);
            var endFace = (NativeFace)_modelService.ProfileFactory.BuildFace(extrudedTapered.EndSweptArea);

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (extrudedTapered.Position != null)
            {
                locationHandle = ((NativeGeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(extrudedTapered.Position).Handle;
            }

            int result = NativeMethods.xbim_solid_build_extruded_tapered(
                ContextHandle,
                startFace.Handle,
                endFace.Handle,
                dirX, dirY, dirZ,
                extrudedTapered.Depth,
                _modelService.Precision,
                locationHandle,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build extruded area solid tapered #{extrudedTapered.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapSolid(shapeHandle);
        }

        private IXShape BuildRevolvedAreaSolid(IIfcRevolvedAreaSolid revolvedSolid)
        {
            if (revolvedSolid.Angle <= 0)
                throw new InvalidOperationException(
                    $"Revolved area solid #{revolvedSolid.EntityLabel} has angle <= 0.");

            // Build the swept profile face
            var profileFace = (NativeFace)_modelService.ProfileFactory.BuildFace(revolvedSolid.SweptArea);

            // Extract axis: origin + direction from the Axis placement
            var axisPoint = NativeGeometryFactory.BuildPoint3d(revolvedSolid.Axis.Location);
            double axisOriginX = axisPoint.X, axisOriginY = axisPoint.Y, axisOriginZ = axisPoint.Z;

            if (!NativeGeometryFactory.BuildDirection3d(revolvedSolid.Axis.Axis,
                    out double axisDirX, out double axisDirY, out double axisDirZ))
                throw new InvalidOperationException(
                    $"Revolved area solid #{revolvedSolid.EntityLabel} has invalid revolution axis direction.");

            // Convert angle to radians
            double angleRadians = revolvedSolid.Angle * _modelService.RadianFactor;

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (revolvedSolid.Position != null)
            {
                locationHandle = ((NativeGeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(revolvedSolid.Position).Handle;
            }

            int result = NativeMethods.xbim_solid_build_revolved(
                ContextHandle,
                profileFace.Handle,
                axisOriginX, axisOriginY, axisOriginZ,
                axisDirX, axisDirY, axisDirZ,
                angleRadians,
                locationHandle,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build revolved area solid #{revolvedSolid.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapSolid(shapeHandle);
        }

        private IXShape BuildRevolvedAreaSolidTapered(IIfcRevolvedAreaSolidTapered revolvedTapered)
        {
            if (revolvedTapered.Angle <= 0)
                throw new InvalidOperationException(
                    $"Revolved area solid tapered #{revolvedTapered.EntityLabel} has angle <= 0.");

            // Build start and end profile faces
            var startFace = (NativeFace)_modelService.ProfileFactory.BuildFace(revolvedTapered.SweptArea);
            var endFace = (NativeFace)_modelService.ProfileFactory.BuildFace(revolvedTapered.EndSweptArea);

            // Extract axis
            var axisPoint = NativeGeometryFactory.BuildPoint3d(revolvedTapered.Axis.Location);
            double axisOriginX = axisPoint.X, axisOriginY = axisPoint.Y, axisOriginZ = axisPoint.Z;

            if (!NativeGeometryFactory.BuildDirection3d(revolvedTapered.Axis.Axis,
                    out double axisDirX, out double axisDirY, out double axisDirZ))
                throw new InvalidOperationException(
                    $"Revolved area solid tapered #{revolvedTapered.EntityLabel} has invalid revolution axis direction.");

            double angleRadians = revolvedTapered.Angle * _modelService.RadianFactor;

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (revolvedTapered.Position != null)
            {
                locationHandle = ((NativeGeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(revolvedTapered.Position).Handle;
            }

            int result = NativeMethods.xbim_solid_build_revolved_tapered(
                ContextHandle,
                startFace.Handle,
                endFace.Handle,
                axisOriginX, axisOriginY, axisOriginZ,
                axisDirX, axisDirY, axisDirZ,
                angleRadians,
                _modelService.Precision,
                locationHandle,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build revolved area solid tapered #{revolvedTapered.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapSolid(shapeHandle);
        }

        private IXShape BuildSweptDiskSolid(IIfcSweptDiskSolid sweptDisk)
        {
            if (sweptDisk.Radius <= 0)
                throw new InvalidOperationException(
                    $"Swept disk solid #{sweptDisk.EntityLabel} has radius <= 0.");

            if (sweptDisk.InnerRadius.HasValue && sweptDisk.InnerRadius.Value >= sweptDisk.Radius)
                throw new InvalidOperationException(
                    $"Swept disk solid #{sweptDisk.EntityLabel} has inner radius >= outer radius.");

            // Build the directrix wire from the Directrix curve
            // The directrix is an IIfcCurve — we need to build a wire from it.
            // This requires WireFactory which delegates to native wire building.
            throw new NotImplementedException(
                $"SweptDiskSolid #{sweptDisk.EntityLabel} requires WireFactory/CurveFactory support (TOPO-002/TOPO-006).");
        }

        private IXShape BuildFixedReferenceSweptAreaSolid(IIfcFixedReferenceSweptAreaSolid fixedRefSwept)
        {
            // This requires both WireFactory (for directrix) and SurfaceFactory (for reference surface)
            throw new NotImplementedException(
                $"FixedReferenceSweptAreaSolid #{fixedRefSwept.EntityLabel} requires WireFactory and SurfaceFactory support (TOPO-002/TOPO-006).");
        }

        private IXShape BuildSurfaceCurveSweptAreaSolid(IIfcSurfaceCurveSweptAreaSolid surfaceCurveSwept)
        {
            // This requires WireFactory (for directrix) and SurfaceFactory (for reference surface)
            throw new NotImplementedException(
                $"SurfaceCurveSweptAreaSolid #{surfaceCurveSwept.EntityLabel} requires WireFactory and SurfaceFactory support (TOPO-002/TOPO-006).");
        }

        #endregion

        #region CSG Solids

        private IXShape BuildCsgSolid(IIfcCsgSolid ifcCsgSolid)
        {
            var treeRoot = ifcCsgSolid.TreeRootExpression;

            if (treeRoot is IIfcBooleanResult boolResult)
                return _modelService.BooleanFactory.Build(boolResult);

            if (treeRoot is IIfcCsgPrimitive3D csgPrimitive)
                return Build(csgPrimitive);

            throw new NotSupportedException(
                $"CSG solid #{ifcCsgSolid.EntityLabel}: unsupported tree root expression type {treeRoot.GetType().Name}.");
        }

        #endregion

        #region Not Yet Implemented (future features)

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
                "Build(IIfcSectionedSpine) requires sweep factory support.");
        }

        #endregion
    }
}
