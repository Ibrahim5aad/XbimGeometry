using System;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    internal readonly struct XDirection : IXDirection
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public bool Is3d { get; }
        public bool IsNull { get; }

        public XDirection(double x, double y)
        {
            double mag = Math.Sqrt(x * x + y * y);
            if (mag < 1e-15)
            {
                X = 0;
                Y = 0;
                Z = 0;
                Is3d = false;
                IsNull = true;
            }
            else
            {
                X = x / mag;
                Y = y / mag;
                Z = 0;
                Is3d = false;
                IsNull = false;
            }
        }

        public XDirection(double x, double y, double z)
        {
            double mag = Math.Sqrt(x * x + y * y + z * z);
            if (mag < 1e-15)
            {
                X = 0;
                Y = 0;
                Z = 0;
                Is3d = true;
                IsNull = true;
            }
            else
            {
                X = x / mag;
                Y = y / mag;
                Z = z / mag;
                Is3d = true;
                IsNull = false;
            }
        }
    }
}
