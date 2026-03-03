using System;
using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimMeshHandle.
    /// Automatically calls xbim_mesh_free when the handle is released.
    /// </summary>
    internal sealed class NativeMeshHandle : SafeHandle
    {
        static NativeMeshHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeMeshHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            NativeHandleMethods.xbim_mesh_free(handle);
            return true;
        }
    }
}
