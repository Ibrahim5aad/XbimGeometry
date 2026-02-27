using System.Diagnostics;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Geometry.Benchmarks.Infrastructure;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;
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
