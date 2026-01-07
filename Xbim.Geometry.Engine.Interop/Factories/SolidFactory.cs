using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds 3D solids from IFC solid model entities (CSG primitives, extruded/revolved
    /// sweeps, tapered variants). Extracts geometric parameters from IFC entities and
    /// delegates solid construction to native OCCT operations.
    /// </summary>
    internal class SolidFactory : IXSolidFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public SolidFactory(ModelGeometryService modelService, ILogger logger)
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
            GeometryFactory.BuildAxis2Placement3d(ifcBlock.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcBlock.XLength <= 0 || ifcBlock.YLength <= 0 || ifcBlock.ZLength <= 0)
                throw new XbimGeometryServiceException(
                    $"CSG Block #{ifcBlock.EntityLabel} has zero or negative dimensions.");

            int result = XbimGeometryNativeApi.xbim_solid_build_block(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcBlock.XLength, ifcBlock.YLength, ifcBlock.ZLength,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build CSG Block #{ifcBlock.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        private IXSolid BuildSphere(IIfcSphere ifcSphere)
        {
            GeometryFactory.BuildAxis2Placement3d(ifcSphere.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcSphere.Radius <= 0)
                throw new XbimGeometryServiceException(
                    $"CSG Sphere #{ifcSphere.EntityLabel} has zero or negative radius.");

            int result = XbimGeometryNativeApi.xbim_solid_build_sphere(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcSphere.Radius,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build CSG Sphere #{ifcSphere.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        private IXSolid BuildRightCircularCylinder(IIfcRightCircularCylinder ifcCylinder)
        {
            GeometryFactory.BuildAxis2Placement3d(ifcCylinder.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcCylinder.Radius <= 0 || ifcCylinder.Height <= 0)
                throw new XbimGeometryServiceException(
                    $"CSG RightCircularCylinder #{ifcCylinder.EntityLabel} has zero or negative dimensions.");

            int result = XbimGeometryNativeApi.xbim_solid_build_right_circular_cylinder(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcCylinder.Radius, ifcCylinder.Height,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build CSG RightCircularCylinder #{ifcCylinder.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        private IXSolid BuildRightCircularCone(IIfcRightCircularCone ifcCone)
        {
            GeometryFactory.BuildAxis2Placement3d(ifcCone.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcCone.BottomRadius <= 0 || ifcCone.Height <= 0)
                throw new XbimGeometryServiceException(
                    $"CSG RightCircularCone #{ifcCone.EntityLabel} has zero or negative dimensions.");

            int result = XbimGeometryNativeApi.xbim_solid_build_right_circular_cone(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcCone.BottomRadius, ifcCone.Height,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build CSG RightCircularCone #{ifcCone.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        private IXSolid BuildRectangularPyramid(IIfcRectangularPyramid ifcPyramid)
        {
            GeometryFactory.BuildAxis2Placement3d(ifcPyramid.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            if (ifcPyramid.XLength <= 0 || ifcPyramid.YLength <= 0 || ifcPyramid.Height <= 0)
                throw new XbimGeometryServiceException(
                    $"CSG RectangularPyramid #{ifcPyramid.EntityLabel} has zero or negative dimensions.");

            int result = XbimGeometryNativeApi.xbim_solid_build_rectangular_pyramid(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ifcPyramid.XLength, ifcPyramid.YLength, ifcPyramid.Height,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build CSG RectangularPyramid #{ifcPyramid.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
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
                XSolidModelType.IfcFacetedBrep => Build((IIfcFacetedBrep)ifcSolid),
                XSolidModelType.IfcFacetedBrepWithVoids => BuildFacetedBrepWithVoids((IIfcFacetedBrepWithVoids)ifcSolid),
                XSolidModelType.IfcAdvancedBrep => BuildAdvancedBrep((IIfcAdvancedBrep)ifcSolid),
                XSolidModelType.IfcAdvancedBrepWithVoids => BuildAdvancedBrepWithVoids((IIfcAdvancedBrepWithVoids)ifcSolid),
                _ => throw new NotSupportedException(
                    $"Solid model type {solidType} is not yet implemented in the P/Invoke layer.")
            };
        }

        private IXShape BuildExtrudedAreaSolid(IIfcExtrudedAreaSolid extrudedSolid)
        {
            if (extrudedSolid.Depth <= 0)
                throw new XbimGeometryServiceException(
                    $"Extruded area solid #{extrudedSolid.EntityLabel} has depth <= 0.");

            if (!GeometryFactory.BuildDirection3d(extrudedSolid.ExtrudedDirection,
                    out double dirX, out double dirY, out double dirZ))
                throw new XbimGeometryServiceException(
                    $"Extruded area solid #{extrudedSolid.EntityLabel} has invalid extrusion direction.");

            // Build the swept profile face
            var profileFace = (XbimFace)_modelService.ProfileFactory.BuildFace(extrudedSolid.SweptArea);

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (extrudedSolid.Position != null)
            {
                locationHandle = ((GeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(extrudedSolid.Position).Handle;
            }

            int result = XbimGeometryNativeApi.xbim_solid_build_extruded(
                ContextHandle,
                profileFace.Handle,
                dirX, dirY, dirZ,
                extrudedSolid.Depth,
                locationHandle,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build extruded area solid #{extrudedSolid.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        private IXShape BuildExtrudedAreaSolidTapered(IIfcExtrudedAreaSolidTapered extrudedTapered)
        {
            if (extrudedTapered.Depth <= 0)
                throw new XbimGeometryServiceException(
                    $"Extruded area solid tapered #{extrudedTapered.EntityLabel} has depth <= 0.");

            if (!GeometryFactory.BuildDirection3d(extrudedTapered.ExtrudedDirection,
                    out double dirX, out double dirY, out double dirZ))
                throw new XbimGeometryServiceException(
                    $"Extruded area solid tapered #{extrudedTapered.EntityLabel} has invalid extrusion direction.");

            // Build start and end profile faces
            var startFace = (XbimFace)_modelService.ProfileFactory.BuildFace(extrudedTapered.SweptArea);
            var endFace = (XbimFace)_modelService.ProfileFactory.BuildFace(extrudedTapered.EndSweptArea);

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (extrudedTapered.Position != null)
            {
                locationHandle = ((GeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(extrudedTapered.Position).Handle;
            }

            int result = XbimGeometryNativeApi.xbim_solid_build_extruded_tapered(
                ContextHandle,
                startFace.Handle,
                endFace.Handle,
                dirX, dirY, dirZ,
                extrudedTapered.Depth,
                _modelService.Precision,
                locationHandle,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build extruded area solid tapered #{extrudedTapered.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        private IXShape BuildRevolvedAreaSolid(IIfcRevolvedAreaSolid revolvedSolid)
        {
            if (revolvedSolid.Angle <= 0)
                throw new XbimGeometryServiceException(
                    $"Revolved area solid #{revolvedSolid.EntityLabel} has angle <= 0.");

            // Build the swept profile face
            var profileFace = (XbimFace)_modelService.ProfileFactory.BuildFace(revolvedSolid.SweptArea);

            // Extract axis: origin + direction from the Axis placement
            var axisPoint = GeometryFactory.BuildPoint3d(revolvedSolid.Axis.Location);
            double axisOriginX = axisPoint.X, axisOriginY = axisPoint.Y, axisOriginZ = axisPoint.Z;

            if (!GeometryFactory.BuildDirection3d(revolvedSolid.Axis.Axis,
                    out double axisDirX, out double axisDirY, out double axisDirZ))
                throw new XbimGeometryServiceException(
                    $"Revolved area solid #{revolvedSolid.EntityLabel} has invalid revolution axis direction.");

            // Convert angle to radians
            double angleRadians = revolvedSolid.Angle * _modelService.RadianFactor;

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (revolvedSolid.Position != null)
            {
                locationHandle = ((GeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(revolvedSolid.Position).Handle;
            }

            int result = XbimGeometryNativeApi.xbim_solid_build_revolved(
                ContextHandle,
                profileFace.Handle,
                axisOriginX, axisOriginY, axisOriginZ,
                axisDirX, axisDirY, axisDirZ,
                angleRadians,
                locationHandle,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build revolved area solid #{revolvedSolid.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        private IXShape BuildRevolvedAreaSolidTapered(IIfcRevolvedAreaSolidTapered revolvedTapered)
        {
            if (revolvedTapered.Angle <= 0)
                throw new XbimGeometryServiceException(
                    $"Revolved area solid tapered #{revolvedTapered.EntityLabel} has angle <= 0.");

            // Build start and end profile faces
            var startFace = (XbimFace)_modelService.ProfileFactory.BuildFace(revolvedTapered.SweptArea);
            var endFace = (XbimFace)_modelService.ProfileFactory.BuildFace(revolvedTapered.EndSweptArea);

            // Extract axis
            var axisPoint = GeometryFactory.BuildPoint3d(revolvedTapered.Axis.Location);
            double axisOriginX = axisPoint.X, axisOriginY = axisPoint.Y, axisOriginZ = axisPoint.Z;

            if (!GeometryFactory.BuildDirection3d(revolvedTapered.Axis.Axis,
                    out double axisDirX, out double axisDirY, out double axisDirZ))
                throw new XbimGeometryServiceException(
                    $"Revolved area solid tapered #{revolvedTapered.EntityLabel} has invalid revolution axis direction.");

            double angleRadians = revolvedTapered.Angle * _modelService.RadianFactor;

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (revolvedTapered.Position != null)
            {
                locationHandle = ((GeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(revolvedTapered.Position).Handle;
            }

            int result = XbimGeometryNativeApi.xbim_solid_build_revolved_tapered(
                ContextHandle,
                startFace.Handle,
                endFace.Handle,
                axisOriginX, axisOriginY, axisOriginZ,
                axisDirX, axisDirY, axisDirZ,
                angleRadians,
                _modelService.Precision,
                locationHandle,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build revolved area solid tapered #{revolvedTapered.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        private IXShape BuildSweptDiskSolid(IIfcSweptDiskSolid sweptDisk)
        {
            if (sweptDisk.Radius <= 0)
                throw new XbimGeometryServiceException(
                    $"Swept disk solid #{sweptDisk.EntityLabel} has radius <= 0.");

            if (sweptDisk.InnerRadius.HasValue && sweptDisk.InnerRadius.Value >= sweptDisk.Radius)
                throw new XbimGeometryServiceException(
                    $"Swept disk solid #{sweptDisk.EntityLabel} has inner radius >= outer radius.");

            // Build the directrix wire, applying optional parametric trimming
            var wireFactory = (WireFactory)_modelService.WireFactory;
            double? startParam = sweptDisk.StartParam.HasValue ? (double)sweptDisk.StartParam.Value : null;
            double? endParam = sweptDisk.EndParam.HasValue ? (double)sweptDisk.EndParam.Value : null;
            using var directrixWire = (XbimWire)wireFactory.BuildDirectrixWire(sweptDisk.Directrix, startParam, endParam);

            double innerRadius = sweptDisk.InnerRadius ?? 0;

            // For polygonal swept disks, fillet the directrix wire at vertices
            using var filletedWire = FilletIfPolygonal(sweptDisk, directrixWire);
            var sweepWire = filletedWire ?? directrixWire;

            int result = XbimGeometryNativeApi.xbim_solid_build_swept_disk(
                _modelService.ContextHandle,
                sweepWire.Handle,
                sweptDisk.Radius,
                innerRadius,
                out NativeShapeHandle solidHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build swept disk solid #{sweptDisk.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(solidHandle);
        }

        private XbimWire FilletIfPolygonal(IIfcSweptDiskSolid sweptDisk, XbimWire directrixWire)
        {
            if (sweptDisk is not IIfcSweptDiskSolidPolygonal polygonal ||
                !polygonal.FilletRadius.HasValue || polygonal.FilletRadius.Value <= 0)
                return null;

            int filletResult = XbimGeometryNativeApi.xbim_wire_fillet(
                _modelService.ContextHandle,
                directrixWire.Handle,
                polygonal.FilletRadius.Value,
                _modelService.Precision,
                out NativeShapeHandle filletedHandle);

            if (filletResult == 0)
                return new XbimWire(filletedHandle);

            _logger.LogWarning("Failed to fillet directrix for SweptDiskSolidPolygonal #{EntityLabel}: {Error}",
                polygonal.EntityLabel, XbimGeometryNativeApi.GetLastError());
            return null;
        }

        private IXShape BuildFixedReferenceSweptAreaSolid(IIfcFixedReferenceSweptAreaSolid fixedRefSwept)
        {
            // Composite profiles: build a solid per sub-profile, combine into compound
            if (fixedRefSwept.SweptArea is IIfcCompositeProfileDef compositeProfile)
            {
                var solidHandles = new List<NativeShapeHandle>();
                try
                {
                    foreach (var profileDef in compositeProfile.Profiles)
                        solidHandles.Add(BuildFixedReferenceSweptAreaSolidCore(fixedRefSwept, profileDef));

                    using var nativeHandles = new NativeHandleArray(solidHandles.ToArray());

                    int result = XbimGeometryNativeApi.xbim_compound_make(
                        ContextHandle,
                        nativeHandles.Ptrs,
                        nativeHandles.Length,
                        out var compoundHandle);

                    if (result != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build compound for FixedReferenceSweptAreaSolid #{fixedRefSwept.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return NativeShapeWrapper.WrapShape(compoundHandle);
                }
                finally
                {
                    foreach (var h in solidHandles)
                        h.Dispose();
                }
            }

            var solidResult = BuildFixedReferenceSweptAreaSolidCore(fixedRefSwept, fixedRefSwept.SweptArea);
            return NativeShapeWrapper.WrapSolid(solidResult);
        }

        private NativeShapeHandle BuildFixedReferenceSweptAreaSolidCore(
            IIfcFixedReferenceSweptAreaSolid fixedRefSwept, IIfcProfileDef profileDef)
        {
            if (profileDef.ProfileType != IfcProfileTypeEnum.AREA)
                throw new XbimGeometryServiceException(
                    $"FixedReferenceSweptAreaSolid #{fixedRefSwept.EntityLabel}: profile must be AREA type.");

            // Build the swept area profile face
            using var profileFace = (XbimFace)_modelService.ProfileFactory.BuildFace(profileDef);

            // Extract fixed reference direction — this becomes the reference plane normal
            if (!GeometryFactory.BuildDirection3d(fixedRefSwept.FixedReference, out double refDirX, out double refDirY, out double refDirZ))
                throw new XbimGeometryServiceException(
                    $"FixedReferenceSweptAreaSolid #{fixedRefSwept.EntityLabel}: FixedReference direction has zero magnitude.");

            // Build the directrix wire with optional parametric trimming
            var wireFactory = (WireFactory)_modelService.WireFactory;
            double? startParam = fixedRefSwept.StartParam.HasValue ? (double)fixedRefSwept.StartParam.Value : null;
            double? endParam = fixedRefSwept.EndParam.HasValue ? (double)fixedRefSwept.EndParam.Value : null;
            using var directrixWire = (XbimWire)wireFactory.BuildDirectrixWire(fixedRefSwept.Directrix, startParam, endParam);

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (fixedRefSwept.Position != null)
            {
                locationHandle = ((GeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(fixedRefSwept.Position).Handle;
            }

            // Reference surface is a plane with normal = FixedReference direction.
            // Origin at (0,0,0) — for an infinite plane, OCCT projects the directrix onto it regardless.
            int result = XbimGeometryNativeApi.xbim_solid_build_fixed_reference_swept(
                ContextHandle,
                profileFace.Handle,
                directrixWire.Handle,
                0.0, 0.0, 0.0,
                refDirX, refDirY, refDirZ,
                1, // isPlanarReferenceSurface — always planar for fixed reference
                _modelService.Precision,
                locationHandle,
                out var solidHandle);

            if (result != 0)
                throw new XbimGeometryFactoryException(
                    $"Failed to build FixedReferenceSweptAreaSolid #{fixedRefSwept.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return solidHandle;
        }

        private IXShape BuildSurfaceCurveSweptAreaSolid(IIfcSurfaceCurveSweptAreaSolid surfaceCurveSwept)
        {
            // Composite profiles: build a solid per sub-profile, combine into compound
            if (surfaceCurveSwept.SweptArea is IIfcCompositeProfileDef compositeProfile)
            {
                var solidHandles = new List<NativeShapeHandle>();
                try
                {
                    foreach (var profileDef in compositeProfile.Profiles)
                        solidHandles.Add(BuildSurfaceCurveSweptAreaSolidCore(surfaceCurveSwept, profileDef));

                    using var nativeHandles = new NativeHandleArray(solidHandles.ToArray());

                    int result = XbimGeometryNativeApi.xbim_compound_make(
                        ContextHandle,
                        nativeHandles.Ptrs,
                        nativeHandles.Length,
                        out var compoundHandle);

                    if (result != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build compound for SurfaceCurveSweptAreaSolid #{surfaceCurveSwept.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return NativeShapeWrapper.WrapShape(compoundHandle);
                }
                finally
                {
                    foreach (var h in solidHandles)
                        h.Dispose();
                }
            }

            var solidResult = BuildSurfaceCurveSweptAreaSolidCore(surfaceCurveSwept, surfaceCurveSwept.SweptArea);
            return NativeShapeWrapper.WrapSolid(solidResult);
        }

        private NativeShapeHandle BuildSurfaceCurveSweptAreaSolidCore(
            IIfcSurfaceCurveSweptAreaSolid surfaceCurveSwept, IIfcProfileDef profileDef)
        {
            if (profileDef.ProfileType != IfcProfileTypeEnum.AREA)
                throw new XbimGeometryServiceException(
                    $"SurfaceCurveSweptAreaSolid #{surfaceCurveSwept.EntityLabel}: profile must be AREA type.");

            // Build the swept area profile face
            using var profileFace = (XbimFace)_modelService.ProfileFactory.BuildFace(profileDef);

            // Build the reference surface 
            var refSurface = (Surface)_modelService.SurfaceFactory.Build(surfaceCurveSwept.ReferenceSurface);
            bool isPlanar = refSurface.SurfaceType == XSurfaceType.IfcPlane;

            // Build the directrix wire with optional parametric trimming
            var wireFactory = (WireFactory)_modelService.WireFactory;
            double? startParam = surfaceCurveSwept.StartParam.HasValue ? (double)surfaceCurveSwept.StartParam.Value : null;
            double? endParam = surfaceCurveSwept.EndParam.HasValue ? (double)surfaceCurveSwept.EndParam.Value : null;
            using var directrixWire = (XbimWire)wireFactory.BuildDirectrixWire(surfaceCurveSwept.Directrix, startParam, endParam);

            // Build optional position location (NullHandle = identity)
            var locationHandle = NativeLocationHandle.NullHandle;
            if (surfaceCurveSwept.Position != null)
            {
                locationHandle = ((GeometryFactory)_modelService.GeometryFactory)
                    .BuildLocationFromAxis3D(surfaceCurveSwept.Position).Handle;
            }

            int result = XbimGeometryNativeApi.xbim_solid_build_surface_curve_swept(
                ContextHandle,
                profileFace.Handle,
                directrixWire.Handle,
                refSurface.Handle,
                isPlanar ? 1 : 0,
                _modelService.Precision,
                locationHandle,
                out var solidHandle);

            if (result != 0)
                throw new XbimGeometryFactoryException(
                    $"Failed to build SurfaceCurveSweptAreaSolid #{surfaceCurveSwept.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return solidHandle;
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

        #region Faceted BRep

        public IXShape Build(IIfcFacetedBrep ifcBrep)
        {
            var solidHandle = BuildClosedShellAsSolid(ifcBrep.Outer);
            return NativeShapeWrapper.WrapShape(solidHandle);
        }

        private IXShape BuildFacetedBrepWithVoids(IIfcFacetedBrepWithVoids ifcBrep)
        {
            // Build the outer solid
            var outerHandle = BuildClosedShellAsSolid(ifcBrep.Outer);
            double fuzzyTol = _modelService.Precision * 10;

            // Cut each void shell from the outer solid
            foreach (var voidShell in ifcBrep.Voids)
            {
                NativeShapeHandle voidSolidHandle;
                try
                {
                    voidSolidHandle = BuildClosedShellAsSolid(voidShell);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Skipping void shell: {Message}", ex.Message);
                    continue;
                }

                int result = XbimGeometryNativeApi.xbim_boolean_cut(
                    ContextHandle, outerHandle, voidSolidHandle,
                    fuzzyTol, out _, out var cutHandle);

                voidSolidHandle.Dispose();

                if (result != 0)
                {
                    _logger.LogWarning("Boolean cut for void failed: {Error}",
                        XbimGeometryNativeApi.GetLastError());
                    continue;
                }

                outerHandle.Dispose();
                outerHandle = cutHandle;
            }

            return NativeShapeWrapper.WrapShape(outerHandle);
        }

        /// <summary>
        /// Builds a closed shell from an IIfcConnectedFaceSet (typically IIfcClosedShell)
        /// by constructing planar faces from polygon loops, sewing them together to merge
        /// shared edges, and converting to a solid.
        /// </summary>
        private NativeShapeHandle BuildClosedShellAsSolid(IIfcConnectedFaceSet faceSet)
        {
            double tolerance = _modelService.MinimumGap;
            var faceHandles = new List<NativeShapeHandle>();

            try
            {
                // Build each face from its polygon bounds
                foreach (var ifcFace in faceSet.CfsFaces)
                {
                    var faceHandle = BuildPlanarFace(ifcFace, tolerance);
                    if (faceHandle != null && !faceHandle.IsInvalid)
                        faceHandles.Add(faceHandle);
                }

                if (faceHandles.Count < 4)
                    throw new XbimGeometryServiceException(
                        $"Closed shell requires at least 4 faces but only {faceHandles.Count} were built.");

                // Use the combined sew+solid API which merges shared edges
                using var nativeFaces = new NativeHandleArray(faceHandles.ToArray());

                int result = XbimGeometryNativeApi.xbim_shell_build_closed_shell(
                    ContextHandle, nativeFaces.Ptrs, nativeFaces.Length, tolerance, out var solidHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build closed shell solid: {XbimGeometryNativeApi.GetLastError()}");

                return solidHandle;
            }
            finally
            {
                foreach (var h in faceHandles)
                    h.Dispose();
            }
        }

        /// <summary>
        /// Builds a planar face from an IIfcFace by extracting polygon loops from its bounds.
        /// The outer bound becomes the face wire; inner bounds become void wires.
        /// </summary>
        private NativeShapeHandle? BuildPlanarFace(IIfcFace ifcFace, double tolerance)
        {
            NativeShapeHandle? outerWireHandle = null;
            var innerWireHandles = new List<NativeShapeHandle>();

            try
            {
                foreach (var bound in ifcFace.Bounds)
                {
                    if (bound.Bound is not IIfcPolyLoop polyLoop)
                    {
                        _logger.LogWarning(
                            "Face bound #{Label} is not an IIfcPolyLoop, skipping.",
                            bound.EntityLabel);
                        continue;
                    }

                    var wireHandle = BuildWireFromPolyLoop(polyLoop, bound.Orientation);
                    if (wireHandle == null || wireHandle.IsInvalid)
                        continue;

                    if (bound is IIfcFaceOuterBound)
                    {
                        outerWireHandle?.Dispose();
                        outerWireHandle = wireHandle;
                    }
                    else
                    {
                        innerWireHandles.Add(wireHandle);
                    }
                }

                // If no explicit outer bound, use the first bound as outer
                if (outerWireHandle == null && innerWireHandles.Count > 0)
                {
                    outerWireHandle = innerWireHandles[0];
                    innerWireHandles.RemoveAt(0);
                }

                if (outerWireHandle == null)
                    return null;

                // Build face from outer wire (with inner wires if any)
                NativeShapeHandle faceHandle;
                if (innerWireHandles.Count == 0)
                {
                    int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                        ContextHandle, outerWireHandle, out faceHandle);

                    if (result != 0)
                    {
                        _logger.LogWarning("Failed to build face from wire: {Error}",
                            XbimGeometryNativeApi.GetLastError());
                        return null;
                    }
                }
                else
                {
                    // Use advanced face build with inner wires (voids)
                    using var nativeInnerWires = new NativeHandleArray(innerWireHandles.ToArray());

                    // surfaceType 0 = PLANE, sameSense 1 = true
                    // For planar faces inferred from wire, pass zero placement — the native
                    // code will infer the plane from the outer wire
                    int result = XbimGeometryNativeApi.xbim_face_build_advanced(
                        ContextHandle,
                        0, // PLANE
                        0, 0, 0, // origin (ignored for inferred plane)
                        0, 0, 1, // zDir
                        1, 0, 0, // xDir
                        0,       // radius (N/A for plane)
                        outerWireHandle,
                        nativeInnerWires.Ptrs,
                        nativeInnerWires.Length,
                        tolerance,
                        1, // sameSense
                        out faceHandle);

                    if (result != 0)
                    {
                        _logger.LogWarning("Failed to build advanced face with voids: {Error}",
                            XbimGeometryNativeApi.GetLastError());
                        return null;
                    }
                }

                return faceHandle;
            }
            finally
            {
                outerWireHandle?.Dispose();
                foreach (var h in innerWireHandles)
                    h.Dispose();
            }
        }

        /// <summary>
        /// Builds a closed polygon wire from an IIfcPolyLoop's vertices.
        /// </summary>
        private NativeShapeHandle? BuildWireFromPolyLoop(IIfcPolyLoop polyLoop, bool orientation)
        {
            var polygon = polyLoop.Polygon;
            if (polygon == null || polygon.Count < 3)
                return null;

            int count = polygon.Count;
            var coords = new double[count * 3];

            // Extract XYZ coordinates; if orientation is reversed, reverse the point order
            if (orientation)
            {
                for (int i = 0; i < count; i++)
                {
                    var pt = polygon[i];
                    coords[i * 3] = pt.X;
                    coords[i * 3 + 1] = pt.Y;
                    coords[i * 3 + 2] = pt.Z;
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var pt = polygon[count - 1 - i];
                    coords[i * 3] = pt.X;
                    coords[i * 3 + 1] = pt.Y;
                    coords[i * 3 + 2] = pt.Z;
                }
            }

            int result = XbimGeometryNativeApi.xbim_wire_build_polygon(
                ContextHandle, coords, count, 1, // closed = true
                out var wireHandle);

            if (result != 0)
            {
                _logger.LogWarning("Failed to build polygon wire: {Error}",
                    XbimGeometryNativeApi.GetLastError());
                return null;
            }

            return wireHandle;
        }

        #endregion

        #region Advanced BRep

        private IXShape BuildAdvancedBrep(IIfcAdvancedBrep ifcBrep)
        {
            var solidHandle = BuildAdvancedShellAsSolid(ifcBrep.Outer);
            return NativeShapeWrapper.WrapShape(solidHandle);
        }

        private IXShape BuildAdvancedBrepWithVoids(IIfcAdvancedBrepWithVoids ifcBrep)
        {
            var outerHandle = BuildAdvancedShellAsSolid(ifcBrep.Outer);
            double fuzzyTol = _modelService.Precision * 10;

            foreach (var voidShell in ifcBrep.Voids)
            {
                NativeShapeHandle voidSolidHandle;
                try
                {
                    voidSolidHandle = BuildAdvancedShellAsSolid(voidShell);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Skipping void shell in AdvancedBrepWithVoids: {Message}", ex.Message);
                    continue;
                }

                int result = XbimGeometryNativeApi.xbim_boolean_cut(
                    ContextHandle, outerHandle, voidSolidHandle,
                    fuzzyTol, out _, out var cutHandle);

                voidSolidHandle.Dispose();

                if (result != 0)
                {
                    _logger.LogWarning("Boolean cut for AdvancedBrep void failed: {Error}",
                        XbimGeometryNativeApi.GetLastError());
                    continue;
                }

                outerHandle.Dispose();
                outerHandle = cutHandle;
            }

            return NativeShapeWrapper.WrapShape(outerHandle);
        }

        /// <summary>
        /// Builds a solid from an IIfcClosedShell whose faces are IIfcAdvancedFace entities.
        /// </summary>
        private NativeShapeHandle BuildAdvancedShellAsSolid(IIfcClosedShell closedShell)
        {
            double tolerance = _modelService.MinimumGap;
            var curveCache = new Dictionary<int, NativeCurveHandle>();

            int result = XbimGeometryNativeApi.xbim_advanced_brep_create(
                ContextHandle, tolerance, out var builder);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to create advanced BRep builder: {XbimGeometryNativeApi.GetLastError()}");

            try
            {
                // Pass 1: Register all edge curves and vertices
                var registeredEdges = new HashSet<int>();
                var registeredVertices = new HashSet<int>();

                var edgeCurves = closedShell.CfsFaces
                    .OfType<IIfcAdvancedFace>()
                    .SelectMany(f => f.Bounds)
                    .Where(b => b.Bound is IIfcEdgeLoop)
                    .SelectMany(b => ((IIfcEdgeLoop)b.Bound).EdgeList)
                    .Select(oe => oe.EdgeElement)
                    .OfType<IIfcEdgeCurve>();

                foreach (var edgeCurve in edgeCurves)
                {
                    int edgeLabel = edgeCurve.EntityLabel;

                    // Register edge curve geometry (deduplicated)
                    if (registeredEdges.Add(edgeLabel))
                    {
                        var curveHandle = BuildCurveForEdge(edgeCurve.EdgeGeometry, curveCache);
                        if (curveHandle != null && !curveHandle.IsInvalid)
                        {
                            if (!edgeCurve.SameSense)
                            {
                                int rev = XbimGeometryNativeApi.xbim_curve_reverse(curveHandle);
                                if (rev != 0)
                                    _logger.LogWarning("Failed to reverse edge curve #{Label}: {Error}",
                                        edgeLabel, XbimGeometryNativeApi.GetLastError());
                            }

                            XbimGeometryNativeApi.xbim_advanced_brep_add_edge_curve(
                                builder, edgeLabel, curveHandle);
                        }
                    }

                    // Register start vertex
                    if (edgeCurve.EdgeStart is IIfcVertexPoint startVP &&
                        startVP.VertexGeometry is IIfcCartesianPoint startPt)
                    {
                        int svLabel = startVP.EntityLabel;
                        if (registeredVertices.Add(svLabel))
                        {
                            XbimGeometryNativeApi.xbim_advanced_brep_add_vertex(
                                builder, svLabel, startPt.X, startPt.Y, startPt.Z);
                        }
                    }

                    // Register end vertex
                    if (edgeCurve.EdgeEnd is IIfcVertexPoint endVP &&
                        endVP.VertexGeometry is IIfcCartesianPoint endPt)
                    {
                        int evLabel = endVP.EntityLabel;
                        if (registeredVertices.Add(evLabel))
                        {
                            XbimGeometryNativeApi.xbim_advanced_brep_add_vertex(
                                builder, evLabel, endPt.X, endPt.Y, endPt.Z);
                        }
                    }
                }

                // Pass 2: Define faces with their bounds and oriented edges
                int faceCount = 0;
                foreach (var ifcFace in closedShell.CfsFaces)
                {
                    if (ifcFace is not IIfcAdvancedFace advancedFace)
                    {
                        _logger.LogWarning("Face #{Label} is not IIfcAdvancedFace, skipping.",
                            ifcFace.EntityLabel);
                        continue;
                    }

                    // Build the face surface
                    var surfaceHandle = BuildSurfaceForAdvancedFace(advancedFace.FaceSurface);
                    if (surfaceHandle == null || surfaceHandle.IsInvalid)
                    {
                        _logger.LogWarning("Failed to build surface for AdvancedFace #{Label}, skipping.",
                            advancedFace.EntityLabel);
                        surfaceHandle?.Dispose();
                        continue;
                    }

                    result = XbimGeometryNativeApi.xbim_advanced_brep_begin_face(
                        builder, surfaceHandle, advancedFace.SameSense ? 1 : 0);
                    surfaceHandle.Dispose();

                    if (result != 0)
                    {
                        _logger.LogWarning("Failed to begin face #{Label}: {Error}",
                            advancedFace.EntityLabel, XbimGeometryNativeApi.GetLastError());
                        continue;
                    }

                    int numberOfBounds = advancedFace.Bounds.Count;
                    foreach (var bound in advancedFace.Bounds)
                    {
                        if (bound.Bound is not IIfcEdgeLoop edgeLoop)
                        {
                            _logger.LogWarning(
                                "AdvancedFace #{FaceLabel} bound #{BoundLabel} is not an IIfcEdgeLoop, skipping.",
                                advancedFace.EntityLabel, bound.EntityLabel);
                            continue;
                        }

                        bool isOuter = numberOfBounds == 1 || bound is IIfcFaceOuterBound;

                        XbimGeometryNativeApi.xbim_advanced_brep_begin_bound(
                            builder, isOuter ? 1 : 0, bound.Orientation ? 1 : 0);

                        foreach (var orientedEdge in edgeLoop.EdgeList)
                        {
                            if (orientedEdge.EdgeElement is not IIfcEdgeCurve edgeCurve)
                                continue;

                            var startVP = edgeCurve.EdgeStart as IIfcVertexPoint;
                            var endVP = edgeCurve.EdgeEnd as IIfcVertexPoint;
                            if (startVP == null || endVP == null)
                                continue;

                            XbimGeometryNativeApi.xbim_advanced_brep_add_bound_edge(
                                builder,
                                edgeCurve.EntityLabel,
                                startVP.EntityLabel,
                                endVP.EntityLabel,
                                orientedEdge.Orientation ? 1 : 0);
                        }

                        XbimGeometryNativeApi.xbim_advanced_brep_end_bound(builder);
                    }

                    XbimGeometryNativeApi.xbim_advanced_brep_end_face(builder);
                    faceCount++;
                }

                if (faceCount < 4)
                    throw new XbimGeometryServiceException(
                        $"Advanced BRep closed shell requires at least 4 faces but only {faceCount} were built.");

                // Build the complete BRep topology natively
                result = XbimGeometryNativeApi.xbim_advanced_brep_build(builder, out var solidHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build advanced BRep solid: {XbimGeometryNativeApi.GetLastError()}");

                return solidHandle;
            }
            finally
            {
                builder.Dispose();
                foreach (var h in curveCache.Values)
                    h.Dispose();
            }
        }

        /// <summary>
        /// Builds a curve handle from an IFC curve entity for edge curve registration.
        /// </summary>
        private NativeCurveHandle? BuildCurveForEdge(IIfcCurve ifcCurve, Dictionary<int, NativeCurveHandle> curveCache)
        {
            int curveLabel = ifcCurve.EntityLabel;
            if (curveCache.TryGetValue(curveLabel, out var cached))
                return NativeCurveHandle.Borrowed(cached);

            try
            {
                var curveFactory = (CurveFactory)_modelService.CurveFactory;
                var curve = (XbimCurve)curveFactory.Build(ifcCurve);
                var curveHandle = curve.DetachHandle();
                curveCache[curveLabel] = curveHandle;
                return curveHandle;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cannot build curve #{Label} ({Type}): {Error}",
                    ifcCurve.EntityLabel, ifcCurve.ExpressType.ExpressName, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Builds a surface handle from an IFC surface entity for advanced face construction.
        /// </summary>
        private NativeSurfaceHandle? BuildSurfaceForAdvancedFace(IIfcSurface ifcSurface)
        {
            try
            {
                var surfaceFactory = (SurfaceFactory)_modelService.SurfaceFactory;
                var surface = (Surface)surfaceFactory.Build(ifcSurface);
                return surface.DetachHandle();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cannot build surface #{Label} ({Type}): {Error}",
                    ifcSurface.EntityLabel, ifcSurface.ExpressType.ExpressName, ex.Message);
                return null;
            }
        }

        #endregion

        #region Face-Based Surface Model

        /// <summary>
        /// Builds a compound of shells from a face-based surface model by constructing
        /// each connected face set as a sewn shell and combining them.
        /// </summary>
        public IXShape Build(IIfcFaceBasedSurfaceModel ifcSurfaceModel)
        {
            double tolerance = _modelService.Precision;
            var shellHandles = new List<NativeShapeHandle>();

            try
            {
                foreach (var faceSet in ifcSurfaceModel.FbsmFaces)
                {
                    var shellHandle = BuildShellFromConnectedFaceSet(faceSet, tolerance);
                    if (shellHandle != null && !shellHandle.IsInvalid)
                        shellHandles.Add(shellHandle);
                }

                if (shellHandles.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"FaceBasedSurfaceModel #{ifcSurfaceModel.EntityLabel}: no valid shells were built.");

                if (shellHandles.Count == 1)
                {
                    // WrapShape takes ownership — remove from list so finally won't dispose it
                    var handle = shellHandles[0];
                    shellHandles.Clear();
                    return NativeShapeWrapper.WrapShape(handle);
                }

                // Assemble multiple shells into a compound
                using var nativeShells = new NativeHandleArray(shellHandles.ToArray());

                int result = XbimGeometryNativeApi.xbim_compound_make(
                    ContextHandle, nativeShells.Ptrs, nativeShells.Length, out var compoundHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"FaceBasedSurfaceModel #{ifcSurfaceModel.EntityLabel}: failed to create compound: " +
                        XbimGeometryNativeApi.GetLastError());

                return NativeShapeWrapper.WrapShape(compoundHandle);
            }
            finally
            {
                foreach (var h in shellHandles)
                    h.Dispose();
            }
        }

        /// <summary>
        /// Builds a sewn shell from a connected face set by constructing planar faces
        /// from polygon loops and sewing them together.
        /// </summary>
        private NativeShapeHandle? BuildShellFromConnectedFaceSet(
            IIfcConnectedFaceSet faceSet, double tolerance)
        {
            var faceHandles = new List<NativeShapeHandle>();

            try
            {
                foreach (var ifcFace in faceSet.CfsFaces)
                {
                    var faceHandle = BuildPlanarFace(ifcFace, tolerance);
                    if (faceHandle != null && !faceHandle.IsInvalid)
                        faceHandles.Add(faceHandle);
                }

                if (faceHandles.Count == 0)
                {
                    _logger.LogWarning("Connected face set produced no valid faces.");
                    return null;
                }

                using var nativeFaces = new NativeHandleArray(faceHandles.ToArray());

                // Build raw shell from faces
                int result = XbimGeometryNativeApi.xbim_shell_build_from_faces(
                    ContextHandle, nativeFaces.Ptrs, nativeFaces.Length, tolerance, out var rawShellHandle);

                if (result != 0)
                {
                    _logger.LogWarning("Failed to build shell from connected face set: {Error}",
                        XbimGeometryNativeApi.GetLastError());
                    return null;
                }

                // Sew the shell to merge shared edges and fix orientation
                result = XbimGeometryNativeApi.xbim_shell_sew(
                    ContextHandle, rawShellHandle, tolerance, out _, out var sewedHandle);

                rawShellHandle.Dispose();

                if (result != 0)
                {
                    _logger.LogWarning("Failed to sew shell: {Error}",
                        XbimGeometryNativeApi.GetLastError());
                    return null;
                }

                return sewedHandle;
            }
            finally
            {
                foreach (var h in faceHandles)
                    h.Dispose();
            }
        }

        #endregion

        #region Not Yet Implemented (future features)

        public IXSolid Build(IIfcHalfSpaceSolid ifcHalfSpaceSolid)
        {
            return BuildHalfSpace(ifcHalfSpaceSolid);
        }

        private IXSolid BuildHalfSpace(IIfcHalfSpaceSolid halfSpace)
        {
            var elementarySurface = halfSpace.BaseSurface as IIfcElementarySurface;
            if (elementarySurface == null)
                throw new XbimGeometryServiceException(
                    $"Half-space #{halfSpace.EntityLabel}: only elementary surfaces are supported.");

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

            GeometryFactory.BuildAxis2Placement3d(elementarySurface.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int agreementFlag = halfSpace.AgreementFlag ? 1 : 0;
            double oneMeter = _modelService.OneMeter;
            double precision = _modelService.Precision;

            if (halfSpace is IIfcPolygonalBoundedHalfSpace polyBounded)
                return BuildPolygonalBoundedHalfSpace(polyBounded,
                    ox, oy, oz, zx, zy, zz, xx, xy, xz,
                    agreementFlag, oneMeter, precision);

            // Basic half-space or IfcBoxedHalfSpace (treated identically per IFC spec)
            int result = XbimGeometryNativeApi.xbim_halfspace_build(
                ContextHandle, surfaceType,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                radius, agreementFlag, oneMeter, precision,
                out NativeShapeHandle outHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build half-space #{halfSpace.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(outHandle);
        }

        private IXSolid BuildPolygonalBoundedHalfSpace(
            IIfcPolygonalBoundedHalfSpace polyBounded,
            double surfOx, double surfOy, double surfOz,
            double surfZx, double surfZy, double surfZz,
            double surfXx, double surfXy, double surfXz,
            int agreementFlag, double oneMeter, double precision)
        {
            if (!(polyBounded.BaseSurface is IIfcPlane))
                throw new XbimGeometryServiceException(
                    $"Polygonal bounded half-space #{polyBounded.EntityLabel}: base surface must be planar.");

            var polyline = polyBounded.PolygonalBoundary as IIfcPolyline;
            if (polyline == null)
                throw new NotSupportedException(
                    $"Polygonal bounded half-space #{polyBounded.EntityLabel}: only polyline boundaries are supported.");

            int pointCount = polyline.Points.Count;
            if (pointCount < 3)
                throw new XbimGeometryServiceException(
                    $"Polygonal bounded half-space #{polyBounded.EntityLabel}: boundary needs at least 3 points.");

            var xCoords = new double[pointCount];
            var yCoords = new double[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                xCoords[i] = polyline.Points[i].Coordinates[0];
                yCoords[i] = polyline.Points[i].Coordinates[1];
            }

            double bOx = 0, bOy = 0, bOz = 0;
            double bZx = 0, bZy = 0, bZz = 1;
            double bXx = 1, bXy = 0, bXz = 0;
            if (polyBounded.Position != null)
            {
                GeometryFactory.BuildAxis2Placement3d(polyBounded.Position,
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
                throw new XbimGeometryServiceException(
                    $"Failed to build polygonal bounded half-space #{polyBounded.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(outHandle);
        }

        /// <summary>
        /// Builds a compound of shells from a shell-based surface model by constructing
        /// each open or closed shell from its connected face set and combining them.
        /// </summary>
        public IXShape Build(IIfcShellBasedSurfaceModel ifcSurfaceModel)
        {
            double tolerance = _modelService.Precision;
            var shellHandles = new List<NativeShapeHandle>();

            try
            {
                foreach (var ifcShell in ifcSurfaceModel.SbsmBoundary)
                {
                    // IIfcShell is a select type — cast to the concrete shell types
                    IIfcConnectedFaceSet? faceSet = ifcShell as IIfcClosedShell
                        ?? ifcShell as IIfcOpenShell as IIfcConnectedFaceSet;

                    if (faceSet == null)
                    {
                        _logger.LogWarning(
                            "ShellBasedSurfaceModel #{Label}: shell boundary item is not a recognized shell type, skipping.",
                            ifcSurfaceModel.EntityLabel);
                        continue;
                    }

                    var shellHandle = BuildShellFromConnectedFaceSet(faceSet, tolerance);
                    if (shellHandle != null && !shellHandle.IsInvalid)
                        shellHandles.Add(shellHandle);
                }

                if (shellHandles.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"ShellBasedSurfaceModel #{ifcSurfaceModel.EntityLabel}: no valid shells were built.");

                if (shellHandles.Count == 1)
                {
                    var handle = shellHandles[0];
                    shellHandles.Clear();
                    return NativeShapeWrapper.WrapShape(handle);
                }

                // Assemble multiple shells into a compound
                using var nativeShells = new NativeHandleArray(shellHandles.ToArray());

                int result = XbimGeometryNativeApi.xbim_compound_make(
                    ContextHandle, nativeShells.Ptrs, nativeShells.Length, out var compoundHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"ShellBasedSurfaceModel #{ifcSurfaceModel.EntityLabel}: failed to create compound: " +
                        XbimGeometryNativeApi.GetLastError());

                return NativeShapeWrapper.WrapShape(compoundHandle);
            }
            finally
            {
                foreach (var h in shellHandles)
                    h.Dispose();
            }
        }

        public IXShape Build(IIfcTessellatedItem ifcTessellatedItem)
        {
            return ifcTessellatedItem switch
            {
                IIfcTriangulatedFaceSet triangulated => BuildTriangulatedFaceSet(triangulated),
                IIfcPolygonalFaceSet polygonal => BuildPolygonalFaceSet(polygonal),
                _ => throw new NotSupportedException(
                    $"Tessellated item type {ifcTessellatedItem.GetType().Name} is not supported.")
            };
        }

        /// <summary>
        /// Extracts 3D coordinates from an IFC coordinate list as a flat array of doubles [x0,y0,z0, x1,y1,z1, ...].
        /// </summary>
        private double[] ExtractCoordinates(IIfcCartesianPointList3D coordList)
        {
            var coordItems = coordList.CoordList;
            var coords = new double[coordItems.Count * 3];
            int i = 0;
            foreach (var pt in coordItems)
            {
                // Each inner IItemSet<IfcLengthMeasure> has 3 values (X, Y, Z)
                coords[i++] = pt[0];
                coords[i++] = pt[1];
                coords[i++] = pt[2];
            }
            return coords;
        }

        /// <summary>
        /// Builds a shell (or solid if closed) from a triangulated face set by constructing
        /// planar triangle faces from indexed coordinate data and sewing them together.
        /// </summary>
        private IXShape BuildTriangulatedFaceSet(IIfcTriangulatedFaceSet triangulated)
        {
            var coordList = triangulated.Coordinates;
            if (coordList == null)
                throw new XbimGeometryServiceException(
                    $"TriangulatedFaceSet #{triangulated.EntityLabel}: missing Coordinates.");

            double tolerance = _modelService.Precision;
            var coords = ExtractCoordinates(coordList);
            int numCoords = coordList.CoordList.Count;
            var faceHandles = new List<NativeShapeHandle>();

            try
            {
                // Build a planar face for each triangle
                foreach (var triangle in triangulated.CoordIndex)
                {
                    // CoordIndex contains 1-based indices
                    var indices = new List<long>();
                    foreach (var idx in triangle)
                        indices.Add((long)idx);

                    if (indices.Count < 3)
                    {
                        _logger.LogWarning("TriangulatedFaceSet #{Label}: skipping degenerate triangle with {Count} indices.",
                            triangulated.EntityLabel, indices.Count);
                        continue;
                    }

                    // Skip degenerate triangles (duplicate indices)
                    if (indices[0] == indices[1] || indices[1] == indices[2] || indices[0] == indices[2])
                        continue;

                    // Extract 3 points from the coordinate array (convert 1-based to 0-based)
                    var triCoords = new double[9];
                    for (int i = 0; i < 3; i++)
                    {
                        int idx = (int)indices[i] - 1; // 1-based to 0-based
                        if (idx < 0 || idx >= numCoords)
                        {
                            _logger.LogWarning("TriangulatedFaceSet #{Label}: index {Index} out of range.",
                                triangulated.EntityLabel, indices[i]);
                            goto nextTriangle;
                        }
                        triCoords[i * 3] = coords[idx * 3];
                        triCoords[i * 3 + 1] = coords[idx * 3 + 1];
                        triCoords[i * 3 + 2] = coords[idx * 3 + 2];
                    }

                    // Build a closed polygon wire from the 3 triangle vertices
                    int result = XbimGeometryNativeApi.xbim_wire_build_polygon(
                        ContextHandle, triCoords, 3, 1, out var wireHandle);

                    if (result != 0)
                    {
                        _logger.LogWarning("TriangulatedFaceSet #{Label}: failed to build triangle wire: {Error}",
                            triangulated.EntityLabel, XbimGeometryNativeApi.GetLastError());
                        continue;
                    }

                    // Build a planar face from the triangle wire
                    result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                        ContextHandle, wireHandle, out var faceHandle);

                    wireHandle.Dispose();

                    if (result != 0)
                    {
                        _logger.LogWarning("TriangulatedFaceSet #{Label}: failed to build triangle face: {Error}",
                            triangulated.EntityLabel, XbimGeometryNativeApi.GetLastError());
                        continue;
                    }

                    faceHandles.Add(faceHandle);
                    nextTriangle:;
                }

                if (faceHandles.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"TriangulatedFaceSet #{triangulated.EntityLabel}: no valid faces were built.");

                return AssembleTessellatedShell(faceHandles, triangulated.Closed.HasValue && (bool)triangulated.Closed.Value,
                    tolerance, triangulated.EntityLabel);
            }
            finally
            {
                foreach (var h in faceHandles)
                    h.Dispose();
            }
        }

        /// <summary>
        /// Builds a shell (or solid if closed) from a polygonal face set by constructing
        /// planar polygon faces from indexed coordinate data and sewing them together.
        /// </summary>
        private IXShape BuildPolygonalFaceSet(IIfcPolygonalFaceSet polygonal)
        {
            var coordList = polygonal.Coordinates;
            if (coordList == null)
                throw new XbimGeometryServiceException(
                    $"PolygonalFaceSet #{polygonal.EntityLabel}: missing Coordinates.");

            double tolerance = _modelService.Precision;
            var coords = ExtractCoordinates(coordList);
            int numCoords = coordList.CoordList.Count;
            var faceHandles = new List<NativeShapeHandle>();

            try
            {
                foreach (var face in polygonal.Faces)
                {
                    // Build the outer polygon wire from coordinate indices (1-based)
                    var outerWire = BuildWireFromCoordIndices(face.CoordIndex, coords, numCoords, tolerance);
                    if (outerWire == null)
                    {
                        _logger.LogWarning("PolygonalFaceSet #{Label}: skipping face with invalid outer loop.",
                            polygonal.EntityLabel);
                        continue;
                    }

                    NativeShapeHandle faceHandle;

                    // Check for faces with voids
                    if (face is IIfcIndexedPolygonalFaceWithVoids faceWithVoids
                        && faceWithVoids.InnerCoordIndices.Count > 0)
                    {
                        var innerWires = new List<NativeShapeHandle>();
                        try
                        {
                            foreach (var innerLoop in faceWithVoids.InnerCoordIndices)
                            {
                                var innerWire = BuildWireFromCoordIndices(innerLoop, coords, numCoords, tolerance);
                                if (innerWire != null)
                                    innerWires.Add(innerWire);
                            }

                            if (innerWires.Count > 0)
                            {
                                using var nativeInnerWires = new NativeHandleArray(innerWires.ToArray());

                                int result = XbimGeometryNativeApi.xbim_face_build_advanced(
                                    ContextHandle,
                                    0, // PLANE
                                    0, 0, 0, // origin (inferred)
                                    0, 0, 1, // zDir
                                    1, 0, 0, // xDir
                                    0,       // radius
                                    outerWire,
                                    nativeInnerWires.Ptrs,
                                    nativeInnerWires.Length,
                                    tolerance,
                                    1, // sameSense
                                    out faceHandle);

                                outerWire.Dispose();

                                if (result != 0)
                                {
                                    _logger.LogWarning("PolygonalFaceSet #{Label}: failed to build face with voids: {Error}",
                                        polygonal.EntityLabel, XbimGeometryNativeApi.GetLastError());
                                    continue;
                                }
                            }
                            else
                            {
                                // All inner wires failed — build simple face
                                int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                                    ContextHandle, outerWire, out faceHandle);
                                outerWire.Dispose();

                                if (result != 0)
                                {
                                    _logger.LogWarning("PolygonalFaceSet #{Label}: failed to build face: {Error}",
                                        polygonal.EntityLabel, XbimGeometryNativeApi.GetLastError());
                                    continue;
                                }
                            }
                        }
                        finally
                        {
                            foreach (var w in innerWires)
                                w.Dispose();
                        }
                    }
                    else
                    {
                        // Simple face without voids
                        int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                            ContextHandle, outerWire, out faceHandle);
                        outerWire.Dispose();

                        if (result != 0)
                        {
                            _logger.LogWarning("PolygonalFaceSet #{Label}: failed to build face: {Error}",
                                polygonal.EntityLabel, XbimGeometryNativeApi.GetLastError());
                            continue;
                        }
                    }

                    faceHandles.Add(faceHandle);
                }

                if (faceHandles.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"PolygonalFaceSet #{polygonal.EntityLabel}: no valid faces were built.");

                return AssembleTessellatedShell(faceHandles, polygonal.Closed.HasValue && (bool)polygonal.Closed.Value,
                    tolerance, polygonal.EntityLabel);
            }
            finally
            {
                foreach (var h in faceHandles)
                    h.Dispose();
            }
        }

        /// <summary>
        /// Builds a closed polygon wire from an IFC coordinate index list and a pre-extracted
        /// coordinate array.
        /// </summary>
        private NativeShapeHandle? BuildWireFromCoordIndices(
            IItemSet<IfcPositiveInteger> indices,
            double[] coords, int numCoords, double tolerance)
        {
            int count = indices.Count;
            if (count < 3)
                return null;

            var polyCoords = new double[count * 3];
            for (int i = 0; i < count; i++)
            {
                int idx = (int)(long)indices[i] - 1; // 1-based to 0-based
                if (idx < 0 || idx >= numCoords)
                    return null;

                polyCoords[i * 3] = coords[idx * 3];
                polyCoords[i * 3 + 1] = coords[idx * 3 + 1];
                polyCoords[i * 3 + 2] = coords[idx * 3 + 2];
            }

            int result = XbimGeometryNativeApi.xbim_wire_build_polygon(
                ContextHandle, polyCoords, count, 1, out var wireHandle);

            if (result != 0)
                return null;

            return wireHandle;
        }

        /// <summary>
        /// Assembles tessellated faces into a shell. If the tessellation is marked as closed,
        /// attempts to create a solid; otherwise returns a sewn shell.
        /// </summary>
        private IXShape AssembleTessellatedShell(
            List<NativeShapeHandle> faceHandles, bool isClosed, double tolerance, int entityLabel)
        {
            using var nativeFaces = new NativeHandleArray(faceHandles.ToArray());

            if (isClosed && faceHandles.Count >= 4)
            {
                // Try to build as solid (sew + close)
                int result = XbimGeometryNativeApi.xbim_shell_build_closed_shell(
                    ContextHandle, nativeFaces.Ptrs, nativeFaces.Length, tolerance, out var solidHandle);

                if (result == 0)
                    return NativeShapeWrapper.WrapShape(solidHandle);

                _logger.LogWarning(
                    "TessellatedFaceSet #{Label}: closed shell conversion failed ({Error}), falling back to open shell.",
                    entityLabel, XbimGeometryNativeApi.GetLastError());
            }

            // Build as open shell (sew only)
            int shellResult = XbimGeometryNativeApi.xbim_shell_build_from_faces(
                ContextHandle, nativeFaces.Ptrs, nativeFaces.Length, tolerance, out var rawShellHandle);

            if (shellResult != 0)
                throw new XbimGeometryServiceException(
                    $"TessellatedFaceSet #{entityLabel}: failed to build shell: {XbimGeometryNativeApi.GetLastError()}");

            // Sew the shell
            int sewResult = XbimGeometryNativeApi.xbim_shell_sew(
                ContextHandle, rawShellHandle, tolerance, out _, out var sewedHandle);

            rawShellHandle.Dispose();

            if (sewResult != 0)
                throw new XbimGeometryServiceException(
                    $"TessellatedFaceSet #{entityLabel}: failed to sew shell: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapShape(sewedHandle);
        }

        public IXShape Build(IIfcSectionedSpine ifcSectionedSpine)
        {
            var crossSections = ifcSectionedSpine.CrossSections.ToList();
            var positions = ifcSectionedSpine.CrossSectionPositions.ToList();

            if (crossSections.Count < 2)
                throw new XbimGeometryServiceException(
                    $"IfcSectionedSpine #{ifcSectionedSpine.EntityLabel} requires at least 2 cross-sections but has {crossSections.Count}.");

            if (crossSections.Count != positions.Count)
                throw new XbimGeometryServiceException(
                    $"IfcSectionedSpine #{ifcSectionedSpine.EntityLabel}: cross-section count ({crossSections.Count}) does not match position count ({positions.Count}).");

            // Build the spine wire from the composite curve
            var spineWire = (XbimWire)_modelService.WireFactory.Build(ifcSectionedSpine.SpineCurve);

            var geometryFactory = (GeometryFactory)_modelService.GeometryFactory;
            var movedFaceHandles = new List<NativeShapeHandle>();

            try
            {
                // Build each cross-section face and move it to its placement position
                for (int i = 0; i < crossSections.Count; i++)
                {
                    var face = (XbimFace)_modelService.ProfileFactory.BuildFace(crossSections[i]);
                    var location = geometryFactory.BuildLocationFromAxis3D(positions[i]);

                    int moveResult = XbimGeometryNativeApi.xbim_shape_moved(
                        face.Handle, location.Handle, out var movedHandle);

                    if (moveResult != 0)
                    {
                        face.Handle.Dispose();
                        location.Handle.Dispose();
                        throw new XbimGeometryServiceException(
                            $"IfcSectionedSpine #{ifcSectionedSpine.EntityLabel}: failed to position cross-section {i}: {XbimGeometryNativeApi.GetLastError()}");
                    }

                    face.Handle.Dispose();
                    location.Handle.Dispose();
                    movedFaceHandles.Add(movedHandle);
                }

                using var nativeSections = new NativeHandleArray(movedFaceHandles.ToArray());

                int result = XbimGeometryNativeApi.xbim_solid_build_sectioned_spine(
                    ContextHandle,
                    spineWire.Handle,
                    nativeSections.Ptrs,
                    nativeSections.Length,
                    _modelService.Precision,
                    out var solidHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build IfcSectionedSpine #{ifcSectionedSpine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeWrapper.WrapSolid(solidHandle);
            }
            finally
            {
                foreach (var h in movedFaceHandles)
                    h.Dispose();
            }
        }

        #endregion
    }
}
