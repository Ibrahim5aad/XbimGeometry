using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class FacetedBrepTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    public FacetedBrepTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(FacetedBrepTests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    public void Dispose() => _service.Dispose();

    private void SaveBrep(IXShape shape, string name)
    {
        #if DEBUG
        if (shape is Shape ns)
        {
            var path = Path.Combine(_brepOutputDir, $"{name}.brep");
            ns.WriteBrep(path);
        }
        #endif
    }

    [Fact]
    public void FacetedBrep_Box_ProducesValidSolid()
    {
        // Arrange: a 10x20x30 box as a faceted BRep
        var shell = IfcMoq.BoxShell(0, 0, 0, 10, 20, 30);
        var brep = IfcMoq.FacetedBrep(shell);

        // Act
        var shape = _solidFactory.Build(brep);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "FacetedBrep_Box");

        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(10 * 20 * 30, 1.0);
    }

    [Fact]
    public void FacetedBrep_Box_HasCorrectTopology()
    {
        // Arrange
        var shell = IfcMoq.BoxShell(0, 0, 0, 5, 5, 5);
        var brep = IfcMoq.FacetedBrep(shell);

        // Act
        var shape = _solidFactory.Build(brep);

        // Assert: a box should have 6 faces
        var faces = shape.AllFaces().ToList();
        faces.Should().HaveCount(6);
    }

    [Fact]
    public void FacetedBrep_Box_IsClosed()
    {
        // Arrange
        var shell = IfcMoq.BoxShell(0, 0, 0, 10, 10, 10);
        var brep = IfcMoq.FacetedBrep(shell);

        // Act
        var shape = _solidFactory.Build(brep);

        // Assert
        shape.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void FacetedBrep_Tetrahedron_ProducesValidSolid()
    {
        // Arrange: a regular tetrahedron (4 triangular faces)
        var v0 = (0.0, 0.0, 0.0);
        var v1 = (10.0, 0.0, 0.0);
        var v2 = (5.0, 10.0, 0.0);
        var v3 = (5.0, 5.0, 10.0);

        // 4 faces with outward-facing normals
        var f0 = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(v0, v2, v1)));    // bottom
        var f1 = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(v0, v1, v3)));    // front
        var f2 = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(v1, v2, v3)));    // right
        var f3 = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(v2, v0, v3)));    // left

        var shell = IfcMoq.ClosedShell(f0, f1, f2, f3);
        var brep = IfcMoq.FacetedBrep(shell);

        // Act
        var shape = _solidFactory.Build(brep);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "FacetedBrep_Tetrahedron");

        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeGreaterThan(0);

        // Tetrahedron should have 4 faces
        shape.AllFaces().Should().HaveCount(4);
    }

    [Fact]
    public void FacetedBrep_ViaSolidModelDispatch_Works()
    {
        // Arrange: use Build(IIfcSolidModel) dispatch path
        var shell = IfcMoq.BoxShell(0, 0, 0, 10, 10, 10);
        var brep = IfcMoq.FacetedBrep(shell);

        // Act — route through the IIfcSolidModel switch
        var shape = _solidFactory.Build((IIfcSolidModel)brep);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
    }

    [Fact]
    public void FacetedBrep_Box_BoundingBoxIsCorrect()
    {
        // Arrange: box from (0,0,0) to (10,20,30)
        var shell = IfcMoq.BoxShell(0, 0, 0, 10, 20, 30);
        var brep = IfcMoq.FacetedBrep(shell);

        // Act
        var shape = _solidFactory.Build(brep);
        var box = shape.Bounds();

        // Assert: bounding box should match the box dimensions
        box.Should().NotBeNull();
        box.LenX.Should().BeApproximately(10, 0.1);
        box.LenY.Should().BeApproximately(20, 0.1);
        box.LenZ.Should().BeApproximately(30, 0.1);
    }

    [Fact]
    public void FacetedBrepWithVoids_BoxWithInnerVoid_ProducesReducedVolume()
    {
        // Arrange: outer box 20x20x20, inner void box 5x5x5 centered at (7.5,7.5,7.5)
        var outerShell = IfcMoq.BoxShell(0, 0, 0, 20, 20, 20);
        var voidShell = IfcMoq.BoxShell(5, 5, 5, 15, 15, 15);
        var brep = IfcMoq.FacetedBrepWithVoids(outerShell, voidShell);

        // Act
        var shape = _solidFactory.Build((IIfcSolidModel)brep);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "FacetedBrepWithVoids_Box");

        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        double outerVol = 20 * 20 * 20;
        double voidVol = 10 * 10 * 10;
        solid.Volume.Should().BeApproximately(outerVol - voidVol, 1.0);
    }
}
