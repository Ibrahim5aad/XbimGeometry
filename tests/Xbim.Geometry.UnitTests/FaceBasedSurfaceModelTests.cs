using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

public class FaceBasedSurfaceModelTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    public FaceBasedSurfaceModelTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(FaceBasedSurfaceModelTests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    public void Dispose() => _service.Dispose();

    private void SaveBrep(IXShape shape, string name)
    {
#if DEBUG
        var path = Path.Combine(_brepOutputDir, $"{name}.brep");
        shape.WriteBrep(path);
#endif
    }

    [Fact]
    public void FaceBasedSurfaceModel_SingleBoxShell_ProducesValidShape()
    {
        // Arrange: single box as a connected face set
        var shell = IfcMoq.BoxShell(0, 0, 0, 10, 20, 30);
        var model = IfcMoq.FaceBasedSurfaceModel(shell);

        // Act
        var shape = _solidFactory.Build(model);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "FaceBasedSurface_SingleBox");
    }

    [Fact]
    public void FaceBasedSurfaceModel_SingleBoxShell_HasCorrectFaceCount()
    {
        // Arrange
        var shell = IfcMoq.BoxShell(0, 0, 0, 5, 5, 5);
        var model = IfcMoq.FaceBasedSurfaceModel(shell);

        // Act
        var shape = _solidFactory.Build(model);

        // Assert: a box has 6 faces
        shape.AllFaces().Should().HaveCount(6);
    }

    [Fact]
    public void FaceBasedSurfaceModel_MultipleShells_ProducesCompound()
    {
        // Arrange: two separate box shells
        var shell1 = IfcMoq.ConnectedFaceSet(MakeBoxFaces(0, 0, 0, 10, 10, 10));
        var shell2 = IfcMoq.ConnectedFaceSet(MakeBoxFaces(20, 0, 0, 30, 10, 10));
        var model = IfcMoq.FaceBasedSurfaceModel(shell1, shell2);

        // Act
        var shape = _solidFactory.Build(model);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        shape.ShapeType.Should().Be(XShapeType.Compound);
        SaveBrep(shape, "FaceBasedSurface_TwoBoxes");
    }

    [Fact]
    public void FaceBasedSurfaceModel_SingleBoxShell_BoundingBoxIsCorrect()
    {
        // Arrange
        var shell = IfcMoq.BoxShell(0, 0, 0, 10, 20, 30);
        var model = IfcMoq.FaceBasedSurfaceModel(shell);

        // Act
        var shape = _solidFactory.Build(model);
        var box = shape.Bounds();

        // Assert
        box.Should().NotBeNull();
        box.LenX.Should().BeApproximately(10, 0.5);
        box.LenY.Should().BeApproximately(20, 0.5);
        box.LenZ.Should().BeApproximately(30, 0.5);
    }

    [Fact]
    public void FaceBasedSurfaceModel_MultipleShells_BoundingBoxSpansBoth()
    {
        // Arrange: two boxes, one at origin, one offset by 50 in X
        var shell1 = IfcMoq.ConnectedFaceSet(MakeBoxFaces(0, 0, 0, 10, 10, 10));
        var shell2 = IfcMoq.ConnectedFaceSet(MakeBoxFaces(50, 0, 0, 60, 10, 10));
        var model = IfcMoq.FaceBasedSurfaceModel(shell1, shell2);

        // Act
        var shape = _solidFactory.Build(model);
        var box = shape.Bounds();

        // Assert: compound bounding box should span from 0 to 60 in X
        box.Should().NotBeNull();
        box.LenX.Should().BeApproximately(60, 1.0);
        box.LenY.Should().BeApproximately(10, 0.5);
        box.LenZ.Should().BeApproximately(10, 0.5);
    }

    /// <summary>
    /// Helper: creates 6 box faces as an array suitable for ConnectedFaceSet.
    /// </summary>
    private static IIfcFace[] MakeBoxFaces(
        double x0, double y0, double z0,
        double x1, double y1, double z1)
    {
        var bottom = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0))));
        var top = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))));
        var front = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1))));
        var back = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0))));
        var left = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0))));
        var right = IfcMoq.Face(IfcMoq.FaceOuterBound(IfcMoq.PolyLoop(
            (x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1))));

        return new[] { bottom, top, front, back, left, right };
    }
}
