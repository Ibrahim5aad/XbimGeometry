using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 3D trimmed curve with a reference to its basis curve.
    /// Owns the basis curve and disposes it when this instance is disposed.
    /// </summary>
    internal class XbimTrimmedCurve3d : XbimBoundedCurve3d, IXTrimmedCurve
    {
        private readonly IDisposable? _basisOwned;

        public IXCurve BasisCurve { get; }

        internal XbimTrimmedCurve3d(NativeCurveHandle handle, IXCurve basisCurve)
            : base(handle, XCurveType.IfcTrimmedCurve)
        {
            BasisCurve = basisCurve;
            _basisOwned = basisCurve as IDisposable;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _basisOwned?.Dispose();
            base.Dispose(disposing);
        }
    }
}
