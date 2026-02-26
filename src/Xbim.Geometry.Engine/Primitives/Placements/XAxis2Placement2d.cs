using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Primitives
{
    internal readonly struct XAxis2Placement2d : IXAxis2Placement2d
    {
        public IXPoint Location { get; }
        public IXDirection Direction { get; }
        public IXDirection YDirection { get; }

        public XAxis2Placement2d(IXPoint location, IXDirection xDirection)
        {
            Location = location;
            Direction = xDirection;
            // Y direction is perpendicular to X in 2D: (-y, x)
            YDirection = new XDirection(-xDirection.Y, xDirection.X);
        }
    }
}
