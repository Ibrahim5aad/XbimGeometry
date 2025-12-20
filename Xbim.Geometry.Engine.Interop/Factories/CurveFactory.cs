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
        private readonly object _cacheLock = new();
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
            if ((int)curve.Dim != 3)
                throw new InvalidOperationException(
                    "Directrix must be a 3D curve.");

            var builtCurve = (Curve)BuildCurve3d(curve);

            // If no trimming requested, return the full curve
            if (!startParam.HasValue && !endParam.HasValue)
                return builtCurve;

            // Resolve missing params to the curve's natural parameter range
            double u1 = startParam ?? builtCurve.FirstParameter;
            double u2 = endParam ?? builtCurve.LastParameter;

            // Skip trimming if the params span the full range
            if (Math.Abs(u1 - builtCurve.FirstParameter) < _modelService.Precision &&
                Math.Abs(u2 - builtCurve.LastParameter) < _modelService.Precision)
                return builtCurve;

            // Trim the curve
            try
            {
                int result = XbimGeometryNativeApi.xbim_curve_build_trimmed_3d(
                    ContextHandle,
                    builtCurve.Handle,
                    u1, u2,
                    1, // sense agreement
                    out var trimmedHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to trim directrix curve #{curve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Curve(trimmedHandle, builtCurve.CurveType);
            }
            finally
            {
                builtCurve.Dispose();
            }
        }

        #region Helpers

        /// <summary>
        /// Returns whether an IFC curve is bounded. Unbounded curves (lines, pcurves,
        /// surface curves) are not valid as composite curve segments.
        /// </summary>
        private static bool IsBoundedCurve(IIfcCurve curve)
        {
            if (curve is IIfcLine) return false;
            if (curve is IIfcOffsetCurve3D oc3d) return IsBoundedCurve(oc3d.BasisCurve);
            if (curve is IIfcOffsetCurve2D oc2d) return IsBoundedCurve(oc2d.BasisCurve);
            if (curve is IIfcPcurve) return false;
            if (curve is IIfcSurfaceCurve) return false;
            return true;
        }

        #endregion

        #region Cache

        /// <summary>
        /// Returns a cached curve if available, otherwise builds it, stores the owning handle
        /// in the cache, and returns a borrowed (non-owning) wrapper to the caller.
        /// </summary>
        private Curve GetOrBuildCached(int entityLabel, Func<Curve> builder)
        {
            lock (_cacheLock)
            {
                if (_curveCache.TryGetValue(entityLabel, out var entry))
                    return new Curve(NativeCurveHandle.Borrowed(entry.Handle), entry.CurveType);

                var built = builder();
                var curveType = built.CurveType;
                var owningHandle = built.DetachHandle();
                _curveCache[entityLabel] = (owningHandle, curveType);
                if (curveType is XCurveType.IfcGradientCurve)
                    return new GradientCurve(NativeCurveHandle.Borrowed(owningHandle), ContextHandle, _modelService.Precision);
                else
                    return new Curve(NativeCurveHandle.Borrowed(owningHandle), curveType);
            }
        }

        public void Dispose()
        {
            lock (_cacheLock)
            {
                foreach (var entry in _curveCache.Values)
                    entry.Handle.Dispose();
                _curveCache.Clear();
            }
        }

        #endregion
    }
}
