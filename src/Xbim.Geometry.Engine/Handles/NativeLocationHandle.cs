using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimLocationHandle.
    /// Automatically calls xbim_location_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeLocationHandle : SafeHandle
    {
        /// <summary>
        /// Sentinel handle representing "no location" (identity transform).
        /// Passed to native functions that accept an optional location parameter.
        /// Uses <c>ownsHandle: false</c> so the GC never tries to release it.
        /// </summary>
        internal static readonly NativeLocationHandle NullHandle = new NativeLocationHandle(ownsHandle: false);

        static NativeLocationHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeLocationHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        private NativeLocationHandle(bool ownsHandle) : base(IntPtr.Zero, ownsHandle) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_location_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
