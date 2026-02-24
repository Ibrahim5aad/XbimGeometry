using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests the V5 compatibility layer: the legacy IXbimGeometryEngine API exposed through
/// GeometryEngine, including Create(), shape adapters, BRep round-trip, transforms,
/// CreateShapeGeometry, solid sets, and face set enumeration.
/// </summary>
public class V5CompatibilityTests : IDisposable
{
    private readonly IXGeometryEngineV6 _engine;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public V5CompatibilityTests()
    {
        var factory = new GeometryConverterFactory();
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _engine = factory.CreateGeometryEngineV6(model, _loggerFactory);
        _logger = _loggerFactory.CreateLogger<V5CompatibilityTests>();
    }

    public void Dispose()
    {
        ((IDisposable)_engine).Dispose();
        _loggerFactory.Dispose();
    }

    #region Create() flow

    [Fact]
    public void Create_ExtrudedAreaSolid_ReturnsValidGeometryObject()
    {
        var extruded = IfcMoq.ExtrudedAreaSolid(
            sweptArea: IfcMoq.RectangleProfile(100, 200),
            depth: 50);

        var geomObj = _engine.Create(extruded, _logger);

        geomObj.Should().NotBeNull();
        geomObj.IsValid.Should().BeTrue();
        geomObj.Should().BeAssignableTo<IXbimGeometryObject>();
    }

    [Fact]
    public void Create_Block_ReturnsValidGeometryObject()
    {
        var block = IfcMoq.Block(10, 20, 30);

        var geomObj = _engine.Create(block, _logger);

        geomObj.Should().NotBeNull();
        geomObj.IsValid.Should().BeTrue();
    }

    #endregion

    #region V5 Solid properties

    [Fact]
    public void V5Solid_Volume_IsPositive()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var geomObj = _engine.CreateSolid(block, _logger);

        var solid = geomObj.Should().BeAssignableTo<IXbimSolid>().Subject;

        solid.Volume.Should().BeApproximately(6000, 0.1);
    }

    [Fact]
    public void V5Solid_SurfaceArea_IsPositive()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        // Surface area of 10x20x30 box = 2*(10*20 + 20*30 + 10*30) = 2*(200+600+300) = 2200
        solid.SurfaceArea.Should().BeApproximately(2200, 1.0);
    }

    #endregion

    #region V5 topology access

    [Fact]
    public void V5Solid_Shells_ReturnsNonEmptySet()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        solid.Shells.Should().NotBeNull();
        solid.Shells.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void V5Solid_Faces_Count_IsCorrect()
    {
        // A box has 6 faces
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        solid.Faces.Should().NotBeNull();
        solid.Faces.Count.Should().Be(6);
    }

    [Fact]
    public void V5Solid_Edges_ReturnsNonEmptySet()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        solid.Edges.Should().NotBeNull();
        solid.Edges.Count.Should().Be(12); // A box has 12 edges
    }

    [Fact]
    public void V5Solid_Vertices_ReturnsNonEmptySet()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        solid.Vertices.Should().NotBeNull();
        solid.Vertices.Count.Should().Be(8); // A box has 8 vertices
    }

    #endregion

    #region BRep round-trip

    [Fact]
    public void ToBrep_ProducesNonEmptyString()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var geomObj = _engine.Create(block, _logger);

        var brepStr = _engine.ToBrep(geomObj);

        brepStr.Should().NotBeNullOrEmpty();
        brepStr.Length.Should().BeGreaterThan(10);
    }

    [Fact]
    public void FromBrep_ReturnsValidObject()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var geomObj = _engine.Create(block, _logger);
        var brepStr = _engine.ToBrep(geomObj);

        var restored = _engine.FromBrep(brepStr);

        restored.Should().NotBeNull();
        restored.IsValid.Should().BeTrue();
    }

    [Fact]
    public void BRep_RoundTrip_PreservesVolume()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var original = _engine.CreateSolid(block, _logger);
        var brepStr = _engine.ToBrep(original);

        var restored = _engine.FromBrep(brepStr);
        var restoredSolid = restored.Should().BeAssignableTo<IXbimSolid>().Subject;

        restoredSolid.Volume.Should().BeApproximately(original.Volume, 0.1);
    }

    #endregion

    #region Shape is IXbimGeometryObject

    [Fact]
    public void Solid_IsIXbimSolid()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var shape = _engine.Build(block);

        shape.Should().BeOfType<XbimSolid>();
        shape.Should().BeAssignableTo<IXbimSolid>();
        ((IXbimGeometryObject)shape).GeometryType.Should().Be(XbimGeometryObjectType.XbimSolidType);
    }

    [Fact]
    public void Face_IsIXbimFace()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var shape = _engine.Build(block);
        var faces = shape.AllFaces();
        var firstFace = faces.First();

        firstFace.Should().BeOfType<XbimFace>();
        firstFace.Should().BeAssignableTo<IXbimFace>();
        ((IXbimGeometryObject)firstFace).GeometryType.Should().Be(XbimGeometryObjectType.XbimFaceType);
    }

    #endregion

    #region V5 Transform

    [Fact]
    public void Transform_V5Solid_ReturnsValidShape()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var geomObj = _engine.Create(block, _logger);

        // Translate by (100, 200, 300)
        var matrix = XbimMatrix3D.CreateTranslation(100, 200, 300);
        var transformed = geomObj.Transform(matrix);

        transformed.Should().NotBeNull();
        transformed.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Transform_V5Solid_ChangesPosition()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var geomObj = _engine.Create(block, _logger);
        var originalBB = geomObj.BoundingBox;

        // Translate by (1000, 0, 0) — should shift the bounding box
        var matrix = XbimMatrix3D.CreateTranslation(1000, 0, 0);
        var transformed = geomObj.Transform(matrix);

        var newBB = transformed.BoundingBox;
        // The new min X should be offset by 1000 from the original
        newBB.X.Should().BeApproximately(originalBB.X + 1000, 0.1);
    }

    #endregion

    #region CreateShapeGeometry

    [Fact]
    public void CreateShapeGeometry_ProducesNonEmptyShapeData()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var geomObj = _engine.Create(block, _logger);

        var shapeGeom = _engine.CreateShapeGeometry(geomObj, 1e-5, 0.1, 0.5,
            XbimGeometryType.PolyhedronBinary, _logger);

        ((IXbimShapeGeometryData)shapeGeom).ShapeData.Should().NotBeNull();
        ((IXbimShapeGeometryData)shapeGeom).ShapeData.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreateShapeGeometry_BoundingBox_HasNonZeroDimensions()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var geomObj = _engine.Create(block, _logger);

        var shapeGeom = _engine.CreateShapeGeometry(geomObj, 1e-5, 0.1, _logger);

        var bb = shapeGeom.BoundingBox;
        bb.SizeX.Should().BeGreaterThan(0);
        bb.SizeY.Should().BeGreaterThan(0);
        bb.SizeZ.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreateShapeGeometry_WithOneMillimetre_ProducesValidMesh()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var geomObj = _engine.Create(block, _logger);

        var shapeGeom = _engine.CreateShapeGeometry(1.0, geomObj, 1e-5, _logger);

        ((IXbimShapeGeometryData)shapeGeom).ShapeData.Should().NotBeNull();
        ((IXbimShapeGeometryData)shapeGeom).ShapeData.Length.Should().BeGreaterThan(0);
    }

    #endregion

    #region Empty SolidSet

    [Fact]
    public void CreateSolidSet_Empty_ReturnsEmptySet()
    {
        var set = _engine.CreateSolidSet();

        set.Should().NotBeNull();
        set.Count.Should().Be(0);
        set.IsSet.Should().BeTrue();
        set.GeometryType.Should().Be(XbimGeometryObjectType.XbimSolidSetType);
    }

    #endregion

    #region V5 FaceSet enumeration

    [Fact]
    public void V5FaceSet_Enumeration_MatchesCount()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        var faces = solid.Faces;
        var enumerated = faces.ToList();

        enumerated.Count.Should().Be(faces.Count);
        enumerated.Count.Should().Be(6);
    }

    [Fact]
    public void V5FaceSet_FirstFace_HasPositiveArea()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        var firstFace = solid.Faces.First;
        firstFace.Area.Should().BeGreaterThan(0);
    }

    [Fact]
    public void V5FaceSet_AllFaces_HaveValidNormals()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        foreach (var face in solid.Faces)
        {
            var normal = face.Normal;
            var length = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
            length.Should().BeApproximately(1.0, 0.01, "face normals should be unit vectors");
        }
    }

    #endregion

    #region V5 Shape geometry type mapping

    [Fact]
    public void V5Solid_GeometryType_IsCorrect()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        solid.GeometryType.Should().Be(XbimGeometryObjectType.XbimSolidType);
    }

    [Fact]
    public void V5Solid_BoundingBox_IsValid()
    {
        var block = IfcMoq.Block(10, 20, 30);
        var solid = _engine.CreateSolid(block, _logger);

        var bb = solid.BoundingBox;
        bb.SizeX.Should().BeApproximately(10, 0.1);
        bb.SizeY.Should().BeApproximately(20, 0.1);
        bb.SizeZ.Should().BeApproximately(30, 0.1);
    }

    #endregion
}
