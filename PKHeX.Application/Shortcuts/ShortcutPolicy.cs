using PKHeX.Application.Abstractions;

namespace PKHeX.Application.Shortcuts;

/// <summary>Outcome of checking a chord against the shortcut rules.</summary>
public enum ShortcutValidationKind
{
    /// <summary>The chord can be assigned.</summary>
    Ok,

    /// <summary>The action already has exactly this chord.</summary>
    Unchanged,

    /// <summary>The key is outside <see cref="ShortcutKeyVocabulary"/>, or the chord is not concrete.</summary>
    UnsupportedKey,

    /// <summary>A non-function key needs the platform's command modifier.</summary>
    NeedsModifier,

    /// <summary>Ctrl+Alt on Windows/Linux, which AltGr produces when typing characters on many layouts.</summary>
    AltGr,

    /// <summary>The platform, a control or a view already owns the chord; see <see cref="ShortcutValidation.ReservedFor"/>.</summary>
    Reserved,

    /// <summary>Another action has the chord; see <see cref="ShortcutValidation.ConflictingActionId"/>.</summary>
    Conflict,
}

/// <summary>Result of <see cref="ShortcutPolicy.CheckUsable"/> or <see cref="ShortcutPolicy.Validate"/>.</summary>
public sealed record ShortcutValidation(
    ShortcutValidationKind Kind,
    string? ConflictingActionId = null,
    ShortcutReservationKind? ReservedFor = null)
{
    public static ShortcutValidation Ok { get; } = new(ShortcutValidationKind.Ok);
    public static ShortcutValidation Unchanged { get; } = new(ShortcutValidationKind.Unchanged);

    /// <summary>True for <see cref="ShortcutValidationKind.Ok"/> and <see cref="ShortcutValidationKind.Unchanged"/>.</summary>
    public bool IsAccepted => Kind is ShortcutValidationKind.Ok or ShortcutValidationKind.Unchanged;
}

/// <summary>
/// Pure rules that decide whether a concrete chord may be assigned to an action. Shared by the
/// resolver (stored overrides that break a rule are ignored) and the shortcut editor (the user
/// sees the reason).
/// </summary>
public static class ShortcutPolicy
{
    private const ShortcutModifiers ConcreteModifiers =
        ShortcutModifiers.Alt | ShortcutModifiers.Control | ShortcutModifiers.Shift | ShortcutModifiers.Meta;

    /// <summary>
    /// Checks <paramref name="concrete"/> against the rules that do not depend on other actions, in
    /// this order: supported key, Windows/Super key on Windows/Linux, reserved chords, command
    /// modifier required for non-function keys, and AltGr (Ctrl+Alt) on Windows/Linux.
    /// </summary>
    /// <remarks>
    /// Reservations are checked before the command-modifier rule so that a chord a view already owns
    /// (for example the literal Ctrl+F box seek on macOS) reports what owns it instead of asking for
    /// a different modifier.
    /// </remarks>
    public static ShortcutValidation CheckUsable(ShortcutDefinition definition, ShortcutChord concrete, IShortcutPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(platform);

        var modifiers = concrete.Modifiers;
        if (!ShortcutKeyVocabulary.IsKnown(concrete.Key) || (modifiers & ~ConcreteModifiers) != 0)
            return new ShortcutValidation(ShortcutValidationKind.UnsupportedKey);

        var standard = platform.Flavor == ShortcutPlatformFlavor.Standard;
        if (standard && (modifiers & ShortcutModifiers.Meta) != 0)
            return new ShortcutValidation(ShortcutValidationKind.Reserved, ReservedFor: ShortcutReservationKind.System);

        foreach (var reservation in platform.Reservations)
        {
            if (reservation.Chord != concrete)
                continue;
            if (reservation.AllowedRole != ShortcutTextRole.None && reservation.AllowedRole == definition.TextRole)
                continue;
            return new ShortcutValidation(ShortcutValidationKind.Reserved, ReservedFor: reservation.Kind);
        }

        var command = ShortcutChord.CommandModifier(platform.Flavor);
        if (!concrete.IsFunctionKey && (modifiers & command) == 0)
            return new ShortcutValidation(ShortcutValidationKind.NeedsModifier);

        const ShortcutModifiers altGr = ShortcutModifiers.Control | ShortcutModifiers.Alt;
        if (standard && (modifiers & altGr) == altGr)
            return new ShortcutValidation(ShortcutValidationKind.AltGr);

        return ShortcutValidation.Ok;
    }

    /// <summary>
    /// Runs <see cref="CheckUsable"/>, then compares <paramref name="concrete"/> with the working
    /// assignment <paramref name="draft"/> (action ID to concrete chord): the same chord on this
    /// action is <see cref="ShortcutValidationKind.Unchanged"/>, and the same chord on another action
    /// is a <see cref="ShortcutValidationKind.Conflict"/>. Unassigned actions never conflict.
    /// </summary>
    public static ShortcutValidation Validate(
        ShortcutDefinition definition,
        ShortcutChord concrete,
        IShortcutPlatform platform,
        IReadOnlyDictionary<string, ShortcutChord?> draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var usable = CheckUsable(definition, concrete, platform);
        if (usable.Kind != ShortcutValidationKind.Ok)
            return usable;

        if (draft.TryGetValue(definition.Id, out var current) && current == concrete)
            return ShortcutValidation.Unchanged;

        foreach (var (actionId, chord) in draft)
        {
            if (chord == concrete && !string.Equals(actionId, definition.Id, StringComparison.Ordinal))
                return new ShortcutValidation(ShortcutValidationKind.Conflict, ConflictingActionId: actionId);
        }

        return ShortcutValidation.Ok;
    }
}
