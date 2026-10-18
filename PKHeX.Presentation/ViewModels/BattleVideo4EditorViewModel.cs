using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Platinum/HeartGold/SoulSilver battle video viewer with staged .bv4 import/export.
/// Imports change only the staged slot; Save writes changed slots, Cancel/close discards them.
/// </summary>
public partial class BattleVideo4EditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly BattleVideo4Session _session;
    private readonly IDialogService? _dialogs;
    private readonly ISpriteRenderer? _sprites;
    private bool _disposed;
    private bool _conflict;

    public BattleVideo4EditorViewModel(SAV4 save, IDialogService? dialogs = null, ISpriteRenderer? sprites = null)
    {
        _session = new BattleVideo4Session(save);
        _dialogs = dialogs;
        _sprites = sprites;
        IsSupported = BattleVideo4Session.IsSupported(save);
        foreach (var slot in _session.Slots) Slots.Add(new BattleVideo4SlotViewModel(slot));
        SelectedSlot = Slots.FirstOrDefault();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => RefreshAll());
    }

    public bool IsSupported { get; }
    public Action? CloseRequested { get; set; }
    public ObservableCollection<BattleVideo4SlotViewModel> Slots { get; } = [];
    public ObservableCollection<BattleVideo4PlayerViewModel> Players { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(ExportCommand), nameof(ResetSlotCommand))]
    private BattleVideo4SlotViewModel? _selectedSlot;

    [ObservableProperty] private bool _exportDecrypted;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isMessageError;

    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool HasSelectedVideo => SelectedSlot?.IsAvailable == true;
    public string SelectedTitle => SelectedSlot is null ? T("BattleVideo4_Title")
        : LocalizedStrings.Instance.Format("BattleVideo4_SlotTitle", SelectedSlot.Index + 1);
    public string SelectedStatus => SelectedSlot is not { IsAvailable: true } slot ? T("BattleVideo4_SlotUnavailable") : GetStatus(slot.Slot.Video!);
    public bool CanSave => _session.HasChanges && !_conflict;

    private static string T(string key) => LocalizedStrings.Instance[key];

    partial void OnSelectedSlotChanged(BattleVideo4SlotViewModel? value) => RefreshSelection();
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    private static string GetStatus(BattleVideo4 video)
    {
        var teams = video.GetTeams();
        var names = video.GetTrainerNames();
        return LocalizedStrings.Instance.Format("BattleVideo4_Status",
            T(video.ChecksumValid ? "BattleVideo4_ChecksumValid" : "BattleVideo4_ChecksumInvalid"),
            names.Count(n => !string.IsNullOrWhiteSpace(n)), BattleVideo4.TrainerCount,
            teams.Sum(t => t.Length), $"0x{video.BlockID:X4}");
    }

    private void RefreshSelection()
    {
        Players.Clear();
        if (SelectedSlot is { IsAvailable: true } slot)
        {
            var video = slot.Slot.Video!;
            var teams = video.GetTeams();
            var names = video.GetTrainerNames();
            var active = Math.Max(2, teams.Count(t => t.Length != 0));
            for (int trainer = 0; trainer < BattleVideo4.TrainerCount; trainer++)
            {
                if (teams[trainer].Length == 0 && trainer >= active) continue;
                Players.Add(new BattleVideo4PlayerViewModel(trainer + 1, names[trainer], teams[trainer], _sprites));
            }
        }
        OnPropertyChanged(nameof(HasSelectedVideo));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedStatus));
    }

    private void RefreshAll()
    {
        foreach (var slot in Slots) slot.Refresh();
        RefreshSelection();
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void SetMessage(string text, bool error) { IsMessageError = error; Message = text; }

    private bool CanUseSelectedSlot() => SelectedSlot?.IsAvailable == true && !_conflict;
    private bool CanResetSlot() => SelectedSlot?.IsChanged == true && !_conflict;

    [RelayCommand(CanExecute = nameof(CanUseSelectedSlot))]
    private async Task ImportAsync()
    {
        if (_dialogs is null || SelectedSlot is not { } slot) return;
        var path = await _dialogs.OpenFileAsync(T("BattleVideo4_ImportTitle"), [$"*.{BattleVideo4.Extension}", "*"]);
        if (_disposed || string.IsNullOrEmpty(path)) return;
        try
        {
            var info = new FileInfo(path);
            if (info.Length != BattleVideo4.SIZE_USED) { ShowImportError(BattleVideo4ImportError.InvalidSize); return; }
            ImportData(slot.Index, await File.ReadAllBytesAsync(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMessage(LocalizedStrings.Instance.Format("BattleVideo4_ReadFailed", ex.Message), true);
        }
    }

    /// <summary>Stages a .bv4 payload into a slot and reports the localized result.</summary>
    public BattleVideo4ImportError ImportData(int index, byte[] data)
    {
        var result = _session.Import(index, data);
        if (result != BattleVideo4ImportError.None) { ShowImportError(result); return result; }
        SetMessage(LocalizedStrings.Instance.Format("BattleVideo4_Imported", index + 1), false);
        RefreshAll();
        ResetSlotCommand.NotifyCanExecuteChanged();
        return result;
    }

    private void ShowImportError(BattleVideo4ImportError error) =>
        SetMessage(LocalizedStrings.Instance.Format($"BattleVideo4_Error_{error}", BattleVideo4.SIZE_USED), true);

    [RelayCommand(CanExecute = nameof(CanUseSelectedSlot))]
    private async Task ExportAsync()
    {
        if (_dialogs is null || SelectedSlot is not { } slot) return;
        var data = _session.Export(slot.Index, ExportDecrypted);
        if (data is null) return;
        var name = $"{PathUtil.CleanFileName(slot.Name)}.{BattleVideo4.Extension}";
        var path = await _dialogs.SaveFileAsync(T("BattleVideo4_ExportTitle"), name, [$"*.{BattleVideo4.Extension}"]);
        if (_disposed || string.IsNullOrEmpty(path)) return;
        try
        {
            await File.WriteAllBytesAsync(path, data);
            SetMessage(LocalizedStrings.Instance.Format("BattleVideo4_Exported", Path.GetFileName(path)), false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMessage(LocalizedStrings.Instance.Format("BattleVideo4_WriteFailed", ex.Message), true);
        }
    }

    /// <summary>Returns the bytes Export would write for a slot (used by tests and the export command).</summary>
    public byte[]? GetExportData(int index) => _session.Export(index, ExportDecrypted);

    [RelayCommand(CanExecute = nameof(CanResetSlot))]
    private void ResetSlot()
    {
        if (SelectedSlot is not { } slot) return;
        _session.Reset(slot.Index);
        SetMessage(string.Empty, false);
        RefreshAll();
        ResetSlotCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var result = _session.TryCommit();
        if (result == BattleVideo4CommitResult.Conflict)
        {
            _conflict = true;
            SetMessage(T("BattleVideo4_Conflict"), true);
            OnPropertyChanged(nameof(CanSave));
            SaveCommand.NotifyCanExecuteChanged();
            ImportCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            ResetSlotCommand.NotifyCanExecuteChanged();
            return;
        }
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}

public sealed partial class BattleVideo4SlotViewModel(BattleVideo4Slot slot) : ObservableObject
{
    public BattleVideo4Slot Slot => slot;
    public int Index => slot.Index;
    public bool IsAvailable => slot.IsAvailable;
    public bool IsChanged => slot.IsChanged;

    /// <summary>"A vs B" from the stored trainer names; localized placeholders when absent.</summary>
    public string Name
    {
        get
        {
            if (slot.Video is not { } video) return LocalizedStrings.Instance["BattleVideo4_NotAvailable"];
            var names = video.GetTrainerNames().Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            return names.Length == 0 ? LocalizedStrings.Instance["BattleVideo4_Empty"]
                : string.Join(LocalizedStrings.Instance["BattleVideo4_Versus"], names);
        }
    }

    public string Label => $"{Index + 1:00} - {Name}{(IsChanged ? " *" : string.Empty)}";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(IsChanged));
    }
}

public sealed class BattleVideo4PlayerViewModel
{
    public BattleVideo4PlayerViewModel(int number, string name, PK4[] team, ISpriteRenderer? sprites)
    {
        if (string.IsNullOrWhiteSpace(name)) name = team.FirstOrDefault()?.OriginalTrainerName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) name = LocalizedStrings.Instance["BattleVideo4_UnknownPlayer"];
        Header = LocalizedStrings.Instance.Format("BattleVideo4_Player", number, name);
        Members = team.Select((pk, i) => new BattleVideo4MemberViewModel(i + 1, pk, sprites)).ToArray();
    }

    public string Header { get; }
    public IReadOnlyList<BattleVideo4MemberViewModel> Members { get; }
    public bool IsEmpty => Members.Count == 0;
}

public sealed class BattleVideo4MemberViewModel(int position, PK4 pk, ISpriteRenderer? sprites)
{
    public int Position => position;
    public string Species => pk.Species < GameInfo.Strings.Species.Count ? GameInfo.Strings.Species[pk.Species] : $"{pk.Species:000}";
    public string Nickname => string.IsNullOrWhiteSpace(pk.Nickname) ? "-" : pk.Nickname;
    public byte Level => pk.CurrentLevel;
    public string LevelText => LocalizedStrings.Instance.Format("BattleVideo4_Level", pk.CurrentLevel);
    public byte[]? Sprite { get; } = sprites?.GetSprite(pk);
}
