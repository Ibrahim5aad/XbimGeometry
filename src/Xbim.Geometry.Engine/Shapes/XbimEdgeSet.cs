using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Shapes
{
    /// <summary>
    /// Collection of edges implementing <see cref="IXbimEdgeSet"/>.
    /// </summary>
    internal class XbimEdgeSet : IXbimEdgeSet
    {
        private readonly IXbimEdge[] _edges;

        internal XbimEdgeSet(IXbimEdge[] edges)
        {
            _edges = edges ?? Array.Empty<IXbimEdge>();
        }

        public int Count => _edges.Length;

        public IXbimEdge First => _edges.Length > 0
            ? _edges[0]
            : throw new InvalidOperationException("Edge set is empty.");

        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimEdgeSetType;
        public bool IsValid => _edges.Length > 0;
        public bool IsSet => true;

        public XbimRect3D BoundingBox
        {
            get
            {
                if (_edges.Length == 0) return XbimRect3D.Empty;
                var result = _edges[0].BoundingBox;
                for (int i = 1; i < _edges.Length; i++)
                    result.Union(_edges[i].BoundingBox);
                return result;
            }
        }

        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
        {
            var transformed = _edges
                .Select(e => (IXbimEdge)e.Transform(matrix3D)).ToArray();
            return new XbimEdgeSet(transformed);
        }

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
        {
            var transformed = _edges
                .Select(e => (IXbimEdge)((XbimShape)e).TransformShallow(matrix3D)).ToArray();
            return new XbimEdgeSet(transformed);
        }

        public IEnumerator<IXbimEdge> GetEnumerator() => ((IEnumerable<IXbimEdge>)_edges).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _edges.GetEnumerator();

        public void Dispose() { }
    }
}
