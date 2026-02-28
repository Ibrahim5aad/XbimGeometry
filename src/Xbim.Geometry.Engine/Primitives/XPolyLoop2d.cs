using System.Collections;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Primitives
{
    /// <summary>
    /// A closed loop of 2D points forming a polygon ring.
    /// </summary>
    internal class XPolyLoop2d : IXPolyLoop2d
    {
        private readonly IXPoint[] _points;

        internal XPolyLoop2d(IXPoint[] points)
        {
            _points = points ?? throw new ArgumentNullException(nameof(points));
        }

        public int NbPoints => _points.Length;

        public IXWire BuildWire(double zDim = 0)
        {
            if (_points.Length == 0)
                return null;

            var coords = new double[_points.Length * 3];
            for (int i = 0; i < _points.Length; i++)
            {
                coords[i * 3] = _points[i].X;
                coords[i * 3 + 1] = _points[i].Y;
                coords[i * 3 + 2] = zDim;
            }

            int result = XbimGeometryNativeApi.xbim_wire_build_polyline(
                NativeContextHandle.NullHandle,
                coords, _points.Length,
                out var wireHandle);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build wire from poly loop: {XbimGeometryNativeApi.GetLastError()}");
            return new XbimWire(wireHandle);
        }

        public IEnumerator<IXPoint> GetEnumerator()
        {
            return ((IEnumerable<IXPoint>)_points).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
