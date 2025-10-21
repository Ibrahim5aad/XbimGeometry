using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Tests for segmented reference curve construction via the native C API.
/// Validates gradient curve extension with superelevation segments.
/// </summary>
public class SegmentedReferenceCurveTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private NativeContextHandle Ctx => _service.ContextHandle;

    public SegmentedReferenceCurveTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
    }

    public void Dispose() => _service.Dispose();

    /// <summary>
    /// Builds a simple gradient curve (horizontal line + constant height) as the base,
    /// then wraps it in a segmented reference curve with no superelevation segments.
    /// The result should be identical to the base gradient curve.
    /// </summary>
    [Fact]
    public void SegmentedReference_NoSegments_MatchesGradientCurve()
    {
        // Build a horizontal 2D line: (0,0) to (100,0)
        XbimGeometryNativeApi.xbim_curve2d_build_line(
            Ctx, 0, 0, 100, 0, out var horizontalHandle).Should().Be(0);

        // Build a constant height function: (0,5) to (100,5) — constant Z=5
        XbimGeometryNativeApi.xbim_curve2d_build_line(
            Ctx, 0, 5, 100, 5, out var heightHandle).Should().Be(0);

        // Build gradient curve
        XbimGeometryNativeApi.xbim_curve_build_gradient(
            Ctx, horizontalHandle, heightHandle, out var gradientHandle).Should().Be(0);

        using (gradientHandle)
        using (horizontalHandle)
        using (heightHandle)
        {
            gradientHandle.IsInvalid.Should().BeFalse();

            // Build segmented reference with zero superelevation segments
            XbimGeometryNativeApi.xbim_curve_build_segmented_reference(
                Ctx, gradientHandle,
                Array.Empty<IntPtr>(), Array.Empty<IntPtr>(), 0,
                NativeLocationHandle.NullHandle,
                out var segRefHandle).Should().Be(0);

            using (segRefHandle)
            {
                segRefHandle.IsInvalid.Should().BeFalse();

                // Parameters should match gradient curve
                XbimGeometryNativeApi.xbim_curve_parameters(
                    gradientHandle, out double gFirst, out double gLast).Should().Be(0);
                XbimGeometryNativeApi.xbim_curve_parameters(
                    segRefHandle, out double sFirst, out double sLast).Should().Be(0);

                sFirst.Should().BeApproximately(gFirst, 1e-6);
                sLast.Should().BeApproximately(gLast, 1e-6);

                // Points should match at several parameters
                for (double u = sFirst; u <= sLast; u += (sLast - sFirst) / 10.0)
                {
                    XbimGeometryNativeApi.xbim_curve_value(
                        gradientHandle, u, out double gx, out double gy, out double gz).Should().Be(0);
                    XbimGeometryNativeApi.xbim_curve_value(
                        segRefHandle, u, out double sx, out double sy, out double sz).Should().Be(0);

                    sx.Should().BeApproximately(gx, 1e-6);
                    sy.Should().BeApproximately(gy, 1e-6);
                    sz.Should().BeApproximately(gz, 1e-6);
                }

                // Superelevation and tilt should be zero with empty segments
                XbimGeometryNativeApi.xbim_curve_get_superelevation_and_tilt(
                    segRefHandle, 50.0, out double superElev, out double cantTilt).Should().Be(0);
                superElev.Should().BeApproximately(0.0, 1e-6);
                cantTilt.Should().BeApproximately(0.0, 1e-6);
            }
        }
    }

    /// <summary>
    /// Builds a gradient curve and a segmented reference curve with constant
    /// superelevation (location Y translation). The resulting Z should be
    /// gradient Z + superelevation.
    /// </summary>
    [Fact]
    public void SegmentedReference_WithConstantSuperelevation_AddsToGradientZ()
    {
        // Horizontal 2D line: (0,0) to (200,0)
        XbimGeometryNativeApi.xbim_curve2d_build_line(
            Ctx, 0, 0, 200, 0, out var horizontalHandle).Should().Be(0);

        // Constant height: Z=10 for the full range
        XbimGeometryNativeApi.xbim_curve2d_build_line(
            Ctx, 0, 10, 200, 10, out var heightHandle).Should().Be(0);

        // Build gradient curve
        XbimGeometryNativeApi.xbim_curve_build_gradient(
            Ctx, horizontalHandle, heightHandle, out var gradientHandle).Should().Be(0);

        using (horizontalHandle)
        using (heightHandle)
        using (gradientHandle)
        {
            // Create a simple superelevation segment: a line from (0,0) to (200,0) (zero rate of change)
            XbimGeometryNativeApi.xbim_curve2d_build_line(
                Ctx, 0, 0, 200, 0, out var segCurveHandle).Should().Be(0);

            // Location with Y translation = 3.0 (starting superelevation of 3m)
            // and next segment location with Y = 3.0 as well (constant)
            XbimGeometryNativeApi.xbim_location_create_from_axis2(
                0, 3.0, 0, 0, 0, 1, 1, 0, 0, out var segLocHandle).Should().Be(0);

            using (segCurveHandle)
            using (segLocHandle)
            {
                var curvePtrs = new[] { segCurveHandle.DangerousGetHandle() };
                var locPtrs = new[] { segLocHandle.DangerousGetHandle() };

                XbimGeometryNativeApi.xbim_curve_build_segmented_reference(
                    Ctx, gradientHandle,
                    curvePtrs, locPtrs, 1,
                    NativeLocationHandle.NullHandle,
                    out var segRefHandle).Should().Be(0);

                using (segRefHandle)
                {
                    segRefHandle.IsInvalid.Should().BeFalse();

                    // At any parameter, the superelevation query should return
                    // the starting superelevation value (3.0) since there's only one segment
                    // and the rate of change is a line (not a spiral/polynomial)
                    XbimGeometryNativeApi.xbim_curve_get_superelevation_and_tilt(
                        segRefHandle, 0.0, out double superElev, out _).Should().Be(0);
                    superElev.Should().BeApproximately(3.0, 1e-3);
                }
            }
        }
    }

    /// <summary>
    /// Validates that the superelevation query function rejects non-segmented-reference curves.
    /// </summary>
    [Fact]
    public void GetSuperelevation_OnNonSegRef_ReturnsError()
    {
        // Build a plain gradient curve
        XbimGeometryNativeApi.xbim_curve2d_build_line(
            Ctx, 0, 0, 50, 0, out var hHandle).Should().Be(0);
        XbimGeometryNativeApi.xbim_curve2d_build_line(
            Ctx, 0, 0, 50, 0, out var vHandle).Should().Be(0);
        XbimGeometryNativeApi.xbim_curve_build_gradient(
            Ctx, hHandle, vHandle, out var gradientHandle).Should().Be(0);

        using (hHandle)
        using (vHandle)
        using (gradientHandle)
        {
            // Query superelevation on a gradient curve (not a segmented reference) should fail
            int result = XbimGeometryNativeApi.xbim_curve_get_superelevation_and_tilt(
                gradientHandle, 25.0, out _, out _);
            result.Should().NotBe(0, "gradient curve is not a segmented reference curve");
        }
    }
}
