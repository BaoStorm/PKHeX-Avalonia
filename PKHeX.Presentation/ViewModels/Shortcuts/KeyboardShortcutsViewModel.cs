using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Application.Shortcuts;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// The keyboard shortcut editor (issue #224). Edits a draft copy of the resolved shortcuts; nothing
/// is stored until <see cref="SaveCommand"/>, which hands the changed actions to
/// <see cref="KeyboardShortcutService.Apply"/>. Cancel and closing the window discard the draft.
/// </summary>
/// <remarks>
/// Every recorded chord is validated against the platform rules and the whole draft before it is
/// accepted, so the draft never holds an unusable chord or the same chord twice and Save is always
/// available. A chord another action holds is not moved until the user confirms with Reassign.
/// While the editor is open, <see cref="KeyboardShortcutService.IsEditorOpen"/> is true so no
/// main-window shortcut runs on the keys the user is recording.
/// </remarks>
public sealed partial class KeyboardShortcutsViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly ShortcutActionCatalog _catalog;
    private readonly KeyboardShortcutService _service;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<string, ShortcutChord?> _draft = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KeyboardShortcutRowViewModel> _rowsById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);

    // Actions whose shown state depends on another action's stored override (a suppressed default
    // or a duplicate). Save writes their draft too, so what is stored matches what the editor showed.
    private readonly HashSet<string> _writeOnSave = new(StringComparer.Ordinal);

    private IReadOnlyList<KeyboardShortcutRowViewModel> _filteredRows;
    private bool _sessionEnded;

    public KeyboardShortcutsViewModel(ShortcutActionCatalog catalog, KeyboardShortcutService service, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(dialogs);

        _catalog = catalog;
        _service = service;
        _dialogs = dialogs;
        _service.IsEditorOpen = true;

        var table = service.Resolve(catalog.Definitions);
        var rows = new List<KeyboardShortcutRowViewModel>(catalog.All.Count);
        foreach (var action in catalog.All)
        {
            var resolved = table[action.Id];
            _draft[action.Id] = resolved.Effective;
            var row = new KeyboardShortcutRowViewModel(this, action, resolved.Default, resolved.Effective);
            rows.Add(row);
            _rowsById[action.Id] = row;
        }

        Rows = rows;
        _filteredRows = rows;

        // Messages and accessible names add the menus to a title that several actions share.
        foreach (var group in rows.GroupBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!group.Skip(1).Any())
                continue;
            foreach (var row in group)
                row.IsTitleShared = true;
        }

        // Issues name other rows, so describe them once every row exists. A shortcut lost to another
        // row is described from the draft, and described again whenever that row changes.
        foreach (var row in rows)
        {
            if (table[row.ActionId].Issue is not { } issue)
                continue;

            if (issue.Kind is ShortcutIssueKind.SuppressedDefault or ShortcutIssueKind.DuplicateStored)
                _writeOnSave.Add(row.ActionId);

            if (LostFor(row, issue) is { } lost)
                row.SetLost(lost, string.Empty);
            else
                row.SetMessage(DescribeIssue(row, issue));
        }
        RefreshLostMessages(except: null);
    }

    public Action? CloseRequested { get; set; }

    /// <summary>Every action: File, Edit, Window and Help, then the tools in Tools menu order.</summary>
    public IReadOnlyList<KeyboardShortcutRowViewModel> Rows { get; }

    /// <summary>The rows that match <see cref="SearchText"/>.</summary>
    public IReadOnlyList<KeyboardShortcutRowViewModel> FilteredRows
    {
        get => _filteredRows;
        private set => SetProperty(ref _filteredRows, value);
    }

    /// <summary>Filters the rows by title, menu name or shortcut text.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>True while any row's recorder waits for keys.</summary>
    public bool IsRecording => Rows.Any(row => row.IsRecording);

    /// <summary>"3 customized": the number of rows whose draft differs from the default.</summary>
    public string CustomizedSummary =>
        LocalizedStrings.Instance.Format("Shortcuts_CustomizedCount", Rows.Count(row => row.IsCustomized));

    /// <summary>True once the user changed any shortcut in this session.</summary>
    public bool HasChanges => _changed.Count > 0;

    /// <summary>Platform text of a concrete chord; never throws for a key outside the vocabulary.</summary>
    internal string Display(ShortcutChord chord)
    {
        try
        {
            return _service.Platform.FormatForDisplay(chord);
        }
        catch (ArgumentException)
        {
            return chord.ToStorageString();
        }
    }

    [RelayCommand]
    private void Save()
    {
        var ids = _changed.Concat(_writeOnSave).Distinct(StringComparer.Ordinal);
        var changes = ids.ToDictionary(id => id, id => _draft[id], StringComparer.Ordinal);
        _service.Apply(_catalog.Definitions, changes);
        EndSession();
        CloseRequested?.Invoke();
    }

    private bool CanCancel => !IsRecording;

    /// <summary>Discards the draft. Unavailable while recording, so Escape reaches the recorder.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        EndSession();
        CloseRequested?.Invoke();
    }

    /// <summary>After confirmation, sets every action's draft back to its platform default.</summary>
    [RelayCommand]
    private async Task ResetAllAsync()
    {
        if (!await _dialogs.ShowConfirmationAsync(T("Shortcuts_ResetAll"), T("Shortcuts_ResetAll_Confirm"),
                T("Shortcuts_ResetAll"), T("Common_Cancel")))
        {
            return;
        }

        if (_sessionEnded)
            return;

        foreach (var row in Rows)
        {
            _draft[row.ActionId] = row.Default;
            row.Chord = row.Default;
            row.ClearFeedback();
            _changed.Add(row.ActionId);
        }
        OnDraftChanged();
    }

    /// <summary>Validates <paramref name="chord"/> for <paramref name="row"/> and applies it or explains why not.</summary>
    internal void Record(KeyboardShortcutRowViewModel row, ShortcutChord? chord)
    {
        if (chord is not { } concrete)
            return;

        var result = ShortcutPolicy.Validate(row.Definition, concrete, _service.Platform, _draft);
        switch (result.Kind)
        {
            case ShortcutValidationKind.Ok:
                Assign(row, concrete);
                // The message is a polite live region, so a screen reader hears the new shortcut.
                row.SetMessage(LocalizedStrings.Instance.Format("Shortcuts_Assigned", Display(concrete)));
                break;
            case ShortcutValidationKind.Unchanged:
                row.ClearFeedback();
                break;
            case ShortcutValidationKind.Conflict:
                var other = _rowsById[result.ConflictingActionId!];
                row.SetConflict(other.ActionId, concrete,
                    LocalizedStrings.Instance.Format("Shortcuts_Conflict", Display(concrete), other.QualifiedTitle));
                break;
            case ShortcutValidationKind.NeedsModifier:
                row.SetMessage(LocalizedStrings.Instance.Format("Shortcuts_Error_NeedsModifier", _service.Platform.PrimaryModifierDisplay));
                break;
            case ShortcutValidationKind.AltGr:
                row.SetMessage(T("Shortcuts_Error_AltGr"));
                break;
            case ShortcutValidationKind.Reserved:
                row.SetMessage(LocalizedStrings.Instance.Format("Shortcuts_Error_Reserved", Display(concrete), ReservedReason(result.ReservedFor)));
                break;
            default:
                row.SetMessage(T("Shortcuts_Error_UnsupportedKey"));
                break;
        }
    }

    /// <summary>Removes the row's shortcut from the draft.</summary>
    internal void Clear(KeyboardShortcutRowViewModel row)
    {
        Assign(row, null);
        row.SetMessage(T("Shortcuts_Removed"));
    }

    /// <summary>Restores the row's default through the same validation, so a conflict still asks first.</summary>
    internal void ResetRow(KeyboardShortcutRowViewModel row)
    {
        if (row.Default is { } fallback)
            Record(row, fallback);
        else
            Clear(row);
    }

    /// <summary>Takes the pending chord from the action that holds it and assigns it to <paramref name="row"/>.</summary>
    internal void Reassign(KeyboardShortcutRowViewModel row)
    {
        if (row.PendingConflict is not { } pending)
            return;

        if (_rowsById.TryGetValue(pending.OtherActionId, out var other) && _draft[other.ActionId] == pending.Chord)
        {
            Assign(other, null);
            var lost = new LostShortcut(LostShortcutKind.Moved, pending.Chord, row.ActionId);
            other.SetLost(lost, DescribeLost(other, lost));
        }

        // Validate again: the draft may have changed since the conflict was shown.
        Record(row, pending.Chord);
    }

    internal void OnRowRecordingChanged()
    {
        OnPropertyChanged(nameof(IsRecording));
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        FilteredRows = query.Length == 0
            ? Rows
            : Rows.Where(row => Matches(row, query)).ToArray();
    }

    // Every menu name counts: the Gen 4 menu calls Box Layout "Block Layout".
    private static bool Matches(KeyboardShortcutRowViewModel row, string query) =>
        row.Titles.Any(title => title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        || row.CategoryTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || (row.HasShortcut && row.ShortcutText.Contains(query, StringComparison.CurrentCultureIgnoreCase));

    private void Assign(KeyboardShortcutRowViewModel row, ShortcutChord? chord)
    {
        _draft[row.ActionId] = chord;
        row.Chord = chord;
        _changed.Add(row.ActionId);
        RefreshLostMessages(except: row);
        OnDraftChanged();
    }

    /// <summary>
    /// Describes again every shortcut a row lost to another row, so no message names an action that
    /// no longer holds the chord. <paramref name="except"/> is the edited row, which gets its own feedback.
    /// </summary>
    private void RefreshLostMessages(KeyboardShortcutRowViewModel? except)
    {
        foreach (var row in Rows)
        {
            if (ReferenceEquals(row, except) || row.Chord is not null || row.Lost is not { } lost)
                continue;
            row.SetLost(lost, DescribeLost(row, lost));
        }
    }

    // The chord the row lost when the editor opened, or null when the issue names no other row.
    private LostShortcut? LostFor(KeyboardShortcutRowViewModel row, ShortcutIssue issue)
    {
        if (issue.OtherActionId is not { } otherId || !_rowsById.ContainsKey(otherId))
            return null;

        return issue.Kind switch
        {
            ShortcutIssueKind.SuppressedDefault when row.Default is { } fallback =>
                new LostShortcut(LostShortcutKind.SuppressedDefault, fallback, otherId),
            ShortcutIssueKind.DuplicateStored when (StoredChord(issue.Raw) ?? row.Default) is { } chord =>
                new LostShortcut(LostShortcutKind.DuplicateStored, chord, otherId),
            _ => null,
        };
    }

    /// <summary>Explains <paramref name="lost"/> from the current draft: who holds the chord now, or that it is free.</summary>
    private string DescribeLost(KeyboardShortcutRowViewModel row, LostShortcut lost)
    {
        var chord = Display(lost.Chord);
        var holder = Rows.FirstOrDefault(other => !ReferenceEquals(other, row) && _draft[other.ActionId] == lost.Chord);
        var other = _rowsById[lost.OtherActionId];

        switch (lost.Kind)
        {
            // A default is also blocked by stored duplicates that nobody has resolved yet.
            case LostShortcutKind.SuppressedDefault:
                var blocker = holder ?? (IsUnresolvedDuplicate(other, lost.Chord) ? other : null);
                return blocker is not null
                    ? LocalizedStrings.Instance.Format("Shortcuts_Issue_SuppressedDefault", chord, blocker.QualifiedTitle)
                    : LocalizedStrings.Instance.Format("Shortcuts_Issue_DefaultFree", chord);

            case LostShortcutKind.DuplicateStored when holder is null && IsUnresolvedDuplicate(row, lost.Chord):
                return LocalizedStrings.Instance.Format("Shortcuts_Issue_DuplicateStored", chord, other.QualifiedTitle);

            case LostShortcutKind.Moved when ReferenceEquals(holder, other):
                return LocalizedStrings.Instance.Format("Shortcuts_Moved", other.QualifiedTitle);
        }

        return holder is not null
            ? LocalizedStrings.Instance.Format("Shortcuts_Issue_UsedBy", chord, holder.QualifiedTitle)
            : LocalizedStrings.Instance.Format("Shortcuts_Issue_Free", chord);
    }

    // True while the row and the row it names both still claim the same stored chord, so neither has it.
    private bool IsUnresolvedDuplicate(KeyboardShortcutRowViewModel row, ShortcutChord chord) =>
        IsDuplicateClaim(row, chord)
        && IsDuplicateClaim(_rowsById[row.Lost!.OtherActionId], chord);

    private static bool IsDuplicateClaim(KeyboardShortcutRowViewModel row, ShortcutChord chord) =>
        row is { Chord: null, Lost: { Kind: LostShortcutKind.DuplicateStored } claim } && claim.Chord == chord;

    private void OnDraftChanged()
    {
        OnPropertyChanged(nameof(CustomizedSummary));
        OnPropertyChanged(nameof(HasChanges));
    }

    private string DescribeIssue(KeyboardShortcutRowViewModel row, ShortcutIssue issue)
    {
        var otherTitle = issue.OtherActionId is { } otherId && _rowsById.TryGetValue(otherId, out var other)
            ? other.QualifiedTitle
            : issue.OtherActionId ?? string.Empty;

        return issue.Kind switch
        {
            ShortcutIssueKind.SuppressedDefault => LocalizedStrings.Instance.Format("Shortcuts_Issue_SuppressedDefault",
                row.Default is { } fallback ? Display(fallback) : string.Empty, otherTitle),
            ShortcutIssueKind.DuplicateStored => LocalizedStrings.Instance.Format("Shortcuts_Issue_DuplicateStored",
                StoredText(issue.Raw) ?? (row.Default is { } shared ? Display(shared) : string.Empty), otherTitle),
            // The stored value is ignored: the row keeps its default, or has no shortcut when it has no default.
            _ => LocalizedStrings.Instance.Format(
                row.Default is null ? "Shortcuts_Issue_InvalidStored_NoDefault" : "Shortcuts_Issue_InvalidStored",
                (issue.Kind == ShortcutIssueKind.UnusableStored ? StoredText(issue.Raw) : issue.Raw) ?? string.Empty),
        };
    }

    // The concrete chord of stored text, when it parses into one chord on this platform.
    private ShortcutChord? StoredChord(string? raw)
    {
        var flavor = _service.Platform.Flavor;
        return ShortcutChord.TryParse(raw, out var parsed) && !parsed.IsAmbiguousOn(flavor) ? parsed.Resolve(flavor) : null;
    }

    // The stored text as the platform shows it when it parses into one chord, otherwise as stored.
    private string? StoredText(string? raw) => StoredChord(raw) is { } chord ? Display(chord) : raw;

    private static string ReservedReason(ShortcutReservationKind? kind) => kind switch
    {
        ShortcutReservationKind.TextEditing => T("Shortcuts_Reserved_TextEditing"),
        ShortcutReservationKind.Storage => T("Shortcuts_Reserved_Storage"),
        ShortcutReservationKind.Controls => T("Shortcuts_Reserved_Controls"),
        _ => T("Shortcuts_Reserved_System"),
    };

    private static string T(string key) => LocalizedStrings.Instance[key];

    /// <summary>Lets main-window shortcuts run again. Safe to call more than once.</summary>
    private void EndSession()
    {
        if (_sessionEnded)
            return;

        _sessionEnded = true;
        _service.IsEditorOpen = false;
    }

    public void Dispose() => EndSession();
}
