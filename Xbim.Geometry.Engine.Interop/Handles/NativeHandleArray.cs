using System.Runtime.InteropServices;

namespace Xbim.Geometry.Engine.Interop.Handles
{
    /// <summary>
    /// Safely extracts raw pointers from an array of <see cref="SafeHandle"/> instances
    /// for use in P/Invoke calls that accept handle arrays.
    /// Increments the reference count on each handle to prevent premature release,
    /// and decrements on dispose.
    /// </summary>
    internal ref struct NativeHandleArray
    {
        private readonly SafeHandle[] _handles;
        private readonly IntPtr[] _ptrs;
        private int _refCount;

        public NativeHandleArray(SafeHandle[] handles)
        {
            _handles = handles;
            _ptrs = new IntPtr[handles.Length];
            _refCount = 0;

            for (int i = 0; i < handles.Length; i++)
            {
                bool success = false;
                handles[i].DangerousAddRef(ref success);
                if (!success)
                {
                    Dispose();
                    throw new ObjectDisposedException(handles[i].GetType().Name);
                }
                _ptrs[i] = handles[i].DangerousGetHandle();
                _refCount++;
            }
        }

        /// <summary>
        /// The raw pointer array suitable for passing to native code.
        /// </summary>
        public IntPtr[] Ptrs => _ptrs;

        /// <summary>
        /// Number of handles in the array.
        /// </summary>
        public int Length => _ptrs.Length;

        public void Dispose()
        {
            for (int i = 0; i < _refCount; i++)
                _handles[i].DangerousRelease();
            _refCount = 0;
        }
    }
}
