using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

[Config(typeof(GeometryBenchmarkConfig))]
[BenchmarkCategory("CSG")]
public class CsgBenchmarks
{
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }

    private MemoryModel _csgPrimitiveModel = null!;
    private MemoryModel _csgSolidModel = null!;
    private IXbimGeometryEngine _csgPrimitiveEngine = null!;
    private IXbimGeometryEngine _csgSolidEngine = null!;
    private IIfcCsgPrimitive3D _csgPrimitive = null!;
    private IIfcCsgSolid _csgSolid = null!;

    [GlobalSetup]
    public void Setup()
    {
        _csgPrimitiveModel = EngineSetup.OpenModel("IfcExamples/csg-primitive.ifc");
        _csgPrimitiveEngine = EngineSetup.CreateEngine(_csgPrimitiveModel, Version);
        _csgPrimitive = _csgPrimitiveModel.Instances.OfType<IIfcCsgPrimitive3D>().First();

        _csgSolidModel = EngineSetup.OpenModel("CsgSolidIsValidSolidTest.ifc");
        _csgSolidEngine = EngineSetup.CreateEngine(_csgSolidModel, Version);
        _csgSolid = _csgSolidModel.Instances.OfType<IIfcCsgSolid>().First();
    }

    [Benchmark(Description = "CsgPrimitive3D")]
    public IXbimSolid CsgPrimitive()
    {
        return _csgPrimitiveEngine.CreateSolid(_csgPrimitive, null);
    }

    [Benchmark(Description = "CsgSolid")]
    public IXbimSolidSet CsgSolid()
    {
        return _csgSolidEngine.CreateSolidSet(_csgSolid, null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _csgPrimitiveModel?.Dispose();
        _csgSolidModel?.Dispose();
    }
}
