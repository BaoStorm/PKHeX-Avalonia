using PKHeX.Application.Shortcuts;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// One shortcut the host binds on the main window. The host owns only the key binding; whether the
/// action can run and what it does stay here, evaluated when the key is pressed.
/// </summary>
/// <param name="ActionId">Stable action ID.</param>
/// <param name="Chord">Concrete chord to bind.</param>
/// <param name="TextRole">Text-editing meaning of the action (Undo or Redo), or none.</param>
/// <param name="IsTextEditingGesture">
/// True when <paramref name="Chord"/> is the platform's own text gesture for
/// <paramref name="TextRole"/> (Ctrl+Z for Undo, Ctrl+Y or Ctrl+Shift+Z for Redo). Only then does the
/// host let a focused, editable text box keep the key for its own text undo or redo. Any other chord
/// (for example Undo moved to Ctrl+U) runs the action from a text box too, because the text box has
/// nothing to do with it.
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
    bool IsTextEditingGesture,
    Func<bool> CanInvoke,
    Action Invoke);
