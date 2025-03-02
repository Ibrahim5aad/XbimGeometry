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
}
