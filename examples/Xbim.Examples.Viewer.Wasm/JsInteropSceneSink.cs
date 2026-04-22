using System;
using System.IO;
using System.Threading.Tasks;
using Xbim.Common.Geometry;
using Xbim.Geometry.Scene;
using Xbim.Geometry.Viewer.Components;

namespace Xbim.Examples.Viewer.Wasm;

/// <summary>
/// Streams scene geometry and instances to the Three.js viewer via the
/// <see cref="XbimViewer"/> component's streaming API.
/// </summary>
public sealed class ViewerSceneSink : ISceneStreamSink
{
    private readonly XbimViewer _viewer;

    public ViewerSceneSink(XbimViewer viewer)
    {
        _viewer = viewer;
    }

    private static float Safe(double v) => double.IsFinite(v) ? (float)v : 0f;
    private static double SafeD(double v) => double.IsFinite(v) ? v : 0d;

    public async ValueTask WriteHeader(StreamHeader header)
    {
        await _viewer.StreamHeaderAsync(
            Safe(header.OneMeter),
            Safe(header.EstimatedBounds.X), Safe(header.EstimatedBounds.Y), Safe(header.EstimatedBounds.Z),
            Safe(header.EstimatedBounds.SizeX), Safe(header.EstimatedBounds.SizeY), Safe(header.EstimatedBounds.SizeZ),
            header.EstimatedProductCount);
    }

    public async ValueTask WriteStyle(int styleId, float r, float g, float b, float a)
    {
        await _viewer.StreamStyleAsync(styleId, Safe(r), Safe(g), Safe(b), Safe(a));
    }

    public async ValueTask WriteGeometry(int geometryId, byte[] meshData, XbimRect3D bounds)
    {
        await _viewer.StreamGeometryAsync(geometryId, new MemoryStream(meshData));
    }

    public async ValueTask WriteInstance(SceneInstance instance)
    {
        var m = instance.Transform;
        var transform = new double[]
        {
            SafeD(m.M11), SafeD(m.M12), SafeD(m.M13), SafeD(m.M14),
            SafeD(m.M21), SafeD(m.M22), SafeD(m.M23), SafeD(m.M24),
            SafeD(m.M31), SafeD(m.M32), SafeD(m.M33), SafeD(m.M34),
            SafeD(m.OffsetX), SafeD(m.OffsetY), SafeD(m.OffsetZ), SafeD(m.M44)
        };

        await _viewer.StreamInstanceAsync(
            instance.ProductLabel, instance.TypeId, instance.GeometryId, instance.StyleId,
            transform,
            Safe(instance.BoundingBox.X), Safe(instance.BoundingBox.Y), Safe(instance.BoundingBox.Z),
            Safe(instance.BoundingBox.SizeX), Safe(instance.BoundingBox.SizeY), Safe(instance.BoundingBox.SizeZ));
    }

    public async ValueTask Complete()
    {
        await _viewer.StreamCompleteAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
