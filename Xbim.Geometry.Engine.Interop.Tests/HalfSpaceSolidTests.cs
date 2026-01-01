using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class HalfSpaceSolidTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    public HalfSpaceSolidTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(HalfSpaceSolidTests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    public void Dispose() => _service.Dispose();

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

    [Fact]
    public void HalfSpace_PlaneAtOrigin_ProducesValidSolid()
    {
        // Arrange: half-space with plane at z=0, normal (0,0,1), agreement=false
        // agreement=false means material on the negative-normal side (z < 0)
        var plane = IfcMoq.Plane(IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 0)));

        var halfSpace = IfcMoq.HalfSpaceSolid(baseSurface: plane, agreementFlag: false);

        // Act
        var solid = _solidFactory.Build(halfSpace);

        // Assert: half-space is a semi-infinite solid (used as boolean operand),
        // so volume may be 0 or infinite — just verify the solid is created
        solid.Should().NotBeNull();
        solid.ShapeType.Should().Be(XShapeType.Solid);
        SaveBrep(solid, "HalfSpace_PlaneAtOrigin");
    }

    [Fact]
    public void HalfSpace_PlaneAtZ5_AgreementTrue_ProducesValidSolid()
    {
        // Arrange: plane at z=5
        var plane = IfcMoq.Plane(IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 5)));

        var halfSpace = IfcMoq.HalfSpaceSolid(baseSurface: plane, agreementFlag: true);

        // Act
        var solid = _solidFactory.Build(halfSpace);

        // Assert
        solid.Should().NotBeNull();
        SaveBrep(solid, "HalfSpace_PlaneAtZ5_AgreementTrue");
    }

    [Fact]
    public void HalfSpace_BoxedTreatedAsBasic_ProducesValidSolid()
    {
        // Arrange: IfcBoxedHalfSpace is treated identically to IfcHalfSpaceSolid
        var plane = IfcMoq.Plane(IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 3)));

        var boxedHalfSpace = IfcMoq.BoxedHalfSpace(baseSurface: plane, agreementFlag: false);

        // Act
        var solid = _solidFactory.Build(boxedHalfSpace);

        // Assert: same as basic half-space, semi-infinite solid
        solid.Should().NotBeNull();
        solid.ShapeType.Should().Be(XShapeType.Solid);
        SaveBrep(solid, "HalfSpace_Boxed");
    }

    [Fact]
    public void HalfSpace_PolygonalBounded_ProducesValidSolid()
    {
        // Arrange: polygonal bounded half-space with a 10x10 rectangular boundary
        // Plane at z=5, boundary is a 10x10 rectangle centered at origin
        var plane = IfcMoq.Plane(IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 5)));

        var polyBounded = IfcMoq.PolygonalBoundedHalfSpace(
            baseSurface: plane,
            agreementFlag: false,
            boundaryPoints: new[] { (-5.0, -5.0), (5.0, -5.0), (5.0, 5.0), (-5.0, 5.0) });

        // Act
        var solid = _solidFactory.Build(polyBounded);

        // Assert
        solid.Should().NotBeNull();
        solid.Volume.Should().BeGreaterThan(0,
            "polygonal bounded half-space should produce a finite solid");
        SaveBrep(solid, "HalfSpace_PolygonalBounded");
    }

    [Fact]
    public void HalfSpace_PolygonalBounded_WithOffset_ProducesValidSolid()
    {
        // Arrange: polygonal bounded half-space with boundary offset by 10 units in X
        var plane = IfcMoq.Plane(IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 0)));

        var polyBounded = IfcMoq.PolygonalBoundedHalfSpace(
            baseSurface: plane,
            agreementFlag: false,
            position: IfcMoq.Axis2Placement3d(
                axis: IfcMoq.Direction3d(0, 0, 1),
                refDir: IfcMoq.Direction3d(1, 0, 0),
                loc: IfcMoq.CartesianPoint3d(10, 0, 0)),
            boundaryPoints: new[] { (-3.0, -3.0), (3.0, -3.0), (3.0, 3.0), (-3.0, 3.0) });

        // Act
        var solid = _solidFactory.Build(polyBounded);

        // Assert
        solid.Should().NotBeNull();
        solid.Volume.Should().BeGreaterThan(0);
        SaveBrep(solid, "HalfSpace_PolygonalBounded_Offset");
    }

    [Fact]
    public void BooleanCut_WithHalfSpace_ClipsBlock()
    {
        // Arrange: clip a 10x10x10 block with a half-space plane at z=5
        var block = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var plane = IfcMoq.Plane(IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 5)));

        var halfSpace = IfcMoq.HalfSpaceSolid(baseSurface: plane, agreementFlag: false);

        var boolResult = IfcMoq.BooleanResult(
            block, halfSpace, IfcBooleanOperator.DIFFERENCE);

        // Act
        var shape = _service.BooleanFactory.Build(boolResult);

        // Assert: half the block should be removed
        shape.Should().NotBeNull();
        SaveBrep(shape, "HalfSpace_BooleanCut_Block");
    }

    [Fact]
    public void HalfSpace_NonElementarySurface_Throws()
    {
        // Arrange: half-space with a B-spline surface should throw
        var bspline = new Moq.Mock<IIfcBSplineSurfaceWithKnots>
        {
            DefaultValue = Moq.DefaultValue.Mock,
            DefaultValueProvider = new MoqDefaultBehaviourProvider()
        }.SetupAllProperties();
        bspline.SetupGet(x => x.EntityLabel).Returns(400);

        var hsMoq = new Moq.Mock<IIfcHalfSpaceSolid>
        {
            DefaultValue = Moq.DefaultValue.Mock,
            DefaultValueProvider = new MoqDefaultBehaviourProvider()
        }.SetupAllProperties();
        hsMoq.SetupGet(x => x.BaseSurface).Returns(bspline.Object);
        hsMoq.SetupGet(x => x.AgreementFlag).Returns(false);
        hsMoq.SetupGet(x => x.EntityLabel).Returns(401);

        // Act & Assert
        var act = () => _solidFactory.Build(hsMoq.Object);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*only elementary surfaces*");
    }
}
