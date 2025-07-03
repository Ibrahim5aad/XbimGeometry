using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Shapes.V5;
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
            if (geomRep == null)
                throw new ArgumentNullException(nameof(geomRep));

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
                throw new InvalidOperationException(
                    $"BoundingBox has zero or negative dimensions.");

            var corner = bbox.Corner;
            double ox = corner.X, oy = corner.Y, oz = corner.Z;

            int result = Internal.XbimGeometryNativeApi.xbim_solid_build_block(
                _service.ContextHandle,
                ox, oy, oz,
                0, 0, 1, // Z direction
                1, 0, 0, // X direction
                xLen, yLen, zLen,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build BoundingBox solid: {Internal.XbimGeometryNativeApi.GetLastError()}");

            return Shapes.ShapeFactory.WrapSolid(NativeShapeHandle);
        }

        #endregion

        #region IXModelGeometryService delegation

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

        #region IXbimGeometryEngine (legacy V5 API)

        // --- Generic Create ---

        public IXbimGeometryObject Create(IIfcGeometricRepresentationItem ifcRepresentation, ILogger logger)
        {
            var shape = Build(ifcRepresentation);
            return V5Shape.Wrap(shape);
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
                        ((Shape)shape).Handle, location.Handle, out var movedHandle);
                    if (moveResult != 0)
                        throw new InvalidOperationException(
                            $"Failed to apply placement: {XbimGeometryNativeApi.GetLastError()}");
                    shape = Shapes.ShapeFactory.WrapShape(movedHandle);
                }
            }
            return V5Shape.Wrap(shape);
        }

        // --- CreateShapeGeometry (deferred to V5COMPAT-007) ---

        public XbimShapeGeometry CreateShapeGeometry(IXbimGeometryObject geometryObject, double precision, double deflection, double angle, XbimGeometryType storageType, ILogger logger)
        {
            throw new NotImplementedException("CreateShapeGeometry not yet implemented.");
        }

        public XbimShapeGeometry CreateShapeGeometry(IXbimGeometryObject geometryObject, double precision, double deflection, ILogger logger)
        {
            throw new NotImplementedException("CreateShapeGeometry not yet implemented.");
        }

        public XbimShapeGeometry CreateShapeGeometry(double oneMillimetre, IXbimGeometryObject geometryObject, double precision, ILogger logger)
        {
            throw new NotImplementedException("CreateShapeGeometry not yet implemented.");
        }

        // --- CreateSolid overloads ---

        public IXbimSolid CreateSolid(IIfcSweptAreaSolid ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcExtrudedAreaSolid ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcRevolvedAreaSolid ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcSweptDiskSolid ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcBoundingBox ifcSolid, ILogger logger)
            => V5Shape.WrapSolid((IXSolid)BuildBoundingBox(ifcSolid));

        public IXbimSolid CreateSolid(IIfcSurfaceCurveSweptAreaSolid ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcHalfSpaceSolid ifcSolid, ILogger logger)
            => V5Shape.WrapSolid((IXSolid)_service.SolidFactory.Build(ifcSolid));

        public IXbimSolid CreateSolid(IIfcPolygonalBoundedHalfSpace ifcSolid, ILogger logger)
            => V5Shape.WrapSolid((IXSolid)_service.SolidFactory.Build((IIfcHalfSpaceSolid)ifcSolid));

        public IXbimSolid CreateSolid(IIfcBoxedHalfSpace ifcSolid, ILogger logger)
            => V5Shape.WrapSolid((IXSolid)_service.SolidFactory.Build((IIfcHalfSpaceSolid)ifcSolid));

        public IXbimSolid CreateSolid(IIfcCsgPrimitive3D ifcSolid, ILogger logger)
            => V5Shape.WrapSolid(_service.SolidFactory.Build(ifcSolid));

        public IXbimSolid CreateSolid(IIfcSphere ifcSolid, ILogger logger)
            => V5Shape.WrapSolid(_service.SolidFactory.Build((IIfcCsgPrimitive3D)ifcSolid));

        public IXbimSolid CreateSolid(IIfcBlock ifcSolid, ILogger logger)
            => V5Shape.WrapSolid(_service.SolidFactory.Build((IIfcCsgPrimitive3D)ifcSolid));

        public IXbimSolid CreateSolid(IIfcRightCircularCylinder ifcSolid, ILogger logger)
            => V5Shape.WrapSolid(_service.SolidFactory.Build((IIfcCsgPrimitive3D)ifcSolid));

        public IXbimSolid CreateSolid(IIfcRightCircularCone ifcSolid, ILogger logger)
            => V5Shape.WrapSolid(_service.SolidFactory.Build((IIfcCsgPrimitive3D)ifcSolid));

        public IXbimSolid CreateSolid(IIfcRectangularPyramid ifcSolid, ILogger logger)
            => V5Shape.WrapSolid(_service.SolidFactory.Build((IIfcCsgPrimitive3D)ifcSolid));

        public IXbimSolid CreateSolid(IIfcSweptDiskSolidPolygonal ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcRevolvedAreaSolidTapered ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcFixedReferenceSweptAreaSolid ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcAdvancedBrep ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcAdvancedBrepWithVoids ifcSolid, ILogger logger)
            => WrapBuildAsSolid((IIfcSolidModel)ifcSolid);

        public IXbimSolid CreateSolid(IIfcSectionedSpine ifcSolid, ILogger logger)
        {
            var shape = _service.SolidFactory.Build(ifcSolid);
            return WrapShapeAsSolid(shape);
        }

        public IXbimSolid CreateSolid(IIfcTriangulatedFaceSet shell, ILogger logger)
        {
            var shape = _service.SolidFactory.Build((IIfcTessellatedItem)shell);
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
            => new V5SolidSet();

        public IXbimSolidSet CreateSolidSet(IIfcBooleanClippingResult ifcSolid, ILogger logger)
        {
            var shape = _service.BooleanFactory.Build((IIfcBooleanResult)ifcSolid);
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
            => WrapShapeAsSolidSet(_service.SolidFactory.Build((IIfcSolidModel)ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcFacetedBrep ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcFacetedBrepWithVoids ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build((IIfcSolidModel)ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcClosedShell ifcSolid, ILogger logger)
        {
            // Build the shell as a shell-based surface model, then extract solids
            var shape = Build((IIfcGeometricRepresentationItem)ifcSolid);
            return WrapShapeAsSolidSet(shape);
        }

        public IXbimSolidSet CreateSolidSet(IIfcSweptAreaSolid ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build((IIfcSolidModel)ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcCsgSolid ifcSolid, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build((IIfcSolidModel)ifcSolid));

        public IXbimSolidSet CreateSolidSet(IIfcTriangulatedFaceSet shell, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build((IIfcTessellatedItem)shell));

        public IXbimSolidSet CreateSolidSet(IIfcPolygonalFaceSet shell, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build((IIfcTessellatedItem)shell));

        public IXbimSolidSet CreateSolidSet(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSurface));

        public IXbimSolidSet CreateSolidSet(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
            => WrapShapeAsSolidSet(_service.SolidFactory.Build(ifcSurface));

        // --- CreateFace overloads ---

        public IXbimFace CreateFace(IIfcProfileDef profileDef, ILogger logger)
        {
            var face = _service.ProfileFactory.BuildFace(profileDef);
            return V5Shape.WrapFace(face);
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
            // Extract points from the polyloop and build a wire, then make a face
            var points = new System.Collections.Generic.List<IXPoint>();
            foreach (var pt in loop.Polygon)
            {
                double x = pt.Coordinates[0];
                double y = pt.Coordinates[1];
                double z = (int)pt.Dim == 3 ? (double)pt.Coordinates[2] : 0.0;
                points.Add(new XPoint(x, y, z));
            }
            var wire = _service.WireFactory.BuildWire(points.ToArray());
            return WrapWireAsFace(wire);
        }

        public IXbimFace CreateFace(IIfcSurface surface, ILogger logger)
            => throw new NotSupportedException("CreateFace from IIfcSurface not yet supported.");

        public IXbimFace CreateFace(IIfcPlane plane, ILogger logger)
            => throw new NotSupportedException("CreateFace from IIfcPlane not yet supported.");

        public IXbimFace CreateFace(IXbimWire wire, ILogger logger)
        {
            if (wire is V5Wire v5Wire)
            {
                var innerWire = v5Wire.Inner as Wire;
                if (innerWire != null)
                {
                    int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                        _service.ContextHandle, innerWire.Handle, out var faceHandle);
                    if (result != 0)
                        throw new InvalidOperationException(
                            $"Failed to build face from wire: {XbimGeometryNativeApi.GetLastError()}");
                    return V5Shape.WrapFace(Shapes.ShapeFactory.WrapFace(faceHandle));
                }
            }
            throw new InvalidOperationException("Wire must be a V5Wire from this geometry engine.");
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
            var shape = _service.SolidFactory.Build((IIfcTessellatedItem)shell);
            return WrapShapeAsGeometryObjectSet(shape);
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcPolygonalFaceSet shell, ILogger logger)
        {
            var shape = _service.SolidFactory.Build((IIfcTessellatedItem)shell);
            return WrapShapeAsGeometryObjectSet(shape);
        }

        // --- Empty set creation ---

        public IXbimGeometryObjectSet CreateGeometryObjectSet()
            => new V5GeometryObjectSet();

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
            return new V5Wire((Wire)wire);
        }

        public IXbimWire CreateWire(IIfcCompositeCurveSegment compCurveSeg, ILogger logger)
            => throw new NotSupportedException("CreateWire from IIfcCompositeCurveSegment not yet supported.");

        // --- Grid ---

        public IXbimSolidSet CreateGrid(IIfcGrid grid, ILogger logger)
            => throw new NotSupportedException("CreateGrid not yet supported.");

        // --- Curve creation (not yet supported) ---

        public IXbimCurve CreateCurve(IIfcCurve curve, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        public IXbimCurve CreateCurve(IIfcPolyline ifcPolyline, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        public IXbimCurve CreateCurve(IIfcCircle curve, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        public IXbimCurve CreateCurve(IIfcEllipse curve, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        public IXbimCurve CreateCurve(IIfcLine curve, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        public IXbimCurve CreateCurve(IIfcTrimmedCurve curve, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        public IXbimCurve CreateCurve(IIfcBSplineCurveWithKnots curve, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        public IXbimCurve CreateCurve(IIfcRationalBSplineCurveWithKnots curve, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        public IXbimCurve CreateCurve(IIfcOffsetCurve3D curve, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        public IXbimCurve CreateCurve(IIfcOffsetCurve2D curve, ILogger logger)
            => throw new NotSupportedException("V5 curve creation not yet supported.");

        // --- Point creation (not yet supported) ---

        public IXbimPoint CreatePoint(double x, double y, double z, double tolerance)
            => throw new NotSupportedException("V5 point creation not yet supported.");

        public IXbimPoint CreatePoint(IIfcCartesianPoint p)
            => throw new NotSupportedException("V5 point creation not yet supported.");

        public IXbimPoint CreatePoint(XbimPoint3D p, double tolerance)
            => throw new NotSupportedException("V5 point creation not yet supported.");

        public IXbimPoint CreatePoint(IIfcPoint pt)
            => throw new NotSupportedException("V5 point creation not yet supported.");

        public IXbimPoint CreatePoint(IIfcPointOnCurve p, ILogger logger)
            => throw new NotSupportedException("V5 point creation not yet supported.");

        public IXbimPoint CreatePoint(IIfcPointOnSurface p, ILogger logger)
            => throw new NotSupportedException("V5 point creation not yet supported.");

        // --- Vertex creation ---

        public IXbimVertex CreateVertexPoint(XbimPoint3D point, double precision)
        {
            int result = XbimGeometryNativeApi.xbim_vertex_build(
                _service.ContextHandle, point.X, point.Y, point.Z, precision, out var handle);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create vertex: {XbimGeometryNativeApi.GetLastError()}");
            return new V5Vertex(new Vertex(handle));
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
            var v5 = geometry as V5Shape;
            if (v5 == null)
                throw new InvalidOperationException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var matrix = gf.BuildTransform(cartesianTransform);

            // Convert IXMatrix to XbimMatrix3D to apply via V5Shape.Transform
            var m = new XbimMatrix3D(
                matrix.M11, matrix.M12, matrix.M13, 0,
                matrix.M21, matrix.M22, matrix.M23, 0,
                matrix.M31, matrix.M32, matrix.M33, 0,
                matrix.OffsetX, matrix.OffsetY, matrix.OffsetZ, 1);

            return v5.Transform(m);
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcPlacement placement)
        {
            var v5 = geometryObject as V5Shape;
            if (v5 == null)
                throw new InvalidOperationException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var loc = gf.BuildLocation(placement);
            return MoveShape(v5, (XLocation)loc);
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcAxis2Placement3D placement)
        {
            var v5 = geometryObject as V5Shape;
            if (v5 == null)
                throw new InvalidOperationException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var loc = gf.BuildLocationFromAxis3D(placement);
            return MoveShape(v5, loc);
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcAxis2Placement2D placement)
        {
            var v5 = geometryObject as V5Shape;
            if (v5 == null)
                throw new InvalidOperationException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var loc = gf.BuildLocationFromAxis2D(placement);
            return MoveShape(v5, loc);
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcObjectPlacement objectPlacement, ILogger logger)
        {
            var v5 = geometryObject as V5Shape;
            if (v5 == null)
                throw new InvalidOperationException("Geometry must originate from this geometry engine.");

            var gf = (GeometryFactory)_service.GeometryFactory;
            var loc = gf.ToLocation(objectPlacement);
            return MoveShape(v5, loc);
        }

        // --- BRep I/O ---

        public IXbimGeometryObject FromBrep(string brepStr)
        {
            var shape = Services.ShapeBinarySerializer.FromBrep(brepStr);
            return V5Shape.Wrap(shape);
        }

        public string ToBrep(IXbimGeometryObject geometryObject)
        {
            var v5 = geometryObject as V5Shape;
            if (v5 == null)
                throw new InvalidOperationException("Geometry must originate from this geometry engine.");
            return v5.Inner.BrepString();
        }

        public void WriteBrep(string filename, IXbimGeometryObject geomObj)
        {
            var v5 = geomObj as V5Shape;
            if (v5 == null)
                throw new InvalidOperationException("Geometry must originate from this geometry engine.");
            ((Shape)v5.Inner).WriteBrep(filename);
        }

        public IXbimGeometryObject ReadBrep(string filename)
        {
            string brepStr = File.ReadAllText(filename);
            return FromBrep(brepStr);
        }

        // --- Triangulation / Mesh (deferred) ---

        public void WriteTriangulation(TextWriter tw, IXbimGeometryObject shape, double tolerance, double deflection, double angle)
            => throw new NotSupportedException("WriteTriangulation not yet supported.");

        public void WriteTriangulation(BinaryWriter bw, IXbimGeometryObject shape, double tolerance, double deflection, double angle)
            => throw new NotSupportedException("WriteTriangulation not yet supported.");

        public void Mesh(IXbimMeshReceiver receiver, IXbimGeometryObject geometryObject, double precision, double deflection, double angle)
            => throw new NotSupportedException("V5 Mesh not yet supported.");

        #endregion

        #region V5 Helpers

        /// <summary>
        /// Builds a solid model via V6 factory and wraps as V5 solid.
        /// </summary>
        private IXbimSolid WrapBuildAsSolid(IIfcSolidModel solidModel)
        {
            var shape = _service.SolidFactory.Build(solidModel);
            return WrapShapeAsSolid(shape);
        }

        /// <summary>
        /// Wraps a V6 shape result as a V5 solid.
        /// If the result is already a solid, wraps directly.
        /// If it's a compound, extracts the first solid.
        /// </summary>
        private static IXbimSolid WrapShapeAsSolid(IXShape shape)
        {
            if (shape is IXSolid solid)
                return V5Shape.WrapSolid(solid);

            // For compound results, extract the first solid
            var s = (Shape)shape;
            var solidHandles = s.GetSubShapeHandles(XShapeType.Solid);
            if (solidHandles.Length > 0)
                return new V5Solid(new Solid(solidHandles[0]));

            throw new InvalidOperationException("Build result is not a solid.");
        }

        /// <summary>
        /// Wraps a V6 shape result as a V5 solid set.
        /// Handles both single solids and compound shapes with multiple solids.
        /// </summary>
        private static IXbimSolidSet WrapShapeAsSolidSet(IXShape shape)
        {
            if (shape is IXSolid solid)
                return new V5SolidSet(new[] { V5Shape.WrapSolid(solid) });

            var s = (Shape)shape;
            return new V5SolidSet(s);
        }

        /// <summary>
        /// Wraps a V6 shape result as a V5 geometry object set.
        /// </summary>
        private static IXbimGeometryObjectSet WrapShapeAsGeometryObjectSet(IXShape shape)
        {
            var wrapped = V5Shape.Wrap(shape);
            return new V5GeometryObjectSet(new[] { wrapped });
        }

        /// <summary>
        /// Builds a planar face from a wire shape.
        /// </summary>
        private IXbimFace WrapWireAsFace(IXWire wire)
        {
            var wireShape = wire as Wire;
            if (wireShape == null)
                throw new InvalidOperationException("Wire must be a Wire instance.");

            int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                _service.ContextHandle, wireShape.Handle, out var faceHandle);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build face from wire: {XbimGeometryNativeApi.GetLastError()}");

            return V5Shape.WrapFace(Shapes.ShapeFactory.WrapFace(faceHandle));
        }

        /// <summary>
        /// Moves a V5Shape using a native location handle, returning a new wrapped V5 shape.
        /// </summary>
        private static IXbimGeometryObject MoveShape(V5Shape v5, XLocation location)
        {
            using (location)
            {
                int moveResult = XbimGeometryNativeApi.xbim_shape_moved(
                    v5.Inner.Handle, location.Handle, out var movedHandle);
                if (moveResult != 0)
                    throw new InvalidOperationException(
                        $"Failed to move shape: {XbimGeometryNativeApi.GetLastError()}");
                return V5Shape.Wrap(Shapes.ShapeFactory.WrapShape(movedHandle));
            }
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
