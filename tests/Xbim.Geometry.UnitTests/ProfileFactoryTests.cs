using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xbim.Geometry.Exceptions;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

public class ProfileFactoryTests : IDisposable
{
    private readonly ModelGeometryService _service;

    public ProfileFactoryTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
    }

    public void Dispose() => _service.Dispose();

    [Theory]
    [InlineData(200, 100, 0)]
    [InlineData(200, 0, 1000)]
    public void Can_build_circle_profile_def(double outerRadius, double locX, double locY)
    {
        var profileFactory = _service.ProfileFactory;
        var circleProfileDef = IfcMoq.CircleProfile(
            radius: outerRadius,
            position: IfcMoq.Axis2Placement2d(
                refDir: IfcMoq.Direction2d(0, 1),
                loc: IfcMoq.CartesianPoint2d(locX, locY)));
        var profileFace = profileFactory.BuildFace(circleProfileDef);
        profileFace.Area.Should().BeApproximately(Math.PI * Math.Pow(outerRadius, 2), 1e-9);
    }

    [Theory]
    [InlineData(200, 10)]
    [InlineData(200, 0)]
    public void Can_build_circle_hollow_profile_def(double outerRadius, double wallThickness)
    {
        var profileFactory = _service.ProfileFactory;
        var circleHollowProfileDef = IfcMoq.CircleHollowProfile(
            radius: outerRadius, wallThickness: wallThickness);
        var face = profileFactory.BuildFace(circleHollowProfileDef);
        double innerRadius;
        if (circleHollowProfileDef.WallThickness > 0)
            innerRadius = outerRadius - wallThickness;
        else
            innerRadius = 0;
        face.Area.Should().BeApproximately(Math.PI * Math.Pow(outerRadius, 2) - Math.PI * Math.Pow(innerRadius, 2), 1e-9);
        var s = profileFactory.BuildWire(circleHollowProfileDef);
        Assert.Throws<XbimGeometryServiceException>(() => profileFactory.BuildEdge(circleHollowProfileDef));
        Assert.Throws<XbimGeometryServiceException>(() => profileFactory.BuildCurve(circleHollowProfileDef));
    }

    [Theory]
    [InlineData(500, 20, 90, 15707.96327)]
    [InlineData(500, 20, 180, 31415.92654)]
    [InlineData(500, 20, 359, 62657.32015)]
    [InlineData(500, 20, 360, 62831.85307)]
    public void Can_build_centre_line_profile_def(double radius, double thickness, double paramEnd, double area)
    {
        var centreLine = IfcMoq.TrimmedCurve2d(IfcMoq.Circle2d(radius: radius), 0, paramEnd);
        var profile = IfcMoq.CenterLineProfile(centreLine, thickness);
        var profileFactory = _service.ProfileFactory;
        if (paramEnd == 360)
        {
            Assert.Throws<XbimGeometryServiceException>(() => profileFactory.BuildFace(profile));
        }
        else
        {
            var face = profileFactory.BuildFace(profile);
            Math.Abs(face.Area).Should().BeApproximately(area, 1e-3);
            var wire = profileFactory.BuildWire(profile);
            var edge = profileFactory.BuildEdge(profile);
            var curve = profileFactory.BuildCurve(profile);
        }
    }
}
