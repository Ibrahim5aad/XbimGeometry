using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class AdvancedBrepTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    public AdvancedBrepTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(AdvancedBrepTests).Assembly.Location)!,
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
    public void AdvancedBrep_Box_ProducesValidSolid()
    {
        // Arrange: a 10x20x30 box as an Advanced BRep with planar faces + line edges
        var shell = IfcMoq.AdvancedBoxShell(0, 0, 0, 10, 20, 30);
        var brep = IfcMoq.AdvancedBrep(shell);

        // Act
        var shape = _solidFactory.Build(brep);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "AdvancedBrep_Box");

        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(10 * 20 * 30, 10.0);
    }

    [Fact]
    public void AdvancedBrep_Box_HasCorrectTopology()
    {
        // Arrange
        var shell = IfcMoq.AdvancedBoxShell(0, 0, 0, 5, 5, 5);
        var brep = IfcMoq.AdvancedBrep(shell);

        // Act
        var shape = _solidFactory.Build(brep);

        // Assert: a box should have 6 faces
        shape.Should().NotBeNull();
        var faces = shape.AllFaces().ToList();
        faces.Should().HaveCount(6);
    }

    [Fact]
    public void AdvancedBrep_Box_IsClosed()
    {
        // Arrange
        var shell = IfcMoq.AdvancedBoxShell(0, 0, 0, 10, 10, 10);
        var brep = IfcMoq.AdvancedBrep(shell);

        // Act
        var shape = _solidFactory.Build(brep);

        // Assert
        shape.Should().NotBeNull();
        shape.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void AdvancedBrep_Box_BoundingBoxIsCorrect()
    {
        // Arrange: box from (0,0,0) to (10,20,30)
        var shell = IfcMoq.AdvancedBoxShell(0, 0, 0, 10, 20, 30);
        var brep = IfcMoq.AdvancedBrep(shell);

        // Act
        var shape = _solidFactory.Build(brep);
        var box = shape.Bounds();

        // Assert: bounding box should match the box dimensions
        box.Should().NotBeNull();
        box.LenX.Should().BeApproximately(10, 0.5);
        box.LenY.Should().BeApproximately(20, 0.5);
        box.LenZ.Should().BeApproximately(30, 0.5);
    }
}
