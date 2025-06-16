using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXVertex"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Vertex.
    /// </summary>
    internal class Vertex : Shape, IXVertex
    {
        internal Vertex(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Tolerance
        {
            get
            {
                // Vertex tolerance not yet available in native API (TOPO-005)
                throw new NotImplementedException(
                    "Vertex tolerance will be available after TOPO-005.");
            }
        }

        public IXPoint VertexGeometry
        {
            get
            {
                // Vertex point extraction not yet available in native API (TOPO-005)
                throw new NotImplementedException(
                    "Vertex point will be available after TOPO-005.");
            }
        }
    }
}
