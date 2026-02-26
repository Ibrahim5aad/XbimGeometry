using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

/// <summary>
/// Tests that WireFactory.Build(IIfcProfileDef) delegates to ProfileFactory
/// and returns valid wires for various profile types.
/// </summary>
public class WireFactoryProfileTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXWireFactory _wireFactory;

    public WireFactoryProfileTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _wireFactory = _service.WireFactory;
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void Build_RectangleProfile_ReturnsClosedWire()
    {
        var profile = IfcMoq.RectangleProfile(100, 200);

        var wire = _wireFactory.Build(profile);

        wire.Should().NotBeNull();
        wire.IsClosed.Should().BeTrue();
        wire.Length.Should().BeApproximately(600, 0.01, "perimeter of 100x200 rectangle is 2*(100+200)=600");
    }

    [Fact]
    public void Build_ArbitraryClosedProfile_ReturnsClosedWire()
    {
        // Triangle profile: (0,0) -> (100,0) -> (50,100)
        var profile = IfcMoq.ArbitraryClosedProfile(new[]
        {
            (0.0, 0.0), (100.0, 0.0), (50.0, 100.0)
        });

        var wire = _wireFactory.Build(profile);

        wire.Should().NotBeNull();
        wire.IsClosed.Should().BeTrue();
        wire.EdgeLoop.Should().NotBeEmpty();
    }
}
