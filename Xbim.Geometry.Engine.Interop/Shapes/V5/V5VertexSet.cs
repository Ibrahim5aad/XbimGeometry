using System;
using System.Collections;
using System.Collections.Generic;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts an array of <see cref="IXbimVertex"/> to the legacy <see cref="IXbimVertexSet"/> interface.
    /// </summary>
    internal class V5VertexSet : IXbimVertexSet
    {
        private readonly IXbimVertex[] _vertices;

        internal V5VertexSet(IXbimVertex[] vertices)
        {
            _vertices = vertices ?? Array.Empty<IXbimVertex>();
        }

        public int Count => _vertices.Length;

        public IXbimVertex First => _vertices.Length > 0
            ? _vertices[0]
            : throw new InvalidOperationException("Vertex set is empty.");

        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimVertexSetType;
        public bool IsValid => _vertices.Length > 0;
        public bool IsSet => true;

        public XbimRect3D BoundingBox
        {
            get
            {
                if (_vertices.Length == 0) return XbimRect3D.Empty;
                var result = _vertices[0].BoundingBox;
                for (int i = 1; i < _vertices.Length; i++)
                    result.Union(_vertices[i].BoundingBox);
                return result;
            }
        }

        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Vertex set transform not supported.");

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Vertex set transform not supported.");

        public IEnumerator<IXbimVertex> GetEnumerator() => ((IEnumerable<IXbimVertex>)_vertices).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _vertices.GetEnumerator();

        public void Dispose() { }
    }
}
