using PKHeX.Application.Shortcuts;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// One shortcut the host binds on the main window. The host owns only the key binding; whether the
/// action can run and what it does stay here, evaluated when the key is pressed.
/// </summary>
/// <param name="ActionId">Stable action ID.</param>
/// <param name="Chord">Concrete chord to bind.</param>
/// <param name="TextRole">
/// Text-editing meaning of the chord. The host lets a focused text box keep chords with a role
/// (Undo and Redo) instead of running the action.
/// </param>
/// <param name="CanInvoke">
/// Live availability: false while the shortcut editor is open, or when the command or its save-type
/// predicate rejects the current state. A binding whose action cannot run must leave the key to the
/// focused control.
/// </param>
/// <param name="Invoke">Runs the action (closing the tool launcher first when it is open).</param>
public sealed record ShortcutBindingEntry(
    string ActionId,
    ShortcutChord Chord,
    ShortcutTextRole TextRole,
    Func<bool> CanInvoke,
    Action Invoke);
