namespace PKHeX.Application.Shortcuts;

/// <summary>Why a chord is not available for an application shortcut.</summary>
public enum ShortcutReservationKind
{
    /// <summary>Text gestures of text boxes (copy, paste, select all, text undo/redo).</summary>
    TextEditing,

    /// <summary>Gestures owned by the box/party storage views (seek, slot copy/paste).</summary>
    Storage,

    /// <summary>Gestures owned by standard controls (grid cell edit, drop-down open).</summary>
    Controls,

    /// <summary>Gestures the operating system or window manager owns.</summary>
    System,
}

/// <summary>
/// A concrete chord the application must not take over. When <see cref="AllowedRole"/> is not
/// <see cref="ShortcutTextRole.None"/>, an action with the same <see cref="ShortcutTextRole"/> may
/// still use the chord (for example application Undo on Ctrl+Z).
/// </summary>
public sealed record ShortcutReservation(
    ShortcutChord Chord,
    ShortcutReservationKind Kind,
    ShortcutTextRole AllowedRole = ShortcutTextRole.None);
