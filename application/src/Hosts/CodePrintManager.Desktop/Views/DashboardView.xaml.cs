using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodePrintManager.Desktop.ViewModels;
using CodePrintManager.Desktop.ViewModels.Components;

namespace CodePrintManager.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
            await vm.RefreshCommand.ExecuteAsync(null);
    }

    private void OnPrinterCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PrinterCardViewModel card)
            card.ClickCardCommand.Execute(null);
    }
}
