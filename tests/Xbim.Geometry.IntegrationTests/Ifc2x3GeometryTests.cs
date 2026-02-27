using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xbim.Tessellator;
using Xunit;

namespace Xbim.Geometry.Engine.Tests
{
    public class Ifc2x3GeometryTests
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IXGeometryConverterFactory factory;
        private readonly ILogger _logger;

        public Ifc2x3GeometryTests(ILoggerFactory loggerFactory, IXGeometryConverterFactory factory)
        {
            _loggerFactory = loggerFactory;
            this.factory = factory;
            _logger = _loggerFactory.CreateLogger<Ifc2x3GeometryTests>();
        }

        [Fact]
        public void CanBuildExtrudedAreaSolid()
        {

            using (var model = IfcStore.Open(@"TestFiles/IFC2x3/FlowSegment.ifc"))
            {
                var extrusion = model.Instances[129365] as IIfcExtrudedAreaSolid;
                extrusion.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);

                var solid = geomEngine.Create(extrusion, _logger);
                solid.Should().NotBeNull();
            }
        }
    }
}