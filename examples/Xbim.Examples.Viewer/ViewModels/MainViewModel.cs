using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Xbim.Common;
using Xbim.Examples.Viewer.WexBim;
using Xbim.Ifc;
using Xbim.ModelGeometry.Scene;

namespace Xbim.Examples.Viewer.ViewModels;

/// <summary>
/// Main view model for the WexBIM viewer application.
/// Manages file loading, status display, and viewport commands.
/// </summary>
internal sealed partial class MainViewModel : ObservableObject
{
    private static readonly HashSet<string> IfcExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ifc", ".ifczip", ".ifcxml"
    };

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private int _vertexCount;

    [ObservableProperty]
    private int _triangleCount;

    [ObservableProperty]
    private int _productCount;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// View model for the product list sidebar.
    /// </summary>
    public ProductListViewModel ProductList { get; } = new();

    /// <summary>
    /// Raised when a new scene model has been loaded and is ready for rendering.
    /// </summary>
    public event Action<SceneModel>? SceneLoaded;

    /// <summary>
    /// Raised when the user requests the camera to fit the scene.
    /// </summary>
    public event Action? FitViewRequested;

    /// <summary>
    /// Loads an IFC or WexBIM file from the given path and raises <see cref="SceneLoaded"/>.
    /// IFC files are converted to WexBIM in memory before loading.
    /// </summary>
    public async Task LoadFileAsync(string path)
    {
        try
        {
            IsLoading = true;
            FileName = Path.GetFileName(path);
            var ext = Path.GetExtension(path);

            SceneModel model;
            if (IfcExtensions.Contains(ext))
            {
                StatusText = $"Opening {FileName}...";
                model = await Task.Run(() => ConvertIfcToSceneModel(path));
            }
            else
            {
                StatusText = "Loading...";
                model = WexBimFileReader.ReadFile(path);
            }

            VertexCount = model.Meshes.Sum(m => m.Positions.Length / 3);
            TriangleCount = model.Meshes.Sum(m => m.Indices.Length / 3);
            ProductCount = model.Products.Count;
            StatusText = $"{FileName} — {VertexCount:N0} vertices, {TriangleCount:N0} triangles, {ProductCount} products";

            ProductList.LoadProducts(model);
            SceneLoaded?.Invoke(model);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private SceneModel ConvertIfcToSceneModel(string ifcPath)
    {
        using var ifcModel = IfcStore.Open(ifcPath);
        var context = new Xbim3DModelContext(ifcModel);
        context.CreateContext();

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.Default, leaveOpen: true))
        {
            ifcModel.SaveAsWexBim(bw);
        }

        ms.Position = 0;
        var model = WexBimFileReader.Read(ms);

        // Build type name mapping from the IFC model's metadata
        var typeNames = new Dictionary<short, string>();
        foreach (var product in model.Products)
        {
            if (!typeNames.ContainsKey(product.TypeId))
            {
                var type = ifcModel.Metadata.GetType(product.TypeId);
                if (type != null)
                    typeNames[product.TypeId] = type.Name;
            }
        }
        model.TypeNames = typeNames;

        return model;
    }

    [RelayCommand]
    private void FitView()
    {
        FitViewRequested?.Invoke();
    }
}
