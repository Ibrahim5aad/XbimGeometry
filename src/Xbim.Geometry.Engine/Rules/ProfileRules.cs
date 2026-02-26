using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Rules
{
    /// <summary>
    /// IFC Where Rule validators for profile definition entities.
    /// Each method enforces a single formal rule from the IFC specification.
    /// </summary>
    internal static class ProfileRules
    {
        // ── IIfcArbitraryClosedProfileDef (IFC4 §8.15.3.1) ─────────────

        /// <summary>
        /// WR1: The dimensionality of the outer curve shall be 2.
        /// </summary>
        public static void WR1_OuterCurveMustBe2D(IIfcArbitraryClosedProfileDef profile)
        {
            var outerCurve = profile.OuterCurve;
            if (outerCurve != null && (int)outerCurve.Dim != 2)
                throw new IfcRuleViolationException("WR1", profile.EntityLabel,
                    "IfcArbitraryClosedProfileDef", "OuterCurve.Dim must be 2.");
        }

        /// <summary>
        /// WR2: The outer curve shall not be of type IfcLine (a line is not a closed curve).
        /// </summary>
        public static void WR2_OuterCurveNotLine(IIfcArbitraryClosedProfileDef profile)
        {
            if (profile.OuterCurve is IIfcLine)
                throw new IfcRuleViolationException("WR2", profile.EntityLabel,
                    "IfcArbitraryClosedProfileDef", "OuterCurve shall not be IfcLine.");
        }

        /// <summary>
        /// WR3: The outer curve shall not be of type IfcOffsetCurve2D.
        /// </summary>
        public static void WR3_OuterCurveNotOffsetCurve2D(IIfcArbitraryClosedProfileDef profile)
        {
            if (profile.OuterCurve is IIfcOffsetCurve2D)
                throw new IfcRuleViolationException("WR3", profile.EntityLabel,
                    "IfcArbitraryClosedProfileDef", "OuterCurve shall not be IfcOffsetCurve2D.");
        }

        /// <summary>
        /// Validates all Where Rules for an arbitrary closed profile definition.
        /// </summary>
        public static void Validate(IIfcArbitraryClosedProfileDef profile)
        {
            WR1_OuterCurveMustBe2D(profile);
            WR2_OuterCurveNotLine(profile);
            WR3_OuterCurveNotOffsetCurve2D(profile);
        }

        // ── IIfcArbitraryOpenProfileDef (IFC4 §8.15.3.2) ───────────────

        /// <summary>
        /// WR11: The dimensionality of the curve shall be 2.
        /// </summary>
        public static void WR11_CurveMustBe2D(IIfcArbitraryOpenProfileDef profile)
        {
            var curve = profile.Curve;
            if (curve != null && (int)curve.Dim != 2)
                throw new IfcRuleViolationException("WR11", profile.EntityLabel,
                    "IfcArbitraryOpenProfileDef", "Curve.Dim must be 2.");
        }

        /// <summary>
        /// WR12: The profile type shall be CURVE.
        /// </summary>
        public static void WR12_ProfileTypeMustBeCurve(IIfcArbitraryOpenProfileDef profile)
        {
            if (profile.ProfileType != IfcProfileTypeEnum.CURVE)
                throw new IfcRuleViolationException("WR12", profile.EntityLabel,
                    "IfcArbitraryOpenProfileDef", "ProfileType must be CURVE.");
        }

        /// <summary>
        /// Validates all Where Rules for an arbitrary open profile definition.
        /// </summary>
        public static void Validate(IIfcArbitraryOpenProfileDef profile)
        {
            WR11_CurveMustBe2D(profile);
            WR12_ProfileTypeMustBeCurve(profile);
        }
    }
}
