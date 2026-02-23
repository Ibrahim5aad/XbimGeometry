using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 2D trimmed curve with a reference to its basis curve.
    /// Owns the basis curve and disposes it when this instance is disposed.
    /// </summary>
    internal class XbimTrimmedCurve2d : XbimBoundedCurve2d, IXTrimmedCurve
    {
        private readonly IDisposable? _basisOwned;

        public IXCurve BasisCurve { get; }

        internal XbimTrimmedCurve2d(NativeCurve2dHandle handle, IXCurve basisCurve)
            : base(handle, XCurveType.IfcTrimmedCurve)
        {
            BasisCurve = basisCurve;
            _basisOwned = basisCurve;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _basisOwned?.Dispose();
            base.Dispose(disposing);
        }
    }
}
