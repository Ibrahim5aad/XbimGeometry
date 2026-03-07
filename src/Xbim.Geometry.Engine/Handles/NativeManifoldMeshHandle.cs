using System.Runtime.InteropServices;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Handles
{
    /// <summary>
    /// A <see cref="SafeHandle"/> wrapping the native XbimManifoldMeshHandle.
    /// Automatically calls xbim_manifold_mesh_destroy when the handle is released.
    /// </summary>
    internal sealed class NativeManifoldMeshHandle : SafeHandle
    {
        static NativeManifoldMeshHandle()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        public NativeManifoldMeshHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            int result = NativeHandleMethods.xbim_manifold_mesh_destroy(handle);
            return result == 0; // XBIM_OK
        }
    }
}
