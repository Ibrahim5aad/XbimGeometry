using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;

namespace Xbim.Geometry.Engine.Primitives
{
    /// <summary>
    /// Wraps a native 2D circle curve with radius and 2D axis placement.
    /// </summary>
    internal class XbimCircle2d : XbimCurve2d, IXCircle
    {
        public double Radius { get; }
        public IXAxisPlacement Position { get; }

        internal XbimCircle2d(NativeCurve2dHandle handle, double radius, IXAxisPlacement position)
            : base(handle, XCurveType.IfcCircle)
        {
            Radius = radius;
            Position = position;
        }
    }
}
