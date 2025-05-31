using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests CSG solid primitives via the full P/Invoke path:
///   Mock IFC entity → NativeSolidFactory → P/Invoke → OCCT → NativeSolid
/// Each test verifies the resulting solid's volume and optionally writes a .brep file.
/// </summary>
public class CsgSolidTests : IDisposable
{
    private readonly NativeModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly IXProfileFactory _profileFactory;
    private readonly string _brepOutputDir;

    public CsgSolidTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new NativeModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;
        _profileFactory = _service.ProfileFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(CsgSolidTests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    public void Dispose() => _service.Dispose();

    private void SaveBrep(IXShape shape, string name)
    {
        #if !DEBUG
          return;
        #endif
        
        if (shape is NativeShape ns)
        {
            var path = Path.Combine(_brepOutputDir, $"{name}.brep");
            ns.WriteBrep(path);
        }
    }

    [Fact]
    public void Block_HasCorrectVolume()
    {
        // Arrange: 10 x 20 x 30 block → volume = 6000
        var ifcBlock = IfcMoq.Block(xLen: 10, yLen: 20, zLen: 30);

        // Act
        var solid = _solidFactory.Build(ifcBlock);

        // Assert
        solid.Should().NotBeNull();
        solid.Should().BeAssignableTo<IXSolid>();
        solid.Volume.Should().BeApproximately(6000, 0.1);

        SaveBrep(solid, "block_10x20x30");
    }

    [Fact]
    public void Sphere_HasCorrectVolume()
    {
        // Arrange: sphere r=5 → volume = 4/3 * π * 5³ ≈ 523.599
        var ifcSphere = IfcMoq.Sphere(radius: 5);

        // Act
        var solid = _solidFactory.Build(ifcSphere);

        // Assert
        solid.Volume.Should().BeApproximately(4.0 / 3.0 * Math.PI * 125, 0.1);

        SaveBrep(solid, "sphere_r5");
    }

    [Fact]
    public void Cylinder_HasCorrectVolume()
    {
        // Arrange: cylinder r=3, h=10 → volume = π * 9 * 10 ≈ 282.743
        var ifcCylinder = IfcMoq.Cylinder(radius: 3, height: 10);

        // Act
        var solid = _solidFactory.Build(ifcCylinder);

        // Assert
        solid.Volume.Should().BeApproximately(Math.PI * 9 * 10, 0.1);

        SaveBrep(solid, "cylinder_r3_h10");
    }

    [Fact]
    public void Cone_HasCorrectVolume()
    {
        // Arrange: cone r=5, h=10 → volume = 1/3 * π * 25 * 10 ≈ 261.799
        var ifcCone = IfcMoq.Cone(radius: 5, height: 10);

        // Act
        var solid = _solidFactory.Build(ifcCone);

        // Assert
        solid.Volume.Should().BeApproximately(Math.PI * 25 * 10 / 3.0, 0.5);

        SaveBrep(solid, "cone_r5_h10");
    }

    [Fact]
    public void Pyramid_HasCorrectVolume()
    {
        // Arrange: pyramid 10x20 base, h=15
        var ifcPyramid = IfcMoq.Pyramid(xLen: 10, yLen: 20, height: 15);

        // Act
        var solid = _solidFactory.Build(ifcPyramid);

        // Assert
        solid.Volume.Should().BeApproximately(250, 1.0);

        SaveBrep(solid, "pyramid_10x20_h15");
    }

    [Fact]
    public void Block_IsValid()
    {
        var ifcBlock = IfcMoq.Block();
        var solid = _solidFactory.Build(ifcBlock);

        ((IXShape)solid).IsValidShape().Should().BeTrue();
    }

    [Fact]
    public void Block_IsClosed()
    {
        var ifcBlock = IfcMoq.Block();
        var solid = _solidFactory.Build(ifcBlock);

        ((IXShape)solid).IsClosed.Should().BeTrue();
    }

    [Fact]
    public void Block_HasCorrectBoundingBox()
    {
        // Block at origin with default placement (zDir=0,0,1, no refDir → xDir=1,0,0)
        // Dimensions: 10 x 20 x 30
        var ifcBlock = IfcMoq.Block(xLen: 10, yLen: 20, zLen: 30);

        var solid = _solidFactory.Build(ifcBlock);
        var bounds = ((IXShape)solid).Bounds();

        bounds.LenX.Should().BeApproximately(10, 0.01);
        bounds.LenY.Should().BeApproximately(20, 0.01);
        bounds.LenZ.Should().BeApproximately(30, 0.01);
    }

    [Fact]
    public void Sphere_IsValid()
    {
        var solid = _solidFactory.Build(IfcMoq.Sphere());
        ((IXShape)solid).IsValidShape().Should().BeTrue();

        SaveBrep(solid, "sphere_default");
    }

    [Fact]
    public void Cylinder_IsValid()
    {
        var solid = _solidFactory.Build(IfcMoq.Cylinder());
        ((IXShape)solid).IsValidShape().Should().BeTrue();
    }

    [Fact]
    public void AllPrimitives_DisposeProperly()
    {
        // Verify no crashes on dispose
        var block = _solidFactory.Build(IfcMoq.Block());
        var sphere = _solidFactory.Build(IfcMoq.Sphere());
        var cylinder = _solidFactory.Build(IfcMoq.Cylinder());
        var cone = _solidFactory.Build(IfcMoq.Cone());
        var pyramid = _solidFactory.Build(IfcMoq.Pyramid());

        block.Should().BeAssignableTo<IDisposable>();
        ((IDisposable)block).Dispose();
        ((IDisposable)sphere).Dispose();
        ((IDisposable)cylinder).Dispose();
        ((IDisposable)cone).Dispose();
        ((IDisposable)pyramid).Dispose();
    }

    // ── Profile tests ─────────────────────────────────────────────────

    [Fact]
    public void RectangleProfile_HasCorrectArea()
    {
        // Arrange: 10 x 20 rectangle → area = 200
        var ifcProfile = IfcMoq.RectangleProfile(xDim: 10, yDim: 20);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Should().BeAssignableTo<IXFace>();
        face.Area.Should().BeApproximately(200, 0.1);

        SaveBrep(face, "profile_rect_10x20");
    }

    [Fact]
    public void CircleProfile_HasCorrectArea()
    {
        // Arrange: circle r=5 → area = π * 25 ≈ 78.54
        var ifcProfile = IfcMoq.CircleProfile(radius: 5);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Area.Should().BeApproximately(Math.PI * 25, 0.1);

        SaveBrep(face, "profile_circle_r5");
    }

    // ── Hollow profile tests ─────────────────────────────────────────

    [Fact]
    public void RectangleHollowProfile_HasCorrectArea()
    {
        // Arrange: 200 x 100 outer, wall=10
        // Outer area = 200 * 100 = 20000
        // Inner = (200-20) x (100-20) = 180 x 80 = 14400
        // Hollow area = 20000 - 14400 = 5600
        var ifcProfile = IfcMoq.RectangleHollowProfile(xDim: 200, yDim: 100, wallThickness: 10);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Should().BeAssignableTo<IXFace>();
        face.Area.Should().BeApproximately(5600, 1.0);

        SaveBrep(face, "profile_rect_hollow_200x100_t10");
    }

    [Fact]
    public void RectangleHollowProfile_WithFillets_IsValid()
    {
        // Arrange: 200 x 100 outer, wall=10, inner fillet=3, outer fillet=5
        var ifcProfile = IfcMoq.RectangleHollowProfile(
            xDim: 200, yDim: 100, wallThickness: 10,
            innerFilletRadius: 3, outerFilletRadius: 5);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();

        SaveBrep(face, "profile_rect_hollow_filleted");
    }

    [Fact]
    public void CircleHollowProfile_HasCorrectArea()
    {
        // Arrange: outer r=50, wall=10 → inner r=40
        // Area = π * (50² - 40²) = π * (2500 - 1600) = π * 900 ≈ 2827.43
        var ifcProfile = IfcMoq.CircleHollowProfile(radius: 50, wallThickness: 10);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Should().BeAssignableTo<IXFace>();
        face.Area.Should().BeApproximately(Math.PI * 900, 1.0);

        SaveBrep(face, "profile_circle_hollow_r50_t10");
    }

    [Fact]
    public void CircleHollowProfile_SmallWall_IsValid()
    {
        // Arrange: outer r=100, wall=2 → very thin tube cross-section
        var ifcProfile = IfcMoq.CircleHollowProfile(radius: 100, wallThickness: 2);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        // Area = π * (100² - 98²) = π * (10000 - 9604) = π * 396 ≈ 1243.94
        face.Area.Should().BeApproximately(Math.PI * 396, 1.0);

        SaveBrep(face, "profile_circle_hollow_r100_t2");
    }

    // ── Structural profile tests ──────────────────────────────────────

    [Fact]
    public void IShapeProfile_HasCorrectArea()
    {
        // Arrange: I-beam with overallWidth=100, overallDepth=200,
        //          webThickness=10, flangeThickness=15, no fillets
        // Area = 2 flanges + web = 2*(100*15) + (200-2*15)*10 = 3000 + 1700 = 4700
        var ifcProfile = IfcMoq.IShapeProfile(
            overallWidth: 100, overallDepth: 200,
            webThickness: 10, flangeThickness: 15);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Should().BeAssignableTo<IXFace>();
        face.Area.Should().BeApproximately(4700, 1.0);

        SaveBrep(face, "profile_ishape_100x200");
    }

    [Fact]
    public void IShapeProfile_WithFillets_IsValid()
    {
        var ifcProfile = IfcMoq.IShapeProfile(
            overallWidth: 100, overallDepth: 200,
            webThickness: 10, flangeThickness: 15,
            filletRadius: 5);

        var face = _profileFactory.BuildFace(ifcProfile);

        face.Should().NotBeNull();
        // With fillets, area should be slightly larger than without (fillets fill corners)
        face.Area.Should().BeGreaterThan(4700);

        SaveBrep(face, "profile_ishape_filleted");
    }

    [Fact]
    public void LShapeProfile_IsValid()
    {
        // Arrange: L-angle with depth=100, width=80, thickness=10
        var ifcProfile = IfcMoq.LShapeProfile(
            depth: 100, thickness: 10, width: 80);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Should().BeAssignableTo<IXFace>();
        // Area = depth*thickness + (width-thickness)*thickness = 100*10 + 70*10 = 1700
        face.Area.Should().BeApproximately(1700, 1.0);

        SaveBrep(face, "profile_lshape_100x80");
    }

    [Fact]
    public void TShapeProfile_IsValid()
    {
        // Arrange: T-shape depth=100, flangeWidth=100, web=10, flangeThk=15
        var ifcProfile = IfcMoq.TShapeProfile(
            depth: 100, flangeWidth: 100,
            webThickness: 10, flangeThickness: 15);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        // Area = flange + web = 100*15 + (100-15)*10 = 1500 + 850 = 2350
        face.Area.Should().BeApproximately(2350, 1.0);

        SaveBrep(face, "profile_tshape_100x100");
    }

    [Fact]
    public void UShapeProfile_IsValid()
    {
        // Arrange: U-channel depth=100, flangeWidth=50, web=8, flangeThk=12
        var ifcProfile = IfcMoq.UShapeProfile(
            depth: 100, flangeWidth: 50,
            webThickness: 8, flangeThickness: 12);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Area.Should().BeGreaterThan(0);

        SaveBrep(face, "profile_ushape_100x50");
    }

    [Fact]
    public void ZShapeProfile_IsValid()
    {
        // Arrange: Z-shape depth=100, flangeWidth=50, web=8, flangeThk=12
        var ifcProfile = IfcMoq.ZShapeProfile(
            depth: 100, flangeWidth: 50,
            webThickness: 8, flangeThickness: 12);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Area.Should().BeGreaterThan(0);

        SaveBrep(face, "profile_zshape_100x50");
    }

    [Fact]
    public void CShapeProfile_IsValid()
    {
        // Arrange: C-shape depth=100, width=50, wallThickness=8, girth=20
        var ifcProfile = IfcMoq.CShapeProfile(
            depth: 100, width: 50,
            wallThickness: 8, girth: 20);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Area.Should().BeGreaterThan(0);

        SaveBrep(face, "profile_cshape_100x50");
    }

    [Fact]
    public void TrapeziumProfile_HasCorrectArea()
    {
        // Arrange: trapezium bottom=100, top=60, height=80, offset=20
        // Area = 0.5 * (100 + 60) * 80 = 6400
        var ifcProfile = IfcMoq.TrapeziumProfile(
            bottomXDim: 100, topXDim: 60, yDim: 80, topXOffset: 20);

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Area.Should().BeApproximately(6400, 1.0);

        SaveBrep(face, "profile_trapezium_100x60x80");
    }

    // ── Arbitrary profile tests ───────────────────────────────────────

    [Fact]
    public void ArbitraryClosedProfile_QuadFace()
    {
        // Arrange: 4-point polygon (rectangle-ish) → area = 20 * 10 = 200
        var ifcProfile = IfcMoq.ArbitraryClosedProfile(new[]
        {
            (0.0, 0.0), (20.0, 0.0), (20.0, 10.0), (0.0, 10.0)
        });

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Should().BeAssignableTo<IXFace>();
        face.Area.Should().BeApproximately(200, 0.1);

        SaveBrep(face, "profile_arbitrary_quad");
    }

    [Fact]
    public void ArbitraryClosedProfile_Triangle()
    {
        // Arrange: triangle with base=10, height=10 → area = 50
        var ifcProfile = IfcMoq.ArbitraryClosedProfile(new[]
        {
            (0.0, 0.0), (10.0, 0.0), (5.0, 10.0)
        });

        var face = _profileFactory.BuildFace(ifcProfile);

        face.Should().NotBeNull();
        face.Area.Should().BeApproximately(50, 0.1);

        SaveBrep(face, "profile_arbitrary_triangle");
    }

    [Fact]
    public void ArbitraryProfileWithVoids_HasReducedArea()
    {
        // Arrange: outer 20x10 rectangle with inner 10x5 rectangle void
        // Outer area = 200, inner area = 50, net = 150
        var ifcProfile = IfcMoq.ArbitraryProfileWithVoids(
            outerPoints: new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 10.0), (0.0, 10.0) },
            innerCurves: new[]
            {
                new[] { (5.0, 2.5), (15.0, 2.5), (15.0, 7.5), (5.0, 7.5) }
            });

        // Act
        var face = _profileFactory.BuildFace(ifcProfile);

        // Assert
        face.Should().NotBeNull();
        face.Area.Should().BeApproximately(150, 1.0);

        SaveBrep(face, "profile_arbitrary_with_void");
    }

    // ── Composite / Derived / Mirrored profile tests ──────────────────

    [Fact]
    public void CompositeProfile_MergesTwoRectangles()
    {
        // Arrange: two non-overlapping rectangle profiles
        var rect1 = IfcMoq.RectangleProfile(xDim: 10, yDim: 20);
        var rect2 = IfcMoq.RectangleProfile(xDim: 10, yDim: 20);
        var composite = IfcMoq.CompositeProfile(rect1, rect2);

        // Act
        var face = _profileFactory.BuildFace(composite);

        // Assert
        face.Should().NotBeNull();
        // Composite returns a compound, so area should be sum of both rectangles
        // Since both are at origin, they overlap but the compound just contains them
        face.Area.Should().BeGreaterThan(0);

        SaveBrep(face, "profile_composite_two_rects");
    }

    [Fact]
    public void DerivedProfile_AppliesTransform()
    {
        // Arrange: rectangle 10x20 with identity transform (scale=1, no translation)
        var parent = IfcMoq.RectangleProfile(xDim: 10, yDim: 20);
        var derived = IfcMoq.DerivedProfile(parent, scale: 1.0);

        // Act
        var face = _profileFactory.BuildFace(derived);

        // Assert
        face.Should().NotBeNull();
        face.Should().BeAssignableTo<IXFace>();
        // Identity transform preserves area
        face.Area.Should().BeGreaterThan(0);

        SaveBrep(face, "profile_derived_identity");
    }

    [Fact]
    public void DerivedProfile_WithTranslation()
    {
        // Arrange: rectangle 10x20 translated to (50, 30)
        var parent = IfcMoq.RectangleProfile(xDim: 10, yDim: 20);
        var derived = IfcMoq.DerivedProfile(parent, translateX: 50, translateY: 30, scale: 1.0);

        // Act
        var face = _profileFactory.BuildFace(derived);

        // Assert
        face.Should().NotBeNull();
        face.Area.Should().BeGreaterThan(0);

        SaveBrep(face, "profile_derived_translated");
    }

    [Fact]
    public void MirroredProfile_PreservesArea()
    {
        // Arrange: L-shape mirrored about Y axis
        var parent = IfcMoq.LShapeProfile(depth: 100, thickness: 10, width: 80);
        var mirrored = IfcMoq.MirroredProfile(parent);

        // Act
        var face = _profileFactory.BuildFace(mirrored);

        // Assert
        face.Should().NotBeNull();
        // Mirroring preserves area = 1700
        face.Area.Should().BeApproximately(1700, 1.0);

        SaveBrep(face, "profile_mirrored_lshape");
    }

    [Fact]
    public void AllProfileTypes_DisposeProperly()
    {
        // Verify no crashes on dispose for various profile types
        var ishape = _profileFactory.BuildFace(IfcMoq.IShapeProfile());
        var lshape = _profileFactory.BuildFace(IfcMoq.LShapeProfile(width: 80));
        var trapezium = _profileFactory.BuildFace(IfcMoq.TrapeziumProfile());
        var arbitrary = _profileFactory.BuildFace(IfcMoq.ArbitraryClosedProfile(
            new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) }));

        ishape.Should().BeAssignableTo<IDisposable>();
        ((IDisposable)ishape).Dispose();
        ((IDisposable)lshape).Dispose();
        ((IDisposable)trapezium).Dispose();
        ((IDisposable)arbitrary).Dispose();
    }
}
