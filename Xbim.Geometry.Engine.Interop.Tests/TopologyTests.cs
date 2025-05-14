using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests topology operations (vertex, edge, wire, face, shell) via direct
/// P/Invoke calls to the native C API.
/// </summary>
public class TopologyTests : IDisposable
{
    private readonly NativeModelGeometryService _service;
    private NativeContextHandle Ctx => _service.ContextHandle;

    public TopologyTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new NativeModelGeometryService(model, loggerFactory);
    }

    public void Dispose() => _service.Dispose();

    // ── Vertex ──────────────────────────────────────────────────────────────

    [Fact]
    public void Vertex_BuildAndQuery_RoundTrips()
    {
        NativeMethods.xbim_vertex_build(Ctx, 10, 20, 30, 1e-6, out var vtx).Should().Be(0);
        using (vtx)
        {
            NativeMethods.xbim_vertex_point(vtx, out var x, out var y, out var z).Should().Be(0);
            x.Should().BeApproximately(10, 1e-10);
            y.Should().BeApproximately(20, 1e-10);
            z.Should().BeApproximately(30, 1e-10);
        }
    }

    [Fact]
    public void Vertex_Build_NegativeTolerance_Fails()
    {
        var result = NativeMethods.xbim_vertex_build(Ctx, 0, 0, 0, -1.0, out var vtx);
        result.Should().Be(4); // XBIM_INVALID_ARG
        vtx.Dispose();
    }

    // ── Edge ────────────────────────────────────────────────────────────────

    [Fact]
    public void Edge_BuildLine_ReturnsValidEdge()
    {
        NativeMethods.xbim_edge_build_line(Ctx, 0, 0, 0, 10, 0, 0, out var edge).Should().Be(0);
        using (edge)
        {
            edge.IsInvalid.Should().BeFalse();
        }
    }

    [Fact]
    public void Edge_BuildLine_Length_IsCorrect()
    {
        // 3-4-5 triangle hypotenuse
        NativeMethods.xbim_edge_build_line(Ctx, 0, 0, 0, 3, 4, 0, out var edge).Should().Be(0);
        using (edge)
        {
            NativeMethods.xbim_edge_length(edge, out var len).Should().Be(0);
            len.Should().BeApproximately(5.0, 1e-6);
        }
    }

    [Fact]
    public void Edge_BuildLine_DegeneratePoints_Fails()
    {
        var result = NativeMethods.xbim_edge_build_line(Ctx, 5, 5, 5, 5, 5, 5, out var edge);
        result.Should().Be(4); // XBIM_INVALID_ARG
        edge.Dispose();
    }

    [Fact]
    public void Edge_BuildCircleArc_QuarterCircle()
    {
        double r = 10;
        NativeMethods.xbim_edge_build_circle_arc(Ctx,
            0, 0, 0, 0, 0, 1, r, 0, Math.PI / 2, out var arc).Should().Be(0);
        using (arc)
        {
            NativeMethods.xbim_edge_length(arc, out var len).Should().Be(0);
            len.Should().BeApproximately(r * Math.PI / 2, 1e-6);
        }
    }

    [Fact]
    public void Edge_BuildFromCurve_TrimsSubRange()
    {
        // Build full semicircle, then trim to quarter
        double r = 10;
        NativeMethods.xbim_edge_build_circle_arc(Ctx,
            0, 0, 0, 0, 0, 1, r, 0, Math.PI, out var full).Should().Be(0);
        using (full)
        {
            NativeMethods.xbim_edge_length(full, out var fullLen).Should().Be(0);

            NativeMethods.xbim_edge_build_from_curve(Ctx, full, 0, Math.PI / 2, out var trimmed).Should().Be(0);
            using (trimmed)
            {
                NativeMethods.xbim_edge_length(trimmed, out var trimLen).Should().Be(0);
                trimLen.Should().BeLessThan(fullLen);
                trimLen.Should().BeApproximately(r * Math.PI / 2, 1e-6);
            }
        }
    }

    // ── Wire ────────────────────────────────────────────────────────────────

    [Fact]
    public void Wire_BuildPolyline_Triangle()
    {
        double[] pts = { 0, 0, 0, 10, 0, 0, 5, 10, 0 };
        NativeMethods.xbim_wire_build_polyline(Ctx, pts, 3, 1e-6, out var wire).Should().Be(0);
        using (wire)
        {
            wire.IsInvalid.Should().BeFalse();
        }
    }

    [Fact]
    public void Wire_BuildPolyline_ClosedSquare_IsMarkedClosed()
    {
        // 5 points: square with last point repeating first
        double[] pts = { 0, 0, 0, 10, 0, 0, 10, 10, 0, 0, 10, 0, 0, 0, 0 };
        NativeMethods.xbim_wire_build_polyline(Ctx, pts, 5, 1e-3, out var wire).Should().Be(0);
        using (wire)
        {
            NativeMethods.xbim_wire_is_closed(wire, 1e-3, out var closed).Should().Be(0);
            closed.Should().Be(1);
        }
    }

    [Fact]
    public void Wire_BuildPolyline_OpenSegment_IsNotClosed()
    {
        double[] pts = { 0, 0, 0, 10, 0, 0 };
        NativeMethods.xbim_wire_build_polyline(Ctx, pts, 2, 1e-6, out var wire).Should().Be(0);
        using (wire)
        {
            NativeMethods.xbim_wire_is_closed(wire, 1e-3, out var closed).Should().Be(0);
            closed.Should().Be(0);
        }
    }

    [Fact]
    public void Wire_BuildPolyline_DuplicatePoints_AreMerged()
    {
        // Three points but middle is near-duplicate of first → should merge to 2 vertices
        double[] pts = { 0, 0, 0, 1e-8, 0, 0, 10, 0, 0 };
        NativeMethods.xbim_wire_build_polyline(Ctx, pts, 3, 1e-3, out var wire).Should().Be(0);
        using (wire)
        {
            wire.IsInvalid.Should().BeFalse();
        }
    }

    [Fact]
    public void Wire_BuildPolygon_ClosedTriangle()
    {
        double[] pts = { 0, 0, 0, 10, 0, 0, 5, 10, 0 };
        NativeMethods.xbim_wire_build_polygon(Ctx, pts, 3, 1, out var wire).Should().Be(0);
        using (wire)
        {
            NativeMethods.xbim_wire_is_closed(wire, 1e-3, out var closed).Should().Be(0);
            closed.Should().Be(1);
        }
    }

    [Fact]
    public void Wire_BuildFromEdges_TwoLineEdges()
    {
        NativeMethods.xbim_edge_build_line(Ctx, 0, 0, 0, 10, 0, 0, out var e1).Should().Be(0);
        NativeMethods.xbim_edge_build_line(Ctx, 10, 0, 0, 10, 10, 0, out var e2).Should().Be(0);
        using (e1)
        using (e2)
        {
            var ptrs = new[] { e1.DangerousGetHandle(), e2.DangerousGetHandle() };
            NativeMethods.xbim_wire_build_from_edges(Ctx, ptrs, 2, out var wire).Should().Be(0);
            using (wire)
            {
                wire.IsInvalid.Should().BeFalse();
            }
        }
    }

    // ── Face ────────────────────────────────────────────────────────────────

    [Fact]
    public void Face_BuildFromWire_Square_HasCorrectArea()
    {
        // 10x10 square wire → face area = 100
        using var wire = BuildSquareWire(10);
        NativeMethods.xbim_face_build_from_wire(Ctx, wire, out var face).Should().Be(0);
        using (face)
        {
            NativeMethods.xbim_face_area(face, out var area).Should().Be(0);
            area.Should().BeApproximately(100, 0.1);
        }
    }

    [Fact]
    public void Face_BuildFromSurface_Plane_ReturnsValidFace()
    {
        NativeMethods.xbim_face_build_from_surface(Ctx,
            0, // XBIM_SURFACE_PLANE
            0, 0, 0,   // origin
            0, 0, 1,   // zDir (normal)
            1, 0, 0,   // xDir
            0,          // radius (unused for plane)
            1e-6,       // tolerance
            out var face).Should().Be(0);
        using (face)
        {
            face.IsInvalid.Should().BeFalse();
        }
    }

    [Fact]
    public void Face_Normal_PlanarXY_PointsInZ()
    {
        using var wire = BuildSquareWire(10);
        NativeMethods.xbim_face_build_from_wire(Ctx, wire, out var face).Should().Be(0);
        using (face)
        {
            NativeMethods.xbim_face_normal(face,
                double.NaN, double.NaN,
                out var nx, out var ny, out var nz).Should().Be(0);
            // Normal should point in Z direction (positive or negative)
            Math.Abs(nz).Should().BeApproximately(1.0, 1e-6);
            Math.Abs(nx).Should().BeLessThan(1e-6);
            Math.Abs(ny).Should().BeLessThan(1e-6);
        }
    }

    [Fact]
    public void Face_Area_Circle_IsCorrect()
    {
        // Build circle wire via circle arc edge (full circle)
        double r = 5;
        NativeMethods.xbim_edge_build_circle_arc(Ctx,
            0, 0, 0, 0, 0, 1, r, 0, 2 * Math.PI, out var circleEdge).Should().Be(0);
        using (circleEdge)
        {
            var ptrs = new[] { circleEdge.DangerousGetHandle() };
            NativeMethods.xbim_wire_build_from_edges(Ctx, ptrs, 1, out var wire).Should().Be(0);
            using (wire)
            {
                NativeMethods.xbim_face_build_from_wire(Ctx, wire, out var face).Should().Be(0);
                using (face)
                {
                    NativeMethods.xbim_face_area(face, out var area).Should().Be(0);
                    area.Should().BeApproximately(Math.PI * r * r, 0.1);
                }
            }
        }
    }

    [Fact]
    public void Face_BuildAdvanced_WithHole()
    {
        // Outer 20x20 square, inner 5x5 hole → area ≈ 400 - 25 = 375
        using var outer = BuildSquareWire(20);
        using var inner = BuildSquareWire(5, offsetX: 7.5, offsetY: 7.5);

        var innerPtrs = new[] { inner.DangerousGetHandle() };
        NativeMethods.xbim_face_build_advanced(Ctx,
            0, // plane
            0, 0, 0, 0, 0, 1, 1, 0, 0, 0, // surface placement
            outer, innerPtrs, 1,
            1e-6, 1, // tolerance, sameSense=true
            out var face).Should().Be(0);
        using (face)
        {
            NativeMethods.xbim_face_area(face, out var area).Should().Be(0);
            area.Should().BeApproximately(375, 1.0);
        }
    }

    // ── Shell ───────────────────────────────────────────────────────────────

    [Fact]
    public void Shell_BuildFromFaces_SixFaces_ReturnsShell()
    {
        using var shell = BuildBoxShell(10, 10, 10);
        shell.IsInvalid.Should().BeFalse();
    }

    [Fact]
    public void Shell_Sew_ValidOrientation_IsFixed()
    {
        using var shell = BuildBoxShell(10, 10, 10);
        NativeMethods.xbim_shell_sew(Ctx, shell, 1e-6, out var isFixed, out var sewn).Should().Be(0);
        using (sewn)
        {
            isFixed.Should().Be(1);
        }
    }

    [Fact]
    public void Shell_MakeSolid_ClosedBox_ReturnsSolid()
    {
        using var shell = BuildBoxShell(10, 10, 10);
        NativeMethods.xbim_shell_make_solid(Ctx, shell, out var solid).Should().Be(0);
        using (solid)
        {
            NativeMethods.xbim_shape_type(solid, out var shapeType).Should().Be(0);
            shapeType.Should().Be(5); // XBIM_SHAPE_SOLID
        }
    }

    [Fact]
    public void Shell_MakeSolid_Volume_IsCorrect()
    {
        using var shell = BuildBoxShell(10, 10, 10);
        NativeMethods.xbim_shell_make_solid(Ctx, shell, out var solid).Should().Be(0);
        using (solid)
        {
            NativeMethods.xbim_shape_volume(solid, out var vol).Should().Be(0);
            Math.Abs(vol).Should().BeApproximately(1000, 1.0);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private NativeShapeHandle BuildSquareWire(double size, double offsetX = 0, double offsetY = 0)
    {
        double x0 = offsetX, y0 = offsetY;
        double x1 = offsetX + size, y1 = offsetY + size;
        double[] pts = { x0, y0, 0, x1, y0, 0, x1, y1, 0, x0, y1, 0 };
        NativeMethods.xbim_wire_build_polygon(Ctx, pts, 4, 1, out var wire);
        return wire;
    }

    private NativeShapeHandle BuildBoxShell(double dx, double dy, double dz)
    {
        // 6 faces of axis-aligned box at origin
        var faces = new NativeShapeHandle[6];
        try
        {
            // Bottom (z=0) and Top (z=dz)
            double[] bottom = { 0, 0, 0, dx, 0, 0, dx, dy, 0, 0, dy, 0 };
            double[] top = { 0, 0, dz, 0, dy, dz, dx, dy, dz, dx, 0, dz };
            // Front (y=0) and Back (y=dy)
            double[] front = { 0, 0, 0, 0, 0, dz, dx, 0, dz, dx, 0, 0 };
            double[] back = { 0, dy, 0, dx, dy, 0, dx, dy, dz, 0, dy, dz };
            // Left (x=0) and Right (x=dx)
            double[] left = { 0, 0, 0, 0, dy, 0, 0, dy, dz, 0, 0, dz };
            double[] right = { dx, 0, 0, dx, 0, dz, dx, dy, dz, dx, dy, 0 };

            var facePoints = new[] { bottom, top, front, back, left, right };
            for (int i = 0; i < 6; i++)
            {
                NativeMethods.xbim_wire_build_polygon(Ctx, facePoints[i], 4, 1, out var wire);
                NativeMethods.xbim_face_build_from_wire(Ctx, wire, out faces[i]);
                wire.Dispose();
            }

            var facePtrs = faces.Select(f => f.DangerousGetHandle()).ToArray();
            NativeMethods.xbim_shell_build_from_faces(Ctx, facePtrs, 6, 1e-6, out var shell);
            return shell;
        }
        finally
        {
            foreach (var f in faces)
                f?.Dispose();
        }
    }
}
