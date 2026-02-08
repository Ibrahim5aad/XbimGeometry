using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Rules;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Verifies that CurveFactory.Build throws IfcRuleViolationException
/// (not XbimGeometryServiceException) for IFC Where Rule violations.
/// </summary>
public class CurveFactoryWRTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXCurveFactory _curveFactory;

    public CurveFactoryWRTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _curveFactory = _service.CurveFactory;
    }

    public void Dispose() => _service.Dispose();

    private static Mock<T> MakeMoq<T>() where T : class, IPersistEntity
    {
        return new Mock<T>
        {
            DefaultValue = DefaultValue.Mock,
            DefaultValueProvider = new MoqDefaultBehaviourProvider()
        }.SetupAllProperties();
    }

    [Fact]
    public void Build_TrimmedCurve_BoundedBasis_ThrowsIfcRuleViolation_WR42()
    {
        // A trimmed curve whose basis is already bounded violates WR42
        var basisMoq = MakeMoq<IIfcPolyline>();
        basisMoq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(3));
        basisMoq.SetupGet(c => c.EntityLabel).Returns(100);

        var trimmedMoq = MakeMoq<IIfcTrimmedCurve>();
        trimmedMoq.SetupGet(c => c.BasisCurve).Returns(basisMoq.Object);
        trimmedMoq.SetupGet(c => c.EntityLabel).Returns(80);
        trimmedMoq.SetupGet(c => c.SenseAgreement).Returns(true);
        trimmedMoq.SetupGet(c => c.MasterRepresentation).Returns(IfcTrimmingPreference.PARAMETER);
        trimmedMoq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(3));
        trimmedMoq.Object.Trim1.Add(new IfcParameterValue(0.0));
        trimmedMoq.Object.Trim2.Add(new IfcParameterValue(1.0));

        var act = () => _curveFactory.Build(trimmedMoq.Object);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR42" && ex.EntityLabel == 80);
    }

    [Fact]
    public void Build_TrimmedCurve_EqualParams_ThrowsIfcRuleViolation_WR41()
    {
        // A trimmed curve with equal parametric trim values violates WR41
        var basisMoq = MakeMoq<IIfcLine>();
        basisMoq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(3));
        basisMoq.SetupGet(c => c.EntityLabel).Returns(101);

        var trimmedMoq = MakeMoq<IIfcTrimmedCurve>();
        trimmedMoq.SetupGet(c => c.BasisCurve).Returns(basisMoq.Object);
        trimmedMoq.SetupGet(c => c.EntityLabel).Returns(81);
        trimmedMoq.SetupGet(c => c.SenseAgreement).Returns(true);
        trimmedMoq.SetupGet(c => c.MasterRepresentation).Returns(IfcTrimmingPreference.PARAMETER);
        trimmedMoq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(3));
        trimmedMoq.Object.Trim1.Add(new IfcParameterValue(5.0));
        trimmedMoq.Object.Trim2.Add(new IfcParameterValue(5.0));

        var act = () => _curveFactory.Build(trimmedMoq.Object);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR41" && ex.EntityLabel == 81);
    }
}
