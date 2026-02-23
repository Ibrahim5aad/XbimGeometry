using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests spiral curve construction and conversion to wire/edge topology
/// via direct P/Invoke calls to the native C API.
/// </summary>
public class SpiralTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly string _brepOutputDir;
    private NativeContextHandle Ctx => _service.ContextHandle;

    public SpiralTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);

        _brepOutputDir = Path.Combine(
            Path.GetDirectoryName(typeof(SpiralTests).Assembly.Location)!,
            "BrepOutput");
        Directory.CreateDirectory(_brepOutputDir);
    }

    public void Dispose() => _service.Dispose();

    #region Clothoid

    [Fact]
    public void Clothoid_BuildCurve_Succeeds()
    {
        // A = 100, arc length 0..50, placement at origin, direction along X
        XbimGeometryNativeApi.xbim_curve_build_clothoid(
            Ctx, 100.0, 0.0, 50.0, 0, 0, 1, 0, out var curveHandle).Should().Be(0);
        using (curveHandle)
        {
            curveHandle.IsInvalid.Should().BeFalse();

            // Check parametric range
            XbimGeometryNativeApi.xbim_curve_parameters(curveHandle, out double first, out double last).Should().Be(0);
            first.Should().BeLessThanOrEqualTo(0.0);
            last.Should().BeGreaterThanOrEqualTo(50.0 - 1e-6);

            // Curve should have positive length
            XbimGeometryNativeApi.xbim_curve_length(curveHandle, out double length).Should().Be(0);
            length.Should().BeGreaterThan(0);

            // Start point should be near the placement origin (0,0,0)
            XbimGeometryNativeApi.xbim_curve_value(curveHandle, first, out double sx, out double sy, out double sz).Should().Be(0);
            sz.Should().BeApproximately(0, 1e-6); // 2D spiral in XY plane
        }
    }

    [Fact]
    public void Clothoid_ConvertToWire_SaveBrep()
    {
        // Build clothoid: A = 100, arc length 0..80
        double clothoidA = 100.0;
        double startParam = 0.0;
        double endParam = 80.0;

        XbimGeometryNativeApi.xbim_curve_build_clothoid(
            Ctx, clothoidA, startParam, endParam, 0, 0, 1, 0, out var curveHandle).Should().Be(0);
        using (curveHandle)
        {
            // Get parametric range and evaluate start/end points
            XbimGeometryNativeApi.xbim_curve_parameters(curveHandle, out double first, out double last).Should().Be(0);

            XbimGeometryNativeApi.xbim_curve_value(curveHandle, first, out double sx, out double sy, out double sz).Should().Be(0);
            XbimGeometryNativeApi.xbim_curve_value(curveHandle, last, out double ex, out double ey, out double ez).Should().Be(0);

            // Build edge from curve handle
            XbimGeometryNativeApi.xbim_edge_build_from_curve_handle(
                Ctx, curveHandle,
                sx, sy, sz,
                ex, ey, ez,
                1,
                out var edgeHandle).Should().Be(0);
            using (edgeHandle)
            {
                edgeHandle.IsInvalid.Should().BeFalse();

                // Verify edge length is positive
                XbimGeometryNativeApi.xbim_edge_length(edgeHandle, out double edgeLen).Should().Be(0);
                edgeLen.Should().BeGreaterThan(0);

                // Build wire from the single edge
                var ptrs = new[] { edgeHandle.DangerousGetHandle() };
                XbimGeometryNativeApi.xbim_wire_build_from_edges(Ctx, ptrs, 1, out var wireHandle).Should().Be(0);
                using (wireHandle)
                {
                    wireHandle.IsInvalid.Should().BeFalse();

                    // Verify wire length
                    XbimGeometryNativeApi.xbim_wire_length(wireHandle, out double wireLen).Should().Be(0);
                    wireLen.Should().BeGreaterThan(0);

                    // Save wire as .brep for inspection
                    var brepPath = Path.Combine(_brepOutputDir, "clothoid_A100_0_80.brep");
                    XbimGeometryNativeApi.xbim_shape_write_brep(wireHandle, brepPath).Should().Be(0);
                }
            }
        }
    }

    [Fact]
    public void Clothoid_DifferentConstants_ProduceDifferentCurves()
    {
        // Two clothoids with different A values should produce different curves
        XbimGeometryNativeApi.xbim_curve_build_clothoid(
            Ctx, 50.0, 0, 30.0, 0, 0, 1, 0, out var c1).Should().Be(0);
        XbimGeometryNativeApi.xbim_curve_build_clothoid(
            Ctx, 200.0, 0, 30.0, 0, 0, 1, 0, out var c2).Should().Be(0);
        using (c1)
        using (c2)
        {
            // Evaluate both at t=30 (end) — different A means different end points
            XbimGeometryNativeApi.xbim_curve_parameters(c1, out _, out double last1).Should().Be(0);
            XbimGeometryNativeApi.xbim_curve_parameters(c2, out _, out double last2).Should().Be(0);

            XbimGeometryNativeApi.xbim_curve_value(c1, last1, out double x1, out double y1, out _).Should().Be(0);
            XbimGeometryNativeApi.xbim_curve_value(c2, last2, out double x2, out double y2, out _).Should().Be(0);

            // End points must differ (smaller A = tighter spiral = more deviation)
            var dist = Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
            dist.Should().BeGreaterThan(0.1);
        }
    }

    [Fact]
    public void Clothoid_ZeroConstant_Fails()
    {
        var result = XbimGeometryNativeApi.xbim_curve_build_clothoid(
            Ctx, 0.0, 0, 10.0, 0, 0, 1, 0, out var curveHandle);
        result.Should().NotBe(0); // XBIM_INVALID_ARG
        curveHandle.IsInvalid.Should().BeTrue();
    }

    [Fact]
    public void Clothoid_WithOffset_SaveBrep()
    {
        // Clothoid with placement offset (10, 5) and rotated direction
        XbimGeometryNativeApi.xbim_curve_build_clothoid(
            Ctx, 150.0, 0, 60.0,
            10.0, 5.0,      // placement offset
            0.707, 0.707,   // 45-degree direction
            out var curveHandle).Should().Be(0);
        using (curveHandle)
        {
            XbimGeometryNativeApi.xbim_curve_parameters(curveHandle, out double first, out double last).Should().Be(0);
            XbimGeometryNativeApi.xbim_curve_value(curveHandle, first, out double sx, out double sy, out double sz).Should().Be(0);
            XbimGeometryNativeApi.xbim_curve_value(curveHandle, last, out double ex, out double ey, out double ez).Should().Be(0);

            // Start point should be near the placement origin (10, 5, 0)
            sx.Should().BeApproximately(10.0, 0.5);
            sy.Should().BeApproximately(5.0, 0.5);

            // Build edge → wire → save brep
            XbimGeometryNativeApi.xbim_edge_build_from_curve_handle(
                Ctx, curveHandle,
                sx, sy, sz, ex, ey, ez,
                1, out var edge).Should().Be(0);
            using (edge)
            {
                var ptrs = new[] { edge.DangerousGetHandle() };
                XbimGeometryNativeApi.xbim_wire_build_from_edges(Ctx, ptrs, 1, out var wire).Should().Be(0);
                using (wire)
                {
                    var brepPath = Path.Combine(_brepOutputDir, "clothoid_A150_offset_45deg.brep");
                    XbimGeometryNativeApi.xbim_shape_write_brep(wire, brepPath).Should().Be(0);
                }
            }
        }
    }

    #endregion
}
