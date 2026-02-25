using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.IO.Memory;
using Xbim.ModelGeometry.Scene;

namespace Xbim.Geometry.Benchmarks.Benchmarks;

public class FullModelConfig : ManualConfig
{
    public FullModelConfig()
    {
        WithOptions(ConfigOptions.Default);
        AddJob(Job.ShortRun
            .WithToolchain(InProcessEmitToolchain.Instance));

        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.Full);
        AddColumn(StatisticColumn.Median);
        AddColumnProvider(DefaultColumnProviders.Instance);
    }
}

[Config(typeof(FullModelConfig))]
[BenchmarkCategory("FullModel")]
public class FullModelBenchmarks
{
    private readonly ILoggerFactory _loggerFactory = EngineSetup.LoggerFactory;

    [Params(XGeometryEngineVersion.V5, XGeometryEngineVersion.V6)]
    public XGeometryEngineVersion Version { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        EngineSetup.EnsureInitialized();
    }

    [Benchmark(Description = "Full model: beam-standard-case")]
    public bool BeamStandardCase()
    {
        using var model = EngineSetup.OpenModel("IfcExamples/beam-standard-case.ifc");
        var context = new Xbim3DModelContext(model, _loggerFactory, Version);
        return context.CreateContext();
    }

    [Benchmark(Description = "Full model: SampleHouse4")]
    public bool SampleHouse4()
    {
        using var model = EngineSetup.OpenModel("IfcExamples/SampleHouse4.ifc");
        var context = new Xbim3DModelContext(model, _loggerFactory, Version);
        return context.CreateContext();
    }

    [Benchmark(Description = "Full model: SampleHouse4 (single-thread)")]
    public bool SampleHouse4SingleThread()
    {
        using var model = EngineSetup.OpenModel("IfcExamples/SampleHouse4.ifc");
        var context = new Xbim3DModelContext(model, _loggerFactory, Version);
        context.MaxThreads = 1;
        return context.CreateContext();
    }
}
