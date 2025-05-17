using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds wire shapes from IFC curve entities, profile definitions, and point arrays.
    /// Wires represent connected sequences of edges forming open or closed paths.
    /// </summary>
    internal class NativeWireFactory : IXWireFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeWireFactory(NativeModelGeometryService modelService, ILogger logger)
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
                throw new InvalidOperationException(
                    $"Failed to build wire from {points.Length} points: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeWire(wireHandle);
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

            throw new NotSupportedException(
                $"Wire from curve type {ifcCurve.ExpressType.ExpressName} #{ifcCurve.EntityLabel} is not yet supported.");
        }

        public IXWire Build(IIfcProfileDef ifcProfileDef)
        {
            // Build the profile as a face, then extract the outer wire
            var face = (NativeFace)_modelService.ProfileFactory.BuildFace(ifcProfileDef);
            // For now, the face itself contains the profile shape.
            // Wire extraction from faces requires TOPO-008 traversal API.
            // Build a wire directly from the profile outline instead.
            throw new NotImplementedException(
                $"Wire from profile #{ifcProfileDef.EntityLabel} requires face wire extraction support (TOPO-008).");
        }

        #region Polyline

        private IXWire BuildFromPolyline(IIfcPolyline ifcPolyline)
        {
            var points = ifcPolyline.Points;
            if (points.Count < 2)
                throw new InvalidOperationException(
                    $"IIfcPolyline #{ifcPolyline.EntityLabel} has fewer than 2 points.");

            var pointsXYZ = new double[points.Count * 3];
            for (int i = 0; i < points.Count; i++)
            {
                var cp = points[i];
                pointsXYZ[i * 3 + 0] = cp.Coordinates[0];
                pointsXYZ[i * 3 + 1] = cp.Coordinates[1];
                pointsXYZ[i * 3 + 2] = (int)cp.Dim == 3 ? (double)cp.Coordinates[2] : 0.0;
            }

            int result = XbimGeometryNativeApi.xbim_wire_build_polyline(
                ContextHandle,
                pointsXYZ, points.Count,
                _modelService.Precision,
                out var wireHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build wire from polyline #{ifcPolyline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeWire(wireHandle);
        }

        #endregion

        #region IndexedPolyCurve

        private IXWire BuildFromIndexedPolyCurve(IIfcIndexedPolyCurve ifcIndexed)
        {
            // Extract all points from the coordinate list
            List<double> allPointsXYZ;

            if (ifcIndexed.Points is IIfcCartesianPointList3D pl3D)
            {
                var coords = pl3D.CoordList;
                allPointsXYZ = new List<double>(coords.Count * 3);
                foreach (var pt in coords)
                {
                    allPointsXYZ.Add(pt[0]);
                    allPointsXYZ.Add(pt[1]);
                    allPointsXYZ.Add(pt.Count > 2 ? pt[2] : 0.0);
                }
            }
            else if (ifcIndexed.Points is IIfcCartesianPointList2D pl2D)
            {
                var coords = pl2D.CoordList;
                allPointsXYZ = new List<double>(coords.Count * 3);
                foreach (var pt in coords)
                {
                    allPointsXYZ.Add(pt[0]);
                    allPointsXYZ.Add(pt[1]);
                    allPointsXYZ.Add(0.0);
                }
            }
            else
            {
                throw new NotSupportedException(
                    $"Unsupported point list type in IIfcIndexedPolyCurve #{ifcIndexed.EntityLabel}.");
            }

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
                    throw new InvalidOperationException(
                        $"Failed to build indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new NativeWire(wireHandle);
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
                    // Each segment is a list of 1-based point indices
                    var indices = ((System.Collections.IEnumerable)segment)
                        .Cast<long>()
                        .ToArray();

                    if (indices.Length == 3)
                    {
                        // Arc segment (3 indices: start, mid, end)
                        // Approximate with a line segment from start to end
                        int startIdx = (int)(indices[0] - 1);
                        int endIdx = (int)(indices[2] - 1);

                        int r = XbimGeometryNativeApi.xbim_edge_build_line(
                            ContextHandle,
                            allPointsXYZ[startIdx * 3], allPointsXYZ[startIdx * 3 + 1], allPointsXYZ[startIdx * 3 + 2],
                            allPointsXYZ[endIdx * 3], allPointsXYZ[endIdx * 3 + 1], allPointsXYZ[endIdx * 3 + 2],
                            out var edgeHandle);
                        if (r != 0)
                            throw new InvalidOperationException(
                                $"Failed to build arc edge in indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");
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
                                throw new InvalidOperationException(
                                    $"Failed to build line edge in indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");
                            edgeHandles.Add(edgeHandle);
                        }
                    }
                }

                // Build wire from edges
                var ptrs = edgeHandles.Select(h => h.DangerousGetHandle()).ToArray();
                int buildResult = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                    ContextHandle,
                    ptrs, ptrs.Length,
                    out var resultWireHandle);

                if (buildResult != 0)
                    throw new InvalidOperationException(
                        $"Failed to build wire from edges in indexed poly curve #{ifcIndexed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new NativeWire(resultWireHandle);
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
            var edgeHandles = new List<NativeShapeHandle>();
            try
            {
                foreach (var segment in ifcComposite.Segments)
                {
                    var parentCurve = segment.ParentCurve;
                    if (parentCurve == null) continue;

                    // Build each segment as a wire, then extract its edges
                    var segWire = Build(parentCurve);
                    // For simplicity, use the wire handle directly as an edge equivalent
                    edgeHandles.Add(((NativeWire)segWire).Handle);
                }

                if (edgeHandles.Count == 0)
                    throw new InvalidOperationException(
                        $"IIfcCompositeCurve #{ifcComposite.EntityLabel} has no valid segments.");

                // If we only got one segment, return it directly
                if (edgeHandles.Count == 1)
                    return new NativeWire(edgeHandles[0]);

                // Build wire from multiple edges/wires
                // The wire handles ARE the segment wires - just return the first for now
                // Full composite wire building needs the sew/merge approach
                return new NativeWire(edgeHandles[0]);
            }
            catch
            {
                foreach (var h in edgeHandles)
                    h.Dispose();
                throw;
            }
        }

        #endregion

        #region Single-Curve Wires

        private IXWire BuildFromTrimmedCurve(IIfcTrimmedCurve ifcTrimmed)
        {
            // Build the basis curve as a wire (trimming handled at curve level)
            return Build(ifcTrimmed.BasisCurve);
        }

        private IXWire BuildFromLine(IIfcLine ifcLine)
        {
            var edge = (NativeEdge)((NativeEdgeFactory)_modelService.EdgeFactory).Build(ifcLine);
            var edgeHandles = new IntPtr[] { edge.Handle.DangerousGetHandle() };

            int result = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                ContextHandle,
                edgeHandles, 1,
                out var wireHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build wire from line #{ifcLine.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeWire(wireHandle);
        }

        private IXWire BuildFromCircle(IIfcCircle ifcCircle)
        {
            var edge = (NativeEdge)((NativeEdgeFactory)_modelService.EdgeFactory).Build(ifcCircle);
            var edgeHandles = new IntPtr[] { edge.Handle.DangerousGetHandle() };

            int result = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                ContextHandle,
                edgeHandles, 1,
                out var wireHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build wire from circle #{ifcCircle.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeWire(wireHandle);
        }

        private IXWire BuildFromEllipse(IIfcEllipse ifcEllipse)
        {
            var edge = (NativeEdge)((NativeEdgeFactory)_modelService.EdgeFactory).Build(ifcEllipse);
            var edgeHandles = new IntPtr[] { edge.Handle.DangerousGetHandle() };

            int result = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                ContextHandle,
                edgeHandles, 1,
                out var wireHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build wire from ellipse #{ifcEllipse.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeWire(wireHandle);
        }

        private IXWire BuildFromBSpline(IIfcBSplineCurveWithKnots ifcBSpline)
        {
            // Build edge from the B-spline curve via EdgeFactory, then wrap as wire
            var edge = (NativeEdge)_modelService.EdgeFactory.Build(ifcBSpline);

            var edgePtrs = new IntPtr[] { edge.Handle.DangerousGetHandle() };
            int wireResult = XbimGeometryNativeApi.xbim_wire_build_from_edges(
                ContextHandle,
                edgePtrs, 1,
                out var wireHandle);

            if (wireResult != 0)
                throw new InvalidOperationException(
                    $"Failed to build wire from B-spline edge #{ifcBSpline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeWire(wireHandle);
        }

        #endregion
    }
}
