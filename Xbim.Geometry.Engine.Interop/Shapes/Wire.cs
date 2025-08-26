using System;
using System.Linq;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXWire"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Wire.
    /// </summary>
    internal class Wire : Shape, IXWire
    {
        internal Wire(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Length
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_wire_length(Handle, out double length);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get wire length: {XbimGeometryNativeApi.GetLastError()}");
                return length;
            }
        }

        public double ContourArea
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_wire_contour_area(Handle, out double area);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get wire contour area: {XbimGeometryNativeApi.GetLastError()}");
                return area;
            }
        }

        public IXEdge[] EdgeLoop
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Edge);
                return handles.Select(h => (IXEdge)new Edge(h)).ToArray();
            }
        }
    }
}
