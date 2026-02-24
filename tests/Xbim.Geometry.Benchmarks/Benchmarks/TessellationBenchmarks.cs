using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

[Config(typeof(GeometryBenchmarkConfig))]
[BenchmarkCategory("Tessellation")]
public class TessellationBenchmarks
{
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }

    private MemoryModel _triangulatedModel = null!;
    private MemoryModel _polygonalModel = null!;
    private MemoryModel _tessellatedBeamModel = null!;
    private IXbimGeometryEngine _triangulatedEngine = null!;
    private IXbimGeometryEngine _polygonalEngine = null!;
    private IXbimGeometryEngine _tessellatedBeamEngine = null!;
    private IIfcTriangulatedFaceSet _triangulatedFaceSet = null!;
    private IIfcPolygonalFaceSet _polygonalFaceSet = null!;
    private IIfcTriangulatedFaceSet _tessellatedBeam = null!;

    [GlobalSetup]
    public void Setup()
    {
        _triangulatedModel = EngineSetup.OpenModel("TriangulatedFaceSetBasicTest.ifc");
        _triangulatedEngine = EngineSetup.CreateEngine(_triangulatedModel, Version);
        _triangulatedFaceSet = _triangulatedModel.Instances.OfType<IIfcTriangulatedFaceSet>().First();

        _polygonalModel = EngineSetup.OpenModel("Ifc4TestFiles/polygonal-face-tessellation.ifc");
        _polygonalEngine = EngineSetup.CreateEngine(_polygonalModel, Version);
        _polygonalFaceSet = _polygonalModel.Instances.OfType<IIfcPolygonalFaceSet>().First();

        _tessellatedBeamModel = EngineSetup.OpenModel("Ifc4TestFiles/beam-straight-i-shape-tessellated.ifc");
        _tessellatedBeamEngine = EngineSetup.CreateEngine(_tessellatedBeamModel, Version);
        _tessellatedBeam = _tessellatedBeamModel.Instances.OfType<IIfcTriangulatedFaceSet>().First();
    }

    [Benchmark(Description = "TriangulatedFaceSet (basic)")]
    public IXbimSolidSet TriangulatedFaceSet_Basic()
    {
        return _triangulatedEngine.CreateSolidSet(_triangulatedFaceSet, null);
    }

    [Benchmark(Description = "PolygonalFaceSet")]
    public IXbimSolidSet PolygonalFaceSet()
    {
        return _polygonalEngine.CreateSolidSet(_polygonalFaceSet, null);
    }

    [Benchmark(Description = "TriangulatedFaceSet (beam)")]
    public IXbimSolidSet TriangulatedFaceSet_Beam()
    {
        return _tessellatedBeamEngine.CreateSolidSet(_tessellatedBeam, null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _triangulatedModel?.Dispose();
        _polygonalModel?.Dispose();
        _tessellatedBeamModel?.Dispose();
    }
}
