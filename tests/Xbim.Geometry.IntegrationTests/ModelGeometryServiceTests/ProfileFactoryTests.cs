using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Tests
{

    public class ProfileFactoryTests
    {
        #region Setup

        private readonly ILoggerFactory _loggerFactory;
        private readonly IXGeometryConverterFactory factory;

        public ProfileFactoryTests(ILoggerFactory loggerFactory, IXGeometryConverterFactory factory)
        {
            _loggerFactory = loggerFactory;
            this.factory = factory;
        }

        #endregion

        [Fact]
        void Can_Build_IIfcArbitraryProfileDef_With_Composite_Curve_Void()
        {
            using var model = MemoryModel.OpenRead("TestFiles/ArbritaryClosedProfileWithCompositeCurveVoid.ifc");
            var engine = (IXGeometryEngineV6)factory.CreateGeometryEngine(model, _loggerFactory);
            var ifcArbitraryProfileDefWithVoids = model.Instances[1] as IIfcArbitraryProfileDefWithVoids;
            var v6face = engine.ProfileFactory.BuildFace(ifcArbitraryProfileDefWithVoids);
            Assert.NotNull(v6face);
            var v5Face = engine.CreateFace(ifcArbitraryProfileDefWithVoids);
            Assert.NotNull(v5Face);
            v5Face.Area.Should().BeApproximately(v6face.Area, 1e-5);
        }

        /// <summary>
        /// In this test several of the inner voids are not areas
        /// </summary>
        [Fact]
        void Can_Build_IIfcArbitraryProfileDef_With_bad_precision_on_closing_segments()
        {
            using var model = MemoryModel.OpenRead("TestFiles/ArbritaryClosedProfileWithBadPrecisionOnClosingSegments.ifc");
            var engine = (IXGeometryEngineV6)factory.CreateGeometryEngine(model, _loggerFactory);
            var ifcArbitraryProfileDefWithVoids = model.Instances[1] as IIfcArbitraryProfileDefWithVoids;
            var v6face = engine.ProfileFactory.BuildFace(ifcArbitraryProfileDefWithVoids);
            Assert.NotNull(v6face);
            var v5Face = engine.CreateFace(ifcArbitraryProfileDefWithVoids);
            Assert.NotNull(v5Face);
            v5Face.Area.Should().BeApproximately(v6face.Area, 1e-5);
        }

        [Fact]
        public void Can_Build_Extruded_CompositeProfileDef()
        {
            using var model = MemoryModel.OpenRead("TestFiles/CuttingOpeningInCompositeProfileDefTest.ifc");
            var engineV6 = (IXGeometryEngineV6)factory.CreateGeometryEngine(model, _loggerFactory);
            var extrusion = model.Instances[43] as IIfcExtrudedAreaSolid;
            extrusion.Should().NotBeNull();
            var occ = engineV6.Build(extrusion) as IXCompound;
            occ.Should().NotBeNull();
            occ.IsSolidsOnly.Should().BeTrue();
            var compositeProfile = (IIfcCompositeProfileDef)extrusion.SweptArea;
            occ.Solids.Length.Should().Be(compositeProfile.Profiles.Count);
            occ.Solids.Sum(s => s.Volume).Should().BeApproximately(12399283891, 1);
        }

    }
}
