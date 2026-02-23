using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class GeometryObjectSetBooleanTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;

    public GeometryObjectSetBooleanTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;
    }

    public void Dispose() => _service.Dispose();

    private IXbimSolid BuildSolid(double x, double y, double z,
        double offX = 0, double offY = 0, double offZ = 0)
    {
        var position = (offX != 0 || offY != 0 || offZ != 0)
            ? IfcMoq.Axis2Placement3d(loc: IfcMoq.CartesianPoint3d(offX, offY, offZ))
            : null;
        var ifcBlock = IfcMoq.Block(xLen: x, yLen: y, zLen: z, position: position);
        return (IXbimSolid)_solidFactory.Build(ifcBlock);
    }

    private static double TotalVolume(IXbimGeometryObjectSet result)
        => result.Solids.Cast<IXSolid>().Sum(s => s.Volume);

    #region Cut

    [Fact]
    public void Cut_SingleSolid_SubtractsVolume()
    {
        var body = BuildSolid(10, 10, 10);
        var tool = BuildSolid(5, 5, 5);
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { body });

        var result = set.Cut(tool, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
        TotalVolume(result).Should().BeApproximately(875, 1.0);
    }

    [Fact]
    public void Cut_WithSolidSet_SubtractsAllTools()
    {
        var body = BuildSolid(10, 10, 10);
        var tool1 = BuildSolid(3, 3, 10);
        var tool2 = BuildSolid(3, 3, 10, offX: 5);
        var toolSet = new XbimSolidSet(new IXbimSolid[] { tool1, tool2 });
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { body });

        var result = set.Cut((IXbimSolidSet)toolSet, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
        // Volume < original (1000) after cutting two blocks
        TotalVolume(result).Should().BeLessThan(1000);
        TotalVolume(result).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Cut_SplitsBody_DecomposesIntoSeparateSolids()
    {
        // Cut a thin slab through the middle to split the block into two pieces
        var body = BuildSolid(10, 10, 10);
        var slab = BuildSolid(10, 1, 10, offY: 4.5);
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { body });

        var result = set.Cut(slab, 0.001);

        result.Should().NotBeNull();
        result.Solids.Count.Should().Be(2,
            "cutting through the middle should produce two separate solids");
        // Each piece: 10 × 4.5 × 10 = 450, total = 900
        TotalVolume(result).Should().BeApproximately(900, 1.0);
    }

    #endregion

    #region Union

    [Fact]
    public void Union_SingleSolid_CombinesVolume()
    {
        var body = BuildSolid(10, 10, 10);
        var tool = BuildSolid(10, 10, 10, offX: 20);
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { body });

        var result = set.Union(tool, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
        TotalVolume(result).Should().BeApproximately(2000, 1.0);
    }

    [Fact]
    public void Union_OverlappingSolids_MergesVolume()
    {
        // Two 10x10x10 blocks, overlapping by 5 along X
        var body = BuildSolid(10, 10, 10);
        var tool = BuildSolid(10, 10, 10, offX: 5);
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { body });

        var result = set.Union(tool, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
        // Union = 1000 + 1000 - 500 overlap = 1500
        TotalVolume(result).Should().BeApproximately(1500, 1.0);
    }

    [Fact]
    public void Union_WithSolidSet_CombinesAllTools()
    {
        var body = BuildSolid(10, 10, 10);
        var tool1 = BuildSolid(10, 10, 10, offX: 20);
        var tool2 = BuildSolid(10, 10, 10, offX: 40);
        var toolSet = new XbimSolidSet(new IXbimSolid[] { tool1, tool2 });
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { body });

        var result = set.Union((IXbimSolidSet)toolSet, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
        TotalVolume(result).Should().BeApproximately(3000, 1.0);
    }

    #endregion

    #region Intersection

    [Fact]
    public void Intersection_OverlappingBlocks_ProducesOverlapRegion()
    {
        var body = BuildSolid(10, 10, 10);
        var tool = BuildSolid(10, 10, 10, offX: 5);
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { body });

        var result = set.Intersection(tool, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
        // Intersection = overlap region = 5 × 10 × 10 = 500
        TotalVolume(result).Should().BeApproximately(500, 1.0);
    }

    [Fact]
    public void Intersection_WithSolidSet_IntersectsWithAll()
    {
        // Body: large 20x20x20 block
        // Tools: two smaller 10x10x10 blocks that partially overlap the body
        var body = BuildSolid(20, 20, 20);
        var tool1 = BuildSolid(10, 10, 10);
        var tool2 = BuildSolid(10, 10, 10, offX: 10);
        var toolSet = new XbimSolidSet(new IXbimSolid[] { tool1, tool2 });
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { body });

        var result = set.Intersection((IXbimSolidSet)toolSet, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
        TotalVolume(result).Should().BeGreaterThan(0);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void Boolean_EmptySet_ReturnsEmptyResult()
    {
        var tool = BuildSolid(5, 5, 5);
        var set = new XbimGeometryObjectSet();

        var result = set.Cut(tool, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().Be(0);
    }

    [Fact]
    public void Boolean_MultipleBodiesInSet_OperatesOnAll()
    {
        // Two separate blocks as bodies, union with a bridge block that spans the gap
        var body1 = BuildSolid(10, 10, 10);
        var body2 = BuildSolid(10, 10, 10, offX: 20);
        var bridge = BuildSolid(30, 5, 5);
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { body1, body2 });

        var result = set.Union(bridge, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
        // Total volume > either body alone
        TotalVolume(result).Should().BeGreaterThan(1000);
    }

    #endregion
}
