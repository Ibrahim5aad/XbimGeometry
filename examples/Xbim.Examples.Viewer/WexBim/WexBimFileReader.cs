using System.Numerics;

namespace Xbim.Examples.Viewer.WexBim;

/// <summary>
/// Reads a WexBIM binary file and produces a <see cref="SceneModel"/> for rendering.
/// This is a standalone parser with no xbim library dependencies.
/// </summary>
public static class WexBimFileReader
{
    /// <summary>
    /// Magic number identifying a valid WexBIM file.
    /// </summary>
    private const int WexBimMagic = 94132117;

    /// <summary>
    /// Quantization divisor for spherical normal encoding.
    /// </summary>
    private const double PackSize = 252.0;

    /// <summary>
    /// Reads a WexBIM file from a file path.
    /// </summary>
    public static SceneModel ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>
    /// Reads a WexBIM file from an arbitrary stream.
    /// </summary>
    public static SceneModel Read(Stream stream)
    {
        using var br = new BinaryReader(stream);
        return Read(br);
    }

    /// <summary>
    /// Reads a WexBIM file from a BinaryReader.
    /// </summary>
    public static SceneModel Read(BinaryReader br)
    {
        var model = new SceneModel();

        // --- Header ---
        int magic = br.ReadInt32();
        if (magic != WexBimMagic)
            throw new InvalidDataException(
                $"Not a valid WexBIM file. Expected magic {WexBimMagic}, got {magic}.");

        model.Version = br.ReadByte();
        int shapeCount = br.ReadInt32();
        int _vertexCount = br.ReadInt32();   // total vertex count (informational)
        int _triangleCount = br.ReadInt32(); // total triangle count (informational)
        int _matrixCount = br.ReadInt32();   // total matrix count (informational)
        int productCount = br.ReadInt32();
        int styleCount = br.ReadInt32();
        model.OneMeter = br.ReadSingle();
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

            // Bounding box: 6 floats (minX, minY, minZ, maxX, maxY, maxZ)
            // Stored as XbimRect3D: X, Y, Z, SizeX, SizeY, SizeZ
            float bx = br.ReadSingle();
            float by = br.ReadSingle();
            float bz = br.ReadSingle();
            float sx = br.ReadSingle();
            float sy = br.ReadSingle();
            float sz = br.ReadSingle();

            // XbimRect3D stores origin + size, so max = origin + size
            var bmin = new Vector3(bx, by, bz);
            var bmax = new Vector3(bx + sx, by + sy, bz + sz);

            model.Regions.Add(new SceneRegion
            {
                Population = population,
                Centre = new Vector3(cx, cy, cz),
                BoundsMin = bmin,
                BoundsMax = bmax
            });

            globalMin = Vector3.Min(globalMin, bmin);
            globalMax = Vector3.Max(globalMax, bmax);
        }

        model.BoundsMin = regionCount > 0 ? globalMin : Vector3.Zero;
        model.BoundsMax = regionCount > 0 ? globalMax : Vector3.Zero;

        // --- Styles ---
        for (int i = 0; i < styleCount; i++)
        {
            model.Styles.Add(new SceneStyle
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

            // Bounding box stored as XbimRect3D: X, Y, Z, SizeX, SizeY, SizeZ
            float bx = br.ReadSingle();
            float by = br.ReadSingle();
            float bz = br.ReadSingle();
            float sx = br.ReadSingle();
            float sy = br.ReadSingle();
            float sz = br.ReadSingle();

            model.Products.Add(new SceneProduct
            {
                Label = label,
                TypeId = typeId,
                BoundsMin = new Vector3(bx, by, bz),
                BoundsMax = new Vector3(bx + sx, by + sy, bz + sz)
            });
        }

        // --- Shapes ---
        for (int i = 0; i < shapeCount; i++)
        {
            int repetition = br.ReadInt32();
            if (repetition < 1) continue;

            if (repetition > 1)
            {
                // Multi-instance shape: read all instance metadata first, then one shared triangulation
                var instances = new (int productLabel, short typeId, int instanceLabel, int styleId, Matrix4x4 transform)[repetition];
                for (int j = 0; j < repetition; j++)
                {
                    int productLabel = br.ReadInt32();
                    short typeId = br.ReadInt16();
                    int instanceLabel = br.ReadInt32();
                    int styleId = br.ReadInt32();

                    // 4x4 matrix stored as 16 doubles (row-major)
                    var m = ReadMatrix4x4(br);
                    instances[j] = (productLabel, typeId, instanceLabel, styleId, m);
                }

                // Read the shared triangulation once
                var meshData = ReadTriangulation(br);

                // Create one SceneMesh per instance, sharing the same geometry but with different transforms
                foreach (var inst in instances)
                {
                    if (meshData.positions.Length == 0) continue;
                    model.Meshes.Add(new SceneMesh
                    {
                        Positions = meshData.positions,
                        Normals = meshData.normals,
                        Indices = meshData.indices,
                        StyleId = inst.styleId,
                        ProductLabel = inst.productLabel,
                        Transform = inst.transform
                    });
                }
            }
            else
            {
                // Single-instance shape: no transform matrix in the stream
                int productLabel = br.ReadInt32();
                short typeId = br.ReadInt16();
                int instanceLabel = br.ReadInt32();
                int styleId = br.ReadInt32();

                var meshData = ReadTriangulation(br);
                if (meshData.positions.Length == 0) continue;

                model.Meshes.Add(new SceneMesh
                {
                    Positions = meshData.positions,
                    Normals = meshData.normals,
                    Indices = meshData.indices,
                    StyleId = styleId,
                    ProductLabel = productLabel,
                    Transform = Matrix4x4.Identity
                });
            }
        }

        return model;
    }

    /// <summary>
    /// Reads a 4x4 transformation matrix stored as 16 doubles.
    /// </summary>
    private static Matrix4x4 ReadMatrix4x4(BinaryReader br)
    {
        // XbimMatrix3D stores as 16 doubles in row-major order:
        // M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34, OffsetX, OffsetY, OffsetZ, M44
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
        float m41 = (float)br.ReadDouble(); // OffsetX
        float m42 = (float)br.ReadDouble(); // OffsetY
        float m43 = (float)br.ReadDouble(); // OffsetZ
        float m44 = (float)br.ReadDouble();

        return new Matrix4x4(
            m11, m12, m13, m14,
            m21, m22, m23, m24,
            m31, m32, m33, m34,
            m41, m42, m43, m44);
    }

    /// <summary>
    /// Reads a mesh triangulation block from the binary stream.
    /// Returns flat arrays of positions, normals, and indices suitable for rendering.
    /// </summary>
    private static (float[] positions, float[] normals, int[] indices) ReadTriangulation(BinaryReader br)
    {
        byte version = br.ReadByte(); // mesh version
        int vertexCount = br.ReadInt32();
        int triangleCount = br.ReadInt32();

        if (vertexCount == 0 && triangleCount == 0)
        {
            // Fully empty mesh: only face count follows
            br.ReadInt32(); // face count (expected 0)
            return (Array.Empty<float>(), Array.Empty<float>(), Array.Empty<int>());
        }

        if (triangleCount == 0)
        {
            // Vertices present but no triangles — must still consume the vertex
            // positions and face count to keep the stream aligned.
            for (int i = 0; i < vertexCount * 3; i++) br.ReadSingle();
            br.ReadInt32(); // face count (expected 0)
            return (Array.Empty<float>(), Array.Empty<float>(), Array.Empty<int>());
        }

        // Read vertex positions: vertexCount * 3 floats
        var positions = new float[vertexCount * 3];
        for (int i = 0; i < vertexCount * 3; i++)
        {
            positions[i] = br.ReadSingle();
        }

        // Determine index reader based on vertex count
        Func<BinaryReader, int> readIndex;
        if (vertexCount <= 0xFF)
            readIndex = r => r.ReadByte();
        else if (vertexCount <= 0xFFFF)
            readIndex = r => r.ReadUInt16();
        else
            readIndex = r => r.ReadInt32();

        // Read faces
        int faceCount = br.ReadInt32();

        // We'll collect expanded vertices (position + normal per index) because
        // non-planar faces have per-vertex normals that differ from planar face normals.
        // To support both, we expand all vertices with their associated normals.
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
                // Read the single packed normal for this planar face
                byte nu = br.ReadByte();
                byte nv = br.ReadByte();
                var normal = DecodeNormal(nu, nv);

                // Read triangle indices
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
                // Non-planar: each vertex has its own packed normal
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
    /// Matches the encoding in WexBimMesh: lon = u/252 * 2*PI, lat = v/252 * PI.
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
