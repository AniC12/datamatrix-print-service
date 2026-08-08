using System.Windows;
using System.Windows.Controls;
using CodePrintManager.Desktop.ViewModels;

namespace CodePrintManager.Desktop.Views;

public partial class PrintersView : UserControl
{
    public PrintersView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrintersViewModel vm)
            await vm.LoadPrintersCommand.ExecuteAsync(null);
    }
}
