using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>Visual/semantic category of a box manipulation.</summary>
public enum BoxManipCategory { Sort, Modify, Delete }

/// <summary>
/// Review-and-apply box manipulation. Selecting an action only stages it; Apply shows the scope and impact,
/// asks for confirmation before deleting, and records the whole operation as one undoable history entry.
/// </summary>
public partial class BoxManipViewModel : ViewModelBase, IDisposable
{
    private readonly SaveFile _sav;
    private readonly IDialogService _dialogService;
    private readonly Action _onComplete;
    private readonly UndoRedoService? _undoRedo;

    public BoxManipViewModel(SaveFile sav, IDialogService dialogService, Action onComplete,
        int currentBox = 0, UndoRedoService? undoRedo = null)
    {
        _sav = sav;
        _dialogService = dialogService;
        _onComplete = onComplete;
        _undoRedo = undoRedo;
        _currentBox = sav.BoxCount == 0 ? 0 : Math.Clamp(currentBox, 0, sav.BoxCount - 1);

        LoadManipulations();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => RefreshLanguage());
    }

    public ObservableCollection<BoxManipItemViewModel> SortActions { get; } = [];
    public ObservableCollection<BoxManipItemViewModel> ClearActions { get; } = [];
    public ObservableCollection<BoxManipItemViewModel> ModifyActions { get; } = [];

    /// <summary>Defaults to the current box so a single accidental Apply never touches every box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeText), nameof(ImpactText), nameof(ApplyAutomationName))]
    private bool _applyToAllBoxes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeText), nameof(ImpactText), nameof(ApplyAutomationName))]
    private int _currentBox;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ImpactText), nameof(IsDestructive), nameof(ApplyAutomationName),
        nameof(SelectedName), nameof(SelectedDescription), nameof(SelectedCategoryText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private BoxManipItemViewModel? _selectedAction;

    [ObservableProperty] private BoxManipItemViewModel? _selectedSort;
    [ObservableProperty] private BoxManipItemViewModel? _selectedClear;
    [ObservableProperty] private BoxManipItemViewModel? _selectedModify;

    partial void OnSelectedSortChanged(BoxManipItemViewModel? value) => Stage(value);
    partial void OnSelectedClearChanged(BoxManipItemViewModel? value) => Stage(value);
    partial void OnSelectedModifyChanged(BoxManipItemViewModel? value) => Stage(value);

    private void Stage(BoxManipItemViewModel? value)
    {
        if (value is null) return;
        if (!ReferenceEquals(SelectedSort, value)) SelectedSort = null;
        if (!ReferenceEquals(SelectedClear, value)) SelectedClear = null;
        if (!ReferenceEquals(SelectedModify, value)) SelectedModify = null;
        SelectedAction = value;
        StatusText = string.Empty;
    }

    partial void OnSelectedActionChanged(BoxManipItemViewModel? value)
    {
        if (value is not null) return;
        SelectedSort = SelectedClear = SelectedModify = null;
    }

    /// <summary>One-based box number for the scope picker.</summary>
    public int CurrentBoxNumber
    {
        get => CurrentBox + 1;
        set => CurrentBox = Math.Clamp(value - 1, 0, Math.Max(0, BoxCount - 1));
    }

    partial void OnCurrentBoxChanged(int value) => OnPropertyChanged(nameof(CurrentBoxNumber));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    [ObservableProperty] private bool _isStatusError;

    public int BoxCount => _sav.BoxCount;
    public bool HasSelection => SelectedAction is not null;
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);
    public bool IsDestructive => SelectedAction?.Category == BoxManipCategory.Delete;
    public bool CanUndo => _undoRedo is not null;
    public string SelectedName => SelectedAction?.Name ?? T("BoxManip_NoSelection");
    public string SelectedDescription => SelectedAction?.Description ?? T("BoxManip_SelectHint");
    public string SelectedCategoryText => SelectedAction?.CategoryText ?? string.Empty;

    public string ScopeText => ApplyToAllBoxes
        ? LocalizedStrings.Instance.Format("BoxManip_ScopeAll", BoxCount)
        : LocalizedStrings.Instance.Format("BoxManip_ScopeCurrent", CurrentBox + 1, GetBoxName(CurrentBox));

    public string ImpactText
    {
        get
        {
            if (SelectedAction is not { } item) return string.Empty;
            var (occupied, matching) = GetImpact(item.Manip);
            return matching is { } count
                ? LocalizedStrings.Instance.Format("BoxManip_ImpactDelete", count, occupied)
                : LocalizedStrings.Instance.Format("BoxManip_ImpactOccupied", occupied);
        }
    }

    public string ApplyAutomationName => SelectedAction is null ? T("BoxManip_Apply")
        : LocalizedStrings.Instance.Format("BoxManip_ApplyAutomation", SelectedAction.Name, ScopeText);

    private static string T(string key) => LocalizedStrings.Instance[key];

    private string GetBoxName(int box)
    {
        var name = _sav is IBoxDetailNameRead named ? named.GetBoxName(box) : string.Empty;
        return string.IsNullOrWhiteSpace(name) ? LocalizedStrings.Instance.Format("BoxManip_DefaultBoxName", box + 1) : name;
    }

    private void LoadManipulations()
    {
        foreach (var m in BoxManipDefaults.SortCommon.Concat(BoxManipDefaults.SortAdvanced))
            if (m.Usable(_sav)) SortActions.Add(new BoxManipItemViewModel(m, BoxManipCategory.Sort));
        foreach (var m in BoxManipDefaults.ClearCommon)
            if (m.Usable(_sav)) ClearActions.Add(new BoxManipItemViewModel(m, BoxManipCategory.Delete));
        foreach (var m in BoxManipDefaults.ModifyCommon)
            if (m.Usable(_sav)) ModifyActions.Add(new BoxManipItemViewModel(m, BoxManipCategory.Modify));
    }

    private (int Start, int Stop) Range => ApplyToAllBoxes ? (0, _sav.BoxCount - 1) : (CurrentBox, CurrentBox);

    private IEnumerable<(int Box, int Slot, PKM Pk)> OccupiedSlots()
    {
        var (start, stop) = Range;
        for (int box = start; box <= stop; box++)
        for (int slot = 0; slot < _sav.BoxSlotCount; slot++)
        {
            var pk = _sav.GetBoxSlotAtIndex(box, slot);
            if (pk.Species != 0) yield return (box, slot, pk);
        }
    }

    /// <summary>Occupied slots in scope, plus the exact number a delete would remove when it can be computed up front.</summary>
    public (int Occupied, int? Matching) GetImpact(BoxManipBase manip)
    {
        var occupied = OccupiedSlots().ToList();
        Func<PKM, bool>? criteria = manip switch
        {
            BoxManipClear clear => clear.Criteria,
            BoxManipClearComplex complex => pk => complex.Criteria(pk, _sav),
            _ => null,
        };
        int? matching = criteria is null ? null
            : occupied.Count(s => !_sav.IsBoxSlotOverwriteProtected(s.Box, s.Slot) && criteria(s.Pk));
        return (occupied.Count, matching);
    }

    private bool CanApply() => SelectedAction is not null && !IsBusy && _sav.BoxCount > 0;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (SelectedAction is not { } item || IsBusy) return;
        IsBusy = true; // blocks double-clicks for the whole confirm/apply sequence
        try
        {
            if (item.Category == BoxManipCategory.Delete)
            {
                var prompt = LocalizedStrings.Instance.Format(ApplyToAllBoxes ? "BoxManip_ConfirmDeleteAll" : "BoxManip_ConfirmDelete",
                    item.Name, ScopeText, ImpactText);
                var confirmed = await _dialogService.ShowConfirmationAsync(T("BoxManip_ConfirmTitle"), prompt,
                    T("BoxManip_ConfirmDeleteButton"), T("Common_Cancel"));
                if (!confirmed) { SetStatus(T("BoxManip_Cancelled"), false); return; }
            }

            var count = Execute(item.Manip);
            SetStatus(count > 0
                ? LocalizedStrings.Instance.Format(CanUndo ? "BoxManip_DoneUndo" : "BoxManip_Done", item.Name, count)
                : LocalizedStrings.Instance.Format("BoxManip_NoChange", item.Name), false);
            SelectedAction = null; // the next operation must be chosen and reviewed again
            if (count > 0) _onComplete?.Invoke();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
            await _dialogService.ShowErrorAsync(T("BoxManip_ErrorTitle"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs the manipulation over the scope; with an undo service the whole run is one undoable step.</summary>
    private int Execute(BoxManipBase manip)
    {
        var (start, stop) = Range;
        var param = new BoxManipParam(start, stop, false);
        if (_undoRedo is null) return manip.Execute(_sav, param);

        var slots = new List<ISlotInfo>();
        for (int box = start; box <= stop; box++)
        for (int slot = 0; slot < _sav.BoxSlotCount; slot++)
        {
            var info = new SlotInfoBox(box, slot, _sav);
            if (info.CanWriteTo(_sav)) slots.Add(info);
        }
        var count = 0;
        _undoRedo.ApplyBatch(slots, _ => count = manip.Execute(_sav, param));
        if (count > 0) _sav.State.Edited = true;
        return count;
    }

    private void SetStatus(string text, bool error) { IsStatusError = error; StatusText = text; }

    private void RefreshLanguage()
    {
        foreach (var item in SortActions.Concat(ClearActions).Concat(ModifyActions)) item.Refresh();
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(ImpactText));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(SelectedCategoryText));
        OnPropertyChanged(nameof(ApplyAutomationName));
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}

public sealed partial class BoxManipItemViewModel(BoxManipBase manip, BoxManipCategory category) : ObservableObject
{
    public BoxManipBase Manip { get; } = manip;
    public BoxManipType Type => Manip.Type;
    public BoxManipCategory Category { get; } = category;
    public bool IsDestructive => Category == BoxManipCategory.Delete;
    public string Name => LocalizedStrings.Instance[$"BoxManip_Action_{Type}"];
    public string Description => LocalizedStrings.Instance[$"BoxManip_Desc_{Type}"];
    public string CategoryText => LocalizedStrings.Instance[$"BoxManip_Category_{Category}"];

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CategoryText));
    }
}
