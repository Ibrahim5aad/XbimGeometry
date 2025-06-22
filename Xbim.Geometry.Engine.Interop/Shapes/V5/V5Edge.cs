using System;
using Xbim.Common.Geometry;

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
                throw new NotSupportedException("Edge start vertex not yet supported.");
            }
        }

        public IXbimVertex EdgeEnd
        {
            get
            {
                throw new NotSupportedException("Edge end vertex not yet supported.");
            }
        }

        public IXbimCurve EdgeGeometry
        {
            get
            {
                throw new NotSupportedException("Edge curve geometry not yet supported.");
            }
        }

        public double Length
        {
            get
            {
                throw new NotSupportedException("Edge length not yet supported.");
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
