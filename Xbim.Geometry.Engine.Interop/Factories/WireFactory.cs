using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;
using Xbim.Geometry.Engine.Interop.Rules;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds wire shapes from IFC curve entities, profile definitions, and point arrays.
    /// Wires represent connected sequences of edges forming open or closed paths.
    /// </summary>
    internal class WireFactory : IXWireFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public WireFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXWire BuildWire(IXPoint[] points)
        {
            if (points == null || points.Length < 2)
                throw new ArgumentException("Wire requires at least 2 points.", nameof(points));

            var pointsXYZ = new double[points.Length * 3];
            for (int i = 0; i < points.Length; i++)
            {
                pointsXYZ[i * 3 + 0] = points[i].X;
                pointsXYZ[i * 3 + 1] = points[i].Y;
                pointsXYZ[i * 3 + 2] = points[i].Z;
            }

            int result = XbimGeometryNativeApi.xbim_wire_build_polyline(
                ContextHandle,
                pointsXYZ, points.Length,
                _modelService.Precision,
                out var wireHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build wire from {points.Length} points: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimWire(wireHandle);
        }

        public IXWire Build(IIfcCurve ifcCurve)
        {
            if (ifcCurve is IIfcPolyline ifcPolyline)
                return BuildFromPolyline(ifcPolyline);

            if (ifcCurve is IIfcIndexedPolyCurve ifcIndexed)
                return BuildFromIndexedPolyCurve(ifcIndexed);

            if (ifcCurve is IIfcCompositeCurve ifcComposite)
                return BuildFromCompositeCurve(ifcComposite);

            if (ifcCurve is IIfcTrimmedCurve ifcTrimmed)
                return BuildFromTrimmedCurve(ifcTrimmed);

            if (ifcCurve is IIfcLine ifcLine)
                return BuildFromLine(ifcLine);

            if (ifcCurve is IIfcCircle ifcCircle)
                return BuildFromCircle(ifcCircle);

            if (ifcCurve is IIfcEllipse ifcEllipse)
                return BuildFromEllipse(ifcEllipse);

            if (ifcCurve is IIfcBSplineCurveWithKnots ifcBSpline)
                return BuildFromBSpline(ifcBSpline);

            if (ifcCurve is IIfcOffsetCurve2D || ifcCurve is IIfcOffsetCurve3D)
                return BuildFromOffsetCurve(ifcCurve);

            throw new NotSupportedException(
                $"Wire from curve type {ifcCurve.ExpressType.ExpressName} #{ifcCurve.EntityLabel} is not yet supported.");
        }

        public IXWire Build(IIfcProfileDef ifcProfileDef)
            => ((ProfileFactory)_modelService.ProfileFactory).BuildWire(ifcProfileDef);

        #region Polyline

        private IXWire BuildFromPolyline(IIfcPolyline ifcPolyline)
        {
            var points = ifcPolyline.Points;
            if (points.Count < 2)
                throw new XbimGeometryServiceException(
                    $"IIfcPolyline #{ifcPolyline.EntityLabel} has fewer than 2 points.");

            var (x, y, z) = CurveFactory.ExtractPolylinePoints3d(ifcPolyline);
            var pointsXYZ = new double[x.Length * 3];
            for (int i = 0; i < x.Length; i++)
            {
                pointsXYZ[i * 3 + 0] = x[i];
                pointsXYZ[i * 3 + 1] = y[i];
                pointsXYZ[i * 3 + 2] = z[i];
            }

            int result = XbimGeometryNativeApi.xbim_wire_build_polyline(
                ContextHandle,
                pointsXYZ, x.Length,
                _modelService.Precision,
                out var wireHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build wire from polyline #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimWire(wireHandle);
        }

        #endregion

        #region IndexedPolyCurve

        private IXWire BuildFromIndexedPolyCurve(IIfcIndexedPolyCurve ifcIndexed)
        {
            List<double> allPointsXYZ = ExtractPointCoordinatesFlat(ifcIndexed);

            // If no segments specified, build a polyline through all points in order
            if (ifcIndexed.Segments == null || !ifcIndexed.Segments.Any())
            {
                int numPoints = allPointsXYZ.Count / 3;
                int result = XbimGeometryNativeApi.xbim_wire_build_polyline(
                    ContextHandle,
                    allPointsXYZ.ToArray(), numPoints,
                    _modelService.Precision,
                    out var wireHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimWire(wireHandle);
            }

            // Build individual edges from segments.
            // Segments are IIfcSegmentIndexSelect — concrete types are
            // IfcLineIndex (IList<long>) and IfcArcIndex (IList<long>) from Xbim.Ifc4.GeometryResource.
            // We handle them generically via IList<long> casting.
            var edgeHandles = new List<NativeShapeHandle>();
            try
            {
                foreach (var segment in ifcIndexed.Segments)
                {
                    var valueList = (System.Collections.IList)segment.Value;
                    var indices = new long[valueList.Count];
                    for (int i = 0; i < valueList.Count; i++)
                        indices[i] = (IfcPositiveInteger)valueList[i]!;

                    if (indices.Length == 3)
                    {
                        // Arc segment (3 indices: start, mid, end) — build circular arc through all 3 points
                        int startIdx = (int)(indices[0] - 1);
                        int midIdx = (int)(indices[1] - 1);
                        int endIdx = (int)(indices[2] - 1);

                        int r = XbimGeometryNativeApi.xbim_edge_build_circle_arc_3pt(
                            ContextHandle,
                            allPointsXYZ[startIdx * 3], allPointsXYZ[startIdx * 3 + 1], allPointsXYZ[startIdx * 3 + 2],
                            allPointsXYZ[midIdx * 3], allPointsXYZ[midIdx * 3 + 1], allPointsXYZ[midIdx * 3 + 2],
                            allPointsXYZ[endIdx * 3], allPointsXYZ[endIdx * 3 + 1], allPointsXYZ[endIdx * 3 + 2],
                            out var edgeHandle);

                        if (r != 0)
                        {
                            // Collinear points — fall back to a straight line from start to end
                            _logger.LogInformation("Arc segment in IndexedPolyCurve #{EntityLabel} has collinear points, using line fallback",
                                ifcIndexed.EntityLabel);
                            r = XbimGeometryNativeApi.xbim_edge_build_line(
                                ContextHandle,
                                allPointsXYZ[startIdx * 3], allPointsXYZ[startIdx * 3 + 1], allPointsXYZ[startIdx * 3 + 2],
                                allPointsXYZ[endIdx * 3], allPointsXYZ[endIdx * 3 + 1], allPointsXYZ[endIdx * 3 + 2],
                                out edgeHandle);
                            if (r != 0)
                                throw new XbimGeometryServiceException(
                                    $"Failed to build fallback line edge in indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");
                        }

                        edgeHandles.Add(edgeHandle);
                    }
                    else
                    {
                        // Line segment (N indices forming a polyline)
                        var segPointsXYZ = new double[indices.Length * 3];
                        for (int i = 0; i < indices.Length; i++)
                        {
                            int idx = (int)(indices[i] - 1); // 1-based to 0-based
                            segPointsXYZ[i * 3 + 0] = allPointsXYZ[idx * 3 + 0];
                            segPointsXYZ[i * 3 + 1] = allPointsXYZ[idx * 3 + 1];
                            segPointsXYZ[i * 3 + 2] = allPointsXYZ[idx * 3 + 2];
                        }

                        for (int i = 0; i < indices.Length - 1; i++)
                        {
                            int r = XbimGeometryNativeApi.xbim_edge_build_line(
                                ContextHandle,
                                segPointsXYZ[i * 3], segPointsXYZ[i * 3 + 1], segPointsXYZ[i * 3 + 2],
                                segPointsXYZ[(i + 1) * 3], segPointsXYZ[(i + 1) * 3 + 1], segPointsXYZ[(i + 1) * 3 + 2],
                                out var edgeHandle);
                            if (r != 0)
                                throw new XbimGeometryServiceException(
                                    $"Failed to build line edge in indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");
                            edgeHandles.Add(edgeHandle);
                        }
                    }
                }

                // Build wire from edges
                using var nativeEdges = new NativeHandleArray(edgeHandles.ToArray());
                int buildResult = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                    ContextHandle,
                    nativeEdges.Ptrs, nativeEdges.Length,
                    out var resultWireHandle);

                if (buildResult != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build wire from edges in indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimWire(resultWireHandle);
            }
            finally
            {
                foreach (var h in edgeHandles)
                    h.Dispose();
            }
        }

        #endregion

        #region Composite Curve

        private IXWire BuildFromCompositeCurve(IIfcCompositeCurve ifcComposite)
        {
            var curveFactory = (CurveFactory)_modelService.CurveFactory;
            var segmentCurves = curveFactory.BuildCompositeCurveSegments3d(ifcComposite);
            try
            {
                if (segmentCurves.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments.");

                // Build wire from curves with shared vertex connectivity
                using var nativeCurves = new NativeHandleArray(
                    segmentCurves.Select(c => c.Handle).ToArray());
                int buildResult = XbimGeometryNativeApi.xbim_wire_build_from_curves(
                    ContextHandle,
                    nativeCurves.Ptrs, nativeCurves.Length,
                    _modelService.Precision,
                    _modelService.MinimumGap,
                    out var wireHandle);

                if (buildResult != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build wire from composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimWire(wireHandle);
            }
            finally
            {
                foreach (var c in segmentCurves)
                    c.Dispose();
            }
        }

        #endregion

        #region Single-Curve Wires

        private IXWire WrapEdgeAsWire(NativeShapeHandle edgeHandle, string curveDescription)
        {
            using var nativeEdges = new NativeHandleArray(new[] { edgeHandle });
            int result = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                ContextHandle,
                nativeEdges.Ptrs, 1,
                out var wireHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build wire from {curveDescription}: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimWire(wireHandle);
        }

        private IXWire BuildFromTrimmedCurve(IIfcTrimmedCurve ifcTrimmed)
        {
            var builtCurve = _modelService.CurveFactory.Build(ifcTrimmed);

            if (builtCurve.Is3d)
            {
                var curve3d = (XbimCurve)builtCurve;
                try
                {
                    var startPt = curve3d.GetPoint(curve3d.FirstParameter);
                    var endPt = curve3d.GetPoint(curve3d.LastParameter);

                    int r = XbimGeometryNativeApi.xbim_edge_build_from_curve_handle(
                        ContextHandle,
                        curve3d.Handle,
                        startPt.X, startPt.Y, startPt.Z,
                        endPt.X, endPt.Y, endPt.Z,
                        1, _modelService.Precision,
                        out var edgeHandle);

                    if (r != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build edge from trimmed curve #{ifcTrimmed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return WrapEdgeAsWire(edgeHandle, $"trimmed curve #{ifcTrimmed.EntityLabel}");
                }
                finally
                {
                    curve3d.Dispose();
                }
            }
            else
            {
                var curve2d = (XbimCurve2d)builtCurve;
                NativeCurve2dHandle curveHandle = null;
                try
                {
                    curveHandle = curve2d.DetachHandle();
                    using var curveArray = new NativeHandleArray(new SafeHandle[] { curveHandle });
                    int r = XbimGeometryNativeApi.xbim_wire_build_from_2d_curves(
                        ContextHandle,
                        curveArray.Ptrs, 1,
                        _modelService.Precision,
                        _modelService.MinimumGap,
                        out var wireHandle);

                    if (r != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build wire from 2D trimmed curve #{ifcTrimmed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return new XbimWire(wireHandle);
                }
                finally
                {
                    curveHandle?.Dispose();
                }
            }
        }

        private IXWire BuildFromLine(IIfcLine ifcLine)
        {
            var edge = (XbimEdge)_modelService.EdgeFactory.Build(ifcLine);
            return WrapEdgeAsWire(edge.Handle, $"line #{ifcLine.EntityLabel}");
        }

        private IXWire BuildFromCircle(IIfcCircle ifcCircle)
        {
            var edge = (XbimEdge)_modelService.EdgeFactory.Build(ifcCircle);
            return WrapEdgeAsWire(edge.Handle, $"circle #{ifcCircle.EntityLabel}");
        }

        private IXWire BuildFromEllipse(IIfcEllipse ifcEllipse)
        {
            var edge = (XbimEdge)_modelService.EdgeFactory.Build(ifcEllipse);
            return WrapEdgeAsWire(edge.Handle, $"ellipse #{ifcEllipse.EntityLabel}");
        }

        private IXWire BuildFromBSpline(IIfcBSplineCurveWithKnots ifcBSpline)
        {
            var edge = (XbimEdge)_modelService.EdgeFactory.Build(ifcBSpline);
            return WrapEdgeAsWire(edge.Handle, $"B-spline #{ifcBSpline.EntityLabel}");
        }

        private IXWire BuildFromOffsetCurve(IIfcCurve ifcCurve)
        {
            var builtCurve = _modelService.CurveFactory.Build(ifcCurve);

            if (builtCurve.Is3d)
            {
                var curve3d = (XbimCurve)builtCurve;
                try
                {
                    var startPt = curve3d.GetPoint(curve3d.FirstParameter);
                    var endPt = curve3d.GetPoint(curve3d.LastParameter);

                    int r = XbimGeometryNativeApi.xbim_edge_build_from_curve_handle(
                        ContextHandle,
                        curve3d.Handle,
                        startPt.X, startPt.Y, startPt.Z,
                        endPt.X, endPt.Y, endPt.Z,
                        1, _modelService.Precision,
                        out var edgeHandle);

                    if (r != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build edge from offset curve #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return WrapEdgeAsWire(edgeHandle, $"offset curve #{ifcCurve.EntityLabel}");
                }
                finally
                {
                    curve3d.Dispose();
                }
            }
            else
            {
                var curve2d = (XbimCurve2d)builtCurve;
                NativeCurve2dHandle curveHandle = null;
                try
                {
                    curveHandle = curve2d.DetachHandle();
                    using var curveArray = new NativeHandleArray(new SafeHandle[] { curveHandle });
                    int r = XbimGeometryNativeApi.xbim_wire_build_from_2d_curves(
                        ContextHandle,
                        curveArray.Ptrs, 1,
                        _modelService.Precision,
                        _modelService.MinimumGap,
                        out var wireHandle);

                    if (r != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build wire from 2D offset curve #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return new XbimWire(wireHandle);
                }
                finally
                {
                    curveHandle?.Dispose();
                }
            }
        }

        #endregion

        #region Directrix

        /// <summary>
        /// Builds a wire from a curve with optional parametric trimming, suitable for
        /// use as a sweep directrix.
        /// </summary>
        internal IXWire BuildDirectrixWire(IIfcCurve ifcCurve, double? startParam, double? endParam)
        {
            double start = startParam ?? double.NaN;
            double end = endParam ?? double.NaN;

            // Workaround: some authoring tools set polyline trim to (0, 1) meaning "entire line".
            // IFC polyline parameterization is arc-length based, so endParam=1 would trim to
            // just 1 model unit — always an authoring error for any real directrix.
            if (ifcCurve is IIfcPolyline &&
                !double.IsNaN(start) && Math.Abs(start) < _modelService.Precision &&
                !double.IsNaN(end) && Math.Abs(end - 1.0) < _modelService.Precision)
            {
                _logger.LogDebug("Polyline trim (0:1) does not comply with schema, expanding to entire length");
                start = double.NaN;
                end = double.NaN;
            }

            // Composite and indexed poly curves need IFC-to-arclength parameterization mapping
            if (ifcCurve is IIfcCompositeCurve ifcCompositeDirectrix)
                return BuildDirectrixCompositeCurve(ifcCompositeDirectrix, start, end);

            if (ifcCurve is IIfcIndexedPolyCurve ifcIndexedDirectrix)
                return BuildDirectrixIndexedPolyCurve(ifcIndexedDirectrix, start, end);

            // Unbounded curves (IIfcLine) can't be built into a wire directly.
            // Build the trimmed curve via CurveFactory, then wrap as wire.
            if (ifcCurve is IIfcLine)
                return BuildDirectrixFromCurveFactory(ifcCurve, start, end);

            // Build full wire from the bounded curve
            var wire = (XbimWire)Build(ifcCurve);

            // If no trimming needed, return the wire as-is
            if (double.IsNaN(start) && double.IsNaN(end))
                return wire;

            try
            {
                // trim
                int result = XbimGeometryNativeApi.xbim_wire_build_trimmed(
                    ContextHandle,
                    wire.Handle,
                    double.IsNaN(start) ? 0.0 : start,
                    double.IsNaN(end) ? double.MaxValue : end,
                    1, // sameSense
                    _modelService.Precision,
                    _modelService.RadianFactor,
                    out var trimmedHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to trim directrix wire for #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimWire(trimmedHandle);
            }
            finally
            {
                wire.Dispose();
            }
        }

        /// <summary>
        /// Builds a directrix wire by first constructing the trimmed curve geometry
        /// via CurveFactory, then wrapping the result as an edge and wire.
        /// Used for unbounded curves that can't be directly built into a wire.
        /// </summary>
        private IXWire BuildDirectrixFromCurveFactory(IIfcCurve ifcCurve, double start, double end)
        {
            double? sp = double.IsNaN(start) ? null : start;
            double? ep = double.IsNaN(end) ? null : end;
            var builtCurve = _modelService.CurveFactory.BuildDirectrix(ifcCurve, sp, ep);

            if (builtCurve.Is3d)
            {
                var curve3d = (XbimCurve)builtCurve;
                try
                {
                    var startPt = curve3d.GetPoint(curve3d.FirstParameter);
                    var endPt = curve3d.GetPoint(curve3d.LastParameter);

                    int r = XbimGeometryNativeApi.xbim_edge_build_from_curve_handle(
                        ContextHandle,
                        curve3d.Handle,
                        startPt.X, startPt.Y, startPt.Z,
                        endPt.X, endPt.Y, endPt.Z,
                        1, _modelService.Precision,
                        out var edgeHandle);

                    if (r != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build edge from directrix curve #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return WrapEdgeAsWire(edgeHandle, $"directrix #{ifcCurve.EntityLabel}");
                }
                finally
                {
                    curve3d.Dispose();
                }
            }
            else
            {
                var curve2d = (XbimCurve2d)builtCurve;
                NativeCurve2dHandle curveHandle = null;
                try
                {
                    curveHandle = curve2d.DetachHandle();
                    using var curveArray = new NativeHandleArray(new SafeHandle[] { curveHandle });
                    int r = XbimGeometryNativeApi.xbim_wire_build_from_2d_curves(
                        ContextHandle,
                        curveArray.Ptrs, 1,
                        _modelService.Precision,
                        _modelService.MinimumGap,
                        out var wireHandle);

                    if (r != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build wire from 2D directrix curve #{ifcCurve.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return new XbimWire(wireHandle);
                }
                finally
                {
                    curveHandle?.Dispose();
                }
            }
        }

        /// <summary>
        /// Builds a directrix wire from a composite curve with IFC-to-arclength parameterization
        /// mapping for trimming. Each segment's parameterized length depends on its type:
        /// IIfcLine=1, IIfcTrimmedCurve=Trim2-Trim1, IIfcPolyline=Points.Count-1,
        /// IIfcIndexedPolyCurve=sum of sub-segment params (arcs=angular span, lines=1).
        /// </summary>
        private IXWire BuildDirectrixCompositeCurve(IIfcCompositeCurve ifcComposite, double startParam, double endParam)
        {
            double startPar = double.IsNaN(startParam) ? 0.0 : startParam;
            double endPar = double.IsNaN(endParam) ? double.PositiveInfinity : endParam;

            double occStart = 0.0;
            double occEnd = 0.0;
            double totCurveLen = 0.0;
            double firstParameterizedLength = 0.0;
            int segIndex = 0;

            var segmentCurves = new List<XbimCurve>();
            try
            {
                int lastLabel = -1;
                foreach (var segment in ifcComposite.Segments)
                {
                    // Skip remaining segments if both params are consumed
                    if (startPar <= 0 && endPar <= 0)
                        continue;

                    // ArchiCAD bug workaround: skip consecutive duplicate segments (same EntityLabel)
                    if (segment.EntityLabel == lastLabel)
                    {
                        _logger.LogInformation(
                            "Composite directrix #{Label}: skipping duplicate segment #{SegLabel} (ArchiCAD bug).",
                            ifcComposite.EntityLabel, segment.EntityLabel);
                        continue;
                    }
                    lastLabel = segment.EntityLabel;

                    // Reject reparameterised segments with non-unit param length
                    if (segment is IIfcReparametrisedCompositeCurveSegment reparam &&
                        (double)reparam.ParamLength != 1.0)
                    {
                        throw new NotSupportedException(
                            $"IIfcReparametrisedCompositeCurveSegment #{segment.EntityLabel} with ParamLength != 1 is not supported.");
                    }

                    var parentCurve = segment.ParentCurve;
                    if (parentCurve == null) continue;

                    // Composite curve segments must be bounded curves
                    if (!CurveRules.IsBoundedCurve(parentCurve))
                        throw new XbimGeometryServiceException(
                            "Composite curve is invalid, only curve segments that are bounded curves are permitted.");

                    // Determine this segment's parameterized length based on its type
                    double segParamLength;
                    if (parentCurve is IIfcPolyline polyline)
                        segParamLength = polyline.Points.Count - 1;
                    else if (parentCurve is IIfcIndexedPolyCurve indexedPoly)
                        segParamLength = GetIndexedPolyCurveParameterizedLength(indexedPoly);
                    else
                        segParamLength = GetSegmentParameterizedLength(segment);

                    if (segIndex == 0)
                        firstParameterizedLength = segParamLength;

                    // Build the segment curve and apply SameSense reversal
                    var curveFactory = (CurveFactory)_modelService.CurveFactory;
                    var segCurve = curveFactory.Build3d(parentCurve);

                    if (!segment.SameSense)
                    {
                        int reverseResult = XbimGeometryNativeApi.xbim_curve_reverse(segCurve.Handle);
                        if (reverseResult != 0)
                            throw new XbimGeometryServiceException(
                                $"Failed to reverse composite directrix segment #{segment.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");
                    }

                    segmentCurves.Add(segCurve);
                    double geoLength = segCurve.Length;
                    totCurveLen += geoLength;

                    // Map IFC parameterization to arc-length offsets
                    if (startPar > 0)
                    {
                        double ratio = Math.Min(startPar / segParamLength, 1.0);
                        startPar -= ratio * segParamLength;
                        occStart += ratio * geoLength;
                    }

                    if (endPar > 0)
                    {
                        // Special case: startParam=0, endParam=1 and fits within first segment
                        // means "entire curve" in some authoring tools
                        if (endPar <= firstParameterizedLength &&
                            !double.IsNaN(endParam) && Math.Abs(endParam - 1.0) < 1e-10 &&
                            !double.IsNaN(startParam) && Math.Abs(startParam) < 1e-10)
                        {
                            occEnd += geoLength;
                        }
                        else
                        {
                            double ratio = Math.Min(endPar / segParamLength, 1.0);
                            endPar -= ratio * segParamLength;
                            occEnd += ratio * geoLength;
                        }
                    }

                    segIndex++;
                }

                if (segmentCurves.Count == 0)
                    throw new XbimGeometryServiceException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments for directrix.");

                // Build wire from curves with shared vertex connectivity
                using var nativeCurves = new NativeHandleArray(
                    segmentCurves.Select(c => c.Handle).ToArray());
                int buildResult = XbimGeometryNativeApi.xbim_wire_build_from_curves(
                    ContextHandle,
                    nativeCurves.Ptrs, nativeCurves.Length,
                    _modelService.Precision,
                    _modelService.MinimumGap,
                    out var wireHandle);

                if (buildResult != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build directrix wire from composite curve #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                var wire = new XbimWire(wireHandle);

                // If no trimming needed (offsets span the entire curve), return as-is
                if (Math.Abs(occStart) < _modelService.Precision &&
                    Math.Abs(occEnd - totCurveLen) < _modelService.Precision)
                    return wire;

                // Trim by arc-length
                try
                {
                    int trimResult = XbimGeometryNativeApi.xbim_wire_build_trimmed_by_length(
                        ContextHandle,
                        wire.Handle,
                        occStart, occEnd,
                        _modelService.Precision,
                        out var trimmedHandle);

                    if (trimResult != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to trim composite directrix #{ifcComposite.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                    return new XbimWire(trimmedHandle);
                }
                finally
                {
                    wire.Dispose();
                }
            }
            finally
            {
                foreach (var c in segmentCurves)
                    c.Dispose();
            }
        }

        /// <summary>
        /// Builds a directrix wire from an indexed poly curve with IFC-to-arclength
        /// parameterization mapping for trimming.
        /// </summary>
        private IXWire BuildDirectrixIndexedPolyCurve(IIfcIndexedPolyCurve ifcIndexed, double startParam, double endParam)
        {
            // Build the full wire
            var wire = (XbimWire)BuildFromIndexedPolyCurve(ifcIndexed);

            // If no trimming, return as-is
            if (double.IsNaN(startParam) && double.IsNaN(endParam))
                return wire;

            double startPar = double.IsNaN(startParam) ? 0.0 : startParam;
            double endPar = double.IsNaN(endParam) ? double.PositiveInfinity : endParam;

            double totalGeoLength = wire.Length;
            double occStart = 0;
            double occEnd = 0;

            if (ifcIndexed.Segments != null && ifcIndexed.Segments.Any())
            {
                // Extract point coordinates for per-segment geometric length computation
                List<double> allPointsXYZ = ExtractPointCoordinatesFlat(ifcIndexed);

                // Walk segments, consuming startPar/endPar per-segment like legacy code
                foreach (var segment in ifcIndexed.Segments)
                {
                    if (startPar <= 0 && endPar <= 0)
                        continue;

                    var valueList = (System.Collections.IList)segment.Value;
                    var indices = new long[valueList.Count];
                    for (int k = 0; k < valueList.Count; k++)
                        indices[k] = (IfcPositiveInteger)valueList[k]!;

                    double segParamLength;
                    double segGeoLength;

                    if (indices.Length == 3)
                    {
                        // Arc segment — parameterized length is angular span, geo length is radius * span
                        int i0 = (int)(indices[0] - 1);
                        int i1 = (int)(indices[1] - 1);
                        int i2 = (int)(indices[2] - 1);

                        double arcSpan = ComputeArcAngularSpan(
                            allPointsXYZ[i0 * 3], allPointsXYZ[i0 * 3 + 1], allPointsXYZ[i0 * 3 + 2],
                            allPointsXYZ[i1 * 3], allPointsXYZ[i1 * 3 + 1], allPointsXYZ[i1 * 3 + 2],
                            allPointsXYZ[i2 * 3], allPointsXYZ[i2 * 3 + 1], allPointsXYZ[i2 * 3 + 2],
                            out double radius);

                        if (arcSpan > 0)
                        {
                            segParamLength = arcSpan;
                            segGeoLength = radius * arcSpan;
                        }
                        else
                        {
                            // Collinear fallback — treat as line
                            segParamLength = 1;
                            double dx = allPointsXYZ[i2 * 3] - allPointsXYZ[i0 * 3];
                            double dy = allPointsXYZ[i2 * 3 + 1] - allPointsXYZ[i0 * 3 + 1];
                            double dz = allPointsXYZ[i2 * 3 + 2] - allPointsXYZ[i0 * 3 + 2];
                            segGeoLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        }
                    }
                    else
                    {
                        // Line segment(s) — parameterized length is number of sub-segments
                        segGeoLength = 0;
                        for (int p = 0; p < indices.Length - 1; p++)
                        {
                            int ip1 = (int)(indices[p] - 1);
                            int ip2 = (int)(indices[p + 1] - 1);
                            double dx = allPointsXYZ[ip2 * 3] - allPointsXYZ[ip1 * 3];
                            double dy = allPointsXYZ[ip2 * 3 + 1] - allPointsXYZ[ip1 * 3 + 1];
                            double dz = allPointsXYZ[ip2 * 3 + 2] - allPointsXYZ[ip1 * 3 + 2];
                            segGeoLength += Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        }
                        segParamLength = Math.Max(indices.Length - 1, 1);
                    }

                    if (startPar > 0)
                    {
                        double ratio = Math.Min(startPar / segParamLength, 1.0);
                        startPar -= ratio * segParamLength;
                        occStart += ratio * segGeoLength;
                    }

                    if (endPar > 0)
                    {
                        double ratio = Math.Min(endPar / segParamLength, 1.0);
                        endPar -= ratio * segParamLength;
                        occEnd += ratio * segGeoLength;
                    }
                }
            }
            else
            {
                // No explicit segments — polyline through all points in order.
                // Each sub-segment has parametric length 1 (total = nPoints - 1).
                // Map from parametric space to arc-length.
                List<double> allPointsXYZ = ExtractPointCoordinatesFlat(ifcIndexed);
                int numPoints = allPointsXYZ.Count / 3;

                for (int i = 0; i < numPoints - 1; i++)
                {
                    if (startPar <= 0 && endPar <= 0)
                        break;

                    int ip1 = i;
                    int ip2 = i + 1;
                    double dx = allPointsXYZ[ip2 * 3] - allPointsXYZ[ip1 * 3];
                    double dy = allPointsXYZ[ip2 * 3 + 1] - allPointsXYZ[ip1 * 3 + 1];
                    double dz = allPointsXYZ[ip2 * 3 + 2] - allPointsXYZ[ip1 * 3 + 2];
                    double segGeoLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    const double segParamLength = 1.0;

                    if (startPar > 0)
                    {
                        double ratio = Math.Min(startPar / segParamLength, 1.0);
                        startPar -= ratio * segParamLength;
                        occStart += ratio * segGeoLength;
                    }

                    if (endPar > 0)
                    {
                        double ratio = Math.Min(endPar / segParamLength, 1.0);
                        endPar -= ratio * segParamLength;
                        occEnd += ratio * segGeoLength;
                    }
                }
            }

            // Clamp
            occStart = Math.Max(0, Math.Min(occStart, totalGeoLength));
            occEnd = Math.Max(occStart, Math.Min(occEnd, totalGeoLength));

            // If mapped range covers the entire wire, return as-is
            if (Math.Abs(occStart) < _modelService.Precision &&
                Math.Abs(occEnd - totalGeoLength) < _modelService.Precision)
                return wire;

            try
            {
                int trimResult = XbimGeometryNativeApi.xbim_wire_build_trimmed_by_length(
                    ContextHandle,
                    wire.Handle,
                    occStart, occEnd,
                    _modelService.Precision,
                    out var trimmedHandle);

                if (trimResult != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to trim indexed poly curve directrix #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new XbimWire(trimmedHandle);
            }
            finally
            {
                wire.Dispose();
            }
        }

        #endregion

        #region Parameterization Helpers

        /// <summary>
        /// Extracts point coordinates from an indexed poly curve as a flat XYZ array.
        /// Delegates to <see cref="CurveFactory.ExtractIndexedPoints3d"/> and flattens the result.
        /// </summary>
        private static List<double> ExtractPointCoordinatesFlat(IIfcIndexedPolyCurve ifcIndexed)
        {
            var tuples = CurveFactory.ExtractIndexedPoints3d(ifcIndexed);
            var result = new List<double>(tuples.Count * 3);
            foreach (var (x, y, z) in tuples)
            {
                result.Add(x);
                result.Add(y);
                result.Add(z);
            }
            return result;
        }

        /// <summary>
        /// Returns the IFC parameterized length of a composite curve segment based on its
        /// parent curve type. Lines return 1, trimmed curves return the parametric span
        /// (Trim2-Trim1), all others return 1.
        /// </summary>
        private double GetSegmentParameterizedLength(IIfcCompositeCurveSegment segment)
        {
            var parent = segment.ParentCurve;

            if (parent is IIfcLine)
                return 1.0;

            if (parent is IIfcTrimmedCurve tc)
            {
                try
                {
                    double valTrim1 = 0.0;
                    double valTrim2 = 1.0;

                    foreach (var trim in tc.Trim1)
                    {
                        if (trim is Xbim.Ifc4.MeasureResource.IfcParameterValue pv)
                        {
                            valTrim1 = (double)pv.Value;
                            break;
                        }
                    }
                    foreach (var trim in tc.Trim2)
                    {
                        if (trim is Xbim.Ifc4.MeasureResource.IfcParameterValue pv)
                        {
                            valTrim2 = (double)pv.Value;
                            break;
                        }
                    }

                    double result = valTrim2 - valTrim1;

                    // For conics, params are periodic — take absolute value
                    if (result < 0 && tc.BasisCurve is IIfcConic)
                        result = Math.Abs(result);

                    return result > 0 ? result : 1.0;
                }
                catch
                {
                    return 1.0;
                }
            }

            return 1.0;
        }

        /// <summary>
        /// Computes the IFC parameterized length of an indexed poly curve by walking its
        /// segments. Arc segments contribute their angular span in radians; line segments
        /// contribute 1 per line. If no segments are defined, returns Points.Count-1.
        /// </summary>
        private double GetIndexedPolyCurveParameterizedLength(IIfcIndexedPolyCurve ifcIndexed)
        {
            if (ifcIndexed.Segments == null || !ifcIndexed.Segments.Any())
            {
                // No explicit segments — polyline through all points
                if (ifcIndexed.Points is IIfcCartesianPointList3D pl3D)
                    return Math.Max(pl3D.CoordList.Count - 1, 1);
                if (ifcIndexed.Points is IIfcCartesianPointList2D pl2D)
                    return Math.Max(pl2D.CoordList.Count - 1, 1);
                return 1.0;
            }

            List<double> allPointsXYZ;
            try
            {
                allPointsXYZ = ExtractPointCoordinatesFlat(ifcIndexed);
            }
            catch (NotSupportedException)
            {
                return 1.0;
            }

            double paramLength = 0.0;

            foreach (var segment in ifcIndexed.Segments)
            {
                var valueList = (System.Collections.IList)segment.Value;
                var indices = new long[valueList.Count];
                for (int k = 0; k < valueList.Count; k++)
                    indices[k] = (IfcPositiveInteger)valueList[k]!;

                if (indices.Length == 3)
                {
                    // Arc segment — compute angular span
                    int i0 = (int)(indices[0] - 1);
                    int i1 = (int)(indices[1] - 1);
                    int i2 = (int)(indices[2] - 1);

                    double startX = allPointsXYZ[i0 * 3], startY = allPointsXYZ[i0 * 3 + 1], startZ = allPointsXYZ[i0 * 3 + 2];
                    double midX = allPointsXYZ[i1 * 3], midY = allPointsXYZ[i1 * 3 + 1], midZ = allPointsXYZ[i1 * 3 + 2];
                    double endX = allPointsXYZ[i2 * 3], endY = allPointsXYZ[i2 * 3 + 1], endZ = allPointsXYZ[i2 * 3 + 2];

                    double arcSpan = ComputeArcAngularSpan(startX, startY, startZ, midX, midY, midZ, endX, endY, endZ, out _);
                    if (arcSpan > 0)
                        paramLength += arcSpan;
                    else
                        paramLength += 1.0; // Collinear fallback — treat as line
                }
                else
                {
                    // Line segment(s) — +1 per line
                    paramLength += Math.Max(indices.Length - 1, 1);
                }
            }

            return paramLength > 0 ? paramLength : 1.0;
        }

        /// <summary>
        /// Computes the angular span of a circular arc through three points, in radians.
        /// Returns 0 if the points are collinear.
        /// </summary>
        private static double ComputeArcAngularSpan(
            double p1X, double p1Y, double p1Z,
            double p2X, double p2Y, double p2Z,
            double p3X, double p3Y, double p3Z,
            out double arcRadius)
        {
            arcRadius = 0.0;

            // Vectors from mid to start and mid to end
            double v1X = p1X - p2X, v1Y = p1Y - p2Y, v1Z = p1Z - p2Z;
            double v2X = p3X - p2X, v2Y = p3Y - p2Y, v2Z = p3Z - p2Z;

            // Cross product to check collinearity
            double crossX = v1Y * v2Z - v1Z * v2Y;
            double crossY = v1Z * v2X - v1X * v2Z;
            double crossZ = v1X * v2Y - v1Y * v2X;
            double crossMag = Math.Sqrt(crossX * crossX + crossY * crossY + crossZ * crossZ);

            if (crossMag < 1e-10)
                return 0.0; // Collinear

            // Compute circle center using circumcircle formula
            double ax = p1X, ay = p1Y, az = p1Z;
            double bx = p2X, by = p2Y, bz = p2Z;
            double cx = p3X, cy = p3Y, cz = p3Z;

            // Use 2D projection onto the plane of the three points
            // Direction vectors in the arc plane
            double abX = bx - ax, abY = by - ay, abZ = bz - az;
            double acX = cx - ax, acY = cy - ay, acZ = cz - az;

            double abLen2 = abX * abX + abY * abY + abZ * abZ;
            double acLen2 = acX * acX + acY * acY + acZ * acZ;
            double abDotAc = abX * acX + abY * acY + abZ * acZ;

            double denom = 2.0 * (abLen2 * acLen2 - abDotAc * abDotAc);
            if (Math.Abs(denom) < 1e-20)
                return 0.0;

            double s = acLen2 * (abLen2 - abDotAc) / denom;
            double t = abLen2 * (acLen2 - abDotAc) / denom;

            double centerX = ax + s * abX + t * acX;
            double centerY = ay + s * abY + t * acY;
            double centerZ = az + s * abZ + t * acZ;

            // Radius
            double rX = p1X - centerX, rY = p1Y - centerY, rZ = p1Z - centerZ;
            double radius = Math.Sqrt(rX * rX + rY * rY + rZ * rZ);
            if (radius < 1e-10)
                return 0.0;

            arcRadius = radius;

            // Vectors from center to start and end points
            double csX = p1X - centerX, csY = p1Y - centerY, csZ = p1Z - centerZ;
            double ceX = p3X - centerX, ceY = p3Y - centerY, ceZ = p3Z - centerZ;

            double csLen = Math.Sqrt(csX * csX + csY * csY + csZ * csZ);
            double ceLen = Math.Sqrt(ceX * ceX + ceY * ceY + ceZ * ceZ);

            if (csLen < 1e-10 || ceLen < 1e-10)
                return 0.0;

            double dot = (csX * ceX + csY * ceY + csZ * ceZ) / (csLen * ceLen);
            dot = Math.Max(-1.0, Math.Min(1.0, dot));

            return Math.Acos(dot);
        }

        #endregion
    }
}
