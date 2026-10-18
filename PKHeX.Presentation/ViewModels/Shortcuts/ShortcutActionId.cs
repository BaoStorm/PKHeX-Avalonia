using System.Text.RegularExpressions;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Stable keyboard-shortcut action IDs. They are a persistence contract: they are the keys of
/// <c>AppSettings.KeyboardShortcuts</c>, so renaming one orphans the user's stored shortcut. The
/// golden list <c>Tests/PKHeX.Avalonia.Tests/Shortcuts/shortcut-action-ids.txt</c> pins the set.
/// </summary>
/// <remarks>
/// Capability-registry actions take their ID from the command property name without its
/// <c>Command</c> suffix (<see cref="FromExpression"/>), so every descriptor that shares one command
/// shares one ID. Shell actions that are not in the registry use the constants below; a dotted
/// suffix names the parameter of a shared command (for example <c>SelectWorkspace.Save</c>).
/// </remarks>
public static partial class ShortcutActionId
{
    public const string OpenFile = "OpenFile";
    public const string SaveFile = "SaveFile";
    public const string SaveFileAs = "SaveFileAs";
    public const string CloseFile = "CloseFile";
    public const string Undo = "Undo";
    public const string Redo = "Redo";
    public const string OpenToolLauncher = "OpenToolLauncher";
    public const string WorkspacePokemon = "SelectWorkspace.Pokemon";
    public const string WorkspaceSave = "SelectWorkspace.Save";
    public const string WorkspaceReports = "SelectWorkspace.Reports";
    public const string TabTrainer = "SelectWorkspaceTab.Trainer";
    public const string TabInventory = "SelectWorkspaceTab.Inventory";
    public const string TabEvents = "SelectWorkspaceTab.Events";
    public const string TabGifts = "SelectWorkspaceTab.Gifts";
    public const string TabBatch = "SelectWorkspaceTab.Batch";
    public const string TabBox = "SelectWorkspaceTab.Box";
    public const string TabParty = "SelectWorkspaceTab.Party";
    public const string OpenSettings = "OpenSettings";
    public const string OpenAbout = "OpenAbout";
    public const string OpenKeyboardShortcuts = "OpenKeyboardShortcuts";

    private const string CommandSuffix = "Command";

    /// <summary>
    /// Derives an ID from the source text of a command argument, as captured by
    /// <see cref="System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"/>: the text after
    /// the last <c>.</c>, trimmed, without a trailing <c>Command</c>. For example
    /// <c>OpenEncounterDatabaseCommand</c> and <c>vm.OpenEncounterDatabaseCommand</c> both give
    /// <c>OpenEncounterDatabase</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The expression does not name a command property (for example a parameter called
    /// <c>command</c> that a helper forgot to forward). This fails when the view model is built.
    /// </exception>
    public static string FromExpression(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var name = expression[(expression.LastIndexOf('.') + 1)..].Trim();
        if (name.EndsWith(CommandSuffix, StringComparison.Ordinal))
            name = name[..^CommandSuffix.Length];

        if (!CommandNamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                $"Cannot derive a shortcut action ID from '{expression}'. Pass the command property " +
                "itself, or forward the caller's [CallerArgumentExpression] value.", nameof(expression));
        }

        return name;
    }

    /// <summary>True when <paramref name="id"/> has the form <c>Name</c> or <c>Name.Parameter</c>.</summary>
    public static bool IsValid(string? id) => id is not null && IdPattern().IsMatch(id);

    [GeneratedRegex("^[A-Z][A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CommandNamePattern();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
