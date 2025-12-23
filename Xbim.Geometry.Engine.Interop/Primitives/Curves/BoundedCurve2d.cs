using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 2D bounded curve (polyline, composite, indexed poly curve)
    /// with start and end points evaluated from the parametric range.
    /// </summary>
    internal class BoundedCurve2d : Curve2d, IXBoundedCurve
    {
        public IXPoint StartPoint => GetPoint(FirstParameter);
        public IXPoint EndPoint => GetPoint(LastParameter);

        internal BoundedCurve2d(NativeCurve2dHandle handle, XCurveType curveType)
            : base(handle, curveType)
        {
        }
    }
}
