using System.Text;

namespace PKHeX.Application.Shortcuts;

/// <summary>
/// A keyboard shortcut: a modifier set plus one key from <see cref="ShortcutKeyVocabulary"/>.
/// A chord is either <em>portable</em> (it may contain <see cref="ShortcutModifiers.Primary"/>; this
/// is the stored and catalog form) or <em>concrete</em> (Primary resolved to Control or Meta; this
/// is the form that is validated, displayed and bound).
/// </summary>
/// <remarks>
/// Always pass a missing shortcut as <c>ShortcutChord?</c> (<see langword="null"/>), never as
/// <c>default(ShortcutChord)</c>, whose <see cref="Key"/> is <see langword="null"/>.
/// </remarks>
public readonly record struct ShortcutChord(ShortcutModifiers Modifiers, string Key)
{
    private const char Separator = '+';

    /// <summary>True when <see cref="Key"/> is one of <c>F1</c>–<c>F12</c>.</summary>
    public bool IsFunctionKey => ShortcutKeyVocabulary.IsFunctionKey(Key);

    /// <summary>
    /// Parses text such as <c>"Primary+Shift+N"</c>, <c>"ctrl + n"</c> or <c>"Cmd+1"</c>. Tokens are
    /// separated by <c>+</c>, trimmed and case-insensitive. Exactly one key token is required; an
    /// empty token, a second key, or an unknown token rejects the whole text.
    /// </summary>
    public static bool TryParse(string? text, out ShortcutChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = ShortcutModifiers.None;
        string? key = null;
        foreach (var rawToken in text.Split(Separator))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
                return false;

            if (TryParseModifier(token, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (key is not null || !ShortcutKeyVocabulary.TryNormalize(token, out var canonical))
                return false;
            key = canonical;
        }

        if (key is null)
            return false;

        chord = new ShortcutChord(modifiers, key);
        return true;
    }

    /// <summary>
    /// Formats the chord in the fixed storage order <c>Primary, Ctrl, Shift, Alt, Meta</c> followed
    /// by the key, joined with <c>+</c> (for example <c>"Primary+Shift+N"</c>).
    /// </summary>
    public string ToStorageString()
    {
        var builder = new StringBuilder();
        var modifiers = Modifiers;
        Append(builder, modifiers, ShortcutModifiers.Primary, "Primary");
        Append(builder, modifiers, ShortcutModifiers.Control, "Ctrl");
        Append(builder, modifiers, ShortcutModifiers.Shift, "Shift");
        Append(builder, modifiers, ShortcutModifiers.Alt, "Alt");
        Append(builder, modifiers, ShortcutModifiers.Meta, "Meta");
        builder.Append(Key ?? string.Empty);
        return builder.ToString();

        static void Append(StringBuilder sb, ShortcutModifiers set, ShortcutModifiers flag, string name)
        {
            if ((set & flag) != 0)
                sb.Append(name).Append(Separator);
        }
    }

    /// <summary>
    /// Returns the concrete chord for <paramref name="flavor"/>: <see cref="ShortcutModifiers.Primary"/>
    /// becomes Control on <see cref="ShortcutPlatformFlavor.Standard"/> and Meta on
    /// <see cref="ShortcutPlatformFlavor.Apple"/>. A chord without Primary is returned unchanged.
    /// </summary>
    public ShortcutChord Resolve(ShortcutPlatformFlavor flavor)
    {
        if ((Modifiers & ShortcutModifiers.Primary) == 0)
            return this;

        var concrete = (Modifiers & ~ShortcutModifiers.Primary) | CommandModifier(flavor);
        return this with { Modifiers = concrete };
    }

    /// <summary>
    /// Returns the portable form for <paramref name="flavor"/>: the platform's command modifier
    /// (Control on Standard, Meta on Apple) becomes <see cref="ShortcutModifiers.Primary"/>. Other
    /// modifiers stay literal, so a deliberate Ctrl chord recorded on macOS remains Ctrl. A chord
    /// that already contains Primary is returned unchanged.
    /// </summary>
    /// <remarks>
    /// A chord that holds both Control and Meta stays fully literal. As <c>Primary+Ctrl</c>, the
    /// macOS chord Ctrl+Cmd+K would resolve to plain Ctrl+K on Windows/Linux, where Primary is
    /// Ctrl, and take another action's shortcut there. Literal <c>Ctrl+Meta+K</c> keeps its meaning
    /// on macOS and is rejected on Windows/Linux, where the Windows key is reserved.
    /// </remarks>
    public ShortcutChord ToPortable(ShortcutPlatformFlavor flavor)
    {
        if ((Modifiers & ShortcutModifiers.Primary) != 0)
            return this;

        const ShortcutModifiers bothCommands = ShortcutModifiers.Control | ShortcutModifiers.Meta;
        if ((Modifiers & bothCommands) == bothCommands)
            return this;

        var command = CommandModifier(flavor);
        if ((Modifiers & command) == 0)
            return this;

        return this with { Modifiers = (Modifiers & ~command) | ShortcutModifiers.Primary };
    }

    /// <summary>
    /// True when this portable chord holds <see cref="ShortcutModifiers.Primary"/> together with the
    /// literal modifier that Primary stands for on <paramref name="flavor"/> (for example
    /// <c>Primary+Ctrl+K</c> on Windows/Linux). <see cref="Resolve"/> would merge the two into one
    /// modifier and produce a different, shorter chord, so such a chord must not be used there.
    /// </summary>
    public bool IsAmbiguousOn(ShortcutPlatformFlavor flavor) =>
        (Modifiers & ShortcutModifiers.Primary) != 0 && (Modifiers & CommandModifier(flavor)) != 0;

    /// <summary>The concrete modifier that <see cref="ShortcutModifiers.Primary"/> stands for.</summary>
    public static ShortcutModifiers CommandModifier(ShortcutPlatformFlavor flavor) =>
        flavor == ShortcutPlatformFlavor.Apple ? ShortcutModifiers.Meta : ShortcutModifiers.Control;

    /// <inheritdoc cref="ToStorageString"/>
    public override string ToString() => ToStorageString();

    private static bool TryParseModifier(string token, out ShortcutModifiers modifier)
    {
        modifier = token.ToUpperInvariant() switch
        {
            "PRIMARY" => ShortcutModifiers.Primary,
            "CTRL" or "CONTROL" => ShortcutModifiers.Control,
            "SHIFT" => ShortcutModifiers.Shift,
            "ALT" or "OPTION" or "OPT" => ShortcutModifiers.Alt,
            "META" or "CMD" or "COMMAND" or "WIN" or "SUPER" => ShortcutModifiers.Meta,
            _ => ShortcutModifiers.None,
        };
        return modifier != ShortcutModifiers.None;
    }
}
