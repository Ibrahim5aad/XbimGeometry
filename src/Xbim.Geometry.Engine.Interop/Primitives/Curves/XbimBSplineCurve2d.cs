using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 2D B-spline curve with continuity, periodicity, and rationality info.
    /// </summary>
    internal class XbimBSplineCurve2d : XbimBoundedCurve2d, IXBSplineCurve
    {
        public XGeometricContinuity Continuity { get; }
        public bool IsPeriodic { get; }
        public bool IsRational { get; }

        internal XbimBSplineCurve2d(NativeCurve2dHandle handle, XCurveType curveType,
            XGeometricContinuity continuity, bool isPeriodic, bool isRational)
            : base(handle, curveType)
        {
            Continuity = continuity;
            IsPeriodic = isPeriodic;
            IsRational = isRational;
        }
    }
}
