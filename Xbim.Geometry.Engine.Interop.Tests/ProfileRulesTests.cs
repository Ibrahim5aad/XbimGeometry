using FluentAssertions;
using Moq;
using Xbim.Common;
using Xbim.Geometry.Engine.Interop.Rules;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.GeometryResource;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class ProfileRulesTests
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

    private static IIfcArbitraryClosedProfileDef ClosedProfileWithCurve(IIfcCurve outerCurve, int label = 42)
    {
        var moq = MakeMoq<IIfcArbitraryClosedProfileDef>();
        moq.SetupGet(p => p.OuterCurve).Returns(outerCurve);
        moq.SetupGet(p => p.EntityLabel).Returns(label);
        return moq.Object;
    }

    private static IIfcArbitraryOpenProfileDef OpenProfileWithCurve(
        IIfcBoundedCurve curve, IfcProfileTypeEnum profileType = IfcProfileTypeEnum.CURVE, int label = 99)
    {
        var moq = MakeMoq<IIfcArbitraryOpenProfileDef>();
        moq.SetupGet(p => p.Curve).Returns(curve);
        moq.SetupGet(p => p.ProfileType).Returns(profileType);
        moq.SetupGet(p => p.EntityLabel).Returns(label);
        return moq.Object;
    }

    private static IIfcCurve CurveWithDim(int dim)
    {
        var moq = MakeMoq<IIfcPolyline>();
        moq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(dim));
        return moq.Object;
    }

    private static IIfcBoundedCurve BoundedCurveWithDim(int dim)
    {
        var moq = MakeMoq<IIfcPolyline>();
        moq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(dim));
        return moq.Object;
    }

    private static IIfcLine LineWithDim(int dim)
    {
        var moq = MakeMoq<IIfcLine>();
        moq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(dim));
        return moq.Object;
    }

    private static IIfcOffsetCurve2D OffsetCurve2dWithDim(int dim)
    {
        var moq = MakeMoq<IIfcOffsetCurve2D>();
        moq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(dim));
        return moq.Object;
    }

    // ── IIfcArbitraryClosedProfileDef WR1 ───────────────────────────

    [Fact]
    public void WR1_ClosedProfile_2dCurve_Passes()
    {
        var profile = ClosedProfileWithCurve(CurveWithDim(2));

        var act = () => ProfileRules.WR1_OuterCurveMustBe2D(profile);

        act.Should().NotThrow();
    }

    [Fact]
    public void WR1_ClosedProfile_3dCurve_Throws()
    {
        var profile = ClosedProfileWithCurve(CurveWithDim(3));

        var act = () => ProfileRules.WR1_OuterCurveMustBe2D(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR1" && ex.EntityLabel == 42);
    }

    // ── IIfcArbitraryClosedProfileDef WR2 ───────────────────────────

    [Fact]
    public void WR2_ClosedProfile_NonLineCurve_Passes()
    {
        var profile = ClosedProfileWithCurve(CurveWithDim(2));

        var act = () => ProfileRules.WR2_OuterCurveNotLine(profile);

        act.Should().NotThrow();
    }

    [Fact]
    public void WR2_ClosedProfile_LineCurve_Throws()
    {
        var profile = ClosedProfileWithCurve(LineWithDim(2));

        var act = () => ProfileRules.WR2_OuterCurveNotLine(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR2" && ex.EntityLabel == 42);
    }

    // ── IIfcArbitraryClosedProfileDef WR3 ───────────────────────────

    [Fact]
    public void WR3_ClosedProfile_NonOffsetCurve_Passes()
    {
        var profile = ClosedProfileWithCurve(CurveWithDim(2));

        var act = () => ProfileRules.WR3_OuterCurveNotOffsetCurve2D(profile);

        act.Should().NotThrow();
    }

    [Fact]
    public void WR3_ClosedProfile_OffsetCurve2D_Throws()
    {
        var profile = ClosedProfileWithCurve(OffsetCurve2dWithDim(2));

        var act = () => ProfileRules.WR3_OuterCurveNotOffsetCurve2D(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR3" && ex.EntityLabel == 42);
    }

    // ── IIfcArbitraryClosedProfileDef Validate (all WRs) ────────────

    [Fact]
    public void Validate_ClosedProfile_Valid_Passes()
    {
        var profile = ClosedProfileWithCurve(CurveWithDim(2));

        var act = () => ProfileRules.Validate(profile);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ClosedProfile_3dLine_ThrowsWR1()
    {
        // WR1 fires before WR2 because the line is 3D
        var profile = ClosedProfileWithCurve(LineWithDim(3));

        var act = () => ProfileRules.Validate(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR1");
    }

    // ── IIfcArbitraryOpenProfileDef WR11 ────────────────────────────

    [Fact]
    public void WR11_OpenProfile_2dCurve_Passes()
    {
        var profile = OpenProfileWithCurve(BoundedCurveWithDim(2));

        var act = () => ProfileRules.WR11_CurveMustBe2D(profile);

        act.Should().NotThrow();
    }

    [Fact]
    public void WR11_OpenProfile_3dCurve_Throws()
    {
        var profile = OpenProfileWithCurve(BoundedCurveWithDim(3), label: 77);

        var act = () => ProfileRules.WR11_CurveMustBe2D(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR11" && ex.EntityLabel == 77);
    }

    // ── IIfcArbitraryOpenProfileDef WR12 ────────────────────────────

    [Fact]
    public void WR12_OpenProfile_CurveType_Passes()
    {
        var profile = OpenProfileWithCurve(BoundedCurveWithDim(2), IfcProfileTypeEnum.CURVE);

        var act = () => ProfileRules.WR12_ProfileTypeMustBeCurve(profile);

        act.Should().NotThrow();
    }

    [Fact]
    public void WR12_OpenProfile_AreaType_Throws()
    {
        var profile = OpenProfileWithCurve(BoundedCurveWithDim(2), IfcProfileTypeEnum.AREA, label: 88);

        var act = () => ProfileRules.WR12_ProfileTypeMustBeCurve(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR12" && ex.EntityLabel == 88);
    }

    // ── IIfcArbitraryOpenProfileDef Validate (all WRs) ──────────────

    [Fact]
    public void Validate_OpenProfile_Valid_Passes()
    {
        var profile = OpenProfileWithCurve(BoundedCurveWithDim(2));

        var act = () => ProfileRules.Validate(profile);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_OpenProfile_3dCurveAndAreaType_ThrowsWR11()
    {
        // WR11 fires first since it's checked before WR12
        var profile = OpenProfileWithCurve(BoundedCurveWithDim(3), IfcProfileTypeEnum.AREA);

        var act = () => ProfileRules.Validate(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR11");
    }
}
