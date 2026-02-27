using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Primitives;
using Xbim.Geometry.Engine.Rules;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.Geometry.Engine.Factories
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
                    0, // params already in OCCT space
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
        internal List<XbimCurve> BuildCompositeCurveSegments3d(IIfcCompositeCurve ifcComposite, bool verifyConnectivity = true)
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

                    XbimCurve segCurve;
                    try
                    {
                        segCurve = Build3d(segment.ParentCurve);
                    }
                    catch (IfcRuleViolationException ex)
                    {
                        _logger.LogWarning(
                            "IIfcCompositeCurve #{Label}: skipping segment #{SegLabel} ({Rule}).",
                            ifcComposite.EntityLabel, segment.EntityLabel, ex.Message);
                        continue;
                    }

                    bool shouldReverse = !segment.SameSense;

                    // Verify connectivity: if reversing per SameSense creates a larger gap
                    // than not reversing, the sense flag is wrong — override it.
                    if (segmentCurves.Count > 0 && verifyConnectivity)
                    {
                        var prevCurve = segmentCurves[segmentCurves.Count - 1];
                        XbimGeometryNativeApi.xbim_curve_parameters(prevCurve.Handle, out _, out double prevLast);
                        XbimGeometryNativeApi.xbim_curve_value(prevCurve.Handle, prevLast,
                            out double pex, out double pey, out double pez);

                        XbimGeometryNativeApi.xbim_curve_parameters(segCurve.Handle, out double curFirst, out double curLast);
                        XbimGeometryNativeApi.xbim_curve_value(segCurve.Handle, curFirst,
                            out double csx, out double csy, out double csz);
                        XbimGeometryNativeApi.xbim_curve_value(segCurve.Handle, curLast,
                            out double cex, out double cey, out double cez);

                        double gapForward = Math.Sqrt((csx - pex) * (csx - pex) + (csy - pey) * (csy - pey) + (csz - pez) * (csz - pez));
                        double gapReversed = Math.Sqrt((cex - pex) * (cex - pex) + (cey - pey) * (cey - pey) + (cez - pez) * (cez - pez));

                        if (shouldReverse && gapReversed > gapForward)
                        {
                            _logger.LogWarning(
                                "IIfcCompositeCurve #{Label}: segment #{SegLabel} has incorrect SameSense, ignoring.",
                                ifcComposite.EntityLabel, segment.EntityLabel);
                            shouldReverse = false;
                        }
                        else if (!shouldReverse && gapForward > gapReversed)
                        {
                            _logger.LogWarning(
                                "IIfcCompositeCurve #{Label}: segment #{SegLabel} has incorrect SameSense, reversing.",
                                ifcComposite.EntityLabel, segment.EntityLabel);
                            shouldReverse = true;
                        }
                    }

                    if (shouldReverse)
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
        /// Pre-marshalled composite curve data for split timing benchmarks.
        /// </summary>
        internal struct CompositeCurveMarshalledData
        {
            public int[] Types;
            public int[] SameSense;
            public double[] Data;
            public int[] Offsets;
        }

        /// <summary>
        /// Marshals an IFC composite curve into flat arrays without calling the native builder.
        /// Used by benchmarks to measure marshalling and native call timings separately.
        /// </summary>
        internal CompositeCurveMarshalledData MarshalCompositeCurve(IIfcCompositeCurve ifcComposite)
        {
            CurveRules.Validate(ifcComposite);

            var types = new List<int>();
            var sameSense = new List<int>();
            var dataList = new List<double>();
            var offsets = new List<int>();
            var prebuiltHandles = new List<XbimCurve>();

            try
            {
                int lastLabel = -1;
                foreach (var segment in ifcComposite.Segments)
                {
                    if (segment.EntityLabel == lastLabel) continue;
                    lastLabel = segment.EntityLabel;

                    if (segment is IIfcReparametrisedCompositeCurveSegment reparam
                        && (double)reparam.ParamLength != 1.0)
                        continue;

                    if (segment.ParentCurve == null) continue;
                    int sense = segment.SameSense ? 1 : 0;
                    MarshalSegment(segment.ParentCurve, sense,
                        types, sameSense, offsets, dataList, prebuiltHandles);
                }
                offsets.Add(dataList.Count);
            }
            finally
            {
                foreach (var c in prebuiltHandles) c.Dispose();
            }

            return new CompositeCurveMarshalledData
            {
                Types = types.ToArray(),
                SameSense = sameSense.ToArray(),
                Data = dataList.ToArray(),
                Offsets = offsets.ToArray(),
            };
        }

        /// <summary>
        /// Calls only the native composite curve builder with pre-marshalled arrays.
        /// Used by benchmarks to measure native-only timing.
        /// </summary>
        internal NativeCurveHandle BuildCompositeFromArrays(CompositeCurveMarshalledData d)
        {
            int result = XbimGeometryNativeApi.xbim_curve_build_composite(
                ContextHandle,
                d.Types.Length,
                d.Types,
                d.SameSense,
                d.Data,
                d.Offsets,
                null, 0,
                out var compositeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build composite curve: {XbimGeometryNativeApi.GetLastError()}");

            return compositeHandle;
        }

        /// <summary>
        /// Builds a composite B-spline from an IFC composite curve in a single native call.
        /// Marshals segment geometry (lines, trimmed circles) into flat arrays and calls
        /// <c>xbim_curve_build_composite</c>. Falls back to pre-built handles for segment
        /// types that cannot be expressed as flat data (B-splines, nested composites, etc.).
        /// </summary>
        internal XbimBoundedCurve3d BuildCompositeCurveBatch3d(IIfcCompositeCurve ifcComposite)
        {
            CurveRules.Validate(ifcComposite);

            var types = new List<int>();
            var sameSense = new List<int>();
            var dataList = new List<double>();
            var offsets = new List<int>();
            var prebuiltHandles = new List<XbimCurve>();

            try
            {
                int lastLabel = -1;
                foreach (var segment in ifcComposite.Segments)
                {
                    if (segment.EntityLabel == lastLabel)
                    {
                        _logger.LogInformation(
                            "IIfcCompositeCurve #{Label}: skipping duplicate segment #{SegLabel} (ArchiCAD bug).",
                            ifcComposite.EntityLabel, segment.EntityLabel);
                        continue;
                    }
                    lastLabel = segment.EntityLabel;

                    if (segment is IIfcReparametrisedCompositeCurveSegment reparam
                        && (double)reparam.ParamLength != 1.0)
                        throw new XbimGeometryServiceException(
                            $"IIfcReparametrisedCompositeCurveSegment #{segment.EntityLabel} is currently unsupported (ParamLength != 1).");

                    if (segment.ParentCurve == null)
                        continue;

                    int sense = segment.SameSense ? 1 : 0;

                    try
                    {
                        MarshalSegment(segment.ParentCurve, sense,
                            types, sameSense, offsets, dataList, prebuiltHandles);
                    }
                    catch (IfcRuleViolationException ex)
                    {
                        _logger.LogWarning(
                            "IIfcCompositeCurve #{Label}: skipping segment #{SegLabel} ({Rule}).",
                            ifcComposite.EntityLabel, segment.EntityLabel, ex.Message);
                        continue;
                    }
                }
                offsets.Add(dataList.Count); // sentinel

                if (types.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments.");

                IntPtr[]? prebuiltPtrs = null;
                NativeHandleArray nativePrebuilt = default;
                try
                {
                    if (prebuiltHandles.Count > 0)
                    {
                        nativePrebuilt = new NativeHandleArray(
                            prebuiltHandles.Select(c => c.Handle).ToArray());
                        prebuiltPtrs = nativePrebuilt.Ptrs;
                    }

                    int result = XbimGeometryNativeApi.xbim_curve_build_composite(
                        ContextHandle,
                        types.Count,
                        types.ToArray(),
                        sameSense.ToArray(),
                        dataList.ToArray(),
                        offsets.ToArray(),
                        prebuiltPtrs,
                        prebuiltHandles.Count,
                        out var compositeHandle);

                    if (result != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return new XbimBoundedCurve3d(compositeHandle, XCurveType.IfcCompositeCurve);
                }
                finally
                {
                    if (prebuiltHandles.Count > 0)
                        nativePrebuilt.Dispose();
                }
            }
            finally
            {
                foreach (var c in prebuiltHandles)
                    c.Dispose();
            }
        }

        /// <summary>
        /// Classifies and marshals a composite curve segment into the batch arrays.
        /// Trimmed lines, polylines, and trimmed circles are marshalled as flat data;
        /// everything else falls back to a pre-built native handle.
        /// Multi-point polylines emit one LINE entry per consecutive point pair.
        /// </summary>
        private void MarshalSegment(
            IIfcCurve parentCurve, int sense,
            List<int> types, List<int> sameSenseList,
            List<int> offsets, List<double> data,
            List<XbimCurve> prebuiltHandles)
        {
            // Trimmed line → single LINE
            if (parentCurve is IIfcTrimmedCurve trimmed && trimmed.BasisCurve is IIfcLine ifcLine)
            {
                bool trimSense = trimmed.SenseAgreement;
                ExtractTrimParameters(trimmed, false,
                    out double u1, out double u2, ref trimSense,
                    out bool useCartesian, out var cp1, out var cp2);

                double sx, sy, sz, ex, ey, ez;
                bool canMarshal = true;
                if (useCartesian && cp1 != null && cp2 != null)
                {
                    var p1 = GeometryFactory.BuildPoint3d(cp1);
                    var p2 = GeometryFactory.BuildPoint3d(cp2);
                    sx = p1.X; sy = p1.Y; sz = p1.Z;
                    ex = p2.X; ey = p2.Y; ez = p2.Z;
                }
                else if (GeometryFactory.BuildDirection3d(ifcLine.Dir.Orientation,
                             out double dx, out double dy, out double dz))
                {
                    var origin = GeometryFactory.BuildPoint3d(ifcLine.Pnt);
                    sx = origin.X + u1 * dx; sy = origin.Y + u1 * dy; sz = origin.Z + u1 * dz;
                    ex = origin.X + u2 * dx; ey = origin.Y + u2 * dy; ez = origin.Z + u2 * dz;
                }
                else
                {
                    sx = sy = sz = ex = ey = ez = 0;
                    canMarshal = false;
                }

                if (canMarshal)
                {
                    if (!trimmed.SenseAgreement)
                    {
                        (sx, ex) = (ex, sx);
                        (sy, ey) = (ey, sy);
                        (sz, ez) = (ez, sz);
                    }

                    offsets.Add(data.Count);
                    sameSenseList.Add(sense);
                    types.Add(XbimGeometryNativeApi.CSegLine);
                    data.AddRange(new[] { sx, sy, sz, ex, ey, ez });
                    return;
                }
                // Fall through to HANDLE if direction is degenerate
            }

            // Polyline → single POLYLINE segment (native builds local B-spline)
            if (parentCurve is IIfcPolyline polyline && polyline.Points.Count >= 2)
            {
                var (px, py, pz) = ExtractPolylinePoints3d(polyline);
                offsets.Add(data.Count);
                sameSenseList.Add(sense);
                types.Add(XbimGeometryNativeApi.CSegPolyline);
                for (int i = 0; i < px.Length; i++)
                {
                    data.Add(px[i]);
                    data.Add(py[i]);
                    data.Add(pz[i]);
                }
                return;
            }

            // Trimmed circle → CIRCLE_TRIM
            if (MarshalCircleArc(parentCurve, sense, types, sameSenseList, offsets, data))
                return;

            // Fallback: build individually via P/Invoke
            offsets.Add(data.Count);
            sameSenseList.Add(sense);
            types.Add(XbimGeometryNativeApi.CSegHandle);
            var curve = Build3d(parentCurve);
            prebuiltHandles.Add(curve);
        }

        /// <summary>
        /// Tries to marshal a trimmed circle arc as flat placement + trim data.
        /// Returns true and appends 13 doubles on success.
        /// </summary>
        private bool MarshalCircleArc(
            IIfcCurve parentCurve, int segSense,
            List<int> types, List<int> sameSenseList,
            List<int> offsets, List<double> data)
        {
            if (!(parentCurve is IIfcTrimmedCurve trimmed && trimmed.BasisCurve is IIfcCircle circle))
                return false;

            if (circle.Radius <= 0)
                return false;

            GeometryFactory.BuildAxis2PlacementAs3d(circle.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            bool trimSense = trimmed.SenseAgreement;
            ExtractTrimParameters(trimmed, true,
                out double u1, out double u2, ref trimSense,
                out bool useCartesian, out var cp1, out var cp2);

            if (useCartesian && cp1 != null && cp2 != null)
            {
                u1 = ProjectPointOnCircle(cp1, ox, oy, oz, zx, zy, zz, xx, xy, xz);
                u2 = ProjectPointOnCircle(cp2, ox, oy, oz, zx, zy, zz, xx, xy, xz);
                HandleEqualTrimParams(trimmed, true, ref u1, ref u2, ref trimSense);
            }

            offsets.Add(data.Count);
            sameSenseList.Add(segSense);
            types.Add(XbimGeometryNativeApi.CSegCircleTrim);
            data.AddRange(new double[]
            {
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                (double)circle.Radius,
                u1, u2,
                trimSense ? 1.0 : 0.0
            });
            return true;
        }

        /// <summary>
        /// Projects a Cartesian point onto a circle defined by its axis placement,
        /// returning the OCCT parameter (angle in radians from the reference direction).
        /// Replicates the gp_Ax2 orthogonalization of the reference direction.
        /// </summary>
        private static double ProjectPointOnCircle(
            IIfcCartesianPoint cp,
            double ox, double oy, double oz,
            double zx, double zy, double zz,
            double xx, double xy, double xz)
        {
            var pt = GeometryFactory.BuildPoint3d(cp);

            // Orthogonalize refDir against axis (same as gp_Ax2 constructor)
            double dot = xx * zx + xy * zy + xz * zz;
            double rxo = xx - dot * zx;
            double ryo = xy - dot * zy;
            double rzo = xz - dot * zz;
            double rmag = Math.Sqrt(rxo * rxo + ryo * ryo + rzo * rzo);
            if (rmag < 1e-15)
            {
                rxo = 1; ryo = 0; rzo = 0;
                rmag = 1;
            }
            rxo /= rmag; ryo /= rmag; rzo /= rmag;

            // Y direction = axis × orthogonalized refDir
            double yx = zy * rzo - zz * ryo;
            double yy = zz * rxo - zx * rzo;
            double yz = zx * ryo - zy * rxo;

            // Project point into circle's local frame
            double dx = pt.X - ox;
            double dy = pt.Y - oy;
            double dz = pt.Z - oz;
            double localX = dx * rxo + dy * ryo + dz * rzo;
            double localY = dx * yx + dy * yy + dz * yz;

            double angle = Math.Atan2(localY, localX);
            if (angle < 0) angle += 2.0 * Math.PI;
            return angle;
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
                    return new XbimGradientCurve(NativeCurveHandle.Borrowed(owningHandle), ContextHandle);
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
