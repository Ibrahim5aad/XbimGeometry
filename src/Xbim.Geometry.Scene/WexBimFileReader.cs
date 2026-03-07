using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Xbim.Geometry.Scene;

/// <summary>
/// Reads a WexBIM binary stream into a structured <see cref="WexBimScene"/> for rendering,
/// inspection, and comparison.
/// </summary>
public static class WexBimFileReader
{
    private const int WexBimMagic = 94132117;
    private const double PackSize = 252.0;

    /// <summary>
    /// Reads a WexBIM file from a file path.
    /// </summary>
    public static WexBimScene ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>
    /// Reads a WexBIM file from an arbitrary stream.
    /// </summary>
    public static WexBimScene Read(Stream stream)
    {
        using var br = new BinaryReader(stream);
        return Read(br);
    }

    /// <summary>
    /// Reads a WexBIM file from a BinaryReader.
    /// </summary>
    public static WexBimScene Read(BinaryReader br)
    {
        var scene = new WexBimScene();

        // --- Header ---
        int magic = br.ReadInt32();
        if (magic != WexBimMagic)
            throw new InvalidDataException(
                $"Not a valid WexBIM file. Expected magic {WexBimMagic}, got {magic}.");

        scene.Version = br.ReadByte();
        int shapeCount = br.ReadInt32();
        scene.TotalVertexCount = br.ReadInt32();
        scene.TotalTriangleCount = br.ReadInt32();
        int _matrixCount = br.ReadInt32();
        int productCount = br.ReadInt32();
        int styleCount = br.ReadInt32();
        scene.OneMeter = br.ReadSingle();
        if (scene.Version >= 4)
        {
            scene.Wcs = new Vector3(
                (float)br.ReadDouble(),
                (float)br.ReadDouble(),
                (float)br.ReadDouble());
        }
        short regionCount = br.ReadInt16();

        // --- Regions ---
        var globalMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var globalMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < regionCount; i++)
        {
            int population = br.ReadInt32();
            float cx = br.ReadSingle();
            float cy = br.ReadSingle();
            float cz = br.ReadSingle();
            float bx = br.ReadSingle();
            float by = br.ReadSingle();
            float bz = br.ReadSingle();
            float sx = br.ReadSingle();
            float sy = br.ReadSingle();
            float sz = br.ReadSingle();

            var bmin = new Vector3(bx, by, bz);
            var bmax = new Vector3(bx + sx, by + sy, bz + sz);

            scene.Regions.Add(new WexBimRegion
            {
                Population = population,
                Centre = new Vector3(cx, cy, cz),
                BoundsMin = bmin,
                BoundsMax = bmax
            });

            globalMin = Vector3.Min(globalMin, bmin);
            globalMax = Vector3.Max(globalMax, bmax);
        }

        scene.BoundsMin = regionCount > 0 ? globalMin : Vector3.Zero;
        scene.BoundsMax = regionCount > 0 ? globalMax : Vector3.Zero;

        // --- Styles ---
        for (int i = 0; i < styleCount; i++)
        {
            scene.Styles.Add(new WexBimStyle
            {
                Id = br.ReadInt32(),
                R = br.ReadSingle(),
                G = br.ReadSingle(),
                B = br.ReadSingle(),
                A = br.ReadSingle()
            });
        }

        // --- Products ---
        for (int i = 0; i < productCount; i++)
        {
            int label = br.ReadInt32();
            short typeId = br.ReadInt16();
            float bx = br.ReadSingle();
            float by = br.ReadSingle();
            float bz = br.ReadSingle();
            float sx = br.ReadSingle();
            float sy = br.ReadSingle();
            float sz = br.ReadSingle();

            scene.Products.Add(new WexBimProduct
            {
                Label = label,
                TypeId = typeId,
                BoundsMin = new Vector3(bx, by, bz),
                BoundsMax = new Vector3(bx + sx, by + sy, bz + sz)
            });
        }

        // --- Shapes ---
        if (scene.Version >= 3)
        {
            // v3+ uses per-region geometry blocks with byte-length-prefixed mesh data
            for (int r = 0; r < regionCount; r++)
            {
                int geomCount = br.ReadInt32();
                for (int g = 0; g < geomCount; g++)
                    ReadShapeGroup(br, scene);
            }
        }
        else
        {
            // v1/v2: flat shape list with inline triangulation
            for (int i = 0; i < shapeCount; i++)
                ReadShapeGroup(br, scene);
        }

        return scene;
    }

    private static void ReadShapeGroup(BinaryReader br, WexBimScene scene)
    {
        int repetition = br.ReadInt32();
        if (repetition < 1) return;

        bool readTransforms = repetition > 1;

        var instances = new List<WexBimShapeInstance>(repetition);
        for (int j = 0; j < repetition; j++)
        {
            instances.Add(new WexBimShapeInstance
            {
                ProductLabel = br.ReadInt32(),
                TypeId = br.ReadInt16(),
                InstanceLabel = br.ReadInt32(),
                StyleId = br.ReadInt32(),
                Transform = readTransforms ? ReadMatrix4x4(br) : Matrix4x4.Identity
            });
        }

        // v3+ prefixes geometry data with a byte length
        (float[] positions, float[] normals, int[] indices) meshData;
        if (scene.Version >= 3)
        {
            int dataLength = br.ReadInt32();
            if (dataLength == 0)
            {
                meshData = (Array.Empty<float>(), Array.Empty<float>(), Array.Empty<int>());
            }
            else
            {
                byte[] geomBytes = br.ReadBytes(dataLength);
                using var ms = new MemoryStream(geomBytes);
                using var gbr = new BinaryReader(ms);
                meshData = ReadTriangulation(gbr);
            }
        }
        else
        {
            meshData = ReadTriangulation(br);
        }

        foreach (var inst in instances)
        {
            if (meshData.positions.Length > 0)
            {
                scene.Meshes.Add(new WexBimMesh
                {
                    Positions = meshData.positions,
                    Normals = meshData.normals,
                    Indices = meshData.indices,
                    StyleId = inst.StyleId,
                    ProductLabel = inst.ProductLabel,
                    Transform = inst.Transform
                });
            }
        }

        scene.Shapes.Add(new WexBimShape
        {
            Instances = instances,
            VertexCount = meshData.positions.Length / 3,
            TriangleCount = meshData.indices.Length / 3
        });
    }

    private static Matrix4x4 ReadMatrix4x4(BinaryReader br)
    {
        float m11 = (float)br.ReadDouble();
        float m12 = (float)br.ReadDouble();
        float m13 = (float)br.ReadDouble();
        float m14 = (float)br.ReadDouble();
        float m21 = (float)br.ReadDouble();
        float m22 = (float)br.ReadDouble();
        float m23 = (float)br.ReadDouble();
        float m24 = (float)br.ReadDouble();
        float m31 = (float)br.ReadDouble();
        float m32 = (float)br.ReadDouble();
        float m33 = (float)br.ReadDouble();
        float m34 = (float)br.ReadDouble();
        float m41 = (float)br.ReadDouble();
        float m42 = (float)br.ReadDouble();
        float m43 = (float)br.ReadDouble();
        float m44 = (float)br.ReadDouble();

        return new Matrix4x4(
            m11, m12, m13, m14,
            m21, m22, m23, m24,
            m31, m32, m33, m34,
            m41, m42, m43, m44);
    }

    /// <summary>
    /// Reads a mesh triangulation block and expands it into flat position, normal, and index arrays.
    /// </summary>
    private static (float[] positions, float[] normals, int[] indices) ReadTriangulation(BinaryReader br)
    {
        byte version = br.ReadByte();
        int vertexCount = br.ReadInt32();
        int triangleCount = br.ReadInt32();

        if (vertexCount == 0 && triangleCount == 0)
        {
            br.ReadInt32(); // face count
            return (Array.Empty<float>(), Array.Empty<float>(), Array.Empty<int>());
        }

        if (triangleCount == 0)
        {
            for (int j = 0; j < vertexCount * 3; j++) br.ReadSingle();
            br.ReadInt32(); // face count
            return (Array.Empty<float>(), Array.Empty<float>(), Array.Empty<int>());
        }

        // Read vertex positions
        var positions = new float[vertexCount * 3];
        for (int j = 0; j < vertexCount * 3; j++)
            positions[j] = br.ReadSingle();

        // Determine index reader based on vertex count
        Func<BinaryReader, int> readIndex;
        if (vertexCount <= 0xFF)
            readIndex = r => r.ReadByte();
        else if (vertexCount <= 0xFFFF)
            readIndex = r => r.ReadUInt16();
        else
            readIndex = r => r.ReadInt32();

        int faceCount = br.ReadInt32();

        // Expand vertices with per-vertex normals
        var expandedPositions = new List<float>(triangleCount * 9);
        var expandedNormals = new List<float>(triangleCount * 9);
        var expandedIndices = new List<int>(triangleCount * 3);
        int currentVertex = 0;

        for (int f = 0; f < faceCount; f++)
        {
            int triCountSigned = br.ReadInt32();
            bool isPlanar = triCountSigned > 0;
            int faceTriCount = Math.Abs(triCountSigned);

            if (isPlanar)
            {
                byte nu = br.ReadByte();
                byte nv = br.ReadByte();
                var normal = DecodeNormal(nu, nv);

                for (int t = 0; t < faceTriCount; t++)
                {
                    for (int v = 0; v < 3; v++)
                    {
                        int idx = readIndex(br);
                        int posBase = idx * 3;

                        expandedPositions.Add(positions[posBase]);
                        expandedPositions.Add(positions[posBase + 1]);
                        expandedPositions.Add(positions[posBase + 2]);

                        expandedNormals.Add(normal.X);
                        expandedNormals.Add(normal.Y);
                        expandedNormals.Add(normal.Z);

                        expandedIndices.Add(currentVertex++);
                    }
                }
            }
            else
            {
                for (int t = 0; t < faceTriCount; t++)
                {
                    for (int v = 0; v < 3; v++)
                    {
                        int idx = readIndex(br);
                        byte nu = br.ReadByte();
                        byte nv = br.ReadByte();
                        var normal = DecodeNormal(nu, nv);

                        int posBase = idx * 3;

                        expandedPositions.Add(positions[posBase]);
                        expandedPositions.Add(positions[posBase + 1]);
                        expandedPositions.Add(positions[posBase + 2]);

                        expandedNormals.Add(normal.X);
                        expandedNormals.Add(normal.Y);
                        expandedNormals.Add(normal.Z);

                        expandedIndices.Add(currentVertex++);
                    }
                }
            }
        }

        return (expandedPositions.ToArray(), expandedNormals.ToArray(), expandedIndices.ToArray());
    }

    /// <summary>
    /// Decodes a packed (u, v) normal into a unit vector using spherical coordinates.
    /// </summary>
    private static Vector3 DecodeNormal(byte u, byte v)
    {
        double lon = u / PackSize * Math.PI * 2.0;
        double lat = v / PackSize * Math.PI;

        float y = (float)Math.Cos(lat);
        float x = (float)(Math.Sin(lon) * Math.Sin(lat));
        float z = (float)(Math.Cos(lon) * Math.Sin(lat));

        return new Vector3(x, y, z);
    }
}

/// <summary>
/// Parsed WexBIM scene containing header, product, style, region, shape, and mesh data.
/// </summary>
public sealed class WexBimScene
{
    public byte Version { get; set; }
    public float OneMeter { get; set; }
    public Vector3 Wcs { get; set; }
    public int TotalVertexCount { get; set; }
    public int TotalTriangleCount { get; set; }
    public Vector3 BoundsMin { get; set; }
    public Vector3 BoundsMax { get; set; }
    public List<WexBimRegion> Regions { get; } = new();
    public List<WexBimStyle> Styles { get; } = new();
    public List<WexBimProduct> Products { get; } = new();
    public List<WexBimShape> Shapes { get; } = new();

    /// <summary>
    /// Triangulated meshes with expanded positions, normals, and indices.
    /// One mesh per shape instance, ready for rendering.
    /// </summary>
    public List<WexBimMesh> Meshes { get; } = new();

    /// <summary>
    /// Optional type name mapping populated from IFC model metadata after reading.
    /// </summary>
    public Dictionary<short, string> TypeNames { get; set; }

    /// <summary>Total shape instances across all shapes.</summary>
    public int TotalShapeInstances => Shapes.Sum(s => s.Instances.Count);
}

/// <summary>
/// A triangulated mesh with positions, normals, and indices for a single shape instance.
/// </summary>
public sealed class WexBimMesh
{
    public float[] Positions { get; init; } = Array.Empty<float>();
    public float[] Normals { get; init; } = Array.Empty<float>();
    public int[] Indices { get; init; } = Array.Empty<int>();
    public int StyleId { get; init; }
    public int ProductLabel { get; init; }
    public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;
}

public sealed class WexBimShape
{
    public List<WexBimShapeInstance> Instances { get; init; } = new();
    public int VertexCount { get; init; }
    public int TriangleCount { get; init; }
}

public sealed class WexBimShapeInstance
{
    public int ProductLabel { get; init; }
    public short TypeId { get; init; }
    public int InstanceLabel { get; init; }
    public int StyleId { get; init; }
    public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;
}

public readonly struct WexBimStyle
{
    public int Id { get; init; }
    public float R { get; init; }
    public float G { get; init; }
    public float B { get; init; }
    public float A { get; init; }
}

public readonly struct WexBimProduct
{
    public int Label { get; init; }
    public short TypeId { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
}

public readonly struct WexBimRegion
{
    public int Population { get; init; }
    public Vector3 Centre { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
}
