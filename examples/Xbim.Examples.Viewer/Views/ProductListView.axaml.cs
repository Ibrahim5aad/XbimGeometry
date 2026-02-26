using Avalonia.Controls;
using Xbim.Examples.Viewer.ViewModels;

namespace Xbim.Examples.Viewer.Views;

public partial class ProductListView : UserControl
{
    public ProductListView()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        ProductTree.SelectionChanged += OnTreeSelectionChanged;
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ProductListViewModel vm)
            return;

        if (ProductTree.SelectedItem is ProductItem item)
        {
            vm.SelectedProduct = item;
        }
        else
        {
            vm.SelectedProduct = null;
        }
    }
}
