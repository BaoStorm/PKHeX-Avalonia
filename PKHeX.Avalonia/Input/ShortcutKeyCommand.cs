using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PKHeX.Application.Shortcuts;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Input;

/// <summary>
/// The command behind one configurable <see cref="global::Avalonia.Input.KeyBinding"/>. Availability is
/// evaluated when the key is pressed. A <see cref="global::Avalonia.Input.KeyBinding"/> handles the key
/// only when <see cref="CanExecute"/> is true, so an unavailable shortcut leaves the key to the
/// focused control.
/// </summary>
/// <remarks>
/// Actions with a text role (application Undo/Redo on Ctrl+Z/Ctrl+Y) yield to a focused text input,
/// which keeps its own text undo/redo. This covers every control that edits through a
/// <see cref="TextBox"/>: AutoCompleteBox, NumericUpDown, filterable combo boxes and grid edit cells.
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
        Entry.CanInvoke() && !(Entry.TextRole != ShortcutTextRole.None && IsTextInputFocused(scope));

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            Entry.Invoke();
    }

    /// <summary>True when keyboard focus is in a text box (or a control built on one) of <paramref name="scope"/>.</summary>
    internal static bool IsTextInputFocused(TopLevel? scope) =>
        scope?.FocusManager?.GetFocusedElement() is Visual focused
        && (focused is TextBox || focused.FindAncestorOfType<TextBox>() is not null);
}
