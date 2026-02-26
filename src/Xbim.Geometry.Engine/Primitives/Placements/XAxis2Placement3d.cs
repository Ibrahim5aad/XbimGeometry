using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Primitives
{
    internal readonly struct XAxis2Placement3d : IXAxis2Placement3d
    {
        public IXAxis1Placement Axis { get; }
        public IXDirection XDirection { get; }
        public IXDirection YDirection { get; }

        public XAxis2Placement3d(IXPoint location, IXDirection zDirection, IXDirection xDirection)
        {
            Axis = new XAxis1Placement(location, zDirection);
            XDirection = xDirection;
            // Y = Z cross X (right-hand rule)
            double yx = zDirection.Y * xDirection.Z - zDirection.Z * xDirection.Y;
            double yy = zDirection.Z * xDirection.X - zDirection.X * xDirection.Z;
            double yz = zDirection.X * xDirection.Y - zDirection.Y * xDirection.X;
            YDirection = new XDirection(yx, yy, yz);
        }
    }
}
