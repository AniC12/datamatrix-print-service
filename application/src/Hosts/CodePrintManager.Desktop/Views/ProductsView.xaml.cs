using System.Windows;
using System.Windows.Controls;
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
        if (DataContext is ProductsViewModel vm && e.NewValue is ProductNode node)
            vm.SelectedProduct = node;
    }
}
