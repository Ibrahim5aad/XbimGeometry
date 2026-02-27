using System;
using System.Diagnostics;
using System.IO;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Geometry.Scene;

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
            return 1;
        }
    }

    static void Convert(string inputPath, string outputPath)
    {
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"Opening {Path.GetFileName(inputPath)}...");
        using var model = IfcStore.Open(inputPath);
        Console.WriteLine($"  Schema: {model.SchemaVersion}, entities: {model.Instances.Count}");

        Console.WriteLine("Creating geometry context...");
        var context = new Xbim3DModelContext(model);

        ReportProgressDelegate progress = (percent, message) =>
        {
            Console.Write($"\r  [{percent,3}%] {message,-40}");
        };

        context.CreateContext(progress);
        Console.WriteLine(); // newline after progress

        Console.WriteLine($"Saving WexBIM to {Path.GetFileName(outputPath)}...");
        using (var fs = File.Create(outputPath))
        using (var bw = new BinaryWriter(fs))
        {
            model.SaveAsWexBim(bw);
        }

        sw.Stop();
        var fileSize = new FileInfo(outputPath).Length;
        Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F1}s — {fileSize / 1024.0:F0} KB written.");
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
