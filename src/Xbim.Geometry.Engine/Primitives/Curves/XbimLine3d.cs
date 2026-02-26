using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;

namespace Xbim.Geometry.Engine.Primitives
{
    /// <summary>
    /// Wraps a native 3D line curve with origin, direction, and parametric unit.
    /// </summary>
    internal class XbimLine3d : XbimCurve, IXLine
    {
        public IXPoint Origin { get; }
        public IXVector Direction { get; }
        public double ParametricUnit { get; }

        internal XbimLine3d(NativeCurveHandle handle, IXPoint origin, IXVector direction, double parametricUnit)
            : base(handle, XCurveType.IfcLine)
        {
            Origin = origin;
            Direction = direction;
            ParametricUnit = parametricUnit;
        }
    }
}
