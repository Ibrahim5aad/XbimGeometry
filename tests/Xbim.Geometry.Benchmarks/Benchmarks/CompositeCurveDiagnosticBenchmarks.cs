using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Xbim.Common.Geometry;
#if OLD_ENGINE
using Xbim.Geometry.Abstractions;
#endif
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Geometry.Engine;
using Xbim.Geometry.Engine.Factories;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using static Xbim.Geometry.Engine.Factories.CurveFactory;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

/// <summary>
/// Diagnostic benchmark that splits composite curve timing into:
///  - MarshalOnly: IFC entity traversal + managed array building (no native call)
///  - NativeOnly:  P/Invoke call with pre-marshalled arrays (no IFC access)
///  - Full:        Complete end-to-end (for comparison)
/// </summary>
[Config(typeof(GeometryBenchmarkConfig))]
[BenchmarkCategory("CompositeCurves")]
public class CompositeCurveDiagnosticBenchmarks
{
    private MemoryModel _model = null!;
    private CurveFactory _curveFactory = null!;
    private IIfcCompositeCurve _curve = null!;
    private CompositeCurveMarshalledData _preMarshalledData;

    private MemoryModel _model2 = null!;
    private CurveFactory _curveFactory2 = null!;
    private IIfcCompositeCurve _curve2 = null!;
    private CompositeCurveMarshalledData _preMarshalledData2;

    [GlobalSetup]
    public void Setup()
    {
        _model = EngineSetup.OpenModel("Ifc4TestFiles/composite-curve.ifc");
#if OLD_ENGINE
        var engine = (XbimGeometryEngine)EngineSetup.CreateEngine(_model, XGeometryEngineVersion.V6);
#else
        var engine = (XbimGeometryEngine)EngineSetup.CreateEngine(_model);
#endif
        _curveFactory = (CurveFactory)engine.ModelService.CurveFactory;
        _curve = _model.Instances.OfType<IIfcCompositeCurve>().First();
        _preMarshalledData = _curveFactory.MarshalCompositeCurve(_curve);

        _model2 = EngineSetup.OpenModel("Ifc4TestFiles/composite-curve2.ifc");
#if OLD_ENGINE
        var engine2 = (XbimGeometryEngine)EngineSetup.CreateEngine(_model2, XGeometryEngineVersion.V6);
#else
        var engine2 = (XbimGeometryEngine)EngineSetup.CreateEngine(_model2);
#endif
        _curveFactory2 = (CurveFactory)engine2.ModelService.CurveFactory;
        _curve2 = _model2.Instances.OfType<IIfcCompositeCurve>().First();
        _preMarshalledData2 = _curveFactory2.MarshalCompositeCurve(_curve2);
    }

    [Benchmark(Description = "Marshal only")]
    public int MarshalOnly()
    {
        var d = _curveFactory.MarshalCompositeCurve(_curve);
        return d.Types.Length; // consume to prevent dead code elimination
    }

    [Benchmark(Description = "Native only")]
    public void NativeOnly()
    {
        using var handle = _curveFactory.BuildCompositeFromArrays(_preMarshalledData);
    }

    [Benchmark(Description = "Full (end-to-end)")]
    public IXbimCurve Full()
    {
        return (IXbimCurve)_curveFactory.Build(_curve);
    }

    [Benchmark(Description = "Marshal only (v2)")]
    public int MarshalOnly2()
    {
        var d = _curveFactory2.MarshalCompositeCurve(_curve2);
        return d.Types.Length;
    }

    [Benchmark(Description = "Native only (v2)")]
    public void NativeOnly2()
    {
        using var handle = _curveFactory2.BuildCompositeFromArrays(_preMarshalledData2);
    }

    [Benchmark(Description = "Full (v2, end-to-end)")]
    public IXbimCurve Full2()
    {
        return (IXbimCurve)_curveFactory2.Build(_curve2);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _model?.Dispose();
        _model2?.Dispose();
    }
}
