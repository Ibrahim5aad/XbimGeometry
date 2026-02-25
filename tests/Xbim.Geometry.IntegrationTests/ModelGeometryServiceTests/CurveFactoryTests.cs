
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Tests
{

    public class CurveFactoryTests
    {

        #region Setup

        readonly ILoggerFactory _loggerFactory;
        private readonly IXbimGeometryServicesFactory factory;

        public CurveFactoryTests(IXbimGeometryServicesFactory factory, ILoggerFactory loggerFactory)
        {
            this._loggerFactory = loggerFactory;
            this.factory = factory;
        }

        #endregion

        #region Composite Curves

        [Fact]
        public void Can_convert_ifc_composite_curve_with_cartesian_preferred_trim()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/composite_curve_with_cartesian_preferred_trim.ifc"))
            {
                var cc = model.Instances.OfType<IIfcCompositeCurve>().FirstOrDefault();
                cc.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngineV6(model, _loggerFactory);
                var compositeCurve =geomEngine.WireFactory.Build(cc);
                compositeCurve.Should().NotBeNull();
                compositeCurve.Length.Should().BeApproximately(4.866638, 1e-5);

            }
        }
        #endregion

        #region Polylines
        [Fact]
        public void Can_convert_polyline_with_very_close_points()
        {
            using (var model = MemoryModel.OpenRead(@"TestFiles/polyline_with_very_close_points.ifc"))
            {
                var ifcPolyline = model.Instances.OfType<IIfcPolyline>().FirstOrDefault();
                ifcPolyline.Should().NotBeNull();
                var geomEngine = factory.CreateGeometryEngineV6(model, _loggerFactory);
                var polyline = geomEngine.WireFactory.Build(ifcPolyline);
                polyline.Should().NotBeNull();
                polyline.EdgeLoop.Length.Should().Be(ifcPolyline.Points.Count -2); ;

            }
        }

        #endregion
    }
}
