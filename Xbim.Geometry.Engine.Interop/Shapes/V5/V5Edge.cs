using System;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts a V6 <see cref="Edge"/> to the legacy <see cref="IXbimEdge"/> interface.
    /// </summary>
    internal class V5Edge : V5Shape, IXbimEdge, IEquatable<IXbimEdge>
    {
        internal V5Edge(Edge edge) : base(edge)
        {
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimEdgeType;

        public IXbimVertex EdgeStart
        {
            get
            {
                var vertexHandles = Inner.GetSubShapeHandles(XShapeType.Vertex);
                if (vertexHandles.Length == 0)
                    throw new InvalidOperationException("Edge has no vertices.");
                return new V5Vertex(new Vertex(vertexHandles[0]));
            }
        }

        public IXbimVertex EdgeEnd
        {
            get
            {
                var vertexHandles = Inner.GetSubShapeHandles(XShapeType.Vertex);
                if (vertexHandles.Length == 0)
                    throw new InvalidOperationException("Edge has no vertices.");
                // For degenerate edges (single vertex), start == end
                return new V5Vertex(new Vertex(vertexHandles[vertexHandles.Length > 1 ? 1 : 0]));
            }
        }

        public IXbimCurve EdgeGeometry
        {
            get
            {
                var curve = (Curve)((Edge)Inner).EdgeGeometry;
                return new V5Curve(curve);
            }
        }

        public double Length
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_edge_length(Inner.Handle, out double length);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get edge length: {XbimGeometryNativeApi.GetLastError()}");
                return length;
            }
        }

        public string ToBRep => Inner.BrepString();

        public bool Equals(IXbimEdge other)
        {
            if (other is V5Edge v5 && Inner is Shape s && v5.Inner is Shape os)
                return s.IsEqual(os);
            return false;
        }
    }
}
