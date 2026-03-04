using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Xbim.Examples.Viewer.WexBim;
using Xbim.Geometry.Scene;

namespace Xbim.Examples.Viewer.ViewModels;

/// <summary>
/// View model for the product list sidebar.
/// Displays IFC products grouped by their entity type.
/// </summary>
internal sealed partial class ProductListViewModel : ObservableObject
{
    [ObservableProperty]
    private ProductItem? _selectedProduct;

    [ObservableProperty]
    private bool _isIsolating;

    /// <summary>
    /// Product items grouped by IFC type name for display in the sidebar.
    /// </summary>
    public ObservableCollection<ProductGroup> Groups { get; } = new();

    /// <summary>
    /// Raised when the selected product changes. The argument is the product label,
    /// or -1 if the selection was cleared.
    /// </summary>
    public event Action<int>? SelectionChanged;

    /// <summary>
    /// Raised when the user requests to zoom to a product's bounding box.
    /// Arguments: product label, bounds min, bounds max.
    /// </summary>
    public event Action<int, Vector3, Vector3>? ZoomRequested;

    /// <summary>
    /// Raised when the user requests to isolate a product (show only its geometry).
    /// </summary>
    public event Action<int>? IsolateRequested;

    /// <summary>
    /// Raised when the user requests to show all products again.
    /// </summary>
    public event Action? ShowAllRequested;

    // Per-product bounding boxes for zoom
    private Dictionary<int, (Vector3 Min, Vector3 Max)> _productBounds = new();

    partial void OnSelectedProductChanged(ProductItem? value)
    {
        SelectionChanged?.Invoke(value?.Label ?? -1);
        ZoomToSelectedCommand.NotifyCanExecuteChanged();
        IsolateSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Populates the product list from a scene model.
    /// Groups products by their IFC type, sorted alphabetically by type name.
    /// Products with no resolvable type name are collected into an "Untyped" group at the end.
    /// </summary>
    public void LoadProducts(WexBimScene model)
    {
        Groups.Clear();
        SelectedProduct = null;
        IsIsolating = false;

        // Build bounds lookup
        _productBounds = model.Products.ToDictionary(
            p => p.Label,
            p => (p.BoundsMin, p.BoundsMax));

        // Resolve type name: prefer model metadata (from IFC), then hardcoded dictionary
        string? ResolveName(short typeId)
        {
            if (model.TypeNames is not null && model.TypeNames.TryGetValue(typeId, out var name))
                return StripIfcPrefix(name);
            if (IfcTypeNames.IsKnown(typeId))
                return IfcTypeNames.GetShortName(typeId);
            return null;
        }

        var typed = new List<WexBimProduct>();
        var untyped = new List<WexBimProduct>();

        foreach (var p in model.Products)
        {
            if (ResolveName(p.TypeId) is not null)
                typed.Add(p);
            else
                untyped.Add(p);
        }

        // Typed groups sorted alphabetically
        var groups = typed
            .GroupBy(p => p.TypeId)
            .OrderBy(g => ResolveName(g.Key))
            .Select(g => new ProductGroup(
                ResolveName(g.Key)!,
                g.OrderBy(p => p.Label)
                    .Select(p => new ProductItem(p.Label, p.TypeId))
                    .ToList()))
            .ToList();

        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        // Untyped group at the end
        if (untyped.Count > 0)
        {
            var items = untyped
                .OrderBy(p => p.Label)
                .Select(p => new ProductItem(p.Label, p.TypeId))
                .ToList();
            Groups.Add(new ProductGroup("Untyped", items));
        }
    }

    private static string StripIfcPrefix(string name)
    {
        return name.StartsWith("Ifc", StringComparison.Ordinal) ? name.Substring(3) : name;
    }

    /// <summary>
    /// Clears the product list.
    /// </summary>
    public void Clear()
    {
        Groups.Clear();
        SelectedProduct = null;
        IsIsolating = false;
        _productBounds.Clear();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProduct))]
    private void ZoomToSelected()
    {
        if (SelectedProduct is null) return;
        if (!_productBounds.TryGetValue(SelectedProduct.Label, out var bounds)) return;
        ZoomRequested?.Invoke(SelectedProduct.Label, bounds.Min, bounds.Max);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProduct))]
    private void IsolateSelected()
    {
        if (SelectedProduct is null) return;
        IsIsolating = true;
        ShowAllCommand.NotifyCanExecuteChanged();
        IsolateRequested?.Invoke(SelectedProduct.Label);
    }

    [RelayCommand(CanExecute = nameof(IsIsolating))]
    private void ShowAll()
    {
        IsIsolating = false;
        ShowAllCommand.NotifyCanExecuteChanged();
        ShowAllRequested?.Invoke();
    }

    private bool HasSelectedProduct => SelectedProduct is not null;
}

/// <summary>
/// A group of products sharing the same IFC entity type.
/// </summary>
internal sealed class ProductGroup
{
    public string TypeName { get; }
    public int Count => Items.Count;
    public List<ProductItem> Items { get; }

    public ProductGroup(string typeName, List<ProductItem> items)
    {
        TypeName = typeName;
        Items = items;
    }
}

/// <summary>
/// A single IFC product entry in the sidebar list.
/// </summary>
internal sealed class ProductItem
{
    public int Label { get; }
    public short TypeId { get; }
    public string DisplayName { get; }

    public ProductItem(int label, short typeId)
    {
        Label = label;
        TypeId = typeId;
        DisplayName = $"#{label}";
    }
}
