namespace CodePrintManager.Domain.Interfaces;

public interface IDialogService
{
    /// <summary>
    /// Show a confirmation dialog with Yes/No buttons.
    /// Returns true if the user confirmed (Yes).
    /// </summary>
    bool Confirm(string message, string title);

    /// <summary>
    /// Show a warning/info dialog with an OK button (no choice needed).
    /// </summary>
    void ShowWarning(string message, string title);
}
