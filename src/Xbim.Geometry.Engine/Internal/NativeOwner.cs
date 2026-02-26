using System;
using System.Runtime.InteropServices;

namespace Xbim.Geometry.Engine.Internal
{
    /// <summary>
    /// Base class for managed wrappers that own a single native <see cref="SafeHandle"/>.
    /// Provides handle access, ownership transfer, and deterministic disposal.
    /// </summary>
    /// <typeparam name="THandle">The concrete <see cref="SafeHandle"/> subclass.</typeparam>
    internal abstract class NativeOwner<THandle> : IDisposable where THandle : SafeHandle
    {
        private THandle? _handle;
        private bool _disposed;

        protected NativeOwner(THandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        /// <summary>
        /// The underlying native handle. Throws if the object has been disposed or detached.
        /// </summary>
        internal THandle Handle => _handle ?? throw new ObjectDisposedException(GetType().Name);

        /// <summary>
        /// Transfers ownership of the native handle to the caller.
        /// After this call, <see cref="Handle"/> throws and <see cref="Dispose()"/> is a no-op.
        /// </summary>
        internal THandle DetachHandle()
        {
            var h = _handle ?? throw new ObjectDisposedException(GetType().Name);
            _handle = null;
            _disposed = true;
            return h;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _handle?.Dispose();
                _handle = null;
            }
        }
    }
}
