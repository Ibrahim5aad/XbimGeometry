using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 3D bounded curve (polyline, composite, indexed poly curve)
    /// with start and end points evaluated from the parametric range.
    /// </summary>
    internal class BoundedCurve3d : Curve, IXBoundedCurve
    {
        public IXPoint StartPoint => GetPoint(FirstParameter);
        public IXPoint EndPoint => GetPoint(LastParameter);

        internal BoundedCurve3d(NativeCurveHandle handle, XCurveType curveType)
            : base(handle, curveType)
        {
        }
    }
}
