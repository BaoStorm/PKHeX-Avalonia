namespace PKHeX.Application.Shortcuts;

/// <summary>
/// The closed set of keys a configurable shortcut may use: letters <c>A</c>–<c>Z</c>, top-row
/// digits <c>D0</c>–<c>D9</c> and function keys <c>F1</c>–<c>F12</c>. Canonical names match
/// Avalonia's <c>Key</c> enum member names, so the host maps them with <c>Enum.Parse</c>.
/// </summary>
/// <remarks>
/// Punctuation (Oem) and numpad keys are excluded on purpose: their meaning and position depend on
/// the keyboard layout, so a stored chord would not reliably mean the same key for every user.
/// Digits are stored as <c>D1</c> rather than <c>1</c> because Avalonia's <c>KeyGesture.Parse</c>
/// reads a bare <c>"1"</c> as the numeric enum value 1 (<c>Key.Cancel</c>), not the digit key.
/// </remarks>
public static class ShortcutKeyVocabulary
{
    private static readonly string[] OrderedNames = BuildNames();
    private static readonly HashSet<string> Canonical = new(OrderedNames, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ByUpperCase =
        OrderedNames.ToDictionary(static name => name, static name => name, StringComparer.OrdinalIgnoreCase);

    /// <summary>All canonical key names, in letter, digit, function-key order.</summary>
    public static IReadOnlyCollection<string> Names => OrderedNames;

    /// <summary>True when <paramref name="key"/> is a canonical vocabulary name (case-sensitive).</summary>
    public static bool IsKnown(string? key) => key is not null && Canonical.Contains(key);

    /// <summary>
    /// Maps a user-typed key token to its canonical name. Matching is case-insensitive, and a bare
    /// digit <c>"0"</c>–<c>"9"</c> maps to <c>"D0"</c>–<c>"D9"</c>.
    /// </summary>
    public static bool TryNormalize(string? token, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var trimmed = token.Trim();
        if (trimmed.Length == 1 && trimmed[0] is >= '0' and <= '9')
        {
            canonical = "D" + trimmed;
            return true;
        }

        if (!ByUpperCase.TryGetValue(trimmed, out var match))
            return false;

        canonical = match;
        return true;
    }

    /// <summary>True for the canonical function keys <c>F1</c>–<c>F12</c>.</summary>
    public static bool IsFunctionKey(string? key) =>
        key is { Length: >= 2 and <= 3 } && key[0] == 'F' && IsKnown(key);

    private static string[] BuildNames()
    {
        var names = new List<string>(26 + 10 + 12);
        for (var c = 'A'; c <= 'Z'; c++)
            names.Add(c.ToString());
        for (var d = 0; d <= 9; d++)
            names.Add("D" + d.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var f = 1; f <= 12; f++)
            names.Add("F" + f.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return [.. names];
    }
}
