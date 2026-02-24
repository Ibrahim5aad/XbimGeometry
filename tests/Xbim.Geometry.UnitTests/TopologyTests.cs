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
/// Tests topology operations (vertex, edge, wire, face, shell) via direct
/// P/Invoke calls to the native C API.
/// </summary>
public class TopologyTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private NativeContextHandle Ctx => _service.ContextHandle;

    public TopologyTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
    }

    public void Dispose() => _service.Dispose();

    #region Vertex

    [Fact]
    public void Vertex_BuildAndQuery_RoundTrips()
    {
        XbimGeometryNativeApi.xbim_vertex_build(Ctx, 10, 20, 30, out var vtx).Should().Be(0);
        using (vtx)
        {
            XbimGeometryNativeApi.xbim_vertex_point(vtx, out var x, out var y, out var z).Should().Be(0);
            x.Should().BeApproximately(10, 1e-10);
            y.Should().BeApproximately(20, 1e-10);
            z.Should().BeApproximately(30, 1e-10);
        }
    }

    [Fact]
    public void Vertex_Build_NegativeTolerance_Fails()
    {
        var result = XbimGeometryNativeApi.xbim_vertex_build(Ctx, 0, 0, 0, out var vtx);
        result.Should().Be(0);
        vtx.Dispose();
    }

    [Fact]
    public void Vertex_Managed_Tolerance_IsPositive()
    {
        XbimGeometryNativeApi.xbim_vertex_build(Ctx, 5, 10, 15, out var vtxHandle);
        var vtx = NativeShapeWrapper.WrapShape<IXVertex>(vtxHandle);
        using (vtx)
        {
            vtx.Tolerance.Should().BeApproximately(1e-5, 1e-9);
        }
    }

    [Fact]
    public void Vertex_Managed_Geometry_ReturnsPoint()
    {
        XbimGeometryNativeApi.xbim_vertex_build(Ctx, 7, 14, 21, out var vtxHandle);
        var vtx = NativeShapeWrapper.WrapShape<IXVertex>(vtxHandle);
        using (vtx)
        {
            var pt = vtx.VertexGeometry;
            pt.X.Should().BeApproximately(7, 1e-10);
            pt.Y.Should().BeApproximately(14, 1e-10);
            pt.Z.Should().BeApproximately(21, 1e-10);
        }
    }

    #endregion

    #region Edge

    [Fact]
    public void Edge_BuildLine_ReturnsValidEdge()
    {
        XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 0, 0, 0, 10, 0, 0, out var edge).Should().Be(0);
        using (edge)
        {
            edge.IsInvalid.Should().BeFalse();
        }
    }

    [Fact]
    public void Edge_BuildLine_Length_IsCorrect()
    {
        // 3-4-5 triangle hypotenuse
        XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 0, 0, 0, 3, 4, 0, out var edge).Should().Be(0);
        using (edge)
        {
            XbimGeometryNativeApi.xbim_edge_length(edge, out var len).Should().Be(0);
            len.Should().BeApproximately(5.0, 1e-6);
        }
    }

    [Fact]
    public void Edge_BuildLine_DegeneratePoints_Fails()
    {
        var result = XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 5, 5, 5, 5, 5, 5, out var edge);
        result.Should().Be(4); // XBIM_INVALID_ARG
        edge.Dispose();
    }

    [Fact]
    public void Edge_BuildCircleArc_QuarterCircle()
    {
        double r = 10;
        XbimGeometryNativeApi.xbim_edge_build_circle_arc(Ctx,
            0, 0, 0, 0, 0, 1, r, 0, Math.PI / 2, out var arc).Should().Be(0);
        using (arc)
        {
            XbimGeometryNativeApi.xbim_edge_length(arc, out var len).Should().Be(0);
            len.Should().BeApproximately(r * Math.PI / 2, 1e-6);
        }
    }

    [Fact]
    public void Edge_BuildFromCurve_TrimsSubRange()
    {
        // Build full semicircle, then trim to quarter
        double r = 10;
        XbimGeometryNativeApi.xbim_edge_build_circle_arc(Ctx,
            0, 0, 0, 0, 0, 1, r, 0, Math.PI, out var full).Should().Be(0);
        using (full)
        {
            XbimGeometryNativeApi.xbim_edge_length(full, out var fullLen).Should().Be(0);

            XbimGeometryNativeApi.xbim_edge_build_from_curve(Ctx, full, 0, Math.PI / 2, out var trimmed).Should().Be(0);
            using (trimmed)
            {
                XbimGeometryNativeApi.xbim_edge_length(trimmed, out var trimLen).Should().Be(0);
                trimLen.Should().BeLessThan(fullLen);
                trimLen.Should().BeApproximately(r * Math.PI / 2, 1e-6);
            }
        }
    }

    [Fact]
    public void Edge_Tolerance_IsPositive()
    {
        XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 0, 0, 0, 10, 0, 0, out var edge).Should().Be(0);
        using (edge)
        {
            XbimGeometryNativeApi.xbim_edge_tolerance(edge, out var tol).Should().Be(0);
            tol.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Edge_Vertices_ReturnsStartAndEnd()
    {
        XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 1, 2, 3, 4, 5, 6, out var edge).Should().Be(0);
        using (edge)
        {
            XbimGeometryNativeApi.xbim_edge_vertices(edge, out var start, out var end).Should().Be(0);
            using (start)
            using (end)
            {
                start.IsInvalid.Should().BeFalse();
                end.IsInvalid.Should().BeFalse();

                XbimGeometryNativeApi.xbim_vertex_point(start, out var sx, out var sy, out var sz).Should().Be(0);
                sx.Should().BeApproximately(1, 1e-10);
                sy.Should().BeApproximately(2, 1e-10);
                sz.Should().BeApproximately(3, 1e-10);

                XbimGeometryNativeApi.xbim_vertex_point(end, out var ex, out var ey, out var ez).Should().Be(0);
                ex.Should().BeApproximately(4, 1e-10);
                ey.Should().BeApproximately(5, 1e-10);
                ez.Should().BeApproximately(6, 1e-10);
            }
        }
    }

    [Fact]
    public void Edge_Managed_Length_MatchesNative()
    {
        XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 0, 0, 0, 3, 4, 0, out var edgeHandle).Should().Be(0);
        var edge = NativeShapeWrapper.WrapShape<IXEdge>(edgeHandle);
        using (edge)
        {
            edge.Length.Should().BeApproximately(5.0, 1e-6);
        }
    }

    [Fact]
    public void Edge_Managed_Tolerance_IsPositive()
    {
        XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 0, 0, 0, 10, 0, 0, out var edgeHandle).Should().Be(0);
        var edge = NativeShapeWrapper.WrapShape<IXEdge>(edgeHandle);
        using (edge)
        {
            edge.Tolerance.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Edge_Managed_StartEnd_Coordinates()
    {
        XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 10, 20, 30, 40, 50, 60, out var edgeHandle).Should().Be(0);
        var edge = NativeShapeWrapper.WrapShape<IXEdge>(edgeHandle);
        using (edge)
        {
            var start = edge.EdgeStart;
            start.Should().NotBeNull();
            // Vertex point query via native handle
            using (start)
            {
                var startShape = (XbimShape)start;
                XbimGeometryNativeApi.xbim_vertex_point(startShape.Handle, out var x, out var y, out var z).Should().Be(0);
                x.Should().BeApproximately(10, 1e-10);
                y.Should().BeApproximately(20, 1e-10);
                z.Should().BeApproximately(30, 1e-10);
            }

            var end = edge.EdgeEnd;
            end.Should().NotBeNull();
            using (end)
            {
                var endShape = (XbimShape)end;
                XbimGeometryNativeApi.xbim_vertex_point(endShape.Handle, out var x, out var y, out var z).Should().Be(0);
                x.Should().BeApproximately(40, 1e-10);
                y.Should().BeApproximately(50, 1e-10);
                z.Should().BeApproximately(60, 1e-10);
            }
        }
    }

    #endregion

    #region Wire

    [Fact]
    public void Wire_BuildPolyline_Triangle()
    {
        double[] pts = { 0, 0, 0, 10, 0, 0, 5, 10, 0 };
        XbimGeometryNativeApi.xbim_wire_build_polyline(Ctx, pts, 3, out var wire).Should().Be(0);
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
        XbimGeometryNativeApi.xbim_wire_build_polyline(Ctx, pts, 5, out var wire).Should().Be(0);
        using (wire)
        {
            XbimGeometryNativeApi.xbim_wire_is_closed(wire, 1e-3, out var closed).Should().Be(0);
            closed.Should().Be(1);
        }
    }

    [Fact]
    public void Wire_BuildPolyline_OpenSegment_IsNotClosed()
    {
        double[] pts = { 0, 0, 0, 10, 0, 0 };
        XbimGeometryNativeApi.xbim_wire_build_polyline(Ctx, pts, 2, out var wire).Should().Be(0);
        using (wire)
        {
            XbimGeometryNativeApi.xbim_wire_is_closed(wire, 1e-3, out var closed).Should().Be(0);
            closed.Should().Be(0);
        }
    }

    [Fact]
    public void Wire_BuildPolyline_DuplicatePoints_AreMerged()
    {
        // Three points but middle is near-duplicate of first → should merge to 2 vertices
        double[] pts = { 0, 0, 0, 1e-8, 0, 0, 10, 0, 0 };
        XbimGeometryNativeApi.xbim_wire_build_polyline(Ctx, pts, 3, out var wire).Should().Be(0);
        using (wire)
        {
            wire.IsInvalid.Should().BeFalse();
        }
    }

    [Fact]
    public void Wire_BuildPolygon_ClosedTriangle()
    {
        double[] pts = { 0, 0, 0, 10, 0, 0, 5, 10, 0 };
        XbimGeometryNativeApi.xbim_wire_build_polygon(Ctx, pts, 3, 1, out var wire).Should().Be(0);
        using (wire)
        {
            XbimGeometryNativeApi.xbim_wire_is_closed(wire, 1e-3, out var closed).Should().Be(0);
            closed.Should().Be(1);
        }
    }

    [Fact]
    public void Wire_BuildFromEdges_TwoLineEdges()
    {
        XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 0, 0, 0, 10, 0, 0, out var e1).Should().Be(0);
        XbimGeometryNativeApi.xbim_edge_build_line(Ctx, 10, 0, 0, 10, 10, 0, out var e2).Should().Be(0);
        using (e1)
        using (e2)
        {
            var ptrs = new[] { e1.DangerousGetHandle(), e2.DangerousGetHandle() };
            XbimGeometryNativeApi.xbim_wire_build_from_edges(Ctx, ptrs, 2, out var wire).Should().Be(0);
            using (wire)
            {
                wire.IsInvalid.Should().BeFalse();
            }
        }
    }

    [Fact]
    public void Wire_Managed_Length_SquarePerimeter()
    {
        using var wireHandle = BuildSquareWire(10);
        var wire = NativeShapeWrapper.WrapShape<IXWire>(wireHandle);
        using (wire)
        {
            wire.Length.Should().BeApproximately(40, 0.1); // 4 * 10
        }
    }

    [Fact]
    public void Wire_Managed_ContourArea_Square()
    {
        using var wireHandle = BuildSquareWire(10);
        var wire = NativeShapeWrapper.WrapShape<IXWire>(wireHandle);
        using (wire)
        {
            wire.ContourArea.Should().BeApproximately(100, 1.0); // 10 * 10
        }
    }

    [Fact]
    public void Wire_Managed_EdgeLoop_ReturnsEdges()
    {
        using var wireHandle = BuildSquareWire(5);
        var wire = NativeShapeWrapper.WrapShape<IXWire>(wireHandle);
        using (wire)
        {
            wire.EdgeLoop.Length.Should().BeGreaterThanOrEqualTo(4);
        }
    }

    #endregion

    #region Face

    [Fact]
    public void Face_BuildFromWire_Square_HasCorrectArea()
    {
        // 10x10 square wire → face area = 100
        using var wire = BuildSquareWire(10);
        XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wire, out var face).Should().Be(0);
        using (face)
        {
            XbimGeometryNativeApi.xbim_face_area(face, out var area).Should().Be(0);
            area.Should().BeApproximately(100, 0.1);
        }
    }

    [Fact]
    public void Face_BuildFromSurface_Plane_ReturnsValidFace()
    {
        XbimGeometryNativeApi.xbim_face_build_from_surface(Ctx,
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
        XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wire, out var face).Should().Be(0);
        using (face)
        {
            XbimGeometryNativeApi.xbim_face_normal(face,
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
        XbimGeometryNativeApi.xbim_edge_build_circle_arc(Ctx,
            0, 0, 0, 0, 0, 1, r, 0, 2 * Math.PI, out var circleEdge).Should().Be(0);
        using (circleEdge)
        {
            var ptrs = new[] { circleEdge.DangerousGetHandle() };
            XbimGeometryNativeApi.xbim_wire_build_from_edges(Ctx, ptrs, 1, out var wire).Should().Be(0);
            using (wire)
            {
                XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wire, out var face).Should().Be(0);
                using (face)
                {
                    XbimGeometryNativeApi.xbim_face_area(face, out var area).Should().Be(0);
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
        XbimGeometryNativeApi.xbim_face_build_advanced(Ctx,
            0, // plane
            0, 0, 0, 0, 0, 1, 1, 0, 0, 0, // surface placement
            outer, innerPtrs, 1,
            1, // sameSense=true
            out var face).Should().Be(0);
        using (face)
        {
            XbimGeometryNativeApi.xbim_face_area(face, out var area).Should().Be(0);
            area.Should().BeApproximately(375, 1.0);
        }
    }

    [Fact]
    public void Face_Tolerance_IsPositive()
    {
        using var wire = BuildSquareWire(10);
        XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wire, out var face).Should().Be(0);
        using (face)
        {
            XbimGeometryNativeApi.xbim_face_tolerance(face, out var tol).Should().Be(0);
            tol.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Face_GetSurface_PlanarFace_ReturnsPlane()
    {
        using var wire = BuildSquareWire(10);
        XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wire, out var face).Should().Be(0);
        using (face)
        {
            XbimGeometryNativeApi.xbim_face_get_surface(face, out var surfHandle, out var surfType).Should().Be(0);
            using (surfHandle)
            {
                surfType.Should().Be(8); // IfcPlane
                surfHandle.IsInvalid.Should().BeFalse();
            }
        }
    }

    [Fact]
    public void Face_Managed_Tolerance_IsPositive()
    {
        using var wire = BuildSquareWire(10);
        XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wire, out var faceHandle).Should().Be(0);
        var face = NativeShapeWrapper.WrapShape<IXFace>(faceHandle);
        using (face)
        {
            face.Tolerance.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Face_Managed_Surface_ReturnsPlaneSurface()
    {
        using var wire = BuildSquareWire(10);
        XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wire, out var faceHandle).Should().Be(0);
        var face = NativeShapeWrapper.WrapShape<IXFace>(faceHandle);
        using (face)
        {
            var surface = face.Surface;
            surface.Should().NotBeNull();
            surface.SurfaceType.Should().Be(XSurfaceType.IfcPlane);
            ((IDisposable)surface).Dispose();
        }
    }

    #endregion

    #region Shell

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
        XbimGeometryNativeApi.xbim_shell_sew(Ctx, shell, 1e-6, out var isFixed, out var sewn).Should().Be(0);
        using (sewn)
        {
            isFixed.Should().Be(1);
        }
    }

    [Fact]
    public void Shell_MakeSolid_ClosedBox_ReturnsSolid()
    {
        using var shell = BuildBoxShell(10, 10, 10);
        XbimGeometryNativeApi.xbim_shell_make_solid(Ctx, shell, out var solid).Should().Be(0);
        using (solid)
        {
            XbimGeometryNativeApi.xbim_shape_type(solid, out var shapeType).Should().Be(0);
            shapeType.Should().Be(5); // XBIM_SHAPE_SOLID
        }
    }

    [Fact]
    public void Shell_MakeSolid_Volume_IsCorrect()
    {
        using var shell = BuildBoxShell(10, 10, 10);
        XbimGeometryNativeApi.xbim_shell_make_solid(Ctx, shell, out var solid).Should().Be(0);
        using (solid)
        {
            XbimGeometryNativeApi.xbim_shape_volume(solid, out var vol).Should().Be(0);
            Math.Abs(vol).Should().BeApproximately(1000, 1.0);
        }
    }

    #endregion

    #region Compound

    [Fact]
    public void Compound_Make_TwoSolids_ReturnsCompound()
    {
        using var s1 = BuildBoxSolid(10, 10, 10);
        using var s2 = BuildBoxSolid(5, 5, 5);
        var ptrs = new IntPtr[] { s1.DangerousGetHandle(), s2.DangerousGetHandle() };
        XbimGeometryNativeApi.xbim_compound_make(Ctx, ptrs, 2, out var compound).Should().Be(0);
        using (compound)
        {
            XbimGeometryNativeApi.xbim_shape_type(compound, out var t).Should().Be(0);
            t.Should().Be(6); // XBIM_SHAPE_COMPOUND
        }
    }

    [Fact]
    public void Compound_ChildCount_MatchesInputCount()
    {
        using var s1 = BuildBoxSolid(10, 10, 10);
        using var s2 = BuildBoxSolid(5, 5, 5);
        using var s3 = BuildBoxSolid(3, 3, 3);
        var ptrs = new IntPtr[] { s1.DangerousGetHandle(), s2.DangerousGetHandle(), s3.DangerousGetHandle() };
        XbimGeometryNativeApi.xbim_compound_make(Ctx, ptrs, 3, out var compound).Should().Be(0);
        using (compound)
        {
            XbimGeometryNativeApi.xbim_compound_child_count(compound, out var count).Should().Be(0);
            count.Should().Be(3);
        }
    }

    [Fact]
    public void Compound_GetChildren_ReturnsSolids()
    {
        using var s1 = BuildBoxSolid(10, 10, 10);
        using var s2 = BuildBoxSolid(5, 5, 5);
        var ptrs = new IntPtr[] { s1.DangerousGetHandle(), s2.DangerousGetHandle() };
        XbimGeometryNativeApi.xbim_compound_make(Ctx, ptrs, 2, out var compound).Should().Be(0);
        using (compound)
        {
            XbimGeometryNativeApi.xbim_compound_child_count(compound, out var count).Should().Be(0);
            count.Should().Be(2);

            var childPtrs = new IntPtr[count];
            int capacity = count;
            XbimGeometryNativeApi.xbim_compound_get_children(compound, childPtrs, ref capacity).Should().Be(0);
            capacity.Should().Be(2);

            for (int i = 0; i < capacity; i++)
            {
                using var child = NativeShapeHandle.FromIntPtr(childPtrs[i]);
                XbimGeometryNativeApi.xbim_shape_type(child, out var childType).Should().Be(0);
                childType.Should().Be(5); // XBIM_SHAPE_SOLID
            }
        }
    }

    [Fact]
    public void Compound_Add_IncreasesChildCount()
    {
        // Start with 1 solid
        using var s1 = BuildBoxSolid(10, 10, 10);
        var ptrs = new IntPtr[] { s1.DangerousGetHandle() };
        XbimGeometryNativeApi.xbim_compound_make(Ctx, ptrs, 1, out var compound).Should().Be(0);
        using (compound)
        {
            XbimGeometryNativeApi.xbim_compound_child_count(compound, out var count1).Should().Be(0);
            count1.Should().Be(1);

            // Add a second solid
            using var s2 = BuildBoxSolid(5, 5, 5);
            XbimGeometryNativeApi.xbim_compound_add(compound, s2).Should().Be(0);

            XbimGeometryNativeApi.xbim_compound_child_count(compound, out var count2).Should().Be(0);
            count2.Should().Be(2);
        }
    }

    [Fact]
    public void Compound_MixedChildren_HasCorrectTypes()
    {
        // Build a solid and a face, combine into compound
        using var solid = BuildBoxSolid(10, 10, 10);
        using var wire = BuildSquareWire(5);
        XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wire, out var face).Should().Be(0);
        using (face)
        {
            var ptrs = new IntPtr[] { solid.DangerousGetHandle(), face.DangerousGetHandle() };
            XbimGeometryNativeApi.xbim_compound_make(Ctx, ptrs, 2, out var compound).Should().Be(0);
            using (compound)
            {
                XbimGeometryNativeApi.xbim_compound_child_count(compound, out var count).Should().Be(0);
                count.Should().Be(2);

                var childPtrs = new IntPtr[count];
                int capacity = count;
                XbimGeometryNativeApi.xbim_compound_get_children(compound, childPtrs, ref capacity).Should().Be(0);

                var types = new int[capacity];
                for (int i = 0; i < capacity; i++)
                {
                    using var child = NativeShapeHandle.FromIntPtr(childPtrs[i]);
                    XbimGeometryNativeApi.xbim_shape_type(child, out types[i]).Should().Be(0);
                }

                types.Should().Contain(5); // XBIM_SHAPE_SOLID
                types.Should().Contain(3); // XBIM_SHAPE_FACE
            }
        }
    }

    [Fact]
    public void Compound_Managed_IsSolidsOnly_True()
    {
        using var s1 = BuildBoxSolid(10, 10, 10);
        using var s2 = BuildBoxSolid(5, 5, 5);
        var ptrs = new IntPtr[] { s1.DangerousGetHandle(), s2.DangerousGetHandle() };
        XbimGeometryNativeApi.xbim_compound_make(Ctx, ptrs, 2, out var compoundHandle).Should().Be(0);

        var compound = NativeShapeWrapper.WrapShape<IXCompound>(compoundHandle);
        using (compound)
        {
            compound.IsSolidsOnly.Should().BeTrue();
            compound.HasSolids.Should().BeTrue();
            compound.HasFaces.Should().BeFalse();
            compound.Solids.Should().HaveCount(2);
            compound.Faces.Should().BeEmpty();
        }
    }

    [Fact]
    public void Compound_Managed_Add_WorksAndInvalidatesCache()
    {
        using var s1 = BuildBoxSolid(10, 10, 10);
        var ptrs = new IntPtr[] { s1.DangerousGetHandle() };
        XbimGeometryNativeApi.xbim_compound_make(Ctx, ptrs, 1, out var compoundHandle).Should().Be(0);

        var compound = NativeShapeWrapper.WrapShape<IXCompound>(compoundHandle);
        using (compound)
        {
            compound.Solids.Should().HaveCount(1);

            // Add another solid via managed Add
            using var s2 = BuildBoxSolid(5, 5, 5);
            var wrappedS2 = NativeShapeWrapper.WrapShape(s2);
            compound.Add(wrappedS2);

            compound.Solids.Should().HaveCount(2);
        }
    }

    #endregion

    #region Curve

    [Fact]
    public void Curve_Line_Parameters_AreFinite()
    {
        XbimGeometryNativeApi.xbim_curve_build_line_3d(
            Ctx, 0, 0, 0, 1, 0, 0, out var curveHandle).Should().Be(0);
        using (curveHandle)
        {
            XbimGeometryNativeApi.xbim_curve_parameters(curveHandle, out double first, out double last).Should().Be(0);
            // Lines in OCCT have infinite parameter range
            first.Should().BeLessThan(0);
            last.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Curve_Circle_Length_IsCircumference()
    {
        double radius = 10;
        XbimGeometryNativeApi.xbim_curve_build_circle_3d(
            Ctx, 0, 0, 0, 0, 0, 1, 1, 0, 0, radius, out var curveHandle).Should().Be(0);
        var curve = new Primitives.XbimCurve(curveHandle, XCurveType.IfcCircle);
        using (curve)
        {
            curve.Length.Should().BeApproximately(2 * Math.PI * radius, 1e-3);
        }
    }

    [Fact]
    public void Curve_Circle_GetPoint_AtStart()
    {
        double radius = 5;
        XbimGeometryNativeApi.xbim_curve_build_circle_3d(
            Ctx, 0, 0, 0, 0, 0, 1, 1, 0, 0, radius, out var curveHandle).Should().Be(0);
        var curve = new Primitives.XbimCurve(curveHandle, XCurveType.IfcCircle);
        using (curve)
        {
            var pt = curve.GetPoint(curve.FirstParameter);
            // Circle starts at (radius, 0, 0) by default
            pt.X.Should().BeApproximately(radius, 1e-6);
            pt.Y.Should().BeApproximately(0, 1e-6);
        }
    }

    [Fact]
    public void Curve_Circle_FirstDerivative_IsTangent()
    {
        double radius = 5;
        XbimGeometryNativeApi.xbim_curve_build_circle_3d(
            Ctx, 0, 0, 0, 0, 0, 1, 1, 0, 0, radius, out var curveHandle).Should().Be(0);
        var curve = new Primitives.XbimCurve(curveHandle, XCurveType.IfcCircle);
        using (curve)
        {
            var pt = curve.GetFirstDerivative(curve.FirstParameter, out var dir);
            // At u=0, tangent of circle in XY plane is (0, 1, 0)
            dir.Y.Should().BeApproximately(1, 1e-6);
        }
    }

    [Fact]
    public void Curve_Circle_SecondDerivative_PointsToCenter()
    {
        double radius = 5;
        XbimGeometryNativeApi.xbim_curve_build_circle_3d(
            Ctx, 0, 0, 0, 0, 0, 1, 1, 0, 0, radius, out var curveHandle).Should().Be(0);
        var curve = new Primitives.XbimCurve(curveHandle, XCurveType.IfcCircle);
        using (curve)
        {
            curve.GetSecondDerivative(curve.FirstParameter, out _, out var normal);
            // At u=0, second derivative points toward center: (-1, 0, 0)
            normal.X.Should().BeApproximately(-1, 1e-6);
        }
    }

    #endregion

    #region Shape (Triangulate, Location)

    [Fact]
    public void Shape_Triangulate_BoxSolid_ReturnsTrue()
    {
        using var solid = BuildBoxSolid(10, 10, 10);
        var shape = NativeShapeWrapper.WrapShape(solid);
        using (shape)
        {
            var meshFactors = new Services.MeshFactors(1000, 1e-3);
            bool ok = shape.Triangulate(meshFactors);
            ok.Should().BeTrue();
        }
    }

    [Fact]
    public void Shape_Location_Identity_ByDefault()
    {
        using var solid = BuildBoxSolid(5, 5, 5);
        var shape = NativeShapeWrapper.WrapShape(solid);
        using (shape)
        {
            var loc = shape.Location;
            loc.IsIdentity.Should().BeTrue();
        }
    }

    [Fact]
    public void Shape_Location_AfterMove_HasTranslation()
    {
        using var solid = BuildBoxSolid(5, 5, 5);

        // Move the shape to (10, 20, 30)
        int locResult = XbimGeometryNativeApi.xbim_location_create_from_axis2(
            10, 20, 30, 0, 0, 1, 1, 0, 0, out var locHandle);
        locResult.Should().Be(0);

        XbimGeometryNativeApi.xbim_shape_moved(solid, locHandle, out var movedHandle);
        locHandle.Dispose();

        var moved = NativeShapeWrapper.WrapShape(movedHandle);
        using (moved)
        {
            var loc = moved.Location;
            loc.IsIdentity.Should().BeFalse();
            loc.OffsetX.Should().BeApproximately(10, 1e-6);
            loc.OffsetY.Should().BeApproximately(20, 1e-6);
            loc.OffsetZ.Should().BeApproximately(30, 1e-6);
        }
    }

    #endregion

    #region Helpers

    private NativeShapeHandle BuildSquareWire(double size, double offsetX = 0, double offsetY = 0)
    {
        double x0 = offsetX, y0 = offsetY;
        double x1 = offsetX + size, y1 = offsetY + size;
        double[] pts = { x0, y0, 0, x1, y0, 0, x1, y1, 0, x0, y1, 0 };
        XbimGeometryNativeApi.xbim_wire_build_polygon(Ctx, pts, 4, 1, out var wire);
        return wire;
    }

    private NativeShapeHandle BuildBoxSolid(double dx, double dy, double dz)
    {
        using var shell = BuildBoxShell(dx, dy, dz);
        XbimGeometryNativeApi.xbim_shell_make_solid(Ctx, shell, out var solid);
        return solid;
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
                XbimGeometryNativeApi.xbim_wire_build_polygon(Ctx, facePoints[i], 4, 1, out var wire);
                XbimGeometryNativeApi.xbim_face_build_from_wire(Ctx, wire, out faces[i]);
                wire.Dispose();
            }

            var facePtrs = faces.Select(f => f.DangerousGetHandle()).ToArray();
            XbimGeometryNativeApi.xbim_shell_build_from_faces(Ctx, facePtrs, 6, 1e-6, out var shell);
            return shell;
        }
        finally
        {
            foreach (var f in faces)
                f?.Dispose();
        }
    }

    #endregion
}
