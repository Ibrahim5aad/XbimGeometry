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
[BenchmarkCategory("BooleanOperations")]
public class BooleanOperationBenchmarks
{
#if OLD_ENGINE
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }
#endif

    private MemoryModel _simpleClipModel = null!;
    private MemoryModel _nestedModel = null!;
    private MemoryModel _complexModel = null!;
    private IXbimGeometryEngine _simpleClipEngine = null!;
    private IXbimGeometryEngine _nestedEngine = null!;
    private IXbimGeometryEngine _complexEngine = null!;
    private IIfcBooleanClippingResult _simpleClip = null!;
    private IIfcBooleanResult _nestedBoolean = null!;
    private IIfcBooleanResult _complexBoolean = null!;

    [GlobalSetup]
    public void Setup()
    {
        _simpleClipModel = EngineSetup.OpenModel("SimpleBooleanClipResultTest.ifc");
#if OLD_ENGINE
        _simpleClipEngine = EngineSetup.CreateEngine(_simpleClipModel, Version);
#else
        _simpleClipEngine = EngineSetup.CreateEngine(_simpleClipModel);
#endif
        _simpleClip = _simpleClipModel.Instances.OfType<IIfcBooleanClippingResult>().First();

        _nestedModel = EngineSetup.OpenModel("NestedBooleansTest.ifc");
#if OLD_ENGINE
        _nestedEngine = EngineSetup.CreateEngine(_nestedModel, Version);
#else
        _nestedEngine = EngineSetup.CreateEngine(_nestedModel);
#endif
        _nestedBoolean = _nestedModel.Instances.OfType<IIfcBooleanResult>().First();

        _complexModel = EngineSetup.OpenModel("ComplexNestedBooleanResult.ifc");
#if OLD_ENGINE
        _complexEngine = EngineSetup.CreateEngine(_complexModel, Version);
#else
        _complexEngine = EngineSetup.CreateEngine(_complexModel);
#endif
        _complexBoolean = _complexModel.Instances.OfType<IIfcBooleanResult>().First();
    }

    [Benchmark(Description = "Boolean simple clip")]
    public IXbimSolidSet BooleanSimpleClip()
    {
        return _simpleClipEngine.CreateSolidSet(_simpleClip, null);
    }

    [Benchmark(Description = "Boolean nested")]
    public IXbimSolidSet BooleanNested()
    {
        return _nestedEngine.CreateSolidSet(_nestedBoolean, null);
    }

    [Benchmark(Description = "Boolean complex nested")]
    public IXbimSolidSet BooleanComplexNested()
    {
        return _complexEngine.CreateSolidSet(_complexBoolean, null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _simpleClipModel?.Dispose();
        _nestedModel?.Dispose();
        _complexModel?.Dispose();
    }
}
