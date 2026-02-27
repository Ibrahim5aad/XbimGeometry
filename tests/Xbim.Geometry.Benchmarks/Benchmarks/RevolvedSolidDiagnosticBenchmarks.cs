using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Geometry.Engine;
using Xbim.Geometry.Engine.Factories;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

/// <summary>
/// Diagnostic benchmark that splits RevolvedAreaSolidTapered timing into:
///  - ProfilesOnly:  Build start + end profile faces (no revolution)
///  - NativeOnly:    Call native revolved tapered with pre-built faces
///  - Full:          Complete end-to-end (for comparison)
/// </summary>
[Config(typeof(GeometryBenchmarkConfig))]
[BenchmarkCategory("SweptSolids")]
public class RevolvedSolidDiagnosticBenchmarks
{
    private MemoryModel _model = null!;
    private XbimGeometryEngine _engine = null!;
    private ProfileFactory _profileFactory = null!;
    private IIfcRevolvedAreaSolidTapered _revolvedTapered = null!;

    // Pre-built data for isolated native-only benchmark
    private NativeShapeHandle _preBuiltStartFace = null!;
    private NativeShapeHandle _preBuiltEndFace = null!;
    private double _axisOriginX, _axisOriginY, _axisOriginZ;
    private double _axisDirX, _axisDirY, _axisDirZ;
    private double _angleRadians;
    private NativeLocationHandle _locationHandle = null!;
    private NativeContextHandle _contextHandle = null!;

    [GlobalSetup]
    public void Setup()
    {
        _model = EngineSetup.OpenModel("Ifc4TestFiles/beam-revolved-solid-tapered.ifc");
        _engine = (XbimGeometryEngine)EngineSetup.CreateEngine(_model);
        _profileFactory = (ProfileFactory)_engine.ModelService.ProfileFactory;
        _revolvedTapered = _model.Instances.OfType<IIfcRevolvedAreaSolidTapered>().First();
        _contextHandle = ((ModelGeometryService)_engine.ModelService).ContextHandle;

        // Pre-build both profile faces
        var startFace = (XbimFace)_profileFactory.BuildFace(_revolvedTapered.SweptArea);
        var endFace = (XbimFace)_profileFactory.BuildFace(_revolvedTapered.EndSweptArea);
        _preBuiltStartFace = startFace.Handle;
        _preBuiltEndFace = endFace.Handle;

        // Pre-extract axis
        var axisPoint = GeometryFactory.BuildPoint3d(_revolvedTapered.Axis.Location);
        _axisOriginX = axisPoint.X;
        _axisOriginY = axisPoint.Y;
        _axisOriginZ = axisPoint.Z;

        GeometryFactory.BuildDirection3d(_revolvedTapered.Axis.Axis,
            out _axisDirX, out _axisDirY, out _axisDirZ);

        _angleRadians = _revolvedTapered.Angle *
            ((ModelGeometryService)_engine.ModelService).RadianFactor;

        // Pre-build location
        if (_revolvedTapered.Position != null)
        {
            _locationHandle = ((GeometryFactory)_engine.ModelService.GeometryFactory)
                .BuildLocationFromAxis3D(_revolvedTapered.Position).Handle;
        }
        else
        {
            _locationHandle = NativeLocationHandle.NullHandle;
        }
    }

    [Benchmark(Description = "Profiles only (start + end)")]
    public (IXFace, IXFace) ProfilesOnly()
    {
        var start = _profileFactory.BuildFace(_revolvedTapered.SweptArea);
        var end = _profileFactory.BuildFace(_revolvedTapered.EndSweptArea);
        return (start, end);
    }

    [Benchmark(Description = "Native only (pre-built faces)")]
    public int NativeOnly()
    {
        int result = XbimGeometryNativeApi.xbim_solid_build_revolved_tapered(
            _contextHandle,
            _preBuiltStartFace,
            _preBuiltEndFace,
            _axisOriginX, _axisOriginY, _axisOriginZ,
            _axisDirX, _axisDirY, _axisDirZ,
            _angleRadians,
            _locationHandle,
            out var outHandle);
        outHandle?.Dispose();
        return result;
    }

    [Benchmark(Description = "Full end-to-end (tapered)")]
    public IXbimSolid Full()
    {
        return _engine.CreateSolid((IIfcRevolvedAreaSolid)_revolvedTapered, null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _model?.Dispose();
    }
}
