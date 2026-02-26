using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Primitives;

internal class SphericalSurface : Surface, IXSphericalSurface
{
    internal SphericalSurface(NativeSurfaceHandle handle, double radius)
        : base(handle, XSurfaceType.IfcSphericalSurface)
    {
        Radius = radius;
    }

    public double Radius { get; }

    public IXAxis2Placement3d Position { get; internal set; }
}
