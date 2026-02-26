using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Engine;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

namespace Xbim.Examples.QuickStart;

/// <summary>
/// Demonstrates the core xbim geometry workflows shown in the repository README:
///   1. Opening an IFC model and creating geometry
///   2. Tessellating geometry into a triangulated mesh
///   3. Exporting a full 3D scene to WexBIM
/// </summary>
internal class Program
{
    static void Main(string[] args)
    {
        var ifcPath = args.Length > 0
            ? args[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "testdata", "SampleHouse4.ifc"));

        if (!File.Exists(ifcPath))
        {
            Console.Error.WriteLine($"Error: IFC file not found: {ifcPath}");
            Console.Error.WriteLine("Usage: Xbim.Examples.QuickStart [path/to/model.ifc]");
            return;
        }

        Console.WriteLine($"Opening {Path.GetFileName(ifcPath)}...");

        // --- Opening a Model and Creating Geometry ---

        using var model = IfcStore.Open(ifcPath);

        var loggerFactory = new LoggerFactory();
        var geomEngine = new XbimGeometryEngine(model, loggerFactory);
        var logger = loggerFactory.CreateLogger("QuickStart");

        // Build a solid from an IFC element
        var extrudedSolid = model.Instances.OfType<IIfcExtrudedAreaSolid>().First();
        IXbimSolid solid = geomEngine.CreateSolid(extrudedSolid, logger);

        Console.WriteLine($"Volume: {solid.Volume}");
        Console.WriteLine($"Faces:  {solid.Faces.Count}");

        // --- Tessellation ---

        // Tessellate a geometry object into a triangulated mesh
        XbimShapeGeometry mesh = geomEngine.CreateShapeGeometry(
            solid,
            model.ModelFactors.Precision,
            model.ModelFactors.DeflectionTolerance,
            model.ModelFactors.DeflectionAngle,
            XbimGeometryType.PolyhedronBinary,
            logger);

        XbimRect3D boundingBox = mesh.BoundingBox;
        Console.WriteLine($"Bounding box: {boundingBox}");

        // --- Full Scene Export to WexBIM ---

        var context = new Xbim3DModelContext(model);
        context.CreateContext();   // builds model scene

        var outputPath = args.Length > 1
            ? args[1]
            : Path.ChangeExtension(ifcPath, ".wexbim");

        using (var fs = File.Create(outputPath))
        using (var bw = new BinaryWriter(fs))
        {
            model.SaveAsWexBim(bw);
        }

        var fileSize = new FileInfo(outputPath).Length;
        Console.WriteLine($"WexBIM written: {Path.GetFileName(outputPath)} ({fileSize / 1024.0:F0} KB)");
    }
}
