using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXFace"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Face.
    /// </summary>
    internal class NativeFace : NativeShape, IXFace
    {
        internal NativeFace(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Area
        {
            get
            {
                int result = NativeMethods.xbim_shape_surface_area(Handle, out double area);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to compute face area: {NativeMethods.GetLastError()}");
                return area;
            }
        }

        public double Tolerance
        {
            get
            {
                // Face tolerance extraction not yet available in native API (TOPO-001)
                throw new NotImplementedException(
                    "Face tolerance will be available after TOPO-001.");
            }
        }

        public IXWire OuterBound
        {
            get
            {
                // Wire extraction not yet available in native API (TOPO-001)
                throw new NotImplementedException(
                    "OuterBound will be available after TOPO-001.");
            }
        }

        public IXWire[] InnerBounds
        {
            get
            {
                // Wire extraction not yet available in native API (TOPO-001)
                throw new NotImplementedException(
                    "InnerBounds will be available after TOPO-001.");
            }
        }

        public IXSurface Surface
        {
            get
            {
                // Surface extraction not yet available in native API (TOPO-006)
                throw new NotImplementedException(
                    "Surface will be available after TOPO-006.");
            }
        }
    }
}
