using PKHeX.Application.Abstractions;

namespace PKHeX.Application.Shortcuts;

/// <summary>Where a resolved shortcut's effective chord came from.</summary>
public enum ShortcutSource
{
    /// <summary>The action has no effective shortcut.</summary>
    None,

    /// <summary>The catalog default for the current platform.</summary>
    Default,

    /// <summary>A valid override stored in settings.</summary>
    User,
}

/// <summary>Why a stored override or a default could not be used as-is.</summary>
public enum ShortcutIssueKind
{
    /// <summary>The stored text does not parse; the default stays in effect.</summary>
    InvalidStored,

    /// <summary>The stored chord parses but breaks a <see cref="ShortcutPolicy"/> rule; the default stays in effect.</summary>
    UnusableStored,

    /// <summary>The default is inactive because a stored override of another action uses the same chord.</summary>
    SuppressedDefault,

    /// <summary>Several actions claim the same chord; none of them gets it.</summary>
    DuplicateStored,
}

/// <summary>A problem found while resolving one action. <see cref="Raw"/> is the stored text, when there is one.</summary>
public sealed record ShortcutIssue(ShortcutIssueKind Kind, string? Raw, string? OtherActionId);

/// <summary>The resolved state of one action.</summary>
/// <param name="Id">Action ID.</param>
/// <param name="Effective">Concrete chord to bind, or <see langword="null"/> when the action has none.</param>
/// <param name="Default">Concrete platform default, or <see langword="null"/> when the action has none.</param>
/// <param name="Source">Where <paramref name="Effective"/> came from.</param>
/// <param name="Issue">A problem to show the user, if any.</param>
public sealed record ResolvedShortcut(
    string Id,
    ShortcutChord? Effective,
    ShortcutChord? Default,
    ShortcutSource Source,
    ShortcutIssue? Issue);

/// <summary>Resolved shortcuts keyed by action ID, in definition order.</summary>
public sealed class ShortcutTable
{
    private readonly Dictionary<string, ResolvedShortcut> _byId;

    public ShortcutTable(IReadOnlyList<ResolvedShortcut> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Ordered = entries;
        _byId = new Dictionary<string, ResolvedShortcut>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!_byId.TryAdd(entry.Id, entry))
                throw new ArgumentException($"Duplicate shortcut action ID '{entry.Id}'.", nameof(entries));
        }
    }

    /// <summary>An empty table.</summary>
    public static ShortcutTable Empty { get; } = new([]);

    /// <summary>Every resolved action, in definition order.</summary>
    public IReadOnlyList<ResolvedShortcut> Ordered { get; }

    /// <summary>Resolved actions keyed by action ID.</summary>
    public IReadOnlyDictionary<string, ResolvedShortcut> Entries => _byId;

    public int Count => Ordered.Count;

    public ResolvedShortcut this[string id] => _byId[id];

    public bool TryGet(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ResolvedShortcut? shortcut) =>
        _byId.TryGetValue(id, out shortcut);

    /// <summary>The effective chord of <paramref name="id"/>, or <see langword="null"/> for an unknown or unassigned action.</summary>
    public ShortcutChord? EffectiveFor(string id) => _byId.TryGetValue(id, out var entry) ? entry.Effective : null;
}

/// <summary>
/// Pure resolution of the stored overrides against the catalog. Never mutates the overrides: a
/// stored value that cannot be used is reported as an <see cref="ShortcutIssue"/> and left in place,
/// so a newer build (or a fixed entry) can still read it.
/// </summary>
public static class ShortcutResolver
{
    /// <summary>
    /// Resolves every definition. Per action: a missing or JSON-null override uses the default; an
    /// empty override means the user removed the shortcut; an override that does not parse or breaks
    /// a policy rule keeps the default and reports an issue; otherwise the override wins. Then, per
    /// chord: one user override wins over defaults that share it; two or more user overrides on the
    /// same chord are all inactive; defaults that collide with each other are all inactive.
    /// </summary>
    public static ShortcutTable Resolve(
        IReadOnlyList<ShortcutDefinition> definitions,
        IReadOnlyDictionary<string, string>? overrides,
        IShortcutPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(platform);

        var flavor = platform.Flavor;
        var resolved = new ResolvedShortcut[definitions.Count];
        var raws = new string?[definitions.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (!seen.Add(definition.Id))
                throw new ArgumentException($"Duplicate shortcut action ID '{definition.Id}'.", nameof(definitions));

            var fallback = definition.ResolveDefault(flavor);
            var fallbackSource = fallback is null ? ShortcutSource.None : ShortcutSource.Default;

            string? raw = null;
            if (overrides is not null && overrides.TryGetValue(definition.Id, out var stored))
                raw = stored;
            raws[i] = raw;

            if (raw is null)
            {
                resolved[i] = new ResolvedShortcut(definition.Id, fallback, fallback, fallbackSource, null);
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                resolved[i] = new ResolvedShortcut(definition.Id, null, fallback, ShortcutSource.None, null);
                continue;
            }

            if (!ShortcutChord.TryParse(raw, out var parsed))
            {
                resolved[i] = new ResolvedShortcut(definition.Id, fallback, fallback, fallbackSource,
                    new ShortcutIssue(ShortcutIssueKind.InvalidStored, raw, null));
                continue;
            }

            var concrete = parsed.Resolve(flavor);
            if (ShortcutPolicy.CheckUsable(definition, concrete, platform).Kind != ShortcutValidationKind.Ok)
            {
                resolved[i] = new ResolvedShortcut(definition.Id, fallback, fallback, fallbackSource,
                    new ShortcutIssue(ShortcutIssueKind.UnusableStored, raw, null));
                continue;
            }

            resolved[i] = new ResolvedShortcut(definition.Id, concrete, fallback, ShortcutSource.User, null);
        }

        ResolveCollisions(resolved, raws);
        return new ShortcutTable(resolved);
    }

    private static void ResolveCollisions(ResolvedShortcut[] resolved, string?[] raws)
    {
        var groups = new Dictionary<ShortcutChord, List<int>>();
        for (var i = 0; i < resolved.Length; i++)
        {
            if (resolved[i].Effective is not { } chord)
                continue;
            if (!groups.TryGetValue(chord, out var members))
                groups[chord] = members = [];
            members.Add(i);
        }

        foreach (var members in groups.Values)
        {
            if (members.Count < 2)
                continue;

            var users = members.Where(i => resolved[i].Source == ShortcutSource.User).ToList();
            var defaults = members.Where(i => resolved[i].Source != ShortcutSource.User).ToList();

            if (users.Count == 1)
            {
                var winner = resolved[users[0]].Id;
                foreach (var index in defaults)
                    Deactivate(resolved, index, new ShortcutIssue(ShortcutIssueKind.SuppressedDefault, null, winner));
                continue;
            }

            if (users.Count >= 2)
            {
                foreach (var index in users)
                    Deactivate(resolved, index, new ShortcutIssue(ShortcutIssueKind.DuplicateStored, raws[index], OtherThan(resolved, users, index)));
                var firstUser = resolved[users[0]].Id;
                foreach (var index in defaults)
                    Deactivate(resolved, index, new ShortcutIssue(ShortcutIssueKind.SuppressedDefault, null, firstUser));
                continue;
            }

            // Defaults only: a catalog bug (a test guards against it). Nobody gets the chord.
            foreach (var index in defaults)
                Deactivate(resolved, index, new ShortcutIssue(ShortcutIssueKind.DuplicateStored, null, OtherThan(resolved, defaults, index)));
        }
    }

    private static string? OtherThan(ResolvedShortcut[] resolved, List<int> members, int index)
    {
        foreach (var other in members)
        {
            if (other != index)
                return resolved[other].Id;
        }
        return null;
    }

    private static void Deactivate(ResolvedShortcut[] resolved, int index, ShortcutIssue issue) =>
        resolved[index] = resolved[index] with { Effective = null, Source = ShortcutSource.None, Issue = issue };
}
