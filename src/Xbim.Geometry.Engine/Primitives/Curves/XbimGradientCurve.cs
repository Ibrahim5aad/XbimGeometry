using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Shapes;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 3D gradient curve combining a horizontal alignment
    /// with a vertical profile to produce a full 3D alignment curve.
    /// </summary>
    internal class XbimGradientCurve : XbimCurve, IXGradientCurve
    {
        private readonly NativeContextHandle _contextHandle;

        internal XbimGradientCurve(NativeCurveHandle handle, NativeContextHandle contextHandle)
            : base(handle, XCurveType.IfcGradientCurve)
        {
            _contextHandle = contextHandle;
        }

        public IXWire ToWire()
        {
            double u0 = FirstParameter;
            double u1 = LastParameter;
            double range = u1 - u0;

            // Sample at ~1 unit intervals, clamped to [20, 1000] points
            int numPoints = Math.Clamp((int)Math.Ceiling(range), 20, 1000);
            double step = range / (numPoints - 1);

            var pointsXYZ = new double[numPoints * 3];
            for (int i = 0; i < numPoints; i++)
            {
                double u = (i < numPoints - 1) ? u0 + i * step : u1;
                var pt = GetFirstDerivative(u, out var _);
                pointsXYZ[i * 3] = pt.X;
                pointsXYZ[i * 3 + 1] = pt.Y;
                pointsXYZ[i * 3 + 2] = pt.Z;
            }

            int result = XbimGeometryNativeApi.xbim_wire_build_polyline(
                _contextHandle, pointsXYZ, numPoints,
                out var wireHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build wire from gradient curve: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimWire(wireHandle);
        }
    }
}
