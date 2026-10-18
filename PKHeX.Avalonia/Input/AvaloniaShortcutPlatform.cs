using Avalonia.Input;
using PKHeX.Application.Shortcuts;

namespace PKHeX.Avalonia.Input;

/// <summary>
/// Host <see cref="IShortcutPlatform"/>: reads the command modifier and the text gestures from
/// Avalonia's <c>PlatformHotkeyConfiguration</c>, adds the view, control and system gestures from
/// <see cref="ViewScopedShortcuts"/>, and formats chords the way menus show them.
/// </summary>
/// <remarks>
/// The flavor follows the configured command modifier, not the operating system: macOS reports Meta
/// (Command), Windows, X11 and the headless test platform report Control. The configuration is read
/// lazily on first use, after the Avalonia application has started.
/// </remarks>
public sealed class AvaloniaShortcutPlatform : IShortcutPlatform
{
    private readonly Lazy<Snapshot> _snapshot;

    public AvaloniaShortcutPlatform()
    {
        _snapshot = new Lazy<Snapshot>(ReadFromApplication);
    }

    /// <summary>
    /// Test constructor with an explicit command modifier and text gestures, so the macOS convention
    /// can be exercised on the headless platform (which always reports Control).
    /// </summary>
    /// <param name="commandModifiers">The platform command modifier (Control or Meta).</param>
    /// <param name="textGestures">
    /// Platform gestures to reserve. <c>TextEditing</c> true reserves the chord for text editing (with
    /// <c>Role</c> naming the application action that may still use it); false reserves it for the system.
    /// </param>
    internal AvaloniaShortcutPlatform(
        KeyModifiers commandModifiers,
        IReadOnlyList<(KeyGesture Gesture, ShortcutTextRole Role, bool TextEditing)> textGestures)
    {
        ArgumentNullException.ThrowIfNull(textGestures);
        _snapshot = new Lazy<Snapshot>(Build(commandModifiers, textGestures));
    }

    public ShortcutPlatformFlavor Flavor => _snapshot.Value.Flavor;

    public IReadOnlyList<ShortcutReservation> Reservations => _snapshot.Value.Reservations;

    /// <summary>The configured command modifier (Control, or Meta on macOS).</summary>
    internal KeyModifiers CommandModifiers => _snapshot.Value.CommandModifiers;

    public string PrimaryModifierDisplay => new KeyGesture(Key.None, CommandModifiers).ToString("p", null);

    /// <summary>Platform display text, the same text a menu shows for the gesture.</summary>
    public string FormatForDisplay(ShortcutChord concrete) =>
        ShortcutGestureMapper.ToKeyGesture(concrete).ToString("p", null);

    /// <summary>
    /// The text gestures Avalonia's default hotkey configuration uses for
    /// <paramref name="commandModifiers"/> (select all, copy, cut, paste, undo, redo). Used when no
    /// application configuration is available and by tests that simulate another platform.
    /// </summary>
    internal static IReadOnlyList<(KeyGesture Gesture, ShortcutTextRole Role, bool TextEditing)> DefaultTextGestures(
        KeyModifiers commandModifiers) =>
    [
        (new KeyGesture(Key.A, commandModifiers), ShortcutTextRole.None, true),
        (new KeyGesture(Key.C, commandModifiers), ShortcutTextRole.None, true),
        (new KeyGesture(Key.X, commandModifiers), ShortcutTextRole.None, true),
        (new KeyGesture(Key.V, commandModifiers), ShortcutTextRole.None, true),
        (new KeyGesture(Key.Z, commandModifiers), ShortcutTextRole.Undo, true),
        (new KeyGesture(Key.Y, commandModifiers), ShortcutTextRole.Redo, true),
        (new KeyGesture(Key.Z, commandModifiers | KeyModifiers.Shift), ShortcutTextRole.Redo, true),
    ];

    private static Snapshot ReadFromApplication()
    {
        var config = global::Avalonia.Application.Current?.PlatformSettings?.HotkeyConfiguration;
        if (config is null)
            return Build(KeyModifiers.Control, DefaultTextGestures(KeyModifiers.Control));

        var gestures = new List<(KeyGesture Gesture, ShortcutTextRole Role, bool TextEditing)>();
        Add(config.SelectAll, ShortcutTextRole.None, textEditing: true);
        Add(config.Copy, ShortcutTextRole.None, textEditing: true);
        Add(config.Cut, ShortcutTextRole.None, textEditing: true);
        Add(config.Paste, ShortcutTextRole.None, textEditing: true);
        Add(config.Undo, ShortcutTextRole.Undo, textEditing: true);
        Add(config.Redo, ShortcutTextRole.Redo, textEditing: true);
        // Shift+F10 on Windows opens the context menu of the focused control.
        Add(config.OpenContextMenu, ShortcutTextRole.None, textEditing: false);
        return Build(config.CommandModifiers, gestures);

        void Add(IEnumerable<KeyGesture>? source, ShortcutTextRole role, bool textEditing)
        {
            foreach (var gesture in source ?? [])
                gestures.Add((gesture, role, textEditing));
        }
    }

    private static Snapshot Build(
        KeyModifiers commandModifiers,
        IReadOnlyList<(KeyGesture Gesture, ShortcutTextRole Role, bool TextEditing)> textGestures)
    {
        var flavor = (commandModifiers & KeyModifiers.Meta) != 0
            ? ShortcutPlatformFlavor.Apple
            : ShortcutPlatformFlavor.Standard;

        var reservations = new List<ShortcutReservation>();
        foreach (var (gesture, role, textEditing) in textGestures)
        {
            // Only chords a shortcut could use matter; Ctrl+Insert and Shift+Insert, for example, do not.
            if (ShortcutGestureMapper.FromGesture(gesture) is not { } chord || !ShortcutKeyVocabulary.IsKnown(chord.Key))
                continue;

            var kind = textEditing ? ShortcutReservationKind.TextEditing : ShortcutReservationKind.System;
            AddUnique(new ShortcutReservation(chord, kind, role));
        }

        foreach (var reservation in ViewScopedShortcuts.For(flavor))
            AddUnique(reservation);

        return new Snapshot(flavor, commandModifiers, reservations);

        void AddUnique(ShortcutReservation reservation)
        {
            if (!reservations.Contains(reservation))
                reservations.Add(reservation);
        }
    }

    private sealed record Snapshot(
        ShortcutPlatformFlavor Flavor,
        KeyModifiers CommandModifiers,
        IReadOnlyList<ShortcutReservation> Reservations);
}
