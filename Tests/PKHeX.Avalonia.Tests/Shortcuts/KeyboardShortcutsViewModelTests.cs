using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using PKHeX.Application.Shortcuts;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// The shortcut editor dialog view model (issue #224): every recorded chord is validated against
/// the platform rules and the whole draft, nothing is stored before Save, and conflicts move a
/// shortcut only after the user confirms.
/// </summary>
public sealed class KeyboardShortcutsViewModelTests
{
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
    private const ShortcutModifiers Shift = ShortcutModifiers.Shift;
    private const ShortcutModifiers Alt = ShortcutModifiers.Alt;
    private const string Encounter = "OpenEncounterDatabase";

    private static LocalizedStrings L => LocalizedStrings.Instance;

    // ---------------------------------------------------------------------
    // Conflicts
    // ---------------------------------------------------------------------

    [Fact]
    public void Conflict_KeepsTheDraft_UntilReassignMovesTheShortcut()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore();
        var (main, editor, _) = ShortcutTestViewModels.CreateEditor(settings, store);
        var encounter = Row(editor, Encounter);
        var launcher = Row(editor, ShortcutActionId.OpenToolLauncher);
        var ctrlK = new ShortcutChord(Ctrl, "K");

        encounter.RecordCommand.Execute(ctrlK);

        // The conflict names the action that holds the chord and changes nothing yet.
        Assert.Equal(new ShortcutChord(Ctrl, "N"), encounter.Chord);
        Assert.Equal(ctrlK, launcher.Chord);
        Assert.True(encounter.HasPendingConflict);
        Assert.True(encounter.ReassignCommand.CanExecute(null));
        Assert.Equal(L.Format("Shortcuts_Conflict", "Ctrl+K", launcher.Title), encounter.Message);
        Assert.Contains(AccessKeyText.Strip(L["Launcher_Title"]), encounter.Message);
        Assert.False(editor.HasChanges);

        encounter.ReassignCommand.Execute(null);

        Assert.Equal(ctrlK, encounter.Chord);
        Assert.False(encounter.HasMessage);
        Assert.False(encounter.HasPendingConflict);
        Assert.False(encounter.ReassignCommand.CanExecute(null));
        Assert.Null(launcher.Chord);
        Assert.Equal(L.Format("Shortcuts_Moved", encounter.Title), launcher.Message);
        Assert.Null(store.Saved);

        editor.SaveCommand.Execute(null);

        Assert.Equal(1, store.SaveCount);
        Assert.Equal("Primary+K", store.Saved!.KeyboardShortcuts[Encounter]);
        Assert.Equal(string.Empty, store.Saved.KeyboardShortcuts[ShortcutActionId.OpenToolLauncher]);
        // The main window rebinds from the service's Changed event.
        Assert.Equal(ctrlK, main.Shortcuts[Encounter].Shortcut);
        Assert.Null(main.Shortcuts.OpenToolLauncher.Shortcut);
    }

    [Fact]
    public void ResetRow_RestoresTheDefault_ThroughTheSameConflictCheck()
    {
        var (_, editor, _) = ShortcutTestViewModels.CreateEditor(new AppSettings(), new FakeSettingsStore());
        var encounter = Row(editor, Encounter);
        var database = Row(editor, "OpenPKMDatabase");
        var ctrlN = new ShortcutChord(Ctrl, "N");

        encounter.RecordCommand.Execute(new ShortcutChord(ShortcutModifiers.None, "F5"));
        Assert.True(encounter.IsCustomized);
        Assert.True(encounter.ResetCommand.CanExecute(null));
        encounter.ResetCommand.Execute(null);
        Assert.Equal(ctrlN, encounter.Chord);
        Assert.False(encounter.IsCustomized);
        Assert.False(encounter.ResetCommand.CanExecute(null));

        // Move Ctrl+N away, then restore it: the default is held by another action now.
        database.RecordCommand.Execute(ctrlN);
        database.ReassignCommand.Execute(null);
        Assert.Null(encounter.Chord);
        Assert.Equal(ctrlN, database.Chord);

        encounter.ResetCommand.Execute(null);
        Assert.Null(encounter.Chord);
        Assert.True(encounter.HasPendingConflict);
        Assert.Equal(L.Format("Shortcuts_Conflict", "Ctrl+N", database.Title), encounter.Message);

        encounter.ReassignCommand.Execute(null);
        Assert.Equal(ctrlN, encounter.Chord);
        Assert.Null(database.Chord);
    }

    // ---------------------------------------------------------------------
    // Rejected chords
    // ---------------------------------------------------------------------

    [Fact]
    public void UnusableChords_ExplainWhy_AndLeaveTheRowUnchanged()
    {
        var (_, editor, _) = ShortcutTestViewModels.CreateEditor(new AppSettings(), new FakeSettingsStore());
        var encounter = Row(editor, Encounter);
        var ctrlN = new ShortcutChord(Ctrl, "N");

        encounter.RecordCommand.Execute(new ShortcutChord(Ctrl, "C"));
        Assert.Equal(L.Format("Shortcuts_Error_Reserved", "Ctrl+C", L["Shortcuts_Reserved_TextEditing"]), encounter.Message);
        Assert.Equal(ctrlN, encounter.Chord);

        encounter.RecordCommand.Execute(new ShortcutChord(ShortcutModifiers.None, "N"));
        Assert.Equal(L.Format("Shortcuts_Error_NeedsModifier", "Ctrl"), encounter.Message);

        encounter.RecordCommand.Execute(new ShortcutChord(Ctrl | Alt, "E"));
        Assert.Equal(L["Shortcuts_Error_AltGr"], encounter.Message);

        // A key outside the vocabulary (as the recorder reports it by enum name).
        encounter.RecordCommand.Execute(new ShortcutChord(Ctrl, "OemPlus"));
        Assert.Equal(L["Shortcuts_Error_UnsupportedKey"], encounter.Message);

        Assert.Equal(ctrlN, encounter.Chord);
        Assert.False(encounter.HasPendingConflict);
        Assert.False(editor.HasChanges);

        // Recording the current chord again clears the message without a change.
        encounter.RecordCommand.Execute(ctrlN);
        Assert.False(encounter.HasMessage);
        Assert.False(editor.HasChanges);

        // A bare function key needs no modifier.
        encounter.RecordCommand.Execute(new ShortcutChord(ShortcutModifiers.None, "F5"));
        Assert.Equal(new ShortcutChord(ShortcutModifiers.None, "F5"), encounter.Chord);
        Assert.True(editor.HasChanges);
    }

    [Fact]
    public void Clear_RemovesTheShortcut_AndSaveStoresTheRemovedDefault()
    {
        var store = new FakeSettingsStore();
        var (_, editor, _) = ShortcutTestViewModels.CreateEditor(new AppSettings(), store);
        var encounter = Row(editor, Encounter);
        Assert.Equal(L.Format("Shortcuts_CustomizedCount", 0), editor.CustomizedSummary);
        Assert.True(encounter.ClearCommand.CanExecute(null));

        encounter.ClearCommand.Execute(null);

        Assert.Null(encounter.Chord);
        Assert.False(encounter.HasShortcut);
        Assert.Equal(L["Shortcuts_None"], encounter.ShortcutText);
        Assert.False(encounter.ClearCommand.CanExecute(null));
        Assert.True(encounter.IsCustomized);
        Assert.Equal(L.Format("Shortcuts_CustomizedCount", 1), editor.CustomizedSummary);

        editor.SaveCommand.Execute(null);
        Assert.Equal(string.Empty, store.Saved!.KeyboardShortcuts[Encounter]);
    }

    // ---------------------------------------------------------------------
    // Cancel, Save and the editor session
    // ---------------------------------------------------------------------

    [Fact]
    public void Cancel_DiscardsTheDraft()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore();
        var (main, editor, service) = ShortcutTestViewModels.CreateEditor(settings, store);
        var closed = 0;
        editor.CloseRequested = () => closed++;
        Assert.True(service.IsEditorOpen);

        Row(editor, Encounter).RecordCommand.Execute(new ShortcutChord(ShortcutModifiers.None, "F5"));
        editor.CancelCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Null(store.Saved);
        Assert.Empty(settings.KeyboardShortcuts);
        Assert.False(service.IsEditorOpen);
        Assert.Equal(new ShortcutChord(Ctrl, "N"), main.Shortcuts[Encounter].Shortcut);
    }

    [Fact]
    public void Save_WithoutChanges_StoresNothing_AndCloses()
    {
        var store = new FakeSettingsStore();
        var (_, editor, service) = ShortcutTestViewModels.CreateEditor(new AppSettings(), store);
        var closed = false;
        editor.CloseRequested = () => closed = true;

        editor.SaveCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal(0, store.SaveCount);
        Assert.False(service.IsEditorOpen);
    }

    [Fact]
    public void Cancel_IsUnavailableWhileARowRecords()
    {
        var (_, editor, _) = ShortcutTestViewModels.CreateEditor(new AppSettings(), new FakeSettingsStore());
        var row = Row(editor, Encounter);
        var changes = new List<string?>();
        editor.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.True(editor.CancelCommand.CanExecute(null));

        row.IsRecording = true;
        Assert.True(editor.IsRecording);
        Assert.False(editor.CancelCommand.CanExecute(null));
        Assert.Contains(nameof(KeyboardShortcutsViewModel.IsRecording), changes);

        row.IsRecording = false;
        Assert.False(editor.IsRecording);
        Assert.True(editor.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void Dispose_ReleasesTheEditorFlag()
    {
        var (_, editor, service) = ShortcutTestViewModels.CreateEditor(new AppSettings(), new FakeSettingsStore());
        Assert.True(service.IsEditorOpen);

        editor.Dispose();
        Assert.False(service.IsEditorOpen);

        editor.Dispose();
        Assert.False(service.IsEditorOpen);
    }

    // ---------------------------------------------------------------------
    // Reset all
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ResetAll_AsksFirst_ThenRestoresEveryDefault()
    {
        var settings = new AppSettings();
        settings.KeyboardShortcuts[Encounter] = "Primary+Shift+N";
        settings.KeyboardShortcuts["OpenBoxReport"] = string.Empty;
        settings.KeyboardShortcuts["FutureAction"] = "Primary+J";
        var store = new FakeSettingsStore();
        var dialogs = new RecordingDialogService();
        var (_, editor, _) = ShortcutTestViewModels.CreateEditor(settings, store, dialogs);
        var encounter = Row(editor, Encounter);
        var boxReport = Row(editor, "OpenBoxReport");
        Assert.Equal(new ShortcutChord(Ctrl | Shift, "N"), encounter.Chord);
        Assert.Null(boxReport.Chord);
        Assert.Equal(L.Format("Shortcuts_CustomizedCount", 2), editor.CustomizedSummary);

        dialogs.ConfirmResult = false;
        await editor.ResetAllCommand.ExecuteAsync(null);
        var (title, message) = Assert.Single(dialogs.Confirmations);
        Assert.Equal(L["Shortcuts_ResetAll"], title);
        Assert.Equal(L["Shortcuts_ResetAll_Confirm"], message);
        Assert.Equal(new ShortcutChord(Ctrl | Shift, "N"), encounter.Chord);
        Assert.False(editor.HasChanges);

        dialogs.ConfirmResult = true;
        await editor.ResetAllCommand.ExecuteAsync(null);
        Assert.Equal(new ShortcutChord(Ctrl, "N"), encounter.Chord);
        Assert.Equal(new ShortcutChord(Ctrl, "R"), boxReport.Chord);
        Assert.All(editor.Rows, row => Assert.False(row.IsCustomized));
        Assert.Equal(L.Format("Shortcuts_CustomizedCount", 0), editor.CustomizedSummary);

        editor.SaveCommand.Execute(null);
        var stored = store.Saved!.KeyboardShortcuts;
        Assert.DoesNotContain(Encounter, stored.Keys);
        Assert.DoesNotContain("OpenBoxReport", stored.Keys);
        Assert.Equal("Primary+J", stored["FutureAction"]);
    }

    // ---------------------------------------------------------------------
    // Search and display
    // ---------------------------------------------------------------------

    [Fact]
    public void Search_MatchesTitleMenuOrShortcut()
    {
        var (_, editor, _) = ShortcutTestViewModels.CreateEditor(new AppSettings(), new FakeSettingsStore());
        var encounter = Row(editor, Encounter);
        var open = Row(editor, ShortcutActionId.OpenFile);
        Assert.Equal(editor.Rows.Count, editor.FilteredRows.Count);

        editor.SearchText = encounter.Title.ToUpperInvariant();
        Assert.Contains(encounter, editor.FilteredRows);
        Assert.DoesNotContain(open, editor.FilteredRows);

        editor.SearchText = "Ctrl+N";
        Assert.Contains(encounter, editor.FilteredRows);
        Assert.DoesNotContain(open, editor.FilteredRows);

        editor.SearchText = AccessKeyText.Strip(L["Menu_File"]);
        Assert.Contains(open, editor.FilteredRows);

        editor.SearchText = "no action has this text";
        Assert.Empty(editor.FilteredRows);

        editor.SearchText = string.Empty;
        Assert.Equal(editor.Rows, editor.FilteredRows);
    }

    [Fact]
    public void Rows_FollowTheCatalogOrder_AndDescribeThemselves()
    {
        var (main, editor, _) = ShortcutTestViewModels.CreateEditor(new AppSettings(), new FakeSettingsStore());

        Assert.Equal(main.Shortcuts.All.Select(action => action.Id), editor.Rows.Select(row => row.ActionId));

        var encounter = Row(editor, Encounter);
        Assert.Equal("Ctrl+N", encounter.ShortcutText);
        Assert.Equal(L.Format("Shortcuts_Default", "Ctrl+N"), encounter.DefaultText);
        Assert.Equal(L.Format("Shortcuts_RecorderName", encounter.Title), encounter.RecorderAutomationName);
        Assert.Equal(L.Format("Shortcuts_ClearFor", encounter.Title), encounter.ClearAutomationName);
        Assert.Equal(L.Format("Shortcuts_ResetFor", encounter.Title), encounter.ResetAutomationName);

        var close = Row(editor, ShortcutActionId.CloseFile);
        Assert.Null(close.Default);
        Assert.Equal(L["Shortcuts_None"], close.ShortcutText);
        Assert.Equal(L.Format("Shortcuts_Default", L["Shortcuts_None"]), close.DefaultText);
        Assert.False(close.ResetCommand.CanExecute(null));
    }

    [Fact]
    public void StoredIssues_AreShownWhenTheEditorOpens()
    {
        var settings = new AppSettings();
        settings.KeyboardShortcuts["OpenBoxReport"] = "Hyper+R";
        settings.KeyboardShortcuts["OpenMisc3"] = "Primary+N";
        var store = new FakeSettingsStore();
        var (_, editor, _) = ShortcutTestViewModels.CreateEditor(settings, store);
        var boxReport = Row(editor, "OpenBoxReport");
        var encounter = Row(editor, Encounter);
        var misc3 = Row(editor, "OpenMisc3");

        Assert.Equal(L.Format("Shortcuts_Issue_InvalidStored", "Hyper+R"), boxReport.Message);
        Assert.Equal(new ShortcutChord(Ctrl, "R"), boxReport.Chord);
        Assert.Equal(L.Format("Shortcuts_Issue_SuppressedDefault", "Ctrl+N", misc3.Title), encounter.Message);
        Assert.Null(encounter.Chord);
        Assert.Equal(new ShortcutChord(Ctrl, "N"), misc3.Chord);

        // Save stores what the editor shows: the suppressed default stays off, and the stored text
        // the editor could not read is kept for a build that can.
        editor.SaveCommand.Execute(null);
        Assert.Equal(string.Empty, store.Saved!.KeyboardShortcuts[Encounter]);
        Assert.Equal("Hyper+R", store.Saved.KeyboardShortcuts["OpenBoxReport"]);
        Assert.Equal("Primary+N", store.Saved.KeyboardShortcuts["OpenMisc3"]);
    }

    [Fact]
    public void DuplicateStoredShortcuts_AreShownInactive_AndSaveResolvesThem()
    {
        var settings = new AppSettings();
        settings.KeyboardShortcuts["OpenMisc3"] = "Primary+Shift+J";
        settings.KeyboardShortcuts["OpenMisc4"] = "Primary+Shift+J";
        var store = new FakeSettingsStore();
        var (main, editor, _) = ShortcutTestViewModels.CreateEditor(settings, store);
        var misc3 = Row(editor, "OpenMisc3");
        var misc4 = Row(editor, "OpenMisc4");

        Assert.Null(misc3.Chord);
        Assert.Null(misc4.Chord);
        Assert.Equal(L.Format("Shortcuts_Issue_DuplicateStored", "Ctrl+Shift+J", misc4.Title), misc3.Message);

        // Keep one of them; Save removes the other so the stored state matches the editor.
        misc3.RecordCommand.Execute(new ShortcutChord(Ctrl | Shift, "J"));
        Assert.Equal(new ShortcutChord(Ctrl | Shift, "J"), misc3.Chord);
        editor.SaveCommand.Execute(null);

        Assert.Equal("Primary+Shift+J", store.Saved!.KeyboardShortcuts["OpenMisc3"]);
        Assert.DoesNotContain("OpenMisc4", store.Saved.KeyboardShortcuts.Keys);
        Assert.Equal(new ShortcutChord(Ctrl | Shift, "J"), main.Shortcuts["OpenMisc3"].Shortcut);
        Assert.Null(main.Shortcuts["OpenMisc4"].Shortcut);
    }

    // ---------------------------------------------------------------------
    // Entry points
    // ---------------------------------------------------------------------

    [Fact]
    public async Task HelpCommand_OpensOneEditorAtATime()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore();
        var service = new KeyboardShortcutService(settings, store, StandardShortcutPlatform.Instance);
        var windows = new NoopWindowService();
        var main = ShortcutTestViewModels.CreateMainViewModel(settings, store, service, windowService: windows);
        Assert.True(main.OpenKeyboardShortcutsCommand.CanExecute(null));

        await main.OpenKeyboardShortcutsCommand.ExecuteAsync(null);

        var (viewModel, title) = Assert.Single(windows.ShownDialogs);
        var editor = Assert.IsType<KeyboardShortcutsViewModel>(viewModel);
        Assert.Equal(L["Dialog_KeyboardShortcuts"], title);
        Assert.True(service.IsEditorOpen);

        // While it is open, a second request (for example its own shortcut) does not stack another.
        await main.OpenKeyboardShortcutsCommand.ExecuteAsync(null);
        Assert.Single(windows.ShownDialogs);

        editor.Dispose();
        await main.OpenKeyboardShortcutsCommand.ExecuteAsync(null);
        Assert.Equal(2, windows.ShownDialogs.Count);
    }

    [Fact]
    public async Task SettingsSection_OpensTheEditorThroughTheMainWindow()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore();
        var service = new KeyboardShortcutService(settings, store, StandardShortcutPlatform.Instance);
        var windows = new NoopWindowService();
        var main = ShortcutTestViewModels.CreateMainViewModel(settings, store, service, windowService: windows);

        await main.OpenSettingsCommand.ExecuteAsync(null);
        var settingsVm = Assert.IsType<SettingsViewModel>(Assert.Single(windows.ShownDialogs).ViewModel);
        Assert.True(settingsVm.CanOpenKeyboardShortcuts);

        await settingsVm.OpenKeyboardShortcutsCommand.ExecuteAsync(null);

        Assert.IsType<KeyboardShortcutsViewModel>(windows.ShownDialogs[^1].ViewModel);
        Assert.Null(store.Saved);
    }

    [Fact]
    public void SettingsSection_IsHiddenWithoutAnEditor()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore();
        var vm = new SettingsViewModel(settings, store, new Mock<IThemeService>().Object, new Mock<IUiDensityService>().Object,
            new LanguageService(), UpdateTestDoubles.Coordinator());

        Assert.False(vm.CanOpenKeyboardShortcuts);
        Assert.False(vm.OpenKeyboardShortcutsCommand.CanExecute(null));
    }

    private static KeyboardShortcutRowViewModel Row(KeyboardShortcutsViewModel editor, string id) =>
        Assert.Single(editor.Rows, row => row.ActionId == id);
}
