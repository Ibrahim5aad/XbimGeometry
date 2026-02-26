using FluentAssertions;
using Moq;
using Xbim.Common;
using Xbim.Geometry.Engine.Rules;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

public class CurveRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static Mock<T> MakeMoq<T>() where T : class, IPersistEntity
    {
        return new Mock<T>
        {
            DefaultValue = DefaultValue.Mock,
            DefaultValueProvider = new MoqDefaultBehaviourProvider()
        }.SetupAllProperties();
    }

    private static IIfcTrimmedCurve TrimmedCurveWith(
        IIfcCurve basisCurve,
        double? param1 = null,
        double? param2 = null,
        int label = 50)
    {
        var moq = MakeMoq<IIfcTrimmedCurve>();
        moq.SetupGet(c => c.BasisCurve).Returns(basisCurve);
        moq.SetupGet(c => c.EntityLabel).Returns(label);
        moq.SetupGet(c => c.SenseAgreement).Returns(true);
        moq.SetupGet(c => c.MasterRepresentation).Returns(IfcTrimmingPreference.PARAMETER);

        var obj = moq.Object;

        if (param1.HasValue)
            obj.Trim1.Add(new IfcParameterValue(param1.Value));
        if (param2.HasValue)
            obj.Trim2.Add(new IfcParameterValue(param2.Value));

        return obj;
    }

    private static IIfcCurve UnboundedCurve()
    {
        var moq = MakeMoq<IIfcLine>();
        moq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(3));
        moq.SetupGet(c => c.EntityLabel).Returns(100);
        return moq.Object;
    }

    private static IIfcBoundedCurve BoundedCurve()
    {
        var moq = MakeMoq<IIfcPolyline>();
        moq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(2));
        moq.SetupGet(c => c.EntityLabel).Returns(101);
        return moq.Object;
    }

    private static IIfcCompositeCurve CompositeCurveWithSegments(params IIfcCompositeCurveSegment[] segments)
    {
        var moq = MakeMoq<IIfcCompositeCurve>();
        moq.SetupGet(c => c.EntityLabel).Returns(200);
        var obj = moq.Object;
        foreach (var s in segments)
            obj.Segments.Add(s);
        return obj;
    }

    private static IIfcCompositeCurveSegment SegmentWith(IIfcCurve parentCurve, int label = 300)
    {
        var moq = MakeMoq<IIfcCompositeCurveSegment>();
        moq.SetupGet(s => s.ParentCurve).Returns(parentCurve);
        moq.SetupGet(s => s.SameSense).Returns(true);
        moq.SetupGet(s => s.EntityLabel).Returns(label);
        return moq.Object;
    }

    // ── IIfcTrimmedCurve WR41: trim values not equal ────────────────

    [Fact]
    public void WR41_TrimmedCurve_DifferentParams_Passes()
    {
        var curve = TrimmedCurveWith(UnboundedCurve(), param1: 0.0, param2: 1.0);

        var act = () => CurveRules.WR41_TrimValuesNotEqual(curve);

        act.Should().NotThrow();
    }

    [Fact]
    public void WR41_TrimmedCurve_EqualParams_Throws()
    {
        var curve = TrimmedCurveWith(UnboundedCurve(), param1: 5.0, param2: 5.0, label: 55);

        var act = () => CurveRules.WR41_TrimValuesNotEqual(curve);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR41" && ex.EntityLabel == 55);
    }

    [Fact]
    public void WR41_TrimmedCurve_NoParams_Passes()
    {
        // Cartesian-only trims — no parametric values to compare structurally
        var curve = TrimmedCurveWith(UnboundedCurve());

        var act = () => CurveRules.WR41_TrimValuesNotEqual(curve);

        act.Should().NotThrow();
    }

    // ── IIfcTrimmedCurve WR42: no trim of bounded curves ───────────

    [Fact]
    public void WR42_TrimmedCurve_UnboundedBasis_Passes()
    {
        var curve = TrimmedCurveWith(UnboundedCurve(), param1: 0.0, param2: 1.0);

        var act = () => CurveRules.WR42_NoTrimOfBoundedCurves(curve);

        act.Should().NotThrow();
    }

    [Fact]
    public void WR42_TrimmedCurve_BoundedBasis_Throws()
    {
        var curve = TrimmedCurveWith(BoundedCurve(), param1: 0.0, param2: 1.0, label: 66);

        var act = () => CurveRules.WR42_NoTrimOfBoundedCurves(curve);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR42" && ex.EntityLabel == 66);
    }

    // ── IIfcTrimmedCurve Validate (all WRs) ─────────────────────────

    [Fact]
    public void Validate_TrimmedCurve_Valid_Passes()
    {
        var curve = TrimmedCurveWith(UnboundedCurve(), param1: 0.0, param2: 1.0);

        var act = () => CurveRules.Validate(curve);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_TrimmedCurve_EqualParamsOnBounded_ThrowsWR41()
    {
        // WR41 fires before WR42 because it's checked first
        var curve = TrimmedCurveWith(BoundedCurve(), param1: 3.0, param2: 3.0);

        var act = () => CurveRules.Validate(curve);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR41");
    }

    // ── IIfcCompositeCurve WR41: segments must be bounded ───────────

    [Fact]
    public void WR41_CompositeCurve_AllBounded_Passes()
    {
        var composite = CompositeCurveWithSegments(
            SegmentWith(BoundedCurve(), label: 301),
            SegmentWith(BoundedCurve(), label: 302));

        var act = () => CurveRules.WR41_SegmentsMustBeBounded(composite);

        act.Should().NotThrow();
    }

    [Fact]
    public void WR41_CompositeCurve_UnboundedSegment_Throws()
    {
        var composite = CompositeCurveWithSegments(
            SegmentWith(BoundedCurve(), label: 301),
            SegmentWith(UnboundedCurve(), label: 302));

        var act = () => CurveRules.WR41_SegmentsMustBeBounded(composite);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR41" && ex.EntityLabel == 200);
    }

    [Fact]
    public void WR41_CompositeCurve_NullParentCurve_Skipped()
    {
        var segMoq = MakeMoq<IIfcCompositeCurveSegment>();
        segMoq.SetupGet(s => s.ParentCurve).Returns((IIfcCurve)null!);
        segMoq.SetupGet(s => s.EntityLabel).Returns(303);

        var composite = CompositeCurveWithSegments(segMoq.Object);

        var act = () => CurveRules.WR41_SegmentsMustBeBounded(composite);

        act.Should().NotThrow();
    }

    // ── IIfcCompositeCurve Validate (all WRs) ───────────────────────

    [Fact]
    public void Validate_CompositeCurve_Valid_Passes()
    {
        var composite = CompositeCurveWithSegments(
            SegmentWith(BoundedCurve()));

        var act = () => CurveRules.Validate(composite);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_CompositeCurve_UnboundedSegment_ThrowsWR41()
    {
        var composite = CompositeCurveWithSegments(
            SegmentWith(UnboundedCurve()));

        var act = () => CurveRules.Validate(composite);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR41");
    }

    // ── IsBoundedCurve helper ───────────────────────────────────────

    [Fact]
    public void IsBoundedCurve_Line_ReturnsFalse()
    {
        CurveRules.IsBoundedCurve(UnboundedCurve()).Should().BeFalse();
    }

    [Fact]
    public void IsBoundedCurve_Polyline_ReturnsTrue()
    {
        CurveRules.IsBoundedCurve(BoundedCurve()).Should().BeTrue();
    }

    [Fact]
    public void IsBoundedCurve_OffsetCurve3D_WithLineBasis_ReturnsFalse()
    {
        var lineMoq = MakeMoq<IIfcLine>();
        lineMoq.SetupGet(c => c.EntityLabel).Returns(400);

        var offsetMoq = MakeMoq<IIfcOffsetCurve3D>();
        offsetMoq.SetupGet(c => c.BasisCurve).Returns(lineMoq.Object);
        offsetMoq.SetupGet(c => c.EntityLabel).Returns(401);

        CurveRules.IsBoundedCurve(offsetMoq.Object).Should().BeFalse();
    }

    [Fact]
    public void IsBoundedCurve_OffsetCurve2D_WithPolylineBasis_ReturnsTrue()
    {
        var polyMoq = MakeMoq<IIfcPolyline>();
        polyMoq.SetupGet(c => c.EntityLabel).Returns(402);

        var offsetMoq = MakeMoq<IIfcOffsetCurve2D>();
        offsetMoq.SetupGet(c => c.BasisCurve).Returns(polyMoq.Object);
        offsetMoq.SetupGet(c => c.EntityLabel).Returns(403);

        CurveRules.IsBoundedCurve(offsetMoq.Object).Should().BeTrue();
    }
}
