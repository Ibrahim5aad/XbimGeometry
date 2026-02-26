using System.Numerics;
using Silk.NET.OpenGL;

namespace Xbim.Examples.Viewer.Rendering;

/// <summary>
/// A draw batch for all meshes sharing a single style (material color).
/// Contains GPU buffer handles and rendering metadata.
/// </summary>
internal readonly struct StyleBatch
{
    /// <summary>Vertex Array Object handle.</summary>
    public uint Vao { get; init; }

    /// <summary>Vertex Buffer Object handle.</summary>
    public uint Vbo { get; init; }

    /// <summary>Index Buffer Object handle.</summary>
    public uint Ibo { get; init; }

    /// <summary>Number of indices to draw.</summary>
    public int IndexCount { get; init; }

    /// <summary>Style color: red component.</summary>
    public float R { get; init; }

    /// <summary>Style color: green component.</summary>
    public float G { get; init; }

    /// <summary>Style color: blue component.</summary>
    public float B { get; init; }

    /// <summary>Style color: alpha component.</summary>
    public float A { get; init; }

    /// <summary>True if alpha is less than 1.0 (needs blending).</summary>
    public bool IsTransparent { get; init; }

    /// <summary>World-space centroid of the batch geometry, used for back-to-front sorting.</summary>
    public Vector3 Centroid { get; init; }
}

/// <summary>
/// Identifies a contiguous range of indices within a <see cref="StyleBatch"/>
/// that belong to a single product.
/// </summary>
internal readonly struct ProductIndexRange
{
    /// <summary>Index of the batch in <see cref="GlScene.OpaqueBatches"/> or <see cref="GlScene.TransparentBatches"/>.</summary>
    public int BatchIndex { get; init; }

    /// <summary>Whether this range is in the transparent batch list.</summary>
    public bool IsTransparent { get; init; }

    /// <summary>Byte offset into the index buffer (startIndex * sizeof(uint)).</summary>
    public int StartIndex { get; init; }

    /// <summary>Number of indices in this range.</summary>
    public int IndexCount { get; init; }
}

/// <summary>
/// Holds all GPU resources for a loaded scene, split into opaque and transparent batches.
/// Dispose to release all OpenGL buffers.
/// </summary>
internal sealed class GlScene : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;

    /// <summary>Opaque draw batches (rendered first, with depth write).</summary>
    public List<StyleBatch> OpaqueBatches { get; } = new();

    /// <summary>Transparent draw batches (rendered second, sorted back-to-front).</summary>
    public List<StyleBatch> TransparentBatches { get; } = new();

    /// <summary>
    /// Maps product label to the index ranges that contain its triangles,
    /// across one or more style batches.
    /// </summary>
    public Dictionary<int, List<ProductIndexRange>> ProductRanges { get; } = new();

    /// <summary>Scene bounding box minimum corner.</summary>
    public Vector3 BoundsMin { get; set; }

    /// <summary>Scene bounding box maximum corner.</summary>
    public Vector3 BoundsMax { get; set; }

    public GlScene(GL gl)
    {
        _gl = gl;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DeleteBatches(OpaqueBatches);
        DeleteBatches(TransparentBatches);
    }

    private void DeleteBatches(List<StyleBatch> batches)
    {
        foreach (var batch in batches)
        {
            _gl.DeleteVertexArray(batch.Vao);
            _gl.DeleteBuffer(batch.Vbo);
            _gl.DeleteBuffer(batch.Ibo);
        }
        batches.Clear();
    }
}
