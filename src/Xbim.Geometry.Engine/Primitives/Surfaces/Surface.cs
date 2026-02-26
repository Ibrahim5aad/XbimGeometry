using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Primitives;

/// <summary>
/// Wraps a native surface handle (Geom_Surface) as an <see cref="IXSurface"/>.
/// Provides access to surface type and periodicity information.
/// </summary>
internal class Surface : NativeOwner<NativeSurfaceHandle>, IXSurface
{
    private readonly XSurfaceType _surfaceType;

    internal Surface(NativeSurfaceHandle handle, XSurfaceType surfaceType) : base(handle)
    {
        _surfaceType = surfaceType;
    }

    public XSurfaceType SurfaceType => _surfaceType;

    public bool IsUPeriodic => false; // TODO: query from native when available

    public bool IsVPeriodic => false; // TODO: query from native when available

    public string BrepString() =>
        throw new NotSupportedException(
            $"BRep export is not supported for elementary surface type {_surfaceType}.");

    public void WriteBrep(string filePath) =>
        throw new NotSupportedException(
            $"BRep export is not supported for elementary surface type {_surfaceType}.");

    public void WriteStl(string filePath) =>
        throw new NotSupportedException(
            $"STL export is not supported for elementary surface type {_surfaceType}.");
}

/// <summary>
/// Wraps a native face shape handle as an <see cref="IXSurface"/>, used for
/// bounded surfaces such as IfcCurveBoundedPlane that are represented as faces.
/// </summary>
internal sealed class FaceSurface : NativeOwner<NativeShapeHandle>, IXSurface
{
    internal FaceSurface(NativeShapeHandle handle, XSurfaceType surfaceType) : base(handle)
    {
        SurfaceType = surfaceType;
    }

    public XSurfaceType SurfaceType { get; }

    public bool IsUPeriodic => false;

    public bool IsVPeriodic => false;

    public string BrepString() =>
        throw new NotSupportedException(
            $"BRep export is not supported for bounded surface type {SurfaceType}.");

    public void WriteBrep(string filePath) =>
        throw new NotSupportedException(
            $"BRep export is not supported for bounded surface type {SurfaceType}.");

    public void WriteStl(string filePath) =>
        throw new NotSupportedException(
            $"STL export is not supported for bounded surface type {SurfaceType}.");
}

/// <summary>
/// Wraps a native surface handle as an <see cref="IXPlane"/> with origin, axis, and reference direction.
/// </summary>
internal class Plane : Surface, IXPlane
{
    public IXPoint Location { get; }
    public IXDirection Axis { get; }
    public IXDirection RefDirection { get; }

    internal Plane(NativeSurfaceHandle handle, IXPoint location, IXDirection axis, IXDirection refDirection)
        : base(handle, XSurfaceType.IfcPlane)
    {
        Location = location;
        Axis = axis;
        RefDirection = refDirection;
    }
}
