using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Input;

/// <summary>
/// The command behind one configurable main-window shortcut. Availability is evaluated when the key
/// is pressed. The shortcut handles the key only when <see cref="CanExecute"/> is true, so an
/// unavailable shortcut leaves the key to the focused control.
/// </summary>
/// <remarks>
/// When the chord is the platform's own text undo/redo gesture (application Undo on Ctrl+Z, Redo on
/// Ctrl+Y), it yields to a focused, editable text input, which keeps its text undo/redo. This covers
/// every control that edits through a <see cref="TextBox"/>: AutoCompleteBox, NumericUpDown,
/// filterable combo boxes and grid edit cells. Any other chord, including Undo moved to Ctrl+U or
/// F9, runs from a text box too: the text box has no meaning for it, so yielding would leave the
/// key doing nothing.
/// </remarks>
internal sealed class ShortcutKeyCommand(ShortcutBindingEntry entry, TopLevel scope) : ICommand
{
    public ShortcutBindingEntry Entry { get; } = entry ?? throw new ArgumentNullException(nameof(entry));

    // Availability is pulled when a key is pressed; there is nothing to push.
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) =>
        Entry.CanInvoke() && !(Entry.IsTextEditingGesture && IsEditableTextInputFocused(scope));

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            Entry.Invoke();
    }

    /// <summary>
    /// True when keyboard focus is in an editable text box (or a control built on one) of
    /// <paramref name="scope"/>. A read-only text box, or one without text undo, has no undo/redo
    /// of its own, so the application shortcut runs there.
    /// </summary>
    internal static bool IsEditableTextInputFocused(TopLevel? scope)
    {
        if (scope?.FocusManager?.GetFocusedElement() is not Visual focused)
            return false;

        var textBox = focused as TextBox ?? focused.FindAncestorOfType<TextBox>();
        return textBox is { IsReadOnly: false, IsUndoEnabled: true };
    }
}
