using System;
using System.Linq;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXSolid"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Solid.
    /// </summary>
    internal class Solid : Shape, IXSolid
    {
        internal Solid(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Volume
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_volume(Handle, out double volume);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to compute volume: {XbimGeometryNativeApi.GetLastError()}");
                return volume;
            }
        }

        public IXShell[] Shells
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Shell);
                return handles.Select(h => (IXShell)new Shell(h)).ToArray();
            }
        }
    }
}
