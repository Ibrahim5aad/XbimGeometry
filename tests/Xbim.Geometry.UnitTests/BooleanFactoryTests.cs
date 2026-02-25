using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class BooleanFactoryTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXBooleanFactory _booleanFactory;
    private readonly IXSolidFactory _solidFactory;
    private readonly string _brepOutputDir;

    private const double Precision = 1e-5;
    private const double PrecisionMax = 0.1;

    public BooleanFactoryTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = new MemoryModel(new EntityFactoryIfc4());
        model.ModelFactors = new XbimModelFactors(angToRads: 1, 0.001, Precision);
        _service = new ModelGeometryService(model, loggerFactory);
        _booleanFactory = _service.BooleanFactory;
        _solidFactory = _service.SolidFactory;

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(BooleanFactoryTests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    public void Dispose() => _service.Dispose();

    private void SaveBrep(IXShape shape, string name)
    {
#if DEBUG
        var path = Path.Combine(_brepOutputDir, $"{name}.brep");
        shape.WriteBrep(path);
#endif
    }

    [Fact]
    public void BooleanCut_TwoBlocks_ProducesValidSolid()
    {
        // Arrange: cut a 5x5x5 block (at origin) from a 10x10x10 block
        var bigBlock = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var smallBlock = IfcMoq.Block(xLen: 5, yLen: 5, zLen: 5);

        var boolResult = IfcMoq.BooleanResult(
            bigBlock, smallBlock, IfcBooleanOperator.DIFFERENCE);

        // Act
        var shape = _booleanFactory.Build(boolResult);

        // Assert
        shape.Should().NotBeNull();
        SaveBrep(shape, "BooleanCut_TwoBlocks");

        // Volume should be 10*10*10 - 5*5*5 = 1000 - 125 = 875
        if (shape is IXSolid solid)
            solid.Volume.Should().BeApproximately(875, 1.0);
    }

    [Fact]
    public void BooleanUnion_TwoOverlappingBlocks_ProducesValidSolid()
    {
        // Arrange: two 10x10x10 blocks, second offset by 5 along X
        // Block1 spans [0,10] in X, Block2 spans [5,15] in X
        // Overlap region is [5,10] = 5 units wide → overlap volume = 5*10*10 = 500
        // Union volume = 1000 + 1000 - 500 = 1500
        var block1 = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var block2 = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10,
            position: IfcMoq.Axis2Placement3d(loc: IfcMoq.CartesianPoint3d(5, 0, 0)));

        var boolResult = IfcMoq.BooleanResult(
            block1, block2, IfcBooleanOperator.UNION);

        // Act
        var shape = _booleanFactory.Build(boolResult);

        // Assert
        shape.Should().NotBeNull();
        SaveBrep(shape, "BooleanUnion_OverlappingBlocks");

        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(1500, 1.0);
    }

    [Fact]
    public void BooleanIntersect_TwoOverlappingBlocks_ProducesSmallerSolid()
    {
        // Arrange: two 10x10x10 blocks, second offset by 5 along X
        // Block1 spans [0,10], Block2 spans [5,15] → overlap = [5,10] = 5x10x10
        // Intersection volume should be 500 (smaller than either original)
        var block1 = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var block2 = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10,
            position: IfcMoq.Axis2Placement3d(loc: IfcMoq.CartesianPoint3d(5, 0, 0)));

        var boolResult = IfcMoq.BooleanResult(
            block1, block2, IfcBooleanOperator.INTERSECTION);

        // Act
        var shape = _booleanFactory.Build(boolResult);

        // Assert
        shape.Should().NotBeNull();
        SaveBrep(shape, "BooleanIntersect_OverlappingBlocks");

        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(500, 1.0,
            "intersection of two overlapping blocks should produce a smaller solid");
    }

    [Fact]
    public void BooleanCut_BlockMinusCylinder_ProducesValidSolidWithCorrectTopology()
    {
        // Arrange: cut a cylinder (r=3, h=10) from a 10x10x10 block
        // Both at origin: block spans [0,10]^3, cylinder at origin only has
        // its first-quadrant quarter inside the block.
        var block = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var cylinder = IfcMoq.Cylinder(radius: 3, height: 10);

        var boolResult = IfcMoq.BooleanResult(
            block, cylinder, IfcBooleanOperator.DIFFERENCE);

        // Act
        var shape = _booleanFactory.Build(boolResult);

        // Assert: shape is a valid closed solid with correct volume
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue("result of boolean cut should be a valid shape");
        shape.IsClosed.Should().BeTrue("boolean cut result should be topologically closed");
        SaveBrep(shape, "BooleanCut_BlockMinusCylinder");

        // Volume = 1000 - (pi*9*10)/4 ≈ 1000 - 70.69 ≈ 929.31
        var solid = shape.Should().BeAssignableTo<IXSolid>().Subject;
        solid.Volume.Should().BeApproximately(1000 - Math.PI * 9 * 10 / 4, 2.0,
            "volume should equal block minus quarter-cylinder intersection");
    }

    [Fact]
    public void BooleanCut_NestedResult_ProducesValidShape()
    {
        // Arrange: nested boolean: (block - smallBlock1) - smallBlock2
        // All at origin: 3x3x3 is fully inside the already-removed 5x5x5 region
        var bigBlock = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var smallBlock1 = IfcMoq.Block(xLen: 5, yLen: 5, zLen: 5);
        var smallBlock2 = IfcMoq.Block(xLen: 3, yLen: 3, zLen: 3);

        var innerBool = IfcMoq.BooleanResult(
            bigBlock, smallBlock1, IfcBooleanOperator.DIFFERENCE, entityLabel: 101);

        var outerBool = IfcMoq.BooleanResult(
            innerBool, smallBlock2, IfcBooleanOperator.DIFFERENCE, entityLabel: 102);

        // Act
        var shape = _booleanFactory.Build(outerBool);

        // Assert
        shape.Should().NotBeNull();
        SaveBrep(shape, "BooleanCut_Nested");

        // Volume = 1000 - 125 = 875 (3x3x3 already inside removed 5x5x5 region)
        if (shape is IXSolid solid)
            solid.Volume.Should().BeApproximately(875, 1.0);
    }

    [Fact]
    public void BooleanCut_WithHalfSpace_ProducesValidSolid()
    {
        // Arrange: clip a 10x10x10 block with a half-space plane at z=5
        // The plane is at z=5, agreement=false means material below (z<5)
        // So cutting with half-space removes material above z=5
        var block = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);

        var plane = IfcMoq.Plane(IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 5)));

        var halfSpace = IfcMoq.HalfSpaceSolid(baseSurface: plane, agreementFlag: false);

        var boolResult = IfcMoq.BooleanResult(
            block, halfSpace, IfcBooleanOperator.DIFFERENCE);

        // Act
        var shape = _booleanFactory.Build(boolResult);

        // Assert
        shape.Should().NotBeNull();
        SaveBrep(shape, "BooleanCut_HalfSpace");
    }

    [Fact]
    public void BooleanClippingResult_ProducesValidShape()
    {
        // Arrange: IIfcBooleanClippingResult is a subtype of IIfcBooleanResult
        var block = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);

        var plane = IfcMoq.Plane(IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 5)));

        var halfSpace = IfcMoq.HalfSpaceSolid(baseSurface: plane, agreementFlag: false);

        var clippingResult = IfcMoq.BooleanClippingResult(block, halfSpace);

        // Act
        var shape = _booleanFactory.Build(clippingResult);

        // Assert
        shape.Should().NotBeNull();
        SaveBrep(shape, "BooleanClippingResult");
    }

    [Fact]
    public void BooleanCut_CSGPrimitiveOperand_ProducesValidShape()
    {
        // Arrange: cut a sphere from a block
        var block = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var sphere = IfcMoq.Sphere(radius: 3);

        var boolResult = IfcMoq.BooleanResult(
            block, sphere, IfcBooleanOperator.DIFFERENCE);

        // Act
        var shape = _booleanFactory.Build(boolResult);

        // Assert
        shape.Should().NotBeNull();
        SaveBrep(shape, "BooleanCut_CSGPrimitive");

        // Sphere at origin: only 1/8 (positive octant) intersects block [0,10]^3
        // Volume = 1000 - (4/3)*pi*27/8 ≈ 1000 - 14.14 ≈ 985.86
        if (shape is IXSolid solid)
            solid.Volume.Should().BeApproximately(1000 - (4.0 / 3) * Math.PI * 27 / 8, 2.0);
    }

    [Fact]
    public void CsgSolid_WithBooleanTreeRoot_ProducesValidShape()
    {
        // Arrange: CSG solid whose tree root is a boolean result (block - cylinder)
        var block = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var cylinder = IfcMoq.Cylinder(radius: 3, height: 10);

        var boolResult = IfcMoq.BooleanResult(
            block, cylinder, IfcBooleanOperator.DIFFERENCE);

        var csgSolid = IfcMoq.CsgSolid(boolResult);

        // Act
        var shape = _solidFactory.Build(csgSolid);

        // Assert
        shape.Should().NotBeNull();
        SaveBrep(shape, "CsgSolid_BooleanTree");
    }

    [Fact]
    public void CsgSolid_WithPrimitiveTreeRoot_ProducesValidSolid()
    {
        // Arrange: CSG solid whose tree root is a single block primitive
        var block = IfcMoq.Block(xLen: 10, yLen: 20, zLen: 30);
        var csgSolid = IfcMoq.CsgSolid(block);

        // Act
        var shape = _solidFactory.Build(csgSolid);

        // Assert
        shape.Should().NotBeNull();
        shape.Should().BeAssignableTo<IXSolid>();
        ((IXSolid)shape).Volume.Should().BeApproximately(6000, 0.1);
        SaveBrep(shape, "CsgSolid_Primitive");
    }

    [Fact]
    public void CsgSolid_NestedBooleanTree_ProducesValidShape()
    {
        // Arrange: CSG solid with nested boolean tree:
        // (bigBlock DIFFERENCE smallBlock) UNION sphere
        // Block 10x10x10 at origin, smallBlock 5x5x5 at origin → cut removes 125
        // Sphere r=3 at origin → 1/8 in positive octant already inside the block
        // OCCT boolean union with a partially-overlapping sphere may produce
        // a compound (solid + solid) rather than a single fused solid.
        var bigBlock = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var smallBlock = IfcMoq.Block(xLen: 5, yLen: 5, zLen: 5);
        var sphere = IfcMoq.Sphere(radius: 3);

        var innerBool = IfcMoq.BooleanResult(
            bigBlock, smallBlock, IfcBooleanOperator.DIFFERENCE, entityLabel: 101);

        var outerBool = IfcMoq.BooleanResult(
            innerBool, sphere, IfcBooleanOperator.UNION, entityLabel: 102);

        var csgSolid = IfcMoq.CsgSolid(outerBool);

        // Act
        var shape = _solidFactory.Build(csgSolid);

        // Assert: result should be a valid shape (may be solid or compound)
        shape.Should().NotBeNull();
        shape.IsValidShape().Should().BeTrue("nested CSG tree result should be valid");
        shape.ShapeType.Should().BeOneOf(
            new[] { XShapeType.Solid, XShapeType.Compound },
            "boolean union result should be a solid or compound");
        SaveBrep(shape, "CsgSolid_NestedBooleanTree");
    }

    #region Displacement-Based Boolean Tests

    [Fact]
    public void Can_union_two_coincidental_blocks()
    {
        var booleanResult = IfcMoq.BooleanResultFromDisplacement();

        var shape = _booleanFactory.Build(booleanResult);
        Assert.True(shape.ShapeType == XShapeType.Solid || shape.ShapeType == XShapeType.Compound);
        var solid = shape as IXSolid;
        if (shape is IXCompound compound)
        {
            Assert.True(compound.IsSolidsOnly && compound.Solids.Count() == 1);
            solid = compound.Solids.First();
        }
        solid.Should().NotBeNull();
        solid!.Shells.Should().HaveCount(1);
        solid.Shells.First().Faces.Should().HaveCount(6);
    }

    [Fact]
    public void Can_cut_two_coincidental_blocks()
    {
        var booleanResult = IfcMoq.BooleanResultFromDisplacement(boolOp: IfcBooleanOperator.DIFFERENCE);
        var shape = _booleanFactory.Build(booleanResult);
        shape.IsEmptyShape().Should().BeTrue();
    }

    /// <summary>
    /// These tests create two blocks and vary their distance apart to either union to one block if they
    /// are less than or equal to 1mm apart, otherwise 2. This shows fuzz tolerance is working correctly.
    /// </summary>
    [Theory]
    [InlineData(-10 - (PrecisionMax * 1.01), 0, 0, false)]
    [InlineData(10 + (PrecisionMax * 1.01), 0, 0, false)]
    [InlineData(-10 - PrecisionMax, 0, 0)]
    [InlineData(10.0 + PrecisionMax, 0, 0)]
    [InlineData(0, 0, -30)]
    [InlineData(0, -20, 0)]
    [InlineData(-10, 0, 0)]
    [InlineData(0, 0, 30)]
    [InlineData(0, 20, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(0, 0, 0)]
    public void Can_union_two_face_connected_blocks(double dispX, double dispY, double dispZ, bool singleSolid = true)
    {
        var booleanResult = IfcMoq.BooleanResultFromDisplacement(displacementX: dispX, displacementY: dispY, displacementZ: dispZ);
        var shape = _booleanFactory.Build(booleanResult);
        if (singleSolid)
        {
            Assert.True(shape.ShapeType == XShapeType.Solid || shape.ShapeType == XShapeType.Compound);
            var solid = shape as IXSolid;
            if (shape is IXCompound compound)
            {
                Assert.True(compound.IsSolidsOnly && compound.Solids.Count() == 1);
                solid = compound.Solids.First();
            }
            solid!.Shells.Should().HaveCount(1);
            solid.Shells.First().Faces.Should().HaveCount(6);
        }
        else
        {
            Assert.True(shape.ShapeType == XShapeType.Compound);
            var compound = (IXCompound)shape;
            Assert.True(compound.IsSolidsOnly);
            compound.Solids.Should().HaveCount(2);
        }
    }

    [Theory]
    [InlineData(10, -10 - (PrecisionMax * 1.01), false)]
    [InlineData(10, 10 + (PrecisionMax * 1.01), false)]
    // [InlineData(10, -10 - PrecisionMax, true)] // different behavior than old engine
    // [InlineData(10, 10 + PrecisionMax, true)]
    public void Can_cut_two_face_connected_blocks(double lenX, double dispX, bool intersects)
    {
        var booleanResult = IfcMoq.BooleanResultFromDisplacement(boolOp: IfcBooleanOperator.DIFFERENCE, lenX: lenX, displacementX: dispX);
        var shape = _booleanFactory.Build(booleanResult);

        Assert.True(shape.ShapeType == XShapeType.Solid);
        var solid = (IXSolid)shape;
        var box = solid.Bounds();
        if (intersects)
        {
            var growth = 2 * (Math.Abs(dispX) - lenX);
            box.LenX.Should().BeApproximately(lenX + growth, Precision);
        }
        else
        {
            box.LenX.Should().BeApproximately(lenX, Precision);
        }
    }

    [Theory]
    [InlineData(10, 5, true)]
    [InlineData(10, -5, true)]
    [InlineData(10, 10 - (Precision * 0.9), false)]
    [InlineData(10, -10 - (Precision * 0.9), false)]
    public void Can_intersect_two_blocks(double lenX, double dispX, bool intersects)
    {
        var booleanResult = IfcMoq.BooleanResultFromDisplacement(
            boolOp: IfcBooleanOperator.INTERSECTION,
            lenX: lenX,
            displacementX: dispX);

        if (!intersects)
        {
            var shape = _booleanFactory.Build(booleanResult);
            shape.IsEmptyShape().Should().BeTrue();
        }
        else
        {
            var shape = _booleanFactory.Build(booleanResult);
            Assert.True(shape.ShapeType == XShapeType.Solid);
            var solid = (IXSolid)shape;
            var box = solid.Bounds();
            box.LenX.Should().BeApproximately(lenX - Math.Abs(dispX), Precision);
        }
    }

    [Theory]
    [InlineData(10)]
    public void Can_build_nested_boolean_results(int depth)
    {
        var booleanResult = IfcMoq.DeepBooleanResult(depth: depth, displacement: 10);
        var shape = _booleanFactory.Build(booleanResult);
        shape.Should().NotBeNull();
    }

    #endregion
}
