using System;
using System.Collections;
using System.Collections.Generic;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts an array of <see cref="IXbimEdge"/> to the legacy <see cref="IXbimEdgeSet"/> interface.
    /// </summary>
    internal class V5EdgeSet : IXbimEdgeSet
    {
        private readonly IXbimEdge[] _edges;

        internal V5EdgeSet(IXbimEdge[] edges)
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

        public XbimRect3D BoundingBox => XbimRect3D.Empty;

        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Edge set transform not supported.");

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Edge set transform not supported.");

        public IEnumerator<IXbimEdge> GetEnumerator() => ((IEnumerable<IXbimEdge>)_edges).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _edges.GetEnumerator();

        public void Dispose() { }
    }
}
