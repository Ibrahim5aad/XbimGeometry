using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

[Config(typeof(GeometryBenchmarkConfig))]
[BenchmarkCategory("ExtrudedSolids")]
public class ExtrudedSolidBenchmarks
{
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }

    private MemoryModel _extrudedModel = null!;
    private MemoryModel _compositeCurveModel = null!;
    private IXbimGeometryEngine _extrudedEngine = null!;
    private IXbimGeometryEngine _compositeCurveEngine = null!;
    private IIfcExtrudedAreaSolid _rectangleExtrusion = null!;
    private IIfcExtrudedAreaSolid _compositeCurveExtrusion = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Simple rectangular profile extrusion
        _extrudedModel = EngineSetup.OpenModel("Ifc4TestFiles/extruded-solid.ifc");
        _extrudedEngine = EngineSetup.CreateEngine(_extrudedModel, Version);
        _rectangleExtrusion = _extrudedModel.Instances.OfType<IIfcExtrudedAreaSolid>().First();

        // Composite curve profile extrusion (more complex profile)
        _compositeCurveModel = EngineSetup.OpenModel("Ifc4TestFiles/composite-curve.ifc");
        _compositeCurveEngine = EngineSetup.CreateEngine(_compositeCurveModel, Version);
        _compositeCurveExtrusion = _compositeCurveModel.Instances.OfType<IIfcExtrudedAreaSolid>().First();
    }

    [Benchmark(Description = "ExtrudedAreaSolid (rectangle profile)")]
    public IXbimSolid ExtrudedAreaSolid_Rectangle()
    {
        return _extrudedEngine.CreateSolid(_rectangleExtrusion, null);
    }

    [Benchmark(Description = "ExtrudedAreaSolid (composite curve profile)")]
    public IXbimSolid ExtrudedAreaSolid_CompositeCurve()
    {
        return _compositeCurveEngine.CreateSolid(_compositeCurveExtrusion, null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _extrudedModel?.Dispose();
        _compositeCurveModel?.Dispose();
    }
}
