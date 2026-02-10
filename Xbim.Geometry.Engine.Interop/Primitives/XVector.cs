using System;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    internal readonly struct XVector : IXVector
    {
        public double X { get; }
        public double Y { get; }
        private readonly double _z;
        public double Z => Is3d ? _z : throw new InvalidOperationException("Z is not defined for a 2D vector.");
        public bool Is3d { get; }
        public bool IsNull { get; }
        public double Magnitude { get; }

        public XVector(double x, double y)
        {
            Magnitude = Math.Sqrt(x * x + y * y);
            Is3d = false;
            IsNull = Magnitude < 1e-15;
            if (!IsNull) { X = x / Magnitude; Y = y / Magnitude; _z = 0; }
            else { X = 0; Y = 0; _z = 0; }
        }

        public XVector(double x, double y, double z)
        {
            Magnitude = Math.Sqrt(x * x + y * y + z * z);
            Is3d = true;
            IsNull = Magnitude < 1e-15;
            if (!IsNull) { X = x / Magnitude; Y = y / Magnitude; _z = z / Magnitude; }
            else { X = 0; Y = 0; _z = 0; }
        }

        private XVector(double x, double y, double z, bool is3d, double magnitude)
        {
            X = x; Y = y; _z = z;
            Is3d = is3d;
            Magnitude = magnitude;
            IsNull = Math.Abs(magnitude) < 1e-15;
        }

        /// <summary>
        /// Creates a 2D vector with a pre-normalized direction and explicit magnitude.
        /// </summary>
        internal static XVector Create2d(double normalizedX, double normalizedY, double magnitude)
            => new XVector(normalizedX, normalizedY, 0, false, magnitude);

        /// <summary>
        /// Creates a 3D vector with a pre-normalized direction and explicit magnitude.
        /// </summary>
        internal static XVector Create3d(double normalizedX, double normalizedY, double normalizedZ, double magnitude)
            => new XVector(normalizedX, normalizedY, normalizedZ, true, magnitude);
    }
}
