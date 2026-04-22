namespace Xbim.Geometry.Viewer;

/// <summary>
/// Represents a model that has been loaded into the viewer.
/// </summary>
public sealed class LoadedModel
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Source { get; init; } = "";
    public ModelSourceType SourceType { get; init; }
    public long? SizeBytes { get; init; }
    public bool IsVisible { get; set; } = true;
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;
}

public enum ModelSourceType
{
    File,
    Url
}
