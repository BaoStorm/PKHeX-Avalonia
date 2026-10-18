using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;

namespace PKHeX.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    private readonly KeyboardShortcutService _shortcutService;

    /// <summary>Every shortcut-capable action with its resolved shortcut; the menus bind to it.</summary>
    public ShortcutActionCatalog Shortcuts { get; private set; }

    /// <summary>
    /// The shortcuts the host binds on the main window: one entry per action that has an effective
    /// chord. Replaced (never mutated) whenever the stored overrides change.
    /// </summary>
    public IReadOnlyList<ShortcutBindingEntry> ShortcutBindings { get; private set; } = [];

    /// <summary>Raised on the UI thread after <see cref="ShortcutBindings"/> was replaced.</summary>
    public event EventHandler? ShortcutBindingsChanged;

    /// <summary>
    /// Opens the shortcut editor (Help menu, the Settings section, or its own shortcut). Saving there
    /// raises <see cref="KeyboardShortcutService.Changed"/>, which rebinds the shortcuts live.
    /// </summary>
    [RelayCommand]
    private async Task OpenKeyboardShortcutsAsync()
    {
        // One editor at a time: it owns KeyboardShortcutService.IsEditorOpen until it closes.
        if (_shortcutService.IsEditorOpen)
            return;

        await _windowService.ShowDialogAsync(
            new KeyboardShortcutsViewModel(Shortcuts, _shortcutService, _dialogService),
            T("Dialog_KeyboardShortcuts"));
    }

    /// <summary>
    /// Builds the action catalog over the shell commands and the capability registry, then resolves
    /// and publishes the shortcuts. Runs once, after <see cref="InitializeToolLauncherItems"/>.
    /// </summary>
    [MemberNotNull(nameof(Shortcuts))]
    internal void InitializeShortcuts()
    {
        if (Shortcuts is not null)
            return;

        Shortcuts = new ShortcutActionCatalog(
            CreateShellShortcutActions(),
            ShortcutActionCatalog.CreateRegistryActions(_toolMenuGroups, _capabilityRegistry));
        ApplyShortcutAssignments();
    }

    private ShortcutAction[] CreateShellShortcutActions() =>
    [
        Shell(ShortcutActionId.OpenFile, "Menu_File_Open", "Menu_File", OpenFileCommand),
        Shell(ShortcutActionId.SaveFile, "Menu_File_Save", "Menu_File", SaveFileCommand),
        Shell(ShortcutActionId.SaveFileAs, "Menu_File_SaveAs", "Menu_File", SaveFileAsCommand),
        Shell(ShortcutActionId.CloseFile, "Menu_File_Close", "Menu_File", CloseFileCommand),

        Shell(ShortcutActionId.Undo, "Menu_Options_Undo", "Menu_Edit", UndoCommand),
        Shell(ShortcutActionId.Redo, "Menu_Options_Redo", "Menu_Edit", RedoCommand),

        Shell(ShortcutActionId.OpenToolLauncher, "Launcher_Title", "Menu_Window", OpenToolLauncherCommand),
        Shell(ShortcutActionId.WorkspacePokemon, "Workspace_Pokemon", "Menu_Window", SelectWorkspaceCommand, MainWorkspace.Pokemon),
        Shell(ShortcutActionId.WorkspaceSave, "Workspace_Save", "Menu_Window", SelectWorkspaceCommand, MainWorkspace.Save),
        Shell(ShortcutActionId.WorkspaceReports, "Workspace_Reports", "Menu_Window", SelectWorkspaceCommand, MainWorkspace.Reports),
        Shell(ShortcutActionId.TabTrainer, "Tab_Trainer", "Menu_Window", SelectWorkspaceTabCommand, TrainerWorkspaceTabIndex),
        Shell(ShortcutActionId.TabInventory, "Tab_Inventory", "Menu_Window", SelectWorkspaceTabCommand, InventoryWorkspaceTabIndex),
        // Events and Gifts follow their Window menu entries, which are hidden for unsupported saves.
        Shell(ShortcutActionId.TabEvents, "Tab_Events", "Menu_Window", SelectWorkspaceTabCommand, EventsWorkspaceTabIndex,
            () => EventFlagsEditor?.IsSupported == true),
        Shell(ShortcutActionId.TabGifts, "Tab_Gifts", "Menu_Window", SelectWorkspaceTabCommand, GiftsWorkspaceTabIndex,
            () => MysteryGiftEditor?.HasAnySupport == true),
        Shell(ShortcutActionId.TabBatch, "Tab_Batch", "Menu_Window", SelectWorkspaceTabCommand, BatchWorkspaceTabIndex),
        Shell(ShortcutActionId.TabBox, "Tab_Box", "Menu_Window", SelectWorkspaceTabCommand, BoxWorkspaceTabIndex),
        Shell(ShortcutActionId.TabParty, "Tab_Party", "Menu_Window", SelectWorkspaceTabCommand, PartyWorkspaceTabIndex),

        Shell(ShortcutActionId.OpenSettings, "Menu_Options_Settings", "Menu_Help", OpenSettingsCommand),
        Shell(ShortcutActionId.OpenAbout, "Menu_Options_About", "Menu_Help", OpenAboutCommand),
        Shell(ShortcutActionId.OpenKeyboardShortcuts, "Menu_Help_KeyboardShortcuts", "Menu_Help", OpenKeyboardShortcutsCommand),
    ];

    private static ShortcutAction Shell(
        string id,
        string titleKey,
        string categoryKey,
        System.Windows.Input.ICommand command,
        object? parameter = null,
        Func<bool>? isApplicable = null) =>
        ShortcutAction.ForShell(ShortcutDefaults.For(id), titleKey, categoryKey, command, parameter, isApplicable);

    /// <summary>
    /// Resolves the stored overrides, publishes each action's shortcut to the catalog and to every
    /// registry descriptor (menus and launcher), and replaces <see cref="ShortcutBindings"/>.
    /// </summary>
    private void ApplyShortcutAssignments()
    {
        var table = _shortcutService.Resolve(Shortcuts.Definitions);
        var platform = _shortcutService.Platform;
        var bindings = new List<ShortcutBindingEntry>();

        foreach (var action in Shortcuts.All)
        {
            var resolved = table[action.Id];
            var text = resolved.Effective is { } effective ? platform.FormatForDisplay(effective) : string.Empty;
            action.SetResolved(resolved, text);
            foreach (var descriptor in action.Descriptors)
                descriptor.SetShortcut(resolved.Effective, text);

            if (resolved.Effective is { } chord)
            {
                var bound = action;
                bindings.Add(new ShortcutBindingEntry(
                    bound.Id,
                    chord,
                    bound.Definition.TextRole,
                    () => !_shortcutService.IsEditorOpen && bound.CanInvoke(),
                    () => InvokeShortcutAction(bound)));
            }
        }

        ShortcutBindings = bindings;
        OnPropertyChanged(nameof(ShortcutBindings));
        ShortcutBindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs a shortcut the way a launcher click does: the launcher closes first unless the shortcut
    /// opens it.
    /// </summary>
    private void InvokeShortcutAction(ShortcutAction action)
    {
        if (_shortcutService.IsEditorOpen || !action.CanInvoke())
            return;

        if (IsToolLauncherOpen && !ReferenceEquals(action, Shortcuts.OpenToolLauncher))
            CloseToolLauncher();
        action.Invoke();
    }

    private void OnShortcutsChanged(object? sender, EventArgs e) => PostToUi(ApplyShortcutAssignments);
}
