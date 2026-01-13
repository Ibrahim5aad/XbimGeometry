using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class TessellatedFaceSetTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    public TessellatedFaceSetTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;
         _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(SweptSolidTests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    private void SaveBrep(IXShape shape, string name)
    {
        #if DEBUG
        if (shape is XbimShape ns)
        {
            var path = Path.Combine(_brepOutputDir, $"{name}.brep");
            ns.WriteBrep(path);
        }
        #endif
    }

    public void Dispose() => _service.Dispose();

    // ── Triangulated Face Set ────────────────────────────────────────

    [Fact]
    public void TriangulatedFaceSet_Tetrahedron_ProducesValidShape()
    {
        // Arrange: a tetrahedron with 4 vertices and 4 triangular faces
        var coords = IfcMoq.CartesianPointList3D(
            (0, 0, 0),
            (10, 0, 0),
            (5, 10, 0),
            (5, 5, 10));

        var triangles = new (long, long, long)[]
        {
            (1, 3, 2), // bottom (reversed for outward normal)
            (1, 2, 4), // front
            (2, 3, 4), // right
            (1, 4, 3), // left
        };

        var faceSet = IfcMoq.TriangulatedFaceSet(coords, triangles, closed: true);

        // Act
        var shape = _solidFactory.Build(faceSet);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "Tetrahedron");
    }

    [Fact]
    public void TriangulatedFaceSet_ClosedBox_ProducesSolid()
    {
        // Arrange: a 10x10x10 box as 12 triangles (2 per face)
        // 8 vertices of the box
        var coords = IfcMoq.CartesianPointList3D(
            (0, 0, 0),   // 1
            (10, 0, 0),  // 2
            (10, 10, 0), // 3
            (0, 10, 0),  // 4
            (0, 0, 10),  // 5
            (10, 0, 10), // 6
            (10, 10, 10),// 7
            (0, 10, 10));// 8

        var triangles = new (long, long, long)[]
        {
            // Bottom (Z=0) — normal facing -Z
            (1, 4, 2), (4, 3, 2),
            // Top (Z=10) — normal facing +Z
            (5, 6, 8), (6, 7, 8),
            // Front (Y=0) — normal facing -Y
            (1, 2, 5), (2, 6, 5),
            // Back (Y=10) — normal facing +Y
            (3, 4, 7), (4, 8, 7),
            // Left (X=0) — normal facing -X
            (1, 5, 4), (5, 8, 4),
            // Right (X=10) — normal facing +X
            (2, 3, 6), (3, 7, 6),
        };

        var faceSet = IfcMoq.TriangulatedFaceSet(coords, triangles, closed: true);

        // Act
        var shape = _solidFactory.Build(faceSet);

        // Assert: closed set should produce a solid with correct volume
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(1000, 1.0);
        SaveBrep(shape, "ClosedBox");
    }

    [Fact]
    public void TriangulatedFaceSet_OpenSurface_ProducesShell()
    {
        // Arrange: two triangles forming an open surface (not closed)
        var coords = IfcMoq.CartesianPointList3D(
            (0, 0, 0),
            (10, 0, 0),
            (10, 10, 0),
            (0, 10, 0));

        var triangles = new (long, long, long)[]
        {
            (1, 2, 3),
            (1, 3, 4),
        };

        var faceSet = IfcMoq.TriangulatedFaceSet(coords, triangles, closed: false);

        // Act
        var shape = _solidFactory.Build(faceSet);

        // Assert: open set should produce a shell (not a solid)
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        shape.Should().NotBeAssignableTo<IXSolid>();
        SaveBrep(shape, "OpenSurface");
    }

    [Fact]
    public void TriangulatedFaceSet_SkipsDegenerateTriangles()
    {
        // Arrange: include one degenerate triangle (duplicate indices)
        var coords = IfcMoq.CartesianPointList3D(
            (0, 0, 0),
            (10, 0, 0),
            (5, 10, 0),
            (5, 5, 10));

        var triangles = new (long, long, long)[]
        {
            (1, 1, 2), // degenerate — same vertex twice
            (1, 3, 2), // valid
            (1, 2, 4), // valid
            (2, 3, 4), // valid
            (1, 4, 3), // valid
        };

        var faceSet = IfcMoq.TriangulatedFaceSet(coords, triangles, closed: true);

        // Act
        var shape = _solidFactory.Build(faceSet);

        // Assert: should succeed despite the degenerate triangle
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "DegenerateTriangles");
    }

    [Fact]
    public void TriangulatedFaceSet_NullClosed_ProducesShell()
    {
        // Arrange: Closed is null (unspecified) — should default to open shell
        var coords = IfcMoq.CartesianPointList3D(
            (0, 0, 0),
            (10, 0, 0),
            (10, 10, 0),
            (0, 10, 0));

        var triangles = new (long, long, long)[]
        {
            (1, 2, 3),
            (1, 3, 4),
        };

        var faceSet = IfcMoq.TriangulatedFaceSet(coords, triangles, closed: null);

        // Act
        var shape = _solidFactory.Build(faceSet);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
    }

    // ── Polygonal Face Set ───────────────────────────────────────────

    [Fact]
    public void PolygonalFaceSet_Box_ProducesValidSolid()
    {
        // Arrange: a 10x20x30 box as 6 quadrilateral faces
        var coords = IfcMoq.CartesianPointList3D(
            (0, 0, 0),    // 1
            (10, 0, 0),   // 2
            (10, 20, 0),  // 3
            (0, 20, 0),   // 4
            (0, 0, 30),   // 5
            (10, 0, 30),  // 6
            (10, 20, 30), // 7
            (0, 20, 30)); // 8

        var faces = new[]
        {
            IfcMoq.IndexedPolygonalFace(1, 4, 3, 2), // bottom
            IfcMoq.IndexedPolygonalFace(5, 6, 7, 8), // top
            IfcMoq.IndexedPolygonalFace(1, 2, 6, 5), // front
            IfcMoq.IndexedPolygonalFace(3, 4, 8, 7), // back
            IfcMoq.IndexedPolygonalFace(1, 5, 8, 4), // left
            IfcMoq.IndexedPolygonalFace(2, 3, 7, 6), // right
        };

        var faceSet = IfcMoq.PolygonalFaceSet(coords, faces, closed: true);

        // Act
        var shape = _solidFactory.Build(faceSet);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(10 * 20 * 30, 1.0);
        SaveBrep(shape, "PolygonalBox");
    }

    [Fact]
    public void PolygonalFaceSet_Pentagon_ProducesValidShape()
    {
        // Arrange: a pentagonal prism top/bottom with 5-sided polygons
        double r = 10;
        double h = 20;
        double cos36 = Math.Cos(Math.PI / 5);
        double sin36 = Math.Sin(Math.PI / 5);
        double cos72 = Math.Cos(2 * Math.PI / 5);
        double sin72 = Math.Sin(2 * Math.PI / 5);

        var coords = IfcMoq.CartesianPointList3D(
            // Bottom pentagon (z=0)
            (r, 0, 0),               // 1
            (r * cos72, r * sin72, 0), // 2
            (-r * cos36, r * sin36, 0),// 3
            (-r * cos36, -r * sin36, 0),// 4
            (r * cos72, -r * sin72, 0), // 5
            // Top pentagon (z=h)
            (r, 0, h),               // 6
            (r * cos72, r * sin72, h), // 7
            (-r * cos36, r * sin36, h),// 8
            (-r * cos36, -r * sin36, h),// 9
            (r * cos72, -r * sin72, h)); // 10

        var faces = new[]
        {
            IfcMoq.IndexedPolygonalFace(1, 5, 4, 3, 2),     // bottom (reversed)
            IfcMoq.IndexedPolygonalFace(6, 7, 8, 9, 10),    // top
            IfcMoq.IndexedPolygonalFace(1, 2, 7, 6),        // side 1
            IfcMoq.IndexedPolygonalFace(2, 3, 8, 7),        // side 2
            IfcMoq.IndexedPolygonalFace(3, 4, 9, 8),        // side 3
            IfcMoq.IndexedPolygonalFace(4, 5, 10, 9),       // side 4
            IfcMoq.IndexedPolygonalFace(5, 1, 6, 10),       // side 5
        };

        var faceSet = IfcMoq.PolygonalFaceSet(coords, faces, closed: true);

        // Act
        var shape = _solidFactory.Build(faceSet);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "PentagonalPrism");
    }

    [Fact]
    public void PolygonalFaceSet_OpenFaces_ProducesShell()
    {
        // Arrange: two quad faces forming an open surface (L-shape)
        var coords = IfcMoq.CartesianPointList3D(
            (0, 0, 0),
            (10, 0, 0),
            (10, 10, 0),
            (0, 10, 0),
            (10, 0, 10),
            (10, 10, 10));

        var faces = new[]
        {
            IfcMoq.IndexedPolygonalFace(1, 2, 3, 4),
            IfcMoq.IndexedPolygonalFace(2, 5, 6, 3),
        };

        var faceSet = IfcMoq.PolygonalFaceSet(coords, faces, closed: false);

        // Act
        var shape = _solidFactory.Build(faceSet);

        // Assert: open set should produce a shell
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        shape.Should().NotBeAssignableTo<IXSolid>();
        SaveBrep(shape, "PolygonalOpenSurface");
    }

    [Fact]
    public void PolygonalFaceSet_WithVoids_ProducesValidShape()
    {
        // Arrange: a single face with a rectangular void
        var coords = IfcMoq.CartesianPointList3D(
            // Outer boundary
            (0, 0, 0),   // 1
            (20, 0, 0),  // 2
            (20, 20, 0), // 3
            (0, 20, 0),  // 4
            // Inner boundary (void)
            (5, 5, 0),   // 5
            (15, 5, 0),  // 6
            (15, 15, 0), // 7
            (5, 15, 0)); // 8

        var faces = new IIfcIndexedPolygonalFace[]
        {
            IfcMoq.IndexedPolygonalFaceWithVoids(
                new long[] { 1, 2, 3, 4 },
                new long[] { 5, 8, 7, 6 }),
        };

        var faceSet = IfcMoq.PolygonalFaceSet(coords, faces, closed: false);

        // Act
        var shape = _solidFactory.Build(faceSet);

        // Assert
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "PolygonalFaceWithVoid");
    }

    [Fact]
    public void PolygonalFaceSet_MissingCoordinates_Throws()
    {
        // Arrange: face set with null coordinates
        var moq = new Moq.Mock<IIfcPolygonalFaceSet>
        {
            DefaultValue = Moq.DefaultValue.Mock,
            DefaultValueProvider = new MoqDefaultBehaviourProvider()
        }.SetupAllProperties();
        moq.SetupGet(p => p.Coordinates).Returns((IIfcCartesianPointList3D)null!);
        moq.SetupGet(p => p.Faces).Returns(new ItemListMoq<IIfcIndexedPolygonalFace>());
        var faceSet = moq.Object;

        // Act & Assert
        var act = () => _solidFactory.Build(faceSet);
        act.Should().Throw<XbimGeometryServiceException>()
           .WithMessage("*missing Coordinates*");
    }
}
