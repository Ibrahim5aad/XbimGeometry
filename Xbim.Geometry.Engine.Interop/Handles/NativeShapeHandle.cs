using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimShapeHandle.
    /// Automatically calls xbim_shape_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeShapeHandle : SafeHandle
    {
        static NativeShapeHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeShapeHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_shape_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
