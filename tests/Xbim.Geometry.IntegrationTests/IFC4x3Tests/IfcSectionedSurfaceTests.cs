using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine;
using Xbim.Ifc4x3;
using Xbim.Ifc4x3.GeometricModelResource;
using Xbim.Ifc4x3.GeometryResource;
using Xbim.Ifc4x3.MeasureResource;
using Xbim.Ifc4x3.ProfileResource;
using Xbim.IO.Memory;
using Xunit;
using ILoggerFactory = Microsoft.Extensions.Logging.ILoggerFactory;

namespace Xbim.Geometry.Engine.Tests.IFC4x3Tests
{
    public class IfcSectionedSurfaceTests
    {
        private readonly IXGeometryConverterFactory _factory;
        private readonly ILoggerFactory _loggerFactory;

        public IfcSectionedSurfaceTests(IXGeometryConverterFactory factory, ILoggerFactory loggerFactory)
        {
            _factory = factory;
            _loggerFactory = loggerFactory;
        }

        [Fact]
        public void CanBuildIfcSectionedSurface()
        {
            // Arrange
            using MemoryModel model = new MemoryModel(new EntityFactoryIfc4x3Add2());
            using var txn = model.BeginTransaction(nameof(CanBuildIfcSectionedSurface));
            var surface = BuildIfcSectionedSurface(model);
            var modelSvc = _factory.CreateModelGeometryService(model, _loggerFactory);

            // Act
            var xSurface = modelSvc.SurfaceFactory.Build(surface) as IXSectionedSurface;

            // Assert
            xSurface.BrepString().Should().NotBeNull();
        }

        /// <summary>
        /// Builds a road-like IfcSectionedSurface: a straight directrix along Z
        /// with varying open cross-section profiles placed at regular stations.
        /// </summary>
        public static IfcSectionedSurface BuildIfcSectionedSurface(MemoryModel model)
        {
            // Directrix: line along Z axis
            var directrix = model.Instances.New<IfcLine>(l =>
            {
                l.Pnt = model.Instances.New<IfcCartesianPoint>(p => { p.X = 0; p.Y = 0; p.Z = 0; });
                l.Dir = model.Instances.New<IfcVector>(v =>
                {
                    v.Orientation = model.Instances.New<IfcDirection>(d => { d.X = 0; d.Y = 0; d.Z = 1; });
                    v.Magnitude = 1;
                });
            });

            // Cross sections — varying profiles with tagged points for non-uniform alignment
            var crossSections = new List<IfcOpenCrossProfileDef>
            {
                // Station 0: 4-point profile (3 segments)
                CreateOpenProfile(model, new[] { 2.0, 3.0, 2.0 }, new[] { 0.5, 0.0, -0.5 },
                    new[] { "P1", "P2", "P3", "P4" }),

                // Station 10: 3-point profile (2 segments) — missing P3 tag
                CreateOpenProfile(model, new[] { 3.5, 3.5 }, new[] { 0.5, -0.5 },
                    new[] { "P1", "P2", "P4" }),

                // Station 20: 3-point profile (2 segments) — missing P3 tag
                CreateOpenProfile(model, new[] { 3.5, 3.5 }, new[] { 0.5, -0.5 },
                    new[] { "P1", "P2", "P4" }),

                // Station 40: 4-point profile with zero-width middle segment
                CreateOpenProfile(model, new[] { 3.5, 0.0, 3.5 }, new[] { 0.5, 0.0, -0.5 },
                    new[] { "P1", "P2", "P3", "P4" }),

                // Station 50: standard 4-point profile
                CreateOpenProfile(model, new[] { 2.0, 4.0, 2.0 }, new[] { 0.5, 0.0, -0.5 },
                    new[] { "P1", "P2", "P3", "P4" }),

                // Station 60: narrower 4-point profile
                CreateOpenProfile(model, new[] { 2.0, 2.0, 2.0 }, new[] { 0.5, 0.0, -0.5 },
                    new[] { "P1", "P2", "P3", "P4" }),

                // Station 70: standard 4-point profile
                CreateOpenProfile(model, new[] { 2.0, 3.0, 2.0 }, new[] { 0.5, 0.0, -0.5 },
                    new[] { "P1", "P2", "P3", "P4" }),
            };

            // Section positions along the directrix.
            // BasisCurve = directrix, DistanceAlong = arc length (IfcLengthMeasure).
            // Axis = (0,1,0) (up), RefDirection = (1,0,0) (lateral).
            double[] stations = { 0, 10, 20, 40, 50, 60, 70 };
            var sectionPositions = new List<IfcAxis2PlacementLinear>();
            foreach (double station in stations)
            {
                sectionPositions.Add(CreateSectionPosition(model, directrix, station));
            }

            // Assemble the IfcSectionedSurface
            var sectionedSurface = model.Instances.New<IfcSectionedSurface>(surface =>
            {
                surface.Directrix = directrix;
                surface.CrossSections.AddRange(crossSections);
                surface.CrossSectionPositions.AddRange(sectionPositions);
            });

            return sectionedSurface;
        }

        private static IfcOpenCrossProfileDef CreateOpenProfile(
            MemoryModel model, double[] widths, double[] slopes, string[] tags)
        {
            return model.Instances.New<IfcOpenCrossProfileDef>(profile =>
            {
                profile.ProfileName = "OpenCrossProfile";
                profile.ProfileType = IfcProfileTypeEnum.CURVE;
                profile.HorizontalWidths = true;

                foreach (double w in widths)
                    profile.Widths.Add(new IfcNonNegativeLengthMeasure(w));
                foreach (double s in slopes)
                    profile.Slopes.Add(new IfcPlaneAngleMeasure(s));
                foreach (string t in tags)
                    profile.Tags.Add(new IfcLabel(t));
            });
        }

        private static IfcAxis2PlacementLinear CreateSectionPosition(
            MemoryModel model, IfcCurve directrix, double distanceAlong)
        {
            return model.Instances.New<IfcAxis2PlacementLinear>(placement =>
            {
                placement.Location = model.Instances.New<IfcPointByDistanceExpression>(pt =>
                {
                    pt.DistanceAlong = new IfcLengthMeasure(distanceAlong);
                    pt.BasisCurve = directrix;
                });

                // RefDirection = lateral (X)
                placement.RefDirection = model.Instances.New<IfcDirection>(d =>
                {
                    d.X = 1; d.Y = 0; d.Z = 0;
                });

                // Axis = surface normal (Z up) so Y_local = Z×X = (0,1,0) carries slopes vertically
                placement.Axis = model.Instances.New<IfcDirection>(d =>
                {
                    d.X = 0; d.Y = 0; d.Z = 1;
                });
            });
        }
    }
}
