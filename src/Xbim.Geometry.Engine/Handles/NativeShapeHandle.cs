using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Handles
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

        private NativeShapeHandle(IntPtr existingHandle) : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(existingHandle);
        }

        /// <summary>
        /// Creates a new <see cref="NativeShapeHandle"/> that takes ownership of the given pointer.
        /// Used when native code returns shape handles via IntPtr arrays.
        /// </summary>
        internal static NativeShapeHandle FromIntPtr(IntPtr ptr)
        {
            return new NativeShapeHandle(ptr);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_shape_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
