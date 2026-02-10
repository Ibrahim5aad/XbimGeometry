using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Primitives;


namespace Xbim.Geometry.Engine.Interop.Primitives;

internal class CylindricalSurface : Surface, IXCylindricalSurface
{
    internal CylindricalSurface(NativeSurfaceHandle handle, double radius) : base(handle, XSurfaceType.IfcCylindricalSurface)
    {
        Radius = radius;
    }

    public double Radius { get; }

    public IXAxis2Placement3d Position { get; internal set; }
}