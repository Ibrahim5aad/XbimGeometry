using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXEdge"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Edge.
    /// </summary>
    internal class Edge : Shape, IXEdge
    {
        internal Edge(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Length
        {
            get
            {
                // Edge length not yet available in native API (TOPO-003)
                throw new NotImplementedException(
                    "Edge length will be available after TOPO-003.");
            }
        }

        public double Tolerance
        {
            get
            {
                // Edge tolerance not yet available in native API (TOPO-003)
                throw new NotImplementedException(
                    "Edge tolerance will be available after TOPO-003.");
            }
        }

        public IXCurve EdgeGeometry
        {
            get
            {
                // Edge geometry not yet available in native API (TOPO-006)
                throw new NotImplementedException(
                    "Edge geometry will be available after TOPO-006.");
            }
        }

        public IXVertex EdgeStart
        {
            get
            {
                // Edge vertex extraction not yet available in native API (TOPO-003)
                throw new NotImplementedException(
                    "Edge vertices will be available after TOPO-003.");
            }
        }

        public IXVertex EdgeEnd
        {
            get
            {
                // Edge vertex extraction not yet available in native API (TOPO-003)
                throw new NotImplementedException(
                    "Edge vertices will be available after TOPO-003.");
            }
        }
    }
}
