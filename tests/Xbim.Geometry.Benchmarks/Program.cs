using System.Diagnostics;
using System.Runtime;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Ifc4.Interfaces;
#if OLD_ENGINE
using Xbim.ModelGeometry.Scene;
#else
using Xbim.Geometry.Scene;
#endif
#if OLD_ENGINE
using Xbim.Geometry.Abstractions;
#endif

namespace Xbim.Geometry.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--quick")
        {
            var file = args.Length > 1 ? args[1] : "IfcExamples/Dormitory-ALL-IFC.ifc";
            QuickRun(file);
            return;
        }

        if (args.Length > 0 && args[0] == "--profile")
        {
            var file = args.Length > 1 ? args[1] : "IfcExamples/Dormitory-ALL-IFC.ifc";
            ProfileRun(file);
            return;
        }

#if !OLD_ENGINE
        if (args.Length > 0 && args[0] == "--compare")
        {
            var file = args.Length > 1 ? args[1] : "IfcExamples/SampleHouse4.ifc";
            CompareRun(file);
            return;
        }
#endif

        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args);
    }

    /// <summary>
    /// Single-pass timed run of Xbim3DModelContext.CreateContext() on the given file.
    /// Usage: dotnet run -c Release -- --quick IfcExamples/Dormitory-ALL-IFC.ifc
    /// </summary>
    private static void QuickRun(string relativePath)
    {
        EngineSetup.EnsureInitialized();
        var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Warning)
            .AddConsole());

#if OLD_ENGINE
        Console.WriteLine($"=== Quick Benchmark (OLD engine): {relativePath} ===");
#else
        Console.WriteLine($"=== Quick Benchmark (NEW engine): {relativePath} ===");
#endif
        Console.WriteLine();

        // ── Load model ──
        var swLoad = Stopwatch.StartNew();
        using var model = EngineSetup.OpenModel(relativePath);
        swLoad.Stop();

        var entityCount = model.Instances.Count;
        Console.WriteLine($"Model loaded: {entityCount:N0} entities in {swLoad.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine();

#if OLD_ENGINE
        // Old engine: run V5 and V6 paths
        foreach (var version in new[] { XGeometryEngineVersion.V5, XGeometryEngineVersion.V6 })
        {
            // ── Multi-threaded ──
            {
                var context = new Xbim3DModelContext(model, loggerFactory, version);
                Console.WriteLine($"[{version}] Processing (multi-threaded, MaxThreads={Environment.ProcessorCount})...");

                var sw = Stopwatch.StartNew();
                var success = context.CreateContext();
                sw.Stop();

                var shapes = context.ShapeInstances().ToList();
                Console.WriteLine($"  Result:     {(success ? "OK" : "FAILED")}");
                Console.WriteLine($"  Time:       {sw.Elapsed.TotalSeconds:F2}s ({sw.ElapsedMilliseconds:N0} ms)");
                Console.WriteLine($"  Shapes:     {shapes.Count:N0}");
                Console.WriteLine($"  Peak memory: ~{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB");
                Console.WriteLine();
            }

            // ── Single-threaded ──
            {
                var context = new Xbim3DModelContext(model, loggerFactory, version);
                context.MaxThreads = 1;
                Console.WriteLine($"[{version}] Processing (single-threaded)...");

                var sw = Stopwatch.StartNew();
                var success = context.CreateContext();
                sw.Stop();

                var shapes = context.ShapeInstances().ToList();
                Console.WriteLine($"  Result:     {(success ? "OK" : "FAILED")}");
                Console.WriteLine($"  Time:       {sw.Elapsed.TotalSeconds:F2}s ({sw.ElapsedMilliseconds:N0} ms)");
                Console.WriteLine($"  Shapes:     {shapes.Count:N0}");
                Console.WriteLine($"  Peak memory: ~{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB");
                Console.WriteLine();
            }
        }
#else
        // New engine
        // ── Multi-threaded run ──
        {
            var context = new Xbim3DModelContext(model, loggerFactory);
            Console.WriteLine($"Processing (multi-threaded, MaxThreads={Environment.ProcessorCount})...");

            var sw = Stopwatch.StartNew();
            var success = context.CreateContext();
            sw.Stop();

            var shapes = context.ShapeInstances().ToList();
            Console.WriteLine($"  Result:     {(success ? "OK" : "FAILED")}");
            Console.WriteLine($"  Time:       {sw.Elapsed.TotalSeconds:F2}s ({sw.ElapsedMilliseconds:N0} ms)");
            Console.WriteLine($"  Shapes:     {shapes.Count:N0}");
            Console.WriteLine($"  Peak memory: ~{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB");
            Console.WriteLine();
        }

        // ── Single-threaded run ──
        {
            var context = new Xbim3DModelContext(model, loggerFactory);
            context.MaxThreads = 1;
            Console.WriteLine("Processing (single-threaded)...");

            var sw = Stopwatch.StartNew();
            var success = context.CreateContext();
            sw.Stop();

            var shapes = context.ShapeInstances().ToList();
            Console.WriteLine($"  Result:     {(success ? "OK" : "FAILED")}");
            Console.WriteLine($"  Time:       {sw.Elapsed.TotalSeconds:F2}s ({sw.ElapsedMilliseconds:N0} ms)");
            Console.WriteLine($"  Shapes:     {shapes.Count:N0}");
            Console.WriteLine($"  Peak memory: ~{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB");
            Console.WriteLine();
        }
#endif

        Console.WriteLine("Done.");
    }

    /// <summary>
    /// Detailed performance profile with model analysis and per-phase/per-operation timing.
    /// Usage: dotnet run -c Release -- --profile IfcExamples/Dormitory-ALL-IFC.ifc
    /// </summary>
    private static void ProfileRun(string relativePath)
    {
        EngineSetup.EnsureInitialized();
        var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Warning)
            .AddConsole());

#if OLD_ENGINE
        Console.WriteLine($"=== Performance Profile (OLD engine): {relativePath} ===");
#else
        Console.WriteLine($"=== Performance Profile (NEW engine): {relativePath} ===");
#endif
        Console.WriteLine();

        // ── Load model ──
        var swLoad = Stopwatch.StartNew();
        using var model = EngineSetup.OpenModel(relativePath);
        swLoad.Stop();
        Console.WriteLine($"Model loaded: {model.Instances.Count:N0} entities in {swLoad.Elapsed.TotalSeconds:F2}s");

        // ── Model composition analysis ──
        AnalyzeModel(model);

        // ── Run CreateContext (single-threaded for deterministic timing) ──
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.WriteLine("Running CreateContext (single-threaded for consistent timing)...");
        Console.WriteLine("══════════════════════════════════════════════════════");

#if OLD_ENGINE
        var context = new Xbim3DModelContext(model, loggerFactory, XGeometryEngineVersion.V6);
#else
        var context = new Xbim3DModelContext(model, loggerFactory);
        context.EnableDiagnostics = true;
#endif
        context.MaxThreads = 1;

        var swTotal = Stopwatch.StartNew();
        var success = context.CreateContext();
        swTotal.Stop();

        var shapes = context.ShapeInstances().ToList();
        Console.WriteLine($"\n  Total:       {swTotal.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"  Result:      {(success ? "OK" : "FAILED")}");
        Console.WriteLine($"  Shapes:      {shapes.Count:N0}");
        Console.WriteLine($"  Peak memory: ~{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB");

        // ── Print sub-operation diagnostics ──
#if !OLD_ENGINE
        context.PrintDiagnostics();
#endif

        // ── Also run multi-threaded for comparison ──
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.WriteLine($"Running CreateContext (multi-threaded, MaxThreads={Environment.ProcessorCount})...");
        Console.WriteLine("══════════════════════════════════════════════════════");

#if OLD_ENGINE
        var contextMt = new Xbim3DModelContext(model, loggerFactory, XGeometryEngineVersion.V6);
#else
        var contextMt = new Xbim3DModelContext(model, loggerFactory);
        contextMt.EnableDiagnostics = true;
#endif

        var swMt = Stopwatch.StartNew();
        var successMt = contextMt.CreateContext();
        swMt.Stop();

        var shapesMt = contextMt.ShapeInstances().ToList();
        Console.WriteLine($"\n  Total:       {swMt.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"  Result:      {(successMt ? "OK" : "FAILED")}");
        Console.WriteLine($"  Shapes:      {shapesMt.Count:N0}");
        Console.WriteLine($"  Peak memory: ~{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB");

#if !OLD_ENGINE
        contextMt.PrintDiagnostics();
#endif

        Console.WriteLine("\nDone.");
    }

#if !OLD_ENGINE
    /// <summary>
    /// Side-by-side comparison of Xbim3DModelContextLegacy (batch) vs Xbim3DModelContext (streaming).
    /// Measures both managed heap and process working set (native + managed).
    /// Usage: dotnet run -c Release -- --compare [path-to-ifc]
    /// </summary>
    private static void CompareRun(string relativePath)
    {
        EngineSetup.EnsureInitialized();
        var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Warning)
            .AddConsole());

        Console.WriteLine($"=== Batch vs Streaming Comparison: {relativePath} ===");
        Console.WriteLine();

        // ── Load model ──
        var swLoad = Stopwatch.StartNew();
        using var model = EngineSetup.OpenModel(relativePath);
        swLoad.Stop();
        Console.WriteLine($"Model loaded: {model.Instances.Count:N0} entities in {swLoad.Elapsed.TotalSeconds:F2}s");

        var voidCount = model.Instances.OfType<IIfcRelVoidsElement>().Count();
        var projCount = model.Instances.OfType<IIfcRelProjectsElement>().Count();
        var voidedProducts = model.Instances.OfType<IIfcRelVoidsElement>()
            .Select(v => v.RelatingBuildingElement.EntityLabel).Distinct().Count();
        Console.WriteLine($"Voids: {voidCount} (affecting {voidedProducts} products), Projections: {projCount}");
        Console.WriteLine();

        var proc = Process.GetCurrentProcess();

        // ── Helper to run a context and capture metrics ──
        void RunContext(string label, Func<(bool success, int shapes, int geoms)> action)
        {
            // Force full GC + compaction to get a clean baseline
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);

            proc.Refresh();
            var wsBefore = proc.WorkingSet64;
            var privateBefore = proc.PrivateMemorySize64;
            var managedBefore = GC.GetTotalMemory(true);

            var sw = Stopwatch.StartNew();
            var (success, shapes, geoms) = action();
            sw.Stop();

            proc.Refresh();
            var wsAfter = proc.WorkingSet64;
            var privateAfter = proc.PrivateMemorySize64;
            var managedAfter = GC.GetTotalMemory(false);

            Console.WriteLine($"  {label}");
            Console.WriteLine($"    Result:         {(success ? "OK" : "FAILED")}");
            Console.WriteLine($"    Time:           {sw.Elapsed.TotalSeconds:F2}s ({sw.ElapsedMilliseconds:N0} ms)");
            Console.WriteLine($"    Shapes:         {shapes:N0}");
            Console.WriteLine($"    Geometries:     {geoms:N0}");
            Console.WriteLine($"    Managed heap:   {managedAfter / (1024.0 * 1024):F1} MB (delta: {(managedAfter - managedBefore) / (1024.0 * 1024):+0.0;-0.0} MB)");
            Console.WriteLine($"    Working set:    {wsAfter / (1024.0 * 1024):F1} MB (delta: {(wsAfter - wsBefore) / (1024.0 * 1024):+0.0;-0.0} MB)");
            Console.WriteLine($"    Private bytes:  {privateAfter / (1024.0 * 1024):F1} MB (delta: {(privateAfter - privateBefore) / (1024.0 * 1024):+0.0;-0.0} MB)");
            Console.WriteLine();
        }

        // ── Single-threaded ──
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.WriteLine("Single-threaded (MaxThreads=1)");
        Console.WriteLine("══════════════════════════════════════════════════════");

        RunContext("Legacy (Xbim3DModelContextLegacy)", () =>
        {
            var context = new Xbim3DModelContextLegacy(model, loggerFactory);
            context.MaxThreads = 1;
            var success = context.CreateContext();
            return (success, context.ShapeInstances().Count(), context.ShapeGeometries().Count());
        });

        {
            var context = new Xbim3DModelContext(model, loggerFactory);
            context.MaxThreads = 1;
            context.EnableDiagnostics = true;

            RunContext("New (Xbim3DModelContext)", () =>
            {
                var success = context.CreateContext();
                return (success, context.ShapeInstances().Count(), context.ShapeGeometries().Count());
            });

            context.PrintDiagnostics();
        }

        // ── Multi-threaded ──
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.WriteLine($"Multi-threaded (MaxThreads={Environment.ProcessorCount})");
        Console.WriteLine("══════════════════════════════════════════════════════");

        RunContext("Legacy (Xbim3DModelContextLegacy)", () =>
        {
            var context = new Xbim3DModelContextLegacy(model, loggerFactory);
            var success = context.CreateContext();
            return (success, context.ShapeInstances().Count(), context.ShapeGeometries().Count());
        });

        RunContext("New (Xbim3DModelContext)", () =>
        {
            var context = new Xbim3DModelContext(model, loggerFactory);
            var success = context.CreateContext();
            return (success, context.ShapeInstances().Count(), context.ShapeGeometries().Count());
        });

        Console.WriteLine("Done.");
    }
#endif

    /// <summary>
    /// Analyzes the IFC model composition — geometry types, products, voids, booleans, mapped items.
    /// </summary>
    private static void AnalyzeModel(IModel model)
    {
        Console.WriteLine("\n── Model Composition ──");

        // Products by type
        var products = model.Instances.OfType<IIfcProduct>().ToList();
        Console.WriteLine($"\nProducts: {products.Count}");
        foreach (var g in products.GroupBy(p => p.GetType().Name).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Key,-40} {g.Count(),5}");

        // Solid models
        var solids = model.Instances.OfType<IIfcSolidModel>().ToList();
        Console.WriteLine($"\nSolid models: {solids.Count}");
        foreach (var g in solids.GroupBy(s => s.GetType().Name).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Key,-40} {g.Count(),5}");

        // Boolean results
        var boolResults = model.Instances.OfType<IIfcBooleanResult>().ToList();
        Console.WriteLine($"\nBoolean results: {boolResults.Count}");
        foreach (var g in boolResults.GroupBy(b => b.GetType().Name).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Key,-40} {g.Count(),5}");
        if (boolResults.Count > 0)
        {
            var byOp = boolResults.GroupBy(b => b.Operator).OrderByDescending(g => g.Count());
            Console.WriteLine("  By operator:");
            foreach (var g in byOp)
                Console.WriteLine($"    {g.Key,-38} {g.Count(),5}");

            // Boolean chain depth analysis
            int maxDepth = 0;
            foreach (var br in boolResults)
            {
                if (br.FirstOperand is not IIfcBooleanResult) // root of a chain
                {
                    // This is a leaf, not root; count roots differently
                }
                // Count roots: boolean results NOT referenced as first operand of another
                int depth = 1;
                var current = br;
                while (current.FirstOperand is IIfcBooleanResult parent)
                {
                    depth++;
                    current = parent;
                }
                if (depth > maxDepth) maxDepth = depth;
            }
            Console.WriteLine($"  Max boolean chain depth: {maxDepth}");
        }

        // Direct tessellation candidates
        var tessellated = model.Instances.OfType<IIfcTessellatedFaceSet>().Count();
        var faceBased = model.Instances.OfType<IIfcFaceBasedSurfaceModel>().Count();
        var shellBased = model.Instances.OfType<IIfcShellBasedSurfaceModel>().Count();
        var facetedBrep = model.Instances.OfType<IIfcFacetedBrep>().Count();
        var connectedFaceSet = model.Instances.OfType<IIfcConnectedFaceSet>().Count();
        Console.WriteLine($"\nDirect tessellation candidates (bypass engine):");
        Console.WriteLine($"  IIfcTessellatedFaceSet:        {tessellated,5}");
        Console.WriteLine($"  IIfcFaceBasedSurfaceModel:     {faceBased,5}");
        Console.WriteLine($"  IIfcShellBasedSurfaceModel:    {shellBased,5}");
        Console.WriteLine($"  IIfcFacetedBrep:               {facetedBrep,5}");
        Console.WriteLine($"  IIfcConnectedFaceSet:          {connectedFaceSet,5}");

        // Mapped items (instancing)
        var mapped = model.Instances.OfType<IIfcMappedItem>().Count();
        Console.WriteLine($"\nMapped items (instancing): {mapped}");

        // Voids and projections
        var voids = model.Instances.OfType<IIfcRelVoidsElement>().ToList();
        var projections = model.Instances.OfType<IIfcRelProjectsElement>().ToList();
        var voidedProducts = voids.Select(v => v.RelatingBuildingElement.EntityLabel).Distinct().Count();
        Console.WriteLine($"\nVoid/projection relations:");
        Console.WriteLine($"  IIfcRelVoidsElement:           {voids.Count,5}  (affecting {voidedProducts} products)");
        Console.WriteLine($"  IIfcRelProjectsElement:        {projections.Count,5}");

        // Profiles used
        var profiles = model.Instances.OfType<IIfcProfileDef>().ToList();
        Console.WriteLine($"\nProfiles: {profiles.Count}");
        foreach (var g in profiles.GroupBy(p => p.GetType().Name).OrderByDescending(g => g.Count()).Take(10))
            Console.WriteLine($"  {g.Key,-40} {g.Count(),5}");

        // Model factors
        var mf = model.ModelFactors;
        Console.WriteLine($"\nModel factors:");
        Console.WriteLine($"  Precision:            {mf.Precision}");
        Console.WriteLine($"  PrecisionBoolean:     {mf.PrecisionBoolean}");
        Console.WriteLine($"  OneMetre:             {mf.OneMetre}");
        Console.WriteLine($"  OneMilliMeter:        {mf.OneMilliMeter}");
        Console.WriteLine($"  DeflectionTolerance:  {mf.DeflectionTolerance}");
        Console.WriteLine($"  DeflectionAngle:      {mf.DeflectionAngle}");
        Console.WriteLine($"  LengthToMetresConversionFactor: {mf.LengthToMetresConversionFactor}");

        // Originating system
        var project = model.Instances.OfType<IIfcProject>().FirstOrDefault();
        if (project?.OwnerHistory?.OwningApplication != null)
        {
            var app = project.OwnerHistory.OwningApplication;
            Console.WriteLine($"  OriginatingSystem:    {app.ApplicationFullName} {app.Version}");
        }
    }
}
