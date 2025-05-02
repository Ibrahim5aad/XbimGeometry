using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimCurveHandle.
    /// Automatically calls xbim_curve_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeCurveHandle : SafeHandle
    {
        static NativeCurveHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeCurveHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_curve_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
