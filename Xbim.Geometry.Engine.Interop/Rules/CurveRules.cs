using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Rules
{
    /// <summary>
    /// IFC Where Rule validators for curve entities.
    /// Each method enforces a single formal rule from the IFC specification.
    /// </summary>
    internal static class CurveRules
    {
        // ── IIfcTrimmedCurve (IFC4 §8.9.3.44) ──────────────────────────

        /// <summary>
        /// WR41: The first and second set of trim values shall not be equal.
        /// Both Trim1 and Trim2 are sets of IfcTrimmingSelect; this rule checks that
        /// they are not identical (i.e. the curve would have zero length).
        /// </summary>
        /// <remarks>
        /// In practice, trim equality is checked after parameter resolution (cartesian projection
        /// or parametric extraction). This rule performs a structural check on the raw trim selects —
        /// if both contain exactly one IfcParameterValue and those values are equal, the rule fires.
        /// Cartesian-only trims require geometric evaluation and are deferred to the factory.
        /// </remarks>
        public static void WR41_TrimValuesNotEqual(IIfcTrimmedCurve curve)
        {
            // Extract parametric values from Trim1 and Trim2
            double? param1 = null;
            double? param2 = null;

            foreach (var trim in curve.Trim1)
            {
                if (trim is Xbim.Ifc4.MeasureResource.IfcParameterValue pv)
                    param1 = (double)pv;
            }

            foreach (var trim in curve.Trim2)
            {
                if (trim is Xbim.Ifc4.MeasureResource.IfcParameterValue pv)
                    param2 = (double)pv;
            }

            // Only fire if both trims provide parametric values and they are equal
            if (param1.HasValue && param2.HasValue && param1.Value == param2.Value)
                throw new IfcRuleViolationException("WR41", curve.EntityLabel,
                    "IfcTrimmedCurve", "Trim1 and Trim2 parameter values shall not be equal.");
        }

        /// <summary>
        /// WR42: If the basis curve is already a bounded curve, trimming should be by parameter only
        /// (NoTrimOfBoundedCurves). An already-bounded curve should not be further trimmed
        /// by cartesian points, as this can produce ambiguous results.
        /// </summary>
        public static void WR42_NoTrimOfBoundedCurves(IIfcTrimmedCurve curve)
        {
            if (curve.BasisCurve is IIfcBoundedCurve)
                throw new IfcRuleViolationException("WR42", curve.EntityLabel,
                    "IfcTrimmedCurve", "BasisCurve is already bounded; bounded curves shall not be trimmed.");
        }

        /// <summary>
        /// Validates all Where Rules for a trimmed curve.
        /// </summary>
        public static void Validate(IIfcTrimmedCurve curve)
        {
            WR41_TrimValuesNotEqual(curve);
            WR42_NoTrimOfBoundedCurves(curve);
        }

        // ── IIfcCompositeCurve (IFC4 §8.9.3.10) ────────────────────────

        /// <summary>
        /// WR41: Every segment in the composite curve shall reference a bounded curve
        /// as its parent curve. Unbounded curves (lines, pcurves, surface curves) are not
        /// valid composite curve segments.
        /// </summary>
        public static void WR41_SegmentsMustBeBounded(IIfcCompositeCurve curve)
        {
            foreach (var segment in curve.Segments)
            {
                if (segment.ParentCurve == null)
                    continue;

                if (!IsBoundedCurve(segment.ParentCurve))
                    throw new IfcRuleViolationException("WR41", curve.EntityLabel,
                        "IfcCompositeCurve",
                        $"Segment #{segment.EntityLabel} references an unbounded curve ({segment.ParentCurve.GetType().Name}).");
            }
        }

        /// <summary>
        /// Validates all Where Rules for a composite curve.
        /// </summary>
        public static void Validate(IIfcCompositeCurve curve)
        {
            WR41_SegmentsMustBeBounded(curve);
        }

        // ── Shared helpers ──────────────────────────────────────────────

        /// <summary>
        /// Returns whether an IFC curve is bounded. Unbounded curves (lines, pcurves,
        /// surface curves) and offset curves with unbounded basis are not valid
        /// as composite curve segments.
        /// </summary>
        internal static bool IsBoundedCurve(IIfcCurve curve)
        {
            if (curve is IIfcLine) return false;
            if (curve is IIfcOffsetCurve3D oc3d) return IsBoundedCurve(oc3d.BasisCurve);
            if (curve is IIfcOffsetCurve2D oc2d) return IsBoundedCurve(oc2d.BasisCurve);
            if (curve is IIfcPcurve) return false;
            if (curve is IIfcSurfaceCurve) return false;
            return true;
        }
    }
}
