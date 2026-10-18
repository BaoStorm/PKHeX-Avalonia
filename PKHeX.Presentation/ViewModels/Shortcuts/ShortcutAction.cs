using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Application.Shortcuts;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// One shortcut-capable action: a shell command (File, Edit, Window, Help) or one command of the
/// capability registry, which may back several menu entries (for example the Pokédex in every
/// generation group). Carries the resolved shortcut that menus, the launcher and the editor show.
/// </summary>
public sealed class ShortcutAction : ObservableObject
{
    private readonly Func<bool> _canInvoke;
    private ShortcutChord? _shortcut;
    private string _shortcutText = string.Empty;
    private ShortcutIssue? _issue;

    private ShortcutAction(
        ShortcutDefinition definition,
        IReadOnlyList<string> titleKeys,
        IReadOnlyList<string> categoryKeys,
        ICommand command,
        object? commandParameter,
        IReadOnlyList<ToolLauncherItem> descriptors,
        Func<bool> canInvoke)
    {
        Definition = definition;
        TitleKey = titleKeys[0];
        TitleKeys = titleKeys;
        CategoryKeys = categoryKeys;
        Command = command;
        CommandParameter = commandParameter;
        Descriptors = descriptors;
        _canInvoke = canInvoke;
    }

    /// <summary>Stable action ID; the key of the stored override.</summary>
    public string Id => Definition.Id;

    /// <summary>Localization key of the title (a menu key, so it may carry an access-key marker).</summary>
    public string TitleKey { get; }

    /// <summary>
    /// Localization keys of every menu entry name the action has, <see cref="TitleKey"/> first. A
    /// registry command can appear under different names (Box Layout in Save Editors, Block Layout
    /// in Gen 4).
    /// </summary>
    public IReadOnlyList<string> TitleKeys { get; }

    /// <summary>Localization keys of the menus the action appears in.</summary>
    public IReadOnlyList<string> CategoryKeys { get; }

    /// <summary>The catalog definition: ID, platform defaults and text role.</summary>
    public ShortcutDefinition Definition { get; }

    /// <summary>Localized title without the access-key marker (de <c>"Ö_ffnen..."</c> shows <c>"Öffnen..."</c>).</summary>
    public string Title => AccessKeyText.Strip(LocalizedStrings.Instance[TitleKey]);

    /// <summary>Every localized name of the action without access-key markers, <see cref="Title"/> first.</summary>
    public IReadOnlyList<string> Titles => TitleKeys
        .Select(key => AccessKeyText.Strip(LocalizedStrings.Instance[key]))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Localized menu names the action appears in, comma-separated.</summary>
    public string CategoryTitle => string.Join(", ", CategoryKeys
        .Select(key => AccessKeyText.Strip(LocalizedStrings.Instance[key]))
        .Distinct(StringComparer.Ordinal));

    /// <summary>Effective concrete chord, or <see langword="null"/> when the action has none.</summary>
    public ShortcutChord? Shortcut => _shortcut;

    /// <summary>Platform display text of <see cref="Shortcut"/>, or an empty string.</summary>
    public string ShortcutText => _shortcutText;

    public bool HasShortcut => _shortcut is not null;

    /// <summary>A problem with the stored override or the default, if resolution found one.</summary>
    public ShortcutIssue? Issue => _issue;

    /// <summary>The command the shortcut runs.</summary>
    internal ICommand Command { get; }

    /// <summary>Parameter passed to <see cref="Command"/>; <see langword="null"/> for registry actions.</summary>
    internal object? CommandParameter { get; }

    /// <summary>Registry descriptors that share this action; empty for shell actions.</summary>
    internal IReadOnlyList<ToolLauncherItem> Descriptors { get; }

    /// <summary>
    /// Live availability, evaluated when a key is pressed: the command's <c>CanExecute</c> plus the
    /// save-type predicate, never a cached flag.
    /// </summary>
    internal bool CanInvoke() => _canInvoke();

    /// <summary>Runs the command when <see cref="CanInvoke"/> is true.</summary>
    /// <returns>True when the command ran.</returns>
    internal bool Invoke()
    {
        if (!CanInvoke())
            return false;

        Command.Execute(CommandParameter);
        return true;
    }

    /// <summary>A shell action: one command and parameter, optionally gated by <paramref name="isApplicable"/>.</summary>
    internal static ShortcutAction ForShell(
        ShortcutDefinition definition,
        string titleKey,
        string categoryKey,
        ICommand command,
        object? commandParameter = null,
        Func<bool>? isApplicable = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new ShortcutAction(definition, [titleKey], [categoryKey], command, commandParameter, [],
            () => command.CanExecute(commandParameter) && (isApplicable?.Invoke() ?? true));
    }

    /// <summary>
    /// A registry action over every descriptor that shares one command. It can run when any
    /// descriptor can (the Pokédex is reachable from whichever generation group matches the save);
    /// the title comes from the first descriptor, and the other names and the categories from all of them.
    /// </summary>
    internal static ShortcutAction ForRegistry(ShortcutDefinition definition, IReadOnlyList<ToolLauncherItem> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        if (descriptors.Count == 0)
            throw new ArgumentException("A registry action needs at least one descriptor.", nameof(descriptors));

        var command = descriptors[0].Command;
        foreach (var descriptor in descriptors)
        {
            if (!string.Equals(descriptor.ActionId, definition.Id, StringComparison.Ordinal))
                throw new ArgumentException($"Descriptor '{descriptor.ActionId}' does not belong to action '{definition.Id}'.", nameof(descriptors));
            if (!ReferenceEquals(descriptor.Command, command))
                throw new ArgumentException($"Action '{definition.Id}' is registered with more than one command.", nameof(descriptors));
        }

        var categories = descriptors
            .Select(descriptor => descriptor.MenuGroupTitleKey)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var titleKeys = descriptors
            .Select(descriptor => descriptor.TitleKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ShortcutAction(definition, titleKeys, categories, command, null, descriptors,
            () => descriptors.Any(descriptor => descriptor.CanInvoke()));
    }

    /// <summary>Publishes the resolved shortcut; raises change notifications only for real changes.</summary>
    internal void SetResolved(ResolvedShortcut resolved, string shortcutText)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(shortcutText);

        var chordChanged = _shortcut != resolved.Effective;
        var textChanged = !string.Equals(_shortcutText, shortcutText, StringComparison.Ordinal);
        var issueChanged = _issue != resolved.Issue;

        _shortcut = resolved.Effective;
        _shortcutText = shortcutText;
        _issue = resolved.Issue;

        if (chordChanged)
        {
            OnPropertyChanged(nameof(Shortcut));
            OnPropertyChanged(nameof(HasShortcut));
        }
        if (textChanged)
            OnPropertyChanged(nameof(ShortcutText));
        if (issueChanged)
            OnPropertyChanged(nameof(Issue));
    }

    /// <summary>Re-reads the localized title and categories after a language change.</summary>
    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Titles));
        OnPropertyChanged(nameof(CategoryTitle));
    }
}
