using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimLocationHandle.
    /// Automatically calls xbim_location_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeLocationHandle : SafeHandle
    {
        static NativeLocationHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeLocationHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_location_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
