using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests for GeometryConverterFactory: verifies factory creation methods
/// and service extraction.
/// </summary>
public class GeometryConverterFactoryTests : IDisposable
{
    private readonly GeometryConverterFactory _factory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Common.IModel _model;
    private readonly List<IDisposable> _disposables = new();

    public GeometryConverterFactoryTests()
    {
        _factory = new GeometryConverterFactory();
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _model = IfcMoq.ModelMock();
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
            d.Dispose();
        _loggerFactory.Dispose();
    }

    [Fact]
    public void CreateModelGeometryService_ReturnsValidService()
    {
        var service = _factory.CreateModelGeometryService(_model, _loggerFactory);
        _disposables.Add((IDisposable)service);

        service.Should().NotBeNull();
        service.Should().NotBeAssignableTo<XbimGeometryEngine>("standalone service is not an engine");
        service.Precision.Should().BeGreaterThan(0);
        service.OneMeter.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreateGeometryEngine_ReturnsValidEngine()
    {
        var engine = _factory.CreateGeometryEngine(_model, _loggerFactory);
        _disposables.Add((IDisposable)engine);

        engine.Should().NotBeNull();
        engine.Should().BeOfType<XbimGeometryEngine>();
    }

    [Fact]
    public void Engine_DelegatesFactoryProperties_ToUnderlyingService()
    {
        var engine = (IXGeometryEngineV6)_factory.CreateGeometryEngine(_model, _loggerFactory);
        _disposables.Add((IDisposable)engine);

        // Verify key factory properties are available through the engine
        engine.SolidFactory.Should().NotBeNull();
        engine.ProfileFactory.Should().NotBeNull();
        engine.BooleanFactory.Should().NotBeNull();
        engine.WexBimMeshFactory.Should().NotBeNull();

        // Verify model parameters match
        engine.Precision.Should().Be(engine.ModelGeometryService.Precision);
        engine.OneMeter.Should().Be(engine.ModelGeometryService.OneMeter);
    }
}
