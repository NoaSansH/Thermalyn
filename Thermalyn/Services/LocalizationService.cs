using System.Globalization;
using System.Windows;

namespace Thermalyn.Services;

// A ResourceDictionary is not safe to read concurrently. Get runs on worker threads, so Apply
// snapshots the strings.
public static class LocalizationService
{
    public const string English = "en";
    public const string French = "fr";
    public const string Spanish = "es";
    public const string German = "de";

    private static ResourceDictionary? _current;
    private static IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static IReadOnlyList<string> Available { get; } = [English, French, Spanish, German];

    public static string SystemDefault =>
        Available.FirstOrDefault(code =>
            code.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
        ?? English;

    public static string Normalize(string? language) =>
        Available.Contains(language, StringComparer.OrdinalIgnoreCase) ? language!.ToLowerInvariant() : SystemDefault;

    // UI thread only.
    public static void Apply(string? language)
    {
        var code = Normalize(language);
        var dictionary = new ResourceDictionary { Source = new Uri($"/Thermalyn;component/Themes/Strings.{code}.xaml", UriKind.Relative) };
        var merged = Application.Current.Resources.MergedDictionaries;

        if (_current is not null) merged.Remove(_current);
        merged.Add(dictionary);
        _current = dictionary;

        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in dictionary.Keys)
        {
            if (key is string name && dictionary[key] is string value) snapshot[name] = value;
        }
        _strings = snapshot;
    }

    public static string Get(string key) => _strings.TryGetValue(key, out var value) ? value : key;

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
}
