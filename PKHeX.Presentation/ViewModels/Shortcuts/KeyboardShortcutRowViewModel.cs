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
    private LostShortcut? _lost;

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

    /// <summary>Every localized name of the action (Box Layout is also Block Layout); search matches each.</summary>
    public IReadOnlyList<string> Titles => _action.Titles;

    /// <summary>
    /// True when another action in the editor has the same <see cref="Title"/> (five tools are
    /// called "Misc", File and Window both have "Save"). Set once, when the editor opens.
    /// </summary>
    internal bool IsTitleShared { get; set; }

    /// <summary>
    /// <see cref="Title"/>, followed by the menus when another action has the same title:
    /// "Misc (Gen 3)". Messages and accessible names use it, so they always name one action.
    /// </summary>
    public string QualifiedTitle => IsTitleShared
        ? LocalizedStrings.Instance.Format("Shortcuts_TitleWithMenu", Title, CategoryTitle)
        : Title;

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
            OnPropertyChanged(nameof(RecorderAutomationName));
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

    /// <summary>
    /// Accessible name of the recorder with its current value: "Shortcut for Encounter Database:
    /// Ctrl+N", "...: None", or "...: Press keys..." while recording. The explicit name replaces the
    /// button text for assistive technology, so the value must be part of it.
    /// </summary>
    public string RecorderAutomationName => LocalizedStrings.Instance.Format(
        "Shortcuts_RecorderName",
        QualifiedTitle,
        IsRecording ? LocalizedStrings.Instance["Shortcuts_Recording"] : ShortcutText);

    /// <summary>Accessible name of the remove button.</summary>
    public string ClearAutomationName => LocalizedStrings.Instance.Format("Shortcuts_ClearFor", QualifiedTitle);

    /// <summary>Accessible name of the restore-default button.</summary>
    public string ResetAutomationName => LocalizedStrings.Instance.Format("Shortcuts_ResetFor", QualifiedTitle);

    /// <summary>Accessible name of the Reassign button: its visible text and the action it moves the shortcut to.</summary>
    public string ReassignAutomationName => LocalizedStrings.Instance.Format("Shortcuts_ReassignFor", QualifiedTitle);

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

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(RecorderAutomationName));
        _owner.OnRowRecordingChanged();
    }

    /// <summary>
    /// The shortcut this row lost to another row (a suppressed default, a duplicate, or a moved
    /// shortcut) while <see cref="Message"/> still explains it; <see langword="null"/> after any
    /// other feedback. The editor rewrites the message when the other row changes.
    /// </summary>
    internal LostShortcut? Lost => _lost;

    /// <summary>Shows <paramref name="message"/> and drops any pending conflict and lost shortcut.</summary>
    internal void SetMessage(string message)
    {
        SetPendingConflict(null);
        _lost = null;
        Message = message;
    }

    /// <summary>Shows a conflict that <see cref="ReassignCommand"/> can resolve.</summary>
    internal void SetConflict(string otherActionId, ShortcutChord chord, string message)
    {
        SetPendingConflict((otherActionId, chord));
        _lost = null;
        Message = message;
    }

    /// <summary>Shows <paramref name="message"/> about a shortcut this row lost to another row.</summary>
    internal void SetLost(LostShortcut lost, string message)
    {
        SetPendingConflict(null);
        _lost = lost;
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

/// <summary>Why a row has no shortcut: the chord another row took from it.</summary>
internal enum LostShortcutKind
{
    /// <summary>The default is inactive because another action's stored shortcut uses it.</summary>
    SuppressedDefault,

    /// <summary>The stored shortcut is inactive because another action stored the same chord.</summary>
    DuplicateStored,

    /// <summary>The user moved the shortcut to another row with Reassign.</summary>
    Moved,
}

/// <summary>A shortcut a row lost to <paramref name="OtherActionId"/>.</summary>
internal sealed record LostShortcut(LostShortcutKind Kind, ShortcutChord Chord, string OtherActionId);
