using System;
using System.Linq;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXShell"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Shell.
    /// </summary>
    internal class Shell : Shape, IXShell
    {
        internal Shell(NativeShapeHandle handle) : base(handle)
        {
        }

        public double SurfaceArea
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_surface_area(Handle, out double area);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to compute surface area: {XbimGeometryNativeApi.GetLastError()}");
                return area;
            }
        }

        public IXFace[] Faces
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Face);
                return handles.Select(h => (IXFace)new Face(h)).ToArray();
            }
        }
    }
}
