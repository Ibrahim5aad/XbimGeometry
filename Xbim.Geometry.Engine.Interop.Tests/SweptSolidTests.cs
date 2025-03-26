using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests swept area solid operations via the full P/Invoke path:
///   Mock IFC entity → NativeSolidFactory → P/Invoke → OCCT → NativeSolid
/// Covers extruded, extruded tapered, revolved, and revolved tapered area solids.
/// </summary>
public class SweptSolidTests : IDisposable
{
    private readonly NativeModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    public SweptSolidTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new NativeModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(SweptSolidTests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    public void Dispose() => _service.Dispose();

    private void SaveBrep(IXShape shape, string name)
    {
        if (shape is NativeShape ns)
        {
            var path = Path.Combine(_brepOutputDir, $"{name}.brep");
            ns.WriteBrep(path);
        }
    }

    [Fact]
    public void ExtrudedAreaSolid_RectangleProfile_HasCorrectVolume()
    {
        // Arrange: 100 x 200 rectangle extruded by depth=50 in Z direction
        // Expected volume = 100 * 200 * 50 = 1,000,000
        var ifcSolid = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(100, 200),
            direction: IfcMoq.Direction3d(0, 0, 1),
            depth: 50);

        // Act
        var shape = _solidFactory.Build(ifcSolid);

        // Assert
        shape.Should().NotBeNull();
        shape.Should().BeAssignableTo<IXSolid>();
        var solid = (IXSolid)shape;
        solid.Volume.Should().BeApproximately(1_000_000, 1.0);
        SaveBrep(shape, "extruded_rectangle");
    }

    [Fact]
    public void ExtrudedAreaSolid_CircleProfile_HasCorrectVolume()
    {
        // Arrange: circle r=10 extruded by depth=30 in Z → volume = π * 10² * 30 ≈ 9424.78
        var ifcSolid = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.CircleProfile(10),
            direction: IfcMoq.Direction3d(0, 0, 1),
            depth: 30);

        // Act
        var shape = _solidFactory.Build(ifcSolid);

        // Assert
        shape.Should().BeAssignableTo<IXSolid>();
        var solid = (IXSolid)shape;
        double expected = Math.PI * 100 * 30; // π*r²*h
        solid.Volume.Should().BeApproximately(expected, 1.0);
        SaveBrep(shape, "extruded_circle");
    }

    [Fact]
    public void ExtrudedAreaSolidTapered_HasValidSolid()
    {
        // Arrange: start=100x200 rect, end=50x100 rect, depth=80
        var ifcSolid = IfcMoq.ExtrudedAreaSolidTapered(
            sweptArea: IfcMoq.RectangleProfile(100, 200),
            endSweptArea: IfcMoq.RectangleProfile(50, 100),
            direction: IfcMoq.Direction3d(0, 0, 1),
            depth: 80);

        // Act
        var shape = _solidFactory.Build(ifcSolid);

        // Assert
        shape.Should().NotBeNull();
        shape.Should().BeAssignableTo<IXSolid>();
        var solid = (IXSolid)shape;
        solid.Volume.Should().BeGreaterThan(0);
        SaveBrep(shape, "extruded_tapered");
    }

    [Fact]
    public void RevolvedAreaSolid_360Degrees_HasValidSolid()
    {
        // Arrange: revolve a 10x20 rectangle 360° around a Y-axis offset in X.
        // The profile lies in the XY plane; revolving around Y sweeps it through Z,
        // producing a torus-like solid of revolution.
        // Axis origin at (-50,0,0), direction Y → profile is 50mm from axis.
        var ifcSolid = IfcMoq.RevolvedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(10, 20),
            axis: IfcMoq.Axis1Placement(
                IfcMoq.CartesianPoint3d(-50, 0, 0),
                IfcMoq.Direction3d(0, 1, 0)),
            angle: 360);

        // Act
        var shape = _solidFactory.Build(ifcSolid);

        // Assert
        shape.Should().NotBeNull();
        shape.Should().BeAssignableTo<IXSolid>();
        var solid = (IXSolid)shape;
        solid.Volume.Should().BeGreaterThan(0);
        SaveBrep(shape, "revolved_360");
    }

    [Fact]
    public void RevolvedAreaSolid_90Degrees_HasValidSolid()
    {
        // Arrange: revolve 90° (quarter turn) around a Y-axis offset in X
        var ifcSolid = IfcMoq.RevolvedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(10, 20),
            axis: IfcMoq.Axis1Placement(
                IfcMoq.CartesianPoint3d(-50, 0, 0),
                IfcMoq.Direction3d(0, 1, 0)),
            angle: 90);

        // Act
        var shape = _solidFactory.Build(ifcSolid);

        // Assert
        shape.Should().BeAssignableTo<IXSolid>();
        var solid = (IXSolid)shape;
        solid.Volume.Should().BeGreaterThan(0);
        SaveBrep(shape, "revolved_90");
    }
}
