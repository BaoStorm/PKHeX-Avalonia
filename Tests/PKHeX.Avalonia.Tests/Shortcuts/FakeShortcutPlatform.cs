using System.Collections.Generic;
using PKHeX.Application.Shortcuts;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// Framework-free <see cref="IShortcutPlatform"/> with explicit reservations, so the pure policy and
/// resolver rules can be tested for both platform flavors without an Avalonia host. The two
/// factories mirror the reservation lists the host platform builds (text gestures from the hotkey
/// configuration, the box/party view gestures, control gestures and system gestures).
/// </summary>
internal sealed class FakeShortcutPlatform(
    ShortcutPlatformFlavor flavor,
    IReadOnlyList<ShortcutReservation> reservations) : IShortcutPlatform
{
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
    private const ShortcutModifiers Cmd = ShortcutModifiers.Meta;
    private const ShortcutModifiers Shift = ShortcutModifiers.Shift;
    private const ShortcutModifiers Alt = ShortcutModifiers.Alt;
    private const ShortcutModifiers None = ShortcutModifiers.None;

    public ShortcutPlatformFlavor Flavor { get; } = flavor;
    public IReadOnlyList<ShortcutReservation> Reservations { get; } = reservations;
    public string PrimaryModifierDisplay => Flavor == ShortcutPlatformFlavor.Apple ? "Cmd" : "Ctrl";
    public string FormatForDisplay(ShortcutChord concrete) => concrete.ToStorageString();

    /// <summary>Windows/Linux desktop conventions, as the Avalonia host reports them.</summary>
    public static FakeShortcutPlatform StandardDesktop() => new(ShortcutPlatformFlavor.Standard,
    [
        .. TextGestures(Ctrl),
        .. ViewGestures(),
        R(Alt, "F4", ShortcutReservationKind.System),
        R(None, "F10", ShortcutReservationKind.System),
        R(Shift, "F10", ShortcutReservationKind.System),
    ]);

    /// <summary>macOS conventions: Command is the command modifier, the views keep literal Ctrl.</summary>
    public static FakeShortcutPlatform Apple() => new(ShortcutPlatformFlavor.Apple,
    [
        .. TextGestures(Cmd),
        .. ViewGestures(),
        R(Cmd, "Q", ShortcutReservationKind.System),
        R(Cmd, "H", ShortcutReservationKind.System),
        R(Cmd | Alt, "H", ShortcutReservationKind.System),
        R(Cmd, "M", ShortcutReservationKind.System),
        R(Cmd, "W", ShortcutReservationKind.System),
        R(Ctrl | Cmd, "F", ShortcutReservationKind.System),
    ]);

    private static IEnumerable<ShortcutReservation> TextGestures(ShortcutModifiers command) =>
    [
        R(command, "A", ShortcutReservationKind.TextEditing),
        R(command, "C", ShortcutReservationKind.TextEditing),
        R(command, "X", ShortcutReservationKind.TextEditing),
        R(command, "V", ShortcutReservationKind.TextEditing),
        R(command, "Z", ShortcutReservationKind.TextEditing, ShortcutTextRole.Undo),
        R(command, "Y", ShortcutReservationKind.TextEditing, ShortcutTextRole.Redo),
        R(command | Shift, "Z", ShortcutReservationKind.TextEditing, ShortcutTextRole.Redo),
    ];

    // Literal Ctrl on every platform: BoxViewer/PartyStrip/PartyViewer/EntitySeek bind these directly.
    private static IEnumerable<ShortcutReservation> ViewGestures() =>
    [
        R(Ctrl, "F", ShortcutReservationKind.Storage),
        R(Ctrl, "C", ShortcutReservationKind.Storage),
        R(Ctrl, "V", ShortcutReservationKind.Storage),
        R(None, "F3", ShortcutReservationKind.Storage),
        R(Shift, "F3", ShortcutReservationKind.Storage),
        R(None, "F2", ShortcutReservationKind.Controls),
        R(None, "F4", ShortcutReservationKind.Controls),
    ];

    private static ShortcutReservation R(
        ShortcutModifiers modifiers,
        string key,
        ShortcutReservationKind kind,
        ShortcutTextRole role = ShortcutTextRole.None) =>
        new(new ShortcutChord(modifiers, key), kind, role);
}
