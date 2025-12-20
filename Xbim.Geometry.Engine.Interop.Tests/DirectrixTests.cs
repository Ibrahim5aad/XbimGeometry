using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests directrix building and trimming in CurveFactory and WireFactory.
/// Verifies that BuildDirectrix applies parametric trimming and
/// BuildDirectrixWire produces correctly trimmed wires.
/// </summary>
public class DirectrixTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXCurveFactory _curveFactory;
    private readonly WireFactory _wireFactory;

    public DirectrixTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _curveFactory = _service.CurveFactory;
        _wireFactory = (WireFactory)_service.WireFactory;
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void BuildDirectrix_WithNullParams_ReturnsFullCurve()
    {
        // A full circle with radius 10
        var ifcCircle = IfcMoq.IfcCircle3d(10);

        var curve = _curveFactory.BuildDirectrix(ifcCircle, null, null);

        curve.Should().NotBeNull();
        // Full circle should span 0 to 2*PI in parametric space
        curve.Length.Should().BeApproximately(2 * Math.PI * 10, 0.01);
    }

    [Fact]
    public void BuildDirectrix_WithTrimParams_ReturnsShorterCurve()
    {
        // A full circle with radius 10, trimmed to a quarter arc (0 to PI/2)
        var ifcCircle = IfcMoq.IfcCircle3d(10);

        var fullCurve = _curveFactory.BuildDirectrix(ifcCircle, null, null);
        var trimmedCurve = _curveFactory.BuildDirectrix(ifcCircle, 0, Math.PI / 2);

        trimmedCurve.Should().NotBeNull();
        // Quarter circle arc length = (PI/2) * radius = 5*PI ≈ 15.71
        trimmedCurve.Length.Should().BeApproximately(Math.PI / 2 * 10, 0.1);
        trimmedCurve.Length.Should().BeLessThan(fullCurve.Length);
    }

    [Fact]
    public void BuildDirectrixWire_WithNullParams_ReturnsFullWire()
    {
        var ifcCircle = IfcMoq.IfcCircle3d(10);

        var wire = _wireFactory.BuildDirectrixWire(ifcCircle, null, null);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(2 * Math.PI * 10, 0.01);
    }

    [Fact]
    public void BuildDirectrixWire_WithTrimParams_ReturnsTrimmedWire()
    {
        var ifcCircle = IfcMoq.IfcCircle3d(10);

        // Trim params are in IFC angle units (degrees for this mock model)
        // 90 degrees = quarter circle
        var wire = _wireFactory.BuildDirectrixWire(ifcCircle, 0, 90);

        wire.Should().NotBeNull();
        // Quarter circle arc length = PI/2 * radius = 5*PI ≈ 15.71
        wire.Length.Should().BeApproximately(Math.PI / 2 * 10, 0.5);
    }

    [Fact]
    public void BuildDirectrixWire_Line_WithTrimParams_ReturnsTrimmedWire()
    {
        // Line from origin along X axis, magnitude 100
        var ifcLine = IfcMoq.IfcLine(0, 0, 0, 1, 0, 0, 100);

        // Build wire and trim to first 50 units
        var wire = _wireFactory.BuildDirectrixWire(ifcLine, 0, 50);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(50, 0.1);
    }
}
