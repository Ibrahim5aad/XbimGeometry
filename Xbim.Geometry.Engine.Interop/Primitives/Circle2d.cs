using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 2D circle curve with radius and 2D axis placement.
    /// </summary>
    internal class Circle2d : Curve2d, IXCircle
    {
        public double Radius { get; }
        public IXAxisPlacement Position { get; }

        internal Circle2d(NativeCurve2dHandle handle, double radius, IXAxisPlacement position)
            : base(handle, XCurveType.IfcCircle)
        {
            Radius = radius;
            Position = position;
        }
    }
}
