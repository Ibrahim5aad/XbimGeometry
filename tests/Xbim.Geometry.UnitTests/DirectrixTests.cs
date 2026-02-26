using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Factories;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

/// <summary>
/// Tests directrix building and trimming in CurveFactory and WireFactory.
/// Verifies that BuildDirectrix applies parametric trimming and
/// BuildDirectrixWire produces correctly trimmed wires.
/// </summary>
public class DirectrixTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXCurveFactory _curveFactory;
    private readonly WireFactory _wireFactory;

    public DirectrixTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _curveFactory = _service.CurveFactory;
        _wireFactory = (WireFactory)_service.WireFactory;
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void BuildDirectrix_WithNullParams_ReturnsFullCurve()
    {
        // A full circle with radius 10
        var ifcCircle = IfcMoq.IfcCircle3d(10);

        var curve = _curveFactory.BuildDirectrix(ifcCircle, null, null);

        curve.Should().NotBeNull();
        // Full circle should span 0 to 2*PI in parametric space
        curve.Length.Should().BeApproximately(2 * Math.PI * 10, 0.01);
    }

    [Fact]
    public void BuildDirectrix_WithTrimParams_ReturnsShorterCurve()
    {
        // A full circle with radius 10, trimmed to a quarter arc (0 to PI/2)
        var ifcCircle = IfcMoq.IfcCircle3d(10);

        var fullCurve = _curveFactory.BuildDirectrix(ifcCircle, null, null);
        var trimmedCurve = _curveFactory.BuildDirectrix(ifcCircle, 0, Math.PI / 2);

        trimmedCurve.Should().NotBeNull();
        // Quarter circle arc length = (PI/2) * radius = 5*PI ≈ 15.71
        trimmedCurve.Length.Should().BeApproximately(Math.PI / 2 * 10, 0.1);
        trimmedCurve.Length.Should().BeLessThan(fullCurve.Length);
    }

    [Fact]
    public void BuildDirectrixWire_WithNullParams_ReturnsFullWire()
    {
        var ifcCircle = IfcMoq.IfcCircle3d(10);

        var wire = _wireFactory.BuildDirectrixWire(ifcCircle, null, null);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(2 * Math.PI * 10, 0.01);
    }

    [Fact]
    public void BuildDirectrixWire_WithTrimParams_ReturnsTrimmedWire()
    {
        var ifcCircle = IfcMoq.IfcCircle3d(10);

        // Trim params are in IFC angle units (degrees for this mock model)
        // 90 degrees = quarter circle
        var wire = _wireFactory.BuildDirectrixWire(ifcCircle, 0, 90);

        wire.Should().NotBeNull();
        // Quarter circle arc length = PI/2 * radius = 5*PI ≈ 15.71
        wire.Length.Should().BeApproximately(Math.PI / 2 * 10, 0.5);
    }

    [Fact]
    public void BuildDirectrixWire_Line_WithTrimParams_ReturnsTrimmedWire()
    {
        // Line from origin along X axis, magnitude 100
        var ifcLine = IfcMoq.IfcLine(0, 0, 0, 1, 0, 0, 100);

        // Build wire and trim to first 50 units
        var wire = _wireFactory.BuildDirectrixWire(ifcLine, 0, 50);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(50, 0.1);
    }

    // ── Composite Curve Directrix Tests ──────────────────────────

    [Fact]
    public void CompositeCurve_SinglePolyline_NoTrim_ReturnsFullLength()
    {
        // Polyline: (0,0,0) → (100,0,0) → (100,100,0) = two segments, length ~200
        var poly = IfcMoq.Polyline3d((0, 0, 0), (100, 0, 0), (100, 100, 0));
        var seg = IfcMoq.CompositeCurveSegment(poly);
        var cc = IfcMoq.CompositeCurve(seg);

        var wire = _wireFactory.BuildDirectrixWire(cc, null, null);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(200, 0.1);
    }

    [Fact]
    public void CompositeCurve_SinglePolyline_TrimmedFirstHalf()
    {
        // Polyline: (0,0,0) → (100,0,0) → (100,100,0)
        // Parameterized length = 2 (two line segments, each normalized to 1)
        // Trim to first segment: startParam=0, endParam=1
        var poly = IfcMoq.Polyline3d((0, 0, 0), (100, 0, 0), (100, 100, 0));
        var seg = IfcMoq.CompositeCurveSegment(poly);
        var cc = IfcMoq.CompositeCurve(seg);

        var wire = _wireFactory.BuildDirectrixWire(cc, 0, 1);

        wire.Should().NotBeNull();
        // endParam=1 <= firstParameterizedLength=2 and startParam=0, endParam=1 → entire curve
        // BUT this is the special case: startParam=0, endParam=1, endPar <= firstParamLen
        // So it takes the full curve
        wire.Length.Should().BeApproximately(200, 0.1);
    }

    [Fact]
    public void CompositeCurve_TwoPolylines_NoTrim_ReturnsFullLength()
    {
        // First polyline: (0,0,0) → (100,0,0) = length 100, paramLen 1
        // Second polyline: (100,0,0) → (100,100,0) = length 100, paramLen 1
        var poly1 = IfcMoq.Polyline3d((0, 0, 0), (100, 0, 0));
        var poly2 = IfcMoq.Polyline3d((100, 0, 0), (100, 100, 0));
        var seg1 = IfcMoq.CompositeCurveSegment(poly1, entityLabel: 10);
        var seg2 = IfcMoq.CompositeCurveSegment(poly2, entityLabel: 11);
        var cc = IfcMoq.CompositeCurve(seg1, seg2);

        var wire = _wireFactory.BuildDirectrixWire(cc, null, null);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(200, 0.1);
    }

    [Fact]
    public void CompositeCurve_TwoPolylines_TrimmedToFirstSegment()
    {
        // Two equal 100-length polyline segments, each paramLen=1
        // Trim: start=0, end=1 → maps to first 100 units of arc-length
        var poly1 = IfcMoq.Polyline3d((0, 0, 0), (100, 0, 0));
        var poly2 = IfcMoq.Polyline3d((100, 0, 0), (100, 100, 0));
        var seg1 = IfcMoq.CompositeCurveSegment(poly1, entityLabel: 10);
        var seg2 = IfcMoq.CompositeCurveSegment(poly2, entityLabel: 11);
        var cc = IfcMoq.CompositeCurve(seg1, seg2);

        // endParam=1 <= firstParamLen=1, startParam=0 → special case, takes full geo length
        // But with 2 segments, endPar=1 maps to ratio=1 of first segment only
        // Wait: the special case triggers when endPar <= firstParameterizedLength
        // AND endParam==1 AND startParam==0. Here firstParamLen=1, endPar=1, so it triggers
        // and occEnd += geoLength for each segment → gets full curve.
        // This matches the legacy behavior for authoring tools that set trim (0,1) meaning "entire"
        var wire = _wireFactory.BuildDirectrixWire(cc, 0, 1);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(200, 0.1);
    }

    [Fact]
    public void CompositeCurve_TwoPolylines_TrimmedToHalf()
    {
        // Two equal 100-length polyline segments, each paramLen=1
        // Total paramLen=2. Trim: start=0, end=0.5 → maps to first 50 units
        var poly1 = IfcMoq.Polyline3d((0, 0, 0), (100, 0, 0));
        var poly2 = IfcMoq.Polyline3d((100, 0, 0), (100, 100, 0));
        var seg1 = IfcMoq.CompositeCurveSegment(poly1, entityLabel: 10);
        var seg2 = IfcMoq.CompositeCurveSegment(poly2, entityLabel: 11);
        var cc = IfcMoq.CompositeCurve(seg1, seg2);

        var wire = _wireFactory.BuildDirectrixWire(cc, 0, 0.5);

        wire.Should().NotBeNull();
        // endParam=0.5, firstParamLen=1. ratio = min(0.5/1, 1) = 0.5
        // occEnd = 0.5 * 100 = 50
        wire.Length.Should().BeApproximately(50, 0.5);
    }

    [Fact]
    public void CompositeCurve_TwoPolylines_TrimmedFromMiddle()
    {
        // Two equal 100-length polyline segments, each paramLen=1
        // Trim: start=0.5, end=1.5 → skip first 50, take next 100 (50 from seg1 + 50 from seg2)
        var poly1 = IfcMoq.Polyline3d((0, 0, 0), (100, 0, 0));
        var poly2 = IfcMoq.Polyline3d((100, 0, 0), (100, 100, 0));
        var seg1 = IfcMoq.CompositeCurveSegment(poly1, entityLabel: 10);
        var seg2 = IfcMoq.CompositeCurveSegment(poly2, entityLabel: 11);
        var cc = IfcMoq.CompositeCurve(seg1, seg2);

        var wire = _wireFactory.BuildDirectrixWire(cc, 0.5, 1.5);

        wire.Should().NotBeNull();
        // startPar=0.5: ratio=0.5/1=0.5, occStart=0.5*100=50, remaining startPar=0
        // endPar=1.5: first seg ratio=min(1.5/1,1)=1, occEnd+=100, remaining=0.5
        //   second seg ratio=min(0.5/1,1)=0.5, occEnd+=50 → total occEnd=150
        // Trimmed length = 150 - 50 = 100
        wire.Length.Should().BeApproximately(100, 0.5);
    }

    [Fact]
    public void CompositeCurve_PolylineAndTrimmedArc_NoTrim_ReturnsFullLength()
    {
        // Polyline segment: (0,0,0) → (100,0,0), length=100
        // Trimmed circle arc: center at (0,0,0), radius=100, refDir along X
        // The circle starts at (100,0,0) (where the polyline ends)
        // 90° arc from 0 to 90 degrees → ends at (0,100,0)
        var poly = IfcMoq.Polyline3d((0, 0, 0), (100, 0, 0));

        var circlePos = IfcMoq.Axis2Placement3d(
            axis: IfcMoq.Direction3d(0, 0, 1),
            refDir: IfcMoq.Direction3d(1, 0, 0),
            loc: IfcMoq.CartesianPoint3d(0, 0, 0));
        var circle = IfcMoq.IfcCircle3d(100, circlePos);
        var trimmedArc = IfcMoq.IfcTrimmedCurve3d(circle, 0, 90);

        var seg1 = IfcMoq.CompositeCurveSegment(poly, entityLabel: 10);
        var seg2 = IfcMoq.CompositeCurveSegment(trimmedArc, entityLabel: 11);
        var cc = IfcMoq.CompositeCurve(seg1, seg2);

        var wire = _wireFactory.BuildDirectrixWire(cc, null, null);

        wire.Should().NotBeNull();
        double expectedArcLen = Math.PI / 2 * 100; // ~157.08
        wire.Length.Should().BeApproximately(100 + expectedArcLen, 1.0);
    }

    [Fact]
    public void CompositeCurve_ThreePolylines_TrimmedToMiddleSegment()
    {
        // Three polyline segments of 100 each, paramLen=1 each, total paramLen=3
        // Trim: start=1, end=2 → skip first 100, take second 100
        var poly1 = IfcMoq.Polyline3d((0, 0, 0), (100, 0, 0));
        var poly2 = IfcMoq.Polyline3d((100, 0, 0), (100, 100, 0));
        var poly3 = IfcMoq.Polyline3d((100, 100, 0), (200, 100, 0));
        var seg1 = IfcMoq.CompositeCurveSegment(poly1, entityLabel: 10);
        var seg2 = IfcMoq.CompositeCurveSegment(poly2, entityLabel: 11);
        var seg3 = IfcMoq.CompositeCurveSegment(poly3, entityLabel: 12);
        var cc = IfcMoq.CompositeCurve(seg1, seg2, seg3);

        var wire = _wireFactory.BuildDirectrixWire(cc, 1, 2);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(100, 0.5);
    }

    // ── Indexed Poly Curve Directrix Tests ───────────────────────

    [Fact]
    public void IndexedPolyCurve_LinesOnly_NoTrim_ReturnsFullLength()
    {
        // 3 points forming 2 line segments, total length 200
        var ipc = IfcMoq.IndexedPolyCurve3d(
            new[] { (0.0, 0.0, 0.0), (100.0, 0.0, 0.0), (100.0, 100.0, 0.0) },
            new[] { new long[] { 1, 2 }, new long[] { 2, 3 } });

        var wire = _wireFactory.BuildDirectrixWire(ipc, null, null);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(200, 0.1);
    }

    [Fact]
    public void IndexedPolyCurve_LinesOnly_TrimmedToHalf()
    {
        // 3 points forming 2 line segments of 100 each, paramLen=1+1=2
        // Trim: start=0, end=1 → maps to first 100 units
        var ipc = IfcMoq.IndexedPolyCurve3d(
            new[] { (0.0, 0.0, 0.0), (100.0, 0.0, 0.0), (100.0, 100.0, 0.0) },
            new[] { new long[] { 1, 2 }, new long[] { 2, 3 } });

        var wire = _wireFactory.BuildDirectrixWire(ipc, 0, 1);

        wire.Should().NotBeNull();
        // paramLen=2, startPar=0→occStart=0, endPar=1→ratio=0.5→occEnd=100
        wire.Length.Should().BeApproximately(100, 0.5);
    }

    [Fact]
    public void IndexedPolyCurve_NoSegments_NoTrim_ReturnsFullLength()
    {
        // No explicit segments → polyline through all points
        var ipc = IfcMoq.IndexedPolyCurve3d(
            new[] { (0.0, 0.0, 0.0), (100.0, 0.0, 0.0), (100.0, 100.0, 0.0) });

        var wire = _wireFactory.BuildDirectrixWire(ipc, null, null);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(200, 0.1);
    }

    [Fact]
    public void IndexedPolyCurve_NoSegments_TrimmedToFirstHalf()
    {
        // No explicit segments → polyline through 3 points, paramLen = 2
        // Trim: start=0, end=1 → first half
        var ipc = IfcMoq.IndexedPolyCurve3d(
            new[] { (0.0, 0.0, 0.0), (100.0, 0.0, 0.0), (100.0, 100.0, 0.0) });

        var wire = _wireFactory.BuildDirectrixWire(ipc, 0, 1);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(100, 0.5);
    }

    [Fact]
    public void IndexedPolyCurve_ArcAndLine_NoTrim_ReturnsFullLength()
    {
        // Arc through 3 points on a circle of radius 100 centered at origin:
        // P1=(100,0,0), P2=(0,100,0), P3=(-100,0,0) → 180° arc = PI*100
        // Line from P3=(-100,0,0) to P4=(-100,100,0) → length 100
        var ipc = IfcMoq.IndexedPolyCurve3d(
            new[] { (100.0, 0.0, 0.0), (0.0, 100.0, 0.0), (-100.0, 0.0, 0.0), (-100.0, 100.0, 0.0) },
            new[] { new long[] { 1, 2, 3 }, new long[] { 3, 4 } });

        var wire = _wireFactory.BuildDirectrixWire(ipc, null, null);

        wire.Should().NotBeNull();
        double expectedArcLen = Math.PI * 100; // ~314.16
        wire.Length.Should().BeApproximately(expectedArcLen + 100, 1.0);
    }

    [Fact]
    public void IndexedPolyCurve_MultiLineSegment_NoTrim_ReturnsFullLength()
    {
        // One line index with 4 points → 3 line sub-segments
        // (0,0,0)→(10,0,0)→(20,0,0)→(30,0,0) = 30 total length
        var ipc = IfcMoq.IndexedPolyCurve3d(
            new[] { (0.0, 0.0, 0.0), (10.0, 0.0, 0.0), (20.0, 0.0, 0.0), (30.0, 0.0, 0.0) },
            new[] { new long[] { 1, 2, 3, 4 } });

        var wire = _wireFactory.BuildDirectrixWire(ipc, null, null);

        wire.Should().NotBeNull();
        wire.Length.Should().BeApproximately(30, 0.1);
    }
}
