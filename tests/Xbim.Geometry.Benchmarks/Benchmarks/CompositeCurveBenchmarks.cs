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
[BenchmarkCategory("CompositeCurves")]
public class CompositeCurveBenchmarks
{
#if OLD_ENGINE
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }
#endif

    private MemoryModel _compositeCurveModel = null!;
    private MemoryModel _compositeCurve2Model = null!;
    private IXbimGeometryEngine _compositeCurveEngine = null!;
    private IXbimGeometryEngine _compositeCurve2Engine = null!;
    private IIfcCompositeCurve _compositeCurve = null!;
    private IIfcCompositeCurve _compositeCurve2 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _compositeCurveModel = EngineSetup.OpenModel("Ifc4TestFiles/composite-curve.ifc");
#if OLD_ENGINE
        _compositeCurveEngine = EngineSetup.CreateEngine(_compositeCurveModel, Version);
#else
        _compositeCurveEngine = EngineSetup.CreateEngine(_compositeCurveModel);
#endif
        _compositeCurve = _compositeCurveModel.Instances.OfType<IIfcCompositeCurve>().First();

        _compositeCurve2Model = EngineSetup.OpenModel("Ifc4TestFiles/composite-curve2.ifc");
#if OLD_ENGINE
        _compositeCurve2Engine = EngineSetup.CreateEngine(_compositeCurve2Model, Version);
#else
        _compositeCurve2Engine = EngineSetup.CreateEngine(_compositeCurve2Model);
#endif
        _compositeCurve2 = _compositeCurve2Model.Instances.OfType<IIfcCompositeCurve>().First();
    }

    [Benchmark(Description = "CompositeCurve")]
    public IXbimCurve CompositeCurve()
    {
        return _compositeCurveEngine.CreateCurve(_compositeCurve as IIfcCurve, null);
    }

    [Benchmark(Description = "CompositeCurve (variant 2)")]
    public IXbimCurve CompositeCurve2()
    {
        return _compositeCurve2Engine.CreateCurve(_compositeCurve2 as IIfcCurve, null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _compositeCurveModel?.Dispose();
        _compositeCurve2Model?.Dispose();
    }
}
