using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts a V6 <see cref="Wire"/> to the legacy <see cref="IXbimWire"/> interface.
    /// </summary>
    internal class V5Wire : V5Shape, IXbimWire, IEquatable<IXbimWire>
    {
        private readonly Wire _wire;

        internal V5Wire(Wire wire) : base(wire)
        {
            _wire = wire;
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimWireType;

        public IXbimEdgeSet Edges
        {
            get
            {
                var v6Edges = _wire.EdgeLoop;
                var v5Edges = v6Edges.Select(e => (IXbimEdge)new V5Edge((Edge)e)).ToArray();
                return new V5EdgeSet(v5Edges);
            }
        }

        public IXbimVertexSet Vertices
        {
            get
            {
                var handles = Inner.GetSubShapeHandles(XShapeType.Vertex);
                var v5Verts = handles.Select(h => (IXbimVertex)new V5Vertex(new Vertex(h))).ToArray();
                return new V5VertexSet(v5Verts);
            }
        }

        public IEnumerable<XbimPoint3D> Points
        {
            get
            {
                var handles = Inner.GetSubShapeHandles(XShapeType.Vertex);
                foreach (var h in handles)
                {
                    int result = XbimGeometryNativeApi.xbim_vertex_point(h, out double x, out double y, out double z);
                    if (result != 0)
                        throw new InvalidOperationException(
                            $"Failed to get vertex point: {XbimGeometryNativeApi.GetLastError()}");
                    yield return new XbimPoint3D(x, y, z);
                }
            }
        }

        public XbimVector3D Normal
        {
            get
            {
                // Newell's method for computing the normal of a polygon
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

        public bool IsPlanar => false;

        public bool IsClosed => Inner.IsClosed;

        public XbimPoint3D Start
        {
            get
            {
                var handles = Inner.GetSubShapeHandles(XShapeType.Vertex);
                if (handles.Length == 0)
                    throw new InvalidOperationException("Wire has no vertices.");
                int result = XbimGeometryNativeApi.xbim_vertex_point(handles[0], out double x, out double y, out double z);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get vertex point: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimPoint3D(x, y, z);
            }
        }

        public XbimPoint3D End
        {
            get
            {
                var handles = Inner.GetSubShapeHandles(XShapeType.Vertex);
                if (handles.Length == 0)
                    throw new InvalidOperationException("Wire has no vertices.");
                var last = handles[handles.Length - 1];
                int result = XbimGeometryNativeApi.xbim_vertex_point(last, out double x, out double y, out double z);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get vertex point: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimPoint3D(x, y, z);
            }
        }

        public double Length
        {
            get
            {
                // Sum the lengths of all edges in the wire
                double total = 0;
                foreach (var edge in _wire.EdgeLoop)
                {
                    int result = XbimGeometryNativeApi.xbim_edge_length(((Edge)edge).Handle, out double edgeLen);
                    if (result != 0)
                        throw new InvalidOperationException(
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

        public string ToBRep => Inner.BrepString();

        public bool Equals(IXbimWire other)
        {
            if (other is V5Wire v5 && Inner is Shape s && v5.Inner is Shape os)
                return s.IsEqual(os);
            return false;
        }
    }
}
