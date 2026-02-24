using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class ShapeProximityTests : IDisposable
{
    private readonly ShapeService _shapeService;
    private readonly ModelGeometryService _service;

    public ShapeProximityTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _shapeService = new ShapeService(loggerFactory);
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
    }

    public void Dispose()
    {
        _service.Dispose();
        _shapeService.Dispose();
    }

    [Fact(Skip = "Broken in OCC 7.8. IS investigating")]
    public void GivenTwoOverlappingShapes_IsOverlapping_ShouldReturnTrue()
    {
        var blockMoq = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var cylinderMoq = IfcMoq.Cylinder(radius: 20, height: 30);

        var block = _service.SolidFactory.Build(blockMoq);
        var cylinder = _service.SolidFactory.Build(cylinderMoq);
        var meshFactors = new Mock<IXMeshFactors>();
        meshFactors.Setup(s => s.OneMeter).Returns(1);
        meshFactors.Setup(s => s.Tolerance).Returns(0.001);
        meshFactors.Setup(s => s.AngularDeflection).Returns(0.26179938);
        meshFactors.Setup(s => s.LinearDefection).Returns(0.012);
        var result = _shapeService.IsOverlapping(block, cylinder, meshFactors.Object);

        result.Should().Be(true);
    }

    [Fact(Skip = "Broken in OCC 7.8. IS investigating")]
    public void GivenTwoNonOverlappingShapes_IsOverlapping_ShouldReturnFalse()
    {
        var blockMoq = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var cylinderMoq = IfcMoq.Cylinder(
            radius: 20, height: 30,
            position: IfcMoq.Axis2Placement3d(loc: IfcMoq.CartesianPoint3d(0, 0, 10000)));

        var block = _service.SolidFactory.Build(blockMoq);
        var cylinder = _service.SolidFactory.Build(cylinderMoq);
        var meshFactors = new Mock<IXMeshFactors>();
        meshFactors.Setup(s => s.OneMeter).Returns(1);
        meshFactors.Setup(s => s.Tolerance).Returns(0.001);
        meshFactors.Setup(s => s.AngularDeflection).Returns(0.26179938);
        meshFactors.Setup(s => s.LinearDefection).Returns(0.012);
        var result = _shapeService.IsOverlapping(block, cylinder, meshFactors.Object);

        result.Should().Be(false);
    }

    [Fact(Skip = "Broken in OCC 7.8. IS investigating")]
    public void GivenTwoTangentShapes_IsOverlapping_ShouldReturnFalse()
    {
        var block1Moq = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10,
            position: IfcMoq.Axis2Placement3d(loc: IfcMoq.CartesianPoint3d(10, 10, 0)));
        var block2Moq = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10,
            position: IfcMoq.Axis2Placement3d(loc: IfcMoq.CartesianPoint3d(10, 0, 0)));

        var block1 = _service.SolidFactory.Build(block1Moq);
        var block2 = _service.SolidFactory.Build(block2Moq);
        var meshFactors = new Mock<IXMeshFactors>();
        meshFactors.Setup(s => s.OneMeter).Returns(1);
        meshFactors.Setup(s => s.Tolerance).Returns(0);
        meshFactors.Setup(s => s.AngularDeflection).Returns(0.26179938);
        meshFactors.Setup(s => s.LinearDefection).Returns(0.012);

        var result = _shapeService.IsOverlapping(block1, block2, meshFactors.Object);

        result.Should().Be(false);
    }
}
