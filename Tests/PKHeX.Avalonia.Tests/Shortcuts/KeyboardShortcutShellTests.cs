using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Application.Shortcuts;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Infrastructure.Configuration;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// Main-window keyboard shortcuts pressed through the composed headless app (issue #224): the real
/// key routing (the binder's tunnelling key handler on the window runs before the focused control)
/// reaches the bound view model actions.
/// </summary>
public sealed class KeyboardShortcutShellTests
{
    // ---------------------------------------------------------------------
    // 9a. Canary: a window-level binding fires from inside a focused child view.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void PrimaryK_FromFocusedBoxViewer_OpensToolLauncher()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        app.Focus(app.Find<BoxViewer>()!);

        app.PressKey(PhysicalKey.K, RawInputModifiers.Control);

        Assert.True(app.ViewModel.IsToolLauncherOpen);
    }

    // ---------------------------------------------------------------------
    // 9b. Ctrl+1/2/3 match the top-row digit keys.
    //     Regression: KeyGesture.Parse("Ctrl+1") read "1" as Key.Cancel (enum value 1), so the digit
    //     key never matched; "2" was Key.Back and "3" was Key.Tab.
    // ---------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(PhysicalKey.Digit1, MainWorkspace.Save, MainWorkspace.Pokemon)]
    [InlineData(PhysicalKey.Digit2, MainWorkspace.Pokemon, MainWorkspace.Save)]
    [InlineData(PhysicalKey.Digit3, MainWorkspace.Pokemon, MainWorkspace.Reports)]
    public void PrimaryDigit_SwitchesWorkspace(PhysicalKey digit, MainWorkspace start, MainWorkspace expected)
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        app.ViewModel.SelectWorkspaceCommand.Execute(start);
        app.Pump();
        Assert.Equal(start, app.ViewModel.ActiveWorkspace);

        app.PressKey(digit, RawInputModifiers.Control);

        Assert.Equal(expected, app.ViewModel.ActiveWorkspace);
    }

    // ---------------------------------------------------------------------
    // 9c. The old Ctrl+2 / Ctrl+3 bindings were really Ctrl+Backspace / Ctrl+Tab and took those keys
    //     from every text box (window bindings run before the focused control).
    // ---------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(PhysicalKey.Backspace)]
    [InlineData(PhysicalKey.Tab)]
    public void PrimaryBackspaceOrTab_InTextBox_DoesNotSwitchWorkspace(PhysicalKey key)
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        Assert.Equal(MainWorkspace.Pokemon, app.ViewModel.ActiveWorkspace);
        var nickname = app.FindByName<TextBox>("NicknameField");
        Assert.NotNull(nickname);
        app.Focus(nickname!);

        app.PressKey(key, RawInputModifiers.Control);

        Assert.Equal(MainWorkspace.Pokemon, app.ViewModel.ActiveWorkspace);
    }

    // ---------------------------------------------------------------------
    // 9d. Encounter Database default (Primary+N): needs a save, opens once, then focuses.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void PrimaryN_OpensTheEncounterDatabaseOnce_ThenFocusesIt()
    {
        using var app = new HeadlessAppFixture();

        app.PressKey(PhysicalKey.N, RawInputModifiers.Control);
        Assert.Empty(app.Windows.ShownTools);

        app.LoadSaveInstance(new SAV6XY());
        app.Focus(app.Find<BoxViewer>()!);
        app.PressKey(PhysicalKey.N, RawInputModifiers.Control);

        var (tool, title) = Assert.Single(app.Windows.ShownTools);
        Assert.IsType<EncounterDatabaseViewModel>(tool);
        Assert.Equal(LocalizedStrings.Instance["Dialog_EncounterDatabase"], title);

        app.PressKey(PhysicalKey.N, RawInputModifiers.Control);
        Assert.Single(app.Windows.ShownTools);
        Assert.Same(tool, Assert.Single(app.Windows.FocusedTools));
    }

    [AvaloniaFact]
    public void PrimaryN_FromTheOpenLauncher_OpensTheToolAndClosesTheLauncher()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        app.Focus(app.Find<BoxViewer>()!);
        app.PressKey(PhysicalKey.K, RawInputModifiers.Control);
        Assert.True(app.ViewModel.IsToolLauncherOpen);

        // Focus the launcher's search box, as when the user starts typing a tool name.
        var search = app.Window.GetVisualDescendants().OfType<TextBox>()
            .Single(box => box.Classes.Contains("launcher-search-input"));
        app.Focus(search);
        app.PressKey(PhysicalKey.N, RawInputModifiers.Control);

        Assert.False(app.ViewModel.IsToolLauncherOpen);
        Assert.IsType<EncounterDatabaseViewModel>(Assert.Single(app.Windows.ShownTools).ViewModel);
    }

    // ---------------------------------------------------------------------
    // 9e. A stored override runs only where its tool applies to the loaded save.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void StoredOverride_RunsOnlyForSavesTheToolSupports()
    {
        var settings = new AppSettings();
        settings.KeyboardShortcuts["OpenMisc3"] = "Primary+Shift+M";
        settings.KeyboardShortcuts["OpenPokedex"] = "Primary+Shift+P";
        using var app = new HeadlessAppFixture(services => services.AddSingleton(settings));
        const RawInputModifiers ctrlShift = RawInputModifiers.Control | RawInputModifiers.Shift;
        var miscGesture = new KeyGesture(Key.M, KeyModifiers.Control | KeyModifiers.Shift);

        app.LoadSaveInstance(new SAV6XY());
        app.Focus(app.Find<BoxViewer>()!);
        // The Gen 3 editor's binding stays unavailable on a Gen 6 save, so the key is not consumed.
        Assert.False(FindOwnedBinding(app, miscGesture).Command!.CanExecute(null));
        app.PressKey(PhysicalKey.M, ctrlShift);
        Assert.Empty(app.Windows.ShownDialogs);

        // The Pokédex is registered in every generation group and runs for this save's generation.
        app.PressKey(PhysicalKey.P, ctrlShift);
        Assert.IsType<Pokedex6EditorViewModel>(Assert.Single(app.Windows.ShownDialogs).ViewModel);

        app.LoadSaveInstance(new SAV3E());
        app.Focus(app.Find<BoxViewer>()!);
        Assert.True(FindOwnedBinding(app, miscGesture).Command!.CanExecute(null));
        app.PressKey(PhysicalKey.M, ctrlShift);
        Assert.IsType<Misc3EditorViewModel>(app.Windows.ShownDialogs.Last().ViewModel);
    }

    // ---------------------------------------------------------------------
    // 9f. Undo/Redo shortcuts leave text boxes their own text undo.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void PrimaryZ_UndoesSlotChanges_ExceptWhileTypingInATextBox()
    {
        using var app = new HeadlessAppFixture();
        var save = new SAV6XY();
        save.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        app.LoadSaveInstance(save);
        var box = app.BoxViewer!;
        box.RequestMoveCommand.Execute((box.CreateDragData(0), box.Slots[1], false));
        app.Pump();
        Assert.Equal(25, box.Slots[1].Species);
        Assert.True(app.ViewModel.UndoCommand.CanExecute(null));

        var nickname = app.FindByName<TextBox>("NicknameField");
        Assert.NotNull(nickname);
        app.Focus(nickname!);
        app.PressKey(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(25, box.Slots[1].Species);
        Assert.True(app.ViewModel.UndoCommand.CanExecute(null));

        // Shortcuts without a text meaning still run from the text box.
        app.PressKey(PhysicalKey.N, RawInputModifiers.Control);
        Assert.IsType<EncounterDatabaseViewModel>(Assert.Single(app.Windows.ShownTools).ViewModel);

        app.Focus(app.Find<BoxViewer>()!);
        app.PressKey(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(25, box.Slots[0].Species);
        Assert.Equal(0, box.Slots[1].Species);

        app.PressKey(PhysicalKey.Y, RawInputModifiers.Control);
        Assert.Equal(25, box.Slots[1].Species);
    }

    [AvaloniaFact]
    public void UndoOnAChordWithoutATextMeaning_RunsFromATextBox()
    {
        // Upstream PKHeX's Ctrl+U: a text box has no Ctrl+U gesture, so yielding to it would leave
        // the key doing nothing.
        var settings = new AppSettings();
        settings.KeyboardShortcuts[ShortcutActionId.Undo] = "Primary+U";
        using var app = new HeadlessAppFixture(services => services.AddSingleton(settings));
        var box = MoveSlotZeroToSlotOne(app);
        var nickname = app.FindByName<TextBox>("NicknameField")!;
        app.Focus(nickname);

        app.PressKey(PhysicalKey.U, RawInputModifiers.Control);

        Assert.Equal(25, box.Slots[0].Species);
        Assert.Equal(0, box.Slots[1].Species);

        // Ctrl+Y is still the text box's own redo gesture, so it stays with the text box.
        app.PressKey(PhysicalKey.Y, RawInputModifiers.Control);
        Assert.Equal(0, box.Slots[1].Species);
    }

    [AvaloniaFact]
    public void PrimaryZ_UndoesSlotChanges_FromAReadOnlyTextBox()
    {
        using var app = new HeadlessAppFixture();
        var box = MoveSlotZeroToSlotOne(app);
        var nickname = app.FindByName<TextBox>("NicknameField")!;
        nickname.IsReadOnly = true;
        app.Focus(nickname);

        // A read-only text box has no text to undo, so the application Undo runs.
        app.PressKey(PhysicalKey.Z, RawInputModifiers.Control);

        Assert.Equal(25, box.Slots[0].Species);
        Assert.Equal(0, box.Slots[1].Species);
    }

    // ---------------------------------------------------------------------
    // Alt chords: the access-key handler must see the chord, or releasing Alt opens the menu bar.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void AltChord_RunsWithoutOpeningTheMenuBar()
    {
        var settings = new AppSettings();
        settings.KeyboardShortcuts[ShortcutActionId.WorkspaceSave] = "Alt+F5";
        settings.KeyboardShortcuts[ShortcutActionId.WorkspacePokemon] = "Alt+F6";
        using var app = new HeadlessAppFixture(services => services.AddSingleton(settings));
        app.LoadSaveInstance(new SAV6XY());
        var menu = app.Find<Menu>();
        Assert.NotNull(menu);

        // Control case: Alt pressed and released alone opens the menu bar, so the check below means something.
        app.Focus(app.Find<BoxViewer>()!);
        app.Window.KeyPressQwerty(PhysicalKey.AltLeft, RawInputModifiers.Alt);
        app.Window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.None);
        app.Pump();
        Assert.True(menu!.IsOpen);
        menu.Close();
        app.Pump();
        Assert.False(menu.IsOpen);

        app.Focus(app.Find<BoxViewer>()!);
        Assert.Equal(MainWorkspace.Pokemon, app.ViewModel.ActiveWorkspace);
        app.Window.KeyPressQwerty(PhysicalKey.AltLeft, RawInputModifiers.Alt);
        app.Window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Alt);
        app.Window.KeyReleaseQwerty(PhysicalKey.F5, RawInputModifiers.Alt);
        app.Window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.None);
        app.Pump();

        Assert.Equal(MainWorkspace.Save, app.ViewModel.ActiveWorkspace);
        Assert.False(menu.IsOpen);

        // The same with no focused element, when the key is raised on the window itself.
        Assert.Null(app.Window.FocusManager!.GetFocusedElement());
        app.Window.KeyPressQwerty(PhysicalKey.AltLeft, RawInputModifiers.Alt);
        app.Window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Alt);
        app.Window.KeyReleaseQwerty(PhysicalKey.F6, RawInputModifiers.Alt);
        app.Window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.None);
        app.Pump();

        Assert.Equal(MainWorkspace.Pokemon, app.ViewModel.ActiveWorkspace);
        Assert.False(menu.IsOpen);
    }

    [AvaloniaFact]
    public void Shortcut_RunsWithoutAFocusedElement()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        app.Focus(app.Find<BoxViewer>()!);
        app.PressKey(PhysicalKey.Digit2, RawInputModifiers.Control);
        Assert.Equal(MainWorkspace.Save, app.ViewModel.ActiveWorkspace);

        // The workspace switch removed the focused control: the key now comes from the window itself.
        Assert.Null(app.Window.FocusManager!.GetFocusedElement());
        app.PressKey(PhysicalKey.K, RawInputModifiers.Control);
        Assert.True(app.ViewModel.IsToolLauncherOpen);
    }

    [AvaloniaFact]
    public void Shortcut_RunsBeforeTheFocusedControlsOwnKeyHandlers()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        var box = app.Find<BoxViewer>()!;
        var seen = 0;
        void Take(object? sender, KeyEventArgs e)
        {
            seen++;
            e.Handled = true;
        }
        box.AddHandler(InputElement.KeyDownEvent, Take, RoutingStrategies.Tunnel);
        box.AddHandler(InputElement.KeyDownEvent, Take, RoutingStrategies.Bubble);
        app.Focus(box);

        // As a window key binding did, the shortcut handles the key before the focused control sees it.
        app.PressKey(PhysicalKey.N, RawInputModifiers.Control);
        Assert.IsType<EncounterDatabaseViewModel>(Assert.Single(app.Windows.ShownTools).ViewModel);
        Assert.Equal(0, seen);

        // A key without a shortcut still reaches the control.
        app.PressKey(PhysicalKey.J, RawInputModifiers.Control);
        Assert.Equal(1, seen);
    }

    // ---------------------------------------------------------------------
    // 9g. Saving in the shortcut editor rebinds the main window live. The test window service
    //     returns from ShowDialogAsync at once, so the rebind must come from the service's event,
    //     not from code that runs after the dialog closes.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public async Task EditorSave_RebindsTheMainWindowLive()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        var service = app.Services.GetRequiredService<KeyboardShortcutService>();
        var store = Assert.IsType<FakeSettingsStore>(app.Services.GetRequiredService<ISettingsStore>());

        await app.ViewModel.OpenKeyboardShortcutsCommand.ExecuteAsync(null);
        var (viewModel, title) = app.Windows.ShownDialogs.Last();
        var editor = Assert.IsType<KeyboardShortcutsViewModel>(viewModel);
        Assert.Equal(LocalizedStrings.Instance["Dialog_KeyboardShortcuts"], title);
        Assert.True(service.IsEditorOpen);

        var encounter = Assert.Single(editor.Rows, row => row.ActionId == "OpenEncounterDatabase");
        encounter.RecordCommand.Execute(new ShortcutChord(ShortcutModifiers.Control | ShortcutModifiers.Shift, "N"));
        editor.SaveCommand.Execute(null);
        app.Pump();

        Assert.Equal("Primary+Shift+N", store.Saved!.KeyboardShortcuts["OpenEncounterDatabase"]);
        Assert.False(service.IsEditorOpen);

        app.Focus(app.Find<BoxViewer>()!);
        app.PressKey(PhysicalKey.N, RawInputModifiers.Control);
        Assert.Empty(app.Windows.ShownTools);
        app.PressKey(PhysicalKey.N, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.IsType<EncounterDatabaseViewModel>(Assert.Single(app.Windows.ShownTools).ViewModel);

        AssertBindingsMatchViewModel(app);

        // The menus follow too, and the Help entry that opened the editor is available.
        Assert.Equal(new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift), OpenEncounterMenuItem(app).InputGesture);
        Assert.True(OpenMenuItem(app, "Menu_Help", "Menu_Help_KeyboardShortcuts").IsEffectivelyEnabled);
    }

    // ---------------------------------------------------------------------
    // 9h. Menus and the launcher show the resolved shortcuts and follow a rebind.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void Menus_ShowTheResolvedShortcuts_AndFollowARebind()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());

        Assert.Equal(new KeyGesture(Key.N, KeyModifiers.Control), OpenEncounterMenuItem(app).InputGesture);
        Assert.Equal(new KeyGesture(Key.O, KeyModifiers.Control), OpenMenuItem(app, "Menu_File", "Menu_File_Open").InputGesture);
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift), OpenMenuItem(app, "Menu_File", "Menu_File_SaveAs").InputGesture);
        Assert.Null(OpenMenuItem(app, "Menu_File", "Menu_File_Close").InputGesture);
        Assert.Equal(new KeyGesture(Key.Z, KeyModifiers.Control), OpenMenuItem(app, "Menu_Edit", "Menu_Options_Undo").InputGesture);
        Assert.Equal(new KeyGesture(Key.Y, KeyModifiers.Control), OpenMenuItem(app, "Menu_Edit", "Menu_Options_Redo").InputGesture);
        Assert.Equal(new KeyGesture(Key.D3, KeyModifiers.Control), OpenMenuItem(app, "Menu_Window", "Workspace_Reports").InputGesture);
        Assert.Null(OpenMenuItem(app, "Menu_Window", "Tab_Trainer").InputGesture);

        var service = app.Services.GetRequiredService<KeyboardShortcutService>();
        Assert.True(service.Apply(app.ViewModel.Shortcuts.Definitions, new Dictionary<string, ShortcutChord?>
        {
            ["OpenEncounterDatabase"] = new ShortcutChord(ShortcutModifiers.Control | ShortcutModifiers.Shift, "N"),
            [ShortcutActionId.OpenFile] = null,
            [ShortcutActionId.TabTrainer] = new ShortcutChord(ShortcutModifiers.Control, "T"),
        }));
        app.Pump();

        Assert.Equal(new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift), OpenEncounterMenuItem(app).InputGesture);
        Assert.Null(OpenMenuItem(app, "Menu_File", "Menu_File_Open").InputGesture);
        Assert.Equal(new KeyGesture(Key.T, KeyModifiers.Control), OpenMenuItem(app, "Menu_Window", "Tab_Trainer").InputGesture);
    }

    [AvaloniaFact]
    public void Launcher_ShowsEachToolsShortcut()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        app.ViewModel.OpenToolLauncherCommand.Execute(null);
        app.Pump();

        var encounterTitle = LocalizedStrings.Instance["Menu_Data_EncounterDatabase"];
        var item = app.Window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("launcher-item") && AutomationProperties.GetName(button) == encounterTitle);
        Assert.Equal("Ctrl+N", AutomationProperties.GetAcceleratorKey(item));
        var hint = item.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("launcher-item-shortcut"));
        Assert.Equal("Ctrl+N", hint.Text);
        Assert.True(hint.IsEffectivelyVisible);

        // A tool without a shortcut shows no hint.
        var unassigned = app.Window.GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("launcher-item") && string.IsNullOrEmpty(AutomationProperties.GetAcceleratorKey(button)));
        Assert.False(unassigned.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Classes.Contains("launcher-item-shortcut")).IsVisible);
    }

    // ---------------------------------------------------------------------
    // Binder: the configurable bindings on the window follow the view model and never touch
    // bindings the binder did not create.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void Binder_OwnsOnlyItsBindings_RebindsLive_AndReleasesThem()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        var window = app.Window;
        var escape = Assert.Single(window.KeyBindings, binding => new KeyGesture(Key.Escape).Equals(binding.Gesture));

        AssertBindingsMatchViewModel(app);
        var owned = window.ShortcutBinder!.OwnedBindings;
        Assert.Contains(owned, binding => new KeyGesture(Key.D1, KeyModifiers.Control).Equals(binding.Gesture));
        Assert.DoesNotContain(owned, binding => new KeyGesture(Key.Cancel, KeyModifiers.Control).Equals(binding.Gesture));

        var service = app.Services.GetRequiredService<KeyboardShortcutService>();
        service.Apply(app.ViewModel.Shortcuts.Definitions, new Dictionary<string, ShortcutChord?>
        {
            ["OpenEncounterDatabase"] = new ShortcutChord(ShortcutModifiers.Control | ShortcutModifiers.Shift, "N"),
        });
        app.Pump();

        AssertBindingsMatchViewModel(app);
        Assert.Same(escape, Assert.Single(window.KeyBindings));
        app.Focus(app.Find<BoxViewer>()!);
        app.PressKey(PhysicalKey.N, RawInputModifiers.Control);
        Assert.Empty(app.Windows.ShownTools);
        app.PressKey(PhysicalKey.N, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.IsType<EncounterDatabaseViewModel>(Assert.Single(app.Windows.ShownTools).ViewModel);

        window.DataContext = null;
        Assert.Null(window.ShortcutBinder);
        Assert.Same(escape, Assert.Single(window.KeyBindings));
        // The released binder no longer handles keys.
        app.PressKey(PhysicalKey.K, RawInputModifiers.Control);
        Assert.False(app.ViewModel.IsToolLauncherOpen);

        window.DataContext = app.ViewModel;
        AssertBindingsMatchViewModel(app);
        app.Focus(app.Find<BoxViewer>()!);
        app.PressKey(PhysicalKey.K, RawInputModifiers.Control);
        Assert.True(app.ViewModel.IsToolLauncherOpen);
    }

    // ---------------------------------------------------------------------
    // 9i. A shortcut saved through the real settings store applies after a restart.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void SavedShortcut_AppliesAfterRestart()
    {
        var paths = new FakeAppPaths();
        try
        {
            using (var first = new HeadlessAppFixture(services => UseRealSettingsStore(services, paths)))
            {
                var service = first.Services.GetRequiredService<KeyboardShortcutService>();
                Assert.True(service.Apply(first.ViewModel.Shortcuts.Definitions, new Dictionary<string, ShortcutChord?>
                {
                    ["OpenEncounterDatabase"] = new ShortcutChord(ShortcutModifiers.Control | ShortcutModifiers.Shift, "N"),
                }));
            }

            Assert.Equal("Primary+Shift+N", new SettingsStore(paths).Load().KeyboardShortcuts["OpenEncounterDatabase"]);

            using var second = new HeadlessAppFixture(services => UseRealSettingsStore(services, paths));
            second.LoadSaveInstance(new SAV6XY());
            second.Focus(second.Find<BoxViewer>()!);
            second.PressKey(PhysicalKey.N, RawInputModifiers.Control);
            Assert.Empty(second.Windows.ShownTools);
            second.PressKey(PhysicalKey.N, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.IsType<EncounterDatabaseViewModel>(Assert.Single(second.Windows.ShownTools).ViewModel);
        }
        finally
        {
            var root = Directory.GetParent(paths.ConfigDirectory)!.FullName;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // 9j. No shortcut runs while the shortcut editor is open.
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void NoShortcutRuns_WhileTheShortcutEditorIsOpen()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(new SAV6XY());
        app.Focus(app.Find<BoxViewer>()!);
        var service = app.Services.GetRequiredService<KeyboardShortcutService>();

        service.IsEditorOpen = true;
        try
        {
            app.PressKey(PhysicalKey.K, RawInputModifiers.Control);
            app.PressKey(PhysicalKey.N, RawInputModifiers.Control);
            app.PressKey(PhysicalKey.Digit2, RawInputModifiers.Control);

            Assert.False(app.ViewModel.IsToolLauncherOpen);
            Assert.Empty(app.Windows.ShownTools);
            Assert.Equal(MainWorkspace.Pokemon, app.ViewModel.ActiveWorkspace);
        }
        finally
        {
            service.IsEditorOpen = false;
        }

        app.PressKey(PhysicalKey.K, RawInputModifiers.Control);
        Assert.True(app.ViewModel.IsToolLauncherOpen);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static void AssertBindingsMatchViewModel(HeadlessAppFixture app)
    {
        var window = app.Window;
        var binder = window.ShortcutBinder;
        Assert.NotNull(binder);
        var expected = app.ViewModel.ShortcutBindings
            .Select(entry => new KeyGesture(Enum.Parse<Key>(entry.Chord.Key), (KeyModifiers)(int)entry.Chord.Modifiers))
            .ToArray();
        var owned = binder!.OwnedBindings.Select(binding => binding.Gesture).ToArray();

        Assert.Equal(expected, owned);
        Assert.Equal(owned.Length, owned.Distinct().Count());
        // The shortcuts are matched by the binder's tunnelling handler, not as window key bindings,
        // which would hide Alt chords from the access-key handler. Only the fixed Escape is a window binding.
        Assert.DoesNotContain(window.KeyBindings, binding => binder.OwnedBindings.Contains(binding));
        Assert.Single(window.KeyBindings, binding => new KeyGesture(Key.Escape).Equals(binding.Gesture));
    }

    private static BoxViewerViewModel MoveSlotZeroToSlotOne(HeadlessAppFixture app)
    {
        var save = new SAV6XY();
        save.SetBoxSlotAtIndex(new PK6 { Species = 25 }, 0, 0);
        app.LoadSaveInstance(save);
        var box = app.BoxViewer!;
        box.RequestMoveCommand.Execute((box.CreateDragData(0), box.Slots[1], false));
        app.Pump();
        Assert.Equal(25, box.Slots[1].Species);
        Assert.True(app.ViewModel.UndoCommand.CanExecute(null));
        return box;
    }

    private static KeyBinding FindOwnedBinding(HeadlessAppFixture app, KeyGesture gesture) =>
        Assert.Single(app.Window.ShortcutBinder!.OwnedBindings, binding => gesture.Equals(binding.Gesture));

    private static MenuItem OpenMenuItem(HeadlessAppFixture app, string topLevelKey, string itemKey)
    {
        var topLevel = OpenSubmenu(app, app.Window.GetVisualDescendants().OfType<MenuItem>(), topLevelKey);
        return topLevel.Items.OfType<MenuItem>().Single(item => Equals(item.Header, LocalizedStrings.Instance[itemKey]));
    }

    private static MenuItem OpenEncounterMenuItem(HeadlessAppFixture app)
    {
        OpenSubmenu(app, app.Window.GetVisualDescendants().OfType<MenuItem>(), "Menu_Tools");
        OpenSubmenu(app, app.Window.GetVisualDescendants().OfType<MenuItem>(), "Menu_Tools_Data");
        var title = LocalizedStrings.Instance["Menu_Data_EncounterDatabase"];
        return app.Window.GetVisualDescendants().OfType<MenuItem>().Single(item => Equals(item.Header, title));
    }

    private static MenuItem OpenSubmenu(HeadlessAppFixture app, IEnumerable<MenuItem> candidates, string headerKey)
    {
        var header = LocalizedStrings.Instance[headerKey];
        var item = candidates.Single(menuItem => Equals(menuItem.Header, header));
        item.IsSubMenuOpen = true;
        app.Pump();
        return item;
    }

    private static void UseRealSettingsStore(IServiceCollection services, FakeAppPaths paths)
    {
        var store = new SettingsStore(paths);
        services.AddSingleton<ISettingsStore>(store);
        services.AddSingleton(store.Load());
    }
}
