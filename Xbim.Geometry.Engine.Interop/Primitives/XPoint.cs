using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    internal readonly struct XPoint : IXPoint
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public bool Is3d { get; }

        public XPoint(double x, double y)
        {
            X = x;
            Y = y;
            Z = 0;
            Is3d = false;
        }

        public XPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
            Is3d = true;
        }
    }
}
