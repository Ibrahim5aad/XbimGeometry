using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 3D B-spline curve with continuity, periodicity, and rationality info.
    /// </summary>
    internal class XbimBSplineCurve3d : XbimBoundedCurve3d, IXBSplineCurve
    {
        public XGeometricContinuity Continuity { get; }
        public bool IsPeriodic { get; }
        public bool IsRational { get; }

        internal XbimBSplineCurve3d(NativeCurveHandle handle, XCurveType curveType,
            XGeometricContinuity continuity, bool isPeriodic, bool isRational)
            : base(handle, curveType)
        {
            Continuity = continuity;
            IsPeriodic = isPeriodic;
            IsRational = isRational;
        }
    }
}
