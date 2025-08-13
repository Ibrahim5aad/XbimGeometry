using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native curve handle (Geom_Curve) as an <see cref="IXCurve"/>.
    /// Provides access to curve type, parametric range, and point evaluation.
    /// </summary>
    internal class Curve : IXCurve, IDisposable
    {
        private NativeCurveHandle _handle;
        private readonly XCurveType _curveType;

        internal Curve(NativeCurveHandle handle, XCurveType curveType)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
            _curveType = curveType;
        }

        internal NativeCurveHandle Handle =>
            _handle ?? throw new ObjectDisposedException(nameof(Curve));

        /// <summary>
        /// Transfers ownership of the native handle to the caller.
        /// After this call, Dispose() becomes a no-op.
        /// </summary>
        internal NativeCurveHandle DetachHandle()
        {
            var h = _handle ?? throw new ObjectDisposedException(nameof(Curve));
            _handle = null!;
            _disposed = true;
            return h;
        }

        public XCurveType CurveType => _curveType;

        public bool Is3d => true;

        public double Length =>
            throw new NotImplementedException("Curve length evaluation not yet available.");

        public double FirstParameter =>
            throw new NotImplementedException("Curve parameter queries not yet available.");

        public double LastParameter =>
            throw new NotImplementedException("Curve parameter queries not yet available.");

        public IXPoint GetPoint(double uParam) =>
            throw new NotImplementedException("Curve point evaluation not yet available.");

        public IXPoint GetFirstDerivative(double uParam, out IXDirection direction) =>
            throw new NotImplementedException("Curve derivative evaluation not yet available.");

        public IXPoint GetSecondDerivative(double uParam, out IXDirection direction, out IXDirection normal) =>
            throw new NotImplementedException("Curve second derivative evaluation not yet available.");

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
}
