using FluentAssertions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Xbim.Common.Geometry;
using Xbim.Common.Model;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xbim.Geometry.Scene;
using Xunit;
namespace Xbim.Geometry.Engine.Tests;


public class NewEngineRegressions
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<NewEngineRegressions> _logger;
    private readonly IXGeometryConverterFactory _geometryfactory;

    public NewEngineRegressions(ILoggerFactory loggerFactory, IXGeometryConverterFactory geometryfactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<NewEngineRegressions>();
        _geometryfactory = geometryfactory;
    }


    [Fact]
    public void ClosedProfileExtrusion()
    {
        using (var m = new MemoryModel(new Ifc2x3.EntityFactoryIfc2x3()))
        {
            m.LoadStep21("TestFiles/Regressions/LakeFault.ifc");
            var extrusion = m.Instances[557697] as IIfcExtrudedAreaSolid;
            var geomEngine = new XbimGeometryEngine(m, _loggerFactory);

            var solid = geomEngine.CreateSolid(extrusion, _logger);

            solid.Should().NotBeNull();
            solid.Volume.Should().BeApproximately(433980, 1);
        }
    }

      [Fact]
    public void FaceBasedSurfaceModel()
    {
        using (var m = new MemoryModel(new Ifc2x3.EntityFactoryIfc2x3()))
        {
            m.LoadStep21("TestFiles/Regressions/Landscape.62995.ifc");
            var extrusion = m.Instances[62991] as IIfcFaceBasedSurfaceModel;
            var geomEngine = new XbimGeometryEngine(m, _loggerFactory);

            var solid = geomEngine.Build(extrusion);

            solid.Should().NotBeNull();
        }
    }

}

