using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;

namespace Xbim.Geometry.Engine.Primitives
{
    /// <summary>
    /// Wraps a native 3D bounded curve (polyline, composite, indexed poly curve)
    /// with start and end points evaluated from the parametric range.
    /// </summary>
    internal class XbimBoundedCurve3d : XbimCurve, IXBoundedCurve
    {
        public IXPoint StartPoint => GetPoint(FirstParameter);
        public IXPoint EndPoint => GetPoint(LastParameter);

        internal XbimBoundedCurve3d(NativeCurveHandle handle, XCurveType curveType)
            : base(handle, curveType)
        {
        }
    }
}
