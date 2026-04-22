#region Directives

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xbim.Common;
using Xbim.Common.Configuration;
using Xbim.Common.Exceptions;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.Interfaces;
using Xbim.Geometry.Scene.Clustering;
using Xbim.Geometry.Scene.Extensions;
using Xbim.Tessellator;

#endregion

namespace Xbim.Geometry.Scene
{
    /// <summary>
    /// A memory-efficient geometry context that processes products in a streaming fashion.
    /// Shapes are built, meshed, and disposed immediately rather than cached for the entire run.
    /// Featured products (those with openings/projections) cache their body BRep during shape
    /// creation and reuse it for boolean operations, avoiding redundant rebuilds.
    /// </summary>
    public class Xbim3DModelContext : ICanLog
    {
        #region Inner types

        private struct GeometryReference
        {
            public XbimRect3D BoundingBox;
            public int GeometryId;
            public int StyleLabel;
            public XbimVector3D? LocalShapeDisplacement;
        }

        private class IfcRepresentationContextCollection : KeyedCollection<int, IIfcRepresentationContext>
        {
            protected override int GetKeyForItem(IIfcRepresentationContext item) => item.EntityLabel;
        }

        /// <summary>
        /// Lightweight state bag for the streaming pipeline. Replaces the old XbimCreateContextHelper.
        /// Caches feature element shapes and voided product body BRep (to avoid double-building).
        /// </summary>
        private sealed class StreamingState : IDisposable
        {
            internal XbimPlacementTree PlacementTree;
            internal readonly ConcurrentDictionary<int, GeometryReference> ShapeLookup = new();
            internal readonly ConcurrentDictionary<int, List<GeometryReference>> MapGeometryReferences = new();
            internal readonly ConcurrentDictionary<int, XbimMatrix3D> MapTransforms = new();
            internal HashSet<int> MappedShapeIds;
            internal HashSet<int> FeatureElementShapeIds;
            internal HashSet<int> ProductShapeIds;
            internal HashSet<int> VoidedProductIds;
            internal HashSet<int> VoidedShapeIds;
            internal List<IGrouping<IIfcElement, IIfcFeatureElement>> OpeningsAndProjections;
            internal Dictionary<int, int> SurfaceStyles;
            internal Dictionary<IIfcRepresentationContext, ConcurrentQueue<XbimBBoxClusterElement>> Clusters;
            internal ParallelOptions ParallelOptions;

            /// <summary>
            /// When set, geometry and instance events are pushed to this channel writer
            /// for live streaming during CreateContext. Null for the non-streaming path.
            /// </summary>
            internal ChannelWriter<SinkEvent> SinkChannel;

            /// <summary>
            /// Cache for feature element shapes (openings/projections). These are shared across
            /// multiple products and are much fewer than the voided product shapes that the old
            /// class cached. Keyed by feature product entity label.
            /// </summary>
            internal readonly ConcurrentDictionary<int, IXShape> FeaturesCache = new();

            /// <summary>
            /// Cache for featured product body BRep shapes (elements with openings or projections).
            /// Populated during WriteShapeGeometries to avoid rebuilding from IFC in ProcessFeaturedProducts.
            /// Keyed by representation item entity label.
            /// </summary>
            internal readonly ConcurrentDictionary<int, IXShape> FeaturedBodiesCache = new();

            /// <summary>
            /// Cache for directly-tessellated voided product body meshes.
            /// Used by the Manifold mesh boolean fast path. Keyed by shape entity label.
            /// </summary>
            internal readonly ConcurrentDictionary<int, (float[] Positions, uint[] Indices, XbimRect3D Bounds)>
                MeshBodiesCache = new();

            /// <summary>
            /// Cache for directly-tessellated feature element meshes (openings/projections).
            /// Used by the Manifold mesh boolean fast path. Keyed by shape entity label.
            /// </summary>
            internal readonly ConcurrentDictionary<int, (float[] Positions, uint[] Indices, XbimRect3D Bounds)>
                MeshFeaturesCache = new();

            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var kvp in FeaturesCache)
                    (kvp.Value as IDisposable)?.Dispose();
                FeaturesCache.Clear();
                foreach (var kvp in FeaturedBodiesCache)
                    (kvp.Value as IDisposable)?.Dispose();
                FeaturedBodiesCache.Clear();
                MeshBodiesCache.Clear();
                MeshFeaturesCache.Clear();
                GC.SuppressFinalize(this);
            }
        }

        #endregion

        #region Fields

        private readonly IfcRepresentationContextCollection _contexts;
        private readonly IXbimGeometryEngine _engine;
        private readonly IModel _model;
        private readonly DynamicDeflection _dynamicDeflection;
        private readonly ContextDiagnostics _diag = new();
        private readonly IXModelGeometryService _modelServices;

        #endregion

        #region Configuration

        /// <summary>
        /// When true, collects per-operation timing data during CreateContext().
        /// Call <see cref="PrintDiagnostics"/> after context creation to see results.
        /// </summary>
        public bool EnableDiagnostics { get => _diag.Enabled; set => _diag.Enabled = value; }

        /// <summary>
        /// Prints accumulated diagnostic timing data to the console.
        /// </summary>
        public void PrintDiagnostics() => _diag.PrintSummary();

        /// <summary>
        /// Maximum duration in milliseconds for boolean operations before they are abandoned.
        /// </summary>
        public static int BooleanTimeOutMilliSeconds;

        /// <summary>
        /// The set of representation identifiers treated as 3D body geometry.
        /// </summary>
        public HashSet<string> BodyRepresentations { get; } = new HashSet<string> { "body", "facetation", "reference" };

        /// <summary>
        /// Controls the behaviour and deflection associated with individual elements during meshing.
        /// </summary>
        [Flags]
        public enum MeshingBehaviourResult
        {
            /// <summary>Perform additions</summary>
            PerformAdditions = 1,
            /// <summary>Perform subtractions</summary>
            PerformSubtractions = 2,
            /// <summary>Replace bounding boxes</summary>
            ReplaceBoundingBox = 4,
            /// <summary>Skip meshing</summary>
            Skip = 8,
            /// <summary>Default meshing</summary>
            Default = PerformAdditions | PerformSubtractions
        }

        /// <summary>
        /// Delegate enabling custom meshing behaviour per element.
        /// </summary>
        public delegate MeshingBehaviourResult MeshingBehaviourSetter(int elementId, int typeId,
            ref double linearDeflection, ref double angularDeflection);

        /// <summary>
        /// Delegate for per-element control of meshing behaviour and deflection.
        /// </summary>
        public MeshingBehaviourSetter CustomMeshingBehaviour;

        /// <summary>
        /// When true (default), uses Manifold mesh booleans for directly-tessellated
        /// shapes before falling back to OCCT BRep booleans.
        /// Set to false to force all boolean operations through the OCCT BRep path.
        /// </summary>
        public bool EnableManifoldBooleans { get; set; } = true;

        /// <summary>
        /// Maximum number of threads for parallel processing. Values &lt;= 0 use the default.
        /// </summary>
        public int MaxThreads { get; set; }

        #endregion

        #region Properties

        /// <summary>
        /// The model associated with this context.
        /// </summary>
        public IModel Model => _model;

        /// <summary>
        /// The geometric representation contexts discovered during initialisation.
        /// </summary>
        public IEnumerable<IIfcRepresentationContext> Contexts
        {
            get
            {
                foreach (var context in _contexts)
                    yield return context;
            }
        }

        #endregion

        #region Static constructor

        static Xbim3DModelContext()
        {
            var timeOut = System.Configuration.ConfigurationManager.AppSettings["BooleanTimeOut"];
            if (!int.TryParse(timeOut, out int seconds))
                seconds = 60;
            BooleanTimeOutMilliSeconds = seconds * 1000;
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a streaming geometry context for the given model.
        /// </summary>
        public Xbim3DModelContext(IModel model, ILoggerFactory loggerFactory,
            string contextType = "model", string requiredContextIdentifier = null)
            : this(model, contextType, requiredContextIdentifier,
                   loggerFactory?.CreateLogger<Xbim3DModelContext>(), loggerFactory)
        {
        }

        /// <summary>
        /// Creates a streaming geometry context for the given model.
        /// </summary>
        public Xbim3DModelContext(IModel model, string contextType = "model",
            string requiredContextIdentifier = null,
            ILogger logger = null, ILoggerFactory loggerFactory = null) : base(logger)
        {
            var factory = InternalServiceProvider.GetService<IXGeometryConverterFactory>();
            if (factory == null)
                throw new InvalidOperationException(
                    "An implementation of IXGeometryConverterFactory could not be found.\n\n" +
                    "To fix this add the following before calling any xbim functionality:\n\n" +
                    " XbimServices.Current.ConfigureServices(opt => opt.AddXbimToolkit(conf => conf.AddGeometryServices()));");

            _model = model;
            if (loggerFactory == null) loggerFactory = InternalServiceProvider.GetLoggerFactory();
            _logger = logger ?? loggerFactory.CreateLogger<Xbim3DModelContext>();
            _engine = factory.CreateGeometryEngine(model, loggerFactory);
            _dynamicDeflection = new DynamicDeflection(model.ModelFactors, _engine, _logger);
            _modelServices = ((IXGeometryEngineV6)_engine).ModelGeometryService;

            // Apply vendor workarounds
            model.AddRevitWorkArounds();
            model.AddWorkAroundTrimForPolylinesIncorrectlySetToOneForEntireCurve();
            model.AddArchicadWorkArounds(_logger);
            model.AddNotImplemented2DPointByDistanceWorkaround(_logger);

            // Discover representation contexts — same logic as Xbim3DModelContext
            var builtContextList = new List<IIfcGeometricRepresentationContext>();
            builtContextList.AddRange(model.Instances.OfType<IIfcGeometricRepresentationSubContext>());

            var parentContexts = builtContextList.OfType<IIfcGeometricRepresentationSubContext>()
                .Select(x => x.ParentContext).Distinct().ToList();
            builtContextList.AddRange(parentContexts);

            var childlessContexts = model.Instances.OfType<IIfcGeometricRepresentationContext>()
                .Where(c => !(c is IIfcGeometricRepresentationSubContext))
                .Where(c => !c.HasSubContexts.Any());
            builtContextList.AddRange(childlessContexts);

            var contexts = builtContextList.Where(c =>
                string.Compare(c.ContextType, contextType, true) == 0 ||
                string.Compare(c.ContextType, "design", true) == 0).ToList();

            if (requiredContextIdentifier != null && contexts.Any())
            {
                var subContexts = contexts.Where(c => c.ContextIdentifier.HasValue
                    && requiredContextIdentifier.ToLower().Contains(c.ContextIdentifier.Value.ToString().ToLower())).ToList();
                if (subContexts.Any())
                    contexts = subContexts;
            }

            if (!contexts.Any())
            {
                contexts = model.Instances.OfType<IIfcGeometricRepresentationContext>()
                    .Where(c =>
                        string.Compare(c.ContextType, "design", true) == 0 ||
                        string.Compare(c.ContextType, "model", true) == 0).ToList();
                if (contexts.Any())
                {
                    LogInfo(this, "Unable to find contexts with Type={0} Identifier={1}, using 'Design' fallback",
                        contextType, requiredContextIdentifier);
                }
                else
                {
                    contexts = model.Instances.OfType<IIfcGeometricRepresentationContext>().ToList();
                    if (!contexts.Any())
                        LogWarning(this, "No Geometric Representation contexts found in model");
                }
            }

            _contexts = new IfcRepresentationContextCollection();
            foreach (var context in contexts)
                _contexts.Add(context);

            // In WASM, Web Worker threads are heavyweight and the pre-allocated pool is small.
            // Limit parallelism to avoid exhausting the pool (which causes deadlocks because
            // new workers require an async round-trip through the browser event loop).
            if (RuntimeInformation.OSArchitecture == Architecture.Wasm)
                MaxThreads = Math.Clamp(Environment.ProcessorCount, 1, 4);
        }

        #endregion

        #region Main API

        /// <summary>
        /// Creates the 3D geometry context asynchronously using a streaming approach.
        /// Shapes are built, meshed, and disposed immediately to minimise memory usage.
        /// </summary>
        public Task<bool> CreateContextAsync(
            ReportProgressDelegate progDelegate = null,
            bool adjustWcs = true,
            bool generateBREPs = false,
            Func<XbimTriangulatedMesh, int, XbimTriangulatedMesh> postTessellationCallback = null,
            DynamicDeflectionSettings dynamicDeflectionSettings = null,
            CancellationToken cancellationToken = default)
        {
            // Run the synchronous pipeline on a thread-pool thread to avoid blocking callers
            return Task.Run(() => CreateContextCore(progDelegate, adjustWcs, generateBREPs, postTessellationCallback, dynamicDeflectionSettings, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Creates the 3D geometry context synchronously.
        /// </summary>
        public bool CreateContext(ReportProgressDelegate progDelegate = null, bool adjustWcs = true,
            bool generateBREPs = false,
            Func<XbimTriangulatedMesh, int, XbimTriangulatedMesh> postTessellationCallback = null,
            DynamicDeflectionSettings dynamicDeflectionSettings = null)
        {
            return CreateContextCore(progDelegate, adjustWcs, generateBREPs, postTessellationCallback, dynamicDeflectionSettings, cancellationToken: CancellationToken.None);
        }

        private bool CreateContextCore(
            ReportProgressDelegate progDelegate,
            bool adjustWcs,
            bool generateBREPs,
            Func<XbimTriangulatedMesh, int, XbimTriangulatedMesh> postTessellationCallback,
            DynamicDeflectionSettings dynamicDeflectionSettings,
            CancellationToken cancellationToken,
            ChannelWriter<SinkEvent> sinkChannel = null)
        {
            _logger.LogTrace("Starting context creation");

            if (_contexts == null || _engine == null)
            {
                _logger.LogWarning("No model context or engine found");
                return false;
            }

            var geometryStore = _model.GeometryStore;
            if (geometryStore == null)
            {
                _logger.LogWarning("No GeometryStore in model");
                return false;
            }

            using var geometryTransaction = geometryStore.BeginInit();
            if (geometryTransaction == null)
            {
                _logger.LogWarning("Failed to begin geometry transaction");
                return false;
            }

            using var state = new StreamingState();
            state.SinkChannel = sinkChannel;

            state.ParallelOptions = new ParallelOptions();
            if (MaxThreads > 0)
                state.ParallelOptions.MaxDegreeOfParallelism = MaxThreads;
            if (cancellationToken != CancellationToken.None)
                state.ParallelOptions.CancellationToken = cancellationToken;

            // Phase 0: Pre-process metadata
            progDelegate?.Invoke(-1, "Initialise");
            using (_diag.TrackPhase("Initialise"))
            {
                if (!PreProcess(state, adjustWcs))
                    return false;
            }
            progDelegate?.Invoke(101, "Initialise");

            // Emit header and styles to the streaming channel (before any geometries)
            if (sinkChannel != null)
                EmitHeaderAndStyles(state);

            // Phase 1: Mesh all unique shape geometries
            _logger.LogTrace("Starting WriteShapeGeometries");
            using (_diag.TrackPhase("WriteShapeGeometries"))
                WriteShapeGeometries(state, progDelegate, geometryTransaction, generateBREPs, postTessellationCallback, dynamicDeflectionSettings);

            // Phase 2: Resolve mapped item references
            _logger.LogTrace("Starting PrepareMapGeometryReferences");
            using (_diag.TrackPhase("PrepareMapGeometryReferences"))
                PrepareMapGeometryReferences(state, progDelegate);

            // Phase 3: Process featured products (boolean operations)
            HashSet<int> processed;
            _logger.LogTrace("Starting ProcessFeaturedProducts");
            using (_diag.TrackPhase("ProcessFeaturedProducts"))
                processed = ProcessFeaturedProducts(state, progDelegate, geometryTransaction);

            // Phase 4: Write remaining product shape instances
            _logger.LogTrace("Starting WriteProductShapes");
            progDelegate?.Invoke(-1, "WriteProductShapes");
            var remainingProducts = _model.Instances.OfType<IIfcProduct>()
                .Where(p => p.Representation != null && !processed.Contains(p.EntityLabel))
                .ToList();
            using (_diag.TrackPhase("WriteProductShapes"))
                WriteProductShapes(state, remainingProducts, geometryTransaction);
            progDelegate?.Invoke(101, "WriteProductShapes");

            // Phase 5: Write regions
            _logger.LogTrace("Starting WriteRegionsToStore");
            progDelegate?.Invoke(-1, "WriteRegionsToDb");
            using (_diag.TrackPhase("WriteRegionsToStore"))
            {
                int nextRegion = 1;
                foreach (var cluster in state.Clusters)
                    nextRegion = WriteRegionsToStore(cluster.Key, cluster.Value, geometryTransaction,
                        state.PlacementTree.WorldCoordinateSystem, nextRegion);
            }
            progDelegate?.Invoke(101, "WriteRegionsToDb");

            geometryTransaction.Commit();
            _logger.LogTrace("Context creation complete");
            return true;
        }

        /// <summary>
        /// Creates the geometry context and streams results to the sink as they are produced.
        /// The processing pipeline and the sink consumer run concurrently — geometries and
        /// instances appear in the viewer as soon as they are tessellated or boolean-resolved,
        /// rather than waiting for the entire model to complete. The geometry store is still
        /// fully populated for subsequent WexBIM export.
        /// </summary>
        public async Task<bool> CreateContextStreamingAsync(
            ISceneStreamSink sink,
            bool adjustWcs = true,
            bool generateBREPs = false,
            ReportProgressDelegate progDelegate = null,
            CancellationToken cancellationToken = default)
        {
            var channel = Channel.CreateUnbounded<SinkEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            // Producer: runs the 5-phase pipeline on a thread-pool thread,
            // writing geometry/instance events into the channel as they complete.
            var producerTask = Task.Run(() =>
            {
                try
                {
                    return CreateContextCore(
                        progDelegate: progDelegate,
                        adjustWcs: adjustWcs,
                        generateBREPs: generateBREPs,
                        postTessellationCallback: null,
                        dynamicDeflectionSettings: null,
                        cancellationToken: cancellationToken,
                        sinkChannel: channel.Writer);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);

            // Consumer: reads channel events and dispatches to the sink.
            // Runs concurrently on the async context while the producer fills the channel.
            await SinkEventConsumer.ConsumeAsync(channel.Reader, sink, cancellationToken);

            var success = await producerTask;
            await sink.Complete();
            return success;
        }

        /// <summary>
        /// Streams the already-created geometry context to a sink for progressive rendering.
        /// Call this after <see cref="CreateContextAsync"/> has completed successfully.
        /// Each geometry and instance is emitted individually, yielding to the caller
        /// between items so a viewer can render progressively.
        /// </summary>
        public async Task StreamToSinkAsync(ISceneStreamSink sink, CancellationToken ct = default)
        {
            await StreamFromStore(sink, ct);
        }

        /// <summary>
        /// Emits the header and all styles to the streaming channel before geometry processing begins.
        /// Styles include both explicit surface styles and synthetic type-default styles.
        /// </summary>
        private void EmitHeaderAndStyles(StreamingState state)
        {
            var ch = state.SinkChannel;
            var mf = _model.ModelFactors;

            var productCount = _model.Instances.OfType<IIfcProduct>()
                .Count(p => p.Representation != null);

            ch.TryWrite(SinkEvent.ForHeader(new StreamHeader(
                (float)mf.OneMeter, 0, 0, 0, XbimRect3D.Empty, productCount)));

            var emitted = new HashSet<int>();

            // Positive styles (from surface style map)
            foreach (var styleLabel in state.SurfaceStyles.Values.Where(v => v > 0))
            {
                if (emitted.Add(styleLabel))
                {
                    var (r, g, b, a) = ResolveStyleColor(styleLabel);
                    ch.TryWrite(SinkEvent.ForStyle(styleLabel, r, g, b, a));
                }
            }

            // Synthetic type-default styles (negative type IDs)
            var colourMap = new Xbim.Ifc.XbimColourMap();
            var typeIds = _model.Instances.OfType<IIfcProduct>()
                .Where(p => p.Representation != null)
                .Select(p => _model.Metadata.ExpressTypeId(p))
                .Distinct();

            foreach (var typeId in typeIds)
            {
                int styleKey = -typeId;
                if (emitted.Add(styleKey))
                {
                    var typeName = _model.Metadata.GetType(typeId)?.Name;
                    var colour = typeName != null ? colourMap[typeName] : Xbim.Ifc.XbimColour.DefaultColour;
                    ch.TryWrite(SinkEvent.ForStyle(styleKey,
                        (float)colour.Red, (float)colour.Green, (float)colour.Blue, (float)colour.Alpha));
                }
            }
        }

        private async Task StreamFromStore(ISceneStreamSink sink, CancellationToken ct)
        {
            var mf = _model.ModelFactors;

            // Compute model bounds from the largest region
            var estimatedBounds = XbimRect3D.Empty;
            var largestRegion = GetLargestRegion();
            if (largestRegion != null)
            {
                estimatedBounds = new XbimRect3D(
                    largestRegion.Centre.X - largestRegion.Size.X / 2,
                    largestRegion.Centre.Y - largestRegion.Size.Y / 2,
                    largestRegion.Centre.Z - largestRegion.Size.Z / 2,
                    largestRegion.Size.X, largestRegion.Size.Y, largestRegion.Size.Z);
            }

            var productCount = _model.Instances.OfType<IIfcProduct>()
                .Count(p => p.Representation != null);

            await sink.WriteHeader(new StreamHeader(
                (float)mf.OneMeter, 0, 0, 0, estimatedBounds, productCount));

            using var reader = _model.GeometryStore.BeginRead();

            // Filter to only renderable instances:
            // - OpeningsAndAdditionsIncluded = final boolean-result shapes (the visible ones)
            // - Skip OpeningsAndAdditionsOnly = raw opening/projection element geometry
            // - Skip OpeningsAndAdditionsExcluded = pre-boolean body shapes (superseded)
            var visibleInstances = reader.ShapeInstances
                .Where(i => i.RepresentationType == XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded)
                .ToList();

            // Match the WexBIM writer's style resolution:
            // - StyleLabel > 0 → resolve from IIfcSurfaceStyle entity
            // - StyleLabel <= 0 → use negative IfcTypeId as synthetic style key,
            //   resolved via XbimColourMap (default colours per IFC product type)
            var colourMap = new Xbim.Ifc.XbimColourMap();
            var emittedStyles = new HashSet<int>();
            foreach (var inst in visibleInstances)
            {
                int styleKey = inst.StyleLabel > 0
                    ? inst.StyleLabel
                    : -inst.IfcTypeId;

                if (emittedStyles.Add(styleKey))
                {
                    if (styleKey > 0)
                    {
                        var (r, g, b, a) = ResolveStyleColor(styleKey);
                        await sink.WriteStyle(styleKey, r, g, b, a);
                    }
                    else
                    {
                        var typeName = _model.Metadata.GetType((short)Math.Abs(styleKey))?.Name;
                        var colour = typeName != null ? colourMap[typeName] : Xbim.Ifc.XbimColour.DefaultColour;
                        await sink.WriteStyle(styleKey,
                            (float)colour.Red, (float)colour.Green, (float)colour.Blue, (float)colour.Alpha);
                    }
                }
            }

            // Emit only geometries referenced by visible instances
            var referencedGeometries = new HashSet<int>(visibleInstances.Select(i => i.ShapeGeometryLabel));
            foreach (var geom in reader.ShapeGeometries)
            {
                ct.ThrowIfCancellationRequested();
                if (!referencedGeometries.Contains(geom.ShapeLabel)) continue;
                var meshBytes = ((IXbimShapeGeometryData)geom).ShapeData;
                if (meshBytes == null || meshBytes.Length == 0) continue;
                await sink.WriteGeometry(geom.ShapeLabel, meshBytes, geom.BoundingBox);
            }

            // Emit instances — one by one so the viewer can render progressively
            foreach (var inst in visibleInstances)
            {
                ct.ThrowIfCancellationRequested();
                int styleKey = inst.StyleLabel > 0
                    ? inst.StyleLabel
                    : -inst.IfcTypeId;

                await sink.WriteInstance(new SceneInstance(
                    inst.IfcProductLabel,
                    inst.IfcTypeId,
                    inst.ShapeGeometryLabel,
                    styleKey,
                    inst.Transformation,
                    inst.BoundingBox));
            }

            await sink.Complete();
        }

        private (float r, float g, float b, float a) ResolveStyleColor(int styleLabel)
        {
            var entity = _model.Instances[styleLabel];

            // StyleLabel typically points to an IIfcSurfaceStyle — look inside its Styles
            // collection for the shading/rendering element that carries the colour.
            if (entity is IIfcSurfaceStyle surfaceStyle)
            {
                var shading = surfaceStyle.Styles
                    .OfType<IIfcSurfaceStyleShading>()
                    .FirstOrDefault();
                if (shading?.SurfaceColour != null)
                    return ExtractColor(shading);
            }

            // Fallback: direct shading entity (unlikely but defensive)
            if (entity is IIfcSurfaceStyleShading directShading && directShading.SurfaceColour != null)
                return ExtractColor(directShading);

            return (0.8f, 0.8f, 0.8f, 1f);
        }

        private static (float r, float g, float b, float a) ExtractColor(IIfcSurfaceStyleShading shading)
        {
            var c = shading.SurfaceColour;
            float r = (float)c.Red;
            float g = (float)c.Green;
            float b = (float)c.Blue;
            float a = 1f;
            if (shading is IIfcSurfaceStyleRendering rendering && rendering.Transparency.HasValue)
                a = 1f - (float)rendering.Transparency.Value;
            return (r, g, b, a);
        }

        private static IIfcSurfaceStyle ResolveMaterialSurfaceStyle(IIfcMaterialSelect matSelect)
        {
            var materials = new List<IIfcMaterial>();
            switch (matSelect)
            {
                case IIfcMaterial m:
                    materials.Add(m);
                    break;
                case IIfcMaterialList ml:
                    materials.AddRange(ml.Materials);
                    break;
                case IIfcMaterialLayerSetUsage mlsu:
                    materials.AddRange(mlsu.ForLayerSet.MaterialLayers
                        .Where(l => l.Material != null).Select(l => l.Material));
                    break;
                case IIfcMaterialLayerSet mls:
                    materials.AddRange(mls.MaterialLayers
                        .Where(l => l.Material != null).Select(l => l.Material));
                    break;
                case IIfcMaterialLayer ml2:
                    if (ml2.Material != null) materials.Add(ml2.Material);
                    break;
                case IIfcMaterialConstituentSet mcs:
                    materials.AddRange(mcs.MaterialConstituents
                        .Where(c => c.Material != null).Select(c => c.Material));
                    break;
                case IIfcMaterialConstituent mc:
                    if (mc.Material != null) materials.Add(mc.Material);
                    break;
                case IIfcMaterialProfileSetUsage mpsu:
                    materials.AddRange(mpsu.ForProfileSet.MaterialProfiles
                        .Where(p => p.Material != null).Select(p => p.Material));
                    break;
                case IIfcMaterialProfileSet mps:
                    materials.AddRange(mps.MaterialProfiles
                        .Where(p => p.Material != null).Select(p => p.Material));
                    break;
                case IIfcMaterialProfile mp:
                    if (mp.Material != null) materials.Add(mp.Material);
                    break;
            }

            foreach (var mat in materials)
            {
                foreach (var rep in mat.HasRepresentation.OfType<IIfcMaterialDefinitionRepresentation>())
                {
                    foreach (var styledRep in rep.Representations.OfType<IIfcStyledRepresentation>())
                    {
                        var surfaceStyle = styledRep.Items
                            .OfType<IIfcStyledItem>()
                            .SelectMany(si => si.Styles.SelectMany(s => s.SurfaceStyles))
                            .FirstOrDefault();
                        if (surfaceStyle != null)
                            return surfaceStyle;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Pipeline methods

        /// <summary>
        /// Pre-processes model metadata: placement tree, openings/projections, shape IDs, styles, clusters.
        /// </summary>
        private bool PreProcess(StreamingState state, bool adjustWcs)
        {
            try
            {
                state.PlacementTree = new XbimPlacementTree(_model, _engine, adjustWcs);

                // Discover openings and projections
                GetOpeningsAndProjections(state);

                state.VoidedProductIds = new HashSet<int>();
                state.VoidedShapeIds = new HashSet<int>();
                foreach (var voidedId in state.OpeningsAndProjections.Select(op => op.Key.EntityLabel))
                    state.VoidedProductIds.Add(voidedId);

                GetProductShapeIds(state);
                GetSurfaceStyles(state);
                GetClusters(state);
                return true;
            }
            catch (Exception e)
            {
                LogError("Pre-processing failed", e);
                return false;
            }
        }

        /// <summary>
        /// Meshes all unique shape geometries. Feature element shapes are cached as IXShape for
        /// later boolean operations. All other shapes (including voided product shapes) are
        /// disposed immediately after meshing.
        /// </summary>
        private void WriteShapeGeometries(StreamingState state, ReportProgressDelegate progDelegate,
            IGeometryStoreInitialiser txn, bool generateBREPs,
            Func<XbimTriangulatedMesh, int, XbimTriangulatedMesh> postTessellationCallback,
            DynamicDeflectionSettings dynamicDeflectionSettings)
        {
            var localTally = 0;
            var localPercentageParsed = 0;
            var total = state.ProductShapeIds.Count + state.OpeningsAndProjections.Count;
            var xbimTessellator = new XbimTessellator(_model, XbimGeometryType.PolyhedronBinary,
                postTessellationCallback: postTessellationCallback);
            var v6engine = (IXGeometryEngineV6)_engine;
            var meshFactory = _modelServices.WexBimMeshFactory;
            var precision = _model.ModelFactors.Precision;
            var deflection = _model.ModelFactors.DeflectionTolerance;
            var deflectionAngle = _model.ModelFactors.DeflectionAngle;
            var manifoldService = EnableManifoldBooleans ? new ManifoldMeshBooleanService() : null;

            progDelegate?.Invoke(-1, "WriteShapeGeometries (" + state.ProductShapeIds.Count + " shapes)");

            // Process grids first (they use a special engine method)
            foreach (var grid in _model.Instances.OfType<IIfcGrid>())
            {
                using (var geomModel = _engine.CreateGrid(grid, _logger))
                {
                    if (geomModel != null && geomModel.IsValid)
                    {
                        var shapeGeom = _engine.CreateShapeGeometry(geomModel, precision, deflection, deflectionAngle,
                            XbimGeometryType.PolyhedronBinary, _logger);
                        shapeGeom.IfcShapeLabel = grid.EntityLabel;
                        var refCounter = new GeometryReference
                        {
                            BoundingBox = shapeGeom.BoundingBox,
                            GeometryId = txn.AddShapeGeometry(shapeGeom),
                            LocalShapeDisplacement = shapeGeom.LocalShapeDisplacement
                        };
                        state.ShapeLookup.TryAdd(grid.EntityLabel, refCounter);

                        state.SinkChannel?.TryWrite(SinkEvent.ForGeometry(
                            refCounter.GeometryId, ((IXbimShapeGeometryData)shapeGeom).ShapeData, shapeGeom.BoundingBox));
                    }
                }
            }

            var processed = new ConcurrentDictionary<int, byte>();

            try
            {
                Parallel.ForEach(state.ProductShapeIds, state.ParallelOptions, shapeId =>
                {
                    using var logScope = _logger.BeginScope("WriteShapeGeometry {entityLabel}", shapeId);
                    if (processed.TryGetValue(shapeId, out byte _dummy))
                        return;
                    processed.TryAdd(shapeId, 0);
                    Interlocked.Increment(ref localTally);

                    IIfcGeometricRepresentationItem shape;
                    try
                    {
                        shape = (IIfcGeometricRepresentationItem)_model.Instances[shapeId];
                    }
                    catch (Exception ex)
                    {
                        LogError(string.Format("Error getting entity #{0}. Geometry ignored.", shapeId), ex);
                        return;
                    }
                    if (shape == null)
                    {
                        LogError(string.Format("Entity #{0} not found. Geometry ignored.", shapeId));
                        return;
                    }

                    var isFeatureElement = state.FeatureElementShapeIds.Contains(shapeId);
                    var isVoidedProduct = state.VoidedShapeIds.Contains(shapeId);

                    XbimShapeGeometry shapeGeom = null;
                    IXShape builtShape = null;
                    bool canDirectTessellate = xbimTessellator.CanMesh(shape);

                    // Mesh-based boolean clipping fast path
                    if (manifoldService != null && !generateBREPs && !canDirectTessellate
                        && shape is IIfcBooleanClippingResult clippingResult)
                    {
                        using (_diag.Track(DiagOp.ManifoldClipping))
                        {
                            try
                            {
                                var resolver = new BooleanClippingMeshResolver(xbimTessellator);
                                var meshResult = resolver.TryResolve(clippingResult);
                                if (meshResult != null)
                                {
                                    shapeGeom = xbimTessellator.SerializeRawMeshToBinary(
                                        meshResult.Value.Positions, meshResult.Value.Indices, shape.EntityLabel);

                                    // Cache for Manifold voided-product path if needed
                                    if (isVoidedProduct && !isFeatureElement)
                                        state.MeshBodiesCache.TryAdd(shapeId,
                                            (meshResult.Value.Positions, meshResult.Value.Indices, meshResult.Value.Bounds));
                                    if (isFeatureElement)
                                        state.MeshFeaturesCache.TryAdd(shapeId,
                                            (meshResult.Value.Positions, meshResult.Value.Indices, meshResult.Value.Bounds));
                                }
                            }
                            catch
                            {
                                // Serialization or mesh issue — fall through to BRep path
                                shapeGeom = null;
                            }
                        }
                    }

                    // Fast path: direct tessellation (now includes voided/feature shapes)
                    if (shapeGeom == null && !generateBREPs && canDirectTessellate)
                    {
                        using (_diag.Track(DiagOp.DirectTessellation))
                        {
                            try
                            {
                                if (isVoidedProduct || isFeatureElement)
                                {
                                    var meshData = XbimTessellator.ExtractRawMesh(
                                        xbimTessellator.MeshToTriangulatedMesh(shape));
                                    shapeGeom = xbimTessellator.SerializeRawMeshToBinary(
                                        meshData.Positions, meshData.Indices, shape.EntityLabel);

                                    if (isFeatureElement)
                                        state.MeshFeaturesCache.TryAdd(shapeId, meshData);
                                    if (isVoidedProduct && !isFeatureElement)
                                        state.MeshBodiesCache.TryAdd(shapeId, meshData);
                                }
                                else
                                {
                                    shapeGeom = xbimTessellator.Mesh(shape);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning("Direct tessellation failed for #{0}=({1}): {2}",
                                    shape.EntityLabel, shape.GetType().Name, ex.Message);
                                shapeGeom = null;
                            }
                        }
                    }
                    else if (shapeGeom == null)
                    {
                        // Full path: build BRep via engine, then mesh
                        try
                        {
                            var timer = _diag.StartTimer();
                            builtShape = v6engine.Build(shape);
                            _diag.RecordCreate(timer, shape.GetType().Name, failed: builtShape == null);
                        }
                        catch (XbimGeometryFaceSetTooLargeException fse)
                        {
                            int faceSetLabel = (int)fse.Data["LargeFaceSetLabel"];
                            _logger.LogWarning(fse, "Large face set #{0} handled as mesh", faceSetLabel);
                            shapeGeom = xbimTessellator.Mesh((IIfcRepresentationItem)_model.Instances[faceSetLabel]);
                        }
                        catch (XbimGeometryServiceException)
                        {
                            _logger.LogWarning("Failed to build geometry for #{0}=({1})",
                                shape.EntityLabel, shape.GetType().Name.ToUpper());
                        }
                        catch (XbimGeometryFactoryException)
                        {
                            _logger.LogWarning("Failed to build geometry for #{0}=({1})",
                                shape.EntityLabel, shape.GetType().Name.ToUpper());
                        }
                        catch (XbimGeometryNotSupportedException)
                        {
                            _logger.LogWarning("Geometry not supported for #{0}=({1})",
                                shape.EntityLabel, shape.GetType().Name.ToUpper());
                        }

                        if (builtShape != null && shapeGeom == null)
                        {
                            // Apply dynamic deflection if configured
                            var thisDeflection = deflection;
                            var thisAngle = deflectionAngle;
                            if (dynamicDeflectionSettings != null)
                            {
                                // Need the bounding box for dynamic deflection — get it from the mesh bounds
                                var bbox = ((IXbimGeometryObject)builtShape).BoundingBox;
                                var def = _dynamicDeflection.GetDeflection(
                                    shape, bbox, deflection, deflectionAngle, dynamicDeflectionSettings);
                                thisDeflection = def.Linear;
                                thisAngle = def.Angular;
                            }

                            var meshTimer = _diag.StartTimer();
                            shapeGeom = MeshShape(builtShape, shapeId, precision, thisDeflection, thisAngle);
                            _diag.RecordMesh(meshTimer, shape.GetType().Name);

                            // Cache feature element shapes for boolean operations
                            if (isFeatureElement)
                            {
                                state.FeaturesCache.TryAdd(shapeId, builtShape);
                                builtShape = null; // prevent disposal below
                            }

                            // Cache voided product body shapes to avoid rebuilding in ProcessFeaturedProducts
                            if (isVoidedProduct && !isFeatureElement)
                            {
                                state.FeaturedBodiesCache.TryAdd(shapeId, builtShape);
                                builtShape = null; // prevent disposal below
                            }
                        }
                    }

                    // Dispose the BRep if not cached
                    if (builtShape != null)
                        (builtShape as IDisposable)?.Dispose();

                    // Store the mesh in the geometry store
                    if (shapeGeom == null || shapeGeom.ShapeData == null || shapeGeom.ShapeData.Length == 0)
                    {
                        LogDebug(shape, "Is an empty shape");
                    }
                    else if (shapeGeom.BoundingBox.SizeX >= 1e100)
                    {
                        LogWarning(shape, "Is an invalid shape");
                    }
                    else
                    {
                        shapeGeom.IfcShapeLabel = shapeId;
                        var reference = new GeometryReference
                        {
                            BoundingBox = shapeGeom.BoundingBox,
                            GeometryId = txn.AddShapeGeometry(shapeGeom),
                            LocalShapeDisplacement = shapeGeom.LocalShapeDisplacement
                        };
                        GetStyleId(state.SurfaceStyles, shapeGeom.IfcShapeLabel, out int styleLabel);
                        reference.StyleLabel = styleLabel;
                        state.ShapeLookup.TryAdd(shapeGeom.IfcShapeLabel, reference);

                        // Emit geometry to streaming channel
                        state.SinkChannel?.TryWrite(SinkEvent.ForGeometry(
                            reference.GeometryId, ((IXbimShapeGeometryData)shapeGeom).ShapeData, shapeGeom.BoundingBox));
                    }

                    // Progress reporting
                    if (progDelegate != null)
                    {
                        var newPercentage = Convert.ToInt32((double)localTally / total * 100.0);
                        if (newPercentage > localPercentageParsed)
                        {
                            Interlocked.Exchange(ref localPercentageParsed, newPercentage);
                            progDelegate(localPercentageParsed, "Creating Geometry");
                        }
                    }
                });
            }
            catch (AggregateException e)
            {
                foreach (var ex in e.InnerExceptions)
                    LogError("Processing failure", ex);
                throw new XbimException("Processing halted due to model error", e);
            }

            progDelegate?.Invoke(101, "WriteShapeGeometries, (" + localTally + " written)");
        }

        /// <summary>
        /// Resolves IIfcMappedItem references to their source geometry entries.
        /// </summary>
        private void PrepareMapGeometryReferences(StreamingState state, ReportProgressDelegate progDelegate)
        {
            progDelegate?.Invoke(-1, "WriteMappedItems (" + state.MappedShapeIds.Count + " items)");

            Parallel.ForEach(state.MappedShapeIds, state.ParallelOptions, mapId =>
            {
                using var _ = _logger.BeginScope("PrepareMapGeometryReferences {entityLabel}", mapId);
                var entity = _model.Instances[mapId];
                if (!(entity is IIfcMappedItem map))
                {
                    LogError(_model.Instances[entity.EntityLabel], "Is an illegal entity in maps collection");
                    return;
                }

                var mapShapes = new List<GeometryReference>();
                foreach (var mapShape in map.MappingSource.MappedRepresentation.Items)
                {
                    var mapShapeLabel = mapShape.EntityLabel;
                    if (state.ShapeLookup.TryGetValue(mapShapeLabel, out GeometryReference mapGeometryRef))
                    {
                        if (GetStyleId(state.SurfaceStyles, map.EntityLabel, out int style))
                            mapGeometryRef.StyleLabel = style;
                        else if (GetStyleId(state.SurfaceStyles, mapShapeLabel, out style))
                            mapGeometryRef.StyleLabel = style;
                        mapShapes.Add(mapGeometryRef);
                    }
                    else if (!(mapShape is IIfcGeometricSet) && !(mapShape is IIfcMappedItem))
                    {
                        LogWarning(_model.Instances[mapShape.EntityLabel], "Failed to find shape in map");
                    }
                }

                if (mapShapes.Any())
                    state.MapGeometryReferences.TryAdd(map.EntityLabel, mapShapes);

                var targetTransform = map.MappingTarget.ToMatrix3D();
                var sourceTransform = map.MappingSource.MappingOrigin.ToMatrix3D();
                state.MapTransforms.TryAdd(map.EntityLabel,
                    XbimMatrix3D.Multiply(targetTransform, sourceTransform));
            });

            progDelegate?.Invoke(101, "WriteMappedItems, (" + state.MappedShapeIds.Count + " written)");
        }

        /// <summary>
        /// Processes products that have openings or projections. For each featured product:
        /// writes excluded instances, rebuilds body BRep from IFC, performs boolean operations
        /// using cached feature shapes, meshes the result, and writes the included instance.
        /// </summary>
        private HashSet<int> ProcessFeaturedProducts(StreamingState state,
            ReportProgressDelegate progDelegate, IGeometryStoreInitialiser txn)
        {
            var processed = new ConcurrentDictionary<int, byte>();
            var localTally = 0;
            var localPercentageParsed = 0;
            var featureCount = state.OpeningsAndProjections.Count;

            progDelegate?.Invoke(-1, "WriteFeatureElements (" + featureCount + " elements)");

            var v6engine = (IXGeometryEngineV6)_engine;
            var shapeFactory = _modelServices.ShapeFactory;
            var meshFactory = _modelServices.WexBimMeshFactory;
            var mf = _model.ModelFactors;
            var manifoldService = new ManifoldMeshBooleanService();
            var xbimTessellator = new XbimTessellator(_model, XbimGeometryType.PolyhedronBinary);

            Parallel.ForEach(state.OpeningsAndProjections, state.ParallelOptions, elementToFeatureGroup =>
            {
                var element = elementToFeatureGroup.Key;
                using var scope = _logger.BeginScope("ProcessFeaturedProduct {entityLabel}", element.EntityLabel);
                Interlocked.Increment(ref localTally);

                // Progress
                if (progDelegate != null)
                {
                    var pct = Convert.ToInt32((double)localTally / featureCount * 100.0);
                    if (pct > localPercentageParsed)
                    {
                        Interlocked.Exchange(ref localPercentageParsed, pct);
                        progDelegate(localPercentageParsed, "Building Elements");
                    }
                }

                try
                {
                    // Step 1: Write Excluded shape instances for the element
                    int contextId = 0;
                    int styleId = 0;
                    var elementInstances = WriteProductShape(state, element, false, txn);
                    foreach (var inst in elementInstances)
                    {
                        contextId = inst.RepresentationContext;
                        if (inst.StyleLabel > 0) styleId = inst.StyleLabel;
                    }

                    if (elementInstances.Count == 0)
                    {
                        processed.TryAdd(element.EntityLabel, 0);
                        return; // parallel loop continue
                    }

                    // Step 2: Write feature instances (OpeningsAndAdditionsOnly)
                    foreach (var feature in elementToFeatureGroup)
                    {
                        WriteProductShape(state, feature, false, txn);
                        processed.TryAdd(feature.EntityLabel, 0);
                    }

                    // Step 3: Determine custom meshing behaviour
                    var thisDeflection = mf.DeflectionTolerance;
                    var thisAngle = mf.DeflectionAngle;
                    var behaviour = MeshingBehaviourResult.Default;

                    if (CustomMeshingBehaviour != null)
                    {
                        behaviour = CustomMeshingBehaviour(element.EntityLabel, element.ExpressType.TypeId,
                            ref thisDeflection, ref thisAngle);
                        if (behaviour == MeshingBehaviourResult.Skip)
                        {
                            processed.TryAdd(element.EntityLabel, 0);
                            return;
                        }
                    }

                    // === Mesh Boolean Fast Path ===
                    // Try Manifold mesh booleans before falling back to OCCT BRep booleans.
                    // This works when both the body and all features have cached mesh data
                    // (i.e., they were directly tessellated during WriteShapeGeometries).
                    if (EnableManifoldBooleans &&
                        TryManifoldBooleans(state, element, elementToFeatureGroup, behaviour,
                            manifoldService, xbimTessellator, contextId, styleId, txn))
                    {
                        processed.TryAdd(element.EntityLabel, 0);
                        return; // mesh boolean succeeded, skip BRep path
                    }

                    // === BRep Boolean Path (fallback) ===
                    // Step 4: Get body BRep (from cache or rebuild from IFC)
                    var disposeBag = new List<IDisposable>();
                    try
                    {
                        var body = BuildProductBody(state, element, v6engine, disposeBag);
                        if (body == null)
                        {
                            LogWarning(element, "Failed to rebuild body for boolean operations");
                            processed.TryAdd(element.EntityLabel, 0);
                            return;
                        }

                        // Step 5: Collect feature shapes (from cache), transform to world coords
                        var openings = new List<IXShape>();
                        var projections = new List<IXShape>();

                        foreach (var feature in elementToFeatureGroup)
                        {
                            var featureShape = GetOrBuildFeatureShape(state, feature, v6engine);
                            if (featureShape == null) continue;

                            var featurePlacement = XbimPlacementTree.GetTransform(feature, state.PlacementTree, _engine, _logger);
                            var featureWorld = shapeFactory.Transform(featureShape, featurePlacement);
                            if (featureWorld != null && featureWorld is IDisposable fd)
                                disposeBag.Add(fd);

                            if (featureWorld != null)
                            {
                                if (feature is IIfcFeatureElementSubtraction)
                                    openings.Add(featureWorld);
                                else
                                    projections.Add(featureWorld);
                            }
                        }

                        // Step 6: Boolean operations
                        var current = body;

                        if (behaviour.HasFlag(MeshingBehaviourResult.PerformAdditions) && projections.Any())
                        {
                            IXShape unionResult;
                            using (_diag.Track(DiagOp.BooleanUnion))
                                unionResult = shapeFactory.Union(current, projections);
                            if (unionResult != null)
                            {
                                if (unionResult is IDisposable ud) disposeBag.Add(ud);
                                current = unionResult;
                            }
                            else
                            {
                                LogWarning(element, "Joining of projections has failed. Projections ignored");
                            }
                        }

                        if (behaviour.HasFlag(MeshingBehaviourResult.PerformSubtractions) && openings.Any())
                        {
                            try
                            {
                                IXShape cutResult;
                                using (_diag.Track(DiagOp.BooleanCut))
                                    cutResult = shapeFactory.Cut(current, openings);
                                if (cutResult != null)
                                {
                                    if (cutResult is IDisposable cd) disposeBag.Add(cd);
                                    current = cutResult;
                                }
                                else
                                {
                                    LogWarning(element, "Cutting openings has failed. Openings ignored");
                                }
                            }
                            catch (TimeoutException)
                            {
                                LogWarning(element, "Cutting openings timed out after {0} seconds. Openings ignored",
                                    BooleanTimeOutMilliSeconds / 1000);
                            }
                        }

                        // Step 7: Mesh the boolean result
                        var meshData = meshFactory.CreateWexBimMesh(
                            current, mf.Precision, thisDeflection, thisAngle, 1.0, out var bounds);

                        if (meshData != null && meshData.Length > 0 && !bounds.IsVoid)
                        {
                            var bbox = new XbimRect3D(
                                bounds.CornerMin.X, bounds.CornerMin.Y, bounds.CornerMin.Z,
                                bounds.LenX, bounds.LenY, bounds.LenZ);

                            var shapeGeometry = new XbimShapeGeometry
                            {
                                IfcShapeLabel = element.EntityLabel,
                                GeometryHash = 0,
                                LOD = XbimLOD.LOD_Unspecified,
                                Format = XbimGeometryType.PolyhedronBinary,
                                BoundingBox = bbox
                            };
                            ((IXbimShapeGeometryData)shapeGeometry).ShapeData = meshData;

                            var shapeInstance = new XbimShapeInstance
                            {
                                IfcProductLabel = element.EntityLabel,
                                ShapeGeometryLabel = 0,
                                StyleLabel = styleId,
                                RepresentationType = XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded,
                                RepresentationContext = contextId,
                                IfcTypeId = _model.Metadata.ExpressTypeId(element),
                                Transformation = XbimMatrix3D.Identity,
                                BoundingBox = bbox
                            };

                            shapeInstance.ShapeGeometryLabel = txn.AddShapeGeometry(shapeGeometry);
                            txn.AddShapeInstance(shapeInstance, shapeInstance.ShapeGeometryLabel);

                            // Emit boolean result to streaming channel
                            EmitGeometryAndInstance(state, shapeInstance, meshData, bbox);
                        }

                        processed.TryAdd(element.EntityLabel, 0);
                    }
                    finally
                    {
                        foreach (var d in disposeBag) d?.Dispose();
                    }
                }
                catch (Exception e)
                {
                    LogWarning(e, element, "Contains features but geometry could not be built: {0}", e.Message);
                    processed.TryAdd(element.EntityLabel, 0);
                }
            });

            progDelegate?.Invoke(101, "WriteFeatureElements, (" + localTally + " written)");
            return new HashSet<int>(processed.Keys);
        }

        /// <summary>
        /// Writes shape instances for products that don't have features (the remaining products).
        /// </summary>
        private void WriteProductShapes(StreamingState state, IEnumerable<IIfcProduct> products,
            IGeometryStoreInitialiser txn)
        {
            // Write grid instances first
            foreach (var grid in _model.Instances.OfType<IIfcGrid>())
            {
                if (state.ShapeLookup.TryGetValue(grid.EntityLabel, out GeometryReference instance) &&
                    grid.Representation != null &&
                    grid.Representation.Representations.Count > 0)
                {
                    var placementTransform = XbimPlacementTree.GetTransform(grid, state.PlacementTree, _engine);
                    placementTransform = ApplyShapeDisplacement(instance, placementTransform);

                    var gRep = grid.Representation?.Representations?.FirstOrDefault();
                    var context = gRep?.ContextOfItems;
                    var intContext = context?.EntityLabel ?? 0;

                    WriteShapeInstanceToStore(instance.GeometryId, instance.StyleLabel, intContext, grid,
                        placementTransform, instance.BoundingBox,
                        XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded, txn, state);
                }
            }

            Parallel.ForEach(products, state.ParallelOptions, product =>
            {
                using var _ = _logger.BeginScope("WriteProductShapes {entityLabel}", product.EntityLabel);
                if (product.Representation?.Representations == null)
                    return;
                if (product.Representation.Representations.Any(r => IsInContext(r) && r.IsBodyRepresentation(BodyRepresentations)))
                    WriteProductShape(state, product, true, txn);
            });
        }

        #endregion

        #region Manifold mesh boolean fast path

        /// <summary>
        /// Returns the single IIfcExtrudedAreaSolid from a product's body representation,
        /// or null if the representation has mapped items, multiple items, tapered extrusions,
        /// or non-extrusion geometry.
        /// </summary>
        private IIfcExtrudedAreaSolid GetSingleExtrusion(IIfcProduct product)
        {
            var reps = product.Representation?.Representations?
                .Where(r => IsInContext(r) && r.IsBodyRepresentation(BodyRepresentations));
            if (reps == null) return null;

            IIfcExtrudedAreaSolid found = null;
            foreach (var rep in reps)
            {
                foreach (var item in rep.Items)
                {
                    if (item is IIfcMappedItem) return null;
                    if (item is IIfcGeometricSet) continue;
                    if (!(item is IIfcExtrudedAreaSolid ext) || item is IIfcExtrudedAreaSolidTapered)
                        return null;
                    if (found != null) return null; // more than one item
                    found = ext;
                }
            }
            return found;
        }

        /// <summary>
        /// Attempts 2D profile subtraction for coplanar extrusions. When a body and all its
        /// openings are simple extrusions with parallel directions, subtracts the opening
        /// profiles in 2D (using Clipper2 via Manifold CrossSection), then extrudes the result.
        /// Much faster than 3D mesh booleans for the common wall+openings case.
        /// </summary>
        private bool TryCoplanarProfileSubtraction(
            StreamingState state, IIfcElement element,
            IEnumerable<IIfcFeatureElement> features,
            MeshingBehaviourResult behaviour,
            XbimTessellator xbimTessellator,
            int contextId, int styleId,
            IGeometryStoreInitialiser txn)
        {
            if (!behaviour.HasFlag(MeshingBehaviourResult.PerformSubtractions))
                return false;

            var bodyExtrusion = GetSingleExtrusion(element);
            if (bodyExtrusion == null) return false;

            double angTol = element.Model.ModelFactors.DeflectionAngle;
            if (!ProfilePolygonBuilder.CanBuildPolygons(bodyExtrusion.SweptArea))
                return false;

            // Compute body world transform: solidPosition first, then elementPlacement
            var bodyPlacement = XbimPlacementTree.GetTransform(
                element, state.PlacementTree, _engine, _logger);
            var bodySolidMat = bodyExtrusion.Position != null
                ? bodyExtrusion.Position.ToMatrix3D()
                : XbimMatrix3D.Identity;
            var bodyWorldMat = XbimMatrix3D.Multiply(bodySolidMat, bodyPlacement);

            // Body extrusion direction in world space
            var bdr = bodyExtrusion.ExtrudedDirection.DirectionRatios;
            var bodyDirLocal = new XbimVector3D(
                bdr[0], bdr[1], bdr.Count > 2 ? bdr[2] : 0);
            var bodyDirWorld = bodyWorldMat.Transform(bodyDirLocal).Normalized();

            // Check every feature: must be a subtraction with a coplanar extrusion
            var openings = new List<(IIfcExtrudedAreaSolid extrusion, XbimMatrix3D openingWorldMat)>();

            foreach (var feature in features)
            {
                if (!(feature is IIfcFeatureElementSubtraction))
                    return false; // has a projection, bail out

                var openingExtrusion = GetSingleExtrusion(feature);
                if (openingExtrusion == null) return false;
                if (!ProfilePolygonBuilder.CanBuildPolygons(openingExtrusion.SweptArea))
                    return false;

                var openingPlacement = XbimPlacementTree.GetTransform(
                    feature, state.PlacementTree, _engine, _logger);
                var openingSolidMat = openingExtrusion.Position != null
                    ? openingExtrusion.Position.ToMatrix3D()
                    : XbimMatrix3D.Identity;
                var openingWorldMat = XbimMatrix3D.Multiply(openingSolidMat, openingPlacement);

                var odr = openingExtrusion.ExtrudedDirection.DirectionRatios;
                var openingDirLocal = new XbimVector3D(
                    odr[0], odr[1], odr.Count > 2 ? odr[2] : 0);
                var openingDirWorld = openingWorldMat.Transform(openingDirLocal).Normalized();

                double crossLen = XbimVector3D.CrossProduct(bodyDirWorld, openingDirWorld).Length;
                if (crossLen > 0.01) return false; // not parallel

                openings.Add((openingExtrusion, openingWorldMat));
            }

            if (openings.Count == 0) return false;

            try
            {
                return ExecuteCoplanarSubtraction(
                    state, bodyExtrusion, bodyWorldMat, angTol,
                    openings, xbimTessellator, element,
                    contextId, styleId, txn);
            }
            catch
            {
                return false;
            }
        }

        private bool ExecuteCoplanarSubtraction(
            StreamingState state,
            IIfcExtrudedAreaSolid bodyExtrusion,
            XbimMatrix3D bodyWorldMat,
            double angTol,
            List<(IIfcExtrudedAreaSolid extrusion, XbimMatrix3D openingWorldMat)> openings,
            XbimTessellator xbimTessellator,
            IIfcElement element,
            int contextId, int styleId,
            IGeometryStoreInitialiser txn)
        {
            // Build body profile (with profile position applied)
            var bodyPolygons = ProfilePolygonBuilder.BuildPolygons(bodyExtrusion.SweptArea, angTol);
            var bodyOuter = bodyPolygons.Outer;
            var bodyInners = bodyPolygons.Inners;
            if (bodyExtrusion.SweptArea is IIfcParameterizedProfileDef bp && bp.Position != null)
            {
                bodyOuter = ProfilePolygonBuilder.ApplyProfilePosition(bodyOuter, bp.Position);
                if (bodyInners != null)
                    for (int h = 0; h < bodyInners.Length; h++)
                        bodyInners[h] = ProfilePolygonBuilder.ApplyProfilePosition(bodyInners[h], bp.Position);
            }

            // Flatten body profile
            var flatBodyOuter = FlattenProfile(bodyOuter);
            FlattenInners(bodyInners, out var flatBodyInners, out var bodyInnerSizes, out int bodyNumInners);

            // Inverse of body world transform for coordinate conversion
            var bodyWorldMatInv = bodyWorldMat;
            bodyWorldMatInv.Invert();

            // Build opening profiles transformed to body's 2D profile space
            int numOpenings = openings.Count;
            var openingContours = new XbimGeometryNativeApi.XbimProfileContours[numOpenings];
            var pinHandles = new List<GCHandle>();

            try
            {
                // Pin body arrays
                var bodyOuterPin = GCHandle.Alloc(flatBodyOuter, GCHandleType.Pinned);
                pinHandles.Add(bodyOuterPin);
                GCHandle bodyInnersPin = default, bodySizesPin = default;
                if (flatBodyInners != null)
                {
                    bodyInnersPin = GCHandle.Alloc(flatBodyInners, GCHandleType.Pinned);
                    pinHandles.Add(bodyInnersPin);
                    bodySizesPin = GCHandle.Alloc(bodyInnerSizes, GCHandleType.Pinned);
                    pinHandles.Add(bodySizesPin);
                }

                for (int oi = 0; oi < numOpenings; oi++)
                {
                    var (openingExtrusion, openingWorldMat) = openings[oi];
                    var openingToBody = XbimMatrix3D.Multiply(openingWorldMat, bodyWorldMatInv);

                    var openingPolygons = ProfilePolygonBuilder.BuildPolygons(openingExtrusion.SweptArea, angTol);
                    var openingOuter = openingPolygons.Outer;
                    if (openingExtrusion.SweptArea is IIfcParameterizedProfileDef op && op.Position != null)
                        openingOuter = ProfilePolygonBuilder.ApplyProfilePosition(openingOuter, op.Position);

                    // Transform opening outer to body's 2D profile space
                    var transformedOuter = new (double X, double Y)[openingOuter.Length];
                    for (int i = 0; i < openingOuter.Length; i++)
                    {
                        var p3d = openingToBody.Transform(
                            new XbimPoint3D(openingOuter[i].X, openingOuter[i].Y, 0));
                        transformedOuter[i] = (p3d.X, p3d.Y);
                    }

                    var flatOpOuter = FlattenProfile(transformedOuter);
                    var opOuterPin = GCHandle.Alloc(flatOpOuter, GCHandleType.Pinned);
                    pinHandles.Add(opOuterPin);

                    // Opening inners (rare but handle them)
                    IntPtr opInnersPtr = IntPtr.Zero, opSizesPtr = IntPtr.Zero;
                    int opNumInners = 0;

                    if (openingPolygons.Inners?.Length > 0)
                    {
                        var openingInners = openingPolygons.Inners;
                        if (openingExtrusion.SweptArea is IIfcParameterizedProfileDef oip && oip.Position != null)
                            for (int h = 0; h < openingInners.Length; h++)
                                openingInners[h] = ProfilePolygonBuilder.ApplyProfilePosition(openingInners[h], oip.Position);

                        // Transform inners to body space
                        for (int h = 0; h < openingInners.Length; h++)
                        {
                            var inner = openingInners[h];
                            var transformed = new (double X, double Y)[inner.Length];
                            for (int i = 0; i < inner.Length; i++)
                            {
                                var p3d = openingToBody.Transform(
                                    new XbimPoint3D(inner[i].X, inner[i].Y, 0));
                                transformed[i] = (p3d.X, p3d.Y);
                            }
                            openingInners[h] = transformed;
                        }

                        FlattenInners(openingInners, out var flatOpInners, out var opInnerSizes, out opNumInners);
                        if (flatOpInners != null)
                        {
                            var opInnersPin = GCHandle.Alloc(flatOpInners, GCHandleType.Pinned);
                            pinHandles.Add(opInnersPin);
                            opInnersPtr = opInnersPin.AddrOfPinnedObject();
                            var opSizesPin = GCHandle.Alloc(opInnerSizes, GCHandleType.Pinned);
                            pinHandles.Add(opSizesPin);
                            opSizesPtr = opSizesPin.AddrOfPinnedObject();
                        }
                    }

                    openingContours[oi] = new XbimGeometryNativeApi.XbimProfileContours
                    {
                        OuterPoints = opOuterPin.AddrOfPinnedObject(),
                        OuterCount = transformedOuter.Length,
                        InnerPoints = opInnersPtr,
                        InnerSizes = opSizesPtr,
                        NumInners = opNumInners
                    };
                }

                // Pin the array of opening contour structs
                var contoursPin = GCHandle.Alloc(openingContours, GCHandleType.Pinned);
                pinHandles.Add(contoursPin);

                // Body extrusion direction and depth
                var dir = bodyExtrusion.ExtrudedDirection;
                double extDirX = dir.DirectionRatios[0];
                double extDirY = dir.DirectionRatios[1];
                double extDirZ = dir.DirectionRatios.Count > 2 ? dir.DirectionRatios[2] : 0;

                // Body placement
                XbimGeometryNativeApi.XbimMat3x4 placementMat = default;
                bool hasPlacement = bodyExtrusion.Position != null;
                if (hasPlacement)
                {
                    ExtrusionMeshBuilder.GetSolidAxes(bodyExtrusion.Position,
                        out double ox, out double oy, out double oz,
                        out double xx, out double xy, out double xz,
                        out double yx, out double yy, out double yz,
                        out double zx, out double zy, out double zz);
                    placementMat = new XbimGeometryNativeApi.XbimMat3x4(
                        xx, yx, zx, ox,
                        xy, yy, zy, oy,
                        xz, yz, zz, oz);
                }

                GCHandle placementPin = default;
                if (hasPlacement)
                {
                    placementPin = GCHandle.Alloc(placementMat, GCHandleType.Pinned);
                    pinHandles.Add(placementPin);
                }

                // We pass the body element placement as a separate transform on the native result.
                // The native function builds in the solid's local coordinate system; we apply
                // the element placement afterward by transforming the output mesh.
                // Actually, the native extrude function already applies the solid position
                // via the placement parameter, but we need the element placement too.
                // Instead: pass the FULL world placement to native (solid * element).
                // But wait: the native function's placement parameter is for the solid Position
                // (maps profile XY + extrusion dir to 3D). The element placement is an additional
                // outer transform. Let's keep the solid position in the native params (same as
                // xbim_manifold_mesh_extrude) and apply the element placement to the result.

                var p = new XbimGeometryNativeApi.XbimCoplanarSubtractParams
                {
                    BodyProfile = new XbimGeometryNativeApi.XbimProfileContours
                    {
                        OuterPoints = bodyOuterPin.AddrOfPinnedObject(),
                        OuterCount = bodyOuter.Length,
                        InnerPoints = bodyInnersPin.IsAllocated ? bodyInnersPin.AddrOfPinnedObject() : IntPtr.Zero,
                        InnerSizes = bodySizesPin.IsAllocated ? bodySizesPin.AddrOfPinnedObject() : IntPtr.Zero,
                        NumInners = bodyNumInners
                    },
                    OpeningProfiles = contoursPin.AddrOfPinnedObject(),
                    NumOpenings = numOpenings,
                    ExtDirX = extDirX,
                    ExtDirY = extDirY,
                    ExtDirZ = extDirZ,
                    Depth = (double)bodyExtrusion.Depth,
                    Placement = placementPin.IsAllocated ? placementPin.AddrOfPinnedObject() : IntPtr.Zero
                };

                int rc = XbimGeometryNativeApi.xbim_manifold_mesh_extrude_with_openings(in p, out var handle);
                if (rc != 0) return false;

                using (handle)
                {
                    // Apply element placement to the mesh
                    var elemPlacement = XbimPlacementTree.GetTransform(
                        element, state.PlacementTree, _engine, _logger);

                    var meshData = ManifoldMeshBooleanService.ExtractMeshData(handle);
                    if (meshData == null) return false;

                    var currentMesh = meshData.Value;
                    if (!elemPlacement.IsIdentity)
                        currentMesh = ManifoldMeshBooleanService.Transform(currentMesh, elemPlacement);

                    return WriteResultMesh(state, currentMesh, xbimTessellator, element, contextId, styleId, txn);
                }
            }
            finally
            {
                foreach (var pin in pinHandles)
                    if (pin.IsAllocated) pin.Free();
            }
        }

        private static double[] FlattenProfile((double X, double Y)[] points)
        {
            var flat = new double[points.Length * 2];
            for (int i = 0; i < points.Length; i++)
            {
                flat[i * 2] = points[i].X;
                flat[i * 2 + 1] = points[i].Y;
            }
            return flat;
        }

        private static void FlattenInners((double X, double Y)[][] inners,
            out double[] flatInners, out int[] innerSizes, out int numInners)
        {
            flatInners = null;
            innerSizes = null;
            numInners = 0;
            if (inners == null || inners.Length == 0) return;

            numInners = inners.Length;
            innerSizes = new int[numInners];
            int totalPts = 0;
            for (int h = 0; h < numInners; h++)
            {
                innerSizes[h] = inners[h].Length;
                totalPts += inners[h].Length;
            }

            flatInners = new double[totalPts * 2];
            int offset = 0;
            for (int h = 0; h < numInners; h++)
            {
                for (int i = 0; i < inners[h].Length; i++)
                {
                    flatInners[offset++] = inners[h][i].X;
                    flatInners[offset++] = inners[h][i].Y;
                }
            }
        }

        /// <summary>
        /// Attempts to perform boolean operations using Manifold mesh booleans.
        /// Returns true if successful; false means the caller should fall back to OCCT BRep booleans.
        /// </summary>
        private bool TryManifoldBooleans(StreamingState state, IIfcElement element,
            IGrouping<IIfcElement, IIfcFeatureElement> features,
            MeshingBehaviourResult behaviour,
            ManifoldMeshBooleanService manifoldService,
            XbimTessellator xbimTessellator,
            int contextId, int styleId,
            IGeometryStoreInitialiser txn)
        {
            // Try 2D profile subtraction first (fastest path for coplanar extrusions)
            if (TryCoplanarProfileSubtraction(state, element, features, behaviour,
                    xbimTessellator, contextId, styleId, txn))
                return true;

            // Collect body mesh data from cache
            var bodyMeshData = CollectBodyMeshData(state, element);
            if (bodyMeshData == null) return false;

            // Collect and transform feature mesh data
            var openingMeshes = new List<ManifoldMeshBooleanService.MeshData>();
            var projectionMeshes = new List<ManifoldMeshBooleanService.MeshData>();

            foreach (var feature in features)
            {
                // Look up the cached mesh for this feature's representation items
                var featureMesh = CollectFeatureMeshData(state, feature);
                if (featureMesh == null) return false; // missing mesh data, fall back

                // Transform feature mesh to world coordinates
                var featurePlacement = XbimPlacementTree.GetTransform(feature, state.PlacementTree, _engine, _logger);
                var transformedMesh = ManifoldMeshBooleanService.Transform(
                    featureMesh.Value, featurePlacement);

                if (feature is IIfcFeatureElementSubtraction)
                {
                    // Skip openings whose bounding box doesn't intersect the body
                    if (!BoundsIntersect(bodyMeshData.Value.Bounds, transformedMesh.Bounds))
                        continue;
                    openingMeshes.Add(transformedMesh);
                }
                else
                    projectionMeshes.Add(transformedMesh);
            }

            var currentMesh = bodyMeshData.Value;

            // Perform union with projections
            if (behaviour.HasFlag(MeshingBehaviourResult.PerformAdditions) && projectionMeshes.Count > 0)
            {
                foreach (var proj in projectionMeshes)
                {
                    ManifoldMeshBooleanService.MeshData? unionResult;
                    using (_diag.Track(DiagOp.ManifoldUnion))
                        unionResult = manifoldService.Union(currentMesh, proj);
                    if (unionResult == null) return false; // Manifold failed
                    currentMesh = unionResult.Value;
                }
            }

            // Perform subtraction of openings
            if (behaviour.HasFlag(MeshingBehaviourResult.PerformSubtractions) && openingMeshes.Count > 0)
            {
                ManifoldMeshBooleanService.MeshData? cutResult;
                using (_diag.Track(DiagOp.ManifoldCut))
                    cutResult = manifoldService.Cut(currentMesh, openingMeshes);
                if (cutResult == null) return false; // Manifold failed
                currentMesh = cutResult.Value;
            }

            return WriteResultMesh(state, currentMesh, xbimTessellator, element, contextId, styleId, txn);
        }

        private static bool BoundsIntersect(XbimRect3D a, XbimRect3D b)
        {
            return a.X < b.X + b.SizeX && a.X + a.SizeX > b.X
                && a.Y < b.Y + b.SizeY && a.Y + a.SizeY > b.Y
                && a.Z < b.Z + b.SizeZ && a.Z + a.SizeZ > b.Z;
        }

        private bool WriteResultMesh(
            StreamingState state,
            ManifoldMeshBooleanService.MeshData mesh,
            XbimTessellator xbimTessellator,
            IIfcElement element,
            int contextId, int styleId,
            IGeometryStoreInitialiser txn)
        {
            XbimShapeGeometry shapeGeometry;
            try
            {
                shapeGeometry = xbimTessellator.SerializeRawMeshToBinary(
                    mesh.Positions, mesh.Indices, element.EntityLabel);
            }
            catch
            {
                return false;
            }

            if (shapeGeometry?.ShapeData == null || shapeGeometry.ShapeData.Length == 0)
                return false;

            var bbox = shapeGeometry.BoundingBox;
            if (bbox.SizeX >= 1e100) return false;

            shapeGeometry.IfcShapeLabel = element.EntityLabel;

            var instanceTransform = XbimMatrix3D.Identity;
            if (shapeGeometry.LocalShapeDisplacement != default)
            {
                instanceTransform = XbimMatrix3D.CreateTranslation(
                    shapeGeometry.LocalShapeDisplacement.Value);
            }

            var shapeInstance = new XbimShapeInstance
            {
                IfcProductLabel = element.EntityLabel,
                ShapeGeometryLabel = 0,
                StyleLabel = styleId,
                RepresentationType = XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded,
                RepresentationContext = contextId,
                IfcTypeId = _model.Metadata.ExpressTypeId(element),
                Transformation = instanceTransform,
                BoundingBox = bbox
            };

            shapeInstance.ShapeGeometryLabel = txn.AddShapeGeometry(shapeGeometry);
            txn.AddShapeInstance(shapeInstance, shapeInstance.ShapeGeometryLabel);

            // Emit boolean result to streaming channel
            EmitGeometryAndInstance(state, shapeInstance, ((IXbimShapeGeometryData)shapeGeometry).ShapeData, bbox);

            return true;
        }

        /// <summary>
        /// Emits a geometry and instance event to the streaming channel for a boolean result.
        /// Called from both the BRep and Manifold boolean paths.
        /// </summary>
        private static void EmitGeometryAndInstance(
            StreamingState state, XbimShapeInstance inst, byte[] meshData, XbimRect3D bbox)
        {
            var ch = state.SinkChannel;
            if (ch == null) return;

            ch.TryWrite(SinkEvent.ForGeometry(inst.ShapeGeometryLabel, meshData, bbox));

            int styleKey = inst.StyleLabel > 0 ? inst.StyleLabel : -inst.IfcTypeId;
            ch.TryWrite(SinkEvent.ForInstance(new SceneInstance(
                inst.IfcProductLabel, inst.IfcTypeId,
                inst.ShapeGeometryLabel, styleKey,
                inst.Transformation, inst.BoundingBox)));
        }

        /// <summary>
        /// Collects body mesh data for a voided product from MeshBodiesCache.
        /// Resolves mapped items and applies cumulative transforms.
        /// Returns null if any body part is missing from the cache.
        /// </summary>
        private ManifoldMeshBooleanService.MeshData? CollectBodyMeshData(
            StreamingState state, IIfcProduct product)
        {
            var placement = XbimPlacementTree.GetTransform(product, state.PlacementTree, _engine, _logger);

            var reps = product.Representation?.Representations?
                .Where(r => IsInContext(r) && r.IsBodyRepresentation(BodyRepresentations));
            if (reps == null) return null;

            var leafItems = new List<(IIfcGeometricRepresentationItem item, XbimMatrix3D transform)>();
            foreach (var rep in reps)
                CollectLeafRepItems(rep.Items, placement, leafItems);

            if (leafItems.Count == 0) return null;

            ManifoldMeshBooleanService.MeshData? combined = null;
            var manifoldService = new ManifoldMeshBooleanService();

            foreach (var (item, transform) in leafItems)
            {
                if (!state.MeshBodiesCache.TryGetValue(item.EntityLabel, out var cached))
                    return null; // not in mesh cache, fall back to BRep

                var mesh = new ManifoldMeshBooleanService.MeshData(cached.Positions, cached.Indices, cached.Bounds);

                if (!transform.IsIdentity)
                    mesh = ManifoldMeshBooleanService.Transform(mesh, transform);

                if (combined == null)
                {
                    combined = mesh;
                }
                else
                {
                    var unionResult = manifoldService.Union(combined.Value, mesh);
                    if (unionResult == null) return null;
                    combined = unionResult;
                }
            }

            return combined;
        }

        /// <summary>
        /// Collects feature mesh data for a feature element from MeshFeaturesCache.
        /// Returns null if the feature is missing from the mesh cache.
        /// </summary>
        private ManifoldMeshBooleanService.MeshData? CollectFeatureMeshData(
            StreamingState state, IIfcProduct feature)
        {
            // Look up representation items for this feature
            var reps = feature.Representation?.Representations?
                .Where(r => IsInContext(r) && r.IsBodyRepresentation(BodyRepresentations));
            if (reps == null) return null;

            ManifoldMeshBooleanService.MeshData? combined = null;
            var manifoldService = new ManifoldMeshBooleanService();

            foreach (var rep in reps)
            {
                foreach (var item in rep.Items.Where(i => !(i is IIfcGeometricSet)))
                {
                    var entityLabel = item.EntityLabel;
                    if (item is IIfcMappedItem map)
                    {
                        // For mapped items, look up source items
                        foreach (var srcItem in map.MappingSource.MappedRepresentation.Items
                            .OfType<IIfcGeometricRepresentationItem>()
                            .Where(i => !(i is IIfcGeometricSet)))
                        {
                            entityLabel = srcItem.EntityLabel;
                            if (!state.MeshFeaturesCache.TryGetValue(entityLabel, out var cached))
                                return null;

                            var mesh = new ManifoldMeshBooleanService.MeshData(
                                cached.Positions, cached.Indices, cached.Bounds);

                            if (combined == null)
                                combined = mesh;
                            else
                            {
                                var u = manifoldService.Union(combined.Value, mesh);
                                if (u == null) return null;
                                combined = u;
                            }
                        }
                    }
                    else
                    {
                        if (!state.MeshFeaturesCache.TryGetValue(entityLabel, out var cached))
                            return null;

                        var mesh = new ManifoldMeshBooleanService.MeshData(
                            cached.Positions, cached.Indices, cached.Bounds);

                        if (combined == null)
                            combined = mesh;
                        else
                        {
                            var u = manifoldService.Union(combined.Value, mesh);
                            if (u == null) return null;
                            combined = u;
                        }
                    }
                }
            }

            return combined;
        }

        #endregion

        #region Body rebuild and feature cache

        /// <summary>
        /// Rebuilds a product's body BRep from IFC, transformed to world coordinates.
        /// All intermediate shapes are added to the disposeBag for cleanup.
        /// </summary>
        private IXShape BuildProductBody(StreamingState state, IIfcProduct product,
            IXGeometryEngineV6 v6engine, List<IDisposable> disposeBag)
        {
            var placement = XbimPlacementTree.GetTransform(product, state.PlacementTree, _engine, _logger);

            var reps = product.Representation?.Representations?
                .Where(r => IsInContext(r) && r.IsBodyRepresentation(BodyRepresentations));
            if (reps == null) return null;

            // Collect all leaf geometry items with their cumulative transforms
            var leafItems = new List<(IIfcGeometricRepresentationItem item, XbimMatrix3D transform)>();
            foreach (var rep in reps)
                CollectLeafRepItems(rep.Items, placement, leafItems);

            if (leafItems.Count == 0) return null;

            var builtShapes = new List<IXShape>();
            foreach (var (item, transform) in leafItems)
            {
                try
                {
                    // Try cached BRep first (saved during WriteShapeGeometries for voided products)
                    IXShape built;
                    if (state.FeaturedBodiesCache.TryRemove(item.EntityLabel, out var cached))
                        built = cached;
                    else
                        built = v6engine.Build(item);
                    if (built == null) continue;

                    if (!transform.IsIdentity)
                    {
                        var transformed = _modelServices.ShapeFactory.Transform(built, transform);
                        if (built is IDisposable bd) disposeBag.Add(bd);
                        if (transformed is IDisposable td) disposeBag.Add(td);
                        builtShapes.Add(transformed);
                    }
                    else
                    {
                        if (built is IDisposable bd) disposeBag.Add(bd);
                        builtShapes.Add(built);
                    }
                }
                catch (Exception ex)
                {
                    LogWarning(product, "Failed to rebuild body part #{0}: {1}", item.EntityLabel, ex.Message);
                }
            }

            if (builtShapes.Count == 0) return null;
            if (builtShapes.Count == 1) return builtShapes[0];

            // Compound multiple body parts via union
            var compound = _modelServices.ShapeFactory.Union(builtShapes[0], builtShapes.Skip(1));
            if (compound != null && compound is IDisposable cd)
                disposeBag.Add(cd);
            return compound ?? builtShapes[0];
        }

        /// <summary>
        /// Recursively resolves mapped items to their leaf geometry items with cumulative transforms.
        /// </summary>
        private void CollectLeafRepItems(IEnumerable<IIfcRepresentationItem> items,
            XbimMatrix3D parentTransform,
            List<(IIfcGeometricRepresentationItem item, XbimMatrix3D transform)> result)
        {
            foreach (var item in items)
            {
                if (item is IIfcMappedItem map)
                {
                    var mapTargetTransform = map.MappingTarget.ToMatrix3D();
                    var mapSourceTransform = map.MappingSource.MappingOrigin.ToMatrix3D();
                    var combined = XbimMatrix3D.Multiply(mapTargetTransform, mapSourceTransform);
                    var fullTransform = XbimMatrix3D.Multiply(combined, parentTransform);
                    CollectLeafRepItems(map.MappingSource.MappedRepresentation.Items, fullTransform, result);
                }
                else if (item is IIfcGeometricRepresentationItem geomItem && !(item is IIfcGeometricSet))
                {
                    result.Add((geomItem, parentTransform));
                }
            }
        }

        /// <summary>
        /// Gets or builds the BRep shape for a feature element (opening or projection).
        /// Feature shapes are cached because they may be shared across multiple products.
        /// </summary>
        private IXShape GetOrBuildFeatureShape(StreamingState state, IIfcProduct feature,
            IXGeometryEngineV6 v6engine)
        {
            return state.FeaturesCache.GetOrAdd(feature.EntityLabel, _ =>
            {
                // Build each of the feature's body representation items
                var reps = feature.Representation?.Representations?
                    .Where(r => IsInContext(r) && r.IsBodyRepresentation(BodyRepresentations));
                if (reps == null) return null;

                var shapes = new List<IXShape>();
                foreach (var rep in reps)
                {
                    foreach (var item in rep.Items.Where(i => !(i is IIfcGeometricSet)))
                    {
                        if (item is IIfcMappedItem map)
                        {
                            foreach (var srcItem in map.MappingSource.MappedRepresentation.Items
                                .OfType<IIfcGeometricRepresentationItem>()
                                .Where(i => !(i is IIfcGeometricSet)))
                            {
                                try
                                {
                                    var built = v6engine.Build(srcItem);
                                    if (built != null) shapes.Add(built);
                                }
                                catch (Exception ex)
                                {
                                    LogWarning(feature, "Failed to build feature part #{0}: {1}",
                                        srcItem.EntityLabel, ex.Message);
                                }
                            }
                        }
                        else if (item is IIfcGeometricRepresentationItem geomItem)
                        {
                            try
                            {
                                var built = v6engine.Build(geomItem);
                                if (built != null) shapes.Add(built);
                            }
                            catch (Exception ex)
                            {
                                LogWarning(feature, "Failed to build feature part #{0}: {1}",
                                    geomItem.EntityLabel, ex.Message);
                            }
                        }
                    }
                }

                if (shapes.Count == 0) return null;
                if (shapes.Count == 1) return shapes[0];
                // Compound multiple parts
                var compound = _modelServices.ShapeFactory.Union(shapes[0], shapes.Skip(1));
                if (compound != null)
                {
                    // Dispose individual parts since union created a new shape
                    foreach (var s in shapes) (s as IDisposable)?.Dispose();
                    return compound;
                }
                return shapes[0];
            });
        }

        #endregion

        #region Instance writing helpers

        /// <summary>
        /// Processes a product's representations and writes shape instances to the store.
        /// </summary>
        private List<XbimShapeInstance> WriteProductShape(StreamingState state, IIfcProduct product,
            bool includesOpenings, IGeometryStoreInitialiser txn)
        {
            if (CustomMeshingBehaviour != null)
            {
                double v1 = 0, v2 = 0;
                var behaviour = CustomMeshingBehaviour(product.EntityLabel, product.ExpressType.TypeId, ref v1, ref v2);
                if (behaviour == MeshingBehaviourResult.Skip)
                    return new List<XbimShapeInstance>();
            }

            if (product.Representation?.Representations == null)
                return new List<XbimShapeInstance>();

            var reps = product.Representation.Representations
                .Where(r => IsInContext(r) && r.IsBodyRepresentation(BodyRepresentations));

            var repType = includesOpenings
                ? XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded
                : XbimGeometryRepresentationType.OpeningsAndAdditionsExcluded;

            if (product is IIfcFeatureElement)
                repType = XbimGeometryRepresentationType.OpeningsAndAdditionsOnly;

            var placementTransform = XbimPlacementTree.GetTransform(product, state.PlacementTree, _engine, _logger);

            return reps.SelectMany(r =>
                WriteProductShapeRepresentationItems(state, product, txn, r, repType, placementTransform, r.Items)).ToList();
        }

        /// <summary>
        /// Writes shape instances for each representation item, resolving mapped items recursively.
        /// </summary>
        private List<XbimShapeInstance> WriteProductShapeRepresentationItems(StreamingState state,
            IIfcProduct product, IGeometryStoreInitialiser txn, IIfcRepresentation rep,
            XbimGeometryRepresentationType repType, XbimMatrix3D placementTransform,
            IItemSet<IIfcRepresentationItem> representationItems)
        {
            var shapesInstances = new List<XbimShapeInstance>();
            if (rep.ContextOfItems == null)
            {
                LogWarning(product, "No ContextOfItems for representation {0}", rep.EntityLabel);
                return shapesInstances;
            }

            var contextId = rep.ContextOfItems.EntityLabel;
            if (repType == XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded &&
                string.Compare(rep.RepresentationIdentifier, "reference", true) == 0)
            {
                repType = XbimGeometryRepresentationType.OpeningsAndAdditionsOnly;
            }

            foreach (var representationItem in representationItems)
            {
                if (representationItem is IIfcMappedItem theMap)
                {
                    var mapId = representationItem.EntityLabel;
                    if (state.MapGeometryReferences.TryGetValue(mapId, out List<GeometryReference> mapGeomIds))
                    {
                        var mapTransform = state.MapTransforms[mapId];
                        foreach (var mappedGeometryReference in mapGeomIds)
                        {
                            var trans = XbimMatrix3D.Multiply(mapTransform, placementTransform);
                            trans = ApplyShapeDisplacement(mappedGeometryReference, trans);

                            shapesInstances.Add(
                                WriteShapeInstanceToStore(mappedGeometryReference.GeometryId,
                                    mappedGeometryReference.StyleLabel, contextId, product,
                                    trans, mappedGeometryReference.BoundingBox, repType, txn, state));

                            if (!(product is IIfcOpeningElement))
                            {
                                var transformedBounds = mappedGeometryReference.BoundingBox.Transform(trans);
                                state.Clusters[rep.ContextOfItems].Enqueue(
                                    new XbimBBoxClusterElement(mappedGeometryReference.GeometryId, transformedBounds));
                            }
                        }
                    }
                    else
                    {
                        if (state.MapTransforms.TryGetValue(mapId, out var mapTransform))
                        {
                            var trans = XbimMatrix3D.Multiply(mapTransform, placementTransform);
                            shapesInstances.AddRange(
                                WriteProductShapeRepresentationItems(state, product, txn, rep, repType, trans,
                                    theMap.MappingSource.MappedRepresentation.Items));
                        }
                    }
                }
                else
                {
                    if (state.ShapeLookup.TryGetValue(representationItem.EntityLabel, out GeometryReference instance))
                    {
                        var trans = ApplyShapeDisplacement(instance, placementTransform);

                        shapesInstances.Add(
                            WriteShapeInstanceToStore(instance.GeometryId, instance.StyleLabel, contextId,
                                product, trans, instance.BoundingBox, repType, txn, state));

                        if (!(product is IIfcOpeningElement))
                        {
                            var transformedBounds = instance.BoundingBox.Transform(trans);
                            state.Clusters[rep.ContextOfItems].Enqueue(
                                new XbimBBoxClusterElement(instance.GeometryId, transformedBounds));
                        }
                    }
                }
            }
            return shapesInstances;
        }

        private XbimShapeInstance WriteShapeInstanceToStore(int shapeLabel, int styleLabel, int ctxtId,
            IIfcProduct product, XbimMatrix3D placementTransform, XbimRect3D bounds,
            XbimGeometryRepresentationType repType, IGeometryStoreInitialiser txn,
            StreamingState state = null)
        {
            var shapeInstance = new XbimShapeInstance
            {
                IfcProductLabel = product.EntityLabel,
                ShapeGeometryLabel = shapeLabel,
                StyleLabel = styleLabel,
                RepresentationType = repType,
                RepresentationContext = ctxtId,
                IfcTypeId = _model.Metadata.ExpressTypeId(product),
                Transformation = placementTransform,
                BoundingBox = bounds
            };

            try
            {
                txn.AddShapeInstance(shapeInstance, shapeLabel);
            }
            catch (Exception e)
            {
                LogError(e, _model.Instances[product.EntityLabel], "Failed to create geometry, {0}", e.Message);
            }

            // Emit to streaming channel for visible (final) instances only
            if (state?.SinkChannel != null &&
                repType == XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded)
            {
                int styleKey = styleLabel > 0 ? styleLabel : -shapeInstance.IfcTypeId;
                state.SinkChannel.TryWrite(SinkEvent.ForInstance(new SceneInstance(
                    shapeInstance.IfcProductLabel, shapeInstance.IfcTypeId,
                    shapeLabel, styleKey, placementTransform, bounds)));
            }

            return shapeInstance;
        }

        #endregion

        #region Utility helpers

        /// <summary>
        /// Meshes an IXShape into an XbimShapeGeometry using the v6 WexBimMeshFactory.
        /// </summary>
        private XbimShapeGeometry MeshShape(IXShape shape, int ifcLabel,
            double precision, double linearDeflection, double angularDeflection)
        {
            var meshData = _modelServices.WexBimMeshFactory.CreateWexBimMesh(
                shape, precision, linearDeflection, angularDeflection, 1.0, out var bounds);

            var shapeGeom = new XbimShapeGeometry
            {
                IfcShapeLabel = ifcLabel,
                Format = XbimGeometryType.PolyhedronBinary,
                LOD = XbimLOD.LOD_Unspecified,
            };

            if (meshData != null && meshData.Length > 0)
            {
                ((IXbimShapeGeometryData)shapeGeom).ShapeData = meshData;

                if (!bounds.IsVoid)
                {
                    shapeGeom.BoundingBox = new XbimRect3D(
                        bounds.CornerMin.X, bounds.CornerMin.Y, bounds.CornerMin.Z,
                        bounds.LenX, bounds.LenY, bounds.LenZ);
                }
            }

            return shapeGeom;
        }

        private bool IsInContext(IIfcRepresentation r)
        {
            if (!_contexts.Any()) return true;
            if (r.ContextOfItems == null)
            {
                if (_contexts.Count == 1)
                {
                    LogWarning(r, "Inferring default context for this representation");
                    return true;
                }
                LogWarning(r, "No Context found for this representation - skipping shape");
                return false;
            }
            return _contexts.Contains(r.ContextOfItems.EntityLabel);
        }

        private bool GetStyleId(Dictionary<int, int> surfaceStyles, int shapeId, out int styleId)
        {
            if (surfaceStyles.TryGetValue(shapeId, out styleId))
                return true;

            if (!(_model.Instances[shapeId] is IIfcBooleanResult item))
            {
                styleId = 0;
                return false;
            }

            var operands = new[] { item.FirstOperand, item.SecondOperand }.Where(o => o != null);
            var stack = new Stack<IIfcBooleanOperand>(operands);
            while (stack.Count != 0)
            {
                var o = stack.Pop();
                if (surfaceStyles.TryGetValue(o.EntityLabel, out styleId))
                    return true;

                item = o as IIfcBooleanResult;
                if (item == null) continue;
                if (item.FirstOperand != null) stack.Push(item.FirstOperand);
                if (item.SecondOperand != null) stack.Push(item.SecondOperand);
            }

            styleId = 0;
            return false;
        }

        private static XbimMatrix3D ApplyShapeDisplacement(GeometryReference shape, XbimMatrix3D transformation)
        {
            if (!shape.LocalShapeDisplacement.HasValue)
                return transformation;
            var translation = XbimMatrix3D.CreateTranslation(shape.LocalShapeDisplacement.Value);
            return XbimMatrix3D.Multiply(translation, transformation);
        }

        private int WriteRegionsToStore(IIfcRepresentationContext context,
            IEnumerable<XbimBBoxClusterElement> elementsToCluster,
            IGeometryStoreInitialiser txn, XbimMatrix3D worldCoordinateSystem, int nextRegionNumber)
        {
            var metre = _model.ModelFactors.OneMetre;
            var regions = new XbimRegionCollection();
            var v = XbimDbscan.GetClusters(elementsToCluster, 5 * metre);
            regions.AddRange(v.Select(item =>
                new XbimRegion("Region " + nextRegionNumber++, item.Bound, item.GeometryIds.Count, worldCoordinateSystem)));
            regions.ContextLabel = context.EntityLabel;
            txn.AddRegions(regions);
            return nextRegionNumber;
        }

        #endregion

        #region Pre-processing helpers

        private void GetOpeningsAndProjections(StreamingState state)
        {
            var compoundElements = XbimMultiValueDictionary<IIfcObjectDefinition, IIfcObjectDefinition>
                .Create<HashSet<IIfcObjectDefinition>>();

            foreach (var aggRel in _model.Instances.OfType<IIfcRelAggregates>())
            {
                if (aggRel.RelatingObject is null)
                {
                    LogWarning(aggRel, "Invalid null value for RelatingObject");
                    continue;
                }
                foreach (var relObj in aggRel.RelatedObjects)
                    compoundElements.Add(aggRel.RelatingObject, relObj);
            }

            var elementsWithFeatures = new List<(IIfcElement Element, IIfcFeatureElement Feature)>();

            // Openings
            var openingRelations = _model.Instances.OfType<IIfcRelVoidsElement>()
                .Where(r =>
                    r.RelatingBuildingElement?.Representation != null &&
                    r.RelatedOpeningElement?.Representation != null).ToList();

            foreach (var rel in openingRelations)
            {
                if (compoundElements.TryGetValue(rel.RelatingBuildingElement,
                    out ICollection<IIfcObjectDefinition> children))
                {
                    foreach (var child in children.OfType<IIfcElement>())
                        elementsWithFeatures.Add((child, rel.RelatedOpeningElement));
                }
                elementsWithFeatures.Add((rel.RelatingBuildingElement, rel.RelatedOpeningElement));
            }

            // Projections
            var projectingRelations = _model.Instances.OfType<IIfcRelProjectsElement>()
                .Where(r =>
                    r.RelatingElement?.Representation != null &&
                    r.RelatedFeatureElement?.Representation != null).ToList();

            foreach (var rel in projectingRelations)
            {
                if (compoundElements.TryGetValue(rel.RelatingElement,
                    out ICollection<IIfcObjectDefinition> children))
                {
                    foreach (var child in children.OfType<IIfcElement>())
                        elementsWithFeatures.Add((child, rel.RelatedFeatureElement));
                }
                elementsWithFeatures.Add((rel.RelatingElement, rel.RelatedFeatureElement));
            }

            state.OpeningsAndProjections = elementsWithFeatures
                .GroupBy(x => x.Element, y => y.Feature).ToList();
        }

        private void GetProductShapeIds(StreamingState state)
        {
            state.MappedShapeIds = new HashSet<int>();
            state.FeatureElementShapeIds = new HashSet<int>();
            state.ProductShapeIds = new HashSet<int>();

            var products = new List<IIfcProduct>();
            foreach (var item in _model.Instances.OfType<IIfcProduct>())
            {
                try
                {
                    var _ = item.Representation;
                    products.Add(item);
                }
                catch (Exception)
                {
                    LogWarning(item, "Exception thrown getting representation for product");
                }
            }

            foreach (var product in products)
            {
                if (CustomMeshingBehaviour != null)
                {
                    double v1 = 0, v2 = 0;
                    var behaviour = CustomMeshingBehaviour(product.EntityLabel, product.ExpressType.TypeId, ref v1, ref v2);
                    if (behaviour == MeshingBehaviourResult.Skip)
                        continue;
                }

                var isFeatureElement = product is IIfcFeatureElement;
                var isVoidedProduct = state.VoidedProductIds.Contains(product.EntityLabel);

                if (product.Representation?.Representations == null)
                    continue;

                foreach (var rep in product.Representation.Representations
                    .Where(r => IsInContext(r) && r.IsBodyRepresentation(BodyRepresentations)))
                {
                    foreach (var shape in rep.Items.Where(i => !(i is IIfcGeometricSet)))
                    {
                        if (shape is IIfcMappedItem mappedItem)
                        {
                            ProcessMappedItem(state, isFeatureElement, isVoidedProduct, mappedItem);
                        }
                        else
                        {
                            state.ProductShapeIds.Add(shape.EntityLabel);
                            if (isFeatureElement) state.FeatureElementShapeIds.Add(shape.EntityLabel);
                            if (isVoidedProduct) state.VoidedShapeIds.Add(shape.EntityLabel);
                        }
                    }
                }
            }
        }

        private void ProcessMappedItem(StreamingState state, bool isFeatureElement, bool isVoidedProduct,
            IIfcMappedItem mappedItem)
        {
            state.MappedShapeIds.Add(mappedItem.EntityLabel);
            foreach (var item in mappedItem.MappingSource.MappedRepresentation.Items)
            {
                if (item is IIfcMappedItem nested)
                    ProcessMappedItem(state, isFeatureElement, isVoidedProduct, nested);
                else if (item != null && !(item is IIfcGeometricSet))
                {
                    state.ProductShapeIds.Add(item.EntityLabel);
                    if (isFeatureElement) state.FeatureElementShapeIds.Add(item.EntityLabel);
                    if (isVoidedProduct) state.VoidedShapeIds.Add(item.EntityLabel);
                }
            }
        }

        private void GetSurfaceStyles(StreamingState state)
        {
            var styledItemsGroup = _model.Instances.OfType<IIfcStyledItem>()
                .Where(s => s.Item != null)
                .GroupBy(s => s.Item.EntityLabel);
            state.SurfaceStyles = new Dictionary<int, int>();
            foreach (var group in styledItemsGroup)
            {
                var val = group.SelectMany(st => st.Styles.SelectMany(s => s.SurfaceStyles)).FirstOrDefault();
                if (val != null)
                    state.SurfaceStyles.Add(group.Key, val.EntityLabel);
            }
        }

        private void GetClusters(StreamingState state)
        {
            state.Clusters = new Dictionary<IIfcRepresentationContext, ConcurrentQueue<XbimBBoxClusterElement>>();
            foreach (var context in _contexts)
                state.Clusters.Add(context, new ConcurrentQueue<XbimBBoxClusterElement>());
        }

        #endregion

        #region Read/Query API

        /// <summary>
        /// Returns all shape instances in this context.
        /// </summary>
        public IEnumerable<XbimShapeInstance> ShapeInstances()
        {
            using (var reader = _model.GeometryStore.BeginRead())
            {
                foreach (var shapeInstance in reader.ShapeInstances)
                    yield return shapeInstance;
            }
        }

        /// <summary>
        /// Returns all shape geometries in this context.
        /// </summary>
        public IEnumerable<XbimShapeGeometry> ShapeGeometries()
        {
            using (var reader = _model.GeometryStore.BeginRead())
            {
                foreach (var shapeGeometry in reader.ShapeGeometries)
                    yield return shapeGeometry;
            }
        }

        /// <summary>
        /// Returns a single shape geometry by label.
        /// </summary>
        public XbimShapeGeometry ShapeGeometry(int shapeGeometryLabel)
        {
            using (var reader = _model.GeometryStore.BeginRead())
                return reader.ShapeGeometry(shapeGeometryLabel);
        }

        /// <summary>
        /// Returns the shape geometry for a shape instance.
        /// </summary>
        public XbimShapeGeometry ShapeGeometry(XbimShapeInstance shapeInstance)
        {
            return ShapeGeometry(shapeInstance.ShapeGeometryLabel);
        }

        /// <summary>
        /// Returns all shape instances that reference the given geometry.
        /// </summary>
        public IEnumerable<XbimShapeInstance> ShapeInstancesOf(XbimShapeGeometry geometry, bool ignoreFeatures = false)
        {
            using (var reader = _model.GeometryStore.BeginRead())
            {
                foreach (var context in _contexts)
                {
                    foreach (var shapeInstance in reader.ShapeInstancesOfGeometry(geometry.ShapeLabel))
                    {
                        if (MatchesShapeRequirements(shapeInstance, context, ignoreFeatures))
                            yield return shapeInstance;
                    }
                }
            }
        }

        /// <summary>
        /// Returns all shape instances that reference the given geometry label.
        /// </summary>
        public IEnumerable<XbimShapeInstance> ShapeInstancesOf(int geometryLabel, bool ignoreFeatures = false)
        {
            using (var reader = _model.GeometryStore.BeginRead())
            {
                foreach (var context in _contexts)
                {
                    foreach (var shapeInstance in reader.ShapeInstancesOfGeometry(geometryLabel))
                    {
                        if (MatchesShapeRequirements(shapeInstance, context, ignoreFeatures))
                            yield return shapeInstance;
                    }
                }
            }
        }

        /// <summary>
        /// Returns all shape instances for a product.
        /// </summary>
        public IEnumerable<XbimShapeInstance> ShapeInstancesOf(IIfcProduct product)
        {
            using (var reader = _model.GeometryStore.BeginRead())
            {
                foreach (var context in _contexts)
                {
                    foreach (var shapeInstance in reader.ShapeInstancesOfEntity(product))
                    {
                        if (context.EntityLabel == shapeInstance.RepresentationContext)
                            yield return shapeInstance;
                    }
                }
            }
        }

        /// <summary>
        /// Returns a triangulated mesh for the given shape geometry label.
        /// </summary>
        public IXbimMeshGeometry3D ShapeGeometryMeshOf(int shapeGeometryLabel)
        {
            var sg = ShapeGeometry(shapeGeometryLabel);
            var mg = new XbimMeshGeometry3D();
            mg.Read(sg.ShapeData);
            return mg;
        }

        /// <summary>
        /// Returns a triangulated mesh for the given shape geometry.
        /// </summary>
        public IXbimMeshGeometry3D ShapeGeometryMeshOf(XbimShapeGeometry shapeGeometry)
        {
            var mg = new XbimMeshGeometry3D();
            mg.Read(shapeGeometry.ShapeData);
            return mg;
        }

        /// <summary>
        /// Returns a triangulated mesh for the given shape instance with transforms applied.
        /// </summary>
        public IXbimMeshGeometry3D ShapeGeometryMeshOf(XbimShapeInstance shapeInstance)
        {
            var sg = ShapeGeometry(shapeInstance.ShapeGeometryLabel);
            var mg = new XbimMeshGeometry3D();
            mg.Add(sg.ShapeData, shapeInstance.IfcTypeId, shapeInstance.IfcProductLabel,
                shapeInstance.InstanceLabel, shapeInstance.Transformation, (short)_model.UserDefinedId);
            return mg;
        }

        /// <summary>
        /// Returns all spatial regions in this context.
        /// </summary>
        public IEnumerable<XbimRegion> GetRegions()
        {
            var contextIds = _contexts.Select(c => c.EntityLabel).ToList();
            using (var reader = _model.GeometryStore.BeginRead())
            {
                foreach (var regions in reader.ContextRegions)
                {
                    if (contextIds.Contains(regions.ContextLabel))
                    {
                        foreach (var region in regions)
                            yield return region;
                    }
                }
            }
        }

        /// <summary>
        /// Returns the most populated spatial region.
        /// </summary>
        public XbimRegion GetLargestRegion()
        {
            var regions = new XbimRegionCollection();
            foreach (var region in GetRegions())
                regions.Add(region);
            return regions.MostPopulated();
        }

        private bool MatchesShapeRequirements(IXbimShapeInstanceData shapeInstance,
            IIfcRepresentationContext context, bool ignoreFeatures)
        {
            return context.EntityLabel == shapeInstance.RepresentationContext &&
                (
                    (typeof(IIfcFeatureElement).IsAssignableFrom(_model.Metadata.GetType(shapeInstance.IfcTypeId)) &&
                        shapeInstance.RepresentationType == (byte)XbimGeometryRepresentationType.OpeningsAndAdditionsOnly)
                    ||
                    (
                        (ignoreFeatures && shapeInstance.RepresentationType == (byte)XbimGeometryRepresentationType.OpeningsAndAdditionsExcluded)
                        ||
                        shapeInstance.RepresentationType == (byte)XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded
                    )
                );
        }

        #endregion
    }
}
