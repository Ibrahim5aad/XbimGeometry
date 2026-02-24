using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class AxisAlignedBBsTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly GeometryPrimitives _geometryPrimitives;
    private const double TOLERANCE = 1e-5;

    public AxisAlignedBBsTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _geometryPrimitives = new GeometryPrimitives();
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void CanTransformBoundingBox()
    {
        var solidService = _service.SolidFactory;
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(
            radius: 30, innerRadius: 15, startParam: 0, endParam: 100);
        var solid = (IXSolid)solidService.Build(ifcSweptDisk);
        var bb = solid.Bounds();

        foreach (var location in Locations)
        {
            var transformed = bb.Transformed(location);

            transformed.Should().NotBeNull();
            transformed.Centroid.X.Should().BeApproximately((bb.Centroid.X * location.Scale) + location.OffsetX, TOLERANCE);
            transformed.Centroid.Y.Should().BeApproximately((bb.Centroid.Y * location.Scale) + location.OffsetY, TOLERANCE);
            transformed.Centroid.Z.Should().BeApproximately((bb.Centroid.Z * location.Scale) + location.OffsetZ, TOLERANCE);
            transformed.LenX.Should().BeApproximately(bb.LenX * location.Scale, TOLERANCE);
            transformed.LenY.Should().BeApproximately(bb.LenY * location.Scale, TOLERANCE);
            transformed.LenZ.Should().BeApproximately(bb.LenZ * location.Scale, TOLERANCE);
        }
    }

    #region Helpers

    public IEnumerable<IXLocation> Locations
    {
        get
        {
            return new List<IXLocation>()
            {
                Location(2, 2, 2, 1, 0, 0, 0, 1), // translation
                Location(0, 0, 0, 0.5, 0, 0, 0, 1), // scale down
            };
        }
    }

    private IXLocation Location(
        double tx, double ty, double tz, double sc, double qw, double qx, double qy, double qz)
    {
        return _geometryPrimitives.BuildLocation(tx, ty, tz, sc, qw, qx, qy, qz);
    }

    #endregion
}
