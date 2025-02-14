using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimContextHandle.
    /// Automatically calls xbim_context_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeContextHandle : SafeHandle
    {
        static NativeContextHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeContextHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_context_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
