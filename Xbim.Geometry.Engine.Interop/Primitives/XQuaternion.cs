using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    internal readonly struct XQuaternion : IXQuaternion
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double W { get; }
        public bool Is3d => true;

        public XQuaternion(double w, double x, double y, double z)
        {
            W = w;
            X = x;
            Y = y;
            Z = z;
        }
    }
}
