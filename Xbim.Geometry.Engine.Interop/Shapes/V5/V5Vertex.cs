using System;
using Xbim.Common.Geometry;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts a V6 <see cref="Vertex"/> to the legacy <see cref="IXbimVertex"/> interface.
    /// </summary>
    internal class V5Vertex : V5Shape, IXbimVertex, IEquatable<IXbimVertex>
    {
        internal V5Vertex(Vertex vertex) : base(vertex)
        {
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimVertexType;

        public XbimPoint3D VertexGeometry
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_vertex_point(
                    Inner.Handle,
                    out double x, out double y, out double z);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get vertex point: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimPoint3D(x, y, z);
            }
        }

        public string ToBRep => Inner.BrepString();

        public bool Equals(IXbimVertex other)
        {
            if (other is V5Vertex v5 && Inner is Shape s && v5.Inner is Shape os)
                return s.IsEqual(os);
            return false;
        }
    }
}
