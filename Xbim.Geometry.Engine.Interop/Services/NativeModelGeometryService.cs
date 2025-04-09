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
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Services
{
    /// <summary>
    /// Manages geometry service lifecycle for an IFC model, providing access to
    /// factory instances and model-level parameters (precision, unit factors, tolerances).
    /// </summary>
    internal class NativeModelGeometryService : IXModelGeometryService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly NativeLoggingService _loggingService;
        private NativeContextHandle? _contextHandle;
        private IModel? _model;

        private double _precisionSquared;
        private double _minAreaM2;
        private double _minimumGap;
        private double _timeout = 60;
        private bool _upgradeFaceSets = true;

        private NativeSolidFactory? _solidFactory;
        private NativeProfileFactory? _profileFactory;
        private NativeGeometryFactory? _geometryFactory;
        private NativeBooleanFactory? _booleanFactory;

        public NativeModelGeometryService(IModel model, ILoggerFactory loggerFactory)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));

            _logger = loggerFactory.CreateLogger<NativeModelGeometryService>();

            // Begin a logging scope with model metadata (matching C++/CLI pattern)
            var scope = new Dictionary<string, object?>
            {
                ["OriginatingSystem"] = model.Header?.FileName?.OriginatingSystem,
                ["CreatedBy"] = model.Header?.CreatingApplication,
                ["IfcVersion"] = model.Header?.SchemaVersion
            };
            _logger.BeginScope(scope);

            _loggingService = new NativeLoggingService(_logger);

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

        // Factory stubs — will be wired up in INTEG-004
        public IXVertexFactory VertexFactory => throw new NotImplementedException("VertexFactory not yet implemented in P/Invoke layer.");
        public IXGeometryFactory GeometryFactory => _geometryFactory ??= new NativeGeometryFactory(this, _logger);
        public IXCurveFactory CurveFactory => throw new NotImplementedException("CurveFactory not yet implemented in P/Invoke layer.");
        public IXSurfaceFactory SurfaceFactory => throw new NotImplementedException("SurfaceFactory not yet implemented in P/Invoke layer.");
        public IXEdgeFactory EdgeFactory => throw new NotImplementedException("EdgeFactory not yet implemented in P/Invoke layer.");
        public IXWireFactory WireFactory => throw new NotImplementedException("WireFactory not yet implemented in P/Invoke layer.");
        public IXFaceFactory FaceFactory => throw new NotImplementedException("FaceFactory not yet implemented in P/Invoke layer.");
        public IXShellFactory ShellFactory => throw new NotImplementedException("ShellFactory not yet implemented in P/Invoke layer.");
        public IXSolidFactory SolidFactory => _solidFactory ??= new NativeSolidFactory(this, _logger);
        public IXCompoundFactory CompoundFactory => throw new NotImplementedException("CompoundFactory not yet implemented in P/Invoke layer.");
        public IXBooleanFactory BooleanFactory => _booleanFactory ??= new NativeBooleanFactory(this, _logger);
        public IXShapeFactory ShapeFactory => throw new NotImplementedException("ShapeFactory not yet implemented in P/Invoke layer.");
        public IXProfileFactory ProfileFactory => _profileFactory ??= new NativeProfileFactory(this, _logger);
        public IXMaterialFactory MaterialFactory => throw new NotImplementedException("MaterialFactory not yet implemented in P/Invoke layer.");
        public IXProjectionFactory ProjectionFactory => throw new NotImplementedException("ProjectionFactory not yet implemented in P/Invoke layer.");
        public IXWexBimMeshFactory WexBimMeshFactory => throw new NotImplementedException("WexBimMeshFactory not yet implemented in P/Invoke layer.");
        public IXShapeBinarySerializer ShapeBinarySerializer => throw new NotImplementedException("ShapeBinarySerializer not yet implemented in P/Invoke layer.");
        public IXModelPlacementBuilder ModelPlacementBuilder => throw new NotImplementedException("ModelPlacementBuilder not yet implemented in P/Invoke layer.");

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

            // Determine minimum gap based on authoring tool (matching C++/CLI logic)
            var app = model.Instances.OfType<IIfcApplication>().FirstOrDefault();
            if (app != null && app.ApplicationIdentifier.ToString() == "Revit")
                _minimumGap = (oneMillimeter * 25.4) / 32; // 1/32nd inch — Revit's min line length
            else
                _minimumGap = oneMillimeter / 5; // 0.2mm for metric models

            // Create or replace the native context
            _contextHandle?.Dispose();

            int result = NativeMethods.xbim_context_create(
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
                string error = NativeMethods.GetLastError();
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
            _contextHandle ?? throw new ObjectDisposedException(nameof(NativeModelGeometryService));

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
            throw new NotImplementedException("Create(IIfcObjectPlacement) not yet implemented in P/Invoke layer.");
        }

        public IXLocation CreateMappingTransform(IIfcMappedItem mappedItem)
        {
            throw new NotImplementedException("CreateMappingTransform not yet implemented in P/Invoke layer.");
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

            _contextHandle?.Dispose();
            _contextHandle = null;

            _loggingService.Dispose();
        }

        #endregion
    }
}
