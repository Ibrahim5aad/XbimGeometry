using FluentAssertions;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop;
using Xbim.Ifc4x3.GeometricModelResource;
using Xbim.IO.Memory;
using Xunit;
using ILoggerFactory = Microsoft.Extensions.Logging.ILoggerFactory;



namespace Xbim.Geometry.Engine.Tests.IFC4x3Tests
{
    public class IfcDirectrixDerivedReferenceSweptAreaSolidTests
    {
        private readonly IXbimGeometryServicesFactory _factory;
        private readonly ILoggerFactory _loggerFactory;


        public IfcDirectrixDerivedReferenceSweptAreaSolidTests(IXbimGeometryServicesFactory factory, ILoggerFactory loggerFactory)
        {
            _factory = factory;
            _loggerFactory = loggerFactory;
        }


        [Fact]
        public void Can_build_sleeper_with_cant_2778()
        {
            // ACCA sleeper with cant/superelevation — directrix spans ~50m along X
            using var model = MemoryModel.OpenRead(
                @"TestFiles/IFC4x3/ACCA_sleepers-linear-placement-cant-implicit.ifc");
            var solid = model.Instances[2778] as IfcDirectrixDerivedReferenceSweptAreaSolid;
            var modelSvc = _factory.CreateModelGeometryService(model, _loggerFactory);

            var xShape = modelSvc.SolidFactory.Build(solid);

            xShape.Should().NotBeNull();
            var xSolid = xShape.Should().BeAssignableTo<IXSolid>().Subject;
            xSolid.IsValidShape().Should().BeTrue();
            xSolid.Shells.Should().HaveCount(1);
            xSolid.Shells[0].Faces.Length.Should().BeGreaterThanOrEqualTo(3);

            // Volume ~0.388 m³ (small railway sleeper cross-section swept over 50m)
            xSolid.Volume.Should().BeApproximately(0.388, 0.05);

            var bb = xSolid.Bounds();
            bb.IsVoid.Should().BeFalse();
            // Directrix runs along X from ~400 to ~450
            bb.LenX.Should().BeApproximately(50.0, 1.0);
            // Cross-section is narrow in Y and Z
            bb.LenY.Should().BeApproximately(0.37, 0.1);
            bb.LenZ.Should().BeApproximately(0.24, 0.1);
        }

        [Fact]
        public void Can_build_directrix_derived_solid_119()
        {
            // DirectrixDerived solid — directrix spans ~100m, 3m×3m cross-section
            using var model = MemoryModel.OpenRead(
                @"TestFiles/IFC4x3/DirectrixDerivedReferenceSweptAreaSolid-2.ifc");
            var solid = model.Instances[119] as IfcDirectrixDerivedReferenceSweptAreaSolid;
            var modelSvc = _factory.CreateModelGeometryService(model, _loggerFactory);

            var xShape = modelSvc.SolidFactory.Build(solid);

            xShape.Should().NotBeNull();
            var xSolid = xShape.Should().BeAssignableTo<IXSolid>().Subject;
            xSolid.IsValidShape().Should().BeTrue();
            xSolid.Shells.Should().HaveCount(1);
            xSolid.Shells[0].Faces.Length.Should().BeGreaterThanOrEqualTo(3);

            // Volume ~900 m³ (3m×3m cross-section swept over 100m)
            xSolid.Volume.Should().BeApproximately(900.0, 5.0);

            var bb = xSolid.Bounds();
            bb.IsVoid.Should().BeFalse();
            // Directrix runs ~100m along X
            bb.LenX.Should().BeApproximately(100.0, 1.0);
            // Cross-section bounds (because of the cant tilt) spans ~5.6m in Y and ~8.6m in Z
            bb.LenY.Should().BeApproximately(5.6, 0.5);
            bb.LenZ.Should().BeApproximately(8.6, 0.5);
        }

    }

}
