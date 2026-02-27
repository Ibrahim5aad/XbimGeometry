using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using System.Linq;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xbim.Geometry.Engine;
using Xbim.Geometry.Exceptions;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Tests
{

    // [DeploymentItem("TestFiles")]
    public class IfcAdvancedBrepTests
    {


        private readonly ILoggerFactory _loggerFactory;
        private readonly IXGeometryConverterFactory factory;

        public IfcAdvancedBrepTests(ILoggerFactory loggerFactory, IXGeometryConverterFactory factory)
        {
            _loggerFactory = loggerFactory;
            this.factory = factory;
        }

        [Fact]
        public void IfcAdvancedBrepTrimmedCurveTest()
        {
            using (var er = new EntityRepository<IIfcAdvancedBrep>(nameof(IfcAdvancedBrepTrimmedCurveTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(er.Entity.Model, _loggerFactory);
                var solid = geomEngine.CreateSolid(er.Entity);
                solid.Faces.Count.Should().Be(14, "This solid should have 14 faces");
            }
        }

        [Fact]
        public void Incorrectly_defined_edge_curve()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/incorrectly_defined_edge_curve.ifc"))
            {
                var brep = model.Instances.OfType<IIfcAdvancedBrep>().FirstOrDefault();
                brep.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);
                var solids = geomEngine.CreateSolidSet(brep);
                solids.Count.Should().Be(3); //this should really be one but the model is incorrect
                solids.First().Faces.Count.Should().Be(60);
            }

        }
        /// <summary>
        /// This test still produces incorrect solids and surfaces, the model appears to be faulty
        /// </summary>
        [Fact]
        public void Incorrectly_defined_edge_curve_with_identical_points()
        {

            using (var model = MemoryModel.OpenRead(@"TestFiles/incorrectly_defined_edge_curve_with_identical_points.ifc"))
            {
                //this model needs workarounds to be applied
                model.AddRevitWorkArounds();
                var brep = model.Instances.OfType<IIfcAdvancedBrep>().FirstOrDefault();
                brep.Should().NotBeNull();

                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);

                var solids = geomEngine.CreateSolidSet(brep);

                solids.Count.Should().Be(2);
                var s1 = solids.ElementAt(0);
                s1.Faces.Count.Should().Be(14);
            }
        }

        [Theory]
        [InlineData("SurfaceCurveSweptAreaSolid_1", 5888416.692/*, DisplayName = "Handles Swepted elipse, sweep parameters override directrix trims"*/)]
        [InlineData("SurfaceCurveSweptAreaSolid_2", 0.0, true, false, true/*, DisplayName = "Handles  reference surface incorrectly parallel to sweep"*/)]
        [InlineData("SurfaceCurveSweptAreaSolid_3", 0.26111117805532907, false/*, DisplayName = "Reference Model from IFC documentation"*/)]
        [InlineData("SurfaceCurveSweptAreaSolid_4", 19.276830224679465/*, DisplayName = "Handles Trimmed directrix is periodic"*/)]
        [InlineData("SurfaceCurveSweptAreaSolid_5", 12.603349469526613, false, true/*, DisplayName = "Handles Polylines Incorrectly Trimmed as 0 to 1"*/)]
        // [InlineData("SurfaceCurveSweptAreaSolid_6", 333574/*, DisplayName = "Directrix trim incorrectly set to 0, 360 by Revit, creates a sphere"*/)]
        [InlineData("SurfaceCurveSweptAreaSolid_7", 927671, false/*, DisplayName = "Directrix trim from Flex Ifc Exporter trim  set to 270, 360 by Revit. Creates a 90 deg elbow"*/)]

        public void SurfaceCurveSweptAreaSolid_Tests(string fileName, double requiredVolume, bool addLinearExtrusionWorkAround = true, bool addPolyTrimWorkAround = false, bool throwsException = false)
        {
            using (var model = MemoryModel.OpenRead($@"TestFiles/{fileName}.ifc"))
            {
                if (addLinearExtrusionWorkAround)
                    ((XbimModelFactors)model.ModelFactors).AddWorkAround("#SurfaceOfLinearExtrusion");
                if (addPolyTrimWorkAround)
                    model.AddWorkAroundTrimForPolylinesIncorrectlySetToOneForEntireCurve();
                var surfaceSweep = model.Instances.OfType<IIfcSurfaceCurveSweptAreaSolid>().FirstOrDefault();
                surfaceSweep.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);
                if (throwsException)
                {
                    var ex = Assert.Throws<XbimGeometryServiceException>(() => geomEngine.CreateSolid(surfaceSweep));
                    ex.Message.Should().StartWith($"Failed to build SurfaceCurveSweptAreaSolid #{surfaceSweep.EntityLabel}");
                }
                else
                {
                    var sweptSolid = geomEngine.CreateSolid(surfaceSweep);
                    sweptSolid.Volume.Should().BeApproximately(requiredVolume, 1);
                }

            }
        }


        [Fact]
        public void Advanced_brep_with_sewing_issues()
        {

            using (var model = MemoryModel.OpenRead(@"TestFiles/advanced_brep_with_sewing_issues.ifc"))
            {
                var brep = model.Instances.OfType<IIfcAdvancedBrep>().FirstOrDefault();
                brep.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);
                var solids = geomEngine.CreateSolidSet(brep);
                var shapeGeom = geomEngine.CreateShapeGeometry(solids,
                    model.ModelFactors.Precision, model.ModelFactors.DeflectionTolerance,
                    model.ModelFactors.DeflectionAngle, XbimGeometryType.PolyhedronBinary);

                solids.Count.Should().Be(2);
                solids.First().Faces.Count.Should().Be(37);
                solids.Last().Faces.Count.Should().Be(10);
            }

        }


        [Theory]
        [InlineData("advanced_brep_1", 1, 2452539   /*, DisplayName = "Self Intersection unorientable shape"*/)]
        [InlineData("advanced_brep_2", 1, 828514    /*, DisplayName = "Curved edges with varying orientation"*/)]
        [InlineData("advanced_brep_3", 1, 2466953   /*, DisplayName = "Badly formed wire orders and missing faces and holes, accurate in V6 but still bad definition"*/)]
        [InlineData("advanced_brep_4", 2, 864225    /*, DisplayName = "Two solids from one advanced brep, errors in holes"*/)]
        [InlineData("advanced_brep_5", 1, 110       /*, DisplayName = "Example of arc and circle having centre displaced twice RevitIncorrectArcCentreSweptCurve"*/)]
        [InlineData("advanced_brep_6", 1, 3246676   /*, DisplayName = "The top face of the sink does not have a hole defined in it, fault model. V6 is truer"*/)]
        [InlineData("advanced_brep_7", 2, 1821558   /*, DisplayName = "Pipe unit built as 2 pieces in V5, V6 correctly build to one piece"*/)]
        [InlineData("advanced_brep_8", 2, 53286     /*, DisplayName = "BSpline with displacement applied twice, example of RevitIncorrectBsplineSweptCurve, V6 corrects dual solids"*/)]
        public void Advanced_brep_tests(string brepFileName, int count, double volume)
        {

            using var model = MemoryModel.OpenRead($@"TestFiles/{brepFileName}.ifc");
            model.AddRevitWorkArounds();

            var brep = model.Instances.OfType<IIfcAdvancedBrep>().FirstOrDefault();
            brep.Should().NotBeNull();
            var engine = factory.CreateGeometryEngine(model, _loggerFactory);

            using var shape = engine.Create(brep);

            if (shape is IXbimSolid solid)
            {
                solid.IsValid.Should().BeTrue();
                volume.Should().BeApproximately(solid.Volume, 10);
            }
            else if (shape is IXCompound c)
            {
                var solids = c.Solids;
                solids.Should().HaveCount(count);
            }
            else if (shape is IXbimGeometryObjectSet set)
            {
                var solids = set.Solids;
                solids.Should().HaveCount(count);
            }
        }
    }
}
