using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Metadata;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Rules;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Ifc4;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

/// <summary>
/// Verifies that ProfileFactory.BuildFace throws IfcRuleViolationException
/// (not XbimGeometryServiceException) for IFC Where Rule violations.
/// </summary>
public class ProfileFactoryWRTests : IDisposable
{
    private readonly ModelGeometryService _service;
    private readonly IXProfileFactory _profileFactory;

    private static readonly ExpressMetaData MetaData =
        ExpressMetaData.GetMetadata(new EntityFactoryIfc4());

    public ProfileFactoryWRTests()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();
        _service = new ModelGeometryService(model, loggerFactory);
        _profileFactory = _service.ProfileFactory;
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
    public void BuildFace_ArbitraryClosed_LineCurve_ThrowsIfcRuleViolation()
    {
        var lineMoq = MakeMoq<IIfcLine>();
        lineMoq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(2));
        var profile = IfcMoq.ArbitraryClosedProfileWithCurve(lineMoq.Object, label: 50);

        var act = () => _profileFactory.BuildFace(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR2" && ex.EntityLabel == 50);
    }

    [Fact]
    public void BuildFace_ArbitraryClosed_OffsetCurve2D_ThrowsIfcRuleViolation()
    {
        var offsetMoq = MakeMoq<IIfcOffsetCurve2D>();
        offsetMoq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(2));
        var profile = IfcMoq.ArbitraryClosedProfileWithCurve(offsetMoq.Object, label: 60);

        var act = () => _profileFactory.BuildFace(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR3" && ex.EntityLabel == 60);
    }

    [Fact]
    public void BuildFace_ArbitraryClosed_3dCurve_ThrowsIfcRuleViolation()
    {
        var curveMoq = MakeMoq<IIfcPolyline>();
        curveMoq.SetupGet(c => c.Dim).Returns(new IfcDimensionCount(3));
        var profile = IfcMoq.ArbitraryClosedProfileWithCurve(curveMoq.Object, label: 70);

        var act = () => _profileFactory.BuildFace(profile);

        act.Should().Throw<IfcRuleViolationException>()
            .Where(ex => ex.RuleId == "WR1" && ex.EntityLabel == 70);
    }
}
