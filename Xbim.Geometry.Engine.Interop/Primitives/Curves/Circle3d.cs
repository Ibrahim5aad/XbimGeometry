using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 3D circle curve with radius and axis placement.
    /// </summary>
    internal class Circle3d : Curve, IXCircle
    {
        public double Radius { get; }
        public IXAxisPlacement Position { get; }

        internal Circle3d(NativeCurveHandle handle, double radius, IXAxisPlacement position)
            : base(handle, XCurveType.IfcCircle)
        {
            Radius = radius;
            Position = position;
        }
    }
}
