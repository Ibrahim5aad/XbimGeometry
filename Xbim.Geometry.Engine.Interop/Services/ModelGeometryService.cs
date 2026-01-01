using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Abstractions.Extensions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Services
{
    /// <summary>
    /// Manages geometry service lifecycle for an IFC model, providing access to
    /// factory instances and model-level parameters (precision, unit factors, tolerances).
    /// </summary>
    internal class ModelGeometryService : IXModelGeometryService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly LoggingService _loggingService;
        private NativeContextHandle? _contextHandle;
        private IModel? _model;

        private double _precisionSquared;
        private double _minAreaM2;
        private double _minimumGap;
        private double _timeout = 60;
        private bool _upgradeFaceSets = true;

        private SolidFactory? _solidFactory;
        private ProfileFactory? _profileFactory;
        private GeometryFactory? _geometryFactory;
        private BooleanFactory? _booleanFactory;
        private VertexFactory? _vertexFactory;
        private CurveFactory? _curveFactory;
        private SurfaceFactory? _surfaceFactory;
        private EdgeFactory? _edgeFactory;
        private WireFactory? _wireFactory;
        private FaceFactory? _faceFactory;
        private ShellFactory? _shellFactory;
        private CompoundFactory? _compoundFactory;
        private ShapeFactory? _shapeFactory;
        private MaterialFactory? _materialFactory;
        private ProjectionFactory? _projectionFactory;
        private WexBimMeshFactory? _wexBimMeshFactory;
        private ShapeBinarySerializer? _shapeBinarySerializer;
        private ModelPlacementBuilder? _modelPlacementBuilder;

        public ModelGeometryService(IModel model, ILoggerFactory loggerFactory)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));

            _logger = loggerFactory.CreateLogger<ModelGeometryService>();

            var scope = new Dictionary<string, object?>
            {
                ["OriginatingSystem"] = model.Header?.FileName?.OriginatingSystem,
                ["CreatedBy"] = model.Header?.CreatingApplication,
                ["IfcVersion"] = model.Header?.SchemaVersion
            };
            _logger.BeginScope(scope);

            _loggingService = new LoggingService(_logger);

            SetModel(model);
        }

        #region Model Properties

        public IModel Model => _model!;

        public double Precision => _model!.ModelFactors.Precision;

        public double PrecisionSquared => _precisionSquared;

        public double OneMeter => _model!.ModelFactors.OneMeter;

        public double OneFoot => _model!.ModelFactors.OneFoot;

        public double OneMillimeter => _model!.ModelFactors.OneMilliMeter;

        public double RadianFactor => _model!.ModelFactors.AngleToRadiansConversionFactor;

        public double MinAreaM2 => _minAreaM2;

        public double MinimumGap
        {
            get => _minimumGap;
            set => _minimumGap = value;
        }

        public double Timeout
        {
            get => _timeout;
            set => _timeout = value;
        }

        public bool UpgradeFaceSets
        {
            get => _upgradeFaceSets;
            set => _upgradeFaceSets = value;
        }

        public IXMeshFactors MeshFactors =>
            new MeshFactors(_model!.ModelFactors.OneMeter, _model!.ModelFactors.Precision);

        #endregion

        #region Service / Factory Properties

        public IXLoggingService LoggingService => _loggingService;

        public IXVertexFactory VertexFactory => _vertexFactory ??= new VertexFactory(this, _logger);
        public IXGeometryFactory GeometryFactory => _geometryFactory ??= new GeometryFactory(this, _logger);
        public IXCurveFactory CurveFactory => _curveFactory ??= new CurveFactory(this, _logger);
        public IXSurfaceFactory SurfaceFactory => _surfaceFactory ??= new SurfaceFactory(this, _logger);
        public IXEdgeFactory EdgeFactory => _edgeFactory ??= new EdgeFactory(this, _logger);
        public IXWireFactory WireFactory => _wireFactory ??= new WireFactory(this, _logger);
        public IXFaceFactory FaceFactory => _faceFactory ??= new FaceFactory(this, _logger);
        public IXShellFactory ShellFactory => _shellFactory ??= new ShellFactory(this, _logger);
        public IXSolidFactory SolidFactory => _solidFactory ??= new SolidFactory(this, _logger);
        public IXCompoundFactory CompoundFactory => _compoundFactory ??= new CompoundFactory(this, _logger);
        public IXBooleanFactory BooleanFactory => _booleanFactory ??= new BooleanFactory(this, _logger);
        public IXShapeFactory ShapeFactory => _shapeFactory ??= new ShapeFactory(this, _logger);
        public IXProfileFactory ProfileFactory => _profileFactory ??= new ProfileFactory(this, _logger);
        public IXMaterialFactory MaterialFactory => _materialFactory ??= new MaterialFactory();
        public IXProjectionFactory ProjectionFactory => _projectionFactory ??= new ProjectionFactory(this, _logger);
        public IXWexBimMeshFactory WexBimMeshFactory => _wexBimMeshFactory ??= new WexBimMeshFactory(this, _logger);
        public IXShapeBinarySerializer ShapeBinarySerializer => _shapeBinarySerializer ??= new ShapeBinarySerializer(_logger);
        public IXModelPlacementBuilder ModelPlacementBuilder => _modelPlacementBuilder ??= new ModelPlacementBuilder(this, _logger);

        #endregion

        #region Initialization

        public void SetModel(IModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));

            double precision = model.ModelFactors.Precision;
            double oneMeter = model.ModelFactors.OneMeter;
            double oneFoot = model.ModelFactors.OneFoot;
            double oneMillimeter = model.ModelFactors.OneMilliMeter;
            double radianFactor = model.ModelFactors.AngleToRadiansConversionFactor;

            _precisionSquared = precision * precision;
            _minAreaM2 = Math.Pow(0.002, 2) * Math.Pow(oneMeter, 2); // 2mm x 2mm

            var app = model.Instances.OfType<IIfcApplication>().FirstOrDefault();
            if (app != null && app.ApplicationIdentifier.ToString() == "Revit")
                _minimumGap = (oneMillimeter * 25.4) / 32; // 1/32nd inch — Revit's min line length
            else
                _minimumGap = oneMillimeter / 5; // 0.2mm for metric models

            // Create or replace the native context
            _contextHandle?.Dispose();

            int result = XbimGeometryNativeApi.xbim_context_create(
                precision,
                oneMeter,
                oneFoot,
                oneMillimeter,
                radianFactor,
                _timeout,
                _minimumGap,
                _loggingService.Callback,
                out var newHandle);

            if (result != 0)
            {
                string error = XbimGeometryNativeApi.GetLastError();
                throw new InvalidOperationException(
                    $"Failed to create native geometry context: {error}");
            }
            _contextHandle = newHandle;

            // Tag the model so other services can find us
            model.AddTagValue("ModelGeometryService", this);
        }

        #endregion

        #region Context Access

        /// <summary>
        /// The native context handle for use by factory classes.
        /// </summary>
        internal NativeContextHandle ContextHandle =>
            _contextHandle ?? throw new ObjectDisposedException(nameof(ModelGeometryService));

        #endregion

        #region Geometry Context Methods

        public ISet<IIfcGeometricRepresentationContext> GetTypical3dContexts()
        {
            var results = new HashSet<IIfcGeometricRepresentationContext>();

            // First pass: look at sub-contexts for "model" or "body" identifiers
            foreach (var c in _model!.Instances.OfType<IIfcGeometricRepresentationSubContext>())
            {
                string str = (c.ContextIdentifier?.ToString()?.ToLower() ?? "") + ":" +
                             (c.ContextType?.ToString()?.ToLower() ?? "");

                if (str.Contains("model") || str.Contains("body"))
                {
                    results.Add(c);
                    results.Add(c.ParentContext);
                }
            }

            // Fallback: check parent contexts directly
            if (results.Count == 0)
            {
                foreach (var c in _model.Instances.OfType<IIfcGeometricRepresentationContext>())
                {
                    string str = (c.ContextIdentifier?.ToString()?.ToLower() ?? "") + ":" +
                                 (c.ContextType?.ToString()?.ToLower() ?? "");
                    if (str.Contains("model") || str.Contains("body"))
                        results.Add(c);
                }
            }

            return results;
        }

        public IXLocation Create(IIfcObjectPlacement objectPlacement)
        {
            var geometryFactory = (GeometryFactory)GeometryFactory;
            return geometryFactory.ToLocation(objectPlacement);
        }

        public IXLocation CreateMappingTransform(IIfcMappedItem mappedItem)
        {
            var geometryFactory = (GeometryFactory)GeometryFactory;

            // Build source transform from the mapping origin
            var sourceLocation = geometryFactory.BuildLocation(mappedItem.MappingSource.MappingOrigin);

            // Build target transform from the mapping target
            var targetMatrix = geometryFactory.BuildTransform(mappedItem.MappingTarget);

            // Compose: source * target
            if (sourceLocation is XLocation srcLoc && targetMatrix.IsIdentity)
                return srcLoc;

            if (sourceLocation.IsIdentity && targetMatrix is XLocation tgtLoc)
                return tgtLoc;

            // For non-identity cases, multiply the matrices
            var composed = sourceLocation.Multiply(targetMatrix);

            // Create a location from the composed matrix
            int result = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                composed.OffsetX, composed.OffsetY, composed.OffsetZ,
                composed.M31, composed.M32, composed.M33, // Z direction
                composed.M11, composed.M12, composed.M13, // X direction
                out var handle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create mapping transform: {XbimGeometryNativeApi.GetLastError()}");

            return new XLocation(handle,
                composed.M11, composed.M12, composed.M13,
                composed.M21, composed.M22, composed.M23,
                composed.M31, composed.M32, composed.M33,
                composed.OffsetX, composed.OffsetY, composed.OffsetZ,
                composed.ScaleX);
        }

        #endregion

        #region Logging

        public void LogError(string format, params object[] args)
            => Log(LogLevel.Error, null, null, format, args);

        public void LogError(IPersistEntity ifcEntity, string format, params object[] args)
            => Log(LogLevel.Error, null, ifcEntity, format, args);

        public void LogError(IPersistEntity ifcEntity, Exception exception, string format, params object[] args)
            => Log(LogLevel.Error, exception, ifcEntity, format, args);

        public void LogError(Exception exception, string format, params object[] args)
            => Log(LogLevel.Error, exception, null, format, args);

        public void LogWarning(string format, params object[] args)
            => Log(LogLevel.Warning, null, null, format, args);

        public void LogWarning(IPersistEntity ifcEntity, string format, params object[] args)
            => Log(LogLevel.Warning, null, ifcEntity, format, args);

        public void LogWarning(IPersistEntity ifcEntity, Exception exception, string format, params object[] args)
            => Log(LogLevel.Warning, exception, ifcEntity, format, args);

        public void LogWarning(Exception exception, string format, params object[] args)
            => Log(LogLevel.Warning, exception, null, format, args);

        public void LogInformation(string format, params object[] args)
            => Log(LogLevel.Information, null, null, format, args);

        public void LogInformation(IPersistEntity ifcEntity, string format, params object[] args)
            => Log(LogLevel.Information, null, ifcEntity, format, args);

        public void LogInformation(IPersistEntity ifcEntity, Exception exception, string format, params object[] args)
            => Log(LogLevel.Information, exception, ifcEntity, format, args);

        public void LogInformation(Exception exception, string format, params object[] args)
            => Log(LogLevel.Information, exception, null, format, args);

        public void LogDebug(string format, params object[] args)
            => Log(LogLevel.Debug, null, null, format, args);

        public void LogDebug(IPersistEntity ifcEntity, string format, params object[] args)
            => Log(LogLevel.Debug, null, ifcEntity, format, args);

        public void LogDebug(IPersistEntity ifcEntity, Exception exception, string format, params object[] args)
            => Log(LogLevel.Debug, exception, ifcEntity, format, args);

        public void LogDebug(Exception exception, string format, params object[] args)
            => Log(LogLevel.Debug, exception, null, format, args);

        private void Log(LogLevel logLevel, Exception? exception, IPersistEntity? ifcEntity, string format, object[] args)
        {
            if (!_logger.IsEnabled(logLevel))
                return;

            if (ifcEntity != null)
            {
                string amendedFormat = "#{EntityId}={EntityType}: " + format;
                var amendedArgs = new object[args.Length + 2];
                amendedArgs[0] = ifcEntity.EntityLabel;
                amendedArgs[1] = ifcEntity.GetType().Name;
                Array.Copy(args, 0, amendedArgs, 2, args.Length);
                _logger.Log(logLevel, 0, exception, amendedFormat, amendedArgs);
            }
            else
            {
                _logger.Log(logLevel, 0, exception, format, args);
            }
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _curveFactory?.Dispose();
            _curveFactory = null;

            _contextHandle?.Dispose();
            _contextHandle = null;

            _loggingService.Dispose();
        }

        #endregion
    }
}
