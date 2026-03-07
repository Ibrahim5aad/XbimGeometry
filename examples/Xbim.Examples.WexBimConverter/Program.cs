using System;
using System.Diagnostics;
using System.IO;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Geometry.Scene;
using Xbim.Tessellator;

namespace Xbim.Examples.WexBimConverter;

internal class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2)
        {
            PrintUsage();
            return 1;
        }

        var inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: File not found: {inputPath}");
            return 1;
        }

        var outputPath = args.Length == 2
            ? args[1]
            : Path.ChangeExtension(inputPath, ".wexbim");

        try
        {
            Convert(inputPath, outputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static void Convert(string inputPath, string outputPath)
    {
        Console.WriteLine($"Opening {Path.GetFileName(inputPath)}...");
        using var model = IfcStore.Open(inputPath);
        Console.WriteLine($"  Schema: {model.SchemaVersion}, entities: {model.Instances.Count}");

        // --- Run 1: WITHOUT extrusion fast path (full OCCT for all shapes) ---
        Console.WriteLine();
        Console.WriteLine("=== WITHOUT extrusion fast path (OCCT only) ===");
        XbimTessellator.EnableExtrusionFastPath = false;
        var timeWithout = RunCreateContext(model);

        // --- Run 2: WITH extrusion fast path ---
        Console.WriteLine();
        Console.WriteLine("=== WITH extrusion fast path ===");
        XbimTessellator.EnableExtrusionFastPath = true;
        var timeWith = RunCreateContext(model);

        // --- Save the fast-path result ---
        Console.WriteLine();
        Console.WriteLine($"Saving WexBIM to {Path.GetFileName(outputPath)}...");
        using (var fs = File.Create(outputPath))
        using (var bw = new BinaryWriter(fs))
        {
            model.SaveAsWexBim(bw);
        }
        var fileSize = new FileInfo(outputPath).Length;
        Console.WriteLine($"  {fileSize / 1024.0:F0} KB written.");

        // --- Summary ---
        Console.WriteLine();
        Console.WriteLine("=== BENCHMARK SUMMARY ===");
        Console.WriteLine($"  OCCT only:          {timeWithout.TotalSeconds,8:F2}s");
        Console.WriteLine($"  With fast extrusion: {timeWith.TotalSeconds,8:F2}s");
        double speedup = timeWithout.TotalSeconds / timeWith.TotalSeconds;
        Console.WriteLine($"  Speedup:             {speedup:F2}x");
    }

    static TimeSpan RunCreateContext(IModel model)
    {
        var context = new Xbim3DModelContext(model);

        ReportProgressDelegate progress = (percent, message) =>
        {
            Console.Write($"\r  [{percent,3}%] {message,-40}");
        };

        var sw = Stopwatch.StartNew();
        context.CreateContext(progress);
        sw.Stop();
        Console.WriteLine();
        Console.WriteLine($"  CreateContext: {sw.Elapsed.TotalSeconds:F2}s");
        return sw.Elapsed;
    }

    static void PrintUsage()
    {
        Console.WriteLine("Xbim WexBIM Converter");
        Console.WriteLine();
        Console.WriteLine("Converts an IFC file to WexBIM format using the xbim geometry engine.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ifc-to-wexbim <input.ifc> [output.wexbim]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  input.ifc       Path to the input IFC file");
        Console.WriteLine("  output.wexbim   Path for the output WexBIM file (default: same name as input)");
    }
}
