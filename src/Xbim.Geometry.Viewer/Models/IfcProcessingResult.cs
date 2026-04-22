namespace Xbim.Geometry.Viewer;

/// <summary>
/// Result returned after processing an IFC file into viewer geometry.
/// </summary>
public sealed class IfcProcessingResult
{
    public int ProductCount { get; init; }
    public TimeSpan Duration { get; init; }
    public byte[]? WexBimData { get; init; }
}

/// <summary>
/// Reports progress during IFC processing.
/// </summary>
public sealed class IfcProcessingProgress
{
    public string Stage { get; init; } = "";
    public int Percent { get; init; }
}
