using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests WexBim mesh creation, BRep string serialization, and binary shape serialization
/// via the full P/Invoke path.
/// </summary>
public class MeshSerializationTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private readonly IXWexBimMeshFactory _meshFactory;
    private readonly IXShapeBinarySerializer _binarySerializer;

    public MeshSerializationTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;
        _meshFactory = _service.WexBimMeshFactory;
        _binarySerializer = _service.ShapeBinarySerializer;
    }

    public void Dispose() => _service.Dispose();

    // ── WexBim Mesh Tests ────────────────────────────────────────

    [Fact]
    public void MeshBlock_ProducesNonEmptyBuffer()
    {
        var ifcBlock = IfcMoq.Block(xLen: 10, yLen: 20, zLen: 30);
        var solid = _solidFactory.Build(ifcBlock);

        var meshBytes = _meshFactory.CreateWexBimMesh(solid, out var bounds);

        meshBytes.Should().NotBeEmpty("a Block solid should triangulate to a non-empty mesh buffer");
        meshBytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MeshBlock_BoundsMatchGeometry()
    {
        var ifcBlock = IfcMoq.Block(xLen: 10, yLen: 20, zLen: 30);
        var solid = _solidFactory.Build(ifcBlock);

        _meshFactory.CreateWexBimMesh(solid, out var bounds);

        bounds.Should().NotBeNull();
        bounds.IsVoid.Should().BeFalse("bounding box should be valid for a meshed Block");
        bounds.LenX.Should().BeApproximately(10, 0.5);
        bounds.LenY.Should().BeApproximately(20, 0.5);
        bounds.LenZ.Should().BeApproximately(30, 0.5);
    }

    [Fact]
    public void MeshCylinder_ProducesNonEmptyBuffer()
    {
        var ifcCylinder = IfcMoq.Cylinder(radius: 3, height: 10);
        var solid = _solidFactory.Build(ifcCylinder);

        var meshBytes = _meshFactory.CreateWexBimMesh(solid, out var bounds);

        meshBytes.Should().NotBeEmpty("a Cylinder solid should triangulate to a non-empty mesh buffer");
    }

    [Fact]
    public void MeshCylinder_BoundsMatchGeometry()
    {
        var ifcCylinder = IfcMoq.Cylinder(radius: 3, height: 10);
        var solid = _solidFactory.Build(ifcCylinder);

        _meshFactory.CreateWexBimMesh(solid, out var bounds);

        bounds.IsVoid.Should().BeFalse();
        // Cylinder r=3: diameter=6 in X and Y, height=10 in Z
        bounds.LenX.Should().BeApproximately(6, 0.5);
        bounds.LenY.Should().BeApproximately(6, 0.5);
        bounds.LenZ.Should().BeApproximately(10, 0.5);
    }

    [Fact]
    public void MeshBlock_ReportsHasCurves()
    {
        var ifcBlock = IfcMoq.Block(xLen: 5, yLen: 5, zLen: 5);
        var solid = _solidFactory.Build(ifcBlock);

        _meshFactory.CreateWexBimMesh(solid, out _, out bool hasCurves);

        // A box has only planar faces / straight edges — no curves
        hasCurves.Should().BeFalse("a Block has only planar faces and straight edges");
    }

    [Fact]
    public void MeshSphere_ProducesNonEmptyBuffer()
    {
        var ifcSphere = IfcMoq.Sphere(radius: 5);
        var solid = _solidFactory.Build(ifcSphere);

        var meshBytes = _meshFactory.CreateWexBimMesh(solid, out var bounds);

        meshBytes.Should().NotBeEmpty("a Sphere should triangulate to a non-empty mesh buffer");
        bounds.IsVoid.Should().BeFalse();
        // Sphere r=5: diameter=10 in all axes
        bounds.LenX.Should().BeApproximately(10, 0.5);
        bounds.LenY.Should().BeApproximately(10, 0.5);
        bounds.LenZ.Should().BeApproximately(10, 0.5);
    }

    [Fact]
    public void MeshWithExplicitParams_ProducesNonEmptyBuffer()
    {
        var ifcBlock = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var solid = _solidFactory.Build(ifcBlock);

        var meshBytes = _meshFactory.CreateWexBimMesh(
            solid,
            tolerance: 1e-5,
            linearDeflection: 0.5,
            angularDeflection: 0.5,
            scale: 1.0,
            out var bounds);

        meshBytes.Should().NotBeEmpty();
        bounds.IsVoid.Should().BeFalse();
    }

    // ── BRep String Serialization Tests ──────────────────────────

    [Fact]
    public void BrepRoundTrip_Block_PreservesVolume()
    {
        var ifcBlock = IfcMoq.Block(xLen: 10, yLen: 20, zLen: 30);
        var solid = _solidFactory.Build(ifcBlock);

        // Serialize to BRep string
        var brepStr = ((XbimShape)solid).BrepString();
        brepStr.Should().NotBeNullOrEmpty("BRep string should be generated");

        // Deserialize from BRep string
        var restored = ShapeBinarySerializer.FromBrep(brepStr);
        restored.Should().NotBeNull();
        restored.Should().BeAssignableTo<IXSolid>();

        // Volume must be preserved
        ((IXSolid)restored).Volume.Should().BeApproximately(6000, 0.1);
    }

    [Fact]
    public void BrepRoundTrip_Cylinder_PreservesVolume()
    {
        var ifcCylinder = IfcMoq.Cylinder(radius: 3, height: 10);
        var solid = _solidFactory.Build(ifcCylinder);
        double originalVolume = solid.Volume;

        var brepStr = ((XbimShape)solid).BrepString();
        var restored = ShapeBinarySerializer.FromBrep(brepStr);

        ((IXSolid)restored).Volume.Should().BeApproximately(originalVolume, 0.1);
    }

    [Fact]
    public void BrepRoundTrip_Sphere_PreservesValidity()
    {
        var ifcSphere = IfcMoq.Sphere(radius: 5);
        var solid = _solidFactory.Build(ifcSphere);

        var brepStr = ((XbimShape)solid).BrepString();
        var restored = ShapeBinarySerializer.FromBrep(brepStr) as XbimShape;

        restored.Should().NotBeNull();
        restored!.IsValidShape().Should().BeTrue("deserialized sphere should be a valid shape");
    }

    // ── Binary Serialization Tests ───────────────────────────────

    [Fact]
    public void BinaryRoundTrip_Block_PreservesVolume()
    {
        var ifcBlock = IfcMoq.Block(xLen: 10, yLen: 20, zLen: 30);
        var solid = _solidFactory.Build(ifcBlock);

        // Serialize
        var binary = _binarySerializer.ToArray(solid);
        binary.Should().NotBeEmpty("binary serialization should produce bytes");
        binary.Length.Should().BeGreaterThan(0);

        // Deserialize
        var restored = _binarySerializer.FromArray(binary);
        restored.Should().NotBeNull();
        restored.Should().BeAssignableTo<IXSolid>();

        // Volume must be preserved
        ((IXSolid)restored).Volume.Should().BeApproximately(6000, 0.1);
    }

    [Fact]
    public void BinaryRoundTrip_Cylinder_PreservesVolume()
    {
        var ifcCylinder = IfcMoq.Cylinder(radius: 3, height: 10);
        var solid = _solidFactory.Build(ifcCylinder);
        double originalVolume = solid.Volume;

        var binary = _binarySerializer.ToArray(solid);
        var restored = _binarySerializer.FromArray(binary);

        ((IXSolid)restored).Volume.Should().BeApproximately(originalVolume, 0.1);
    }

    [Fact]
    public void BinaryRoundTrip_WithTriangles_PreservesVolume()
    {
        var ifcBlock = IfcMoq.Block(xLen: 5, yLen: 10, zLen: 15);
        var solid = _solidFactory.Build(ifcBlock);

        // Serialize with embedded triangulation
        var binary = _binarySerializer.ToArray(solid, withTriangles: true, withNormals: true);
        binary.Should().NotBeEmpty();

        var restored = _binarySerializer.FromArray(binary);
        ((IXSolid)restored).Volume.Should().BeApproximately(750, 0.1);
    }

    [Fact]
    public void BinaryRoundTrip_PreservesShapeValidity()
    {
        var ifcBlock = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);
        var solid = _solidFactory.Build(ifcBlock);

        var binary = _binarySerializer.ToArray(solid);
        var restored = _binarySerializer.FromArray(binary) as XbimShape;

        restored.Should().NotBeNull();
        restored!.IsValidShape().Should().BeTrue("deserialized shape should be valid");
        restored.IsClosed.Should().BeTrue("deserialized block should be closed");
    }

    // ── Disposal Safety ──────────────────────────────────────────

    [Fact]
    public void DisposingSerializedShapes_DoesNotCrash()
    {
        var ifcBlock = IfcMoq.Block(xLen: 5, yLen: 5, zLen: 5);
        var solid = _solidFactory.Build(ifcBlock);

        // BRep round-trip
        var brepStr = ((XbimShape)solid).BrepString();
        var fromBrep = ShapeBinarySerializer.FromBrep(brepStr);

        // Binary round-trip
        var binary = _binarySerializer.ToArray(solid);
        var fromBinary = _binarySerializer.FromArray(binary);

        // Disposing all shapes should not crash
        (fromBrep as IDisposable)?.Dispose();
        (fromBinary as IDisposable)?.Dispose();
        (solid as IDisposable)?.Dispose();
    }
}
