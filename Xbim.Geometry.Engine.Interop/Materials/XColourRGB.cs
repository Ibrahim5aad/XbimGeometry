using System;
using System.Collections.Generic;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Interop.Materials
{
    /// <summary>
    /// Represents an RGB colour with components in the 0.0 to 1.0 range.
    /// </summary>
    internal readonly struct XColourRGB : IXColourRGB
    {
        public double Red { get; }
        public double Green { get; }
        public double Blue { get; }

        public XColourRGB(double red, double green, double blue)
        {
            Red = Math.Clamp(red, 0.0, 1.0);
            Green = Math.Clamp(green, 0.0, 1.0);
            Blue = Math.Clamp(blue, 0.0, 1.0);
        }

        public bool Equals(IXColourRGB? other)
        {
            if (other is null) return false;
            return Math.Abs(Red - other.Red) < 1e-6
                && Math.Abs(Green - other.Green) < 1e-6
                && Math.Abs(Blue - other.Blue) < 1e-6;
        }

        public bool Equals(IXColourRGB? x, IXColourRGB? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return Math.Abs(x.Red - y.Red) < 1e-6
                && Math.Abs(x.Green - y.Green) < 1e-6
                && Math.Abs(x.Blue - y.Blue) < 1e-6;
        }

        public int GetHashCode(IXColourRGB obj)
        {
            return HashCode.Combine(
                (int)(obj.Red * 255),
                (int)(obj.Green * 255),
                (int)(obj.Blue * 255));
        }

        public override bool Equals(object? obj)
        {
            return obj is IXColourRGB other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)(Red * 255), (int)(Green * 255), (int)(Blue * 255));
        }
    }
}
