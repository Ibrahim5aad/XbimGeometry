using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Xbim.Examples.Viewer.ViewModels;

namespace Xbim.Examples.Viewer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        ProductList.DataContext = _viewModel.ProductList;

        _viewModel.SceneLoaded += model => Viewport.LoadModel(model);
        _viewModel.FitViewRequested += () => Viewport.FitCamera();
        _viewModel.ProductList.SelectionChanged += OnProductSelectionChanged;
        _viewModel.ProductList.ZoomRequested += (_, min, max) => Viewport.ZoomToProduct(min, max);
        _viewModel.ProductList.IsolateRequested += label => Viewport.IsolateProduct(label);
        _viewModel.ProductList.ShowAllRequested += () => Viewport.ShowAll();

        // Load file from command-line argument if provided
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
        {
            Opened += async (_, _) => await _viewModel.LoadFileAsync(args[1]);
        }
    }

    private void OnProductSelectionChanged(int productLabel)
    {
        Viewport.HighlightProduct(productLabel);
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open IFC or WexBIM File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("IFC Files") { Patterns = new[] { "*.ifc", "*.ifczip", "*.ifcxml" } },
                new FilePickerFileType("WexBIM Files") { Patterns = new[] { "*.wexbim" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is not null)
            await _viewModel.LoadFileAsync(path);
    }
}
