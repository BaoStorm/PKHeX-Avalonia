using System.Windows.Input;
using PKHeX.Application.Shortcuts;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Every shortcut-capable action of the main window: the shell actions (File, Edit, Window, Help)
/// followed by one action per capability-registry command, in Tools menu order. The named
/// properties are the shell actions the menu XAML binds to.
/// </summary>
/// <remarks>
/// Construction enforces the identity contract: action IDs are unique and valid, and IDs map
/// one-to-one to commands (a shell command with a parameter counts as one command per parameter).
/// A violation is a programming error and throws while the view model is built.
/// </remarks>
public sealed class ShortcutActionCatalog
{
    private readonly Dictionary<string, ShortcutAction> _byId;

    internal ShortcutActionCatalog(IReadOnlyList<ShortcutAction> shellActions, IReadOnlyList<ShortcutAction> registryActions)
    {
        ArgumentNullException.ThrowIfNull(shellActions);
        ArgumentNullException.ThrowIfNull(registryActions);

        var all = shellActions.Concat(registryActions).ToArray();
        _byId = new Dictionary<string, ShortcutAction>(all.Length, StringComparer.Ordinal);
        var commands = new Dictionary<(ICommand Command, object? Parameter), string>();
        foreach (var action in all)
        {
            if (!ShortcutActionId.IsValid(action.Id))
                throw new InvalidOperationException($"Shortcut action ID '{action.Id}' is not valid.");
            if (!_byId.TryAdd(action.Id, action))
                throw new InvalidOperationException($"Shortcut action ID '{action.Id}' is used twice.");
            if (!commands.TryAdd((action.Command, action.CommandParameter), action.Id))
            {
                throw new InvalidOperationException(
                    $"Shortcut actions '{commands[(action.Command, action.CommandParameter)]}' and '{action.Id}' run the same command.");
            }
        }

        All = all;
        Definitions = all.Select(action => action.Definition).ToArray();

        OpenFile = this[ShortcutActionId.OpenFile];
        SaveFile = this[ShortcutActionId.SaveFile];
        SaveFileAs = this[ShortcutActionId.SaveFileAs];
        CloseFile = this[ShortcutActionId.CloseFile];
        Undo = this[ShortcutActionId.Undo];
        Redo = this[ShortcutActionId.Redo];
        OpenToolLauncher = this[ShortcutActionId.OpenToolLauncher];
        WorkspacePokemon = this[ShortcutActionId.WorkspacePokemon];
        WorkspaceSave = this[ShortcutActionId.WorkspaceSave];
        WorkspaceReports = this[ShortcutActionId.WorkspaceReports];
        TabTrainer = this[ShortcutActionId.TabTrainer];
        TabInventory = this[ShortcutActionId.TabInventory];
        TabEvents = this[ShortcutActionId.TabEvents];
        TabGifts = this[ShortcutActionId.TabGifts];
        TabBatch = this[ShortcutActionId.TabBatch];
        TabBox = this[ShortcutActionId.TabBox];
        TabParty = this[ShortcutActionId.TabParty];
        OpenSettings = this[ShortcutActionId.OpenSettings];
        OpenAbout = this[ShortcutActionId.OpenAbout];
        OpenKeyboardShortcuts = this[ShortcutActionId.OpenKeyboardShortcuts];
    }

    public ShortcutAction OpenFile { get; }
    public ShortcutAction SaveFile { get; }
    public ShortcutAction SaveFileAs { get; }
    public ShortcutAction CloseFile { get; }
    public ShortcutAction Undo { get; }
    public ShortcutAction Redo { get; }
    public ShortcutAction OpenToolLauncher { get; }
    public ShortcutAction WorkspacePokemon { get; }
    public ShortcutAction WorkspaceSave { get; }
    public ShortcutAction WorkspaceReports { get; }
    public ShortcutAction TabTrainer { get; }
    public ShortcutAction TabInventory { get; }
    public ShortcutAction TabEvents { get; }
    public ShortcutAction TabGifts { get; }
    public ShortcutAction TabBatch { get; }
    public ShortcutAction TabBox { get; }
    public ShortcutAction TabParty { get; }
    public ShortcutAction OpenSettings { get; }
    public ShortcutAction OpenAbout { get; }
    public ShortcutAction OpenKeyboardShortcuts { get; }

    /// <summary>Every action: File, Edit, Window and Help, then registry actions in Tools menu order.</summary>
    public IReadOnlyList<ShortcutAction> All { get; }

    /// <summary>The definitions of <see cref="All"/>, in the same order, for resolution and persistence.</summary>
    public IReadOnlyList<ShortcutDefinition> Definitions { get; }

    /// <exception cref="KeyNotFoundException">No action has <paramref name="id"/>.</exception>
    public ShortcutAction this[string id] => _byId[id];

    public bool TryGet(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ShortcutAction? action) =>
        _byId.TryGetValue(id, out action);

    /// <summary>Re-reads every localized title and category after a language change.</summary>
    public void RefreshLocalization()
    {
        foreach (var action in All)
            action.RefreshLocalization();
    }

    /// <summary>
    /// Builds one action per registry command. Descriptors are visited in Tools menu order (the
    /// group order, then registration order inside a group), followed by any descriptor that is in
    /// no group; each action keeps every descriptor that shares its ID.
    /// </summary>
    internal static IReadOnlyList<ShortcutAction> CreateRegistryActions(
        IEnumerable<ToolMenuGroup> menuGroups,
        IEnumerable<ToolLauncherItem> registry)
    {
        ArgumentNullException.ThrowIfNull(menuGroups);
        ArgumentNullException.ThrowIfNull(registry);

        var order = new List<string>();
        var descriptors = new Dictionary<string, List<ToolLauncherItem>>(StringComparer.Ordinal);
        foreach (var item in menuGroups.SelectMany(group => group.Items).Concat(registry))
        {
            if (!descriptors.TryGetValue(item.ActionId, out var list))
            {
                descriptors[item.ActionId] = list = [];
                order.Add(item.ActionId);
            }
            if (!list.Contains(item))
                list.Add(item);
        }

        return order
            .Select(id => ShortcutAction.ForRegistry(ShortcutDefaults.For(id), descriptors[id]))
            .ToArray();
    }
}

/// <summary>
/// Default chords, in portable form (<c>Primary</c> is Ctrl on Windows/Linux and Cmd on macOS).
/// Actions not listed here have no default.
/// </summary>
/// <remarks>
/// The registry defaults follow upstream PKHeX (WinForms <c>Main.Designer.cs</c>) where the action
/// exists here and the chord is free: Ctrl+F stays with the box seek, Ctrl+Shift+S stays Save As, and
/// upstream's Ctrl+U undo is not adopted because Ctrl+Z is the platform convention. The batch editor
/// has no macOS default because Cmd+M minimizes the window there.
/// </remarks>
internal static class ShortcutDefaults
{
    private static readonly Dictionary<string, ShortcutDefinition> Table = new ShortcutDefinition[]
    {
        ShortcutDefinition.Same(ShortcutActionId.OpenFile, "Primary+O"),
        ShortcutDefinition.Same(ShortcutActionId.SaveFile, "Primary+S"),
        ShortcutDefinition.Same(ShortcutActionId.SaveFileAs, "Primary+Shift+S"),
        ShortcutDefinition.Same(ShortcutActionId.OpenToolLauncher, "Primary+K"),
        // D1-D3, never "1": Avalonia's KeyGesture.Parse("Ctrl+1") reads "1" as Key.Cancel.
        ShortcutDefinition.Same(ShortcutActionId.WorkspacePokemon, "Primary+D1"),
        ShortcutDefinition.Same(ShortcutActionId.WorkspaceSave, "Primary+D2"),
        ShortcutDefinition.Same(ShortcutActionId.WorkspaceReports, "Primary+D3"),
        ShortcutDefinition.Same(ShortcutActionId.Undo, "Primary+Z", ShortcutTextRole.Undo),
        new(ShortcutActionId.Redo, "Primary+Y", "Primary+Shift+Z", ShortcutTextRole.Redo),
        ShortcutDefinition.Same(Registry(nameof(MainWindowViewModel.OpenEncounterDatabaseCommand)), "Primary+N"),
        ShortcutDefinition.Same(Registry(nameof(MainWindowViewModel.OpenPKMDatabaseCommand)), "Primary+D"),
        ShortcutDefinition.Same(Registry(nameof(MainWindowViewModel.OpenMysteryGiftDatabaseCommand)), "Primary+G"),
        ShortcutDefinition.Same(Registry(nameof(MainWindowViewModel.OpenBoxReportCommand)), "Primary+R"),
        new(Registry(nameof(MainWindowViewModel.OpenBatchEditorCommand)), "Primary+M", null),
    }.ToDictionary(definition => definition.Id, StringComparer.Ordinal);

    /// <summary>Every action ID that has a default on at least one platform.</summary>
    internal static IReadOnlyCollection<string> Ids => Table.Keys;

    /// <summary>The definition of <paramref name="id"/>: its defaults, or none.</summary>
    internal static ShortcutDefinition For(string id) =>
        Table.TryGetValue(id, out var definition) ? definition : ShortcutDefinition.Same(id, null);

    private static string Registry(string commandPropertyName) => ShortcutActionId.FromExpression(commandPropertyName);
}
