using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.Shortcuts;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// One action in the shortcut editor: its title, the draft shortcut (not yet saved), the default,
/// and the feedback for the last edit. Every edit goes through the owning
/// <see cref="KeyboardShortcutsViewModel"/>, which validates it against the whole draft.
/// </summary>
public sealed partial class KeyboardShortcutRowViewModel : ObservableObject
{
    private readonly KeyboardShortcutsViewModel _owner;
    private readonly ShortcutAction _action;
    private ShortcutChord? _chord;
    private string _message = string.Empty;
    private (string OtherActionId, ShortcutChord Chord)? _pendingConflict;

    internal KeyboardShortcutRowViewModel(
        KeyboardShortcutsViewModel owner,
        ShortcutAction action,
        ShortcutChord? defaultChord,
        ShortcutChord? chord)
    {
        _owner = owner;
        _action = action;
        Default = defaultChord;
        _chord = chord;
    }

    /// <summary>Stable action ID.</summary>
    public string ActionId => _action.Id;

    /// <summary>Localized action title without the access-key marker.</summary>
    public string Title => _action.Title;

    /// <summary>Localized names of the menus the action appears in.</summary>
    public string CategoryTitle => _action.CategoryTitle;

    /// <summary>The concrete platform default, or <see langword="null"/> when the action has none.</summary>
    public ShortcutChord? Default { get; }

    /// <summary>"Default: Ctrl+N", or "Default: None".</summary>
    public string DefaultText => LocalizedStrings.Instance.Format(
        "Shortcuts_Default",
        Default is { } fallback ? _owner.Display(fallback) : LocalizedStrings.Instance["Shortcuts_None"]);

    /// <summary>The draft shortcut. The draft never holds a duplicate or an unusable chord.</summary>
    public ShortcutChord? Chord
    {
        get => _chord;
        internal set
        {
            if (_chord == value)
                return;

            _chord = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShortcutText));
            OnPropertyChanged(nameof(HasShortcut));
            OnPropertyChanged(nameof(IsCustomized));
            ClearCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Display text of the draft shortcut, or "None".</summary>
    public string ShortcutText => _chord is { } chord ? _owner.Display(chord) : LocalizedStrings.Instance["Shortcuts_None"];

    public bool HasShortcut => _chord is not null;

    /// <summary>True when the draft differs from the platform default.</summary>
    public bool IsCustomized => _chord != Default;

    /// <summary>Feedback for the last edit, or a problem found when the editor opened.</summary>
    public string Message
    {
        get => _message;
        private set
        {
            if (string.Equals(_message, value, StringComparison.Ordinal))
                return;

            _message = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => _message.Length > 0;

    /// <summary>The chord the user tried to assign while another action holds it, until it is reassigned or replaced.</summary>
    internal (string OtherActionId, ShortcutChord Chord)? PendingConflict => _pendingConflict;

    public bool HasPendingConflict => _pendingConflict is not null;

    /// <summary>True while the recorder of this row waits for keys.</summary>
    [ObservableProperty]
    private bool _isRecording;

    /// <summary>Accessible name of the recorder: "Shortcut for Encounter Database".</summary>
    public string RecorderAutomationName => LocalizedStrings.Instance.Format("Shortcuts_RecorderName", Title);

    /// <summary>Accessible name of the remove button.</summary>
    public string ClearAutomationName => LocalizedStrings.Instance.Format("Shortcuts_ClearFor", Title);

    /// <summary>Accessible name of the restore-default button.</summary>
    public string ResetAutomationName => LocalizedStrings.Instance.Format("Shortcuts_ResetFor", Title);

    internal ShortcutDefinition Definition => _action.Definition;

    /// <summary>Receives the chord the recorder captured; <see langword="null"/> is ignored.</summary>
    [RelayCommand]
    private void Record(ShortcutChord? chord) => _owner.Record(this, chord);

    [RelayCommand(CanExecute = nameof(HasShortcut))]
    private void Clear() => _owner.Clear(this);

    [RelayCommand(CanExecute = nameof(IsCustomized))]
    private void Reset() => _owner.ResetRow(this);

    /// <summary>Moves the pending chord here and removes it from the action that held it.</summary>
    [RelayCommand(CanExecute = nameof(HasPendingConflict))]
    private void Reassign() => _owner.Reassign(this);

    partial void OnIsRecordingChanged(bool value) => _owner.OnRowRecordingChanged();

    /// <summary>Shows <paramref name="message"/> and drops any pending conflict.</summary>
    internal void SetMessage(string message)
    {
        SetPendingConflict(null);
        Message = message;
    }

    /// <summary>Shows a conflict that <see cref="ReassignCommand"/> can resolve.</summary>
    internal void SetConflict(string otherActionId, ShortcutChord chord, string message)
    {
        SetPendingConflict((otherActionId, chord));
        Message = message;
    }

    /// <summary>Removes the message and any pending conflict.</summary>
    internal void ClearFeedback() => SetMessage(string.Empty);

    private void SetPendingConflict((string OtherActionId, ShortcutChord Chord)? conflict)
    {
        if (_pendingConflict == conflict)
            return;

        _pendingConflict = conflict;
        OnPropertyChanged(nameof(HasPendingConflict));
        ReassignCommand.NotifyCanExecuteChanged();
    }
}
