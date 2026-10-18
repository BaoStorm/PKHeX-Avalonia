namespace PKHeX.Application.Shortcuts;

/// <summary>
/// A shortcut-capable action as the resolver sees it: a stable <see cref="Id"/>, its portable default
/// chord per platform flavor (<see langword="null"/> means no default) and its text role.
/// </summary>
/// <param name="Id">Stable action ID, also the key in <c>AppSettings.KeyboardShortcuts</c>.</param>
/// <param name="StandardDefault">Portable default on Windows/Linux, for example <c>"Primary+N"</c>.</param>
/// <param name="AppleDefault">Portable default on macOS.</param>
/// <param name="TextRole">Text-editing meaning shared with the platform's text gestures.</param>
public sealed record ShortcutDefinition(
    string Id,
    string? StandardDefault,
    string? AppleDefault,
    ShortcutTextRole TextRole = ShortcutTextRole.None)
{
    /// <summary>Creates a definition with the same portable default on every platform.</summary>
    public static ShortcutDefinition Same(string id, string? portable, ShortcutTextRole role = ShortcutTextRole.None) =>
        new(id, portable, portable, role);

    /// <summary>The portable default text for <paramref name="flavor"/>, or <see langword="null"/>.</summary>
    public string? DefaultFor(ShortcutPlatformFlavor flavor) =>
        flavor == ShortcutPlatformFlavor.Apple ? AppleDefault : StandardDefault;

    /// <summary>
    /// The concrete default chord for <paramref name="flavor"/>, or <see langword="null"/> when the
    /// action has no default there (or the catalog text does not parse).
    /// </summary>
    public ShortcutChord? ResolveDefault(ShortcutPlatformFlavor flavor) =>
        ShortcutChord.TryParse(DefaultFor(flavor), out var chord) ? chord.Resolve(flavor) : null;
}
