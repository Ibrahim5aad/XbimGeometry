using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimSurfaceHandle.
    /// Automatically calls xbim_surface_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeSurfaceHandle : SafeHandle
    {
        static NativeSurfaceHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeSurfaceHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_surface_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
