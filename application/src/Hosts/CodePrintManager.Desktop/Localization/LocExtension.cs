using System.Windows.Data;
using System.Windows.Markup;

namespace CodePrintManager.Desktop.Localization;

/// <summary>
/// XAML markup extension for localized strings.
/// Usage: {loc:Loc Nav_Dashboard}
/// Binds to TranslationSource.Instance[key] and auto-updates on language switch.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = TranslationSource.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
