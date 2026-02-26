using FluentAssertions;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometricModelResource;
using Xbim.Ifc4x3.SharedInfrastructureElements;
using Xbim.IO.Memory;
using Xbim.ModelGeometry.Scene;
using Xunit;
using ILoggerFactory = Microsoft.Extensions.Logging.ILoggerFactory;



namespace Xbim.Geometry.Engine.Tests.IFC4x3Tests
{
    public class IfcSectionedSolidHorizontalTests
    {
        private readonly IXGeometryConverterFactory _factory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly string _brepOutputDir;
        private const double Tolerance = 1e-5;


        public IfcSectionedSolidHorizontalTests(IXGeometryConverterFactory factory, ILoggerFactory loggerFactory)
        {
            _factory = factory;
            _loggerFactory = loggerFactory;
            _brepOutputDir = Path.Combine(
                    Path.GetDirectoryName(typeof(IfcSectionedSolidHorizontalTests).Assembly.Location)!,
                    "BrepOutput");
            Directory.CreateDirectory(_brepOutputDir);
        }

        private void SaveBrep(IXShape shape, string name)
        {
#if DEBUG
            if (shape is XbimShape ns)
            {
                var path = Path.Combine(_brepOutputDir, $"{name}.brep");
                ns.WriteBrep(path);
            }
#endif
        }

        [Theory]
        [InlineData(@"TestFiles/IFC4x3/Viadotto Acerno.ifc", 160615)]
        [InlineData(@"TestFiles/IFC4x3/SectionedSolidHorizontal-1.ifc", 116)]
        public void CanBuildIfcSectionedSolidHorizontal(string ifcFile, int solidId)
        {
            // Arrange
            using var model = MemoryModel.OpenRead(ifcFile);

            var solid = model.Instances[solidId] as IfcSectionedSolidHorizontal;
            var modelSvc = _factory.CreateModelGeometryService(model, _loggerFactory);

            // Act
            var xSolid = modelSvc.SolidFactory.Build(solid);

            // Assert
            xSolid.Should().NotBeNull();
        }
    }

}
