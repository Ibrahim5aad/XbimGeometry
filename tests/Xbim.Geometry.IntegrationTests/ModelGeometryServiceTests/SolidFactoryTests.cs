using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Tests
{

    public class SolidFactoryTests
    {
        #region Setup

        private readonly ILoggerFactory _loggerFactory;
        private readonly IXGeometryConverterFactory _factory;

        public SolidFactoryTests(ILoggerFactory loggerFactory, IXGeometryConverterFactory factory)
        {
            _loggerFactory = loggerFactory;
            _factory = factory;
        }

        #endregion

        [Fact]
        public void Can_extrude_arbitrary_profile_def_with_voids()
        {
            using var model = MemoryModel.OpenRead("TestFiles/ExtrudedAreaSolidFailsOnExtrusion.ifc");
            var engine = (IXGeometryEngineV6)_factory.CreateGeometryEngine(model, _loggerFactory);
            var ifcExtrudedAreaSolid = model.Instances[1] as IIfcExtrudedAreaSolid;
            var v6Solid = engine.SolidFactory.Build(ifcExtrudedAreaSolid) as IXSolid;
            Assert.NotNull(v6Solid);
            var v5Solid = engine.CreateSolid(ifcExtrudedAreaSolid);
            Assert.NotNull(v5Solid);
            v5Solid.Volume.Should().BeApproximately(v6Solid.Volume, 1e-5);
        }
    }

}
