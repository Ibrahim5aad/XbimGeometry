#if !OLD_ENGINE
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Geometry.Scene;
using Xbim.IO.Memory;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

/// <summary>
/// Compares Xbim3DModelContextLegacy (batch, caches all voided BRep) against
/// Xbim3DModelContext (streaming, caches featured body BRep for reuse).
/// </summary>
[Config(typeof(FullModelConfig))]
[BenchmarkCategory("StreamingComparison")]
[MemoryDiagnoser]
public class StreamingContextBenchmarks
{
    private readonly ILoggerFactory _loggerFactory = EngineSetup.LoggerFactory;

    [GlobalSetup]
    public void Setup()
    {
        EngineSetup.EnsureInitialized();
    }

    // ── beam-standard-case (small model, no booleans) ──

    [Benchmark(Baseline = true, Description = "beam: Legacy (batch)")]
    public bool Beam_Batch()
    {
        using var model = EngineSetup.OpenModel("IfcExamples/beam-standard-case.ifc");
        var context = new Xbim3DModelContextLegacy(model, _loggerFactory);
        return context.CreateContext();
    }

    [Benchmark(Description = "beam: New (streaming)")]
    public bool Beam_Streaming()
    {
        using var model = EngineSetup.OpenModel("IfcExamples/beam-standard-case.ifc");
        var context = new Xbim3DModelContext(model, _loggerFactory);
        return context.CreateContext();
    }

    // ── SampleHouse4 (medium model with openings/booleans) ──

    [Benchmark(Description = "SampleHouse4: Legacy (batch)")]
    public bool SampleHouse4_Batch()
    {
        using var model = EngineSetup.OpenModel("IfcExamples/SampleHouse4.ifc");
        var context = new Xbim3DModelContextLegacy(model, _loggerFactory);
        return context.CreateContext();
    }

    [Benchmark(Description = "SampleHouse4: New (streaming)")]
    public bool SampleHouse4_Streaming()
    {
        using var model = EngineSetup.OpenModel("IfcExamples/SampleHouse4.ifc");
        var context = new Xbim3DModelContext(model, _loggerFactory);
        return context.CreateContext();
    }

    // ── SampleHouse4 single-threaded (isolates CPU overhead of body rebuild) ──

    [Benchmark(Description = "SampleHouse4 1T: Legacy (batch)")]
    public bool SampleHouse4_Batch_1T()
    {
        using var model = EngineSetup.OpenModel("IfcExamples/SampleHouse4.ifc");
        var context = new Xbim3DModelContextLegacy(model, _loggerFactory);
        context.MaxThreads = 1;
        return context.CreateContext();
    }

    [Benchmark(Description = "SampleHouse4 1T: New (streaming)")]
    public bool SampleHouse4_Streaming_1T()
    {
        using var model = EngineSetup.OpenModel("IfcExamples/SampleHouse4.ifc");
        var context = new Xbim3DModelContext(model, _loggerFactory);
        context.MaxThreads = 1;
        return context.CreateContext();
    }
}
#endif
