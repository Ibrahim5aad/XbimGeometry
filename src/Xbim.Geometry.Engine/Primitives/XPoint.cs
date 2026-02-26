using System;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Primitives
{
    internal readonly struct XPoint : IXPoint
    {
        public double X { get; }
        public double Y { get; }
        private readonly double _z;
        public double Z => Is3d ? _z : throw new InvalidOperationException("Z is not defined for a 2D point.");
        public bool Is3d { get; }

        public XPoint(double x, double y)
        {
            X = x;
            Y = y;
            _z = 0;
            Is3d = false;
        }

        public XPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            _z = z;
            Is3d = true;
        }
    }
}
