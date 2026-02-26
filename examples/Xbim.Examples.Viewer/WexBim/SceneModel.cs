using System.Numerics;

namespace Xbim.Examples.Viewer.WexBim;

/// <summary>
/// Top-level container for all geometry parsed from a WexBIM file.
/// Holds meshes, styles, products, regions, and global metadata.
/// </summary>
public sealed class SceneModel
{
    /// <summary>
    /// Triangulated meshes ready for rendering, each associated with a style and product.
    /// </summary>
    public List<SceneMesh> Meshes { get; } = new();

    /// <summary>
    /// Visual styles (materials) referenced by meshes.
    /// </summary>
    public List<SceneStyle> Styles { get; } = new();

    /// <summary>
    /// IFC products (building elements) with their bounding boxes.
    /// </summary>
    public List<SceneProduct> Products { get; } = new();

    /// <summary>
    /// Spatial regions used for level-of-detail and culling.
    /// </summary>
    public List<SceneRegion> Regions { get; } = new();

    /// <summary>
    /// Scale factor: how many model units equal one meter.
    /// </summary>
    public float OneMeter { get; set; } = 1.0f;

    /// <summary>
    /// File format version.
    /// </summary>
    public byte Version { get; set; }

    /// <summary>
    /// Axis-aligned bounding box minimum corner (computed from regions).
    /// </summary>
    public Vector3 BoundsMin { get; set; }

    /// <summary>
    /// Axis-aligned bounding box maximum corner (computed from regions).
    /// </summary>
    public Vector3 BoundsMax { get; set; }

    /// <summary>
    /// Maps type IDs to IFC type names (e.g. "IfcWall").
    /// Populated from the IFC model's metadata when loading from IFC files;
    /// null when loading standalone WexBIM files.
    /// </summary>
    public Dictionary<short, string>? TypeNames { get; set; }
}

/// <summary>
/// A triangulated mesh with interleaved position and normal data, ready for GPU upload.
/// </summary>
public sealed class SceneMesh
{
    /// <summary>
    /// Vertex positions as a flat array [x0,y0,z0, x1,y1,z1, ...].
    /// </summary>
    public float[] Positions { get; init; } = Array.Empty<float>();

    /// <summary>
    /// Vertex normals as a flat array [nx0,ny0,nz0, nx1,ny1,nz1, ...].
    /// </summary>
    public float[] Normals { get; init; } = Array.Empty<float>();

    /// <summary>
    /// Triangle indices into the position/normal arrays.
    /// </summary>
    public int[] Indices { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Style (material) identifier for this mesh.
    /// </summary>
    public int StyleId { get; init; }

    /// <summary>
    /// IFC product label that owns this mesh.
    /// </summary>
    public int ProductLabel { get; init; }

    /// <summary>
    /// World transform applied to this mesh instance.
    /// </summary>
    public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;
}

/// <summary>
/// A visual style (material color) for rendering.
/// </summary>
public readonly struct SceneStyle
{
    public int Id { get; init; }
    public float R { get; init; }
    public float G { get; init; }
    public float B { get; init; }
    public float A { get; init; }
}

/// <summary>
/// An IFC product (building element) with spatial bounds.
/// </summary>
public readonly struct SceneProduct
{
    public int Label { get; init; }
    public short TypeId { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
}

/// <summary>
/// A spatial region grouping nearby geometry for efficient processing.
/// </summary>
public readonly struct SceneRegion
{
    public int Population { get; init; }
    public Vector3 Centre { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
}
