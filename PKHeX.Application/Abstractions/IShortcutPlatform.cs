using PKHeX.Application.Shortcuts;

namespace PKHeX.Application.Abstractions;

/// <summary>
/// Platform keyboard conventions the framework-free shortcut model needs: which modifier is the
/// command modifier, which chords are already taken, and how a chord is shown to the user. The
/// Avalonia host implements this from its hotkey configuration; <see cref="StandardShortcutPlatform"/>
/// is the fallback when no host is present.
/// </summary>
public interface IShortcutPlatform
{
    /// <summary>Platform convention that decides what <see cref="ShortcutModifiers.Primary"/> means.</summary>
    ShortcutPlatformFlavor Flavor { get; }

    /// <summary>Concrete chords an application shortcut must not use.</summary>
    IReadOnlyList<ShortcutReservation> Reservations { get; }

    /// <summary>Formats a concrete chord for display, for example <c>"Ctrl+Shift+N"</c> or <c>"⇧⌘N"</c>.</summary>
    string FormatForDisplay(ShortcutChord concrete);

    /// <summary>Display text of the command modifier alone, for example <c>"Ctrl"</c> or <c>"⌘"</c>.</summary>
    string PrimaryModifierDisplay { get; }
}
