using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Xbim.Geometry.Benchmarks.Infrastructure;

/// <summary>
/// Default BenchmarkDotNet configuration for geometry benchmarks.
/// Uses InProcess toolchain to avoid native DLL resolution issues
/// when BenchmarkDotNet spawns child processes.
/// </summary>
public class GeometryBenchmarkConfig : ManualConfig
{
    public GeometryBenchmarkConfig()
    {
        WithOptions(ConfigOptions.Default);
        AddJob(Job.MediumRun
            .WithToolchain(InProcessEmitToolchain.Instance));

        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.Full);
        AddColumn(StatisticColumn.Median);
        AddColumnProvider(DefaultColumnProviders.Instance);
    }
}
