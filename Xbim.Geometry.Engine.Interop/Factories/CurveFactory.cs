using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Rules;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Exceptions;
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

        // Caches owning handles for gradient and segmented reference curves.
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
            if (curve.Dim == 2)
                return BuildCurve2d(curve);

            return BuildCurve3d(curve);
        }

        /// <summary>
        /// Builds a 3D curve regardless of the IFC-declared dimensionality.
        /// </summary>
        public XbimCurve Build3d(IIfcCurve curve) => (XbimCurve)BuildCurve3d(curve);

        public IXCurve BuildDirectrix(IIfcCurve curve, double? startParam, double? endParam)
        {
            if ((int)curve.Dim != 3)
                throw new XbimGeometryServiceException(
                    "Directrix must be a 3D curve.");

            var builtCurve = (XbimCurve)BuildCurve3d(curve);

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
                    throw new XbimGeometryServiceException(
                        $"Failed to trim directrix curve #{curve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimCurve(trimmedHandle, builtCurve.CurveType);
            }
            finally
            {
                builtCurve.Dispose();
            }
        }

        #region Trim Parameter Helpers

        /// <summary>
        /// Parses Trim1/Trim2 selects from an IFC trimmed curve and resolves parametric values.
        /// When Cartesian trim points should be used (either preferred or because parametric values
        /// are missing), sets <paramref name="useCartesian"/> to true and returns the points in
        /// <paramref name="cp1"/> and <paramref name="cp2"/> for the caller to project onto the
        /// basis curve. Otherwise, applies radian factor (for conics) or line magnitude scaling
        /// (for lines) to the parametric values and handles the equal-parameter case.
        /// </summary>
        internal void ExtractTrimParameters(
            IIfcTrimmedCurve ifcTrimmed,
            bool isConic,
            out double u1,
            out double u2,
            ref bool sense,
            out bool useCartesian,
            out IIfcCartesianPoint? cp1,
            out IIfcCartesianPoint? cp2)
        {
            bool preferCartesian = ifcTrimmed.MasterRepresentation == Ifc4.Interfaces.IfcTrimmingPreference.CARTESIAN;

            // Parse trim selects
            u1 = double.NegativeInfinity;
            u2 = double.PositiveInfinity;
            cp1 = null;
            cp2 = null;

            foreach (var trim in ifcTrimmed.Trim1)
            {
                if (trim is IIfcCartesianPoint pt)
                    cp1 = pt;
                else if (trim is Xbim.Ifc4.MeasureResource.IfcParameterValue pv)
                    u1 = (double)pv;
            }

            foreach (var trim in ifcTrimmed.Trim2)
            {
                if (trim is IIfcCartesianPoint pt)
                    cp2 = pt;
                else if (trim is Xbim.Ifc4.MeasureResource.IfcParameterValue pv)
                    u2 = (double)pv;
            }

            // Determine whether to use Cartesian projection
            if ((preferCartesian && cp1 != null && cp2 != null) ||
                (cp1 != null && cp2 != null &&
                 (double.IsNegativeInfinity(u1) || double.IsPositiveInfinity(u2))))
            {
                useCartesian = true;
                return;
            }

            useCartesian = false;

            if (double.IsNegativeInfinity(u1) || double.IsPositiveInfinity(u2))
            {
                throw new XbimGeometryServiceException(
                    $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: TrimValuesConsistent — " +
                    "either a single value is specified for Trim, or the two trimming values are of different type.");
            }

            // Apply parametric scaling
            if (isConic)
            {
                u1 *= _modelService.RadianFactor;
                u2 *= _modelService.RadianFactor;
            }
            else if (ifcTrimmed.BasisCurve is IIfcLine ifcBaseLine)
            {
                u1 *= ifcBaseLine.Dir.Magnitude;
                u2 *= ifcBaseLine.Dir.Magnitude;
            }

            // Handle equal parameters
            HandleEqualTrimParams(ifcTrimmed, isConic, ref u1, ref u2, ref sense);
        }

        /// <summary>
        /// Handles the equal-parameter case after Cartesian trim point projection. When both
        /// parameters are within precision of each other, conics produce a full circle (u1=0,
        /// u2=2*PI, sense=true) and non-conics throw.
        /// </summary>
        internal void HandleEqualTrimParams(
            IIfcTrimmedCurve ifcTrimmed,
            bool isConic,
            ref double u1,
            ref double u2,
            ref bool sense)
        {
            if (Math.Abs(u1 - u2) < _modelService.Precision)
            {
                if (isConic)
                {
                    u1 = 0.0;
                    u2 = Math.PI * 2.0;
                    sense = true;
                }
                else
                {
                    _logger.LogInformation("IIfcTrimmedCurve #{Label}: parametric trim points are equal on non-conic — empty curve.",
                        ifcTrimmed.BasisCurve.EntityLabel);
                    throw new XbimGeometryServiceException(
                        $"IIfcTrimmedCurve #{ifcTrimmed.EntityLabel}: trim parameters are equal on a non-conic basis, resulting in an empty curve.");
                }
            }
        }

        #endregion

        #region Composite Curve Segment Helpers

        /// <summary>
        /// Iterates composite curve segments, applying ArchiCAD duplicate-skip and bounded-curve
        /// validation, builds each segment as a 3D curve, and reverses if !SameSense.
        /// The caller is responsible for disposing the returned curve wrappers.
        /// </summary>
        internal List<XbimCurve> BuildCompositeCurveSegments3d(IIfcCompositeCurve ifcComposite)
        {
            CurveRules.Validate(ifcComposite);

            var segmentCurves = new List<XbimCurve>();
            try
            {
                int lastLabel = -1;
                foreach (var segment in ifcComposite.Segments)
                {
                    // ArchiCAD bug workaround: skip consecutive duplicate segments (same EntityLabel)
                    if (segment.EntityLabel == lastLabel)
                    {
                        _logger.LogInformation(
                            "IIfcCompositeCurve #{Label}: skipping duplicate segment #{SegLabel} (ArchiCAD bug).",
                            ifcComposite.EntityLabel, segment.EntityLabel);
                        continue;
                    }
                    lastLabel = segment.EntityLabel;

                    // Reparametrised segments with non-unit ParamLength are unsupported
                    if (segment is IIfcReparametrisedCompositeCurveSegment reparam
                        && (double)reparam.ParamLength != 1.0)
                        throw new XbimGeometryServiceException(
                            $"IIfcReparametrisedCompositeCurveSegment #{segment.EntityLabel} is currently unsupported (ParamLength != 1).");

                    if (segment.ParentCurve == null)
                        continue;

                    var segCurve = (XbimCurve)Build(segment.ParentCurve);

                    if (!segment.SameSense)
                    {
                        int reverseResult = XbimGeometryNativeApi.xbim_curve_reverse(segCurve.Handle);
                        if (reverseResult != 0)
                            throw new XbimGeometryServiceException(
                                $"Failed to reverse composite curve segment: {XbimGeometryNativeApi.GetLastError()}");
                    }

                    segmentCurves.Add(segCurve);
                }
            }
            catch
            {
                foreach (var c in segmentCurves)
                    c.Dispose();
                throw;
            }

            return segmentCurves;
        }

        /// <summary>
        /// Iterates composite curve segments, applying ArchiCAD duplicate-skip and bounded-curve
        /// validation, builds each segment as a 2D curve, and reverses if !SameSense.
        /// The caller is responsible for disposing the returned curve wrappers.
        /// </summary>
        internal List<XbimCurve2d> BuildCompositeCurveSegments2d(IIfcCompositeCurve ifcComposite)
        {
            CurveRules.Validate(ifcComposite);

            var segmentCurves = new List<XbimCurve2d>();
            try
            {
                int lastLabel = -1;
                foreach (var segment in ifcComposite.Segments)
                {
                    // ArchiCAD bug workaround: skip consecutive duplicate segments (same EntityLabel)
                    if (segment.EntityLabel == lastLabel)
                    {
                        _logger.LogInformation(
                            "IIfcCompositeCurve #{Label}: skipping duplicate segment #{SegLabel} (ArchiCAD bug).",
                            ifcComposite.EntityLabel, segment.EntityLabel);
                        continue;
                    }
                    lastLabel = segment.EntityLabel;

                    // Reparametrised segments with non-unit ParamLength are unsupported
                    if (segment is IIfcReparametrisedCompositeCurveSegment reparam
                        && (double)reparam.ParamLength != 1.0)
                        throw new XbimGeometryServiceException(
                            $"IIfcReparametrisedCompositeCurveSegment #{segment.EntityLabel} is currently unsupported (ParamLength != 1).");

                    if (segment.ParentCurve == null)
                        continue;

                    var segCurve = (XbimCurve2d)BuildCurve2d(segment.ParentCurve);

                    if (!segment.SameSense)
                    {
                        int reverseResult = XbimGeometryNativeApi.xbim_curve2d_reverse(segCurve.Handle);
                        if (reverseResult != 0)
                            throw new XbimGeometryServiceException(
                                $"Failed to reverse 2D composite curve segment: {XbimGeometryNativeApi.GetLastError()}");
                    }

                    segmentCurves.Add(segCurve);
                }
            }
            catch
            {
                foreach (var c in segmentCurves)
                    c.Dispose();
                throw;
            }

            return segmentCurves;
        }

        #endregion

        #region Point Extraction Helpers

        /// <summary>
        /// Extracts 2D coordinates from a polyline into separate X and Y arrays.
        /// </summary>
        internal static (double[] x, double[] y) ExtractPolylinePoints2d(IIfcPolyline polyline)
        {
            var ifcPoints = polyline.Points;
            int count = ifcPoints.Count;
            var x = new double[count];
            var y = new double[count];

            for (int i = 0; i < count; i++)
            {
                var coords = ifcPoints[i].Coordinates;
                x[i] = coords[0];
                y[i] = coords[1];
            }

            return (x, y);
        }

        /// <summary>
        /// Extracts 3D coordinates from a polyline into separate X, Y, and Z arrays.
        /// Points with Dim=2 are promoted to 3D with Z=0.
        /// </summary>
        internal static (double[] x, double[] y, double[] z) ExtractPolylinePoints3d(IIfcPolyline polyline)
        {
            var ifcPoints = polyline.Points;
            int count = ifcPoints.Count;
            var x = new double[count];
            var y = new double[count];
            var z = new double[count];

            for (int i = 0; i < count; i++)
            {
                var cp = ifcPoints[i];
                x[i] = cp.Coordinates[0];
                y[i] = cp.Coordinates[1];
                z[i] = (int)cp.Dim == 3 ? (double)cp.Coordinates[2] : 0.0;
            }

            return (x, y, z);
        }

        /// <summary>
        /// Extracts 2D point coordinates from an indexed poly curve's point list.
        /// </summary>
        internal static List<(double x, double y)> ExtractIndexedPoints2d(IIfcIndexedPolyCurve ifcIndexed)
        {
            var coordList = ifcIndexed.Points;

            if (coordList is IIfcCartesianPointList2D pointList2D)
            {
                var points = new List<(double, double)>(pointList2D.CoordList.Count);
                foreach (var coords in pointList2D.CoordList)
                    points.Add((coords[0], coords[1]));
                return points;
            }

            throw new NotSupportedException(
                $"2D IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel} requires IIfcCartesianPointList2D, " +
                $"but found {coordList?.GetType().Name ?? "null"}.");
        }

        /// <summary>
        /// Extracts 3D point coordinates from an indexed poly curve's point list.
        /// Handles both IIfcCartesianPointList3D (native 3D) and IIfcCartesianPointList2D (Z=0).
        /// </summary>
        internal static List<(double x, double y, double z)> ExtractIndexedPoints3d(IIfcIndexedPolyCurve ifcIndexed)
        {
            var coordList = ifcIndexed.Points;

            if (coordList is IIfcCartesianPointList3D pointList3D)
            {
                var points = new List<(double, double, double)>(pointList3D.CoordList.Count);
                foreach (var coords in pointList3D.CoordList)
                    points.Add((coords[0], coords[1], coords.Count > 2 ? coords[2] : 0.0));
                return points;
            }

            if (coordList is IIfcCartesianPointList2D pointList2D)
            {
                var points = new List<(double, double, double)>(pointList2D.CoordList.Count);
                foreach (var coords in pointList2D.CoordList)
                    points.Add((coords[0], coords[1], 0.0));
                return points;
            }

            throw new NotSupportedException(
                $"Unsupported point list type in IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}.");
        }

        #endregion

        #region Cache

        /// <summary>
        /// Returns a cached curve if available, otherwise builds it, stores the owning handle
        /// in the cache, and returns a borrowed (non-owning) wrapper to the caller.
        /// </summary>
        private XbimCurve GetOrBuildCached(int entityLabel, Func<XbimCurve> builder)
        {
            lock (_cacheLock)
            {
                if (_curveCache.TryGetValue(entityLabel, out var entry))
                    return new XbimCurve(NativeCurveHandle.Borrowed(entry.Handle), entry.CurveType);

                var built = builder();
                var curveType = built.CurveType;
                var owningHandle = built.DetachHandle();
                _curveCache[entityLabel] = (owningHandle, curveType);
                if (curveType is XCurveType.IfcGradientCurve)
                    return new XbimGradientCurve(NativeCurveHandle.Borrowed(owningHandle), ContextHandle, _modelService.Precision);
                else
                    return new XbimCurve(NativeCurveHandle.Borrowed(owningHandle), curveType);
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
