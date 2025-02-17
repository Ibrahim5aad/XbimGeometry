using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXWire"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Wire.
    /// </summary>
    internal class NativeWire : NativeShape, IXWire
    {
        internal NativeWire(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Length
        {
            get
            {
                // Wire length not yet available in native API (TOPO-002)
                throw new NotImplementedException(
                    "Wire length will be available after TOPO-002.");
            }
        }

        public double ContourArea
        {
            get
            {
                // Contour area not yet available in native API (TOPO-002)
                throw new NotImplementedException(
                    "ContourArea will be available after TOPO-002.");
            }
        }

        public IXEdge[] EdgeLoop
        {
            get
            {
                // Edge traversal not yet available in native API (TOPO-008)
                throw new NotImplementedException(
                    "Edge traversal will be available after TOPO-008.");
            }
        }
    }
}
