using System;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    internal readonly struct XVector : IXVector
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public bool Is3d { get; }
        public bool IsNull { get; }
        public double Magnitude { get; }

        public XVector(double x, double y)
        {
            Magnitude = Math.Sqrt(x * x + y * y);
            Is3d = false;
            IsNull = Magnitude < 1e-15;
            if (!IsNull) { X = x / Magnitude; Y = y / Magnitude; Z = 0; }
            else { X = 0; Y = 0; Z = 0; }
        }

        public XVector(double x, double y, double z)
        {
            Magnitude = Math.Sqrt(x * x + y * y + z * z);
            Is3d = true;
            IsNull = Magnitude < 1e-15;
            if (!IsNull) { X = x / Magnitude; Y = y / Magnitude; Z = z / Magnitude; }
            else { X = 0; Y = 0; Z = 0; }
        }
    }
}
