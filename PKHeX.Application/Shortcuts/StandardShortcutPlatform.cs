using System.Text;
using PKHeX.Application.Abstractions;

namespace PKHeX.Application.Shortcuts;

/// <summary>
/// Framework-free Windows/Linux keyboard conventions. Used when no host platform is registered,
/// for example by tests that construct view models directly. It reserves only the standard text
/// gestures (select all, copy, cut, paste and text undo/redo); the Avalonia host platform adds the
/// view-scoped and system gestures it knows about.
/// </summary>
public sealed class StandardShortcutPlatform : IShortcutPlatform
{
    /// <summary>Shared instance; the type has no state.</summary>
    public static StandardShortcutPlatform Instance { get; } = new();

    private static readonly IReadOnlyList<ShortcutReservation> TextReservations =
    [
        Text("A"),
        Text("C"),
        Text("X"),
        Text("V"),
        Text("Z", role: ShortcutTextRole.Undo),
        Text("Y", role: ShortcutTextRole.Redo),
        Text("Z", ShortcutModifiers.Shift, ShortcutTextRole.Redo),
    ];

    private StandardShortcutPlatform()
    {
    }

    public ShortcutPlatformFlavor Flavor => ShortcutPlatformFlavor.Standard;

    public IReadOnlyList<ShortcutReservation> Reservations => TextReservations;

    public string PrimaryModifierDisplay => "Ctrl";

    /// <summary>Formats as <c>Ctrl+Shift+Alt+Win+Key</c>, showing digit keys without their <c>D</c> prefix.</summary>
    public string FormatForDisplay(ShortcutChord concrete)
    {
        var builder = new StringBuilder();
        Append(builder, concrete.Modifiers, ShortcutModifiers.Control, "Ctrl");
        Append(builder, concrete.Modifiers, ShortcutModifiers.Shift, "Shift");
        Append(builder, concrete.Modifiers, ShortcutModifiers.Alt, "Alt");
        Append(builder, concrete.Modifiers, ShortcutModifiers.Meta, "Win");
        builder.Append(DisplayKey(concrete.Key));
        return builder.ToString();

        static void Append(StringBuilder sb, ShortcutModifiers set, ShortcutModifiers flag, string name)
        {
            if ((set & flag) != 0)
                sb.Append(name).Append('+');
        }
    }

    private static string DisplayKey(string? key) =>
        key is { Length: 2 } && key[0] == 'D' && char.IsAsciiDigit(key[1]) ? key[1..] : key ?? string.Empty;

    private static ShortcutReservation Text(
        string key,
        ShortcutModifiers extra = ShortcutModifiers.None,
        ShortcutTextRole role = ShortcutTextRole.None) =>
        new(new ShortcutChord(ShortcutModifiers.Control | extra, key), ShortcutReservationKind.TextEditing, role);
}
