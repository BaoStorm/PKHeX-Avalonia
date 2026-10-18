using PKHeX.Application.Abstractions;
using PKHeX.Application.Shortcuts;

namespace PKHeX.Application.Services;

/// <summary>
/// Owns the user's keyboard shortcut overrides in <see cref="AppSettings.KeyboardShortcuts"/>:
/// resolves them against a shortcut catalog and persists edits. Framework-free; the presentation
/// layer supplies the catalog definitions and the host binds the resolved chords.
/// </summary>
/// <remarks>
/// Only overrides are stored, in portable form (<c>Primary</c> for the platform's command modifier),
/// so a settings file moves between Windows/Linux and macOS without rewriting. Entries for action IDs
/// the given catalog does not know are never touched, so a newer build's overrides survive an older
/// build's edits.
/// </remarks>
public sealed class KeyboardShortcutService(AppSettings settings, ISettingsStore store, IShortcutPlatform platform)
{
    private readonly AppSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ISettingsStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The platform conventions used for resolution, validation and display.</summary>
    public IShortcutPlatform Platform { get; } = platform ?? throw new ArgumentNullException(nameof(platform));

    /// <summary>
    /// True while the shortcut editor is open. Hosts check it before running a bound shortcut so a
    /// chord pressed while the editor records keys never triggers an action.
    /// </summary>
    public bool IsEditorOpen { get; set; }

    /// <summary>Raised once after <see cref="Apply"/> changed and saved the stored overrides.</summary>
    public event EventHandler? Changed;

    /// <summary>Resolves the stored overrides against <paramref name="definitions"/>. Never mutates settings.</summary>
    public ShortcutTable Resolve(IReadOnlyList<ShortcutDefinition> definitions) =>
        ShortcutResolver.Resolve(definitions, _settings.KeyboardShortcuts, Platform);

    /// <summary>
    /// Stores <paramref name="changes"/> (action ID to concrete chord, <see langword="null"/> for no
    /// shortcut). A chord equal to the action's default removes the override; removing a default
    /// stores an empty string; any other chord is stored in portable form. IDs that
    /// <paramref name="definitions"/> does not contain are ignored. When anything changed, settings
    /// are saved once and <see cref="Changed"/> is raised once.
    /// </summary>
    /// <returns>True when the stored overrides changed.</returns>
    /// <exception cref="InvalidOperationException">A chord breaks a <see cref="ShortcutPolicy"/> rule.</exception>
    public bool Apply(IReadOnlyList<ShortcutDefinition> definitions, IReadOnlyDictionary<string, ShortcutChord?> changes)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(changes);

        var flavor = Platform.Flavor;
        var known = new Dictionary<string, ShortcutDefinition>(definitions.Count, StringComparer.Ordinal);
        foreach (var definition in definitions)
            known.TryAdd(definition.Id, definition);

        // Validate everything first so a rejected chord leaves the stored overrides untouched.
        var pending = new List<(string Id, string? Value)>(changes.Count);
        foreach (var (id, chord) in changes)
        {
            if (!known.TryGetValue(id, out var definition))
                continue;

            if (chord is { } concrete)
            {
                var check = ShortcutPolicy.CheckUsable(definition, concrete, Platform);
                if (check.Kind != ShortcutValidationKind.Ok)
                {
                    throw new InvalidOperationException(
                        $"Shortcut '{concrete.ToStorageString()}' cannot be assigned to '{id}': {check.Kind}.");
                }
            }

            var fallback = definition.ResolveDefault(flavor);
            string? value;
            if (chord == fallback)
                value = null; // back to the default (or still none): no override needed
            else if (chord is null)
                value = string.Empty; // the user removed a default shortcut
            else
                value = chord.Value.ToPortable(flavor).ToStorageString();
            pending.Add((id, value));
        }

        var map = _settings.KeyboardShortcuts;
        var changed = false;
        foreach (var (id, value) in pending)
        {
            if (value is null)
            {
                changed |= map.Remove(id);
                continue;
            }

            if (map.TryGetValue(id, out var existing) && string.Equals(existing, value, StringComparison.Ordinal))
                continue;

            map[id] = value;
            changed = true;
        }

        if (!changed)
            return false;

        _store.Save(_settings);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
