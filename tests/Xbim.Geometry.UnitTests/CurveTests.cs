using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class CurveTests : IDisposable
{
    private readonly ModelGeometryService _service;

    public CurveTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void Can_Get_Derivatives()
    {
        var basisLine = IfcMoq.Line3d(
            magnitude: 100,
            origin: IfcMoq.CartesianPoint3d(0, 0, 0),
            direction: IfcMoq.Direction3d(0, 0, 1));
        var ifcTrimmedCurve = IfcMoq.TrimmedCurve3d(
            basisCurve: basisLine, trimParam1: 1, trimParam2: 30);
        var curveFactory = _service.CurveFactory;
        var tc = curveFactory.Build(ifcTrimmedCurve) as IXTrimmedCurve;

        var p1 = tc?.GetFirstDerivative(1, out var direction1);
        IXDirection? normal2 = null;
        var p2 = tc?.GetSecondDerivative(1, out var direction2, out normal2);
        normal2.Should().NotBeNull();
        double.IsNaN(normal2.X).Should().BeTrue();
        double.IsNaN(normal2.Y).Should().BeTrue();
        normal2.IsNull.Should().BeTrue();
    }
}
