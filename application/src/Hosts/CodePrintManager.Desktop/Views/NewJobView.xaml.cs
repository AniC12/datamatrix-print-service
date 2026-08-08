using System.Windows;
using System.Windows.Controls;
using CodePrintManager.Desktop.ViewModels;

namespace CodePrintManager.Desktop.Views;

public partial class NewJobView : UserControl
{
    public NewJobView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewJobViewModel vm)
            await vm.LoadCommand.ExecuteAsync(null);
    }
}
