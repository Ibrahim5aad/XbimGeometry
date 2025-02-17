using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXSolid"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Solid.
    /// </summary>
    internal class NativeSolid : NativeShape, IXSolid
    {
        internal NativeSolid(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Volume
        {
            get
            {
                int result = NativeMethods.xbim_shape_volume(Handle, out double volume);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to compute volume: {NativeMethods.GetLastError()}");
                return volume;
            }
        }

        public IXShell[] Shells
        {
            get
            {
                // Shell traversal not yet available in native API (TOPO-008)
                throw new NotImplementedException(
                    "Shell traversal will be available after TOPO-008.");
            }
        }
    }
}
