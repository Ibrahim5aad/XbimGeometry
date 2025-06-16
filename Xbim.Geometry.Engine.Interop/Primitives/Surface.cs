using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native surface handle (Geom_Surface) as an <see cref="IXSurface"/>.
    /// Provides access to surface type and periodicity information.
    /// </summary>
    internal class Surface : IXSurface, IDisposable
    {
        private NativeSurfaceHandle _handle;
        private readonly XSurfaceType _surfaceType;

        internal Surface(NativeSurfaceHandle handle, XSurfaceType surfaceType)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
            _surfaceType = surfaceType;
        }

        internal NativeSurfaceHandle Handle =>
            _handle ?? throw new ObjectDisposedException(nameof(Surface));

        public XSurfaceType SurfaceType => _surfaceType;

        public bool IsUPeriodic => false; // TODO: query from native when available

        public bool IsVPeriodic => false; // TODO: query from native when available

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle?.Dispose();
            _handle = null!;
        }

        #endregion
    }

    /// <summary>
    /// Wraps a native surface handle as an <see cref="IXPlane"/> with origin, axis, and reference direction.
    /// </summary>
    internal class Plane : Surface, IXPlane
    {
        public IXPoint Location { get; }
        public IXDirection Axis { get; }
        public IXDirection RefDirection { get; }

        internal Plane(NativeSurfaceHandle handle, IXPoint location, IXDirection axis, IXDirection refDirection)
            : base(handle, XSurfaceType.IfcPlane)
        {
            Location = location;
            Axis = axis;
            RefDirection = refDirection;
        }
    }
}
