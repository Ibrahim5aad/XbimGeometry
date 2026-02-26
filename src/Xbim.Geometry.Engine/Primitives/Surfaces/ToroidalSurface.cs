using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;

namespace Xbim.Geometry.Engine.Primitives;

internal class ToroidalSurface : Surface, IXToroidalSurface
{
    internal ToroidalSurface(NativeSurfaceHandle handle, double radius)
        : base(handle, XSurfaceType.IfcToroidalSurface)
    {
        Radius = radius;
    }

    public double Radius { get; }

    public IXAxis2Placement3d Position { get; internal set; }
}
