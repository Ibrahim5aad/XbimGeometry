using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds curve geometry from IFC curve entities. Routes curves to 2D or 3D
    /// builders based on their dimensionality, and handles caching for expensive
    /// gradient and segmented reference curves.
    /// </summary>
    internal partial class CurveFactory : IXCurveFactory, IDisposable
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        // Caches owning handles for expensive-to-build gradient and segmented reference curves.
        // Keyed by IFC entity label. Callers receive borrowed (non-owning) Curve wrappers.
        private readonly Dictionary<int, (NativeCurveHandle Handle, XCurveType CurveType)> _curveCache = new();

        public CurveFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        /// <summary>
        /// Main entry point: builds a curve from an IFC curve entity.
        /// Gradient and segmented reference curves are always 3D.
        /// Other curves are dispatched to 2D or 3D builders based on <c>curve.Dim</c>.
        /// </summary>
        public IXCurve Build(IIfcCurve curve)
        {
            // Gradient and segmented reference curves are always 3D
            if (curve is IfcSegmentedReferenceCurve ifcSegRef)
                return GetOrBuildCached(ifcSegRef.EntityLabel, () => BuildSegmentedReferenceCurve(ifcSegRef));

            if (curve is IfcGradientCurve ifcGradient)
                return GetOrBuildCached(ifcGradient.EntityLabel, () => BuildGradientCurve(ifcGradient));

            // Route to 2D or 3D builder based on dimensionality
            if ((int)curve.Dim == 2)
                return BuildCurve2d(curve);

            return BuildCurve3d(curve);
        }

        public IXCurve BuildDirectrix(IIfcCurve curve, double? startParam, double? endParam)
        {
            // Build the full curve, trimming will be handled at the sweep level
            return Build(curve);
        }

        #region Cache

        /// <summary>
        /// Returns a cached curve if available, otherwise builds it, stores the owning handle
        /// in the cache, and returns a borrowed (non-owning) wrapper to the caller.
        /// </summary>
        private Curve GetOrBuildCached(int entityLabel, Func<Curve> builder)
        {
            if (_curveCache.TryGetValue(entityLabel, out var entry))
                return new Curve(NativeCurveHandle.Borrowed(entry.Handle), entry.CurveType);

            var built = builder();
            var curveType = built.CurveType;
            var owningHandle = built.DetachHandle();
            _curveCache[entityLabel] = (owningHandle, curveType);
            return new Curve(NativeCurveHandle.Borrowed(owningHandle), curveType);
        }

        public void Dispose()
        {
            foreach (var entry in _curveCache.Values)
                entry.Handle.Dispose();
            _curveCache.Clear();
        }

        #endregion
    }
}
