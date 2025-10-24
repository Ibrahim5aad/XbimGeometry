using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native 2D curve handle (Geom2d_Curve) as an <see cref="IXCurve"/>.
    /// Point evaluation returns XY coordinates with Z=0.
    /// </summary>
    internal class Curve2d : NativeOwner<NativeCurve2dHandle>, IXCurve
    {
        private readonly XCurveType _curveType;

        internal Curve2d(NativeCurve2dHandle handle, XCurveType curveType) : base(handle)
        {
            _curveType = curveType;
        }

        public XCurveType CurveType => _curveType;

        public bool Is3d => false;

        public double Length
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_curve2d_length(Handle, out double length);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to compute 2D curve length: {XbimGeometryNativeApi.GetLastError()}");
                return length;
            }
        }

        public double FirstParameter
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_curve2d_parameters(Handle, out double first, out _);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get 2D curve parameters: {XbimGeometryNativeApi.GetLastError()}");
                return first;
            }
        }

        public double LastParameter
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_curve2d_parameters(Handle, out _, out double last);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get 2D curve parameters: {XbimGeometryNativeApi.GetLastError()}");
                return last;
            }
        }

        public IXPoint GetPoint(double uParam)
        {
            int result = XbimGeometryNativeApi.xbim_curve2d_value(
                Handle, uParam, out double x, out double y);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to evaluate 2D curve at u={uParam}: {XbimGeometryNativeApi.GetLastError()}");
            return new XPoint(x, y, 0);
        }

        public IXPoint GetFirstDerivative(double uParam, out IXDirection direction)
        {
            int result = XbimGeometryNativeApi.xbim_curve2d_d1(
                Handle, uParam,
                out double px, out double py,
                out double dx, out double dy);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to evaluate 2D curve D1 at u={uParam}: {XbimGeometryNativeApi.GetLastError()}");

            double mag = Math.Sqrt(dx * dx + dy * dy);
            if (mag > 1e-15)
                direction = new XDirection(dx / mag, dy / mag, 0);
            else
                direction = new XDirection(1, 0, 0);

            return new XPoint(px, py, 0);
        }

        public IXPoint GetSecondDerivative(double uParam, out IXDirection direction, out IXDirection normal)
        {
            int result = XbimGeometryNativeApi.xbim_curve2d_d2(
                Handle, uParam,
                out double px, out double py,
                out double d1x, out double d1y,
                out double d2x, out double d2y);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to evaluate 2D curve D2 at u={uParam}: {XbimGeometryNativeApi.GetLastError()}");

            double mag1 = Math.Sqrt(d1x * d1x + d1y * d1y);
            if (mag1 > 1e-15)
                direction = new XDirection(d1x / mag1, d1y / mag1, 0);
            else
                direction = new XDirection(1, 0, 0);

            double mag2 = Math.Sqrt(d2x * d2x + d2y * d2y);
            if (mag2 > 1e-15)
                normal = new XDirection(d2x / mag2, d2y / mag2, 0);
            else
                normal = new XDirection(0, 1, 0);

            return new XPoint(px, py, 0);
        }
    }
}
