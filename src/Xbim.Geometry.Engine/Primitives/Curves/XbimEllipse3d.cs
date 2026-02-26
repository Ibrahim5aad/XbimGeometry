using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;

namespace Xbim.Geometry.Engine.Primitives
{
    /// <summary>
    /// Wraps a native 3D ellipse curve with semi-axes and axis placement.
    /// </summary>
    internal class XbimEllipse3d : XbimCurve, IXEllipse
    {
        public double MajorRadius { get; }
        public double MinorRadius { get; }
        public IXAxisPlacement Position { get; }

        internal XbimEllipse3d(NativeCurveHandle handle, double majorRadius, double minorRadius, IXAxisPlacement position)
            : base(handle, XCurveType.IfcEllipse)
        {
            MajorRadius = majorRadius;
            MinorRadius = minorRadius;
            Position = position;
        }
    }
}
