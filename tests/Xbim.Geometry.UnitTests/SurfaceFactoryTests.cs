using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Factories;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xbim.Ifc4;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

public class SurfaceFactoryTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    public SurfaceFactoryTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
    }

    public void Dispose() => _loggerFactory.Dispose();

    private ModelGeometryService CreateService()
    {
        return new ModelGeometryService(new MemoryModel(new EntityFactoryIfc4x1()), _loggerFactory);
    }

    [Fact]
    public void Can_build_curve_bounded_surface_with_explicit_outer()
    {
        using var modelService = CreateService();
        var surfaceFactory = (SurfaceFactory)modelService.SurfaceFactory;

        // 10x20 rectangle on the XY plane
        var plane = IfcMoq.PlaneSurface();
        var outer = IfcMoq.RectangularBoundaryCurve<IIfcOuterBoundaryCurve>(w: 10, h: 20);
        var cbs = IfcMoq.CurveBoundedSurface(plane, implicitOuter: false, outer);

        var result = surfaceFactory.BuildCurveBoundedSurface(cbs);

        result.Should().NotBeNull();
        result.SurfaceType.Should().Be(XSurfaceType.IfcCurveBoundedSurface);

        // Wrap as face to check area
        var face = (IXFace)NativeShapeWrapper.WrapFace(result.Handle);
        face.Area.Should().BeApproximately(200, 1e-3);
    }

    [Fact]
    public void Can_build_curve_bounded_surface_with_inner_hole()
    {
        using var modelService = CreateService();
        var surfaceFactory = (SurfaceFactory)modelService.SurfaceFactory;

        // 10x20 outer, 4x4 inner hole at (3,3,0)
        var plane = IfcMoq.PlaneSurface();
        var outer = IfcMoq.RectangularBoundaryCurve<IIfcOuterBoundaryCurve>(w: 10, h: 20);
        var inner = IfcMoq.RectangularBoundaryCurve<IIfcBoundaryCurve>(w: 4, h: 4, ox: 3, oy: 3);
        var cbs = IfcMoq.CurveBoundedSurface(plane, implicitOuter: false, outer, inner);

        var result = surfaceFactory.BuildCurveBoundedSurface(cbs);

        result.Should().NotBeNull();

        var face = (IXFace)NativeShapeWrapper.WrapFace(result.Handle);
        face.Area.Should().BeApproximately(200 - 16, 1e-3);
    }

    [Fact]
    public void Can_build_curve_bounded_surface_brep_output()
    {
        using var modelService = CreateService();
        var surfaceFactory = (SurfaceFactory)modelService.SurfaceFactory;

        var plane = IfcMoq.PlaneSurface();
        var outer = IfcMoq.RectangularBoundaryCurve<IIfcOuterBoundaryCurve>(w: 10, h: 20);
        var cbs = IfcMoq.CurveBoundedSurface(plane, implicitOuter: false, outer);

        var result = surfaceFactory.BuildCurveBoundedSurface(cbs);

        var brep = result.BrepString();
        brep.Should().NotBeNullOrWhiteSpace();
    }
}
