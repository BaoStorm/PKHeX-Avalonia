using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using PKHeX.Presentation.Localization;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// Wording rules of the shortcut editor's strings in every UI language (issue #224): placeholders
/// match English, accessible names contain their visible labels, and the unsupported-key message
/// names the keys that work.
/// </summary>
public sealed partial class ShortcutLocalizationTests
{
    public static IEnumerable<object[]> Languages => LocalizedStrings.SupportedLanguages.Select(language => new object[] { language });

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [Theory]
    [MemberData(nameof(Languages))]
    public void ShortcutStrings_KeepTheEnglishPlaceholders(string language)
    {
        var english = Strings("en");
        var translated = Strings(language);

        foreach (var (key, value) in english.Where(entry => entry.Key.StartsWith("Shortcuts_", StringComparison.Ordinal)
                                                            || entry.Key.StartsWith("Settings_KeyboardShortcuts", StringComparison.Ordinal)))
        {
            Assert.True(translated.ContainsKey(key), $"{language} is missing {key}.");
            Assert.Equal(Placeholders(value), Placeholders(translated[key]));
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void AccessibleNames_ContainTheirVisibleLabels(string language)
    {
        var strings = Strings(language);

        // Settings: the "Customize..." button is named "Customize keyboard shortcuts" (WCAG 2.5.3).
        var open = strings["Settings_KeyboardShortcuts_Open"].TrimEnd('.', '…').Trim();
        Assert.Contains(open, strings["Settings_KeyboardShortcuts_OpenName"], StringComparison.CurrentCultureIgnoreCase);
        Assert.NotEqual(strings["Settings_KeyboardShortcuts"], strings["Settings_KeyboardShortcuts_OpenName"]);

        // Editor: the Reassign button adds the row it moves the shortcut to.
        Assert.Contains(strings["Shortcuts_Reassign"], strings["Shortcuts_ReassignFor"], StringComparison.CurrentCultureIgnoreCase);
    }

    [Theory]
    [InlineData("en", "top row")]
    [InlineData("pt-BR", "linha superior")]
    [InlineData("de", "oberen Reihe")]
    [InlineData("es", "fila superior")]
    [InlineData("fr", "rangée supérieure")]
    [InlineData("it", "riga superiore")]
    [InlineData("ja", "上部の数字キー")]
    [InlineData("ko", "위쪽 줄의 숫자 키")]
    [InlineData("zh-Hans", "上方一排的数字键")]
    [InlineData("zh-Hant", "上方一排的數字鍵")]
    public void UnsupportedKeyMessage_NamesTheTopRowDigitKeys(string language, string topRow)
    {
        // Numpad digits are not supported, so "use a digit" alone contradicts a user who pressed one.
        Assert.Contains(topRow, Strings(language)["Shortcuts_Error_UnsupportedKey"], StringComparison.Ordinal);
    }

    private static string[] Placeholders(string value) =>
        Placeholder().Matches(value).Select(match => match.Value).Distinct().Order(StringComparer.Ordinal).ToArray();

    // Read from the catalog files: the runtime lookup falls back to English and would hide a missing key.
    private static IReadOnlyDictionary<string, string> Strings(string language)
    {
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "PKHeX.Presentation")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("repo root");

        var path = Path.Combine(dir, "PKHeX.Presentation", "Localization", "Strings", $"{language}.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }
}
