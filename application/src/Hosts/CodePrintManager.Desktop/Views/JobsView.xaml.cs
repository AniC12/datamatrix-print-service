using System.Windows;
using System.Windows.Controls;
using CodePrintManager.Desktop.ViewModels;

namespace CodePrintManager.Desktop.Views;

public partial class JobsView : UserControl
{
    public JobsView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is JobsViewModel vm)
            await vm.LoadJobsCommand.ExecuteAsync(null);
    }
}
