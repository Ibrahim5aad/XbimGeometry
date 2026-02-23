using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimAdvancedBrepBuilderHandle.
    /// Automatically calls xbim_advanced_brep_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeAdvancedBrepBuilderHandle : SafeHandle
    {
        static NativeAdvancedBrepBuilderHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeAdvancedBrepBuilderHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_advanced_brep_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
