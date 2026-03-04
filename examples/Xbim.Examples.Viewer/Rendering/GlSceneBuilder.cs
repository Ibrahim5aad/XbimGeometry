using System.Numerics;
using Silk.NET.OpenGL;
using Xbim.Geometry.Scene;

namespace Xbim.Examples.Viewer.Rendering;

/// <summary>
/// Converts a <see cref="WexBimScene"/> into GPU-ready draw batches grouped by style.
/// Meshes are merged per style with transforms baked into vertex data on the CPU.
/// </summary>
internal static class GlSceneBuilder
{
    /// <summary>
    /// Uploads a scene model to the GPU and returns a <see cref="GlScene"/>
    /// with opaque and transparent batches ready for rendering.
    /// </summary>
    public static GlScene Build(GL gl, WexBimScene model)
    {
        var scene = new GlScene(gl)
        {
            BoundsMin = model.BoundsMin,
            BoundsMax = model.BoundsMax
        };

        // Build a lookup from style id to RGBA
        var styleLookup = new Dictionary<int, WexBimStyle>();
        foreach (var style in model.Styles)
            styleLookup[style.Id] = style;

        // Group meshes by style id
        var groups = new Dictionary<int, List<WexBimMesh>>();
        foreach (var mesh in model.Meshes)
        {
            if (!groups.TryGetValue(mesh.StyleId, out var list))
            {
                list = new List<WexBimMesh>();
                groups[mesh.StyleId] = list;
            }
            list.Add(mesh);
        }

        // Build one batch per style group
        foreach (var (styleId, meshes) in groups)
        {
            // Resolve style color (default to grey if not found)
            float r = 0.6f, g = 0.6f, b = 0.6f, a = 1.0f;
            if (styleLookup.TryGetValue(styleId, out var style))
            {
                r = style.R;
                g = style.G;
                b = style.B;
                a = style.A;
            }

            var productRanges = new List<(int productLabel, int startIndex, int indexCount)>();
            var batch = CreateBatch(gl, meshes, r, g, b, a, productRanges);

            bool isTransparent = batch.IsTransparent;
            var targetList = isTransparent ? scene.TransparentBatches : scene.OpaqueBatches;
            int batchIndex = targetList.Count;
            targetList.Add(batch);

            // Register per-product index ranges
            foreach (var (productLabel, startIndex, indexCount) in productRanges)
            {
                if (!scene.ProductRanges.TryGetValue(productLabel, out var ranges))
                {
                    ranges = new List<ProductIndexRange>();
                    scene.ProductRanges[productLabel] = ranges;
                }
                ranges.Add(new ProductIndexRange
                {
                    BatchIndex = batchIndex,
                    IsTransparent = isTransparent,
                    StartIndex = startIndex,
                    IndexCount = indexCount
                });
            }
        }

        return scene;
    }

    private static StyleBatch CreateBatch(
        GL gl, List<WexBimMesh> meshes, float r, float g, float b, float a,
        List<(int productLabel, int startIndex, int indexCount)> productRanges)
    {
        // Calculate total sizes for pre-allocation
        int totalVertices = 0;
        int totalIndices = 0;
        foreach (var mesh in meshes)
        {
            totalVertices += mesh.Positions.Length / 3;
            totalIndices += mesh.Indices.Length;
        }

        // Interleaved vertex data: [px, py, pz, nx, ny, nz] per vertex
        var vertices = new float[totalVertices * 6];
        var indices = new uint[totalIndices];

        int vertexOffset = 0; // count of vertices written so far
        int vertexFloatOffset = 0;
        int indexOffset = 0;
        var centroidSum = Vector3.Zero;
        int centroidCount = 0;

        foreach (var mesh in meshes)
        {
            // Record the index range for this mesh's product
            int meshIndexStart = indexOffset;
            int meshVertexCount = mesh.Positions.Length / 3;
            var transform = mesh.Transform;
            bool hasTransform = transform != Matrix4x4.Identity;

            // Compute the normal matrix (transpose of inverse of upper-left 3x3)
            Matrix4x4 normalMatrix4;
            bool hasNormalMatrix = false;
            if (hasTransform && Matrix4x4.Invert(transform, out var inverse))
            {
                normalMatrix4 = Matrix4x4.Transpose(inverse);
                hasNormalMatrix = true;
            }
            else
            {
                normalMatrix4 = Matrix4x4.Identity;
            }

            // Transform and interleave vertex data
            for (int i = 0; i < meshVertexCount; i++)
            {
                int srcIdx = i * 3;
                float px = mesh.Positions[srcIdx];
                float py = mesh.Positions[srcIdx + 1];
                float pz = mesh.Positions[srcIdx + 2];

                float nx = mesh.Normals[srcIdx];
                float ny = mesh.Normals[srcIdx + 1];
                float nz = mesh.Normals[srcIdx + 2];

                if (hasTransform)
                {
                    // Transform position
                    var pos = Vector3.Transform(new Vector3(px, py, pz), transform);
                    px = pos.X;
                    py = pos.Y;
                    pz = pos.Z;

                    // Transform normal
                    if (hasNormalMatrix)
                    {
                        var n = Vector3.TransformNormal(new Vector3(nx, ny, nz), normalMatrix4);
                        float len = n.Length();
                        if (len > 1e-6f)
                        {
                            nx = n.X / len;
                            ny = n.Y / len;
                            nz = n.Z / len;
                        }
                    }
                }

                int dst = vertexFloatOffset + i * 6;
                vertices[dst] = px;
                vertices[dst + 1] = py;
                vertices[dst + 2] = pz;
                vertices[dst + 3] = nx;
                vertices[dst + 4] = ny;
                vertices[dst + 5] = nz;

                centroidSum += new Vector3(px, py, pz);
                centroidCount++;
            }

            // Remap indices with the current vertex offset
            for (int i = 0; i < mesh.Indices.Length; i++)
            {
                indices[indexOffset + i] = (uint)(mesh.Indices[i] + vertexOffset);
            }

            vertexOffset += meshVertexCount;
            vertexFloatOffset += meshVertexCount * 6;
            indexOffset += mesh.Indices.Length;

            // Record the product's index range within this batch
            productRanges.Add((mesh.ProductLabel, meshIndexStart, mesh.Indices.Length));
        }

        var centroid = centroidCount > 0
            ? centroidSum / centroidCount
            : Vector3.Zero;

        // Upload to GPU
        uint vao = gl.GenVertexArray();
        uint vbo = gl.GenBuffer();
        uint ibo = gl.GenBuffer();

        gl.BindVertexArray(vao);

        // Upload vertex data
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        unsafe
        {
            fixed (float* ptr = vertices)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(vertices.Length * sizeof(float)),
                    ptr, BufferUsageARB.StaticDraw);
            }
        }

        // Upload index data
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ibo);
        unsafe
        {
            fixed (uint* ptr = indices)
            {
                gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                    (nuint)(indices.Length * sizeof(uint)),
                    ptr, BufferUsageARB.StaticDraw);
            }
        }

        uint stride = 6 * sizeof(float);

        // Attribute 0: position (vec3 at offset 0)
        // Attribute 1: normal (vec3 at offset 12 bytes)
        gl.EnableVertexAttribArray(0);
        gl.EnableVertexAttribArray(1);
        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride,
                (void*)(3 * sizeof(float)));
        }

        gl.BindVertexArray(0);

        return new StyleBatch
        {
            Vao = vao,
            Vbo = vbo,
            Ibo = ibo,
            IndexCount = totalIndices,
            R = r,
            G = g,
            B = b,
            A = a,
            IsTransparent = a < 1.0f,
            Centroid = centroid
        };
    }
}
