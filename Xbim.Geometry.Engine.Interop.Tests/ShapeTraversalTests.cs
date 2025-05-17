using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests shape traversal API: extracting faces from solids/shells,
/// edges from wires, outer/inner bounds from faces.
/// </summary>
public class ShapeTraversalTests : IDisposable
{
    private readonly NativeModelGeometryService _service;
    private readonly IXSolidFactory _solidFactory;
    private NativeContextHandle Ctx => _service.ContextHandle;

    public ShapeTraversalTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new NativeModelGeometryService(model, loggerFactory);
        _solidFactory = _service.SolidFactory;
    }

    public void Dispose() => _service.Dispose();

    // ── Solid → Shells ─────────────────────────────────────────────────────

    [Fact]
    public void Solid_Shells_BlockHasOneShell()
    {
        using var block = (NativeSolid)_solidFactory.Build(
            IfcMoq.Block(10, 20, 30));

        var shells = block.Shells;
        shells.Should().HaveCount(1);
        shells[0].ShapeType.Should().Be(XShapeType.Shell);
    }

    // ── Solid → AllFaces ────────────────────────────────────────────────────

    [Fact]
    public void Solid_AllFaces_BlockHasSixFaces()
    {
        using var block = (NativeSolid)_solidFactory.Build(
            IfcMoq.Block(10, 20, 30));

        var faces = block.AllFaces().ToArray();
        faces.Should().HaveCount(6);
        foreach (var face in faces)
            face.ShapeType.Should().Be(XShapeType.Face);
    }

    [Fact]
    public void Solid_AllFaces_CylinderHasThreeFaces()
    {
        // A cylinder has 3 faces: top cap, bottom cap, lateral
        using var cyl = (NativeSolid)_solidFactory.Build(
            IfcMoq.Cylinder(5, 10));

        var faces = cyl.AllFaces().ToArray();
        faces.Should().HaveCount(3);
    }

    // ── Shell → Faces ──────────────────────────────────────────────────────

    [Fact]
    public void Shell_Faces_BlockShellHasSixFaces()
    {
        using var block = (NativeSolid)_solidFactory.Build(
            IfcMoq.Block(10, 20, 30));

        var shells = block.Shells;
        shells.Should().HaveCount(1);

        var faces = shells[0].Faces;
        faces.Should().HaveCount(6);
        foreach (var face in faces)
            face.Area.Should().BeGreaterThan(0);
    }

    // ── Wire → EdgeLoop ────────────────────────────────────────────────────

    [Fact]
    public void Wire_EdgeLoop_SquareWireHasFourEdges()
    {
        double[] pts = { 0, 0, 0, 10, 0, 0, 10, 10, 0, 0, 10, 0 };
        XbimGeometryNativeApi.xbim_wire_build_polygon(Ctx, pts, 4, 1, out var wireHandle);
        using var wire = new NativeWire(wireHandle);

        var edges = wire.EdgeLoop;
        edges.Should().HaveCount(4);
        foreach (var edge in edges)
            edge.ShapeType.Should().Be(XShapeType.Edge);
    }

    // ── Face → OuterBound ──────────────────────────────────────────────────

    [Fact]
    public void Face_OuterBound_ReturnsClosedWire()
    {
        double[] pts = { 0, 0, 0, 10, 0, 0, 10, 10, 0, 0, 10, 0 };
        XbimGeometryNativeApi.xbim_wire_build_polygon(Ctx, pts, 4, 1, out var wireHandle);
        XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wireHandle, out var faceHandle);
        wireHandle.Dispose();

        using var face = new NativeFace(faceHandle);
        var outer = face.OuterBound;
        outer.Should().NotBeNull();
        outer.ShapeType.Should().Be(XShapeType.Wire);
    }

    // ── Face → InnerBounds ─────────────────────────────────────────────────

    [Fact]
    public void Face_InnerBounds_SimpleFaceHasNoInnerBounds()
    {
        double[] pts = { 0, 0, 0, 10, 0, 0, 10, 10, 0, 0, 10, 0 };
        XbimGeometryNativeApi.xbim_wire_build_polygon(Ctx, pts, 4, 1, out var wireHandle);
        XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wireHandle, out var faceHandle);
        wireHandle.Dispose();

        using var face = new NativeFace(faceHandle);
        face.InnerBounds.Should().BeEmpty();
    }

    [Fact]
    public void Face_InnerBounds_FaceWithHoleHasOneInnerBound()
    {
        // Outer 20x20 square, inner 5x5 hole
        double[] outerPts = { 0, 0, 0, 20, 0, 0, 20, 20, 0, 0, 20, 0 };
        double[] innerPts = { 7.5, 7.5, 0, 12.5, 7.5, 0, 12.5, 12.5, 0, 7.5, 12.5, 0 };

        XbimGeometryNativeApi.xbim_wire_build_polygon(Ctx, outerPts, 4, 1, out var outerWire);
        XbimGeometryNativeApi.xbim_wire_build_polygon(Ctx, innerPts, 4, 1, out var innerWire);

        var innerPtrs = new[] { innerWire.DangerousGetHandle() };
        XbimGeometryNativeApi.xbim_face_build_advanced(Ctx,
            0, // plane
            0, 0, 0, 0, 0, 1, 1, 0, 0, 0, // surface placement
            outerWire, innerPtrs, 1,
            1e-6, 1,
            out var faceHandle);

        outerWire.Dispose();
        innerWire.Dispose();

        using var face = new NativeFace(faceHandle);
        face.InnerBounds.Should().HaveCount(1);
        face.InnerBounds[0].ShapeType.Should().Be(XShapeType.Wire);
    }

    // ── Count subshapes (native API) ───────────────────────────────────────

    [Fact]
    public void CountSubshapes_BlockEdges_Is12()
    {
        // A box/block has 12 edges
        using var block = (NativeSolid)_solidFactory.Build(
            IfcMoq.Block(10, 20, 30));

        XbimGeometryNativeApi.xbim_shape_count_subshapes(
            block.Handle, (int)XShapeType.Edge, out int count).Should().Be(0);
        count.Should().Be(12);
    }

    [Fact]
    public void CountSubshapes_BlockVertices_Is8()
    {
        // A box/block has 8 vertices
        using var block = (NativeSolid)_solidFactory.Build(
            IfcMoq.Block(10, 20, 30));

        XbimGeometryNativeApi.xbim_shape_count_subshapes(
            block.Handle, (int)XShapeType.Vertex, out int count).Should().Be(0);
        count.Should().Be(8);
    }
}
