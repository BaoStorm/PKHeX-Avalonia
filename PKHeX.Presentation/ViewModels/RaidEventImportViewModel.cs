using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Validated, staged import of a Sword/Shield or Scarlet/Violet raid-event package (News/BCAT tables).
/// The save is untouched until Apply; Cancel/close discards the staged package.
/// </summary>
public partial class RaidEventImportViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly SaveFile _save;
    private readonly IDialogService? _dialogs;
    private RaidEventImportSession? _session;
    private RaidEventPackageError _error;
    private string? _errorDetail;
    private bool _conflict;
    private bool _disposed;

    public RaidEventImportViewModel(SaveFile save, IDialogService? dialogs = null)
    {
        _save = save;
        _dialogs = dialogs;
        TargetGame = RaidEventImportSession.GetTargetGame(save);
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => RefreshLanguage());
    }

    public RaidEventGame? TargetGame { get; }
    public bool IsSupported => TargetGame is not null;
    public Action? CloseRequested { get; set; }
    public ObservableCollection<RaidEventBlockRowViewModel> Blocks { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty] private string _packagePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isAcknowledged;

    public bool IsStaged => _session is not null;
    public bool RequiresAcknowledgement => _session?.Warnings.Count > 0;
    public bool HasWarnings => Warnings.Count != 0;
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);
    public bool IsError => _error != RaidEventPackageError.None || _conflict;
    public bool CanApply => _session is { HasChanges: true } && (!RequiresAcknowledgement || IsAcknowledged);

    public string TargetLabel => LocalizedStrings.Instance.Format("RaidEventImport_Target", TargetGame switch
    {
        RaidEventGame.SwordShield => T("RaidEventImport_Game_SwordShield"),
        RaidEventGame.ScarletViolet => T("RaidEventImport_Game_ScarletViolet"),
        _ => T("RaidEventImport_Game_Unsupported"),
    }, TargetGame is { } game && _save is ISaveFileRevision revision
        ? T($"RaidEventImport_SaveRevision_{game}{revision.SaveRevision}") : string.Empty);

    public string PackageLabel => _session is null ? string.Empty
        : LocalizedStrings.Instance.Format("RaidEventImport_Package", T($"RaidEventImport_Layout_{_session.Package.Layout}"));

    public string IdentifierLabel => _session?.Package.EventIdentifier is not { } id ? string.Empty
        : LocalizedStrings.Instance.Format("RaidEventImport_Identifier", id,
            _session.CurrentEventIdentifier is { } current ? current.ToString() : "-");

    public string StatusText
    {
        get
        {
            if (_conflict) return T("RaidEventImport_Conflict");
            if (_error != RaidEventPackageError.None)
                return LocalizedStrings.Instance.Format($"RaidEventImport_Error_{_error}", _errorDetail ?? string.Empty);
            if (_session is { HasChanges: false }) return T("RaidEventImport_AlreadyImported");
            return string.Empty;
        }
    }

    private static string T(string key) => LocalizedStrings.Instance[key];

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (_dialogs is null) return;
        var folder = await _dialogs.OpenFolderAsync(T("RaidEventImport_ChooseFolderTitle"));
        if (_disposed || string.IsNullOrEmpty(folder)) return;
        LoadPackage(folder);
    }

    /// <summary>Reads and validates a package folder against the save, replacing any previously staged package.</summary>
    public void LoadPackage(string folder)
    {
        if (_disposed) return;
        PackagePath = folder;
        var read = RaidEventPackageReader.ReadFolder(folder);
        if (!read.IsValid) { SetState(null, read.Error, read.Detail); return; }
        var plan = RaidEventImportSession.Create(_save, read.Package!);
        SetState(plan.Session, plan.Error, plan.Detail);
    }

    private void SetState(RaidEventImportSession? session, RaidEventPackageError error, string? detail)
    {
        _session = session;
        _error = error;
        _errorDetail = detail;
        _conflict = false;
        IsAcknowledged = false;
        RebuildRows();
        NotifyStateChanged();
    }

    private void RebuildRows()
    {
        Blocks.Clear();
        Warnings.Clear();
        if (_session is null) return;
        foreach (var block in _session.Blocks) Blocks.Add(new RaidEventBlockRowViewModel(block));
        foreach (var warning in _session.Warnings) Warnings.Add(T($"RaidEventImport_Warning_{warning}"));
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsStaged));
        OnPropertyChanged(nameof(RequiresAcknowledgement));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(PackageLabel));
        OnPropertyChanged(nameof(IdentifierLabel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void RefreshLanguage()
    {
        RebuildRows();
        OnPropertyChanged(nameof(TargetLabel));
        NotifyStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (!CanApply || _session is null) return;
        var result = _session.TryCommit();
        if (result == RaidEventCommitResult.Conflict)
        {
            // The staged snapshot is stale; drop it so the package must be re-validated against current data.
            _session = null;
            _conflict = true;
            IsAcknowledged = false;
            RebuildRows();
            NotifyStateChanged();
            return;
        }
        _session = null;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        _session = null;
        CloseRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session = null;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}

/// <summary>One destination table row in the import preview.</summary>
public sealed class RaidEventBlockRowViewModel(RaidEventBlockPlan plan)
{
    public string TableName => plan.TableName;
    public string BlockKey => $"0x{plan.BlockKey:X8}";
    public string FileName => plan.FileName ?? "-";
    public string Size => $"0x{plan.Length:X}";
    public RaidEventBlockChange Change => plan.Change;
    public string ChangeText => LocalizedStrings.Instance[$"RaidEventImport_Change_{plan.Change}"];
}
