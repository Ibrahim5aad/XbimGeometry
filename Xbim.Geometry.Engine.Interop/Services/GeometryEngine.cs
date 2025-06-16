using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
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

        #region IXbimGeometryEngine (legacy V5 API — stubs)

        public IXbimGeometryObject Create(IIfcGeometricRepresentationItem ifcRepresentation, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 Create API not yet implemented. Use the V6 factory API instead.");
        }

        public IXbimGeometryObject Create(IIfcGeometricRepresentationItem ifcRepresentation, IIfcAxis2Placement3D objectLocation, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 Create API not yet implemented. Use the V6 factory API instead.");
        }

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

        public IXbimSolid CreateSolid(IIfcSweptAreaSolid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcExtrudedAreaSolid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcRevolvedAreaSolid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcSweptDiskSolid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcBoundingBox ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcSurfaceCurveSweptAreaSolid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcBooleanClippingResult ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcBooleanOperand ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcHalfSpaceSolid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcPolygonalBoundedHalfSpace ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcBoxedHalfSpace ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcManifoldSolidBrep ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcFacetedBrep ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcFacetedBrepWithVoids ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcClosedShell ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcCsgPrimitive3D ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcCsgSolid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcSphere ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcBlock ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcRightCircularCylinder ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcRightCircularCone ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcRectangularPyramid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcSweptDiskSolidPolygonal ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcRevolvedAreaSolidTapered ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcFixedReferenceSweptAreaSolid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcAdvancedBrep ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcAdvancedBrepWithVoids ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcSectionedSpine ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcSweptAreaSolid ifcSolid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcBooleanResult boolOp, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcTriangulatedFaceSet shell, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcPolygonalFaceSet shell, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcTriangulatedFaceSet shell, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolid CreateSolid(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcTessellatedFaceSet shell, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcPolygonalFaceSet shell, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateSolidSet()
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObjectSet CreateGeometryObjectSet()
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimSolidSet CreateGrid(IIfcGrid grid, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimFace CreateFace(IIfcProfileDef profileDef, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimFace CreateFace(IIfcCompositeCurve cCurve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimFace CreateFace(IIfcPolyline pline, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimFace CreateFace(IIfcPolyLoop loop, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimFace CreateFace(IIfcSurface surface, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimFace CreateFace(IIfcPlane plane, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimFace CreateFace(IXbimWire wire, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimShell CreateShell(IIfcOpenShell shell, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimShell CreateShell(IIfcConnectedFaceSet shell, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimShell CreateShell(IIfcSurfaceOfLinearExtrusion linExt, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimWire CreateWire(IIfcCurve curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimWire CreateWire(IIfcCompositeCurveSegment compCurveSeg, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcCurve curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcPolyline ifcPolyline, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcCircle curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcEllipse curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcLine curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcTrimmedCurve curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcBSplineCurveWithKnots curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcRationalBSplineCurveWithKnots curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcOffsetCurve3D curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimCurve CreateCurve(IIfcOffsetCurve2D curve, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimPoint CreatePoint(double x, double y, double z, double tolerance)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimPoint CreatePoint(IIfcCartesianPoint p)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimPoint CreatePoint(XbimPoint3D p, double tolerance)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimPoint CreatePoint(IIfcPoint pt)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimPoint CreatePoint(IIfcPointOnCurve p, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimPoint CreatePoint(IIfcPointOnSurface p, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimVertex CreateVertexPoint(XbimPoint3D point, double precision)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public XbimMatrix3D ToMatrix3D(IIfcObjectPlacement objPlacement, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObject Transformed(IXbimGeometryObject geometry, IIfcCartesianTransformationOperator cartesianTransform)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcPlacement placement)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcAxis2Placement3D placement)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcAxis2Placement2D placement)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcObjectPlacement objectPlacement, ILogger logger)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObject FromBrep(string brepStr)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public string ToBrep(IXbimGeometryObject geometryObject)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public void WriteBrep(string filename, IXbimGeometryObject geomObj)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public IXbimGeometryObject ReadBrep(string filename)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public void WriteTriangulation(TextWriter tw, IXbimGeometryObject shape, double tolerance, double deflection, double angle)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public void WriteTriangulation(BinaryWriter bw, IXbimGeometryObject shape, double tolerance, double deflection, double angle)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
        }

        public void Mesh(IXbimMeshReceiver receiver, IXbimGeometryObject geometryObject, double precision, double deflection, double angle)
        {
            throw new NotImplementedException("Legacy V5 API not yet implemented.");
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
