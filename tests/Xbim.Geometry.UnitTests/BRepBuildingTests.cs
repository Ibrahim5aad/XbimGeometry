using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Services;
using Xbim.Ifc4;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

public class BRepBuildingTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    public BRepBuildingTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
    }

    public void Dispose() => _loggerFactory.Dispose();

    private ModelGeometryService CreateService()
    {
        return new ModelGeometryService(new MemoryModel(new EntityFactoryIfc4x1()), _loggerFactory);
    }

    [Fact]
    public void Can_Create_Plane()
    {
        using var modelService = CreateService();

        var plane = modelService.SurfaceFactory.BuildPlane(
                        modelService.GeometryFactory.BuildPoint3d(10, 20, 30),
                        modelService.GeometryFactory.BuildDirection3d(0, 0, 1));
        plane.Axis.Is3d.Should().BeTrue();
        plane.Axis.Z.Should().Be(1);
        plane.Axis.Y.Should().Be(0);
        plane.Axis.X.Should().Be(0);
        plane.Location.Is3d.Should().BeTrue();
        plane.Location.Z.Should().Be(30);
        plane.Location.Y.Should().Be(20);
        plane.Location.X.Should().Be(10);
        plane.RefDirection.Z.Should().Be(0);
        plane.RefDirection.Y.Should().Be(0);
        plane.RefDirection.X.Should().Be(1);
    }

    [Fact]
    public void Can_Create_WireAndFace()
    {
        using var modelService = CreateService();
        var plane = modelService.SurfaceFactory.BuildPlane(
                        modelService.GeometryFactory.BuildPoint3d(10, 20, 30),
                        modelService.GeometryFactory.BuildDirection3d(0, 0, 1));
        var outerBound = new IXPoint[]
        {
            modelService.GeometryFactory.BuildPoint3d(0, 0, 0),
            modelService.GeometryFactory.BuildPoint3d(10, 0, 0),
            modelService.GeometryFactory.BuildPoint3d(10, 20, 0),
            modelService.GeometryFactory.BuildPoint3d(0, 20, 0),
            modelService.GeometryFactory.BuildPoint3d(0, 0, 0)
        };
        var wire = modelService.WireFactory.BuildWire(outerBound);

        wire.IsClosed.Should().BeTrue();
        wire.ContourArea.Should().Be(200);
        wire.Length.Should().Be(60);
        var face = modelService.FaceFactory.BuildFace(plane, new[] { wire });
        face.IsClosed.Should().BeTrue();
        face.Area.Should().BeApproximately(200, 1e-5);
    }

    /// <summary>
    /// This test builds a faulty face with wire bounds that are incorrectly wound and fixes the resulting shape
    /// Demos the possiblility of build faces from poorly defined data
    /// </summary>
    [Fact]
    public void Can_Create_Wire_InnerBoundAndFace()
    {
        using var modelService = CreateService();

        var plane = modelService.SurfaceFactory.BuildPlane(
                        modelService.GeometryFactory.BuildPoint3d(10, 20, 0),
                        modelService.GeometryFactory.BuildDirection3d(0, 0, 1));
        var outerBound = new IXPoint[]
        {
            modelService.GeometryFactory.BuildPoint3d(0, 0, 0),
            modelService.GeometryFactory.BuildPoint3d(10, 0, 0),
            modelService.GeometryFactory.BuildPoint3d(10, 20, 0),
            modelService.GeometryFactory.BuildPoint3d(0, 20, 0),
            modelService.GeometryFactory.BuildPoint3d(0, 0, 0)
        };
        var innerBound = new IXPoint[]
        {
            modelService.GeometryFactory.BuildPoint3d(1, 1, 0),
            modelService.GeometryFactory.BuildPoint3d(6, 1, 0),
            modelService.GeometryFactory.BuildPoint3d(6, 6, 0),
            modelService.GeometryFactory.BuildPoint3d(1, 6, 0),
            modelService.GeometryFactory.BuildPoint3d(1, 1, 0)
        };
        var outerWire = modelService.WireFactory.BuildWire(outerBound.Reverse().ToArray());
        outerWire.IsClosed.Should().BeTrue();
        outerWire.ContourArea.Should().Be(200);
        outerWire.Length.Should().Be(60);

        var innerWire = modelService.WireFactory.BuildWire(innerBound);
        innerWire.IsClosed.Should().BeTrue();
        innerWire.ContourArea.Should().Be(25);
        innerWire.Length.Should().Be(20);

        var face = modelService.FaceFactory.BuildFace(plane, new[] { outerWire, innerWire });
        face.IsClosed.Should().BeTrue();
        var normal = modelService.GeometryFactory.NormalAt(face, modelService.GeometryFactory.BuildPoint3d(0, 0, 0), 1e-5);
        normal.Z.Should().Be(1);

        var fixedFaces = modelService.ShapeFactory.FixFace(face);
        fixedFaces.Count().Should().Be(1);
        var fixedFace = fixedFaces.First();
#if DEBUG
        var str = fixedFace.BrepString();
#endif

        fixedFace.Area.Should().BeApproximately(200 - 25, 1e-5, "If this  area is negative the face fix has not worked");
    }

    [Fact]
    public void Can_Create_Faces_From_Multiple_Outer_Bounds()
    {
        using var modelService = CreateService();

        var plane = modelService.SurfaceFactory.BuildPlane(
                        modelService.GeometryFactory.BuildPoint3d(10, 20, 0),
                        modelService.GeometryFactory.BuildDirection3d(0, 0, 1));
        var outerBound1 = new IXPoint[]
        {
            modelService.GeometryFactory.BuildPoint3d(0, 0, 0),
            modelService.GeometryFactory.BuildPoint3d(10, 0, 0),
            modelService.GeometryFactory.BuildPoint3d(10, 20, 0),
            modelService.GeometryFactory.BuildPoint3d(0, 20, 0),
            modelService.GeometryFactory.BuildPoint3d(0, 0, 0)
        };
        var innerBound = new IXPoint[]
        {
            modelService.GeometryFactory.BuildPoint3d(1, 1, 0),
            modelService.GeometryFactory.BuildPoint3d(6, 1, 0),
            modelService.GeometryFactory.BuildPoint3d(6, 6, 0),
            modelService.GeometryFactory.BuildPoint3d(1, 6, 0),
            modelService.GeometryFactory.BuildPoint3d(1, 1, 0)
        };
        var outerBound2 = new IXPoint[]
        {
            modelService.GeometryFactory.BuildPoint3d(100, 0, 0),
            modelService.GeometryFactory.BuildPoint3d(110, 0, 0),
            modelService.GeometryFactory.BuildPoint3d(110, 20, 0),
            modelService.GeometryFactory.BuildPoint3d(100, 20, 0),
            modelService.GeometryFactory.BuildPoint3d(100, 0, 0)
        };
        var outerWire1 = modelService.WireFactory.BuildWire(outerBound1);
        var innerWire = modelService.WireFactory.BuildWire(innerBound);
        var outerWire2 = modelService.WireFactory.BuildWire(outerBound2);
        var face = modelService.FaceFactory.BuildFace(plane, new[] { outerWire1, innerWire, outerWire2 });

        var fixedFaces = modelService.ShapeFactory.FixFace(face);

        fixedFaces.Count().Should().Be(2, "Two outer bounds have been provided, this should cause the fixer to split the face");
        var fixedFace1 = fixedFaces.First();
        var fixedFace2 = fixedFaces.Last();
#if DEBUG
        var str1 = fixedFace1.BrepString();
        var str2 = fixedFace2.BrepString();
#endif

        fixedFace1.Area.Should().BeApproximately(200 - 25, 1e-5, "If this  area is negative the face fix has not worked");
        fixedFace2.Area.Should().BeApproximately(200, 1e-5, "If this  area is negative the face fix has not worked");
    }
}
