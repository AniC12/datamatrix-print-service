using System.Text.Json;
using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CodePrintManager.Application.Services;

public class LocalizationService : ILocalizationService
{
    private readonly string _localizationDir;
    private readonly ILogger<LocalizationService> _logger;

    private readonly Dictionary<string, Dictionary<string, string>> _cache = new();
    private readonly Dictionary<string, string> _displayNames = new();
    private readonly List<string> _availableLanguages = new();

    private Dictionary<string, string> _currentDict = new();
    private Dictionary<string, string> _fallbackDict = new();

    public string CurrentLanguage { get; private set; } = "en";
    public IReadOnlyList<string> AvailableLanguages => _availableLanguages;
    public IReadOnlyDictionary<string, string> LanguageDisplayNames => _displayNames;

    public event Action? LanguageChanged;

    public LocalizationService(string localizationDir, ILogger<LocalizationService> logger)
    {
        _localizationDir = localizationDir;
        _logger = logger;

        DiscoverLanguages();
        LoadLanguage("en"); // always load English as fallback
        _fallbackDict = GetOrLoad("en");
        _currentDict = _fallbackDict;
    }

    public string this[string key]
    {
        get
        {
            if (_currentDict.TryGetValue(key, out var value))
                return value;
            if (_fallbackDict.TryGetValue(key, out var fallback))
            {
                _logger.LogDebug("Localization key '{Key}' missing in '{Lang}', using English fallback", key, CurrentLanguage);
                return fallback;
            }
            _logger.LogWarning("Localization key '{Key}' not found in any language", key);
            return key;
        }
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Format error for key '{Key}' with template '{Template}'", key, template);
            return template;
        }
    }

    public void SetLanguage(string languageCode)
    {
        if (string.Equals(CurrentLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
            return;

        if (!_availableLanguages.Contains(languageCode))
        {
            _logger.LogWarning("Language '{Lang}' not available, staying on '{Current}'", languageCode, CurrentLanguage);
            return;
        }

        _currentDict = GetOrLoad(languageCode);
        CurrentLanguage = languageCode;
        _logger.LogInformation("Language switched to '{Lang}'", languageCode);
        LanguageChanged?.Invoke();
    }

    private void DiscoverLanguages()
    {
        if (!Directory.Exists(_localizationDir))
        {
            _logger.LogWarning("Localization directory not found: {Dir}", _localizationDir);
            _availableLanguages.Add("en");
            _displayNames["en"] = "English";
            return;
        }

        foreach (var file in Directory.GetFiles(_localizationDir, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(file);
            try
            {
                var json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);

                var nativeName = code;
                if (doc.RootElement.TryGetProperty("_meta", out var meta) &&
                    meta.TryGetProperty("nativeName", out var nameEl))
                {
                    nativeName = nameEl.GetString() ?? code;
                }

                _availableLanguages.Add(code);
                _displayNames[code] = nativeName;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read language file: {File}", file);
            }
        }

        // Ensure "en" is first
        if (_availableLanguages.Remove("en"))
            _availableLanguages.Insert(0, "en");

        if (_availableLanguages.Count == 0)
        {
            _availableLanguages.Add("en");
            _displayNames["en"] = "English";
        }

        _logger.LogInformation("Discovered languages: {Languages}", string.Join(", ", _availableLanguages));
    }

    private Dictionary<string, string> GetOrLoad(string languageCode)
    {
        if (_cache.TryGetValue(languageCode, out var cached))
            return cached;

        var dict = LoadLanguage(languageCode);
        _cache[languageCode] = dict;
        return dict;
    }

    private Dictionary<string, string> LoadLanguage(string languageCode)
    {
        var path = Path.Combine(_localizationDir, $"{languageCode}.json");
        if (!File.Exists(path))
        {
            _logger.LogWarning("Language file not found: {Path}", path);
            return new Dictionary<string, string>();
        }

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string>();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // Skip _meta section
                if (prop.Name == "_meta")
                    continue;

                if (prop.Value.ValueKind == JsonValueKind.String)
                    dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }

            _logger.LogInformation("Loaded {Count} keys from {File}", dict.Count, path);
            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse language file: {Path}", path);
            return new Dictionary<string, string>();
        }
    }
}
