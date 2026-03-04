using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Geometry.Scene;
using Xunit;
using Xunit.Abstractions;

namespace Xbim.Geometry.Engine.Tests;

/// <summary>
/// Verifies that <see cref="Xbim3DModelContext"/> produces equivalent WexBIM output
/// to the legacy <see cref="Xbim3DModelContextLegacy"/>.
/// </summary>
public class StreamingContextTests
{
    private readonly ITestOutputHelper _output;

    public StreamingContextTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Streaming_produces_equivalent_wexbim_to_batch()
    {
        // --- Legacy (batch) context ---
        WexBimScene batchScene;
        using (var model = IfcStore.Open("TestFiles/IfcExamples/SampleHouse4.ifc"))
        {
            var context = new Xbim3DModelContextLegacy(model, (ILoggerFactory)null);
            context.CreateContext().Should().BeTrue("batch CreateContext should succeed");

            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                model.SaveAsWexBim(bw);

            ms.Position = 0;
            batchScene = WexBimFileReader.Read(ms);
        }

        // --- New (streaming) context ---
        WexBimScene streamScene;
        using (var model = IfcStore.Open("TestFiles/IfcExamples/SampleHouse4.ifc"))
        {
            var context = new Xbim3DModelContext(model, (ILoggerFactory)null);
            context.CreateContext().Should().BeTrue("streaming CreateContext should succeed");

            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                model.SaveAsWexBim(bw);

            ms.Position = 0;
            streamScene = WexBimFileReader.Read(ms);
        }

        // --- Compare header ---
        streamScene.Version.Should().Be(batchScene.Version, "WexBIM version should match");
        streamScene.OneMeter.Should().Be(batchScene.OneMeter, "OneMeter should match");

        // --- Compare products ---
        _output.WriteLine($"Batch:     {batchScene.Products.Count} products, {batchScene.TotalShapeInstances} shape instances, {batchScene.Shapes.Count} shapes");
        _output.WriteLine($"Streaming: {streamScene.Products.Count} products, {streamScene.TotalShapeInstances} shape instances, {streamScene.Shapes.Count} shapes");
        _output.WriteLine($"Batch:     {batchScene.TotalVertexCount} vertices, {batchScene.TotalTriangleCount} triangles");
        _output.WriteLine($"Streaming: {streamScene.TotalVertexCount} vertices, {streamScene.TotalTriangleCount} triangles");

        streamScene.Products.Count.Should().Be(batchScene.Products.Count, "product count should match");

        // All products from batch should exist in streaming
        var batchProductLabels = batchScene.Products.Select(p => p.Label).OrderBy(l => l).ToList();
        var streamProductLabels = streamScene.Products.Select(p => p.Label).OrderBy(l => l).ToList();
        streamProductLabels.Should().BeEquivalentTo(batchProductLabels, "same product labels");

        // --- Compare styles ---
        streamScene.Styles.Count.Should().Be(batchScene.Styles.Count, "style count should match");
        var batchStyleIds = batchScene.Styles.Select(s => s.Id).OrderBy(x => x).ToList();
        var streamStyleIds = streamScene.Styles.Select(s => s.Id).OrderBy(x => x).ToList();
        streamStyleIds.Should().BeEquivalentTo(batchStyleIds, "same style IDs");

        // --- Compare shape instances ---
        // Collect all (productLabel, styleId) pairs from shape instances
        var batchInstances = batchScene.Shapes
            .SelectMany(s => s.Instances)
            .Select(i => (i.ProductLabel, i.StyleId))
            .OrderBy(x => x.ProductLabel).ThenBy(x => x.StyleId)
            .ToList();

        var streamInstances = streamScene.Shapes
            .SelectMany(s => s.Instances)
            .Select(i => (i.ProductLabel, i.StyleId))
            .OrderBy(x => x.ProductLabel).ThenBy(x => x.StyleId)
            .ToList();

        streamInstances.Count.Should().Be(batchInstances.Count, "total shape instance count should match");

        // Report differences if any
        var batchSet = batchInstances.ToHashSet();
        var streamSet = streamInstances.ToHashSet();
        var onlyInBatch = batchSet.Except(streamSet).ToList();
        var onlyInStream = streamSet.Except(batchSet).ToList();

        if (onlyInBatch.Count > 0 || onlyInStream.Count > 0)
        {
            _output.WriteLine($"\nDifferences in shape instances:");
            foreach (var (prod, style) in onlyInBatch)
                _output.WriteLine($"  Only in Batch:     product={prod}, style={style}");
            foreach (var (prod, style) in onlyInStream)
                _output.WriteLine($"  Only in Streaming: product={prod}, style={style}");
        }

        streamInstances.Should().BeEquivalentTo(batchInstances,
            "same (product, style) shape instances");

        // --- Compare total triangle/vertex counts ---
        // Allow small tolerance for featured products where boolean order may cause
        // slightly different tessellation
        var vertexDelta = Math.Abs(streamScene.TotalVertexCount - batchScene.TotalVertexCount);
        var triangleDelta = Math.Abs(streamScene.TotalTriangleCount - batchScene.TotalTriangleCount);
        _output.WriteLine($"\nVertex delta: {vertexDelta}, Triangle delta: {triangleDelta}");

        // Within 5% tolerance for mesh counts (boolean rebuild can differ slightly)
        if (batchScene.TotalTriangleCount > 0)
        {
            var pctDiff = (double)triangleDelta / batchScene.TotalTriangleCount;
            _output.WriteLine($"Triangle count difference: {pctDiff:P1}");
            pctDiff.Should().BeLessThan(0.05, "triangle counts should be within 5%");
        }

        // --- Compare regions ---
        streamScene.Regions.Count.Should().Be(batchScene.Regions.Count, "region count should match");
    }
}
