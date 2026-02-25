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
[BenchmarkCategory("SweptSolids")]
public class SweptSolidBenchmarks
{
#if OLD_ENGINE
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }
#endif

    private MemoryModel _sweptDiskModel = null!;
    private MemoryModel _revolvedModel = null!;
    private MemoryModel _surfaceCurveModel = null!;
    private IXbimGeometryEngine _sweptDiskEngine = null!;
    private IXbimGeometryEngine _revolvedEngine = null!;
    private IXbimGeometryEngine _surfaceCurveEngine = null!;
    private IIfcSweptDiskSolid _sweptDisk = null!;
    private IIfcRevolvedAreaSolid _revolvedSolid = null!;
    private IIfcSurfaceCurveSweptAreaSolid _surfaceCurveSwept = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sweptDiskModel = EngineSetup.OpenModel("SweptDiskSolid_1.ifc");
#if OLD_ENGINE
        _sweptDiskEngine = EngineSetup.CreateEngine(_sweptDiskModel, Version);
#else
        _sweptDiskEngine = EngineSetup.CreateEngine(_sweptDiskModel);
#endif
        _sweptDisk = _sweptDiskModel.Instances.OfType<IIfcSweptDiskSolid>().First();

        _revolvedModel = EngineSetup.OpenModel("Ifc4TestFiles/beam-revolved-solid-tapered.ifc");
#if OLD_ENGINE
        _revolvedEngine = EngineSetup.CreateEngine(_revolvedModel, Version);
#else
        _revolvedEngine = EngineSetup.CreateEngine(_revolvedModel);
#endif
        _revolvedSolid = _revolvedModel.Instances.OfType<IIfcRevolvedAreaSolid>().First();

        _surfaceCurveModel = EngineSetup.OpenModel("SurfaceCurveSweptAreaSolid_1.ifc");
#if OLD_ENGINE
        _surfaceCurveEngine = EngineSetup.CreateEngine(_surfaceCurveModel, Version);
#else
        _surfaceCurveEngine = EngineSetup.CreateEngine(_surfaceCurveModel);
#endif
        _surfaceCurveSwept = _surfaceCurveModel.Instances.OfType<IIfcSurfaceCurveSweptAreaSolid>().First();
    }

    [Benchmark(Description = "SweptDiskSolid")]
    public IXbimSolid SweptDiskSolid()
    {
        return _sweptDiskEngine.CreateSolid(_sweptDisk, null);
    }

    [Benchmark(Description = "RevolvedAreaSolid")]
    public IXbimSolid RevolvedAreaSolid()
    {
        return _revolvedEngine.CreateSolid(_revolvedSolid, null);
    }

    [Benchmark(Description = "SurfaceCurveSweptAreaSolid")]
    public IXbimSolid SurfaceCurveSweptAreaSolid()
    {
        return _surfaceCurveEngine.CreateSolid(_surfaceCurveSwept, null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sweptDiskModel?.Dispose();
        _revolvedModel?.Dispose();
        _surfaceCurveModel?.Dispose();
    }
}
