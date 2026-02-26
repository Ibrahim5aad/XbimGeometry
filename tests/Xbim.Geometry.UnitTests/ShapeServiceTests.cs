using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

public class ShapeServiceTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly ShapeService _shapeService;

    public ShapeServiceTests()
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

    [Fact]
    public void CanScaleShape()
    {
        var solidService = _service.SolidFactory;
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(
            radius: 30, innerRadius: 15, startParam: 0, endParam: 100);
        var solid = (IXSolid)solidService.Build(ifcSweptDisk);
        var scale = 0.001;

        Assert.False(solid.IsEmptyShape());
        Assert.True(solid.IsValidShape());

        var moved = _shapeService.Scaled(solid, scale);

        moved.Should().NotBeNull();
        var volDiff = Math.Abs(moved.As<IXSolid>().Volume - solid.Volume * Math.Pow(scale, 3));
        volDiff.Should().BeLessThan(Math.Pow(scale, 3));
    }

    [Fact]
    public void CanUnionShapes()
    {
        var solidService = _service.SolidFactory;
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(
            radius: 30, innerRadius: 15, startParam: 0, endParam: 60);
        var ifcSweptDisk2 = IfcMoq.SweptDiskSolidParametric(
            radius: 20, innerRadius: 5, startParam: 50, endParam: 100);
        var solid1 = (IXSolid)solidService.Build(ifcSweptDisk);
        var solid2 = (IXSolid)solidService.Build(ifcSweptDisk2);

        var unionShape = _shapeService.Union(solid1, solid2, _service.Precision);

        unionShape.Should().NotBeNull();
    }

    [Fact]
    public void CanCutShapes()
    {
        var solidService = _service.SolidFactory;
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(
            radius: 30, innerRadius: 15, startParam: 0, endParam: 60);
        var ifcSweptDisk2 = IfcMoq.SweptDiskSolidParametric(
            radius: 30, innerRadius: 15, startParam: 50, endParam: 100);
        var solid1 = (IXSolid)solidService.Build(ifcSweptDisk);
        var solid2 = (IXSolid)solidService.Build(ifcSweptDisk2);

        var cutShape = _shapeService.Cut(solid1, solid2, _service.Precision);

        cutShape.Should().NotBeNull();
    }

    [Fact]
    public void CanIntersectShapes()
    {
        var solidService = _service.SolidFactory;
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(
            radius: 30, innerRadius: 10, startParam: 0, endParam: 60);
        var block = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var solid1 = (IXSolid)solidService.Build(ifcSweptDisk);
        var solid2 = solidService.Build(block);

        var intersectShape = _shapeService.Intersect(solid1, solid2, _service.Precision);

        intersectShape.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(Transformations))]
    public void CanTransformShape(IXMatrix mat, double volFactor, IIfcBlock ifcBlock)
    {
        var solidService = _service.SolidFactory;
        var solid = solidService.Build(ifcBlock);

        solid.IsEmptyShape().Should().BeFalse();
        solid.IsValidShape().Should().BeTrue();

        var transformed = _shapeService.Transform(solid, mat) as IXSolid;

        transformed.Should().NotBeNull();
        transformed.IsEmptyShape().Should().BeFalse();
        transformed.IsValidShape().Should().BeTrue();
        transformed.Volume.Should().BeApproximately(solid.Volume * volFactor, 0.001);
    }

    [Fact]
    public void CanTransformShape_WithIfcTransformationOperator3D()
    {
        double scale = 2.3;
        var transformationOperator = IfcMoq.CartesianTransformationOperator3d(scale, 33, 0, 0);
        var mat = _service.GeometryFactory.BuildTransform(transformationOperator);
        var solidService = _service.SolidFactory;
        var ifcSweptDisk = IfcMoq.ExtrudedAreaSolid(depth: 500);

        var solid = (IXSolid)solidService.Build(ifcSweptDisk);

        var transformed = _shapeService.Transform(solid, mat) as IXSolid;
        transformed.Should().NotBeNull();
        transformed.IsEmptyShape().Should().BeFalse();
        transformed.IsValidShape().Should().BeTrue();
        transformed.Volume.Should().BeApproximately(solid.Volume * Math.Pow(scale, 3), 0.001);
    }

    [Fact]
    public void CanTransformShape_WithIfcTransformationOperator3DnonUniform()
    {
        double scale1 = 0.5, scale2 = 1.5, scale3 = 0.1;
        var transformationOperator = IfcMoq.CartesianTransformationOperator3dNonUniform(scale1, scale2, scale3);
        var mat = _service.GeometryFactory.BuildTransform(transformationOperator);
        var solidService = _service.SolidFactory;

        var solid = solidService.Build(IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10));

        var transformed = _shapeService.Transform(solid, mat) as IXSolid;
        transformed.Should().NotBeNull();
        transformed.IsEmptyShape().Should().BeFalse();
        transformed.IsValidShape().Should().BeTrue();
        transformed.Volume.Should().BeApproximately(solid.Volume * scale1 * scale2 * scale3, 0.001);
    }

    #region Helpers

    public static IEnumerable<object[]> Transformations
    {
        get
        {
            var result = new List<object[]>();

            var simpleTranslation = new double[] { 1, 0, 0, 2,
                                                   0, 1, 0, 3,
                                                   0, 0, 1, 1,
                                                   0, 0, 0, 1 };

            var angle = 30 * Math.PI / 180;
            var rotationAroundXWithTranslation = new double[]
                { 1,  0,                0,                 2.1,
                  0,  Math.Cos(angle), -Math.Sin(angle),   7.3,
                  0,  Math.Sin(angle),  Math.Cos(angle),   4.7,
                  0,  0,                0,                 1    };

            var nonUniformScaling = new double[] { 1,   0,   0,   3,
                                                   0,   1,   0,   3,
                                                   0,   0,   1,   3,
                                                   0.5, 1.5, 0.1, 1 };

            result.Add(new object[] { ToMatrix(simpleTranslation), 1, IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10) });
            result.Add(new object[] { ToMatrix(rotationAroundXWithTranslation), 1, IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10) });
            result.Add(new object[] { ToMatrix(nonUniformScaling), 0.075, IfcMoq.Block(xLen: 10, yLen: 4, zLen: 3) });

            return result;
        }
    }

    private static IXMatrix ToMatrix(double[] values)
    {
        var matrix = new Mock<IXMatrix>();
        matrix.Setup(m => m.Values).Returns(values);
        matrix.Setup(m => m.M11).Returns(values[0]);
        matrix.Setup(m => m.M12).Returns(values[1]);
        matrix.Setup(m => m.M13).Returns(values[2]);

        matrix.Setup(m => m.M21).Returns(values[4]);
        matrix.Setup(m => m.M22).Returns(values[5]);
        matrix.Setup(m => m.M23).Returns(values[6]);

        matrix.Setup(m => m.M31).Returns(values[8]);
        matrix.Setup(m => m.M32).Returns(values[9]);
        matrix.Setup(m => m.M33).Returns(values[10]);

        matrix.Setup(m => m.M44).Returns(values[15]);

        matrix.Setup(m => m.ScaleX).Returns(values[12]);
        matrix.Setup(m => m.ScaleY).Returns(values[13]);
        matrix.Setup(m => m.ScaleZ).Returns(values[14]);

        matrix.Setup(m => m.OffsetX).Returns(values[3]);
        matrix.Setup(m => m.OffsetY).Returns(values[7]);
        matrix.Setup(m => m.OffsetZ).Returns(values[11]);

        return matrix.Object;
    }

    #endregion
}
