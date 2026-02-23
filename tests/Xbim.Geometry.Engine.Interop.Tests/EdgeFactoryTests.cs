using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests EdgeFactory.Build(IXCurve) via the full P/Invoke path:
///   IFC curve mock → CurveFactory → IXCurve → EdgeFactory → IXEdge
/// Verifies that edges are created with correct geometry from various curve types.
/// </summary>
public class EdgeFactoryTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXEdgeFactory _edgeFactory;
    private readonly IXCurveFactory _curveFactory;

    public EdgeFactoryTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _edgeFactory = _service.EdgeFactory;
        _curveFactory = _service.CurveFactory;
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void BuildFromCircleCurve_ReturnsEdgeWithCorrectLength()
    {
        // Arrange: circle with radius 10, circumference = 2 * π * 10 ≈ 62.832
        double radius = 10;
        var ifcCircle = IfcMoq.IfcCircle3d(radius);
        var curve = _curveFactory.Build(ifcCircle);

        // Act
        var edge = _edgeFactory.Build(curve);

        // Assert
        edge.Should().NotBeNull();
        edge.Should().BeAssignableTo<IXEdge>();
        edge.Length.Should().BeApproximately(2.0 * Math.PI * radius, 0.01,
            "a full circle edge should have circumference 2πr");
    }

    [Fact]
    public void BuildFromCircleCurve_Is3d()
    {
        // Arrange
        var ifcCircle = IfcMoq.IfcCircle3d(5);
        var curve = _curveFactory.Build(ifcCircle);

        // Verify the curve is 3D
        curve.Is3d.Should().BeTrue();

        // Act
        var edge = _edgeFactory.Build(curve);

        // Assert
        edge.Should().NotBeNull();
        edge.EdgeStart.Should().NotBeNull();
        edge.EdgeEnd.Should().NotBeNull();
    }

    [Fact]
    public void BuildFromEllipseCurve_ReturnsNonZeroLengthEdge()
    {
        // Arrange: ellipse with semi-axes 10 and 5
        var ifcEllipse = IfcMoq.IfcEllipse3d(semiAxis1: 10, semiAxis2: 5);
        var curve = _curveFactory.Build(ifcEllipse);

        // Act
        var edge = _edgeFactory.Build(curve);

        // Assert
        edge.Should().NotBeNull();
        edge.Length.Should().BeGreaterThan(0, "an ellipse edge should have positive length");
        // Ellipse perimeter ≈ π * (3(a+b) - √((3a+b)(a+3b))) ≈ 48.44 (Ramanujan approx)
        edge.Length.Should().BeApproximately(48.44, 0.1,
            "ellipse edge length should match Ramanujan's approximation for perimeter");
    }

    [Fact]
    public void BuildFromTrimmedCircle_QuarterArc_ReturnsCorrectLength()
    {
        // Arrange: circle radius 10 trimmed from 0° to 90° → quarter arc
        double radius = 10;
        var ifcCircle = IfcMoq.IfcCircle3d(radius);
        var ifcTrimmed = IfcMoq.IfcTrimmedCurve3d(ifcCircle, param1: 0, param2: 90);
        var curve = _curveFactory.Build(ifcTrimmed);

        // Act
        var edge = _edgeFactory.Build(curve);

        // Assert: quarter circle length = 2πr / 4 = πr / 2
        double expectedLength = Math.PI * radius / 2.0;
        edge.Should().NotBeNull();
        edge.Length.Should().BeApproximately(expectedLength, 0.01,
            "a 0°–90° trimmed circle should produce a quarter-arc edge");
    }

    [Fact]
    public void BuildFromCircleCurve_DifferentRadii_ScalesCorrectly()
    {
        // Arrange: two circles with different radii
        var curve1 = _curveFactory.Build(IfcMoq.IfcCircle3d(1));
        var curve2 = _curveFactory.Build(IfcMoq.IfcCircle3d(10));

        // Act
        var edge1 = _edgeFactory.Build(curve1);
        var edge2 = _edgeFactory.Build(curve2);

        // Assert: ratio of lengths should be 1:10
        double ratio = edge2.Length / edge1.Length;
        ratio.Should().BeApproximately(10.0, 0.01,
            "edge length should scale linearly with radius");
    }
}
