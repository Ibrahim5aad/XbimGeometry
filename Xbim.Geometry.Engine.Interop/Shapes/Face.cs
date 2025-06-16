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
    internal class Face : Shape, IXFace
    {
        internal Face(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Area
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_surface_area(Handle, out double area);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to compute face area: {XbimGeometryNativeApi.GetLastError()}");
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
                int result = XbimGeometryNativeApi.xbim_face_outer_wire(Handle, out var wireHandle);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get outer wire: {XbimGeometryNativeApi.GetLastError()}");
                return new Wire(wireHandle);
            }
        }

        public IXWire[] InnerBounds
        {
            get
            {
                // Count total wires, subtract 1 for the outer wire
                int countResult = XbimGeometryNativeApi.xbim_shape_count_subshapes(
                    Handle, (int)XShapeType.Wire, out int totalWires);
                if (countResult != 0 || totalWires <= 1)
                    return Array.Empty<IXWire>();

                int capacity = totalWires - 1;
                var ptrs = new IntPtr[capacity];
                int innerCount = capacity;
                int getResult = XbimGeometryNativeApi.xbim_face_inner_wires(Handle, ptrs, ref innerCount);
                if (getResult != 0 || innerCount == 0)
                    return Array.Empty<IXWire>();

                var wires = new IXWire[innerCount];
                for (int i = 0; i < innerCount; i++)
                    wires[i] = new Wire(NativeShapeHandle.FromIntPtr(ptrs[i]));
                return wires;
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
