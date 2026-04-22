using Xbim.Geometry.Viewer.Components;

namespace Xbim.Geometry.Viewer;

/// <summary>
/// Processes IFC model data and streams the resulting geometry into an <see cref="XbimViewer"/>.
/// Register an implementation in DI to enable IFC file support in the <see cref="FileLoaderPanel"/>.
/// </summary>
public interface IIfcProcessingService
{
    Task<IfcProcessingResult> ProcessAsync(
        Stream data,
        string fileName,
        XbimViewer viewer,
        IProgress<IfcProcessingProgress>? progress = null,
        CancellationToken ct = default);
}
