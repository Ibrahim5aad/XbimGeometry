using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Geometry.Engine.Interop;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xbim.Tessellator;
using Xunit;

namespace Xbim.Geometry.Engine.Tests
{
    public class Ifc4GeometryTests
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IXbimGeometryServicesFactory factory;
        private readonly ILogger _logger;

        public Ifc4GeometryTests(ILoggerFactory loggerFactory, IXbimGeometryServicesFactory factory)
        {
            _loggerFactory = loggerFactory;
            this.factory = factory;
            _logger = _loggerFactory.CreateLogger<Ifc4GeometryTests>();
        }

        [Fact]
        public void Can_build_ifcadvancedbrep_with_faulty_surface_orientation()
        {

            using (var model = MemoryModel.OpenRead(@"TestFiles/ifcadvancedbrep_with_faulty_surface_orientation.ifc"))
            {
                //MemoryModel.SetWorkArounds(model.Header, model.ModelFactors as XbimModelFactors);
                var advancedBrep = model.Instances.OfType<IIfcAdvancedBrep>().FirstOrDefault();
                model.AddRevitWorkArounds();
                advancedBrep.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);

                var solid = geomEngine.Create(advancedBrep, _logger) as IXbimSolid;
                solid.Volume.Should().BeApproximately(102264692.6969, 1e-4);
            }
        }


        [Fact]
        public void Can_build_polygonal_face_tessellation()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/polygonal-face-tessellation.ifc"))
            {
                var pfs = model.Instances.OfType<IIfcPolygonalFaceSet>().FirstOrDefault();
                pfs.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);
                var faceModel = geomEngine.CreateSurfaceModel(pfs, _logger);
                faceModel.Should().NotBeNull();
                faceModel.Solids.First().Faces.Count.Should().Be(11);
            }
        }

        [Fact]
        public void Can_build_polygonal_faceset_as_solid()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/polygonal-face-tessellation.ifc"))
            {
                var pfs = model.Instances.OfType<IIfcPolygonalFaceSet>().FirstOrDefault();
                pfs.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);
                var solidModel = geomEngine.Create(pfs, _logger) as IXbimSolid;

                solidModel.Should().NotBeNull();
                solidModel.Faces.Count.Should().Be(11);
                solidModel.Volume.Should().BeApproximately(6500000000000, 1);

            }
        }

        [Fact]
        public void composite_curve_with_disconnection()
        {
            using (var er = new EntityRepository<IIfcCompositeCurve>(nameof(composite_curve_with_disconnection)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(er.Model, _loggerFactory);
                var face = geomEngine.CreateFace(er.Entity, _logger);
                face.Area.Should().BeApproximately(22084715, 1);
            }
        }

        [Theory]
        [InlineData(@"TestFiles/IFC4x3/Viadotto Acerno.ifc")]
        public void CanBuildCurveNotFollowingImplementersAgreement(string filePath)
        {
            // Model contains IfcCurveSegments that don't follow IA precisely. E.g. use of IfcParameterValue vs IfcLengthMeasure
            // Arrange
            using var model = MemoryModel.OpenRead(filePath);
            var curve = model.Instances.FirstOrDefault(g => g is IIfcCompositeCurve) as IIfcCompositeCurve;
            var modelSvc = factory.CreateModelGeometryService(model, _loggerFactory);

            // Act
            var xCurve = modelSvc.CurveFactory.Build(curve);

            // Assert
            xCurve.Should().NotBeNull();
        }

        [Fact]
        public void CentreLineProfileTest()
        {
            using (var er = new EntityRepository<IIfcExtrudedAreaSolid>(nameof(CentreLineProfileTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(er.Model, _loggerFactory);
                var extrudedSolid = geomEngine.CreateSolid(er.Entity, _logger);
                HelperFunctions.IsValidSolid(extrudedSolid);
            }
        }

        [Fact]
        public void TrimmedCurveWithLargeRadianValueTest()
        {
            using (var er = new EntityRepository<IIfcExtrudedAreaSolid>(nameof(TrimmedCurveWithLargeRadianValueTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var extrudedSolid = geomEngine.CreateSolid(er.Entity, _logger);
                HelperFunctions.IsValidSolid(extrudedSolid);
                extrudedSolid.Volume.Should().BeApproximately(14999524619, 1);
            }
        }


        [Fact]
        public void ExtrudedSolidWithNullPositionTest()
        {
            using (var er = new EntityRepository<IIfcExtrudedAreaSolid>(nameof(ExtrudedSolidWithNullPositionTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var extrudedSolid = geomEngine.CreateSolid(er.Entity, _logger);
                HelperFunctions.IsValidSolid(extrudedSolid);

            }
        }
        [Fact]
        public void FacetedBrepIsValidSolidTest()
        {
            using (var er = new EntityRepository<IIfcFacetedBrep>(nameof(FacetedBrepIsValidSolidTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var solid = geomEngine.CreateSolidSet(er.Entity, _logger).FirstOrDefault();
                HelperFunctions.IsValidSolid(solid);

            }

        }

        [Fact]
        public void closed_shell_is_valid_test()
        {
            using (var er = new EntityRepository<IIfcFacetedBrep>(nameof(closed_shell_is_valid_test)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                geomEngine.ModelService.UpgradeFaceSets = true;
                var solids = geomEngine.CreateSolidSet(er.Entity, _logger);
                solids.Count.Should().Be(4, "Should return 4 solids");

                //repeat without upgradingFaceset false. This will create a solid with four shells that are exactly the same as the solids previously generated
                //in essence the topology is now incorrect but visually and mathmatically everything is the same
                geomEngine.ModelService.UpgradeFaceSets = false;
                var compoundSolid = geomEngine.CreateSolidSet(er.Entity, _logger);
                compoundSolid.Count.Should().Be(1, "Should return 1 solid");
                solids.Sum(s => s.Volume).Should().BeApproximately(compoundSolid.Sum(s => s.Volume), 1e-5);
            }

        }
        [Fact]
        public void FacetedBrepWithFacesOutsideNorlamTolerancesTest()
        {
            using (var er = new EntityRepository<IIfcFacetedBrep>(nameof(FacetedBrepWithFacesOutsideNorlamTolerancesTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var solid = geomEngine.CreateSolidSet(er.Entity, _logger).FirstOrDefault();
                HelperFunctions.IsValidSolid(solid);

            }

        }
        [Fact]
        public void CsgSolidIsValidSolidTest()
        {
            using (var er = new EntityRepository<IIfcCsgSolid>(nameof(CsgSolidIsValidSolidTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var solid = geomEngine.CreateSolidSet(er.Entity, _logger).FirstOrDefault();
                HelperFunctions.IsValidSolid(solid);

            }
        }
        [Fact]
        public void IndexedPolyCurveTest()
        {
            using (var er = new EntityRepository<IIfcExtrudedAreaSolid>(nameof(IndexedPolyCurveTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var solid = geomEngine.CreateSolid(er.Entity, _logger);
                HelperFunctions.IsValidSolid(solid);

            }
        }

        [Fact]
        public void TriangulatedFaceSetBasicTest()
        {
            using (var er = new EntityRepository<IIfcTriangulatedFaceSet>(nameof(TriangulatedFaceSetBasicTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);

                var beam = geomEngine.CreateSurfaceModel(er.Entity);

                Math.Abs(beam.BoundingBox.Volume - 20000000).Should().BeLessThan(1);
            }
        }

        [Fact]
        public void CsgSolidBoundingBoxTest()
        {
            using (var er = new EntityRepository<IfcCsgSolid>(nameof(CsgSolidBoundingBoxTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var solid = geomEngine.CreateSolidSet(er.Entity, _logger).FirstOrDefault();
                Math.Abs(solid.Volume - solid.BoundingBox.Volume).Should().BeLessThan(1e-5);

            }
        }

        [Fact]
        public void ExtrudedAreaSolidBasicTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/extruded-solid.ifc"))
            {
                var eas = model.Instances.OfType<IfcExtrudedAreaSolid>().FirstOrDefault();
                eas.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var solid = geomEngine.CreateSolid(eas, _logger);
                Math.Abs(solid.Volume - solid.BoundingBox.Volume).Should().BeLessThan(1e-5);
            }
        }
        [Fact]
        public void SurfaceModelBasicTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/surface-model.ifc"))
            {
                var shape = model.Instances.OfType<IfcFaceBasedSurfaceModel>().FirstOrDefault();
                shape.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.CreateSurfaceModel(shape, _logger);
                geom.Shells.Count.Should().Be(1);
                ((int)geom.Shells.First.BoundingBox.Volume).Should().Be(2000000000);
            }
        }

        [Fact]
        public void BrepSolidModelBasicTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/brep-model.ifc"))
            {
                var shape = model.Instances.OfType<IfcFacetedBrep>().FirstOrDefault();
                shape.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.CreateSolidSet(shape, _logger).FirstOrDefault();
                geom.Volume.Should().BeApproximately(geom.BoundingBox.Volume, 1e-5);
            }
        }


        [Fact]
        public void MultipleProfileBasicTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/BeamUnitTestsVaryingProfile.ifc"))
            {
                var profiles = model.Instances.OfType<IfcExtrudedAreaSolid>();
                profiles.Count().Should().Be(2);
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                foreach (var profile in profiles)
                {
                    var geom = geomEngine.CreateSolid(profile, _logger);
                    geom.Volume.Should().BeGreaterThan(0);
                }
            }
        }


        #region IfcAdvancedBrep geometries

        [Fact]
        public void AdvancedMultiSegmentPolylineTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/advanced-face-multisegment-polyline.ifc"))
            {
                var shape = model.Instances.OfType<IfcAdvancedBrep>().FirstOrDefault();
                shape.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.Create(shape, _logger) as IXbimSolid;
                Math.Abs(geom.Volume - 72767).Should().BeLessThan(1);
            }
        }

        [Fact]
        public void BrepSolidModelAdvancedTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/cube-advanced-brep.ifc"))
            {
                var shape = model.Instances.OfType<IfcAdvancedBrep>().FirstOrDefault();
                shape.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);

                var solid = geomEngine.Create(shape, _logger) as IXbimSolid;
                solid.Should().NotBeNull();
                solid.Volume.Should().BeApproximately(0.83333333333333282, 1e-7);

            }
        }

        /// <summary>
        /// This model has a composite curve with 12 segments, the 11th segment is an incorrectly defined trimed circle, the axis is wrongly defined
        /// The process should fail and handle the failure correctly
        /// </summary>
        [Fact]
        public void can_handle_discontinuous_composite_curve()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/discontinuous_composite_curve.ifc"))
            {
                var cc = model.Instances.OfType<IIfcCompositeCurve>().FirstOrDefault();
                cc.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var exception = Assert.Throws<XbimGeometryServiceException>(() => geomEngine.CreateWire(cc, _logger));
                exception.Message.Should().Be("IfcCompositeCurve could not be built as a wire");
            }
        }

        [Fact]
        public void AdvancedBrepTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/BasinAdvancedBrep.ifc"))
            {
                var advancedBrep = model.Instances.OfType<IfcAdvancedBrep>().FirstOrDefault();
                advancedBrep.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var basinSolids = geomEngine.CreateSolidSet(advancedBrep, _logger);

                basinSolids.Sum(s => s.Volume).Should().BeApproximately(2045022.3839364732, 1e-7);
            }
        }

        [Fact]
        public void AdvancedBrepComplexCurvesandSurfacesTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/Axis2PlacementError.ifc"))
            {
                var advancedBrep = model.Instances.OfType<IfcAdvancedBrep>().FirstOrDefault(i => i.EntityLabel == 27743);
                model.AddRevitWorkArounds();
                //units are not correctly set in the ifc file
                model.ModelFactors.Initialise(1, 1e-3, 1e-2);
                advancedBrep.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var basin = geomEngine.CreateSolidSet(advancedBrep, _logger);
                basin.Count.Should().Be(2);
                basin.Sum(s => s.Volume).Should().BeApproximately(44834423, 1);

            }
        }


        [Fact]
        public void TriangulatedFaceSetAdvancedTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/BasinTessellation.ifc"))
            {
                var triangulatedFaceSet = model.Instances.OfType<IfcTriangulatedFaceSet>().FirstOrDefault();
                triangulatedFaceSet.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var basin = geomEngine.CreateSurfaceModel(triangulatedFaceSet);
                basin.BoundingBox.Volume.Should().BeApproximately(23913891.92412, 1e-5);
            }
        }



        #endregion

        #region Tessellation tests

        [Fact]
        public void TriangulatedFaceSet1Test()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/beam-straight-i-shape-tessellated.ifc"))
            {
                var triangulatedFaceSet = model.Instances.OfType<IfcTriangulatedFaceSet>().FirstOrDefault();
                triangulatedFaceSet.Should().NotBeNull();

                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);

                var t = DateTime.Now;
                var geom = geomEngine.CreateSurfaceModel(triangulatedFaceSet);
                var elapsed = DateTime.Now - t;
                var elapsedMm = elapsed.TotalMilliseconds;
                Console.WriteLine($"Time taken to create solid from IfcTriangulatedFaceSet: {elapsedMm} ms");

                geom.Solids.Count.Should().Be(1);
                Math.Abs(geom.Solids.First.BoundingBox.Volume - 1.32).Should().BeLessThan(1e-5);

            }
        }

        [Fact]
        public void TriangulatedFaceSet2Test()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/column-straight-rectangle-tessellation.ifc"))
            {
                var triangulatedFaceSet = model.Instances.OfType<IfcTriangulatedFaceSet>().FirstOrDefault();
                triangulatedFaceSet.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.CreateSurfaceModel(triangulatedFaceSet);
                geom.Solids.Count.Should().Be(1);
                Math.Abs(geom.Solids.First.BoundingBox.Volume - 7680).Should().BeLessThan(1e-3);
            }
        }

        [Fact]
        public void TriangulatedFaceSet3Test()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/slab-tessellated-unique-vertices.ifc"))
            {
                var triangulatedFaceSet = model.Instances.OfType<IfcTriangulatedFaceSet>().FirstOrDefault();
                triangulatedFaceSet.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.CreateSurfaceModel(triangulatedFaceSet);
                var s = geom.Solids.First;
                geom.Solids.Count.Should().Be(1);
                Math.Abs(geom.Solids.First.BoundingBox.Volume - 103.92304).Should().BeLessThan(1e-3);
            }
        }

        #endregion

        #region Grid placement

        [Fact]
        public void GridTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/grid-placement.ifc"))
            {

                var placements = model.Instances.OfType<IIfcGridPlacement>();
                placements.Should().NotBeEmpty();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                foreach (var p in placements)
                {
                    XbimMatrix3D m = geomEngine.ToMatrix3D(p, _logger);
                    m.IsIdentity.Should().BeFalse();
                }
                //make a graphic of the grid
                var ifcGrid = model.Instances.OfType<IIfcGrid>().FirstOrDefault();
                ifcGrid.Should().NotBeNull();
                var geom = geomEngine.CreateGrid(ifcGrid, _logger);
                geom.Count.Should().BeGreaterThan(0, "At least one solid must be returned");
                foreach (var solid in geom)
                {
                    solid.Volume.Should().BeGreaterThan(0);
                }
            }
        }

        [Fact]
        public void GridWithIfcLineTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/grid-lines.ifc"))
            {

                var ifcGrid = model.Instances.OfType<IIfcGrid>().FirstOrDefault();
                ifcGrid.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.CreateGrid(ifcGrid, _logger);
                geom.Count.Should().BeGreaterThan(0, "At least one solid must be returned");
                foreach (var solid in geom)
                {
                    solid.Volume.Should().BeGreaterThan(0);
                }
            }
        }


        #endregion

        #region Tapered extrusions


        [Fact]
        public void ExtrudedAreaSolidTaperedTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/air-terminal-element.ifc"))
            {
                var taperedSolid = model.Instances.OfType<IfcExtrudedAreaSolidTapered>().FirstOrDefault();
                taperedSolid.Should().NotBeNull();
                var geomEngineV6 = factory.CreateGeometryEngine(model, _loggerFactory);
                var barV6 = geomEngineV6.Create(taperedSolid) as XbimSolid;
                barV6.Should().NotBeNull();
                barV6.Volume.Should().BeApproximately(707, 1);
            }
        }

        [Fact]
        public void RevolvedAreaSolidTaperedTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/beam-revolved-solid-tapered.ifc"))
            {
                var taperedSolid = model.Instances.OfType<IfcRevolvedAreaSolidTapered>().FirstOrDefault();
                taperedSolid.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var bar = geomEngine.CreateSolid(taperedSolid, _logger);
                bar.Volume.Should().BeGreaterThan(0);
            }
        }

        [Fact]
        public void WireInitFromIfcIndexedPolyCurveTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/WirePolycurve.ifc"))
            {
                var shape = model.Instances[185] as IIfcGeometricRepresentationItem;
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var geomObject = geomEngine.Create(shape, _logger) as XbimSolid;
                geomObject.IsValid.Should().BeTrue();
                geomObject.Volume.Should().Be(210000000);
            }
        }

        [Fact]
        public void SectionedSpineTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/sectioned-spine.ifc"))
            {
                var sectionedSpine = model.Instances.OfType<IfcSectionedSpine>().FirstOrDefault();
                sectionedSpine.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var bar = geomEngine.CreateSolid(sectionedSpine, _logger);
                bar.Volume.Should().BeGreaterThan(0);
            }
        }

        [Fact]
        public void FixedReferenceSweptSolidTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/fixed-reference-sweptarea.ifc"))
            {
                var sectionedSpine = model.Instances.OfType<IfcFixedReferenceSweptAreaSolid>().FirstOrDefault();
                sectionedSpine.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var bar = geomEngine.CreateSolid(sectionedSpine, _logger);
                bar.Volume.Should().BeApproximately(2513.059484, 1e-5);
            }
        }



        #endregion

        [Fact]
        public void MirroredProfileDefTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/IfcMirroredProfileDef.ifc"))
            {
                var derived = model.Instances[50] as IIfcDerivedProfileDef; //derived profile, mirrored by transform
                var mirrored = model.Instances[177] as IIfcMirroredProfileDef;//mirrored versio of above
                derived.Should().NotBeNull();
                mirrored.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var dFace = geomEngine.CreateFace(derived, _logger);
                var mFace = geomEngine.CreateFace(mirrored, _logger);
                var brepD = dFace.ToBRep;
                var brepM = mFace.ToBRep;
                var differ = new Diff();
                var diffs = differ.DiffText(brepM, brepD);
                mFace.Normal.X.Should().BeApproximately(dFace.Normal.X, 1e-5);
                mFace.Normal.Y.Should().BeApproximately(dFace.Normal.Y, 1e-5);
                mFace.Normal.Z.Should().BeApproximately(dFace.Normal.Z, 1e-5);
                diffs.Length.Should().Be(2);
            }
        }


        [Fact]
        public void CylindricalSurfaceTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/cylindrical-surface.ifc"))
            {
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var solids = geomEngine.CreateSolidSet();
                foreach (var brep in model.Instances.OfType<IIfcAdvancedBrep>())
                {

                    var geom = geomEngine.Create(brep, _logger) as IXbimSolid;

                    foreach (var face in geom.Faces)
                    {
                        face.Area.Should().BeGreaterThan(0);

                    }
                    geom.Volume.Should().BeGreaterThan(0);
                    solids.Add(geom);
                }
                solids.Sum(s => s.Volume).Should().BeApproximately(338123274, 1);
            }
        }

        [Fact]
        public void NotClosedShellTest()
        {
            using (var er = new EntityRepository<IIfcClosedShell>(nameof(NotClosedShellTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var solid = geomEngine.CreateSolidSet(er.Entity, _logger);

                solid.IsValid.Should().BeTrue();
            }
        }

        /// <summary>
        /// This test checks a composite curve that has trimmed IfcLine segments
        /// </summary>
        [Fact]
        public void CompositeCurveWithIfcLineTest()
        {
            using (var er = new EntityRepository<IIfcCompositeCurve>(nameof(CompositeCurveWithIfcLineTest)))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var curve = geomEngine.CreateWire(er.Entity, _logger);
                curve.Should().NotBeNull();
                curve.IsValid.Should().BeTrue();

                curve.IsPlanar.Should().BeTrue();
                curve.Points.Count().Should().Be(17);
            }
        }

        /// <summary>
        /// This test checks a compsite curve that has incorrect defintions for the sense of its segments
        /// </summary>
        [Fact]
        public void CompositeCurveBadSenseTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/composite-curve.ifc"))
            {
                var comp = model.Instances[3268144] as IIfcCompositeCurve;
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.CreateWire(comp, _logger);
            }
        }
        /// <summary>
        /// This test checks for a composite curve that has a trimmed circle that is not within the tolerance of the model at its connections
        /// </summary>
        [Fact]
        public void CompositeCurveBadPrecisionTest()
        {
            using (var er = new EntityRepository<IIfcCompositeCurve>(nameof(CompositeCurveBadPrecisionTest), 1.0, 1e-5, false))
            {
                er.Entity.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(er.Entity.Model, _loggerFactory);
                var wire = geomEngine.CreateWire(er.Entity, _logger);
                wire.Edges.Count.Should().Be(12);
                var curve = geomEngine.CreateCurve(er.Entity, _logger);
                curve.Length.Should().BeApproximately(wire.Length, 0.999);
            }

        }

        [Fact]
        public void CompositeCurveEmptySegmentTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/composite-curve4.ifc"))
            {
                var eas = model.Instances[3676127] as IIfcExtrudedAreaSolid;
                eas.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.CreateSolid(eas, _logger);
                geom.Volume.Should().BeApproximately(11443062587, 5);

            }
        }

        /// <summary>
        /// A Polygonally bounded half space that has a boundary that is not a closed area, the composite curve segments do not join
        /// this is an incorrectly defined half space and therefore it cannot build a valid result.
        /// The solid build should fail elegantly
        /// </summary>
        [Fact]
        public void CompositeCurveSegmentsDoNotCloseTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/composite-curve5.ifc"))
            {

                var pbhs = model.Instances[3942238] as IIfcBooleanClippingResult;
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);


                // Test the faulty entity on its own
                var ex = Assert.Throws<XbimGeometryServiceException>(() => geomEngine.ModelService.WireFactory.Build(model.Instances[3942179] as IIfcCompositeCurve));
                ex.Message.Should().Be("IfcCompositeCurve could not be built as a wire");


                // We return the first operand now
                var solid = geomEngine.CreateSolidSet(pbhs, _logger);
                solid.Should().NotBeNullOrEmpty();
                solid.Count.Should().Be(1);
            }
        }

        [Fact]
        public void BooleanOpeningsTotalSubractionTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/boolean-complete-subtraction.ifc"))
            {
                var ifcWall = model.Instances.OfType<IIfcWall>().FirstOrDefault();
                ifcWall.Should().NotBeNull();
                var ifcOpening = model.Instances.OfType<IIfcOpeningElement>().FirstOrDefault();
                ifcOpening.Should().NotBeNull();

                var opening = model.Instances[1133441] as IIfcExtrudedAreaSolid;
                opening.Should().NotBeNull();
                var wall = model.Instances[1133397] as IIfcExtrudedAreaSolid;
                wall.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                //create it in the right position
                var geomOpening = geomEngine.Create(opening, (IIfcAxis2Placement3D)((IIfcLocalPlacement)ifcOpening.ObjectPlacement).RelativePlacement, _logger) as IXbimSolid;
                geomOpening.Should().NotBeNull();
                geomOpening.Volume.Should().BeGreaterThan(0);
                var geomWall = geomEngine.CreateSolid(wall, _logger);
                geomWall.Volume.Should().BeGreaterThan(0);
                var result = geomWall.Cut(geomOpening, model.ModelFactors.Precision);
                result.Count.Should().Be(0);
            }
        }

        /// <summary>
        /// This test has a void with has zero area, it is a 2 segment self intersection polyline
        /// </summary>
        [Fact]
        public void CloseProfileWithVoidsTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/closed-profile-with-voids.ifc"))
            {

                var eas = model.Instances[23512] as IIfcExtrudedAreaSolid;
                eas.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.CreateSolid(eas, _logger);
                // var brep = geom.ToBRep;
                geom.Volume.Should().BeApproximately(2278352481546, 1);
            }
        }

        [Fact]
        public void TrimmedEllipseTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/TrimmedEllipseTest.ifc"))
            {

                var eas = model.Instances[272261] as IIfcExtrudedAreaSolid;
                eas.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var geom = geomEngine.CreateSolid(eas, _logger);
                geom.Volume.Should().BeApproximately(18117345688, 1);

            }
        }

        [Fact]
        public void LongRunningBooleanTest()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/Ifc4TestFiles/long-running-boolean.ifc"))
            {

                var ifcWall = model.Instances[39] as IIfcExtrudedAreaSolid;
                ifcWall.Should().NotBeNull();
                var geomEngine = new XbimGeometryEngine(model, _loggerFactory);
                var solids = geomEngine.CreateSolidSet();
                foreach (var ifcOpening in model.Instances.OfType<IIfcOpeningElement>())
                {
                    var firstOrDefault = ifcOpening.Representation.Representations.FirstOrDefault();
                    firstOrDefault.Should().NotBeNull();
                    {
                        var opening = firstOrDefault.Items.FirstOrDefault() as IIfcGeometricRepresentationItem;
                        var geomOpening = geomEngine.Create(opening, (IIfcAxis2Placement3D)((IIfcLocalPlacement)ifcOpening.ObjectPlacement).RelativePlacement, _logger) as IXbimSolid;
                        geomOpening.Should().NotBeNull();
                        solids.Add(geomOpening);
                    }

                }
                var wallGeom = geomEngine.CreateSolid(ifcWall, _logger);

                var result = wallGeom.Cut(solids, model.ModelFactors.Precision);
                result.Count.Should().BeGreaterThan(0);

            }
        }

        [Fact]
        public void IfcCenterLineProfileDefTest()
        {

            using (var m = new MemoryModel(new Ifc4.EntityFactoryIfc4()))
            {
                using (var txn = m.BeginTransaction("Test"))
                {
                    var semiCircle = IfcModelBuilder.MakeSemiCircle(m, 20);
                    var cl = IfcModelBuilder.MakeCenterLineProfileDef(m, semiCircle, 5);
                    var geomEngine = new XbimGeometryEngine(m, _loggerFactory);
                    var face = geomEngine.CreateFace(cl, _logger);
                    face.Should().NotBeNull();
                    face.IsValid.Should().BeTrue("Invalid face returned");
                }
            }
        }

        [Fact]
        public void IfcSurfaceOfLinearExtrusionTest()
        {
            using (var m = new MemoryModel(new Ifc4.EntityFactoryIfc4()))
            using (var txn = m.BeginTransaction("Test"))
            {
                var semiCircle = IfcModelBuilder.MakeSemiCircle(m, 20);
                var def = IfcModelBuilder.MakeArbitraryOpenProfileDef(m, semiCircle);
                var cl = IfcModelBuilder.MakeSurfaceOfLinearExtrusion(m, def, 50, new XbimVector3D(0, 0, 1));
                var geomEngine = new XbimGeometryEngine(m, _loggerFactory);
                var face = geomEngine.CreateFace(cl, _logger);
                face.Should().NotBeNull();
                face.IsValid.Should().BeTrue("Invalid face returned");
            }
        }

        [Fact]
        public void IfcSurfaceOfRevolutionTest()
        {
            using (var m = new MemoryModel(new Ifc4.EntityFactoryIfc4()))
            using (var txn = m.BeginTransaction("Test"))
            {
                var cc = IfcModelBuilder.MakeRationalBSplineCurveWithKnots(m);
                var def = IfcModelBuilder.MakeArbitraryOpenProfileDef(m, cc);
                var rev = IfcModelBuilder.MakeSurfaceOfRevolution(m, def);
                var geomEngine = new XbimGeometryEngine(m, _loggerFactory);
                var face = geomEngine.CreateFace(rev, _logger);
                face.Should().NotBeNull();
                face.IsValid.Should().BeTrue("Invalid face returned");
            }
        }

        [Fact]
        public void IncorrectlyDefinedEdgeCurveV6()
        {
            using var model = MemoryModel.OpenRead(
                @"TestFiles/incorrectly_defined_edge_curve_with_identical_points.ifc");
            model.AddRevitWorkArounds();
            var brep = model.Instances.OfType<IIfcAdvancedBrep>().FirstOrDefault();
            brep.Should().NotBeNull();
            var geomEngine = new XbimGeometryEngine(model, _loggerFactory);

            // CreateSolid must include all sub-solids, matching the total from CreateSolidSet
            var solidSet = geomEngine.CreateSolidSet(brep, _logger);
            var solid = geomEngine.CreateSolid(brep, _logger);

            solid.Should().NotBeNull();
            double setVolume = solidSet.Sum(s => s.Volume);
            solid.Volume.Should().BeApproximately(
                setVolume, setVolume * 0.05,
                "CreateSolid must aggregate all sub-solids, not truncate to the first");
        }

        [Fact]
        public void IfcTriangulatedFaceSetTest()
        {
            using (var store = IfcStore.Open("TestFiles/Ifc4TestFiles/BasinTessellation.ifc"))
            {
                var basinTess = store.Instances[501] as IIfcTriangulatedFaceSet;

                var tessellator = new XbimTessellator(store, XbimGeometryType.PolyhedronBinary);
                Assert.NotNull(basinTess);
                Assert.True(tessellator.CanMesh(basinTess));
                var geom = tessellator.Mesh(basinTess);
                Assert.True((int)(geom.BoundingBox.Volume) == 23913891);

            }
        }


        [Fact]
        public void IfcTriangulatedFaceSetWithNormalsTest()
        {
            using (var store = IfcStore.Open("TestFiles/Ifc4TestFiles/column-straight-rectangle-tessellation.ifc"))
            {
                var columnTess = store.Instances[288] as IIfcTriangulatedFaceSet;

                var tessellator = new XbimTessellator(store, XbimGeometryType.PolyhedronBinary);
                Assert.NotNull(columnTess);
                Assert.True(tessellator.CanMesh(columnTess));
                var geom = tessellator.Mesh(columnTess);
                Assert.True((int)(geom.BoundingBox.Volume) == 7680);

            }
        }

        [Fact]
        public void IfcTriangulatedFaceSetWithColoursTest()
        {
            using (var store = IfcStore.Open("TestFiles/IFC4x3/tessellation-with-individual-colors.ifc"))
            {
                var triangulatedFaceSet = store.Instances.OfType<IIfcTriangulatedFaceSet>().FirstOrDefault();
                Assert.NotNull(triangulatedFaceSet);

                var tessellator = new XbimTessellator(store, XbimGeometryType.PolyhedronBinary);
                Assert.True(tessellator.CanMesh(triangulatedFaceSet));
                var geom = tessellator.Mesh(triangulatedFaceSet);
                Assert.True((int)(geom.BoundingBox.Volume) == 2000000000);
            }
        }


        [Fact]
        public void IfcTriangulatedFaceSetSimpleBinaryTest()
        {
            using (var store = IfcStore.Open("TestFiles/Ifc4TestFiles/BasinTessellation.ifc"))
            {
                var triangulatedFaceSet = store.Instances.OfType<IfcTriangulatedFaceSet>().FirstOrDefault();

                var tessellator = new XbimTessellator(store, XbimGeometryType.PolyhedronBinary);
                Assert.NotNull(triangulatedFaceSet);
                Assert.True(tessellator.CanMesh(triangulatedFaceSet));
                var geom = tessellator.Mesh(triangulatedFaceSet);
                using (var ms = new MemoryStream(((IXbimShapeGeometryData)geom).ShapeData))
                {
                    using (var br = new BinaryReader(ms))
                    {
                        XbimShapeTriangulation myShapeTriangulation = br.ReadShapeTriangulation();
                        Assert.True(myShapeTriangulation.Faces.Sum(t => t.TriangleCount) == triangulatedFaceSet.NumberOfTriangles);
                    }
                }
            }
        }
        [Fact]
        public void IfcTriangulatedFaceSetComplexBinaryTest()
        {
            using (var store = IfcStore.Open("TestFiles/Ifc4TestFiles/IFC4TessellationComplex.ifc"))
            {
                var triangulatedFaceSet = store.Instances[4373] as IIfcTriangulatedFaceSet;

                var tessellator = new XbimTessellator(store, XbimGeometryType.PolyhedronBinary);
                Assert.NotNull(triangulatedFaceSet);
                Assert.True(tessellator.CanMesh(triangulatedFaceSet));
                var geom = tessellator.Mesh(triangulatedFaceSet);
                using (var ms = new MemoryStream(((IXbimShapeGeometryData)geom).ShapeData))
                {
                    using (var br = new BinaryReader(ms))
                    {
                        XbimShapeTriangulation myShapeTriangulation = br.ReadShapeTriangulation();
                        Assert.True(myShapeTriangulation.Faces.Sum(t => t.TriangleCount) == triangulatedFaceSet.NumberOfTriangles);
                    }
                }
            }
        }

    }
}
