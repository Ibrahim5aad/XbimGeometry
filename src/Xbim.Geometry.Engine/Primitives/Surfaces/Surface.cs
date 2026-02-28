using System.Runtime.InteropServices;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Exceptions;

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
        throw new XbimGeometryNotSupportedException(
            $"BRep export is not supported for elementary surface type {_surfaceType}.");

    public void WriteBrep(string filePath) =>
        throw new XbimGeometryNotSupportedException(
            $"BRep export is not supported for elementary surface type {_surfaceType}.");

    public void WriteStl(string filePath) =>
        throw new XbimGeometryNotSupportedException(
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

    public string BrepString()
    {
        int result = XbimGeometryNativeApi.xbim_shape_to_brep_string(
            Handle, out IntPtr strPtr, out int strLen);

        if (result != 0 || strPtr == IntPtr.Zero)
            throw new XbimGeometryServiceException(
                $"Failed to serialize surface to BRep: {XbimGeometryNativeApi.GetLastError()}");

        try
        {
            return Marshal.PtrToStringAnsi(strPtr, strLen);
        }
        finally
        {
            XbimGeometryNativeApi.xbim_string_free(strPtr);
        }
    }

    public void WriteBrep(string filePath)
    {
        int result = XbimGeometryNativeApi.xbim_shape_write_brep(Handle, filePath);
        if (result != 0)
            throw new XbimGeometryServiceException(
                $"Failed to write BRep file '{filePath}': {XbimGeometryNativeApi.GetLastError()}");
    }

    public void WriteStl(string filePath)
    {
        int result = XbimGeometryNativeApi.xbim_shape_write_stl(Handle, filePath, 0.1);
        if (result != 0)
            throw new XbimGeometryServiceException(
                $"Failed to write STL file '{filePath}': {XbimGeometryNativeApi.GetLastError()}");
    }
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
