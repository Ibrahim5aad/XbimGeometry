using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

[Config(typeof(GeometryBenchmarkConfig))]
[BenchmarkCategory("BooleanOperations")]
public class BooleanOperationBenchmarks
{
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }

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
        _simpleClipEngine = EngineSetup.CreateEngine(_simpleClipModel, Version);
        _simpleClip = _simpleClipModel.Instances.OfType<IIfcBooleanClippingResult>().First();

        _nestedModel = EngineSetup.OpenModel("NestedBooleansTest.ifc");
        _nestedEngine = EngineSetup.CreateEngine(_nestedModel, Version);
        _nestedBoolean = _nestedModel.Instances.OfType<IIfcBooleanResult>().First();

        _complexModel = EngineSetup.OpenModel("ComplexNestedBooleanResult.ifc");
        _complexEngine = EngineSetup.CreateEngine(_complexModel, Version);
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
