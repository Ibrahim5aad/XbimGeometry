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

        private NativeCurveHandle(IntPtr existingHandle, bool ownsHandle) : base(IntPtr.Zero, ownsHandle)
        {
            SetHandle(existingHandle);
        }

        /// <summary>
        /// Creates a non-owning handle that references an existing native curve pointer.
        /// The caller must ensure the owning handle outlives this borrowed reference.
        /// Disposing the borrowed handle is safe and does not release the native resource.
        /// </summary>
        internal static NativeCurveHandle Borrowed(IntPtr ptr)
        {
            return new NativeCurveHandle(ptr, ownsHandle: false);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_curve_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
