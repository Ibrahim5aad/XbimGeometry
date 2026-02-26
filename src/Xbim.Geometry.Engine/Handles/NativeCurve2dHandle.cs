using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimCurve2dHandle.
    /// Automatically calls xbim_curve2d_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeCurve2dHandle : SafeHandle
    {
        static NativeCurve2dHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeCurve2dHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_curve2d_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
