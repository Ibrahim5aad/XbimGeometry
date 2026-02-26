using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimContextHandle.
    /// Automatically calls xbim_context_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeContextHandle : SafeHandle
    {
        /// <summary>
        /// Sentinel handle representing "no context" (NULL).
        /// Passed to native functions where the context is optional (used only for logging).
        /// Uses <c>ownsHandle: false</c> so the GC never tries to release it.
        /// </summary>
        internal static readonly NativeContextHandle NullHandle = new NativeContextHandle(ownsHandle: false);

        static NativeContextHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeContextHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        private NativeContextHandle(bool ownsHandle) : base(IntPtr.Zero, ownsHandle) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_context_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
