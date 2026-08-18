namespace CodePrintManager.Domain.Interfaces;

public interface ILocalizationService
{
    /// <summary>Get a translated string by key. Returns English fallback if missing, then raw key.</summary>
    string this[string key] { get; }

    /// <summary>Current language code (e.g., "en", "ru", "hy").</summary>
    string CurrentLanguage { get; }

    /// <summary>Available language codes discovered from JSON files.</summary>
    IReadOnlyList<string> AvailableLanguages { get; }

    /// <summary>Display names for each language in its native script (e.g., "Русский").</summary>
    IReadOnlyDictionary<string, string> LanguageDisplayNames { get; }

    /// <summary>Switch to a different language. Fires LanguageChanged.</summary>
    void SetLanguage(string languageCode);

    /// <summary>Fired after language changes. Subscribers should refresh displayed text.</summary>
    event Action? LanguageChanged;

    /// <summary>Get translated string with format arguments (string.Format).</summary>
    string Format(string key, params object[] args);
}
