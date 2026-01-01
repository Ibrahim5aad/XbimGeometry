using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests IfcSectionedSpine solid construction via the full P/Invoke path:
///   Mock IFC entity -> SolidFactory -> P/Invoke -> OCCT -> Solid
/// </summary>
public class SectionedSpineTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    public SectionedSpineTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(SectionedSpineTests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    public void Dispose() => _service.Dispose();

    private void SaveBrep(IXShape shape, string name)
    {
        #if DEBUG
        if (shape is XbimShape ns)
        {
            var path = Path.Combine(_brepOutputDir, $"{name}.brep");
            ns.WriteBrep(path);
        }
        #endif
    }

    [Fact]
    public void SectionedSpine_TwoRectangleSections_ProducesSolid()
    {
        // Arrange: straight spine from (0,0,0) to (0,0,100), two identical 20x10 rectangle sections
        var spine = IfcMoq.CompositeCurveFromPolyline(
            (0, 0, 0), (0, 0, 100));

        var profile1 = IfcMoq.RectangleProfile(20, 10);
        var profile2 = IfcMoq.RectangleProfile(20, 10);

        // Position sections at start and end of spine
        var pos1 = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 0));
        var pos2 = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 100));

        var sectionedSpine = IfcMoq.SectionedSpine(
            spine,
            new[] { profile1, profile2 },
            new[] { pos1, pos2 });

        // Act
        var shape = _solidFactory.Build(sectionedSpine);

        // Assert
        shape.Should().NotBeNull();
        shape.Should().BeAssignableTo<IXSolid>();

        var solid = (IXSolid)shape;
        // Uniform 20x10 rectangle swept 100 units = volume 20,000
        solid.Volume.Should().BeApproximately(20_000, 100);

        SaveBrep(shape, "sectioned_spine_two_rect");
    }

    [Fact]
    public void SectionedSpine_TaperedSections_ProducesSolid()
    {
        // Arrange: straight spine from origin to Z=100, tapering from 40x20 to 20x10
        var spine = IfcMoq.CompositeCurveFromPolyline(
            (0, 0, 0), (0, 0, 100));

        var profile1 = IfcMoq.RectangleProfile(40, 20);
        var profile2 = IfcMoq.RectangleProfile(20, 10);

        var pos1 = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 0));
        var pos2 = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 100));

        var sectionedSpine = IfcMoq.SectionedSpine(
            spine,
            new[] { profile1, profile2 },
            new[] { pos1, pos2 });

        // Act
        var shape = _solidFactory.Build(sectionedSpine);

        // Assert
        shape.Should().NotBeNull();
        shape.Should().BeAssignableTo<IXSolid>();

        var solid = (IXSolid)shape;
        // Tapered solid: volume should be between the smaller and larger prisms
        solid.Volume.Should().BeGreaterThan(10_000);   // 20*10*100 = 20,000 min if uniform small
        solid.Volume.Should().BeLessThan(100_000);     // 40*20*100 = 80,000 max if uniform large

        SaveBrep(shape, "sectioned_spine_tapered");
    }

    [Fact]
    public void SectionedSpine_ThreeSections_ProducesSolid()
    {
        // Arrange: spine from origin to Z=200, three sections at Z=0, Z=100, Z=200
        var spine = IfcMoq.CompositeCurveFromPolyline(
            (0, 0, 0), (0, 0, 200));

        var profile1 = IfcMoq.RectangleProfile(30, 15);
        var profile2 = IfcMoq.RectangleProfile(40, 20);
        var profile3 = IfcMoq.RectangleProfile(30, 15);

        var pos1 = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 0));
        var pos2 = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 100));
        var pos3 = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 200));

        var sectionedSpine = IfcMoq.SectionedSpine(
            spine,
            new[] { profile1, profile2, profile3 },
            new[] { pos1, pos2, pos3 });

        // Act
        var shape = _solidFactory.Build(sectionedSpine);

        // Assert
        shape.Should().NotBeNull();
        shape.Should().BeAssignableTo<IXSolid>();

        var solid = (IXSolid)shape;
        solid.Volume.Should().BeGreaterThan(50_000);

        SaveBrep(shape, "sectioned_spine_three_sections");
    }

    [Fact]
    public void SectionedSpine_CircularProfile_ProducesSolid()
    {
        // Arrange: straight spine with two circular cross-sections
        var spine = IfcMoq.CompositeCurveFromPolyline(
            (0, 0, 0), (0, 0, 50));

        var profile1 = IfcMoq.CircleProfile(25);
        var profile2 = IfcMoq.CircleProfile(25);

        var pos1 = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 0));
        var pos2 = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 50));

        var sectionedSpine = IfcMoq.SectionedSpine(
            spine,
            new[] { profile1, profile2 },
            new[] { pos1, pos2 });

        // Act
        var shape = _solidFactory.Build(sectionedSpine);

        // Assert
        shape.Should().NotBeNull();
        shape.Should().BeAssignableTo<IXSolid>();

        var solid = (IXSolid)shape;
        // Cylinder: pi * r^2 * h = pi * 625 * 50 ≈ 98,175
        solid.Volume.Should().BeApproximately(Math.PI * 625 * 50, 500);

        SaveBrep(shape, "sectioned_spine_circular");
    }

    [Fact]
    public void SectionedSpine_LessThanTwoSections_Throws()
    {
        var spine = IfcMoq.CompositeCurveFromPolyline(
            (0, 0, 0), (0, 0, 100));

        var profile1 = IfcMoq.RectangleProfile(20, 10);

        var pos1 = IfcMoq.Axis2Placement3d(
            loc: IfcMoq.CartesianPoint3d(0, 0, 0));

        var sectionedSpine = IfcMoq.SectionedSpine(
            spine,
            new[] { profile1 },
            new[] { pos1 });

        // Act & Assert
        var act = () => _solidFactory.Build(sectionedSpine);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 2*");
    }

    [Fact]
    public void SectionedSpine_MismatchedCounts_Throws()
    {
        var spine = IfcMoq.CompositeCurveFromPolyline(
            (0, 0, 0), (0, 0, 100));

        var profile1 = IfcMoq.RectangleProfile(20, 10);
        var profile2 = IfcMoq.RectangleProfile(20, 10);

        // Only one position for two sections
        var pos1 = IfcMoq.Axis2Placement3d(
            loc: IfcMoq.CartesianPoint3d(0, 0, 0));

        var sectionedSpine = IfcMoq.SectionedSpine(
            spine,
            new[] { profile1, profile2 },
            new[] { pos1 });

        // Act & Assert
        var act = () => _solidFactory.Build(sectionedSpine);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not match*");
    }
}
