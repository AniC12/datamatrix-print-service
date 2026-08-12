using System.Windows;
using CodePrintManager.Domain.Interfaces;

namespace CodePrintManager.Desktop;

public class WpfDialogService : IDialogService
{
    public bool Confirm(string message, string title)
    {
        var result = MessageBox.Show(message, title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    public void ShowWarning(string message, string title)
    {
        MessageBox.Show(message, title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
