using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Integration tests for the V6 geometry engine Build method.
/// These tests exercise the full pipeline: IFC entity → Build() dispatch → factory → P/Invoke → OCCT → IXShape.
/// </summary>
public class GeometryEngineV6Tests : IDisposable
{
    private readonly GeometryConverterFactory _converterFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IXGeometryEngineV6 _engine;
    private readonly string _brepOutputDir;

    public GeometryEngineV6Tests()
    {
        _converterFactory = new GeometryConverterFactory();
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _engine = (IXGeometryEngineV6)_converterFactory.CreateGeometryEngine(model, _loggerFactory);

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(GeometryEngineV6Tests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    public void Dispose()
    {
        ((IDisposable)_engine).Dispose();
        _loggerFactory.Dispose();
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

    #region CSG Primitives via Build()

    [Fact]
    public void Build_Block_ReturnsValidSolid()
    {
        var block = IfcMoq.Block(10, 20, 30);

        var shape = _engine.Build(block);

        shape.Should().NotBeNull();
        shape.ShapeType.Should().Be(XShapeType.Solid);
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(6000, 0.1);
        solid.Shells.Should().HaveCount(1);
        shape.AllFaces().Should().HaveCount(6);
        SaveBrep(shape, "build_block");
    }

    [Fact]
    public void Build_Sphere_ReturnsValidSolid()
    {
        var sphere = IfcMoq.Sphere(radius: 5);

        var shape = _engine.Build(sphere);

        shape.Should().NotBeNull();
        shape.ShapeType.Should().Be(XShapeType.Solid);
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        double expectedVolume = (4.0 / 3.0) * Math.PI * 125; // 4/3 * pi * r^3
        solid.Volume.Should().BeApproximately(expectedVolume, 1.0);
        SaveBrep(shape, "build_sphere");
    }

    [Fact]
    public void Build_Cylinder_ReturnsValidSolid()
    {
        var cylinder = IfcMoq.Cylinder(radius: 3, height: 10);

        var shape = _engine.Build(cylinder);

        shape.Should().NotBeNull();
        shape.ShapeType.Should().Be(XShapeType.Solid);
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        double expectedVolume = Math.PI * 9 * 10; // pi * r^2 * h
        solid.Volume.Should().BeApproximately(expectedVolume, 1.0);
        SaveBrep(shape, "build_cylinder");
    }

    [Fact]
    public void Build_Cone_ReturnsValidSolid()
    {
        var cone = IfcMoq.Cone(radius: 5, height: 10);

        var shape = _engine.Build(cone);

        shape.Should().NotBeNull();
        shape.ShapeType.Should().Be(XShapeType.Solid);
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        double expectedVolume = (1.0 / 3.0) * Math.PI * 25 * 10;
        solid.Volume.Should().BeApproximately(expectedVolume, 1.0);
        SaveBrep(shape, "build_cone");
    }

    #endregion

    #region Swept Solids via Build()

    [Fact]
    public void Build_ExtrudedAreaSolid_RectangleProfile_ReturnsValidSolid()
    {
        var extruded = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(xDim: 100, yDim: 200),
            depth: 50);

        var shape = _engine.Build(extruded);

        shape.Should().NotBeNull();
        shape.ShapeType.Should().Be(XShapeType.Solid);
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(100 * 200 * 50, 1.0);
        solid.Shells.Should().HaveCount(1);
        shape.AllFaces().Should().HaveCount(6);
        SaveBrep(shape, "build_extruded_rect");
    }

    [Fact]
    public void Build_ExtrudedAreaSolid_CircleProfile_ReturnsValidSolid()
    {
        var extruded = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.CircleProfile(radius: 10),
            depth: 100);

        var shape = _engine.Build(extruded);

        shape.Should().NotBeNull();
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        double expectedVolume = Math.PI * 100 * 100; // pi * r^2 * h
        solid.Volume.Should().BeApproximately(expectedVolume, 10.0);
        SaveBrep(shape, "build_extruded_circle");
    }

    [Fact]
    public void Build_RevolvedAreaSolid_ReturnsValidSolid()
    {
        // Revolve a 10x20 rectangle 360° around a Y-axis offset at x=-50
        var revolved = IfcMoq.RevolvedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(xDim: 10, yDim: 20),
            axis: IfcMoq.Axis1Placement(
                IfcMoq.CartesianPoint3d(-50, 0, 0),
                IfcMoq.Direction3d(0, 1, 0)),
            angle: 360);

        var shape = _engine.Build(revolved);

        shape.Should().NotBeNull();
        shape.ShapeType.Should().Be(XShapeType.Solid);
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeGreaterThan(0);
        SaveBrep(shape, "build_revolved");
    }

    [Fact]
    public void Build_ExtrudedAreaSolidTapered_ReturnsValidSolid()
    {
        var tapered = IfcMoq.ExtrudedAreaSolidTapered(
            sweptArea: IfcMoq.RectangleProfile(100, 200),
            endSweptArea: IfcMoq.RectangleProfile(50, 100),
            depth: 100);

        var shape = _engine.Build(tapered);

        shape.Should().NotBeNull();
        shape.ShapeType.Should().Be(XShapeType.Solid);
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeGreaterThan(0);
        SaveBrep(shape, "build_extruded_tapered");
    }

    #endregion

    #region Boolean Operations via Build()

    [Fact]
    public void Build_BooleanDifference_ReturnsValidShape()
    {
        // Cut a small block from a larger block
        var bigBlock = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(100, 100),
            depth: 100);
        var smallBlock = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(50, 50),
            depth: 100);

        var boolResult = IfcMoq.BooleanResult(bigBlock, smallBlock, IfcBooleanOperator.DIFFERENCE);

        var shape = _engine.Build(boolResult);

        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        // Volume should be less than the big block
        if (shape is IXSolid solid)
            solid.Volume.Should().BeLessThan(100 * 100 * 100);
        SaveBrep(shape, "build_boolean_difference");
    }

    [Fact]
    public void Build_BooleanUnion_ReturnsValidShape()
    {
        var block1 = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(100, 100),
            depth: 50);
        var block2 = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(100, 100),
            depth: 50);

        var boolResult = IfcMoq.BooleanResult(block1, block2, IfcBooleanOperator.UNION);

        var shape = _engine.Build(boolResult);

        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "build_boolean_union");
    }

    [Fact]
    public void Build_BooleanClippingResult_ReturnsValidShape()
    {
        // Clip an extruded solid with a half-space plane
        var solid = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(100, 100),
            depth: 100);
        var halfSpace = IfcMoq.HalfSpaceSolid(
            baseSurface: IfcMoq.Plane(),
            agreementFlag: false);

        var clipping = IfcMoq.BooleanClippingResult(solid, halfSpace);

        var shape = _engine.Build(clipping);

        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "build_boolean_clipping");
    }

    #endregion

    #region CSG Solid via Build()

    [Fact]
    public void Build_CsgSolid_WithPrimitiveRoot_ReturnsValidSolid()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var csgSolid = IfcMoq.CsgSolid(block);

        var shape = _engine.Build(csgSolid);

        shape.Should().NotBeNull();
        shape.ShapeType.Should().Be(XShapeType.Solid);
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(6000, 0.1);
        SaveBrep(shape, "build_csg_primitive");
    }

    [Fact]
    public void Build_CsgSolid_WithBooleanTreeRoot_ReturnsValidShape()
    {
        var block = IfcMoq.Block(100, 100, 100);
        var sphere = IfcMoq.Sphere(30);
        var boolResult = IfcMoq.BooleanResult(block, sphere, IfcBooleanOperator.DIFFERENCE);
        var csgSolid = IfcMoq.CsgSolid(boolResult);

        var shape = _engine.Build(csgSolid);

        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue();
        SaveBrep(shape, "build_csg_boolean_tree");
    }

    #endregion

    #region Error Handling

    [Fact]
    public void Build_NullArgument_ThrowsArgumentNull()
    {
        var act = () => _engine.Build(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Factory Delegation Verification

    [Fact]
    public void V6Engine_ExposesAllFactories()
    {
        _engine.SolidFactory.Should().NotBeNull();
        _engine.ProfileFactory.Should().NotBeNull();
        _engine.BooleanFactory.Should().NotBeNull();
        _engine.GeometryFactory.Should().NotBeNull();
        _engine.WexBimMeshFactory.Should().NotBeNull();
        _engine.ShapeBinarySerializer.Should().NotBeNull();
        _engine.CompoundFactory.Should().NotBeNull();
        _engine.VertexFactory.Should().NotBeNull();
        _engine.EdgeFactory.Should().NotBeNull();
        _engine.WireFactory.Should().NotBeNull();
        _engine.FaceFactory.Should().NotBeNull();
        _engine.ShellFactory.Should().NotBeNull();
    }

    [Fact]
    public void V6Engine_ModelPropertiesMatchService()
    {
        var service = _engine.ModelGeometryService;

        _engine.Precision.Should().Be(service.Precision);
        _engine.OneMeter.Should().Be(service.OneMeter);
        _engine.OneFoot.Should().Be(service.OneFoot);
        _engine.OneMillimeter.Should().Be(service.OneMillimeter);
        _engine.RadianFactor.Should().Be(service.RadianFactor);
    }

    #endregion
}
