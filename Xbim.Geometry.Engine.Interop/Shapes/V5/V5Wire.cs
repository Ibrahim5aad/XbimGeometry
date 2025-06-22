using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;

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
                throw new NotSupportedException("Wire point enumeration not yet supported.");
            }
        }

        public XbimVector3D Normal
        {
            get
            {
                throw new NotSupportedException("Wire normal not yet supported.");
            }
        }

        public bool IsPlanar => false;

        public bool IsClosed => Inner.IsClosed;

        public XbimPoint3D Start
        {
            get
            {
                throw new NotSupportedException("Wire start point not yet supported.");
            }
        }

        public XbimPoint3D End
        {
            get
            {
                throw new NotSupportedException("Wire end point not yet supported.");
            }
        }

        public double Length
        {
            get
            {
                throw new NotSupportedException("Wire length not yet supported.");
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
