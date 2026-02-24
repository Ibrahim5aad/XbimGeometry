using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class SolidFactoryTests : IDisposable
{
    private readonly ModelGeometryService _service;

    public SolidFactoryTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = new MemoryModel(new EntityFactoryIfc4());
        model.ModelFactors = new XbimModelFactors(1, 0.001, 1e-5);
        _service = new ModelGeometryService(model, loggerFactory);
    }

    public void Dispose() => _service.Dispose();

    #region CSG Tests

    [Fact]
    public void Can_create_csg_block()
    {
        var solidFactory = _service.SolidFactory;
        var blockMoq = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var solid = solidFactory.Build(blockMoq);
        Assert.Equal(XShapeType.Solid, solid.ShapeType);
        var shells = solid.Shells;
        shells.Should().HaveCount(1);
        Assert.Equal(XShapeType.Shell, shells.First().ShapeType);
        var faces = shells.First().Faces;
        faces.Should().HaveCount(6);
        foreach (var face in faces)
        {
            Assert.NotNull(face.OuterBound);
            Assert.Equal(XShapeType.Wire, face.OuterBound.ShapeType);
            face.InnerBounds.Should().BeEmpty();
            Assert.Equal(XShapeType.Face, face.ShapeType);
            var planarSurface = face.Surface as IXPlane;
            Assert.NotNull(planarSurface);
            Assert.Equal(XSurfaceType.IfcPlane, planarSurface.SurfaceType);
            foreach (var edge in face.OuterBound.EdgeLoop)
            {
                Assert.Equal(XShapeType.Edge, edge.ShapeType);
                var lineSeg = edge.EdgeGeometry as IXLine;
                Assert.NotNull(lineSeg);
            }
        }
    }

    [Fact]
    public void Can_create_csg_rectangular_pyramid()
    {
        var solidFactory = _service.SolidFactory;
        var pyramidMoq = IfcMoq.Pyramid(xLen: 10, yLen: 20, height: 30);

        var solid = solidFactory.Build(pyramidMoq);
        Assert.Equal(XShapeType.Solid, solid.ShapeType);
        var shells = solid.Shells;
        shells.Count().Should().Be(1);
        Assert.Equal(XShapeType.Shell, shells.First().ShapeType);
        var faces = shells.First().Faces;
        faces.Count().Should().Be(5);
        foreach (var face in faces)
        {
            Assert.NotNull(face.OuterBound);
            Assert.Equal(XShapeType.Wire, face.OuterBound.ShapeType);
            face.InnerBounds.Should().BeEmpty();
            Assert.Equal(XShapeType.Face, face.ShapeType);
            var planarSurface = face.Surface as IXPlane;
            Assert.NotNull(planarSurface);
            Assert.Equal(XSurfaceType.IfcPlane, planarSurface.SurfaceType);
            foreach (var edge in face.OuterBound.EdgeLoop)
            {
                Assert.Equal(XShapeType.Edge, edge.ShapeType);
                var lineSeg = edge.EdgeGeometry as IXLine;
                Assert.NotNull(lineSeg);
            }
        }
    }

    [Fact]
    public void Can_create_csg_cone()
    {
        var rccMoq = IfcMoq.Cone(radius: 20, height: 30);

        var solidFactory = _service.SolidFactory;
        var solid = solidFactory.Build(rccMoq);
        Assert.Equal(XShapeType.Solid, solid.ShapeType);
        var shells = solid.Shells;
        shells.Count().Should().Be(1);
        Assert.Equal(XShapeType.Shell, shells.First().ShapeType);
        var faces = shells.First().Faces;
        faces.Count().Should().Be(2);
        foreach (var face in faces)
        {
            var s = face.BrepString;
        }
        foreach (var face in faces)
        {
            var outerBound = face.OuterBound;
            Assert.NotNull(outerBound);
            Assert.Equal(XShapeType.Wire, outerBound.ShapeType);
            face.InnerBounds.Should().BeEmpty();
            Assert.Equal(XShapeType.Face, face.ShapeType);
            if (face.Surface is IXConicalSurface conicalSurface)
            {
                Assert.Equal(XSurfaceType.IfcSurfaceOfRevolution, conicalSurface.SurfaceType);
                outerBound.EdgeLoop.Count().Should().Be(3);
                foreach (var edge in outerBound.EdgeLoop)
                {
                    Assert.Equal(XShapeType.Edge, edge.ShapeType);
                    var edgeGeom = edge.EdgeGeometry;
                    if (edgeGeom == null)
                    {
                        var start = edge.EdgeStart.VertexGeometry;
                        var end = edge.EdgeEnd.VertexGeometry;
                        start.X.Should().BeApproximately(end.X, 1e-5);
                        start.Y.Should().BeApproximately(end.Y, 1e-5);
                        start.Z.Should().BeApproximately(end.Z, 1e-5);
                    }
                }
            }
            else
            {
                var planarSurface = (IXPlane)face.Surface;
                Assert.Equal(XSurfaceType.IfcPlane, planarSurface.SurfaceType);
                outerBound.EdgeLoop.Count().Should().Be(1);
                var circleEdge = outerBound.EdgeLoop.First();
                Assert.Equal(XCurveType.IfcCircle, circleEdge.EdgeGeometry.CurveType);
                var circle = circleEdge.EdgeGeometry as IXCircle;
                Assert.NotNull(circle);
                Assert.Equal<double>(rccMoq.BottomRadius, circle.Radius);
            }
        }
    }

    [Fact]
    public void Can_create_csg_cylinder()
    {
        var rccMoq = IfcMoq.Cylinder(radius: 20, height: 30);

        var solidFactory = _service.SolidFactory;
        var solid = solidFactory.Build(rccMoq);
        Assert.Equal(XShapeType.Solid, solid.ShapeType);
        var shells = solid.Shells;
        shells.Count().Should().Be(1);
        Assert.Equal(XShapeType.Shell, shells.First().ShapeType);
        var faces = shells.First().Faces;
        Assert.Equal(3, faces.Count());
        Assert.Equal(2, faces.Count(f => f.Surface.SurfaceType == XSurfaceType.IfcPlane));
        Assert.Equal(1, faces.Count(f => f.Surface.SurfaceType == XSurfaceType.IfcCylindricalSurface));
        foreach (var face in faces)
        {
            var outerBound = face.OuterBound;
            Assert.NotNull(outerBound);
            Assert.Equal(XShapeType.Wire, outerBound.ShapeType);
            face.InnerBounds.Should().BeEmpty();
            Assert.Equal(XShapeType.Face, face.ShapeType);
            if (face.Surface is IXCylindricalSurface cylindricalSurface)
            {
                Assert.Equal(XSurfaceType.IfcCylindricalSurface, cylindricalSurface.SurfaceType);
                outerBound.EdgeLoop.Should().HaveCount(3);
                outerBound.EdgeLoop.Count(e => e.EdgeGeometry.CurveType == XCurveType.IfcCircle).Should().Be(2);
                outerBound.EdgeLoop.Count(e => e.EdgeGeometry.CurveType == XCurveType.IfcLine).Should().Be(1);
            }
            else
            {
                var planarSurface = (IXPlane)face.Surface;
                Assert.Equal(XSurfaceType.IfcPlane, planarSurface.SurfaceType);
                outerBound.EdgeLoop.Should().HaveCount(1);
                var circleEdge = outerBound.EdgeLoop.First();
                Assert.Equal(XCurveType.IfcCircle, circleEdge.EdgeGeometry.CurveType);
                var circle = circleEdge.EdgeGeometry as IXCircle;
                Assert.NotNull(circle);
                Assert.Equal<double>(rccMoq.Radius, circle.Radius);
            }
        }
    }

    [Fact]
    public void Can_create_csg_sphere()
    {
        var sphereMoq = IfcMoq.Sphere(radius: 20);

        var solidFactory = _service.SolidFactory;
        var solid = solidFactory.Build(sphereMoq);
        Assert.Equal(XShapeType.Solid, solid.ShapeType);
        var shells = solid.Shells;
        shells.Should().HaveCount(1);
        Assert.Equal(XShapeType.Shell, shells.First().ShapeType);
        var faces = shells.First().Faces;
        faces.Should().HaveCount(1);
        var face = faces.First();
        Assert.NotNull(face.OuterBound);
        Assert.Equal(XShapeType.Wire, face.OuterBound.ShapeType);
        face.InnerBounds.Should().BeEmpty();
        Assert.Equal(XShapeType.Face, face.ShapeType);
        var sphericalSurface = face.Surface as IXSphericalSurface;
        Assert.NotNull(sphericalSurface);
        Assert.Equal(XSurfaceType.IfcSphericalSurface, sphericalSurface.SurfaceType);
        face.OuterBound.EdgeLoop.Should().HaveCount(3);
        face.OuterBound.EdgeLoop.Count(e => e.EdgeGeometry?.CurveType == XCurveType.IfcCircle).Should().Be(1);
        face.OuterBound.EdgeLoop.Count(e => e.EdgeGeometry == null).Should().Be(2);
    }

    #endregion

    #region Swept Disks

    [Fact]
    public void Can_create_swept_disk_solid_with_line_directrix()
    {
        var solidService = _service.SolidFactory;
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(radius: 30, innerRadius: 15, startParam: 0, endParam: 100);
        var solid = (IXSolid)solidService.Build(ifcSweptDisk);
        Assert.False(solid.IsEmptyShape());
        Assert.True(solid.IsValidShape());
        double volume = solid.Volume;
        volume.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Can_create_swept_disk_solid_with_polyline_directrix()
    {
        var solidService = _service.SolidFactory;
        var points = new (double X, double Y, double Z)[] { (0, 0, 0), (1000, 0, 0), (1000, 1500, 0), (0, 1500, 0) };
        var pline = IfcMoq.Polyline(dim: 3);
        foreach (var (X, Y, Z) in points)
        {
            pline.Points.Add(IfcMoq.CartesianPoint3d(X, Y, Z));
        }
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(directrix: pline, radius: 30, innerRadius: 15, startParam: 0, endParam: null);
        var solid = (IXSolid)solidService.Build(ifcSweptDisk);
        var s = solid.BrepString();
        Assert.False(solid.IsEmptyShape());
        Assert.True(solid.IsValidShape());
        double volume = solid.Volume;
        volume.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Can_create_swept_disk_solid_with_composite_curve_directrix()
    {
        var solidService = _service.SolidFactory;
        var directrix = IfcMoq.TypicalCompositeCurve(_service.CurveFactory, out double totalParametricLength, out double totalLength);
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(directrix: directrix, radius: 10, innerRadius: 8);
        var solid = (IXSolid)solidService.Build(ifcSweptDisk);
        Assert.False(solid.IsEmptyShape());
        Assert.True(solid.IsValidShape());
        double volume = solid.Volume;
        volume.Should().BeApproximately(14052.092819053487, _service.Precision);
    }

    [Theory]
    [InlineData(30, 20, 0, Math.PI, 100)]
    [InlineData(30, 20, Math.PI, 3 * Math.PI / 2, 100)]
    public void Can_create_swept_disk_solid_with_trimmed_circle_directrix(double outerRadius, double innerRadius, double startParam, double endParam, double directrixRadius)
    {
        var solidService = _service.SolidFactory;
        var circle = IfcMoq.Circle3d(radius: 100);
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(directrix: circle, radius: outerRadius, innerRadius: innerRadius, startParam: startParam, endParam: endParam);
        var solid = (IXSolid)solidService.Build(ifcSweptDisk);
#if DEBUG
        var bstr = solid.BrepString();
#endif
        Assert.False(solid.IsEmptyShape());
        Assert.True(solid.IsValidShape());
        double volume = solid.Volume;
        double outerArea = Math.PI * outerRadius * outerRadius;
        double innerArea = Math.PI * innerRadius * innerRadius;
        double directixLen = directrixRadius * (endParam - startParam);
        double desiredVolume = (outerArea - innerArea) * directixLen;
        volume.Should().BeApproximately(desiredVolume, 1e-5);
    }

    [Theory]
    [InlineData(30, 20, 0, Math.PI, 100, 200, 760929)]
    public void Can_create_swept_disk_solid_with_trimmed_elipse_directrix(double outerRadius, double innerRadius, double startParam, double endParam, double semi1, double semi2, double calcVolume)
    {
        var solidService = _service.SolidFactory;
        var ellipse = IfcMoq.Ellipse3d(semi1: semi1, semi2: semi2);
        var ifcSweptDisk = IfcMoq.SweptDiskSolidParametric(directrix: ellipse, radius: outerRadius, innerRadius: innerRadius, startParam: startParam, endParam: endParam);
        var solid = (IXSolid)solidService.Build(ifcSweptDisk);
#if DEBUG
        var bstr = solid.BrepString();
#endif
        Assert.False(solid.IsEmptyShape());
        Assert.True(solid.IsValidShape());
        solid.Volume.Should().BeApproximately(calcVolume, 1);
    }

    #endregion

    #region Swept Areas

    [Fact]
    public void Can_create_swept_area_solid_with_rectangle_profile_def()
    {
        var solidService = _service.SolidFactory;
        var rectProfileDef = IfcMoq.RectangleProfile(xDim: 200, yDim: 400);
        var extrudedArea = IfcMoq.ExtrudedAreaSolid(sweptArea: rectProfileDef, depth: 300);
        var extrusion = (IXSolid)solidService.Build(extrudedArea);
        extrusion.Volume.Should().Be(200 * 400 * 300);
    }

    [Fact]
    public void Can_create_swept_area_solid_with_circle_profile_def()
    {
        var solidService = _service.SolidFactory;
        var circleProfileDef = IfcMoq.CircleProfile(radius: 200);
        var extrudedArea = IfcMoq.ExtrudedAreaSolid(sweptArea: circleProfileDef, depth: 900);
        var extrusion = (IXSolid)solidService.Build(extrudedArea);
        extrusion.Volume.Should().Be((Math.PI * 200 * 200) * 900);
    }

    #endregion
}
