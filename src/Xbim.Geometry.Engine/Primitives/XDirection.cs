using System;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Primitives
{
    internal readonly struct XDirection : IXDirection
    {
        public double X { get; }
        public double Y { get; }
        private readonly double _z;
        public double Z => Is3d ? _z : throw new InvalidOperationException("Z is not defined for a 2D direction.");
        public bool Is3d { get; }
        public bool IsNull { get; }

        public XDirection(double x, double y)
        {
            double mag = Math.Sqrt(x * x + y * y);
            if (mag < 1e-15)
            {
                X = double.NaN;
                Y = double.NaN;
                _z = 0;
                Is3d = false;
                IsNull = true;
            }
            else
            {
                X = x / mag;
                Y = y / mag;
                _z = 0;
                Is3d = false;
                IsNull = false;
            }
        }

        public XDirection(double x, double y, double z)
        {
            double mag = Math.Sqrt(x * x + y * y + z * z);
            if (mag < 1e-15)
            {
                X = double.NaN;
                Y = double.NaN;
                _z = double.NaN;
                Is3d = true;
                IsNull = true;
            }
            else
            {
                X = x / mag;
                Y = y / mag;
                _z = z / mag;
                Is3d = true;
                IsNull = false;
            }
        }
    }
}
