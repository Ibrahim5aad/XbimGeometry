using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class ShellBasedSurfaceModelTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    public ShellBasedSurfaceModelTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(ShellBasedSurfaceModelTests).Assembly.Location)!,
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
    public void ShellBasedSurfaceModel_SingleClosedShell_ProducesValidShape()
    {
        // Arrange: a closed shell (box)
        var closedShell = IfcMoq.BoxShell(0, 0, 0, 10, 20, 30);
        var model = IfcMoq.ShellBasedSurfaceModel(closedShell);

        // Act
        var shape = _solidFactory.Build(model);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "ShellBasedSurface_SingleClosedShell");
    }

    [Fact]
    public void ShellBasedSurfaceModel_SingleClosedShell_HasCorrectFaceCount()
    {
        var closedShell = IfcMoq.BoxShell(0, 0, 0, 5, 5, 5);
        var model = IfcMoq.ShellBasedSurfaceModel(closedShell);

        var shape = _solidFactory.Build(model);

        shape.AllFaces().Should().HaveCount(6);
    }

    [Fact]
    public void ShellBasedSurfaceModel_SingleClosedShell_BoundingBoxIsCorrect()
    {
        var closedShell = IfcMoq.BoxShell(0, 0, 0, 10, 20, 30);
        var model = IfcMoq.ShellBasedSurfaceModel(closedShell);

        var shape = _solidFactory.Build(model);
        var box = shape.Bounds();

        box.Should().NotBeNull();
        box.LenX.Should().BeApproximately(10, 0.5);
        box.LenY.Should().BeApproximately(20, 0.5);
        box.LenZ.Should().BeApproximately(30, 0.5);
    }

    [Fact]
    public void ShellBasedSurfaceModel_SingleOpenShell_ProducesValidShape()
    {
        // Arrange: an open shell (3 faces forming an L-shape, not a closed volume)
        var face1 = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (0, 0, 0), (10, 0, 0), (10, 10, 0), (0, 10, 0))));
        var face2 = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (0, 0, 0), (0, 10, 0), (0, 10, 5), (0, 0, 5))));
        var face3 = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (0, 0, 0), (0, 0, 5), (10, 0, 5), (10, 0, 0))));

        var openShell = IfcMoq.OpenShell(face1, face2, face3);
        var model = IfcMoq.ShellBasedSurfaceModel(openShell);

        // Act
        var shape = _solidFactory.Build(model);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "ShellBasedSurface_SingleOpenShell");
    }

    [Fact]
    public void ShellBasedSurfaceModel_MultipleShells_ProducesCompound()
    {
        // Arrange: two separate closed shells
        var shell1 = IfcMoq.BoxShell(0, 0, 0, 10, 10, 10);
        var shell2 = IfcMoq.BoxShell(20, 0, 0, 30, 10, 10);
        var model = IfcMoq.ShellBasedSurfaceModel(shell1, shell2);

        // Act
        var shape = _solidFactory.Build(model);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        shape.ShapeType.Should().Be(XShapeType.Compound);
        SaveBrep(shape, "ShellBasedSurface_TwoClosedShells");
    }

    [Fact]
    public void ShellBasedSurfaceModel_MixedShells_ProducesCompound()
    {
        // Arrange: one closed shell (box) and one open shell (two faces)
        var closedShell = IfcMoq.BoxShell(0, 0, 0, 10, 10, 10);

        var openFace1 = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (20, 0, 0), (30, 0, 0), (30, 10, 0), (20, 10, 0))));
        var openFace2 = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (20, 0, 0), (20, 10, 0), (20, 10, 5), (20, 0, 5))));
        var openShell = IfcMoq.OpenShell(openFace1, openFace2);

        var model = IfcMoq.ShellBasedSurfaceModel(closedShell, openShell);

        // Act
        var shape = _solidFactory.Build(model);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        shape.ShapeType.Should().Be(XShapeType.Compound);
        SaveBrep(shape, "ShellBasedSurface_MixedShells");
    }

    [Fact]
    public void ShellBasedSurfaceModel_MultipleShells_BoundingBoxSpansBoth()
    {
        var shell1 = IfcMoq.BoxShell(0, 0, 0, 10, 10, 10);
        var shell2 = IfcMoq.BoxShell(50, 0, 0, 60, 10, 10);
        var model = IfcMoq.ShellBasedSurfaceModel(shell1, shell2);

        var shape = _solidFactory.Build(model);
        var box = shape.Bounds();

        box.Should().NotBeNull();
        box.LenX.Should().BeApproximately(60, 1.0);
        box.LenY.Should().BeApproximately(10, 0.5);
        box.LenZ.Should().BeApproximately(10, 0.5);
    }
}
