using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 3D ellipse curve with semi-axes and axis placement.
    /// </summary>
    internal class Ellipse3d : Curve, IXEllipse
    {
        public double MajorRadius { get; }
        public double MinorRadius { get; }
        public IXAxisPlacement Position { get; }

        internal Ellipse3d(NativeCurveHandle handle, double majorRadius, double minorRadius, IXAxisPlacement position)
            : base(handle, XCurveType.IfcEllipse)
        {
            MajorRadius = majorRadius;
            MinorRadius = minorRadius;
            Position = position;
        }
    }
}
