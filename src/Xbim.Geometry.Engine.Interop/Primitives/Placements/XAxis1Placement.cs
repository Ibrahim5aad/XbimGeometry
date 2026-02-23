using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    internal readonly struct XAxis1Placement : IXAxis1Placement
    {
        public IXPoint Location { get; }
        public IXDirection Direction { get; }

        public XAxis1Placement(IXPoint location, IXDirection direction)
        {
            Location = location;
            Direction = direction;
        }
    }
}
