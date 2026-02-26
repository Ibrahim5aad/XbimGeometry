using System;
using System.Collections;
using System.Collections.Generic;
using Xbim.Geometry.Abstractions;

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
            throw new NotSupportedException(
                "Wire construction from footprint poly loops is not supported.");
        }

        public IEnumerator<IXPoint> GetEnumerator()
        {
            return ((IEnumerable<IXPoint>)_points).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
