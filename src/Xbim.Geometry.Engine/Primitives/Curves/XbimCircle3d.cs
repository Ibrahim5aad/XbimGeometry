using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;

namespace Xbim.Geometry.Engine.Primitives
{
    /// <summary>
    /// Wraps a native 3D circle curve with radius and axis placement.
    /// </summary>
    internal class XbimCircle3d : XbimCurve, IXCircle
    {
        public double Radius { get; }
        public IXAxisPlacement Position { get; }

        internal XbimCircle3d(NativeCurveHandle handle, double radius, IXAxisPlacement position)
            : base(handle, XCurveType.IfcCircle)
        {
            Radius = radius;
            Position = position;
        }
    }
}
