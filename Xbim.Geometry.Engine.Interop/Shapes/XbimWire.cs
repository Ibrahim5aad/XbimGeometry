using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Represents a wire shape (TopoDS_Wire), implementing <see cref="IXWire"/>
    /// and <see cref="IXbimWire"/> interfaces.
    /// </summary>
    internal class XbimWire : XbimShape, IXWire, IXbimWire, IEquatable<IXbimWire>
    {
        internal XbimWire(NativeShapeHandle handle) : base(handle)
        {
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimWireType;

        #region IXWire

        public double Length
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_wire_length(Handle, out double length);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get wire length: {XbimGeometryNativeApi.GetLastError()}");
                return length;
            }
        }

        public double ContourArea
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_wire_contour_area(Handle, out double area);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get wire contour area: {XbimGeometryNativeApi.GetLastError()}");
                return area;
            }
        }

        public IXEdge[] EdgeLoop
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Edge);
                return handles.Select(h => (IXEdge)new XbimEdge(h)).ToArray();
            }
        }

        #endregion

        #region IXbimWire

        IXbimEdgeSet IXbimWire.Edges
        {
            get
            {
                var v6Edges = EdgeLoop;
                var edges = v6Edges.Select(e => (IXbimEdge)e).ToArray();
                return new XbimEdgeSet(edges);
            }
        }

        IXbimVertexSet IXbimWire.Vertices
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Vertex);
                var verts = handles.Select(h => (IXbimVertex)new XbimVertex(h)).ToArray();
                return new XbimVertexSet(verts);
            }
        }

        public IEnumerable<XbimPoint3D> Points
        {
            get
            {
                int count = 0;
                int result = XbimGeometryNativeApi.xbim_wire_get_ordered_points(Handle, null, ref count);
                if (result != 0 || count == 0)
                    yield break;

                var coords = new double[count * 3];
                result = XbimGeometryNativeApi.xbim_wire_get_ordered_points(Handle, coords, ref count);
                if (result != 0)
                    yield break;

                for (int i = 0; i < count; i++)
                    yield return new XbimPoint3D(coords[i * 3], coords[i * 3 + 1], coords[i * 3 + 2]);
            }
        }

        public XbimVector3D Normal
        {
            get
            {
                var pts = Points.ToList();
                if (pts.Count < 3)
                    return new XbimVector3D(0, 0, 1);

                double nx = 0, ny = 0, nz = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    var curr = pts[i];
                    var next = pts[(i + 1) % pts.Count];
                    nx += (curr.Y - next.Y) * (curr.Z + next.Z);
                    ny += (curr.Z - next.Z) * (curr.X + next.X);
                    nz += (curr.X - next.X) * (curr.Y + next.Y);
                }

                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-15)
                    return new XbimVector3D(0, 0, 1);
                return new XbimVector3D(nx / len, ny / len, nz / len);
            }
        }

        public bool IsPlanar
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_wire_is_planar(Handle, 1e-7, out int isPlanar);
                return result == 0 && isPlanar != 0;
            }
        }

        public XbimPoint3D Start
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Vertex);
                if (handles.Length == 0)
                    throw new XbimGeometryServiceException("Wire has no vertices.");
                int result = XbimGeometryNativeApi.xbim_vertex_point(handles[0], out double x, out double y, out double z);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get vertex point: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimPoint3D(x, y, z);
            }
        }

        public XbimPoint3D End
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Vertex);
                if (handles.Length == 0)
                    throw new XbimGeometryServiceException("Wire has no vertices.");
                var last = handles[handles.Length - 1];
                int result = XbimGeometryNativeApi.xbim_vertex_point(last, out double x, out double y, out double z);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get vertex point: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimPoint3D(x, y, z);
            }
        }

        double IXbimWire.Length
        {
            get
            {
                double total = 0;
                foreach (var edge in EdgeLoop)
                {
                    int result = XbimGeometryNativeApi.xbim_edge_length(((XbimEdge)edge).Handle, out double edgeLen);
                    if (result != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to get edge length: {XbimGeometryNativeApi.GetLastError()}");
                    total += edgeLen;
                }
                return total;
            }
        }

        public IXbimWire Trim(double start, double end, double tolerance, ILogger logger = null)
        {
            throw new NotSupportedException("Wire trimming not yet supported.");
        }

        public string ToBRep => BrepString();

        public bool Equals(IXbimWire other)
        {
            if (other is XbimShape os)
                return IsEqual(os);
            return false;
        }

        #endregion
    }
}
