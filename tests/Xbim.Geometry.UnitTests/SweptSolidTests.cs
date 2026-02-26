using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

/// <summary>
/// Tests swept area solid operations via the full P/Invoke path:
///   Mock IFC entity → SolidFactory → P/Invoke → OCCT → Solid
/// Covers extruded, extruded tapered, revolved, revolved tapered, and swept disk area solids.
/// </summary>
public class SweptSolidTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    public SweptSolidTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(SweptSolidTests).Assembly.Location)!,
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

    // ── Extruded area solid tests ────────────────────────────────────

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
    public void ExtrudedAreaSolid_SmallDepth_VolumeMatchesAreaTimesDepth()
    {
        // Arrange: 10 x 20 rectangle, depth=10 → volume = 10 * 20 * 10 = 2000
        var ifcSolid = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(10, 20),
            direction: IfcMoq.Direction3d(0, 0, 1),
            depth: 10);

        var shape = _solidFactory.Build(ifcSolid);

        var solid = (IXSolid)shape;
        solid.Volume.Should().BeApproximately(2000, 0.1);
    }

    [Fact]
    public void ExtrudedAreaSolid_IsValid()
    {
        var ifcSolid = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(50, 80),
            direction: IfcMoq.Direction3d(0, 0, 1),
            depth: 25);

        var shape = _solidFactory.Build(ifcSolid);

        shape.IsValidShape().Should().BeTrue();
    }

    [Fact]
    public void ExtrudedAreaSolid_IsClosed()
    {
        var ifcSolid = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(50, 80),
            direction: IfcMoq.Direction3d(0, 0, 1),
            depth: 25);

        var shape = _solidFactory.Build(ifcSolid);

        shape.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void ExtrudedAreaSolid_HasCorrectBoundingBox()
    {
        // Arrange: 40 x 60 rectangle extruded by 100 in Z
        var ifcSolid = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(40, 60),
            direction: IfcMoq.Direction3d(0, 0, 1),
            depth: 100);

        var shape = _solidFactory.Build(ifcSolid);
        var bounds = shape.Bounds();

        bounds.LenX.Should().BeApproximately(40, 0.1);
        bounds.LenY.Should().BeApproximately(60, 0.1);
        bounds.LenZ.Should().BeApproximately(100, 0.1);
    }

    // ── Extruded area solid tapered tests ────────────────────────────

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
    public void ExtrudedAreaSolidTapered_IsValid()
    {
        var ifcSolid = IfcMoq.ExtrudedAreaSolidTapered(
            sweptArea: IfcMoq.RectangleProfile(100, 200),
            endSweptArea: IfcMoq.RectangleProfile(50, 100),
            direction: IfcMoq.Direction3d(0, 0, 1),
            depth: 80);

        var shape = _solidFactory.Build(ifcSolid);

        shape.IsValidShape().Should().BeTrue();
        shape.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void ExtrudedAreaSolidTapered_VolumeIsBetweenStartAndEndAreas()
    {
        // Start area = 100*200 = 20000, End area = 50*100 = 5000, depth=80
        // Volume of frustum should be between min(A)*h = 400000 and max(A)*h = 1600000
        var ifcSolid = IfcMoq.ExtrudedAreaSolidTapered(
            sweptArea: IfcMoq.RectangleProfile(100, 200),
            endSweptArea: IfcMoq.RectangleProfile(50, 100),
            direction: IfcMoq.Direction3d(0, 0, 1),
            depth: 80);

        var shape = _solidFactory.Build(ifcSolid);
        var solid = (IXSolid)shape;

        solid.Volume.Should().BeGreaterThan(5000 * 80);    // min area * depth
        solid.Volume.Should().BeLessThan(20000 * 80);      // max area * depth
    }

    // ── Revolved area solid tests ────────────────────────────────────

    [Fact]
    public void RevolvedAreaSolid_360Degrees_HasValidSolid()
    {
        // Arrange: revolve a 10x20 rectangle 360° around a Y-axis offset in X.
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
    public void RevolvedAreaSolid_360Degrees_VolumeByPappusTheorem()
    {
        // Pappus' centroid theorem: V = 2π * R * A
        // where R = distance from axis to profile centroid, A = profile area.
        // Profile: 10 x 20 rectangle at origin → centroid at (0, 0).
        // Axis: origin at (-50,0,0), direction Y. Distance from centroid to axis = 50.
        // A = 10 * 20 = 200
        // V = 2π * 50 * 200 = 20000π ≈ 62831.85
        var ifcSolid = IfcMoq.RevolvedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(10, 20),
            axis: IfcMoq.Axis1Placement(
                IfcMoq.CartesianPoint3d(-50, 0, 0),
                IfcMoq.Direction3d(0, 1, 0)),
            angle: 360);

        var shape = _solidFactory.Build(ifcSolid);
        var solid = (IXSolid)shape;

        double expectedVolume = 2 * Math.PI * 50 * (10 * 20);
        solid.Volume.Should().BeApproximately(expectedVolume, expectedVolume * 0.01);
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

    [Fact]
    public void RevolvedAreaSolid_90Degrees_IsQuarterOfFull()
    {
        // 90° revolve should be approximately 1/4 of a full 360° revolve
        var axis = IfcMoq.Axis1Placement(
            IfcMoq.CartesianPoint3d(-50, 0, 0),
            IfcMoq.Direction3d(0, 1, 0));

        var fullSolid = IfcMoq.RevolvedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(10, 20),
            axis: axis, angle: 360);
        var quarterSolid = IfcMoq.RevolvedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(10, 20),
            axis: axis, angle: 90);

        var fullShape = (IXSolid)_solidFactory.Build(fullSolid);
        var quarterShape = (IXSolid)_solidFactory.Build(quarterSolid);

        double expectedQuarter = fullShape.Volume / 4.0;
        quarterShape.Volume.Should().BeApproximately(expectedQuarter, expectedQuarter * 0.01);
    }

    [Fact]
    public void RevolvedAreaSolid_IsValidAndClosed()
    {
        var ifcSolid = IfcMoq.RevolvedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(10, 20),
            axis: IfcMoq.Axis1Placement(
                IfcMoq.CartesianPoint3d(-50, 0, 0),
                IfcMoq.Direction3d(0, 1, 0)),
            angle: 360);

        var shape = _solidFactory.Build(ifcSolid);

        shape.IsValidShape().Should().BeTrue();
        shape.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void RevolvedAreaSolid_CircleProfile_ProducesTorus()
    {
        // Revolving a circle around an offset axis produces a torus
        // Torus volume = 2π²Rr² where R = major radius, r = minor radius
        // Profile: circle r=5, axis at (-30, 0, 0), dir Y → R=30, r=5
        // V = 2π² * 30 * 25 ≈ 14804.41
        var ifcSolid = IfcMoq.RevolvedAreaSolid(
            sweptArea: IfcMoq.CircleProfile(5),
            axis: IfcMoq.Axis1Placement(
                IfcMoq.CartesianPoint3d(-30, 0, 0),
                IfcMoq.Direction3d(0, 1, 0)),
            angle: 360);

        var shape = _solidFactory.Build(ifcSolid);
        var solid = (IXSolid)shape;

        double expectedVolume = 2 * Math.PI * Math.PI * 30 * 25;
        solid.Volume.Should().BeApproximately(expectedVolume, expectedVolume * 0.01);
        SaveBrep(shape, "revolved_torus");
    }

    // ── Revolved area solid tapered tests ────────────────────────────

    [Fact]
    public void RevolvedAreaSolidTapered_90Degrees_HasPositiveVolume()
    {
        // Use a moderate taper and 90° sweep to stay within OCCT pipe constraints.
        // Profile centroid must be offset from axis to give a non-zero revolution radius.
        var ifcSolid = IfcMoq.RevolvedAreaSolidTapered(
            sweptArea: IfcMoq.RectangleProfile(10, 20),
            endSweptArea: IfcMoq.RectangleProfile(8, 16),
            axis: IfcMoq.Axis1Placement(
                IfcMoq.CartesianPoint3d(-50, 0, 0),
                IfcMoq.Direction3d(0, 1, 0)),
            angle: 90);

        var shape = _solidFactory.Build(ifcSolid);

        shape.Should().NotBeNull();
        shape.Should().BeAssignableTo<IXSolid>();
        var solid = (IXSolid)shape;
        solid.Volume.Should().BeGreaterThan(0);
        SaveBrep(shape, "revolved_tapered_90");
    }

    // ── Swept disk solid tests ───────────────────────────────────────

    [Fact]
    public void SweptDiskSolid_StraightLine_ProducesCylindricalShape()
    {
        // Sweep a disk of radius 10 along a straight line of length 100
        // IFC parametric length = param * magnitude, so magnitude=1, trim 0→100 = 100 units
        var line = IfcMoq.IfcLine(0, 0, 0, 1, 0, 0, 1);
        var trimmedLine = IfcMoq.IfcTrimmedCurve3d(line, 0, 100);
        var ifcSolid = IfcMoq.SweptDiskSolid(radius: 10, directrix: trimmedLine);

        var shape = _solidFactory.Build(ifcSolid) as IXSolid;

        shape.Should().NotBeNull();
        // Volume = π * R² * L = π * 100 * 100
        shape.Volume.Should().BeApproximately(Math.PI * 100 * 100, 10);
        SaveBrep(shape, "swept_disk_straight");
    }

    [Fact]
    public void SweptDiskSolid_WithInnerRadius_ProducesHollowPipe()
    {
        // Sweep a hollow disk (outer=10, inner=5) along a straight line of length 100
        var line = IfcMoq.IfcLine(0, 0, 0, 1, 0, 0, 1);
        var trimmedLine = IfcMoq.IfcTrimmedCurve3d(line, 0, 100);
        var ifcSolid = IfcMoq.SweptDiskSolid(radius: 10, innerRadius: 5, directrix: trimmedLine);

        var shape = _solidFactory.Build(ifcSolid) as IXSolid;

        shape.Should().NotBeNull();
        double expectedVol = Math.PI * 100 * (100 - 25); // π * L * (R² - r²)
        shape.Volume.Should().BeApproximately(expectedVol, 50);
        SaveBrep(shape, "swept_disk_hollow");
    }

    // ── Dispose tests ────────────────────────────────────────────────

    [Fact]
    public void AllSweptSolids_DisposeProperly()
    {
        var extruded = _solidFactory.Build(IfcMoq.ExtrudedAreaSolid());
        var extrudedTapered = _solidFactory.Build(IfcMoq.ExtrudedAreaSolidTapered());
        var revolved = _solidFactory.Build(IfcMoq.RevolvedAreaSolid());

        extruded.Should().BeAssignableTo<IDisposable>();
        extruded.Dispose();
        extrudedTapered.Dispose();
        revolved.Dispose();
    }
}
