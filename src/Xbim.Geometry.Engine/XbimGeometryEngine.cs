using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xbim.Common;
using Xbim.Common.Configuration;
using Xbim.Common.Exceptions;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Abstractions.Extensions;
using Xbim.Geometry.Engine.Configuration;
using Xbim.Geometry.Engine.Factories;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Primitives;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Geometry.Exceptions;
using Xbim.Geometry.WexBim;
using Xbim.Ifc;
using Xbim.Ifc4;
using Xbim.Ifc4.Interfaces;



namespace Xbim.Geometry.Engine
{
    /// <summary>
    /// The xbim Geometry Engine.
    /// </summary>
    /// <remarks>This managed class provides an interoperability layer to the underlying
    /// native geometry engine. It implements both the managed engine interface for DI
    /// consumers and the V6 geometry service interface for direct model access.</remarks>
    public class XbimGeometryEngine : IXbimManagedGeometryEngine, IXGeometryEngineV6, IDisposable
    {
        private const string ModelGeometryServiceKey = "ModelGeometryService";
        private ModelGeometryService _service;
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private GeometryEngineOptions _engineOptions;


        static XbimGeometryEngine()
        {
            // Initialise services with defaults. This honours any registrations made earlier.
            if (XbimServices.Current.IsBuilt == false)
            {
                XbimServices.Current.ConfigureServices(opt => opt.AddXbimToolkit(conf => conf.AddGeometryServices()));
            }
        }

        private XbimGeometryEngine() { }


        /// <summary>
        /// Creates an instance of <see cref="XbimGeometryEngine"/> for use with dependency injection.
        /// A model must be registered using <see cref="RegisterModel(IModel)"/> before invoking any geometry functions.
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="geometryOptions"></param>
        public XbimGeometryEngine(ILoggerFactory loggerFactory, IOptions<GeometryEngineOptions> geometryOptions = null)
        {
            _engineOptions = geometryOptions?.Value ?? new GeometryEngineOptions();
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<XbimGeometryEngine>();

            _logger.LogDebug("XbimGeometryEngine constructed successfully");
        }


        /// <summary>
        /// Creates an instance of <see cref="XbimGeometryEngine"/> and registers the provided model with the Geometry Engine
        /// </summary>
        /// <param name="model"></param>
        /// <param name="loggerFactory"></param>
        /// <param name="options"></param>
        public XbimGeometryEngine(IModel model, ILoggerFactory loggerFactory, GeometryEngineOptions options = null)
        {
            _engineOptions = options ?? new GeometryEngineOptions();
            _loggerFactory = loggerFactory ?? InternalServiceProvider.GetRequiredService<ILoggerFactory>();
            _logger = _loggerFactory.CreateLogger<XbimGeometryEngine>();

            try
            {
                RegisterModel(model);
                _logger.LogDebug("XbimGeometryEngine constructed successfully");
            }
            catch (Exception e)
            {
                _logger.LogError(0, e, "Failed to construct XbimGeometryEngine");
                throw;
            }
        }

        /// <summary>
        /// Gets and sets the <see cref="GeometryEngineOptions"/>
        /// </summary>
        public GeometryEngineOptions EngineOptions
        {
            get { return _engineOptions; }
            internal set { _engineOptions = value; }
        }

        /// <summary>
        /// Gets the ModelService associated with the engine
        /// </summary>
        public IXModelGeometryService ModelService => _service;

        /// <summary>
        /// Gets the underlying model geometry service, ensuring the engine has been initialized.
        /// </summary>
        private ModelGeometryService Service =>
            _service ?? throw new InvalidOperationException("Models must be registered before invoking the Geometry Engine");

        /// <summary>
        /// Associates a new geometry Engine instance and services with the provided <see cref="IModel"/>
        /// </summary>
        /// <param name="model">The model to register</param>
        public void RegisterModel(IModel model)
        {
            _service = new ModelGeometryService(model, _loggerFactory);
            _logger.LogTrace("Created ModelGeometryService for model");
            EnsureModelTagged(model);
        }

        /// <summary>
        /// Unregisters a model with the underlying geometry Engine services
        /// </summary>
        /// <param name="model"></param>
        public void UnregisterModel(IModel model)
        {
            IModel underlyingModel = GetModel(model);
            underlyingModel.RemoveTagValue(ModelGeometryServiceKey);
        }

        private bool EnsureModelTagged(IModel model)
        {
            bool result = true;
            IModel underlyingModel = GetModel(model);
            if (underlyingModel.GetTagValue<IXModelGeometryService>(ModelGeometryServiceKey, out _) == false)
            {
                result = underlyingModel.AddTagValue(ModelGeometryServiceKey, (IXModelGeometryService)_service);
            }
            if (!result)
            {
                _logger.LogError("Model.Tag should be null or a Dictionary<string, object>");
                throw new XbimGeometryException("Failed to initialise Model with the Geometry Engine.\n\n IModel.Tag is expected to be null or a Dictionary<string, object>");
            }

            return result;
        }

        private static IModel GetModel(IModel model)
        {
            IModel underlyingModel = model;
            if (model is IfcStore ifcStore)
            {
                underlyingModel = ifcStore.Model;
            }

            return underlyingModel;
        }


        #region IXGeometryEngineV6

        /// <inheritdoc/>
        public IXModelGeometryService ModelGeometryService => _service;

        /// <inheritdoc/>
        public IXShape Build(IIfcGeometricRepresentationItem geomRep)
        {
            ArgumentNullException.ThrowIfNull(geomRep);

            // Solid models (extruded, revolved, CSG, swept disk, BRep, etc.)
            if (geomRep is IIfcSolidModel solidModel)
                return Service.SolidFactory.Build(solidModel);

            // Boolean results (union, cut, intersect)
            if (geomRep is IIfcBooleanResult boolResult)
                return Service.BooleanFactory.Build(boolResult);

            // Half-space solids (clipping planes)
            if (geomRep is IIfcHalfSpaceSolid halfSpace)
                return Service.SolidFactory.Build(halfSpace);

            // CSG primitives (block, sphere, cylinder, cone, pyramid)
            if (geomRep is IIfcCsgPrimitive3D csgPrimitive)
                return Service.SolidFactory.Build(csgPrimitive);

            // Surface models
            if (geomRep is IIfcFaceBasedSurfaceModel faceBasedSurface)
                return Service.SolidFactory.Build(faceBasedSurface);

            if (geomRep is IIfcShellBasedSurfaceModel shellBasedSurface)
                return Service.SolidFactory.Build(shellBasedSurface);

            // Tessellated items
            if (geomRep is IIfcTessellatedItem tessellated)
                return Service.SolidFactory.Build(tessellated);

            // Faceted BRep
            if (geomRep is IIfcFacetedBrep facetedBrep)
                return Service.SolidFactory.Build(facetedBrep);

            // Sectioned spine
            if (geomRep is IIfcSectionedSpine sectionedSpine)
                return Service.SolidFactory.Build(sectionedSpine);

            if (geomRep is IIfcCurve curve)
                return Service.WireFactory.Build(curve);

            // Bounding box (build as a simple block)
            if (geomRep is IIfcBoundingBox boundingBox)
                return BuildBoundingBox(boundingBox);

            // Surfaces we build as shapes
            if (geomRep is IIfcCurveBoundedPlane ifcCurveBoundedPlane)
            {
                var faceSurface = (Service.SurfaceFactory as SurfaceFactory).BuildCurveBoundedPlane(ifcCurveBoundedPlane);
                return NativeShapeWrapper.WrapFace(faceSurface.Handle);
            }

            if (geomRep is IIfcCurveBoundedSurface ifcCurveBoundedSurface)
            {
                var faceSurface = (Service.SurfaceFactory as SurfaceFactory).BuildCurveBoundedSurface(ifcCurveBoundedSurface);
                return NativeShapeWrapper.WrapFace(faceSurface.Handle);
            }

            throw new XbimGeometryNotSupportedException(
                $"Build: unsupported geometric representation type {geomRep.GetType().Name} (#{(geomRep as IPersistEntity)?.EntityLabel}).");
        }

        #endregion

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

        #region IXModelGeometryService forwarding

        public IXLoggingService LoggingService => Service.LoggingService;
        public IXVertexFactory VertexFactory => Service.VertexFactory;
        public IXGeometryFactory GeometryFactory => Service.GeometryFactory;
        public IXCurveFactory CurveFactory => Service.CurveFactory;
        public IXSurfaceFactory SurfaceFactory => Service.SurfaceFactory;
        public IXEdgeFactory EdgeFactory => Service.EdgeFactory;
        public IXWireFactory WireFactory => Service.WireFactory;
        public IXFaceFactory FaceFactory => Service.FaceFactory;
        public IXShellFactory ShellFactory => Service.ShellFactory;
        public IXSolidFactory SolidFactory => Service.SolidFactory;
        public IXCompoundFactory CompoundFactory => Service.CompoundFactory;
        public IXBooleanFactory BooleanFactory => Service.BooleanFactory;
        public IXShapeFactory ShapeFactory => Service.ShapeFactory;
        public IXProfileFactory ProfileFactory => Service.ProfileFactory;
        public IXMaterialFactory MaterialFactory => Service.MaterialFactory;
        public IXProjectionFactory ProjectionFactory => Service.ProjectionFactory;
        public IXWexBimMeshFactory WexBimMeshFactory => Service.WexBimMeshFactory;
        public IXShapeBinarySerializer ShapeBinarySerializer => Service.ShapeBinarySerializer;
        public IXModelPlacementBuilder ModelPlacementBuilder => Service.ModelPlacementBuilder;

        public double Timeout { get => Service.Timeout; set => Service.Timeout = value; }
        public double Precision => Service.Precision;
        public double PrecisionSquared => Service.PrecisionSquared;
        public double OneMeter => Service.OneMeter;
        public double OneFoot => Service.OneFoot;
        public double OneMillimeter => Service.OneMillimeter;
        public double MinimumGap { get => Service.MinimumGap; set => Service.MinimumGap = value; }
        public double RadianFactor => Service.RadianFactor;
        public double MinAreaM2 => Service.MinAreaM2;
        public IXMeshFactors MeshFactors => Service.MeshFactors;
        public IModel Model => Service.Model;
        public bool UpgradeFaceSets { get => Service.UpgradeFaceSets; set => Service.UpgradeFaceSets = value; }

        public void SetModel(IModel model) => Service.SetModel(model);
        public ISet<IIfcGeometricRepresentationContext> GetTypical3dContexts() => Service.GetTypical3dContexts();
        public IXLocation Create(IIfcObjectPlacement objectPlacement) => Service.Create(objectPlacement);
        public IXLocation CreateMappingTransform(IIfcMappedItem mappedItem) => Service.CreateMappingTransform(mappedItem);

        public void LogError(string format, params object[] args) => Service.LogError(format, args);
        public void LogError(IPersistEntity ifcEntity, string format, params object[] args) => Service.LogError(ifcEntity, format, args);
        public void LogError(IPersistEntity ifcEntity, Exception exception, string format, params object[] args) => Service.LogError(ifcEntity, exception, format, args);
        public void LogError(Exception exception, string format, params object[] args) => Service.LogError(exception, format, args);

        public void LogWarning(string format, params object[] args) => Service.LogWarning(format, args);
        public void LogWarning(IPersistEntity ifcEntity, string format, params object[] args) => Service.LogWarning(ifcEntity, format, args);
        public void LogWarning(IPersistEntity ifcEntity, Exception exception, string format, params object[] args) => Service.LogWarning(ifcEntity, exception, format, args);
        public void LogWarning(Exception exception, string format, params object[] args) => Service.LogWarning(exception, format, args);

        public void LogInformation(string format, params object[] args) => Service.LogInformation(format, args);
        public void LogInformation(IPersistEntity ifcEntity, string format, params object[] args) => Service.LogInformation(ifcEntity, format, args);
        public void LogInformation(IPersistEntity ifcEntity, Exception exception, string format, params object[] args) => Service.LogInformation(ifcEntity, exception, format, args);
        public void LogInformation(Exception exception, string format, params object[] args) => Service.LogInformation(exception, format, args);

        public void LogDebug(string format, params object[] args) => Service.LogDebug(format, args);
        public void LogDebug(IPersistEntity ifcEntity, string format, params object[] args) => Service.LogDebug(ifcEntity, format, args);
        public void LogDebug(IPersistEntity ifcEntity, Exception exception, string format, params object[] args) => Service.LogDebug(ifcEntity, exception, format, args);
        public void LogDebug(Exception exception, string format, params object[] args) => Service.LogDebug(exception, format, args);

        #endregion

        #region IXbimGeometryEngine

        public IXbimGeometryObject Create(IIfcGeometricRepresentationItem ifcRepresentation, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcRepresentation))
            {
                return (IXbimGeometryObject)Build(ifcRepresentation);
            }
        }

        public IXbimGeometryObject Create(IIfcGeometricRepresentationItem ifcRepresentation, IIfcAxis2Placement3D objectLocation, ILogger logger)
        {
            try
            {
                using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcRepresentation))
                {
                    var shape = Build(ifcRepresentation);
                    if (objectLocation != null)
                    {
                        var gf = (GeometryFactory)Service.GeometryFactory;
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
            }
            catch (Exception e)
            {
                (logger ?? _logger).LogError("EE001: Failed to create geometry #{ifcEntityLabel} of type {ifcType}, {error}", ifcRepresentation.EntityLabel, ifcRepresentation.GetType().Name, e.Message);
                return null;
            }
        }


        public XbimShapeGeometry CreateShapeGeometry(IXbimGeometryObject geometryObject, double precision, double deflection,
            double angle, XbimGeometryType storageType, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, geometryObject))
            {
                if (storageType != XbimGeometryType.PolyhedronBinary)
                    throw new XbimGeometryNotSupportedException("Only PolyhedronBinary storage type is supported.");

                var shapeGeom = new XbimShapeGeometry();

                IXShape v6Shape = ExtractV6Shape(geometryObject);
                if (v6Shape == null)
                {
                    _logger.LogWarning("CreateShapeGeometry: unable to extract shape from geometry object.");
                    return shapeGeom;
                }

                var meshFactory = (WexBimMeshFactory)Service.WexBimMeshFactory;
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
        }

        public XbimShapeGeometry CreateShapeGeometry(IXbimGeometryObject geometryObject, double precision, double deflection, double angle, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, geometryObject))
            {
                return CreateShapeGeometry(geometryObject, precision, deflection, angle, XbimGeometryType.PolyhedronBinary, logger);
            }
        }

        public XbimShapeGeometry CreateShapeGeometry(IXbimGeometryObject geometryObject, double precision, double deflection, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, geometryObject))
            {
                return CreateShapeGeometry(geometryObject, precision, deflection, 0.5, XbimGeometryType.PolyhedronBinary, logger);
            }
        }

        /// <summary>
        /// Values for deflection read from config files
        /// </summary>
        public XbimShapeGeometry CreateShapeGeometry(double oneMillimetre, IXbimGeometryObject geometryObject, double precision, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, geometryObject))
            {
                var mf = Service.MeshFactors;
                double deflection = oneMillimetre * mf.LinearDefection / Service.OneMillimeter;
                return CreateShapeGeometry(geometryObject, precision, deflection, mf.AngularDeflection, XbimGeometryType.PolyhedronBinary, logger);
            }
        }

        // --- CreateSolid overloads ---

        public IXbimSolid CreateSolid(IIfcSweptAreaSolid ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcExtrudedAreaSolid ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcRevolvedAreaSolid ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcSweptDiskSolid ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcBoundingBox ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)BuildBoundingBox(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcSurfaceCurveSweptAreaSolid ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcHalfSpaceSolid ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)Service.SolidFactory.Build(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcPolygonalBoundedHalfSpace ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)Service.SolidFactory.Build(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcBoxedHalfSpace ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)Service.SolidFactory.Build(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcCsgPrimitive3D ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)Service.SolidFactory.Build(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcSphere ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)Service.SolidFactory.Build(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcBlock ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)Service.SolidFactory.Build(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcRightCircularCylinder ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)Service.SolidFactory.Build(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcRightCircularCone ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)Service.SolidFactory.Build(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcRectangularPyramid ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return (IXbimSolid)Service.SolidFactory.Build(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcSweptDiskSolidPolygonal ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcRevolvedAreaSolidTapered ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcFixedReferenceSweptAreaSolid ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcAdvancedBrep ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcAdvancedBrepWithVoids ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return BuildAsSolid(ifcSolid);
        }

        public IXbimSolid CreateSolid(IIfcSectionedSpine ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return WrapShapeAsSolid(Service.SolidFactory.Build(ifcSolid));
        }

        public IXbimSolid CreateSolid(IIfcTriangulatedFaceSet shell, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, shell))
                return WrapShapeAsSolid(Service.SolidFactory.Build(shell));
        }

        public IXbimSolid CreateSolid(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSurface))
                return WrapShapeAsSolid(Service.SolidFactory.Build(ifcSurface));
        }

        public IXbimSolid CreateSolid(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSurface))
                return WrapShapeAsSolid(Service.SolidFactory.Build(ifcSurface));
        }

        // --- CreateSolidSet overloads ---

        public IXbimSolidSet CreateSolidSet()
        {
            try
            {
                using (new Tracer(LogHelper.CurrentFunctionName(), _logger))
                    return new XbimSolidSet();
            }
            catch (Exception e)
            {
                _logger.LogError(0, e, "Failed in CreateSolidSet");
                throw new Exception("Engine is not valid", e);
            }
        }

        public IXbimSolidSet CreateSolidSet(IIfcBooleanClippingResult ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return WrapShapeAsSolidSet(Service.BooleanFactory.Build(ifcSolid));
        }

        public IXbimSolidSet CreateSolidSet(IIfcBooleanOperand ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid as IPersistEntity))
            {
                if (ifcSolid is IIfcBooleanResult boolResult)
                    return WrapShapeAsSolidSet(Service.BooleanFactory.Build(boolResult));
                if (ifcSolid is IIfcSolidModel solidModel)
                    return WrapShapeAsSolidSet(Service.SolidFactory.Build(solidModel));
                if (ifcSolid is IIfcHalfSpaceSolid halfSpace)
                    return WrapShapeAsSolidSet(Service.SolidFactory.Build(halfSpace));
                if (ifcSolid is IIfcCsgPrimitive3D csg)
                    return WrapShapeAsSolidSet(Service.SolidFactory.Build(csg));
                throw new XbimGeometryNotSupportedException(
                    $"Unsupported boolean operand type: {ifcSolid.GetType().Name}");
            }
        }

        public IXbimSolidSet CreateSolidSet(IIfcBooleanResult boolOp, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, boolOp))
                return WrapShapeAsSolidSet(Service.BooleanFactory.Build(boolOp));
        }

        public IXbimSolidSet CreateSolidSet(IIfcManifoldSolidBrep ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build(ifcSolid));
        }

        public IXbimSolidSet CreateSolidSet(IIfcFacetedBrep ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build(ifcSolid));
        }

        public IXbimSolidSet CreateSolidSet(IIfcFacetedBrepWithVoids ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build((IIfcSolidModel)ifcSolid));
        }

        public IXbimSolidSet CreateSolidSet(IIfcClosedShell ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build(ifcSolid));
        }

        public IXbimSolidSet CreateSolidSet(IIfcSweptAreaSolid ifcSolid, ILogger logger = null)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build(ifcSolid));
        }

        public IXbimSolidSet CreateSolidSet(IIfcCsgSolid ifcSolid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSolid))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build(ifcSolid));
        }

        public IXbimSolidSet CreateSolidSet(IIfcTriangulatedFaceSet shell, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, shell))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build(shell));
        }

        public IXbimSolidSet CreateSolidSet(IIfcPolygonalFaceSet shell, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, shell))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build(shell));
        }

        public IXbimSolidSet CreateSolidSet(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSurface))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build(ifcSurface));
        }

        public IXbimSolidSet CreateSolidSet(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSurface))
                return WrapShapeAsSolidSet(Service.SolidFactory.Build(ifcSurface));
        }

        // --- CreateFace overloads ---

        public IXbimFace CreateFace(IIfcProfileDef profileDef, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, profileDef))
                return (IXbimFace)Service.ProfileFactory.BuildFace(profileDef);
        }

        public IXbimFace CreateFace(IIfcCompositeCurve cCurve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, cCurve))
                return WrapWireAsFace(Service.WireFactory.Build(cCurve));
        }

        public IXbimFace CreateFace(IIfcPolyline pline, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, pline))
                return WrapWireAsFace(Service.WireFactory.Build(pline));
        }

        public IXbimFace CreateFace(IIfcPolyLoop loop, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, loop))
            {
                var points = new List<IXPoint>();
                foreach (var pt in loop.Polygon)
                {
                    double x = pt.Coordinates[0];
                    double y = pt.Coordinates[1];
                    double z = (int)(long)pt.Dim == 3 ? (double)pt.Coordinates[2] : 0.0;
                    points.Add(new XPoint(x, y, z));
                }
                var wire = Service.WireFactory.BuildWire(points.ToArray());
                return WrapWireAsFace(wire);
            }
        }

        public IXbimFace CreateFace(IIfcSurface surface, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, surface))
            {
                if (surface is IIfcSurfaceOfLinearExtrusion linearExtrusion)
                    return CreateFaceOfLinearExtrusion(linearExtrusion);
                if (surface is IIfcSurfaceOfRevolution)
                    return CreateFaceFromNaturalBounds(surface);

                var built = Service.SurfaceFactory.Build(surface);
                return SurfaceToFace(built, surface.EntityLabel);
            }
        }

        public IXbimFace CreateFace(IIfcPlane plane, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, plane))
                return CreateFace((IIfcSurface)plane, logger);
        }

        public IXbimFace CreateFace(IXbimWire wire, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, wire))
            {
                if (wire is XbimWire wireShape)
                {
                    int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                        Service.ContextHandle, wireShape.Handle, out var faceHandle);
                    if (result != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build face from wire: {XbimGeometryNativeApi.GetLastError()}");
                    return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
                }

                throw new XbimGeometryServiceException("Wire must be a XbimWire from this geometry engine.");
            }
        }

        // --- CreateSurfaceModel overloads ---

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcShellBasedSurfaceModel ifcSurface, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSurface))
                return WrapShapeAsGeometryObjectSet(Service.SolidFactory.Build(ifcSurface));
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcFaceBasedSurfaceModel ifcSurface, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcSurface))
                return WrapShapeAsGeometryObjectSet(Service.SolidFactory.Build(ifcSurface));
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcTriangulatedFaceSet shell, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, shell))
                return WrapShapeAsGeometryObjectSet(Service.SolidFactory.Build(shell));
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcTessellatedFaceSet shell, ILogger logger = null)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, shell))
                return WrapShapeAsGeometryObjectSet(Service.SolidFactory.Build(shell));
        }

        public IXbimGeometryObjectSet CreateSurfaceModel(IIfcPolygonalFaceSet shell, ILogger logger = null)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, shell))
                return WrapShapeAsGeometryObjectSet(Service.SolidFactory.Build(shell));
        }

        // --- Empty set creation ---

        public IXbimGeometryObjectSet CreateGeometryObjectSet()
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger))
                return new XbimGeometryObjectSet();
        }

        // --- Shell creation ---

        public IXbimShell CreateShell(IIfcOpenShell shell, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, shell))
                throw new NotSupportedException("CreateShell from IIfcOpenShell not yet supported.");
        }

        public IXbimShell CreateShell(IIfcConnectedFaceSet shell, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, shell))
                throw new NotSupportedException("CreateShell from IIfcConnectedFaceSet not yet supported.");
        }

        public IXbimShell CreateShell(IIfcSurfaceOfLinearExtrusion linExt, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, linExt))
                throw new NotSupportedException("CreateShell from IIfcSurfaceOfLinearExtrusion not yet supported.");
        }

        // --- Wire creation ---

        public IXbimWire CreateWire(IIfcCurve curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return (IXbimWire)Service.WireFactory.Build(curve);
        }

        public IXbimWire CreateWire(IIfcCompositeCurveSegment compCurveSeg, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, compCurveSeg))
                throw new NotSupportedException("CreateWire from IIfcCompositeCurveSegment not yet supported.");
        }

        // --- Grid ---

        public IXbimSolidSet CreateGrid(IIfcGrid grid, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, grid))
            {
                var curveFactory = (CurveFactory)Service.CurveFactory;

                var uHandles = BuildGridAxisCurves(grid.UAxes, curveFactory, logger ?? _logger);
                var vHandles = BuildGridAxisCurves(grid.VAxes, curveFactory, logger ?? _logger);
                var wHandles = BuildGridAxisCurves(grid.WAxes, curveFactory, logger ?? _logger);

                if (uHandles.Length == 0 && vHandles.Length == 0 && wHandles.Length == 0)
                    return new XbimSolidSet();

                try
                {
                    using var uArray = new NativeHandleArray(uHandles);
                    using var vArray = new NativeHandleArray(vHandles);
                    using var wArray = new NativeHandleArray(wHandles);

                    int result = XbimGeometryNativeApi.xbim_grid_create(
                        Service.ContextHandle,
                        uArray.Ptrs, uArray.Length,
                        vArray.Ptrs, vArray.Length,
                        wArray.Ptrs, wArray.Length,
                        out var shapeHandle);

                    if (result != 0)
                    {
                        (logger ?? _logger).LogWarning("CreateGrid failed: {Error}",
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
        }

        // --- Curve creation ---

        public IXbimCurve CreateCurve(IIfcCurve curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return ((CurveFactory)Service.CurveFactory).Build(curve) as IXbimCurve;
        }

        public IXbimCurve CreateCurve(IIfcPolyline ifcPolyline, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, ifcPolyline))
                return ((CurveFactory)Service.CurveFactory).Build(ifcPolyline) as IXbimCurve;
        }

        public IXbimCurve CreateCurve(IIfcCircle curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return ((CurveFactory)Service.CurveFactory).Build(curve) as IXbimCurve;
        }

        public IXbimCurve CreateCurve(IIfcEllipse curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return ((CurveFactory)Service.CurveFactory).Build(curve) as IXbimCurve;
        }

        public IXbimCurve CreateCurve(IIfcLine curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return ((CurveFactory)Service.CurveFactory).Build(curve) as IXbimCurve;
        }

        public IXbimCurve CreateCurve(IIfcTrimmedCurve curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return ((CurveFactory)Service.CurveFactory).Build(curve) as IXbimCurve;
        }

        public IXbimCurve CreateCurve(IIfcBSplineCurveWithKnots curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return ((CurveFactory)Service.CurveFactory).Build(curve) as IXbimCurve;
        }

        public IXbimCurve CreateCurve(IIfcRationalBSplineCurveWithKnots curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return ((CurveFactory)Service.CurveFactory).Build(curve) as IXbimCurve;
        }

        public IXbimCurve CreateCurve(IIfcOffsetCurve3D curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return ((CurveFactory)Service.CurveFactory).Build(curve) as IXbimCurve;
        }

        public IXbimCurve CreateCurve(IIfcOffsetCurve2D curve, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, curve))
                return ((CurveFactory)Service.CurveFactory).Build(curve) as IXbimCurve;
        }

        // --- Point creation ---

        public IXbimPoint CreatePoint(double x, double y, double z, double tolerance)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger))
                return new XbimPoint(x, y, z, tolerance);
        }

        public IXbimPoint CreatePoint(IIfcCartesianPoint p)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, p))
            {
                double x = p.Coordinates[0];
                double y = p.Coordinates[1];
                double z = p.Coordinates.Count == 3 ? (double)p.Coordinates[2] : 0.0;
                return new XbimPoint(x, y, z, Service.Precision);
            }
        }

        public IXbimPoint CreatePoint(XbimPoint3D p, double tolerance)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, p))
                return new XbimPoint(p.X, p.Y, p.Z, tolerance);
        }

        public IXbimPoint CreatePoint(IIfcPoint pt)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, pt))
            {
                if (pt is IIfcCartesianPoint cp)
                    return CreatePoint(cp);
                if (pt is IIfcPointOnCurve poc)
                    return CreatePoint(poc, _logger);
                if (pt is IIfcPointOnSurface pos)
                    return CreatePoint(pos, _logger);
                throw new XbimGeometryNotSupportedException($"IIfcPoint type {pt.GetType().Name} is not supported.");
            }
        }

        public IXbimPoint CreatePoint(IIfcPointOnCurve p, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, p))
            {
                var factory = (CurveFactory)Service.CurveFactory;
                using var curve = factory.Build3d(p.BasisCurve);
                double param = p.PointParameter;
                if (p.BasisCurve is IIfcConic)
                    param *= Service.RadianFactor;
                var pt = curve.GetPoint(param);
                return new XbimPoint(pt.X, pt.Y, pt.Z, Service.Precision);
            }
        }

        public IXbimPoint CreatePoint(IIfcPointOnSurface pos, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, pos))
                return CreatePoint(pos, _logger);
        }

        // --- Vertex creation ---

        public IXbimVertex CreateVertexPoint(XbimPoint3D point, double precision)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, point))
            {
                int result = XbimGeometryNativeApi.xbim_vertex_build(
                    Service.ContextHandle, point.X, point.Y, point.Z, out var handle);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to create vertex: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimVertex(handle);
            }
        }

        // --- Transforms ---

        public XbimMatrix3D ToMatrix3D(IIfcObjectPlacement objPlacement, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, objPlacement))
            {
                using var loc = (XLocation)Service.Create(objPlacement);
                return new XbimMatrix3D(
                    loc.M11, loc.M12, loc.M13, 0,
                    loc.M21, loc.M22, loc.M23, 0,
                    loc.M31, loc.M32, loc.M33, 0,
                    loc.OffsetX, loc.OffsetY, loc.OffsetZ, 1);
            }
        }

        public IXbimGeometryObject Transformed(IXbimGeometryObject geometry, IIfcCartesianTransformationOperator cartesianTransform)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, geometry))
            {
                var shape = geometry as XbimShape;
                if (shape == null)
                    throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");

                var gf = (GeometryFactory)Service.GeometryFactory;
                var matrix = gf.BuildTransform(cartesianTransform);

                var m = new XbimMatrix3D(
                    matrix.M11 * matrix.ScaleX, matrix.M12 * matrix.ScaleX, matrix.M13 * matrix.ScaleX, 0,
                    matrix.M21 * matrix.ScaleY, matrix.M22 * matrix.ScaleY, matrix.M23 * matrix.ScaleY, 0,
                    matrix.M31 * matrix.ScaleZ, matrix.M32 * matrix.ScaleZ, matrix.M33 * matrix.ScaleZ, 0,
                    matrix.OffsetX, matrix.OffsetY, matrix.OffsetZ, 1);

                return shape.Transform(m);
            }
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcPlacement placement)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, geometryObject))
            {
                var shape = geometryObject as XbimShape
                    ?? throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");
                var gf = (GeometryFactory)Service.GeometryFactory;
                var loc = gf.BuildLocation(placement);
                return MoveShape(shape, (XLocation)loc);
            }
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcAxis2Placement3D placement)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, geometryObject))
            {
                var shape = geometryObject as XbimShape
                    ?? throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");
                var gf = (GeometryFactory)Service.GeometryFactory;
                var loc = gf.BuildLocationFromAxis3D(placement);
                return MoveShape(shape, loc);
            }
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcAxis2Placement2D placement)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, geometryObject))
            {
                var shape = geometryObject as XbimShape
                    ?? throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");
                var gf = (GeometryFactory)Service.GeometryFactory;
                var loc = gf.BuildLocationFromAxis2D(placement);
                return MoveShape(shape, loc);
            }
        }

        public IXbimGeometryObject Moved(IXbimGeometryObject geometryObject, IIfcObjectPlacement objectPlacement, ILogger logger)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), logger ?? _logger, geometryObject))
            {
                if (geometryObject is not XbimShape shape)
                    throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");
                var gf = (GeometryFactory)Service.GeometryFactory;
                var loc = gf.ToLocation(objectPlacement);
                return MoveShape(shape, loc);
            }
        }

        // --- BRep I/O ---

        public IXbimGeometryObject FromBrep(string brepStr)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger))
                return (IXbimGeometryObject)Services.ShapeBinarySerializer.FromBrep(brepStr);
        }

        public string ToBrep(IXbimGeometryObject geometryObject)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, geometryObject))
            {
                var shape = geometryObject as XbimShape
                    ?? throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");
                return shape.BrepString();
            }
        }

        /// <summary>
        /// Write the <see cref="IXbimGeometryObject"/> Brep to a file
        /// </summary>
        public void WriteBrep(string filename, IXbimGeometryObject geomObj)
        {
            var shape = geomObj as XbimShape
                ?? throw new XbimGeometryServiceException("Geometry must originate from this geometry engine.");
            shape.WriteBrep(filename);
        }

        /// <summary>
        /// Read a <see cref="IXbimGeometryObject"/> Brep from a file
        /// </summary>
        public IXbimGeometryObject ReadBrep(string filename)
        {
            string brepStr = File.ReadAllText(filename);
            return FromBrep(brepStr);
        }

        // --- Triangulation / Mesh ---

        /// <summary>
        /// Writes a triangulation to the provided TextWriter
        /// </summary>
        public void WriteTriangulation(TextWriter tw, IXbimGeometryObject shape, double tolerance, double deflection)
        {
            WriteTriangulation(tw, shape, tolerance, deflection: deflection, angle: 0.5);
        }

        /// <summary>
        /// Writes a triangulation to the provided TextWriter
        /// </summary>
        public void WriteTriangulation(TextWriter tw, IXbimGeometryObject shape, double tolerance, double deflection, double angle)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, shape))
                throw new NotSupportedException("Text-based triangulation is not supported. Use binary format.");
        }

        /// <summary>
        /// Writes a triangulation to the provided BinaryWriter
        /// </summary>
        public void WriteTriangulation(BinaryWriter bw, IXbimGeometryObject shape, double tolerance, double deflection, double angle)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, shape))
            {
                IXShape v6Shape = ExtractV6Shape(shape);
                if (v6Shape == null)
                {
                    _logger.LogWarning("WriteTriangulation: unable to extract shape from geometry object.");
                    return;
                }

                byte[] meshData = Service.WexBimMeshFactory.CreateWexBimMesh(v6Shape, tolerance, deflection, angle, 1.0, out _);
                if (meshData != null && meshData.Length > 0)
                    bw.Write(meshData);
            }
        }

        /// <summary>
        /// Writes a triangulation to the provided BinaryWriter
        /// </summary>
        public void WriteTriangulation(BinaryWriter bw, IXbimGeometryObject shape, double tolerance, double deflection)
        {
            WriteTriangulation(bw, shape, tolerance, deflection: deflection, angle: 0.5);
        }

        /// <summary>
        /// Creates a mesh for the given <see cref="IXbimGeometryObject"/>
        /// </summary>
        public void Mesh(IXbimMeshReceiver receiver, IXbimGeometryObject geometryObject, double precision, double deflection,
            double angle = 0.5)
        {
            using (new Tracer(LogHelper.CurrentFunctionName(), _logger, geometryObject))
            {
                IXShape v6Shape = ExtractV6Shape(geometryObject);
                if (v6Shape == null)
                {
                    _logger.LogWarning("Mesh: unable to extract shape from geometry object.");
                    return;
                }

                byte[] meshData = Service.WexBimMeshFactory.CreateWexBimMesh(v6Shape, precision, deflection, angle, 1.0, out _);
                if (meshData == null || meshData.Length == 0)
                    return;

                var mesh = new WexBimMesh(meshData);

                receiver.BeginUpdate();
                try
                {
                    foreach (var face in mesh.Faces)
                    {
                        int faceId = receiver.AddFace();
                        var allIndices = face.Indices.ToArray();
                        int triCount = face.TriangleCount;

                        if (face.IsPlanar)
                        {
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
        }

        #endregion

        #region Private Helpers

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
                Service.ContextHandle,
                ox, oy, oz,
                0, 0, 1,
                1, 0, 0,
                xLen, yLen, zLen,
                out var NativeShapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build BoundingBox solid: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapSolid(NativeShapeHandle);
        }

        private IXbimSolid BuildAsSolid(IIfcSolidModel solidModel)
        {
            var shape = Service.SolidFactory.Build(solidModel);
            return WrapShapeAsSolid(shape);
        }

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

        private static IXbimSolidSet WrapShapeAsSolidSet(IXShape shape)
        {
            if (shape is XbimSolidSet solidSet)
                return solidSet;
            if (shape is XbimSolid solid)
                return new XbimSolidSet(new IXbimSolid[] { solid });

            var s = (XbimShape)shape;
            return new XbimSolidSet(s);
        }

        private static IXbimGeometryObjectSet WrapShapeAsGeometryObjectSet(IXShape shape)
        {
            return new XbimGeometryObjectSet(new IXbimGeometryObject[] { (IXbimGeometryObject)shape });
        }

        private IXbimFace CreateFaceFromNaturalBounds(IIfcSurface ifcSurface)
        {
            var built = Service.SurfaceFactory.Build(ifcSurface);
            if (built is not Surface surf)
                throw new XbimGeometryServiceException(
                    $"Failed to build surface #{ifcSurface.EntityLabel}: unexpected type.");

            int result = XbimGeometryNativeApi.xbim_face_build_surface_natural_bounds(
                Service.ContextHandle, surf.Handle, Service.Precision, out var faceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build face from surface #{ifcSurface.EntityLabel}: " +
                    $"{XbimGeometryNativeApi.GetLastError()}");

            return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
        }

        private IXbimFace CreateFaceOfLinearExtrusion(IIfcSurfaceOfLinearExtrusion ifcExtrusion)
        {
            var built = Service.SurfaceFactory.Build(ifcExtrusion);
            if (built is not Surface surf)
                throw new XbimGeometryServiceException(
                    $"SurfaceOfLinearExtrusion #{ifcExtrusion.EntityLabel}: unexpected surface type.");

            double depth = ifcExtrusion.Depth;

            int result = XbimGeometryNativeApi.xbim_face_build_surface_with_depth(
                Service.ContextHandle, surf.Handle, depth, Service.Precision, out var faceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build face from SurfaceOfLinearExtrusion #{ifcExtrusion.EntityLabel}: " +
                    $"{XbimGeometryNativeApi.GetLastError()}");

            return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
        }

        private IXbimFace SurfaceToFace(IXSurface built, int entityLabel)
        {
            if (built is FaceSurface faceSurface)
                return (IXbimFace)NativeShapeWrapper.WrapFace(faceSurface.Handle);

            if (built is Surface surf)
            {
                int result = XbimGeometryNativeApi.xbim_face_build_unbounded_from_surface(
                    Service.ContextHandle, surf.Handle, Service.Precision, out var faceHandle);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build face from surface #{entityLabel}: {XbimGeometryNativeApi.GetLastError()}");
                return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
            }

            throw new NotSupportedException(
                $"Cannot create face from surface type {built.GetType().Name} #{entityLabel}.");
        }

        private IXbimFace WrapWireAsFace(IXWire wire)
        {
            var wireShape = wire as XbimWire
                ?? throw new XbimGeometryServiceException("Wire must be a XbimWire instance.");

            int result = XbimGeometryNativeApi.xbim_face_build_from_wire(
                Service.ContextHandle, wireShape.Handle, out var faceHandle);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build face from wire: {XbimGeometryNativeApi.GetLastError()}");

            return (IXbimFace)NativeShapeWrapper.WrapFace(faceHandle);
        }

        private static IXbimGeometryObject MoveShape(XbimShape shape, XLocation location)
        {
            int moveResult = XbimGeometryNativeApi.xbim_shape_moved(
                shape.Handle, location.Handle, out var movedHandle);
            if (moveResult != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to move shape: {XbimGeometryNativeApi.GetLastError()}");
            return (IXbimGeometryObject)NativeShapeWrapper.WrapShape(movedHandle);
        }

        private IXShape ExtractV6Shape(IXbimGeometryObject geometryObject)
        {
            if (geometryObject is XbimShape shape)
                return shape;

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
                    Service.ContextHandle,
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

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _service?.Dispose();
        }

        #endregion

#pragma warning restore CS1591
    }


    internal static class LogHelper
    {
        public static string CurrentFunctionName([CallerMemberName] string caller = "")
        {
            return caller;
        }
    }

    /// <summary>
    /// Traces method calls
    /// </summary>
    internal class Tracer : IDisposable
    {
        private readonly string methodName;
        private readonly ILogger logger;

        public Tracer(string methodName, ILogger logger)
        {
            this.methodName = methodName;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            logger.LogTrace("Entering GeometryEngine {function}", methodName);
        }

        public Tracer(string methodName, ILogger logger, IPersistEntity entity)
        {
            this.methodName = methodName;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Entering GeometryEngine {function} with #{entity} [{type}]",
                methodName, entity.EntityLabel, entity.GetType().Name);
            }
        }

        public Tracer(string methodName, ILogger logger, IXbimGeometryObject geometryObject)
        {
            this.methodName = methodName;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Entering GeometryEngine {function} with {tag} [{type}]",
                    methodName, geometryObject.Tag, geometryObject.GetType().Name);
            }
        }

        public Tracer(string methodName, ILogger logger, XbimPoint3D point)
        {
            this.methodName = methodName;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            logger.LogTrace("Entering GeometryEngine {function} with point {x},{y},{z}", methodName, point.X, point.Y, point.Z);
        }

        #region IDisposable Support
        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    logger.LogTrace("Exiting GeometryEngine {function}", methodName);
                }

                disposedValue = true;
            }
        }

        ~Tracer()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
