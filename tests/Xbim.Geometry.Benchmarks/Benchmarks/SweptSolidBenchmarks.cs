using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

[Config(typeof(GeometryBenchmarkConfig))]
[BenchmarkCategory("SweptSolids")]
public class SweptSolidBenchmarks
{
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }

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
        _sweptDiskEngine = EngineSetup.CreateEngine(_sweptDiskModel, Version);
        _sweptDisk = _sweptDiskModel.Instances.OfType<IIfcSweptDiskSolid>().First();

        _revolvedModel = EngineSetup.OpenModel("Ifc4TestFiles/beam-revolved-solid-tapered.ifc");
        _revolvedEngine = EngineSetup.CreateEngine(_revolvedModel, Version);
        _revolvedSolid = _revolvedModel.Instances.OfType<IIfcRevolvedAreaSolid>().First();

        _surfaceCurveModel = EngineSetup.OpenModel("SurfaceCurveSweptAreaSolid_1.ifc");
        _surfaceCurveEngine = EngineSetup.CreateEngine(_surfaceCurveModel, Version);
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
