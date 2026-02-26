using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

public class ShapeBinarySerializerTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXShapeBinarySerializer _shapeBinarySerializer;

    public ShapeBinarySerializerTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _shapeBinarySerializer = _service.ShapeBinarySerializer;
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void CanSerializeShapeToBinary()
    {
        var solidFactory = _service.SolidFactory;
        var blockMoq = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var block = solidFactory.Build(blockMoq);

        var binary = _shapeBinarySerializer.ToArray(block);

        binary.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CanDeserializeShapeFromBinary()
    {
        var solidFactory = _service.SolidFactory;
        var blockMoq = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var block = solidFactory.Build(blockMoq);

        var binary = _shapeBinarySerializer.ToArray(block);

        binary.Should().NotBeNullOrEmpty();

        var deserialized = _shapeBinarySerializer.FromArray(binary);

        deserialized.Should().NotBeNull();
        deserialized.Bounds().LenX.Should().BeApproximately(block.Bounds().LenX, 1e-5);
        deserialized.Bounds().LenY.Should().BeApproximately(block.Bounds().LenY, 1e-5);
        deserialized.Bounds().LenZ.Should().BeApproximately(block.Bounds().LenZ, 1e-5);
    }
}
