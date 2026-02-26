using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Rules
{
    /// <summary>
    /// Thrown when an IFC entity violates a formal Where Rule defined in the IFC specification.
    /// Distinguishes IFC file malformation from geometry engine failures.
    /// </summary>
    public class IfcRuleViolationException : XbimGeometryServiceException
    {
        /// <summary>
        /// The IFC Where Rule identifier (e.g. "WR1", "WR2").
        /// </summary>
        public string RuleId { get; }

        /// <summary>
        /// The IFC entity label of the violating entity.
        /// </summary>
        public int EntityLabel { get; }

        /// <summary>
        /// The IFC entity type name (e.g. "IfcArbitraryClosedProfileDef").
        /// </summary>
        public string EntityType { get; }

        /// <summary>
        /// Creates a new rule violation exception for the specified entity and rule.
        /// </summary>
        /// <param name="ruleId">The IFC Where Rule identifier (e.g. "WR1").</param>
        /// <param name="entityLabel">The IFC entity label.</param>
        /// <param name="entityType">The IFC entity type name.</param>
        /// <param name="description">A human-readable description of the violation.</param>
        public IfcRuleViolationException(string ruleId, int entityLabel, string entityType, string description)
            : base($"{entityType} #{entityLabel} violates {ruleId}: {description}")
        {
            RuleId = ruleId;
            EntityLabel = entityLabel;
            EntityType = entityType;
        }
    }
}
