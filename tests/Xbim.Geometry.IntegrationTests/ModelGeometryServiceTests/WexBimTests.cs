
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.WexBim;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Tests
{
    public class WexBimTests
    {
        private readonly IXGeometryConverterFactory _geomConverterFactory;
        private readonly ILoggerFactory _loggerFactory;

        public WexBimTests(IXGeometryConverterFactory geomConverterFactory, ILoggerFactory loggerFactory)
        {
            _geomConverterFactory = geomConverterFactory;
            _loggerFactory = loggerFactory;
        }

        [Fact]
        public void Can_read_and_write_faceted_brep_to_wexbim_V5_and_V6()
        {
            using var mm = MemoryModel.OpenRead("testfiles/FacetedBrepForSmoothing.ifc");
            var geomEngineV6 = _geomConverterFactory.CreateGeometryEngineV6(mm, _loggerFactory);
            var geomEngineV5 = _geomConverterFactory.CreateGeometryEngineV5(mm, _loggerFactory);
            var ifcFacetedBrep = mm.Instances[32] as IIfcFacetedBrep;
            var facetedBrepV6 = geomEngineV6.Build(ifcFacetedBrep);
            var meshFactors = geomEngineV6.MeshFactors.SetGranularity(MeshGranularity.Fine);
            var bytesV6 = geomEngineV6.WexBimMeshFactory.CreateWexBimMesh(facetedBrepV6, out IXAxisAlignedBoundingBox bounds);
            var wexBimV6 = new WexBimMesh(bytesV6);
            var facetedBrepV5 = geomEngineV5.Create(ifcFacetedBrep);
            var shapeGeom = geomEngineV5.CreateShapeGeometry(mm.ModelFactors.OneMilliMeter, facetedBrepV5, mm.ModelFactors.Precision, null);
            var wexBimV5 = new WexBimMesh(shapeGeom.ToByteArray());
            wexBimV5.FaceCount.Should().Be(wexBimV6.FaceCount);
            wexBimV5.TriangleCount.Should().Be(wexBimV6.TriangleCount);
            wexBimV5.VertexCount.Should().Be(wexBimV6.VertexCount);
        }
    }
}
