using FluentAssertions;
using Xbim.Geometry.Engine.Rules;
using Xbim.Geometry.Exceptions;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

public class IfcRuleViolationExceptionTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var ex = new IfcRuleViolationException("WR1", 42, "IfcArbitraryClosedProfileDef", "OuterCurve.Dim must be 2");

        ex.RuleId.Should().Be("WR1");
        ex.EntityLabel.Should().Be(42);
        ex.EntityType.Should().Be("IfcArbitraryClosedProfileDef");
    }

    [Fact]
    public void Message_HasExpectedFormat()
    {
        var ex = new IfcRuleViolationException("WR2", 100, "IfcTrimmedCurve", "BasisCurve must not be bounded");

        ex.Message.Should().Be("IfcTrimmedCurve #100 violates WR2: BasisCurve must not be bounded");
    }

    [Fact]
    public void InheritsFromXbimGeometryServiceException()
    {
        var ex = new IfcRuleViolationException("WR1", 1, "IfcCircle", "test");

        ex.Should().BeAssignableTo<XbimGeometryServiceException>();
    }
}
