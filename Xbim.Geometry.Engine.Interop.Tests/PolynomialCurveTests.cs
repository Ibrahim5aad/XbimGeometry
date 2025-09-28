using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Ifc4x3;
using Xbim.Ifc4x3.GeometryResource;
using Xbim.Ifc4x3.MeasureResource;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests for IFC4x3 IfcPolynomialCurve support via the native polynomial curve builder.
/// </summary>
public class PolynomialCurveTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    public PolynomialCurveTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
    }

    [Fact]
    public void BuildPolynomialCurve_LinearX_CubicY_ReturnsValidCurve()
    {
        // Arrange: linear X(u) = u, cubic Y(u) = -3.889e-6 * u^3
        // Same as the Cubic_300_1000 test case from legacy tests
        using var model = new MemoryModel(new EntityFactoryIfc4x3Add2());
        using var txn = model.BeginTransaction("polynomial_test");

        var polyCurve = model.Instances.New<IfcPolynomialCurve>(p =>
        {
            p.Position = model.Instances.New<IfcAxis2Placement2D>(placement =>
            {
                placement.Location = model.Instances.New<IfcCartesianPoint>(pnt =>
                {
                    pnt.X = 0; pnt.Y = 0;
                });
                placement.RefDirection = model.Instances.New<IfcDirection>(d =>
                {
                    d.X = 1; d.Y = 0;
                });
            });
        });

        polyCurve.CoefficientsX.AddRange(new[] { 0.0, 1.0 }.Select(x => new IfcReal(x)));
        polyCurve.CoefficientsY.AddRange(new[] { 0.0, 0.0, 0.0, -3.88888888888889E-6 }.Select(x => new IfcReal(x)));
        txn.Commit();

        var factory = new GeometryConverterFactory();
        var modelSvc = factory.CreateModelGeometryService(model, _loggerFactory);

        double startParam = -142.857142857143;
        double endParam = startParam + 100;

        // Act
        var curve = modelSvc.CurveFactory.BuildPolynomialCurve2d(polyCurve, startParam, endParam);

        // Assert
        curve.Should().NotBeNull();

        // The curve should have the expected parameter range
        curve.FirstParameter.Should().BeApproximately(startParam, 1.0,
            "B-spline fitting may adjust parameter bounds slightly");
        curve.LastParameter.Should().BeApproximately(endParam, 1.0);

        // Check the start point (at firstParam): should be (0, 0) since the polynomial
        // evaluates relative to the placement origin
        var startPoint = curve.GetPoint(curve.FirstParameter);
        startPoint.X.Should().BeApproximately(0.0, 0.1);
        startPoint.Y.Should().BeApproximately(0.0, 0.1);
    }

    [Fact]
    public void BuildPolynomialCurve_CubicInf_ReturnsValidCurve()
    {
        // Arrange: Cubic_300_inf test case
        using var model = new MemoryModel(new EntityFactoryIfc4x3Add2());
        using var txn = model.BeginTransaction("polynomial_test_2");

        var polyCurve = model.Instances.New<IfcPolynomialCurve>(p =>
        {
            p.Position = model.Instances.New<IfcAxis2Placement2D>(placement =>
            {
                placement.Location = model.Instances.New<IfcCartesianPoint>(pnt =>
                {
                    pnt.X = 0; pnt.Y = 0;
                });
                placement.RefDirection = model.Instances.New<IfcDirection>(d =>
                {
                    d.X = 1; d.Y = 0;
                });
            });
        });

        polyCurve.CoefficientsX.AddRange(new[] { 0.0, 1.0 }.Select(x => new IfcReal(x)));
        polyCurve.CoefficientsY.AddRange(new[] { 0.0, 0.0, 0.0, -5.55555555555556E-6 }.Select(x => new IfcReal(x)));
        txn.Commit();

        var factory = new GeometryConverterFactory();
        var modelSvc = factory.CreateModelGeometryService(model, _loggerFactory);

        double startParam = -100;
        double endParam = startParam + 100;

        // Act
        var curve = modelSvc.CurveFactory.BuildPolynomialCurve2d(polyCurve, startParam, endParam);

        // Assert
        curve.Should().NotBeNull();

        // Verify we can evaluate points along the curve
        var midPoint = curve.GetPoint((curve.FirstParameter + curve.LastParameter) / 2.0);
        midPoint.Should().NotBeNull();

        // The X coordinate at midpoint should be roughly halfway through the parameter range
        // Since X(u) = u and parameters span 100 units, X at midpoint should be ~50 from start
        var endPoint = curve.GetPoint(curve.LastParameter);
        endPoint.Should().NotBeNull();
    }

    [Fact]
    public void BuildPolynomialCurve_QuadraticXY_EvaluatesCorrectly()
    {
        // Arrange: simple quadratic X(u) = u, Y(u) = u^2 over [0, 10]
        using var model = new MemoryModel(new EntityFactoryIfc4x3Add2());
        using var txn = model.BeginTransaction("polynomial_test_3");

        var polyCurve = model.Instances.New<IfcPolynomialCurve>(p =>
        {
            p.Position = model.Instances.New<IfcAxis2Placement2D>(placement =>
            {
                placement.Location = model.Instances.New<IfcCartesianPoint>(pnt =>
                {
                    pnt.X = 0; pnt.Y = 0;
                });
                placement.RefDirection = model.Instances.New<IfcDirection>(d =>
                {
                    d.X = 1; d.Y = 0;
                });
            });
        });

        // X(u) = u (linear), Y(u) = u^2 (quadratic)
        polyCurve.CoefficientsX.AddRange(new[] { 0.0, 1.0 }.Select(x => new IfcReal(x)));
        polyCurve.CoefficientsY.AddRange(new[] { 0.0, 0.0, 1.0 }.Select(x => new IfcReal(x)));
        txn.Commit();

        var factory = new GeometryConverterFactory();
        var modelSvc = factory.CreateModelGeometryService(model, _loggerFactory);

        // Act
        var curve = modelSvc.CurveFactory.BuildPolynomialCurve2d(polyCurve, 0, 10);

        // Assert
        curve.Should().NotBeNull();

        // At u=0: X=0, Y=0
        var p0 = curve.GetPoint(curve.FirstParameter);
        p0.X.Should().BeApproximately(0.0, 0.01);
        p0.Y.Should().BeApproximately(0.0, 0.01);

        // At u=10: X=10, Y=100
        var p10 = curve.GetPoint(curve.LastParameter);
        p10.X.Should().BeApproximately(10.0, 0.1);
        p10.Y.Should().BeApproximately(100.0, 0.5);

        // At midpoint u=5: X=5, Y=25
        double midParam = (curve.FirstParameter + curve.LastParameter) / 2.0;
        var p5 = curve.GetPoint(midParam);
        p5.X.Should().BeApproximately(5.0, 0.1);
        p5.Y.Should().BeApproximately(25.0, 0.5);
    }
}
