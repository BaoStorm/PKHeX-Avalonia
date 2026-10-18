namespace PKHeX.Application.Shortcuts;

/// <summary>
/// Framework-free keyboard modifier set for a <see cref="ShortcutChord"/>. <see cref="Alt"/>,
/// <see cref="Control"/>, <see cref="Shift"/> and <see cref="Meta"/> use the same bit values as
/// Avalonia's <c>KeyModifiers</c>, so the host can map them without a lookup table.
/// </summary>
[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    /// <summary>Windows/Super key on Windows and Linux; Command (⌘) on macOS.</summary>
    Meta = 8,

    /// <summary>
    /// Portable placeholder for the platform's command modifier: Control on Windows/Linux and
    /// Command (⌘) on macOS. It appears only in stored settings and catalog defaults; a chord is
    /// resolved to a concrete modifier with <see cref="ShortcutChord.Resolve"/> before it is bound.
    /// </summary>
    Primary = 16,
}
