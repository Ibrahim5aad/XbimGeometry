using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class CurveFactoryTests : IDisposable
{
    private readonly ModelGeometryService _service;

    public CurveFactoryTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = new MemoryModel(new EntityFactoryIfc4());
        model.ModelFactors = new XbimModelFactors(1, 0.001, 1e-5);
        _service = new ModelGeometryService(model, loggerFactory);
    }

    public void Dispose() => _service.Dispose();

    #region Line Tests

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(-10)]
    [InlineData(0.1)]
    public void Can_convert_ifc_line_3d(double parametricLength)
    {
        var ifcLine = IfcMoq.Line3d(magnitude: parametricLength);
        var ifcLineSegment = IfcMoq.TrimmedCurve3d(ifcLine, 0, 100);

        var curveFactory = _service.CurveFactory;
        var line = curveFactory.Build(ifcLine) as IXLine;
        var trimmedLine = curveFactory.Build(ifcLineSegment) as IXTrimmedCurve;
        Assert.NotNull(line);
        Assert.NotNull(trimmedLine);
        if (line != null && trimmedLine != null)
        {
            Assert.Equal(ifcLine.Pnt.X, line.Origin.X);
            Assert.Equal(ifcLine.Pnt.Y, line.Origin.Y);
            Assert.Equal(ifcLine.Pnt.Z, line.Origin.Z);
            trimmedLine.LastParameter.Should().Be(Math.Abs(parametricLength * 100));
            trimmedLine.EndPoint.Z.Should().Be(parametricLength * 100);
            Assert.Equal(ifcLine.Dir.Orientation.X, line.Direction.X);
            Assert.Equal(ifcLine.Dir.Orientation.Y, line.Direction.Y);
            Assert.Equal(ifcLine.Dir.Orientation.Z, line.Direction.Z);
            var p1 = line.GetPoint(500);

            var p2 = line.GetFirstDerivative(500, out IXDirection normal);
            Assert.Equal(p1.X, p2.X);
            Assert.Equal(p1.Y, p2.Y);
            Assert.Equal(p1.Z, p2.Z);
            Assert.Equal(line.Direction.X, normal.X);
            Assert.Equal(line.Direction.Y, normal.Y);
            Assert.Equal(line.Direction.Z, normal.Z);
            Assert.True(line.Is3d);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(-10)]
    [InlineData(0.1)]
    public void Can_convert_ifc_line_2d(double parametricLength)
    {
        var ifcLine = IfcMoq.Line2d(magnitude: parametricLength);
        var ifcLineSegment = IfcMoq.TrimmedCurve2d(ifcLine, 0, 100);
        var curveFactory = _service.CurveFactory;
        var line = curveFactory.Build(ifcLine) as IXLine;
        var trimmedLine = (IXTrimmedCurve)curveFactory.Build(ifcLineSegment);
        Assert.NotNull(line);
        Assert.Equal(ifcLine.Pnt.X, line.Origin.X);
        Assert.Equal(ifcLine.Pnt.Y, line.Origin.Y);
        Assert.Equal<double>(ifcLine.Dir.Magnitude, line.Direction.Magnitude);
        Assert.Equal(ifcLine.Dir.Orientation.X, line.Direction.X);
        Assert.Equal(ifcLine.Dir.Orientation.Y, line.Direction.Y);

        trimmedLine.LastParameter.Should().Be(Math.Abs(parametricLength * 100));

        trimmedLine.EndPoint.X.Should().Be(parametricLength * 100);
        var p1 = line.GetPoint(500);
        var p2 = line.GetFirstDerivative(500, out IXDirection normal);
        Assert.Equal(p1.X, p2.X);
        Assert.Equal(p1.Y, p2.Y);

        Assert.Equal(line.Direction.X, normal.X);
        Assert.Equal(line.Direction.Y, normal.Y);

        Assert.False(line.Is3d);
        p1.Invoking(p => p.Z).Should().Throw<Exception>();
        normal.Invoking(n => n.Z).Should().Throw<Exception>();
    }

    #endregion

    #region Circles

    [Theory]
    [InlineData(10)]
    [InlineData(-10, false, true)]
    [InlineData(0, false, true)]
    [InlineData(10, true)]
    public void Can_convert_ifc_circle_3d(double radius, bool location2d = false, bool checkException = false)
    {
        var ifcCircle = IfcMoq.Circle3d(radius: radius, location: location2d ? IfcMoq.Axis2Placement2d() : null);
        var curveFactory = _service.CurveFactory;
        if (checkException)
            ifcCircle.Invoking(c => curveFactory.Build(c)).Should().Throw<Exception>();
        else
        {
            var circle = curveFactory.Build(ifcCircle);
            Assert.Equal(XCurveType.IfcCircle, circle.CurveType);
            Assert.True(circle.Is3d);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-10, false, true)]
    [InlineData(0, false, true)]
    [InlineData(10, true, true)]
    public void Can_convert_ifc_circle_2d(double radius, bool location3d = false, bool checkException = false)
    {
        var ifcCircle = IfcMoq.Circle2d(radius: radius, location: location3d ? IfcMoq.Axis2Placement3d() : null);
        var curveFactory = _service.CurveFactory;
        if (checkException)
            ifcCircle.Invoking(c => curveFactory.Build(c)).Should().Throw<XbimGeometryServiceException>();
        else
        {
            var circle = curveFactory.Build(ifcCircle);
            Assert.Equal(XCurveType.IfcCircle, circle.CurveType);
            Assert.False(circle.Is3d);
        }
    }

    #endregion

    #region Ellipse

    [Theory]
    [InlineData(10, -1, true)]
    [InlineData(4, 12)]
    [InlineData(5, 5)]
    [InlineData(15, 5)]
    [InlineData(-1, 1, true)]
    public void Can_convert_ifc_ellipse_3d(double major, double minor, bool checkException = false)
    {
        var ifcEllipse = IfcMoq.Ellipse3d(semi1: major, semi2: minor);
        var curveFactory = _service.CurveFactory;
        if (checkException)
            ifcEllipse.Invoking(c => curveFactory.Build(c)).Should().Throw<Exception>();
        else
        {
            var ellipse = curveFactory.Build(ifcEllipse) as IXEllipse;
            Assert.NotNull(ellipse);
            Assert.Equal(XCurveType.IfcEllipse, ellipse.CurveType);
            Assert.True(ellipse.Is3d);
            Assert.True(ellipse.MajorRadius >= ellipse.MinorRadius);
        }
    }

    [Theory]
    [InlineData(10, -1, true)]
    [InlineData(4, 12)]
    [InlineData(5, 5)]
    [InlineData(15, 5)]
    [InlineData(-1, 1, true)]
    public void Can_convert_ifc_ellipse_2d(double semi1, double semi2, bool checkException = false)
    {
        var ifcEllipse = IfcMoq.Ellipse2d(semi1: semi1, semi2: semi2);
        var curveFactory = _service.CurveFactory;
        if (checkException)
            ifcEllipse.Invoking(c => curveFactory.Build(c)).Should().Throw<Exception>();
        else
        {
            var ellipse = curveFactory.Build(ifcEllipse) as IXEllipse;
            Assert.NotNull(ellipse);
            Assert.Equal(XCurveType.IfcEllipse, ellipse.CurveType);
            Assert.False(ellipse.Is3d);
            Assert.True(ellipse.MajorRadius >= ellipse.MinorRadius);
        }
    }

    #endregion

    #region Trimmed Curve Tests

    [Theory]
    [InlineData(1, 0, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 10, 100)]
    public void Can_convert_ifc_trimmed_line_3d(double parametricLength = 1, double trim1 = 0, double trim2 = 10)
    {
        var basisLine = IfcMoq.Line3d(
            magnitude: parametricLength,
            origin: IfcMoq.CartesianPoint3d(0, 0, 0),
            direction: IfcMoq.Direction3d(0, 0, 1));
        var ifcTrimmedCurve = IfcMoq.TrimmedCurve3d(basisCurve: basisLine, trimParam1: trim1, trimParam2: trim2);
        var curveFactory = _service.CurveFactory;
        var tc = (IXTrimmedCurve)curveFactory.Build(ifcTrimmedCurve);
        Assert.Equal(XCurveType.IfcTrimmedCurve, tc.CurveType);
        Assert.Equal(XCurveType.IfcLine, tc.BasisCurve.CurveType);
        Assert.True(tc.Is3d);
        Assert.Equal(tc.EndPoint.Z, parametricLength * trim2);
        Assert.Equal(tc.StartPoint.Z, parametricLength * trim1);
    }

    [Theory]
    [InlineData(1, 0, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 10, 100)]
    public void Can_convert_ifc_trimmed_line_2d(double parametricLength = 1, double trim1 = 0, double trim2 = 10)
    {
        var basisLine = IfcMoq.Line2d(
            magnitude: parametricLength,
            origin: IfcMoq.CartesianPoint2d(0, 0),
            direction: IfcMoq.Direction2d(1, 0));
        var ifcTrimmedCurve = IfcMoq.TrimmedCurve2d(basisCurve: basisLine, trimParam1: trim1, trimParam2: trim2);
        var curveFactory = _service.CurveFactory;
        var tc = (IXTrimmedCurve)curveFactory.Build(ifcTrimmedCurve);
        Assert.Equal(XCurveType.IfcTrimmedCurve, tc.CurveType);
        Assert.Equal(XCurveType.IfcLine, tc.BasisCurve.CurveType);
        Assert.False(tc.Is3d);
        Assert.Equal(tc.EndPoint.X, parametricLength * trim2);
        Assert.Equal(tc.StartPoint.X, parametricLength * trim1);
    }

    [Theory]
    [InlineData(Math.PI / 2, Math.PI, true, 1)]
    [InlineData(Math.PI, Math.PI / 2, true, 2)]
    [InlineData(Math.PI, Math.PI / 2, false, 3)]
    [InlineData(Math.PI / 2, Math.PI, false, 4)]
    [InlineData(Math.PI / 180 * 12, 0, false, 5)]
    public void Can_convert_ifc_trimmed_circle_2d(double trim1, double trim2, bool sameSense, int ifcCase)
    {
        double radius = 10;
        var ifcTrimmedCurve = IfcMoq.TrimmedCurve2d(
            trimParam1: trim1,
            trimParam2: trim2,
            sense: sameSense,
            basisCurve: IfcMoq.Circle2d(radius: radius));

        var curveFactory = _service.CurveFactory;
        var tc = (IXTrimmedCurve)curveFactory.Build(ifcTrimmedCurve);
        Assert.Equal(XCurveType.IfcTrimmedCurve, tc.CurveType);
        Assert.Equal(XCurveType.IfcCircle, tc.BasisCurve.CurveType);
        var basisCurve = tc.BasisCurve as IXCircle;
        Assert.NotNull(basisCurve);
        var origin = basisCurve.Position as IXAxis2Placement2d;
        Assert.NotNull(origin);

        switch (ifcCase)
        {
            case 1:
                (radius * Math.PI / 2).Should().BeApproximately(tc.Length, _service.Precision);
                tc.StartPoint.Y.Should().BeApproximately(radius, _service.Precision);
                tc.EndPoint.X.Should().BeApproximately(-radius, _service.Precision);
                break;
            case 2:
                (3 * radius * Math.PI / 2).Should().BeApproximately(tc.Length, _service.Precision);
                tc.EndPoint.Y.Should().BeApproximately(radius, _service.Precision);
                tc.StartPoint.X.Should().BeApproximately(-radius, _service.Precision);
                break;
            case 3:
                (radius * Math.PI / 2).Should().BeApproximately(tc.Length, _service.Precision);
                tc.EndPoint.Y.Should().BeApproximately(radius, _service.Precision);
                tc.StartPoint.X.Should().BeApproximately(-radius, _service.Precision);
                break;
            case 4:
                (3 * radius * Math.PI / 2).Should().BeApproximately(tc.Length, _service.Precision);
                tc.StartPoint.Y.Should().BeApproximately(radius, _service.Precision);
                tc.EndPoint.X.Should().BeApproximately(-radius, _service.Precision);
                break;
            case 5:
                tc.Length.Should().BeApproximately(radius * trim1, _service.Precision);
                tc.StartPoint.X.Should().BeApproximately(radius * Math.Cos(trim1), _service.Precision);
                tc.StartPoint.Y.Should().BeApproximately(radius * Math.Sin(trim1), _service.Precision);
                tc.EndPoint.X.Should().BeApproximately(radius, _service.Precision);
                tc.EndPoint.Y.Should().BeApproximately(0, _service.Precision);
                break;
        }
        tc.Is3d.Should().BeFalse();
    }

    [Theory]
    [InlineData(Math.PI / 2, Math.PI, true, 1)]
    [InlineData(Math.PI, Math.PI / 2, true, 2)]
    [InlineData(Math.PI, Math.PI / 2, false, 3)]
    [InlineData(Math.PI / 2, Math.PI, false, 4)]
    public void Can_convert_ifc_trimmed_circle_3d(double trim1, double trim2, bool sameSense, int ifcCase)
    {
        double radius = 10;
        var ifcTrimmedCurve = IfcMoq.TrimmedCurve3d(
            trimParam1: trim1,
            trimParam2: trim2,
            sense: sameSense,
            basisCurve: IfcMoq.Circle3d(radius: radius));

        var curveFactory = _service.CurveFactory;
        var tc = (IXTrimmedCurve)curveFactory.Build(ifcTrimmedCurve);
        Assert.Equal(XCurveType.IfcTrimmedCurve, tc.CurveType);
        Assert.Equal(XCurveType.IfcCircle, tc.BasisCurve.CurveType);
        var basisCurve = tc.BasisCurve as IXCircle;
        Assert.NotNull(basisCurve);
        var origin = basisCurve.Position as IXAxis2Placement3d;
        Assert.NotNull(origin);
        Assert.True(tc.Is3d);

        switch (ifcCase)
        {
            case 1:
                (radius * Math.PI / 2).Should().BeApproximately(tc.Length, _service.Precision);
                tc.StartPoint.Y.Should().BeApproximately(radius, _service.Precision);
                tc.EndPoint.X.Should().BeApproximately(-radius, _service.Precision);
                break;
            case 2:
                (3 * radius * Math.PI / 2).Should().BeApproximately(tc.Length, _service.Precision);
                tc.EndPoint.Y.Should().BeApproximately(radius, _service.Precision);
                tc.StartPoint.X.Should().BeApproximately(-radius, _service.Precision);
                break;
            case 3:
                (radius * Math.PI / 2).Should().BeApproximately(tc.Length, _service.Precision);
                tc.EndPoint.Y.Should().BeApproximately(radius, _service.Precision);
                tc.StartPoint.X.Should().BeApproximately(-radius, _service.Precision);
                break;
            case 4:
                (3 * radius * Math.PI / 2).Should().BeApproximately(tc.Length, _service.Precision);
                tc.StartPoint.Y.Should().BeApproximately(radius, _service.Precision);
                tc.EndPoint.X.Should().BeApproximately(-radius, _service.Precision);
                break;
        }
    }

    [Theory]
    [InlineData(Math.PI / 2, Math.PI, true, true)]
    [InlineData(Math.PI, Math.PI / 2, true, true)]
    [InlineData(Math.PI, Math.PI / 2, true, false)]
    [InlineData(Math.PI / 2, Math.PI, true, false)]
    [InlineData(Math.PI / 2, Math.PI, false, true)]
    [InlineData(Math.PI, Math.PI / 2, false, true)]
    [InlineData(Math.PI, Math.PI / 2, false, false)]
    [InlineData(Math.PI / 2, Math.PI, false, false)]
    public void Can_convert_ifc_trimmed_ellipse_3d(double trim1, double trim2, bool reverseAxis, bool sameSense = true)
    {
        double semi1 = reverseAxis ? 5 : 10;
        double semi2 = reverseAxis ? 10 : 5;
        double quadrantLength = 12.110560271815889;
        var ifcTrimmedCurve = IfcMoq.TrimmedCurve3d(
            trimParam1: trim1,
            trimParam2: trim2,
            sense: sameSense,
            basisCurve: IfcMoq.Ellipse3d(semi1: semi1, semi2: semi2));

        var curveFactory = _service.CurveFactory;
        var tc = (IXTrimmedCurve)curveFactory.Build(ifcTrimmedCurve);
        Assert.Equal(XCurveType.IfcTrimmedCurve, tc.CurveType);
        Assert.Equal(XCurveType.IfcEllipse, tc.BasisCurve.CurveType);
        var basisCurve = tc.BasisCurve as IXEllipse;
        Assert.NotNull(basisCurve);
        var origin = basisCurve.Position as IXAxis2Placement3d;
        Assert.NotNull(origin);
        Assert.True(tc.Is3d);
        var sp = tc.StartPoint;
        var ep = tc.EndPoint;
        if (sameSense)
        {
            if (trim1 > trim2)
            {
                (3 * quadrantLength).Should().BeApproximately(tc.Length, 1e-2);
                tc.StartPoint.X.Should().BeApproximately(origin.Axis.Location.X - semi1, _service.Precision);
                tc.EndPoint.Y.Should().BeApproximately(origin.Axis.Location.Y + semi2, _service.Precision);
            }
            else
            {
                quadrantLength.Should().BeApproximately(tc.Length, 1e-2);
                tc.StartPoint.Y.Should().BeApproximately(origin.Axis.Location.Y + semi2, _service.Precision);
                tc.EndPoint.X.Should().BeApproximately(origin.Axis.Location.X - semi1, _service.Precision);
            }
        }
        else
        {
            if (trim1 > trim2)
            {
                (3 * quadrantLength).Should().BeApproximately(tc.Length, 1e-2);
                tc.StartPoint.X.Should().BeApproximately(origin.Axis.Location.X - semi1, _service.Precision);
                tc.EndPoint.Y.Should().BeApproximately(origin.Axis.Location.Y + semi2, _service.Precision);
            }
            else
            {
                quadrantLength.Should().BeApproximately(tc.Length, 1e-2);
                tc.StartPoint.Y.Should().BeApproximately(origin.Axis.Location.Y + semi2, _service.Precision);
                tc.EndPoint.X.Should().BeApproximately(origin.Axis.Location.X - semi1, _service.Precision);
            }
        }
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(5, 10)]
    public void Can_convert_ifc_trimmed_ellipse_2d(double semi1 = 10, double semi2 = 5)
    {
        var ifcTrimmedCurve = IfcMoq.TrimmedCurve2d(
            trimParam1: 0,
            trimParam2: Math.PI / 2,
            basisCurve: IfcMoq.Ellipse2d(semi1: semi1, semi2: semi2));

        var tc = _service.CurveFactory.Build(ifcTrimmedCurve) as IXTrimmedCurve;
        Assert.NotNull(tc);
        Assert.Equal(XCurveType.IfcTrimmedCurve, tc.CurveType);
        Assert.Equal(XCurveType.IfcEllipse, tc.BasisCurve.CurveType);
        var basisCurve = tc.BasisCurve as IXEllipse;
        Assert.NotNull(basisCurve);
        var origin = basisCurve.Position as IXAxis2Placement2d;
        Assert.NotNull(origin);
        Assert.False(tc.Is3d);
        tc.StartPoint.X.Should().BeApproximately(origin.Location.X + semi1, _service.Precision);
        tc.StartPoint.Y.Should().BeApproximately(origin.Location.Y, _service.Precision);
        tc.EndPoint.X.Should().BeApproximately(origin.Location.X, _service.Precision);
        tc.EndPoint.Y.Should().BeApproximately(origin.Location.Y + semi2, _service.Precision);
    }

    #endregion

    #region Composite Curves

    [Fact]
    public void Can_convert_ifc_composite_curve_simple_arc()
    {
        var ifcCompCurve = IfcMoq.CompositeCurve3d();
        var curveService = _service.CurveFactory;
        var edgeService = _service.EdgeFactory;
        var cc = curveService.Build(ifcCompCurve);

        Assert.NotNull(cc);
        Assert.Equal(XCurveType.IfcCompositeCurve, cc.CurveType);

        var edge = edgeService.Build(cc);

        var paramsRads = cc.LastParameter - cc.FirstParameter;
        paramsRads.Should().BeApproximately(Math.PI * 0.5, 1e-5);
    }

    [Fact]
    public void Can_convert_ifc_composite_curve_three_arcs()
    {
        var circ1 = IfcMoq.Circle3d(radius: 20);
        var circ2 = IfcMoq.Circle3d(radius: 20, IfcMoq.Axis2Placement3d(refDir: IfcMoq.Direction3d(-1, 0, 0), loc: IfcMoq.CartesianPoint3d(0, 40, 0)));
        var circ3 = IfcMoq.Circle3d(radius: 20, IfcMoq.Axis2Placement3d(loc: IfcMoq.CartesianPoint3d(-40, 40, 0)));

        var arc1 = IfcMoq.TrimmedCurve3d(circ1, trimParam2: Math.PI / 2);
        var arc2 = IfcMoq.TrimmedCurve3d(circ2, trimParam1: Math.PI / 2, trimParam2: 0, sense: true);
        var arc3 = IfcMoq.TrimmedCurve3d(circ3, trimParam2: Math.PI / 2);

        var x1 = (IXTrimmedCurve)_service.CurveFactory.Build(arc1);
        var x2 = (IXTrimmedCurve)_service.CurveFactory.Build(arc2);
        var x3 = (IXTrimmedCurve)_service.CurveFactory.Build(arc3);
        var paramLength1 = x1.LastParameter - x1.FirstParameter;
        var paramLength2 = x2.LastParameter - x2.FirstParameter;
        var paramLength3 = x3.LastParameter - x3.FirstParameter;
        var totalParametricLength = paramLength1 + paramLength2 + paramLength3;
        var seg1 = IfcMoq.CompositeCurveSegment3d(arc1, entityLabel: 1);
        var seg2 = IfcMoq.CompositeCurveSegment3d(arc2, entityLabel: 2);
        var seg3 = IfcMoq.CompositeCurveSegment3d(arc3, entityLabel: 3);

        var ifcCompCurve = IfcMoq.CompositeCurve3d(new[] { seg1, seg2, seg3 });

        var cc = _service.CurveFactory.Build(ifcCompCurve);
        Assert.NotNull(cc);
        Assert.Equal(XCurveType.IfcCompositeCurve, cc.CurveType);
        totalParametricLength.Should().BeApproximately(cc.LastParameter - cc.FirstParameter, _service.Precision);
    }

    [Fact]
    public void Can_convert_ifc_composite_curve_three_arcs_two_lines()
    {
        var ifcCompCurve = IfcMoq.TypicalCompositeCurve(_service.CurveFactory, out double totalParametricLength, out double totalLength);

        var cc = _service.CurveFactory.Build(ifcCompCurve);

        Assert.NotNull(cc);
        Assert.Equal(XCurveType.IfcCompositeCurve, cc.CurveType);
        totalLength.Should().BeApproximately(cc.Length, _service.MinimumGap);
        totalParametricLength.Should().BeApproximately(cc.LastParameter - cc.FirstParameter, _service.Precision);
    }

    [Fact]
    public void Can_convert_ifc_composite_curve_to_directrix()
    {
        var ifcCompCurve = IfcMoq.TypicalCompositeCurve(_service.CurveFactory, out double totalParametricLength, out double totalLength);

        var cc = _service.CurveFactory.BuildDirectrix(ifcCompCurve, 10, totalParametricLength - 10);

        Assert.NotNull(cc);
        Assert.Equal(XCurveType.IfcCompositeCurve, cc.CurveType);
        (totalParametricLength - 20).Should().BeApproximately(cc.LastParameter - cc.FirstParameter, _service.Precision);
    }

    #endregion
}
