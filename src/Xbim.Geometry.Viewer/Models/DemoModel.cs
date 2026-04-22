namespace Xbim.Geometry.Viewer;

/// <summary>
/// A pre-configured model that can be loaded from a URL.
/// </summary>
public sealed class DemoModel
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string? Description { get; init; }
}
