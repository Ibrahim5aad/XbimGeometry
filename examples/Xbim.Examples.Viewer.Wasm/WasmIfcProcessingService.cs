using System.Diagnostics;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.IO;
using Xbim.Geometry.Scene;
using Xbim.Geometry.Viewer;
using Xbim.Geometry.Viewer.Components;

namespace Xbim.Examples.Viewer.Wasm;

/// <summary>
/// Processes IFC files and streaming the resulting scene directly into the viewer.
/// </summary>
public sealed class WasmIfcProcessingService : IIfcProcessingService
{
    private static readonly Dictionary<string, (string Label, int Percent)> PhaseMap = new()
    {
        ["Initialise"]               = ("Analysing model...", 15),
        ["WriteShapeGeometries"]     = ("Processing geometry...", 30),
        ["PrepareMapGeometryReferences"] = ("Resolving mapped items...", 55),
        ["ProcessFeaturedProducts"]  = ("Processing booleans...", 65),
        ["WriteProductShapes"]       = ("Writing products...", 80),
        ["WriteRegionsToDb"]         = ("Finalising regions...", 90),
    };

    public async Task<IfcProcessingResult> ProcessAsync(
        Stream data, string fileName, XbimViewer viewer,
        IProgress<IfcProcessingProgress>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // Phase: Open IFC model
        progress?.Report(new IfcProcessingProgress { Stage = "Opening IFC model...", Percent = 5 });

        var storageType = fileName.StorageType();
        if (storageType == StorageType.Invalid) storageType = StorageType.Ifc;

        using var model = await Task.Run(
            () => IfcStore.Open(data, storageType, XbimModelType.MemoryModel), ct);

        // Phase: Stream geometry into the viewer
        progress?.Report(new IfcProcessingProgress { Stage = "Preparing geometry...", Percent = 10 });

        await viewer.StreamBeginAsync();

        ReportProgressDelegate progDelegate = (percent, phase) =>
        {
            if (percent == -1 && phase is string name && PhaseMap.TryGetValue(name, out var info))
                progress?.Report(new IfcProcessingProgress { Stage = info.Label, Percent = info.Percent });
        };

        var context = new Xbim3DModelContext(model);
        await using var sink = new ViewerSceneSink(viewer);
        await context.CreateContextStreamingAsync(sink, generateBREPs: false, progDelegate: progDelegate);

        // Phase: Prepare WexBIM data
        progress?.Report(new IfcProcessingProgress { Stage = "Saving WexBIM...", Percent = 95 });

        using var output = new MemoryStream();
        using (var bw = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            model.SaveAsWexBim(bw);
        }

        sw.Stop();

        var shapeInstances = context.ShapeInstances();
        var productCount = new HashSet<int>(
            shapeInstances.Select(s => s.IfcProductLabel)).Count;

        return new IfcProcessingResult
        {
            ProductCount = productCount,
            Duration = sw.Elapsed,
            WexBimData = output.ToArray()
        };
    }
}
