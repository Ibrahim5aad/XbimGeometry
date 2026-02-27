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
/// Diagnostic benchmark that splits ExtrudedAreaSolid timing into:
///  - ProfileOnly:  Build the 2D profile face (no extrusion)
///  - ExtrudeOnly:  Extrude a pre-built face (no profile building)
///  - Full:         Complete end-to-end (for comparison)
/// </summary>
[Config(typeof(GeometryBenchmarkConfig))]
[BenchmarkCategory("ExtrudedSolids")]
public class ExtrudedSolidDiagnosticBenchmarks
{
    private MemoryModel _model = null!;
    private XbimGeometryEngine _engine = null!;
    private ProfileFactory _profileFactory = null!;
    private SolidFactory _solidFactory = null!;
    private IIfcExtrudedAreaSolid _extrusion = null!;

    // Pre-built data for isolated extrude-only benchmark
    private NativeShapeHandle _preBuiltFaceHandle = null!;
    private double _dirX, _dirY, _dirZ;
    private double _depth;
    private NativeLocationHandle _locationHandle = null!;
    private NativeContextHandle _contextHandle = null!;

    [GlobalSetup]
    public void Setup()
    {
        _model = EngineSetup.OpenModel("Ifc4TestFiles/extruded-solid.ifc");
        _engine = (XbimGeometryEngine)EngineSetup.CreateEngine(_model);
        _profileFactory = (ProfileFactory)_engine.ModelService.ProfileFactory;
        _solidFactory = (SolidFactory)_engine.ModelService.SolidFactory;
        _extrusion = _model.Instances.OfType<IIfcExtrudedAreaSolid>().First();
        _contextHandle = ((ModelGeometryService)_engine.ModelService).ContextHandle;

        // Pre-build profile face for the extrude-only benchmark
        var face = (XbimFace)_profileFactory.BuildFace(_extrusion.SweptArea);
        _preBuiltFaceHandle = face.Handle;

        // Pre-extract direction
        GeometryFactory.BuildDirection3d(_extrusion.ExtrudedDirection,
            out _dirX, out _dirY, out _dirZ);
        _depth = _extrusion.Depth;

        // Pre-build location
        if (_extrusion.Position != null)
        {
            _locationHandle = ((GeometryFactory)_engine.ModelService.GeometryFactory)
                .BuildLocationFromAxis3D(_extrusion.Position).Handle;
        }
        else
        {
            _locationHandle = NativeLocationHandle.NullHandle;
        }
    }

    [Benchmark(Description = "Profile only (rectangle)")]
    public IXFace ProfileOnly()
    {
        return _profileFactory.BuildFace(_extrusion.SweptArea);
    }

    [Benchmark(Description = "Extrude only (pre-built face)")]
    public int ExtrudeOnly()
    {
        int result = XbimGeometryNativeApi.xbim_solid_build_extruded(
            _contextHandle,
            _preBuiltFaceHandle,
            _dirX, _dirY, _dirZ,
            _depth,
            _locationHandle,
            out var outHandle);
        outHandle?.Dispose();
        return result;
    }

    [Benchmark(Description = "Full end-to-end (rectangle)")]
    public IXbimSolid Full()
    {
        return _engine.CreateSolid(_extrusion, null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _model?.Dispose();
    }
}
