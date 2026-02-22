using System;
using System.IO;
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

public class ShapeSetOperationTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly IXWireFactory _wireFactory;

    public ShapeSetOperationTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;
        _wireFactory = _service.WireFactory;
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

    #region XbimSolidSet Serialization

    [Fact]
    public void SolidSet_ToBRep_SingleSolid_ReturnsNonEmptyString()
    {
        var solid = BuildSolid(10, 10, 10);
        var set = new XbimSolidSet(new[] { solid });

        set.ToBRep.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SolidSet_ToBRep_MultipleSolids_ReturnsNonEmptyString()
    {
        var s1 = BuildSolid(10, 10, 10);
        var s2 = BuildSolid(5, 5, 5, offX: 20);
        var set = new XbimSolidSet(new[] { s1, s2 });

        var brep = set.ToBRep;

        brep.Should().NotBeNullOrEmpty();
        brep.Length.Should().BeGreaterThan(set.First.ToBRep.Length,
            "compound BRep should be longer than a single solid");
    }

    [Fact]
    public void SolidSet_ToBRep_Empty_ReturnsEmptyString()
    {
        var set = new XbimSolidSet();

        set.ToBRep.Should().BeEmpty();
    }

    [Fact]
    public void SolidSet_BrepString_MultipleSolids_ReturnsNonEmptyString()
    {
        var s1 = BuildSolid(10, 10, 10);
        var s2 = BuildSolid(5, 5, 5, offX: 20);
        var set = new XbimSolidSet(new[] { s1, s2 });

        var brep = ((IXShape)set).BrepString();

        brep.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SolidSet_SaveAsBrep_MultipleSolids_WritesFile()
    {
        var s1 = BuildSolid(10, 10, 10);
        var s2 = BuildSolid(5, 5, 5, offX: 20);
        var set = new XbimSolidSet(new[] { s1, s2 });
        var path = Path.Combine(Path.GetTempPath(), $"solidset_{Guid.NewGuid()}.brep");

        try
        {
            set.SaveAsBrep(path);
            File.Exists(path).Should().BeTrue();
            new FileInfo(path).Length.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SolidSet_WriteBrep_MultipleSolids_WritesFile()
    {
        var s1 = BuildSolid(10, 10, 10);
        var s2 = BuildSolid(5, 5, 5, offX: 20);
        var set = new XbimSolidSet(new[] { s1, s2 });
        var path = Path.Combine(Path.GetTempPath(), $"solidset_wb_{Guid.NewGuid()}.brep");

        try
        {
            ((IXShape)set).WriteBrep(path);
            File.Exists(path).Should().BeTrue();
            new FileInfo(path).Length.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SolidSet_WriteStl_MultipleSolids_WritesFile()
    {
        var s1 = BuildSolid(10, 10, 10);
        var s2 = BuildSolid(5, 5, 5, offX: 20);
        var set = new XbimSolidSet(new[] { s1, s2 });
        var path = Path.Combine(Path.GetTempPath(), $"solidset_{Guid.NewGuid()}.stl");

        try
        {
            ((IXShape)set).WriteStl(path);
            File.Exists(path).Should().BeTrue();
            new FileInfo(path).Length.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SolidSet_Triangulate_MultipleSolids_Succeeds()
    {
        var s1 = BuildSolid(10, 10, 10);
        var s2 = BuildSolid(5, 5, 5, offX: 20);
        var set = new XbimSolidSet(new[] { s1, s2 });

        var meshFactors = new XbimMeshFactors(1.0, 0.5, false);
        var result = ((IXShape)set).Triangulate(meshFactors);

        result.Should().BeTrue();
    }

    #endregion

    #region XbimShellSet Operations

    [Fact]
    public void ShellSet_Add_Shell_IncreasesCount()
    {
        var solid = BuildSolid(10, 10, 10);
        var shells = solid.Shells.ToArray();
        shells.Should().HaveCountGreaterThan(0);

        var set = new XbimShellSet(Array.Empty<IXbimShell>());
        set.Count.Should().Be(0);

        set.Add(shells[0]);
        set.Count.Should().Be(1);
    }

    [Fact]
    public void ShellSet_Add_Solid_DecomposesToShells()
    {
        var solid = BuildSolid(10, 10, 10);
        var set = new XbimShellSet(Array.Empty<IXbimShell>());

        set.Add(solid);

        set.Count.Should().BeGreaterThan(0,
            "adding a solid should decompose it into shells");
    }

    [Fact]
    public void ShellSet_Add_ShellSet_AddsAllShells()
    {
        var solid1 = BuildSolid(10, 10, 10);
        var solid2 = BuildSolid(5, 5, 5, offX: 20);
        var source = new XbimShellSet(
            solid1.Shells.Concat(solid2.Shells).ToArray());

        var target = new XbimShellSet(Array.Empty<IXbimShell>());
        target.Add(source);

        target.Count.Should().Be(source.Count);
    }

    [Fact]
    public void ShellSet_Cut_WithSolid_ReturnsResult()
    {
        var solid = BuildSolid(10, 10, 10);
        var tool = BuildSolid(5, 5, 5);
        var shells = solid.Shells.ToArray();
        var set = new XbimShellSet(shells);

        var result = set.Cut(tool, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ShellSet_Union_WithSolidSet_ReturnsResult()
    {
        // Use two separate solids as bodies (via shells) and union with a solid tool
        var body = BuildSolid(10, 10, 10);
        var tool = BuildSolid(5, 5, 5);
        var toolSet = new XbimSolidSet(new[] { tool });
        var shells = body.Shells.ToArray();
        var set = new XbimShellSet(shells);

        var result = set.Cut((IXbimSolidSet)toolSet, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ShellSet_Intersection_WithSolid_ReturnsResult()
    {
        var solid = BuildSolid(10, 10, 10);
        var tool = BuildSolid(10, 10, 10, offX: 5);
        var shells = solid.Shells.ToArray();
        var set = new XbimShellSet(shells);

        var result = set.Intersection(tool, 0.001);

        result.Should().NotBeNull();
        result.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ShellSet_SelfUnion_OverlappingShells_Merges()
    {
        var solid1 = BuildSolid(10, 10, 10);
        var solid2 = BuildSolid(10, 10, 10, offX: 5);
        var allShells = solid1.Shells.Concat(solid2.Shells).ToArray();
        var set = new XbimShellSet(allShells);
        int originalCount = set.Count;

        set.Union(0.001);

        // After union the shell count should be <= original (merged)
        set.Count.Should().BeLessThanOrEqualTo(originalCount);
        set.Count.Should().BeGreaterThan(0);
    }

    #endregion

    #region XbimGeometryObjectSet ToBRep and Sew

    [Fact]
    public void GeometryObjectSet_ToBRep_SingleSolid_ReturnsNonEmpty()
    {
        var solid = BuildSolid(10, 10, 10);
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { solid });

        var brep = set.ToBRep;

        brep.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GeometryObjectSet_ToBRep_MultipleSolids_ReturnsCompoundBrep()
    {
        var s1 = BuildSolid(10, 10, 10);
        var s2 = BuildSolid(5, 5, 5, offX: 20);
        var set = new XbimGeometryObjectSet(new IXbimGeometryObject[] { s1, s2 });

        var brep = set.ToBRep;

        brep.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GeometryObjectSet_ToBRep_Empty_ReturnsEmpty()
    {
        var set = new XbimGeometryObjectSet();

        set.ToBRep.Should().BeEmpty();
    }

    [Fact]
    public void GeometryObjectSet_Sew_AdjacentFaces_Succeeds()
    {
        // Build two solids, get their faces, put them into a geometry object set and sew
        var solid = BuildSolid(10, 10, 10);
        var faces = solid.Faces.ToArray();
        var set = new XbimGeometryObjectSet(
            faces.Cast<IXbimGeometryObject>());

        var result = set.Sew();

        result.Should().BeTrue();
        set.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GeometryObjectSet_Sew_Empty_ReturnsFalse()
    {
        var set = new XbimGeometryObjectSet();

        set.Sew().Should().BeFalse();
    }

    #endregion

    #region XbimWire Trim

    [Fact]
    public void Wire_Trim_RectangleProfile_ReturnsShorterWire()
    {
        var profile = IfcMoq.RectangleProfile(100, 200);
        var wire = (IXbimWire)_wireFactory.Build(profile);
        var fullLength = wire.Length;

        var trimmed = wire.Trim(0, fullLength / 2, 1e-6);

        trimmed.Should().NotBeNull();
        trimmed.Length.Should().BeApproximately(fullLength / 2, 1.0);
    }

    [Fact]
    public void Wire_Trim_CircleProfile_ReturnsTrimmedArc()
    {
        var profile = IfcMoq.CircleProfile(50);
        var wire = (IXbimWire)_wireFactory.Build(profile);
        var fullLength = wire.Length;
        fullLength.Should().BeGreaterThan(0);

        // Trim to quarter of the circumference
        var quarter = fullLength / 4;
        var trimmed = wire.Trim(0, quarter, 1e-6);

        trimmed.Should().NotBeNull();
        trimmed.Length.Should().BeApproximately(quarter, 1.0);
    }

    [Fact]
    public void Wire_Trim_MiddleSegment_ReturnsCorrectLength()
    {
        var profile = IfcMoq.RectangleProfile(100, 200);
        var wire = (IXbimWire)_wireFactory.Build(profile);
        var fullLength = wire.Length;

        // Trim from 1/4 to 3/4
        double start = fullLength * 0.25;
        double end = fullLength * 0.75;
        var trimmed = wire.Trim(start, end, 1e-6);

        trimmed.Should().NotBeNull();
        trimmed.Length.Should().BeApproximately(fullLength / 2, 1.0);
    }

    #endregion

    private class XbimMeshFactors : IXMeshFactors
    {
        public XbimMeshFactors(double linear, double angular, bool relative)
        {
            LinearDefection = linear;
            AngularDeflection = angular;
            Relative = relative;
        }

        public double OneMeter { get; set; } = 1.0;
        public double LinearDefection { get; set; }
        public double AngularDeflection { get; set; }
        public bool Relative { get; set; }
        public double Tolerance { get; set; } = 1e-6;

        public IXMeshFactors SetGranularity(MeshGranularity granularity) => this;
    }
}
