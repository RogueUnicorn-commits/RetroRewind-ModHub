using System.IO;
using System.Collections.Generic;
using System;

using System.Globalization;
using System.Text.Json;

namespace RogueUnicorn.StoreTransfer;

internal static class Localization
{
    private static Dictionary<string,string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private static string _language = "en";

    public static string Language => _language;

    public static void Load()
    {
        var culture = CultureInfo.CurrentUICulture;
        var candidates = new[] { culture.Name, culture.TwoLetterISOLanguageName, "en" };

        foreach (var candidate in candidates)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "RetroRewindModHub_Data", "Localization", candidate + ".json");
            if (!File.Exists(path))
                continue;

            try
            {
                var json = File.ReadAllText(path);
                _strings = JsonSerializer.Deserialize<Dictionary<string,string>>(json)
                           ?? new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                _language = candidate;
                return;
            }
            catch { }
        }

        _strings = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        _language = "en";
    }

    public static string Get(string text)
        => string.IsNullOrWhiteSpace(text) ? text :
           _strings.TryGetValue(text, out var value) ? value : text;

    public static string Get(string text, params object[] args)
    {
        var value = Get(text);
        return args.Length == 0 ? value : string.Format(value, args);
    }
}
