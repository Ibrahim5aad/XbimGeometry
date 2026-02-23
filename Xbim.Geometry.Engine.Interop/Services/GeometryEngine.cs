using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Exceptions;
using Xbim.Geometry.WexBim;
using Xbim.Ifc4;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Services
{
    /// <summary>
    /// Geometry engine providing both the V6 model geometry service interface
    /// and the legacy V5 shape-creation API for backward compatibility.
    /// </summary>
    internal class GeometryEngine : IXGeometryEngineV6, IDisposable
    {
        private readonly ModelGeometryService _service;
        private readonly ILogger _logger;

        public GeometryEngine(ModelGeometryService service, ILoggerFactory loggerFactory)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger = loggerFactory.CreateLogger<GeometryEngine>();
        }

        #region IXGeometryEngineV6

        public IXModelGeometryService ModelGeometryService => _service;

        public IXShape Build(IIfcGeometricRepresentationItem geomRep)
        {
            ArgumentNullException.ThrowIfNull(geomRep);

            // Solid models (extruded, revolved, CSG, swept disk, BRep, etc.)
            if (geomRep is IIfcSolidModel solidModel)
                return _service.SolidFactory.Build(solidModel);

            // Boolean results (union, cut, intersect)
            if (geomRep is IIfcBooleanResult boolResult)
                return _service.BooleanFactory.Build(boolResult);

            // Half-space solids (clipping planes)
            if (geomRep is IIfcHalfSpaceSolid halfSpace)
                return _service.SolidFactory.Build(halfSpace);

            // CSG primitives (block, sphere, cylinder, cone, pyramid)
            if (geomRep is IIfcCsgPrimitive3D csgPrimitive)
                return _service.SolidFactory.Build(csgPrimitive);

            // Surface models
            if (geomRep is IIfcFaceBasedSurfaceModel faceBasedSurface)
                return _service.SolidFactory.Build(faceBasedSurface);

            if (geomRep is IIfcShellBasedSurfaceModel shellBasedSurface)
                return _service.SolidFactory.Build(shellBasedSurface);

            // Tessellated items
            if (geomRep is IIfcTessellatedItem tessellated)
                return _service.SolidFactory.Build(tessellated);

            // Faceted BRep
            if (geomRep is IIfcFacetedBrep facetedBrep)
                return _service.SolidFactory.Build(facetedBrep);

            // Sectioned spine
            if (geomRep is IIfcSectionedSpine sectionedSpine)
                return _service.SolidFactory.Build(sectionedSpine);

            // Bounding box (build as a simple block)
            if (geomRep is IIfcBoundingBox boundingBox)
                return BuildBoundingBox(boundingBox);

            throw new NotSupportedException(
                $"Build: unsupported geometric representation type {geomRep.GetType().Name} (#{(geomRep as IPersistEntity)?.EntityLabel}).");
        }

        private IXShape BuildBoundingBox(IIfcBoundingBox bbox)
        {
            double xLen = bbox.XDim;
            double yLen = bbox.YDim;
            double zLen = bbox.ZDim;

            if (xLen <= 0 || yLen <= 0 || zLen <= 0)
                throw new XbimGeometryServiceException(
                    $"BoundingBox has zero or negative dimensions.");

            var corner = bbox.Corner;
            double ox = corner.X, oy = corner.Y, oz = corner.Z;

            int result = XbimGeometryNativeApi.xbim_solid_build_block(
                _service.ContextHandle,
                ox, oy, oz,
                0, 0, 1, // Z direction
                1, 0, 0, // X direction
                xLen, yLen, zLen,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build BoundingBox solid: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        #endregion

        #region IXModelGeometryService

        public IXLoggingService LoggingService => _service.LoggingService;
        public IXVertexFactory VertexFactory => _service.VertexFactory;
        public IXGeometryFactory GeometryFactory => _service.GeometryFactory;
        public IXCurveFactory CurveFactory => _service.CurveFactory;
        public IXSurfaceFactory SurfaceFactory => _service.SurfaceFactory;
        public IXEdgeFactory EdgeFactory => _service.EdgeFactory;
        public IXWireFactory WireFactory => _service.WireFactory;
        public IXFaceFactory FaceFactory => _service.FaceFactory;
        public IXShellFactory ShellFactory => _service.ShellFactory;
        public IXSolidFactory SolidFactory => _service.SolidFactory;
        public IXCompoundFactory CompoundFactory => _service.CompoundFactory;
        public IXBooleanFactory BooleanFactory => _service.BooleanFactory;
        public IXShapeFactory ShapeFactory => _service.ShapeFactory;
        public IXProfileFactory ProfileFactory => _service.ProfileFactory;
        public IXMaterialFactory MaterialFactory => _service.MaterialFactory;
        public IXProjectionFactory ProjectionFactory => _service.ProjectionFactory;
        public IXWexBimMeshFactory WexBimMeshFactory => _service.WexBimMeshFactory;
        public IXShapeBinarySerializer ShapeBinarySerializer => _service.ShapeBinarySerializer;
        public IXModelPlacementBuilder ModelPlacementBuilder => _service.ModelPlacementBuilder;

        public double Timeout { get => _service.Timeout; set => _service.Timeout = value; }
        public double Precision => _service.Precision;
        public double PrecisionSquared => _service.PrecisionSquared;
        public double OneMeter => _service.OneMeter;
        public double OneFoot => _service.OneFoot;
        public double OneMillimeter => _service.OneMillimeter;
        public double MinimumGap { get => _service.MinimumGap; set => _service.MinimumGap = value; }
        public double RadianFactor => _service.RadianFactor;
        public double MinAreaM2 => _service.MinAreaM2;
        public IXMeshFactors MeshFactors => _service.MeshFactors;
        public IModel Model => _service.Model;
        public bool UpgradeFaceSets { get => _service.UpgradeFaceSets; set => _service.UpgradeFaceSets = value; }

        public void SetModel(IModel model) => _service.SetModel(model);
        public ISet<IIfcGeometricRepresentationContext> GetTypical3dContexts() => _service.GetTypical3dContexts();
        public IXLocation Create(IIfcObjectPlacement objectPlacement) => _service.Create(objectPlacement);
        public IXLocation CreateMappingTransform(IIfcMappedItem mappedItem) => _service.CreateMappingTransform(mappedItem);

        public void LogError(string format, params object[] args) => _service.LogError(format, args);
        public void LogError(IPersistEntity ifcEntity, string format, params object[] args) => _service.LogError(ifcEntity, format, args);
        public void LogError(IPersistEntity ifcEntity, Exception exception, string format, params object[] args) => _service.LogError(ifcEntity, exception, format, args);
        public void LogError(Exception exception, string format, params object[] args) => _service.LogError(exception, format, args);

        public void LogWarning(string format, params object[] args) => _service.LogWarning(format, args);
        public void LogWarning(IPersistEntity ifcEntity, string format, params object[] args) => _service.LogWarning(ifcEntity, format, args);
        public void LogWarning(IPersistEntity ifcEntity, Exception exception, string format, params object[] args) => _service.LogWarning(ifcEntity, exception, format, args);
        public void LogWarning(Exception exception, string format, params object[] args) => _service.LogWarning(exception, format, args);

        public void LogInformation(string format, params object[] args) => _service.LogInformation(format, args);
        public void LogInformation(IPersistEntity ifcEntity, string format, params object[] args) => _service.LogInformation(ifcEntity, format, args);
        public void LogInformation(IPersistEntity ifcEntity, Exception exception, string format, params object[] args) => _service.LogInformation(ifcEntity, exception, format, args);
        public void LogInformation(Exception exception, string format, params object[] args) => _service.LogInformation(exception, format, args);

        public void LogDebug(string format, params object[] args) => _service.LogDebug(format, args);
        public void LogDebug(IPersistEntity ifcEntity, string format, params object[] args) => _service.LogDebug(ifcEntity, format, args);
        public void LogDebug(IPersistEntity ifcEntity, Exception exception, string format, params object[] args) => _service.LogDebug(ifcEntity, exception, format, args);
        public void LogDebug(Exception exception, string format, params object[] args) => _service.LogDebug(exception, format, args);

        #endregion

        #region IXbimGeometryEngine

        // --- Generic Create ---

        public IXbimGeometryObject Create(IIfcGeometricRepresentationItem ifcRepresentation, ILogger logger)
        {
            return (IXbimGeometryObject)Build(ifcRepresentation);
        }

        public IXbimGeometryObject Create(IIfcGeometricRepresentationItem ifcRepresentation, IIfcAxis2Placement3D objectLocation, ILogger logger)
        {
            var shape = Build(ifcRepresentation);
            if (objectLocation != null)
            {
                var gf = (GeometryFactory)_service.GeometryFactory;
                var location = gf.BuildLocationFromAxis3D(objectLocation);
                using (location)
                {
                    int moveResult = XbimGeometryNativeApi.xbim_shape_moved(
                        ((XbimShape)shape).Handle, location.Handle, out var movedHandle);
                    if (moveResult != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to apply placement: {XbimGeometryNativeApi.GetLastError()}");
                    shape = NativeShapeWrapper.WrapShape(movedHandle);
                }
            }
            return (IXbimGeometryObject)shape;
        }

        // --- CreateShapeGeometry ---

        public XbimShapeGeometry CreateShapeGeometry(IXbimGeometryObject geometryObject, double precision, double deflection, double angle, XbimGeometryType storageType, ILogger logger)
        {
            if (storageType != XbimGeometryType.PolyhedronBinary)
                throw new NotSupportedException("Only PolyhedronBinary storage type is supported.");

            var shapeGeom = new XbimShapeGeometry();

            IXShape v6Shape = ExtractV6Shape(geometryObject);
            if (v6Shape == null)
            {
                _logger.LogWarning("CreateShapeGeometry: unable to extract shape from geometry object.");
                return shapeGeom;
            }

            var meshFactory = (WexBimMeshFactory)_service.WexBimMeshFactory;
            byte[] meshData = meshFactory.CreateWexBimMesh(v6Shape, precision, deflection, angle, 1.0, out var bounds);

            ((IXbimShapeGeometryData)shapeGeom).ShapeData = meshData;

            if (meshData.Length > 0)
            {
                shapeGeom.BoundingBox = geometryObject.BoundingBox;
                shapeGeom.LOD = XbimLOD.LOD_Unspecified;
                shapeGeom.Format = storageType;
            }

            return shapeGeom;
        }

        public XbimShapeGeometry CreateShapeGeometry(IXbimGeometryObject geometryObject, double precision, double deflection, ILogger logger)
        {
            return CreateShapeGeometry(geometryObject, precision, deflection, 0.5, XbimGeometryType.PolyhedronBinary, logger);
        }

        public XbimShapeGeometry CreateShapeGeometry(double oneMillimetre, IXbimGeometryObject geometryObject, double precision, ILogger logger)
        {
            var mf = _service.MeshFactors;
            double deflection = oneMillimetre * mf.LinearDefection / _service.OneMillimeter;
            return CreateShapeGeometry(geometryObject, precision, deflection, mf.AngularDeflection, XbimGeometryType.PolyhedronBinary, logger);
        }

        // --- CreateSolid overloads ---

        public IXbimSolid CreateSolid(IIfcSweptAreaSolid ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcExtrudedAreaSolid ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcRevolvedAreaSolid ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcSweptDiskSolid ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcBoundingBox ifcSolid, ILogger logger)
            => (IXbimSolid)BuildBoundingBox(ifcSolid);

        public IXbimSolid CreateSolid(IIfcSurfaceCurveSweptAreaSolid ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcHalfSpaceSolid ifcSolid, ILogger logger)
            => (IXbimSolid)_service.SolidFactory.Build(ifcSolid);

        public IXbimSolid CreateSolid(IIfcPolygonalBoundedHalfSpace ifcSolid, ILogger logger)
            => (IXbimSolid)_service.SolidFactory.Build(ifcSolid);

        public IXbimSolid CreateSolid(IIfcBoxedHalfSpace ifcSolid, ILogger logger)
            => (IXbimSolid)_service.SolidFactory.Build(ifcSolid);

        public IXbimSolid CreateSolid(IIfcCsgPrimitive3D ifcSolid, ILogger logger)
            => (IXbimSolid)_service.SolidFactory.Build(ifcSolid);

        public IXbimSolid CreateSolid(IIfcSphere ifcSolid, ILogger logger)
            => (IXbimSolid)_service.SolidFactory.Build(ifcSolid);

        public IXbimSolid CreateSolid(IIfcBlock ifcSolid, ILogger logger)
            => (IXbimSolid)_service.SolidFactory.Build(ifcSolid);

        public IXbimSolid CreateSolid(IIfcRightCircularCylinder ifcSolid, ILogger logger)
            => (IXbimSolid)_service.SolidFactory.Build(ifcSolid);

        public IXbimSolid CreateSolid(IIfcRightCircularCone ifcSolid, ILogger logger)
            => (IXbimSolid)_service.SolidFactory.Build(ifcSolid);

        public IXbimSolid CreateSolid(IIfcRectangularPyramid ifcSolid, ILogger logger)
            => (IXbimSolid)_service.SolidFactory.Build(ifcSolid);

        public IXbimSolid CreateSolid(IIfcSweptDiskSolidPolygonal ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcRevolvedAreaSolidTapered ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcFixedReferenceSweptAreaSolid ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcAdvancedBrep ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcAdvancedBrepWithVoids ifcSolid, ILogger logger)
            => BuildAsSolid(ifcSolid);

        public IXbimSolid CreateSolid(IIfcSectionedSpine ifcSolid, ILogger logger)
        {
            var shape = _service.SolidFactory.Build(ifcSolid);
            return WrapShapeAsSolid(shape);
        }

        public IXbimSolid CreateSolid(IIfcTriangulatedFaceSet shell, ILogger logger)
        {
            var shape = _service.SolidFactory.Build(shell);
            return WrapShapeAsSolid(shape);
        }

        public IXbimSolid CreateSolid(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
        {
            var shape = _service.SolidFactory.Build(ifcSurface);
            return WrapShapeAsSolid(shape);
        }

        public IXbimSolid CreateSolid(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
        {
            var shape = _service.SolidFactory.Build(ifcSurface);
            return WrapShapeAsSolid(shape);
        }

        // --- CreateSolidSet overloads ---

        public IXbimSolidSet CreateSolidSet()
            => new XbimSolidSet();

        public IXbimSolidSet CreateSolidSet(IIfcBooleanClippingResult ifcSolid, ILogger logger)
        {
            var shape = _service.BooleanFactory.Build(ifcSolid);
            return WrapShapeAsSolidSet(shape);
        }

        public IXbimSolidSet CreateSolidSet(IIfcBooleanOperand ifcSolid, ILogger logger)
        {
            if (ifcSolid is IIfcBooleanResult boolResult)
                return WrapShapeAsSolidSet(_service.BooleanFactory.Build(boolResult));
            if (ifcSolid is IIfcSolidModel solidModel)
                return WrapShapeAsSolidSet(_service.SolidFactory.Build(solidModel));
            if (ifcSolid is IIfcHalfSpaceSolid halfSpace)
                return WrapShapeAsSolidSet(_service.SolidFactory.Build(halfSpace));
            if (ifcSolid is IIfcCsgPrimitive3D csg)
                return WrapShapeAsSolidSet(_service.SolidFactory.Build(csg));
            throw new NotSupportedException(
                $"Unsupported boolean operand type: {ifcSolid.GetType().Name}");
        }

        public IXbimSolidSet CreateSolidSet(IIfcBooleanResult boolOp, ILogger logger)
            => WrapShapeAsSolidSet(_service.BooleanFactory.Build(boolOp));

        public IXbimSolidSet CreateSolidSet(IIfcManifoldSolidBrep ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcFacetedBrep ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcFacetedBrepWithVoids ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build((IIfcSolidModel)ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcClosedShell ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcSweptAreaSolid ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcCsgSolid ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcTriangulatedFaceSet shell, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(shell));

        public IXbimSolidSet CreateSolidSet(IIfcPolygonalFaceSet shell, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(shell));

        public IXbimSolidSet CreateSolidSet(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSurface));

        public IXbimSolidSet CreateSolidSet(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSurface));

        // --- CreateFace overloads ---

        public IXbimFace CreateFace(IIfcProfileDef profileDef, ILogger logger)
        {
            var face = _service.ProfileFactory.BuildFace(profileDef);
            return (IXbimFace)face;
        }

        public IXbimFace CreateFace(IIfcCompositeCurve cCurve, ILogger logger)
        {
            var wire = _service.WireFactory.Build(cCurve);
            return WrapWireAsFace(wire);
        }

        public IXbimFace CreateFace(IIfcPolyline pline, ILogger logger)
        {
            var wire = _service.WireFactory.Build(pline);
            return WrapWireAsFace(wire);
        }

        public IXbimFace CreateFace(IIfcPolyLoop loop, ILogger logger)
        {
            var points = new List<IXPoint>();
            foreach (var pt in loop.Polygon)
            {
                double x = pt.Coordinates[0];
                double y = pt.Coordinates[1];
                double z = (int)(long)pt.Dim == 3 ? (double)pt.Coordinates[2] : 0.0;
                points.Add(new XPoint(x, y, z));
            }
            var wire = _service.WireFactory.BuildWire(points.ToArray());
            return WrapWireAsFace(wire);
        }

        public IXbimFace CreateFace(IIfcSurface surface, ILogger logger)
        {
            if (surface is IIfcSurfaceOfLinearExtrusion linearExtrusion)
                return CreateFaceOfLinearExtrusion(linearExtrusion);
            if (surface is IIfcSurfaceOfRevolution)
                return CreateFaceFromNaturalBounds(surface);

            var built = _service.SurfaceFactory.Build(surface);
            return SurfaceToFace(built, surface.EntityLabel);
        }

        private IXbimFace CreateFaceFromNaturalBounds(IIfcSurface ifcSurface)
        {
            var built = _service.SurfaceFactory.Build(ifcSurface);
            if (built is not Surface surf)
                throw new XbimGeometryServiceException(
                    $"Failed to build surface #{ifcSurface.EntityLabel}: unexpected type.");

            int result = XbimGeometryNativeApi.xbim_face_build_surface_natural_bounds(
                _service.ContextHandle, surf.Handle, _service.Precision, out var faceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build face from surface #{ifcSurface.EntityLabel}: " +
                    $"{XbimGeometryNativeApi.GetLastError()}");

            return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
        }

        private IXbimFace CreateFaceOfLinearExtrusion(IIfcSurfaceOfLinearExtrusion ifcExtrusion)
        {
            var built = _service.SurfaceFactory.Build(ifcExtrusion);
            if (built is not Surface surf)
                throw new XbimGeometryServiceException(
                    $"SurfaceOfLinearExtrusion #{ifcExtrusion.EntityLabel}: unexpected surface type.");

            double depth = ifcExtrusion.Depth;

            int result = XbimGeometryNativeApi.xbim_face_build_surface_with_depth(
                _service.ContextHandle, surf.Handle, depth, _service.Precision, out var faceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build face from SurfaceOfLinearExtrusion #{ifcExtrusion.EntityLabel}: " +
                    $"{XbimGeometryNativeApi.GetLastError()}");

            return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
        }

        public IXbimFace CreateFace(IIfcPlane plane, ILogger logger)
            => CreateFace((IIfcSurface)plane, logger);

        public IXbimFace CreateFace(IXbimWire wire, ILogger logger)
        {
            if (wire is XbimWire wireShape)
            {
                int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                    _service.ContextHandle, wireShape.Handle, out var faceHandle);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build face from wire: {XbimGeometryNativeApi.GetLastError()}");
                return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
            }

            throw new XbimGeometryServiceException("Wire must be a XbimWire from this geometry engine.");
        }

        // --- CreateSurfaceModel overloads ---

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
        {
            var shape = _service.SolidFactory.Build(ifcSurface);
            return WrapShapeAsGeometryObjectSet(shape);
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
        {
            var shape = _service.SolidFactory.Build(ifcSurface);
            return WrapShapeAsGeometryObjectSet(shape);
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcTessellatedFaceSet shell, ILogger logger)
        {
            var shape = _service.SolidFactory.Build(shell);
            return WrapShapeAsGeometryObjectSet(shape);
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcPolygonalFaceSet shell, ILogger logger)
        {
            var shape = _service.SolidFactory.Build(shell);
            return WrapShapeAsGeometryObjectSet(shape);
        }

        // --- Empty set creation ---

        public IXbimGeometryObjectSet CreateGeometryObjectSet()
            => new XbimGeometryObjectSet();

        // --- Shell creation ---

        public IXbimShell CreateShell(IIfcOpenShell shell, ILogger logger)
            => throw new NotSupportedException("CreateShell from IIfcOpenShell not yet supported.");

        public IXbimShell CreateShell(IIfcConnectedFaceSet shell, ILogger logger)
            => throw new NotSupportedException("CreateShell from IIfcConnectedFaceSet not yet supported.");

        public IXbimShell CreateShell(IIfcSurfaceOfLinearExtrusion linExt, ILogger logger)
            => throw new NotSupportedException("CreateShell from IIfcSurfaceOfLinearExtrusion not yet supported.");

        // --- Wire creation ---

        public IXbimWire CreateWire(IIfcCurve curve, ILogger logger)
        {
            var wire = _service.WireFactory.Build(curve);
            return (IXbimWire)wire;
        }

        public IXbimWire CreateWire(IIfcCompositeCurveSegment compCurveSeg, ILogger logger)
            => throw new NotSupportedException("CreateWire from IIfcCompositeCurveSegment not yet supported.");

        // --- Grid ---

        public IXbimSolidSet CreateGrid(IIfcGrid grid, ILogger logger)
        {
            var curveFactory = (CurveFactory)_service.CurveFactory;

            var uHandles = BuildGridAxisCurves(grid.UAxes, curveFactory, logger);
            var vHandles = BuildGridAxisCurves(grid.VAxes, curveFactory, logger);
            var wHandles = BuildGridAxisCurves(grid.WAxes, curveFactory, logger);

            if (uHandles.Length == 0 && vHandles.Length == 0 && wHandles.Length == 0)
                return new XbimSolidSet();

            try
            {
                using var uArray = new NativeHandleArray(uHandles);
                using var vArray = new NativeHandleArray(vHandles);
                using var wArray = new NativeHandleArray(wHandles);

                int result = XbimGeometryNativeApi.xbim_grid_create(
                    _service.ContextHandle,
                    uArray.Ptrs, uArray.Length,
                    vArray.Ptrs, vArray.Length,
                    wArray.Ptrs, wArray.Length,
                    out var shapeHandle);

                if (result != 0)
                {
                    logger?.LogWarning("CreateGrid failed: {Error}",
                        XbimGeometryNativeApi.GetLastError());
                    return new XbimSolidSet();
                }

                return new XbimSolidSet(new XbimShape(shapeHandle));
            }
            finally
            {
                foreach (var h in uHandles) h?.Dispose();
                foreach (var h in vHandles) h?.Dispose();
                foreach (var h in wHandles) h?.Dispose();
            }
        }

        private static NativeCurve2dHandle[] BuildGridAxisCurves(
            IEnumerable<IIfcGridAxis> axes, CurveFactory curveFactory, ILogger logger)
        {
            if (axes == null)
                return Array.Empty<NativeCurve2dHandle>();

            var handles = new List<NativeCurve2dHandle>();
            foreach (var axis in axes)
            {
                if (axis.AxisCurve == null) continue;
                try
                {
                    var curve2d = (XbimCurve2d)curveFactory.BuildCurve2d(axis.AxisCurve);
                    handles.Add(curve2d.DetachHandle());
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex,
                        "Failed to build 2D curve for grid axis {Tag}, skipping",
                        axis.AxisTag?.Value ?? "(unnamed)");
                }
            }
            return handles.ToArray();
        }

        // --- Curve creation ---

        public IXbimCurve CreateCurve(IIfcCurve curve, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(curve) as IXbimCurve;

        public IXbimCurve CreateCurve(IIfcPolyline ifcPolyline, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(ifcPolyline) as IXbimCurve;

        public IXbimCurve CreateCurve(IIfcCircle curve, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(curve) as IXbimCurve;

        public IXbimCurve CreateCurve(IIfcEllipse curve, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(curve) as IXbimCurve;

        public IXbimCurve CreateCurve(IIfcLine curve, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(curve) as IXbimCurve;

        public IXbimCurve CreateCurve(IIfcTrimmedCurve curve, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(curve) as IXbimCurve;

        public IXbimCurve CreateCurve(IIfcBSplineCurveWithKnots curve, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(curve) as IXbimCurve;

        public IXbimCurve CreateCurve(IIfcRationalBSplineCurveWithKnots curve, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(curve) as IXbimCurve;

        public IXbimCurve CreateCurve(IIfcOffsetCurve3D curve, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(curve) as IXbimCurve;

        public IXbimCurve CreateCurve(IIfcOffsetCurve2D curve, ILogger logger)
            => ((CurveFactory)_service.CurveFactory).Build(curve) as IXbimCurve;

        // --- Point creation ---

        public IXbimPoint CreatePoint(double x, double y, double z, double tolerance)
            => new XbimPoint(x, y, z, tolerance);

        public IXbimPoint CreatePoint(IIfcCartesianPoint p)
        {
            double x = p.Coordinates[0];
            double y = p.Coordinates[1];
            double z = p.Coordinates.Count == 3 ? (double)p.Coordinates[2] : 0.0;
            return new XbimPoint(x, y, z, _service.Precision);
        }

        public IXbimPoint CreatePoint(XbimPoint3D p, double tolerance)
            => new XbimPoint(p.X, p.Y, p.Z, tolerance);

        public IXbimPoint CreatePoint(IIfcPoint pt)
        {
            if (pt is IIfcCartesianPoint cp)
                return CreatePoint(cp);
            if (pt is IIfcPointOnCurve poc)
                return CreatePoint(poc, _logger);
            if (pt is IIfcPointOnSurface pos)
                return CreatePoint(pos, _logger);
            throw new NotSupportedException($"IIfcPoint type {pt.GetType().Name} is not supported.");
        }

        public IXbimPoint CreatePoint(IIfcPointOnCurve p, ILogger logger)
        {
            var factory = (CurveFactory)_service.CurveFactory;
            using var curve = factory.Build3d(p.BasisCurve);
            double param = p.PointParameter;
            if (p.BasisCurve is IIfcConic)
                param *= _service.RadianFactor;
            var pt = curve.GetPoint(param);
            return new XbimPoint(pt.X, pt.Y, pt.Z, _service.Precision);
        }

        public IXbimPoint CreatePoint(IIfcPointOnSurface pos, ILogger logger)
        {
            return CreatePoint(pos, _logger);
        }

        // --- Vertex creation ---

        public IXbimVertex CreateVertexPoint(XbimPoint3D point, double precision)
        {
            int result = XbimGeometryNativeApi.xbim_vertex_build(
                _service.ContextHandle, point.X, point.Y, point.Z, out var handle);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to create vertex: {XbimGeometryNativeApi.GetLastError()}");
            return new XbimVertex(handle);
        }

        // --- Transforms ---

        public XbimMatrix3D ToMatrix3D(IIfcObjectPlacement objPlacement, ILogger logger)
        {
            using var loc = (XLocation)_service.Create(objPlacement);
            return new XbimMatrix3D(
                loc.M11, loc.M12, loc.M13, 0,
                loc.M21, loc.M22, loc.M23, 0,
                loc.M31, loc.M32, loc.M33, 0,
                loc.OffsetX, loc.OffsetY, loc.OffsetZ, 1);
        }

        public IXbimGeometryObject Transformed(IXbimGeometryObject geometry, IIfcCartesianTransformationOperator cartesianTransform)
        {
            var shape = geometry as XbimShape;
            if (shape == null)
                throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var matrix = gf.BuildTransform(cartesianTransform);

            var m = new XbimMatrix3D(
                matrix.M11 * matrix.ScaleX, matrix.M12 * matrix.ScaleX, matrix.M13 * matrix.ScaleX, 0,
                matrix.M21 * matrix.ScaleY, matrix.M22 * matrix.ScaleY, matrix.M23 * matrix.ScaleY, 0,
                matrix.M31 * matrix.ScaleZ, matrix.M32 * matrix.ScaleZ, matrix.M33 * matrix.ScaleZ, 0,
                matrix.OffsetX, matrix.OffsetY, matrix.OffsetZ, 1);

            return shape.Transform(m);
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcPlacement placement)
        {
            var shape = geometryObject as XbimShape;
            if (shape == null)
                throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var loc = gf.BuildLocation(placement);
            return MoveShape(shape, (XLocation)loc);
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcAxis2Placement3D placement)
        {
            var shape = geometryObject as XbimShape;
            if (shape == null)
                throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var loc = gf.BuildLocationFromAxis3D(placement);
            return MoveShape(shape, loc);
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcAxis2Placement2D placement)
        {
            var shape = geometryObject as XbimShape;
            if (shape == null)
                throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var loc = gf.BuildLocationFromAxis2D(placement);
            return MoveShape(shape, loc);
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcObjectPlacement objectPlacement, ILogger logger)
        {
            if (geometryObject is not XbimShape shape)
                throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var loc = gf.ToLocation(objectPlacement);
            return MoveShape(shape, loc);
        }

        // --- BRep I/O ---

        public IXbimGeometryObject FromBrep(string brepStr)
        {
            return (IXbimGeometryObject)Services.ShapeBinarySerializer.FromBrep(brepStr);
        }

        public string ToBrep(IXbimGeometryObject geometryObject)
        {
            var shape = geometryObject as XbimShape;
            if (shape == null)
                throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");
            return shape.BrepString();
        }

        public void WriteBrep(string filename, IXbimGeometryObject geomObj)
        {
            var shape = geomObj as XbimShape;
            if (shape == null)
                throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");
            shape.WriteBrep(filename);
        }

        public IXbimGeometryObject ReadBrep(string filename)
        {
            string brepStr = File.ReadAllText(filename);
            return FromBrep(brepStr);
        }

        // --- Triangulation / Mesh ---

        public void WriteTriangulation(TextWriter tw, IXbimGeometryObject shape, double tolerance, double deflection, double angle)
            => throw new NotSupportedException("Text-based triangulation is not supported. Use binary format.");

        public void WriteTriangulation(BinaryWriter bw, IXbimGeometryObject shape, double tolerance, double deflection, double angle)
        {
            IXShape v6Shape = ExtractV6Shape(shape);
            if (v6Shape == null)
            {
                _logger.LogWarning("WriteTriangulation: unable to extract shape from geometry object.");
                return;
            }

            byte[] meshData = _service.WexBimMeshFactory.CreateWexBimMesh(v6Shape, tolerance, deflection, angle, 1.0, out _);
            if (meshData != null && meshData.Length > 0)
                bw.Write(meshData);
        }

        public void Mesh(IXbimMeshReceiver receiver, IXbimGeometryObject geometryObject, double precision, double deflection, double angle)
        {
            IXShape v6Shape = ExtractV6Shape(geometryObject);
            if (v6Shape == null)
            {
                _logger.LogWarning("Mesh: unable to extract shape from geometry object.");
                return;
            }

            byte[] meshData = _service.WexBimMeshFactory.CreateWexBimMesh
                            (v6Shape, precision, deflection, angle, 1.0, out _);
            if (meshData == null || meshData.Length == 0)
                return;

            var mesh = new WexBimMesh(meshData);

            receiver.BeginUpdate();
            try
            {
                foreach (var face in mesh.Faces)
                {
                    int faceId = receiver.AddFace();
                    var allIndices = face.Indices.ToArray(); // length = TriangleCount * 3
                    int triCount = face.TriangleCount;

                    if (face.IsPlanar)
                    {
                        // Single normal for the entire face — deduplicate nodes by global vertex index
                        var faceNormal = face.NormalAt(0);
                        double nx = faceNormal.X, ny = faceNormal.Y, nz = faceNormal.Z;

                        var nodeMap = new Dictionary<int, int>(triCount * 3);
                        var triangles = new (int a, int b, int c)[triCount];

                        for (int i = 0; i < triCount; i++)
                        {
                            int gA = allIndices[i * 3];
                            int gB = allIndices[i * 3 + 1];
                            int gC = allIndices[i * 3 + 2];

                            if (!nodeMap.TryGetValue(gA, out int nA))
                            {
                                var v = mesh[gA];
                                nA = receiver.AddNode(faceId, v.X, v.Y, v.Z, nx, ny, nz);
                                nodeMap[gA] = nA;
                            }
                            if (!nodeMap.TryGetValue(gB, out int nB))
                            {
                                var v = mesh[gB];
                                nB = receiver.AddNode(faceId, v.X, v.Y, v.Z, nx, ny, nz);
                                nodeMap[gB] = nB;
                            }
                            if (!nodeMap.TryGetValue(gC, out int nC))
                            {
                                var v = mesh[gC];
                                nC = receiver.AddNode(faceId, v.X, v.Y, v.Z, nx, ny, nz);
                                nodeMap[gC] = nC;
                            }

                            triangles[i] = (nA, nB, nC);
                        }

                        foreach (var (a, b, c) in triangles)
                            receiver.AddTriangle(faceId, a, b, c);
                    }
                    else
                    {
                        // Per-vertex normals — deduplicate by global vertex index, using the
                        // normal from the first occurrence
                        var nodeMap = new Dictionary<int, int>(triCount * 3);
                        var triangleNodeIds = new int[triCount * 3];

                        for (int i = 0; i < triCount; i++)
                        {
                            for (int j = 0; j < 3; j++)
                            {
                                int pos = i * 3 + j;
                                int gIdx = allIndices[pos];
                                if (!nodeMap.TryGetValue(gIdx, out int nodeId))
                                {
                                    var vertex = mesh[gIdx];
                                    var normal = face.NormalAt(pos);
                                    nodeId = receiver.AddNode(faceId, vertex.X, vertex.Y, vertex.Z, normal.X, normal.Y, normal.Z);
                                    nodeMap[gIdx] = nodeId;
                                }
                                triangleNodeIds[pos] = nodeId;
                            }
                        }

                        for (int i = 0; i < triCount; i++)
                            receiver.AddTriangle(faceId, triangleNodeIds[i * 3], triangleNodeIds[i * 3 + 1], triangleNodeIds[i * 3 + 2]);
                    }
                }
            }
            finally
            {
                receiver.EndUpdate();
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Extracts the underlying V6 shape from a geometry object.
        /// For sets, builds a compound shape from all constituent objects.
        /// </summary>
        private IXShape ExtractV6Shape(IXbimGeometryObject geometryObject)
        {
            if (geometryObject is XbimShape shape)
                return shape;

            // For sets, build a compound from all elements
            if (geometryObject.IsSet && geometryObject is IEnumerable<IXbimGeometryObject> set)
            {
                var shapeHandles = new List<NativeShapeHandle>();
                foreach (var item in set)
                {
                    if (item is XbimShape s)
                        shapeHandles.Add(s.Handle);
                }

                if (shapeHandles.Count == 0)
                    return null;

                using var nativeHandles = new NativeHandleArray(shapeHandles.ToArray());
                int result = XbimGeometryNativeApi.xbim_compound_make(
                    _service.ContextHandle,
                    nativeHandles.Ptrs,
                    nativeHandles.Length,
                    out var compoundHandle);

                if (result != 0)
                {
                    _logger.LogWarning("Failed to create compound from set: {Error}",
                        XbimGeometryNativeApi.GetLastError());
                    return null;
                }

                return NativeShapeWrapper.WrapShape(compoundHandle);
            }

            return null;
        }

        /// <summary>
        /// Builds a solid model via V6 factory and returns as IXbimSolid.
        /// </summary>
        private IXbimSolid BuildAsSolid(IIfcSolidModel solidModel)
        {
            var shape = _service.SolidFactory.Build(solidModel);
            return WrapShapeAsSolid(shape);
        }

        /// <summary>
        /// Returns an IXShape result as IXbimSolid.
        /// If the result is already a solid, casts directly.
        /// If it's a compound of solids, extracts them into an <see cref="XbimSolidSet"/>
        /// which implements both IXbimSolid and IXbimSolidSet.
        /// </summary>
        private static IXbimSolid WrapShapeAsSolid(IXShape shape)
        {
            if (shape is XbimSolid solid)
                return solid;
            if (shape is XbimSolidSet solidSet)
                return solidSet;

            var s = (XbimShape)shape;
            var handles = s.GetSubShapeHandles(XShapeType.Solid);
            if (handles.Length == 1)
                return new XbimSolid(handles[0]);
            if (handles.Length > 1)
                return new XbimSolidSet(handles.Select(h => (IXbimSolid)new XbimSolid(h)));

            throw new XbimGeometryServiceException("Build result is not a solid.");
        }

        /// <summary>
        /// Returns an IXShape result as IXbimSolidSet.
        /// </summary>
        private static IXbimSolidSet WrapShapeAsSolidSet(IXShape shape)
        {
            if (shape is XbimSolidSet solidSet)
                return solidSet;

            if (shape is XbimSolid solid)
                return new XbimSolidSet(new IXbimSolid[] { solid });

            var s = (XbimShape)shape;
            return new XbimSolidSet(s);
        }

        /// <summary>
        /// Wraps a V6 shape as an IXbimGeometryObjectSet.
        /// </summary>
        private static IXbimGeometryObjectSet WrapShapeAsGeometryObjectSet(IXShape shape)
        {
            return new XbimGeometryObjectSet(new IXbimGeometryObject[] { (IXbimGeometryObject)shape });
        }

        /// <summary>
        /// Creates an unbounded face from a built surface.
        /// For bounded surfaces (e.g. IfcCurveBoundedPlane) that are already represented
        /// as a face shape, wraps the handle directly. For geometric surfaces, calls the
        /// native unbounded face builder.
        /// </summary>
        private IXbimFace SurfaceToFace(IXSurface built, int entityLabel)
        {
            if (built is FaceSurface faceSurface)
                return (IXbimFace)NativeShapeWrapper.WrapFace(faceSurface.Handle);

            if (built is Surface surf)
            {
                int result = XbimGeometryNativeApi.xbim_face_build_unbounded_from_surface(
                    _service.ContextHandle, surf.Handle, _service.Precision, out var faceHandle);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build face from surface #{entityLabel}: {XbimGeometryNativeApi.GetLastError()}");
                return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
            }

            throw new NotSupportedException(
                $"Cannot create face from surface type {built.GetType().Name} #{entityLabel}.");
        }

        /// <summary>
        /// Builds a planar face from a wire shape.
        /// </summary>
        private IXbimFace WrapWireAsFace(IXWire wire)
        {
            var wireShape = wire as XbimWire;
            if (wireShape == null)
                throw new XbimGeometryServiceException("Wire must be a XbimWire instance.");

            int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                _service.ContextHandle, wireShape.Handle, out var faceHandle);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build face from wire: {XbimGeometryNativeApi.GetLastError()}");

            return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
        }

        /// <summary>
        /// Moves a shape using a native location handle, returning a new shape.
        /// </summary>
        private static IXbimGeometryObject MoveShape(XbimShape shape, XLocation location)
        {
            int moveResult = XbimGeometryNativeApi.xbim_shape_moved(
                shape.Handle, location.Handle, out var movedHandle);
            if (moveResult != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to move shape: {XbimGeometryNativeApi.GetLastError()}");
            return (IXbimGeometryObject)NativeShapeWrapper.WrapShape(movedHandle);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _service.Dispose();
        }

        #endregion
    }
}
