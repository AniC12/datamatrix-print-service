using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodePrintManager.Desktop.ViewModels;
using CodePrintManager.Domain.Entities;

namespace CodePrintManager.Desktop.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProductsViewModel vm)
            await vm.LoadProductsCommand.ExecuteAsync(null);
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ProductsViewModel vm)
        {
            if (e.NewValue is ProductNode node)
                vm.SelectedProduct = node;
        }
    }

    /// <summary>
    /// Clicking the empty background of the TreeView clears the selection,
    /// allowing the user to add folders/products at root level.
    /// </summary>
    private void OnTreeBackgroundClick(object sender, MouseButtonEventArgs e)
    {
        // Only handle clicks directly on the TreeView background (not on items)
        if (e.OriginalSource is not TreeViewItem && sender is TreeView treeView)
        {
            // Check if we clicked on an actual tree item by hit-testing
            var hitResult = treeView.InputHitTest(e.GetPosition(treeView));
            if (hitResult is FrameworkElement fe && fe.DataContext is ProductNode)
                return; // Click was on a tree node, not background

            // Clear selection
            if (DataContext is ProductsViewModel vm)
                vm.SelectedProduct = null;

            // Deselect in the TreeView by clearing the visual selection
            if (treeView.SelectedItem != null)
            {
                var container = treeView.ItemContainerGenerator.ContainerFromItem(treeView.SelectedItem) as TreeViewItem;
                if (container != null)
                    container.IsSelected = false;
            }
        }
    }
}
