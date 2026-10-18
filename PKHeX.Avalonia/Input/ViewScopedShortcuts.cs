using PKHeX.Application.Shortcuts;

namespace PKHeX.Avalonia.Input;

/// <summary>
/// Chords that views, standard controls and the operating system already use, so a configurable
/// main-window shortcut must not take them. Kept next to the XAML it mirrors;
/// <c>ViewKeyBindingInventoryTests</c> fails when a view binds a supported key that is missing here.
/// </summary>
/// <remarks>
/// The storage views bind literal <c>Ctrl</c> gestures (<c>Gesture="Ctrl+F"</c>), which is the
/// Control key on every platform, including macOS.
/// </remarks>
public static class ViewScopedShortcuts
{
    private const ShortcutModifiers None = ShortcutModifiers.None;
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
    private const ShortcutModifiers Shift = ShortcutModifiers.Shift;
    private const ShortcutModifiers Alt = ShortcutModifiers.Alt;
    private const ShortcutModifiers Meta = ShortcutModifiers.Meta;

    /// <summary>
    /// Box and party storage gestures: seek (<c>BoxViewer</c>, <c>EntitySeekView</c>) and slot
    /// view/set (<c>BoxViewer</c>, <c>PartyStrip</c>, <c>PartyViewer</c>).
    /// </summary>
    public static IReadOnlyList<ShortcutReservation> StorageGestures { get; } =
    [
        Reserve(Ctrl, "F", ShortcutReservationKind.Storage),
        Reserve(Ctrl, "C", ShortcutReservationKind.Storage),
        Reserve(Ctrl, "V", ShortcutReservationKind.Storage),
        Reserve(None, "F3", ShortcutReservationKind.Storage),
        Reserve(Shift, "F3", ShortcutReservationKind.Storage),
    ];

    /// <summary>Standard control gestures: data grid cell edit (F2) and drop-down open (F4).</summary>
    public static IReadOnlyList<ShortcutReservation> ControlGestures { get; } =
    [
        Reserve(None, "F2", ShortcutReservationKind.Controls),
        Reserve(None, "F4", ShortcutReservationKind.Controls),
    ];

    /// <summary>Windows/Linux window-manager and menu gestures.</summary>
    public static IReadOnlyList<ShortcutReservation> StandardSystemGestures { get; } =
    [
        Reserve(Alt, "F4", ShortcutReservationKind.System),
        Reserve(None, "F10", ShortcutReservationKind.System),
        Reserve(Shift, "F10", ShortcutReservationKind.System),
    ];

    /// <summary>macOS application and window gestures (quit, hide, hide others, minimize, close, full screen).</summary>
    public static IReadOnlyList<ShortcutReservation> AppleSystemGestures { get; } =
    [
        Reserve(Meta, "Q", ShortcutReservationKind.System),
        Reserve(Meta, "H", ShortcutReservationKind.System),
        Reserve(Meta | Alt, "H", ShortcutReservationKind.System),
        Reserve(Meta, "M", ShortcutReservationKind.System),
        Reserve(Meta, "W", ShortcutReservationKind.System),
        Reserve(Ctrl | Meta, "F", ShortcutReservationKind.System),
    ];

    /// <summary>The storage, control and system gestures for <paramref name="flavor"/>.</summary>
    public static IReadOnlyList<ShortcutReservation> For(ShortcutPlatformFlavor flavor) =>
    [
        .. StorageGestures,
        .. ControlGestures,
        .. flavor == ShortcutPlatformFlavor.Apple ? AppleSystemGestures : StandardSystemGestures,
    ];

    private static ShortcutReservation Reserve(ShortcutModifiers modifiers, string key, ShortcutReservationKind kind) =>
        new(new ShortcutChord(modifiers, key), kind);
}
