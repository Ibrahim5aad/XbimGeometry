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
[BenchmarkCategory("AdvancedBreps")]
public class AdvancedBrepBenchmarks
{
#if OLD_ENGINE
    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }
#endif

    private MemoryModel _brep1Model = null!;
    private MemoryModel _cubeBrepModel = null!;
    private MemoryModel _facetedBrepModel = null!;
    private IXbimGeometryEngine _brep1Engine = null!;
    private IXbimGeometryEngine _cubeBrepEngine = null!;
    private IXbimGeometryEngine _facetedBrepEngine = null!;
    private IIfcAdvancedBrep _advancedBrep1 = null!;
    private IIfcAdvancedBrep _cubeBrep = null!;
    private IIfcFacetedBrep _facetedBrep = null!;

    [GlobalSetup]
    public void Setup()
    {
        _brep1Model = EngineSetup.OpenModel("advanced_brep_1.ifc");
#if OLD_ENGINE
        _brep1Engine = EngineSetup.CreateEngine(_brep1Model, Version);
#else
        _brep1Engine = EngineSetup.CreateEngine(_brep1Model);
#endif
        _advancedBrep1 = _brep1Model.Instances.OfType<IIfcAdvancedBrep>().First();

        _cubeBrepModel = EngineSetup.OpenModel("Ifc4TestFiles/cube-advanced-brep.ifc");
#if OLD_ENGINE
        _cubeBrepEngine = EngineSetup.CreateEngine(_cubeBrepModel, Version);
#else
        _cubeBrepEngine = EngineSetup.CreateEngine(_cubeBrepModel);
#endif
        _cubeBrep = _cubeBrepModel.Instances.OfType<IIfcAdvancedBrep>().First();

        _facetedBrepModel = EngineSetup.OpenModel("FacetedBrepIsValidSolidTest.ifc");
#if OLD_ENGINE
        _facetedBrepEngine = EngineSetup.CreateEngine(_facetedBrepModel, Version);
#else
        _facetedBrepEngine = EngineSetup.CreateEngine(_facetedBrepModel);
#endif
        _facetedBrep = _facetedBrepModel.Instances.OfType<IIfcFacetedBrep>().First();
    }

    [Benchmark(Description = "AdvancedBrep (complex)")]
    public IXbimSolidSet AdvancedBrep_Complex()
    {
        return _brep1Engine.CreateSolidSet(_advancedBrep1 as IIfcManifoldSolidBrep, null);
    }

    [Benchmark(Description = "AdvancedBrep (cube)")]
    public IXbimSolid AdvancedBrep_Cube()
    {
        return _cubeBrepEngine.CreateSolid(_cubeBrep, null);
    }

    [Benchmark(Description = "FacetedBrep")]
    public IXbimSolidSet FacetedBrep()
    {
        return _facetedBrepEngine.CreateSolidSet(_facetedBrep, null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _brep1Model?.Dispose();
        _cubeBrepModel?.Dispose();
        _facetedBrepModel?.Dispose();
    }
}
