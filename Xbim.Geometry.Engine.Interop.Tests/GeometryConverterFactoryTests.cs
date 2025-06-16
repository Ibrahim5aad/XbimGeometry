using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests for GeometryConverterFactory: verifies factory creation methods,
/// version dispatch, and service extraction.
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
        service.Should().BeOfType<ModelGeometryService>();
        service.Precision.Should().BeGreaterThan(0);
        service.OneMeter.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreateGeometryEngineV6_ReturnsValidEngine()
    {
        var engine = _factory.CreateGeometryEngineV6(_model, _loggerFactory);
        _disposables.Add((IDisposable)engine);

        engine.Should().NotBeNull();
        engine.Should().BeOfType<GeometryEngine>();
        engine.ModelGeometryService.Should().NotBeNull();
        engine.ModelGeometryService.Should().BeOfType<ModelGeometryService>();
    }

    [Fact]
    public void CreateGeometryEngineV5_ThrowsPlatformNotSupported()
    {
        var act = () => _factory.CreateGeometryEngineV5(_model, _loggerFactory);

        act.Should().Throw<PlatformNotSupportedException>();
    }

    [Fact]
    public void CreateGeometryEngine_V6_ReturnsV6Engine()
    {
        var engine = _factory.CreateGeometryEngine(XGeometryEngineVersion.V6, _model, _loggerFactory);
        _disposables.Add((IDisposable)engine);

        engine.Should().NotBeNull();
        engine.Should().BeOfType<GeometryEngine>();
    }

    [Fact]
    public void CreateGeometryEngine_V5_ThrowsPlatformNotSupported()
    {
        var act = () => _factory.CreateGeometryEngine(XGeometryEngineVersion.V5, _model, _loggerFactory);

        act.Should().Throw<PlatformNotSupportedException>();
    }

    [Fact]
    public void CreateGeometryEngine_InvalidVersion_ThrowsArgumentOutOfRange()
    {
        var act = () => _factory.CreateGeometryEngine((XGeometryEngineVersion)99, _model, _loggerFactory);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetUnderlyingModelGeometryService_ExtractsServiceFromV6Engine()
    {
        var engine = _factory.CreateGeometryEngineV6(_model, _loggerFactory);
        _disposables.Add((IDisposable)engine);

        var service = _factory.GetUnderlyingModelGeometryService(engine);

        service.Should().NotBeNull();
        service.Should().BeOfType<ModelGeometryService>();
        service.Should().BeSameAs(engine.ModelGeometryService);
    }

    [Fact]
    public void V6Engine_DelegatesFactoryProperties_ToUnderlyingService()
    {
        var engine = _factory.CreateGeometryEngineV6(_model, _loggerFactory);
        _disposables.Add((IDisposable)engine);

        // Verify key factory properties are available through the V6 engine
        engine.SolidFactory.Should().NotBeNull();
        engine.ProfileFactory.Should().NotBeNull();
        engine.BooleanFactory.Should().NotBeNull();
        engine.WexBimMeshFactory.Should().NotBeNull();

        // Verify model parameters match
        engine.Precision.Should().Be(engine.ModelGeometryService.Precision);
        engine.OneMeter.Should().Be(engine.ModelGeometryService.OneMeter);
    }
}
