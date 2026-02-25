
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Tests
{

    public class BooleanFactoryTests
    {

        #region Setup

        private readonly IXbimGeometryServicesFactory factory;
        private readonly ILoggerFactory _loggerFactory;

        const double Precision = 1e-5;

        public BooleanFactoryTests(IXbimGeometryServicesFactory factory, ILoggerFactory loggerFactory)
        {
            this.factory = factory;
            _loggerFactory = loggerFactory;
        }

        #endregion

        [Fact]
        public void Can_Clip_With_HalfSpace()
        {
            using var model = MemoryModel.OpenRead("TestFiles/BooleanClippingWithHalfSpace.ifc");
            var geomEngine = factory.CreateGeometryEngineV6(model, _loggerFactory);
            var booleanOp = model.Instances[1] as IIfcBooleanClippingResult;
            var shape = geomEngine.Build(booleanOp);
            shape.Should().NotBeNull();
            shape.Should().BeAssignableTo<IXSolid>();
            ((IXSolid)shape).Volume.Should().BeApproximately(125458771.93626986, Precision);
        }


        /// <summary>
        /// The polygonally bound half spaces are extremely small and leave holes that are less than 0.2mm
        /// Xbim ignores holes of this size as in building terms it is an extreme level of detail and most likely a draughting error
        /// </summary>
        [Fact]
        public void Can_build_boolean_clipping_result_with_halfspaces()
        {
            using var model = MemoryModel.OpenRead("TestFiles/boolean_clipping_result_with_halfspace.ifc");
            var geomEngine = factory.CreateGeometryEngineV6(model, _loggerFactory);
            var booleanOp = model.Instances[1] as IIfcBooleanClippingResult;
            var shape = geomEngine.Create(booleanOp) as IXbimSolid;
            shape.Should().NotBeNull();
            shape.Volume.Should().BeApproximately(3.4498500000004735, Precision);
        }

        [Fact]
        public void Can_build_boolean_result_with_small_solids()
        {
            using var model = MemoryModel.OpenRead("TestFiles/boolean_result_with_small_solids.ifc");
            var geomEngine = factory.CreateGeometryEngineV6(model, _loggerFactory);
            var booleanOp = model.Instances[1] as IIfcBooleanClippingResult;
            var shape = geomEngine.Create(booleanOp) as IXbimSolid;
            shape.Should().NotBeNull();
            shape.Volume.Should().BeApproximately(1.832928042298613e-5, Precision);
        }

        [Fact]
        public void Can_build_boolean_result_with_bad_polygonal_half_space_bounds()
        {
            using var model = MemoryModel.OpenRead("TestFiles/boolean_result_with_bad_polygonal_half_space_bounds.ifc");
            var geomEngine = factory.CreateGeometryEngineV6(model, _loggerFactory);
            var booleanOp = model.Instances[1] as IIfcBooleanClippingResult;
            var shape = geomEngine.Create(booleanOp) as IXbimSolid;
            shape.Should().NotBeNull();
            shape.Volume.Should().BeApproximately(0.91613407890247534, Precision);
        }


        [Theory]
        [InlineData(@"TestFiles/Ifc4TestFiles/wffdmcc3-_Navis - Existing.ifc")]
        public void CanBuildIfcClippingBooleanResult(string filePath)
        {
            // Arrange
            using var model = MemoryModel.OpenRead(filePath);
            var booleanResult = model.Instances[206469] as IIfcBooleanClippingResult;
            var modelSvc = factory.CreateModelGeometryService(model, _loggerFactory);

            // Act
            var solid = modelSvc.BooleanFactory.Build(booleanResult);

            // Assert
            solid.Should().NotBeNull();
        }

    }
}
