using System.ComponentModel;
using CodePrintManager.Domain.Interfaces;

namespace CodePrintManager.Desktop.Localization;

/// <summary>
/// Singleton bridge between ILocalizationService and WPF bindings.
/// Fires PropertyChanged("Item[]") on language switch so all {loc:Loc} bindings update.
/// </summary>
public class TranslationSource : INotifyPropertyChanged
{
    public static TranslationSource Instance { get; } = new();

    private ILocalizationService? _service;

    public string this[string key] => _service?[key] ?? key;

    public void Initialize(ILocalizationService service)
    {
        _service = service;
        service.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
