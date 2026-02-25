using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Xbim.Common.Geometry;
#if OLD_ENGINE
using Xbim.Geometry.Abstractions;
#endif
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

[Config(typeof(GeometryBenchmarkConfig))]
[BenchmarkCategory("Tessellation")]
public class TessellationBenchmarks
{
#if OLD_ENGINE
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }
#endif

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
#if OLD_ENGINE
        _triangulatedEngine = EngineSetup.CreateEngine(_triangulatedModel, Version);
#else
        _triangulatedEngine = EngineSetup.CreateEngine(_triangulatedModel);
#endif
        _triangulatedFaceSet = _triangulatedModel.Instances.OfType<IIfcTriangulatedFaceSet>().First();

        _polygonalModel = EngineSetup.OpenModel("Ifc4TestFiles/polygonal-face-tessellation.ifc");
#if OLD_ENGINE
        _polygonalEngine = EngineSetup.CreateEngine(_polygonalModel, Version);
#else
        _polygonalEngine = EngineSetup.CreateEngine(_polygonalModel);
#endif
        _polygonalFaceSet = _polygonalModel.Instances.OfType<IIfcPolygonalFaceSet>().First();

        _tessellatedBeamModel = EngineSetup.OpenModel("Ifc4TestFiles/beam-straight-i-shape-tessellated.ifc");
#if OLD_ENGINE
        _tessellatedBeamEngine = EngineSetup.CreateEngine(_tessellatedBeamModel, Version);
#else
        _tessellatedBeamEngine = EngineSetup.CreateEngine(_tessellatedBeamModel);
#endif
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
