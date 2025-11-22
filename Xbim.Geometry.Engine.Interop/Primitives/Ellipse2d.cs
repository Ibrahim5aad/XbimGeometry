using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 2D ellipse curve with semi-axes and 2D axis placement.
    /// </summary>
    internal class Ellipse2d : Curve2d, IXEllipse
    {
        public double MajorRadius { get; }
        public double MinorRadius { get; }
        public IXAxisPlacement Position { get; }

        internal Ellipse2d(NativeCurve2dHandle handle, double majorRadius, double minorRadius, IXAxisPlacement position)
            : base(handle, XCurveType.IfcEllipse)
        {
            MajorRadius = majorRadius;
            MinorRadius = minorRadius;
            Position = position;
        }
    }
}
