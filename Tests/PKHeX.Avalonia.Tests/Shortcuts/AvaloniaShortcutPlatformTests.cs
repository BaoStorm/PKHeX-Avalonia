using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using PKHeX.Application.Shortcuts;
using PKHeX.Avalonia.Converters;
using PKHeX.Avalonia.Input;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// The Avalonia side of the shortcut model (issue #224): the framework-free key vocabulary maps
/// one-to-one onto Avalonia keys, chords become the gestures that menus and key bindings use, and
/// the host platform reports the command modifier and reserved gestures of the running platform.
/// </summary>
public sealed class AvaloniaShortcutPlatformTests
{
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
    private const ShortcutModifiers Shift = ShortcutModifiers.Shift;
    private const ShortcutModifiers Alt = ShortcutModifiers.Alt;
    private const ShortcutModifiers Meta = ShortcutModifiers.Meta;
    private const ShortcutModifiers None = ShortcutModifiers.None;

    /// <summary>The macOS convention simulated on the headless platform (which always reports Control).</summary>
    internal static AvaloniaShortcutPlatform AppleHost() =>
        new(KeyModifiers.Meta, AvaloniaShortcutPlatform.DefaultTextGestures(KeyModifiers.Meta));

    // ---------------------------------------------------------------------
    // Vocabulary and gesture mapping
    // ---------------------------------------------------------------------

    [Fact]
    public void EveryVocabularyName_IsANonNumericAvaloniaKeyName()
    {
        foreach (var name in ShortcutKeyVocabulary.Names)
        {
            Assert.False(int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _), name);
            Assert.True(Enum.TryParse<Key>(name, ignoreCase: false, out var key), name);
            Assert.True(Enum.IsDefined(key), name);
            Assert.Equal(name, key.ToString());
        }
    }

    [Theory]
    [InlineData(ShortcutModifiers.Control, "D1", Key.D1, KeyModifiers.Control)]
    [InlineData(ShortcutModifiers.Control, "D2", Key.D2, KeyModifiers.Control)]
    [InlineData(ShortcutModifiers.Control, "D3", Key.D3, KeyModifiers.Control)]
    [InlineData(ShortcutModifiers.Control | ShortcutModifiers.Shift, "N", Key.N, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(ShortcutModifiers.Meta | ShortcutModifiers.Alt, "F12", Key.F12, KeyModifiers.Meta | KeyModifiers.Alt)]
    [InlineData(ShortcutModifiers.None, "F5", Key.F5, KeyModifiers.None)]
    public void ToKeyGesture_UsesTheNamedKey(ShortcutModifiers modifiers, string key, Key expectedKey, KeyModifiers expectedModifiers)
    {
        var gesture = ShortcutGestureMapper.ToKeyGesture(new ShortcutChord(modifiers, key));

        Assert.Equal(new KeyGesture(expectedKey, expectedModifiers), gesture);
    }

    [Fact]
    public void ToKeyGesture_RoundTripsEveryVocabularyKey()
    {
        ShortcutModifiers[] modifierSets = [None, Ctrl, Ctrl | Shift, Meta, Meta | Alt | Shift];
        foreach (var name in ShortcutKeyVocabulary.Names)
        {
            foreach (var modifiers in modifierSets)
            {
                var chord = new ShortcutChord(modifiers, name);
                var gesture = ShortcutGestureMapper.ToKeyGesture(chord);
                Assert.Equal(chord, ShortcutGestureMapper.FromGesture(gesture));
            }
        }
    }

    [Fact]
    public void ToKeyGesture_RejectsPortableAndUnsupportedChords()
    {
        Assert.Throws<ArgumentException>(() => ShortcutGestureMapper.ToKeyGesture(new ShortcutChord(ShortcutModifiers.Primary, "N")));
        Assert.Throws<ArgumentException>(() => ShortcutGestureMapper.ToKeyGesture(new ShortcutChord(Ctrl, "Escape")));
        Assert.Throws<ArgumentException>(() => ShortcutGestureMapper.ToKeyGesture(new ShortcutChord(Ctrl, "NumPad1")));
        Assert.False(ShortcutGestureMapper.TryToKeyGesture(default, out _));
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightCtrl)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LWin)]
    [InlineData(Key.RWin)]
    [InlineData(Key.None)]
    [InlineData(Key.ImeProcessed)]
    [InlineData(Key.DeadCharProcessed)]
    [InlineData(Key.System)]
    [InlineData(Key.CapsLock)]
    [InlineData(Key.NumLock)]
    [InlineData(Key.Scroll)]
    public void FromKeyEvent_IgnoresKeysThatCannotEndAShortcut(Key key)
    {
        Assert.Null(ShortcutGestureMapper.FromKeyEvent(key, KeyModifiers.Control));
    }

    [Fact]
    public void FromKeyEvent_KeepsTheKeyNameAndModifiers()
    {
        Assert.Equal(new ShortcutChord(Ctrl | Shift, "N"), ShortcutGestureMapper.FromKeyEvent(Key.N, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Equal(new ShortcutChord(Meta, "D1"), ShortcutGestureMapper.FromKeyEvent(Key.D1, KeyModifiers.Meta));
        // Outside the vocabulary: returned by name so the policy can explain why it is not usable.
        Assert.Equal(new ShortcutChord(Ctrl, "Escape"), ShortcutGestureMapper.FromKeyEvent(Key.Escape, KeyModifiers.Control));
    }

    // ---------------------------------------------------------------------
    // Converter (menus)
    // ---------------------------------------------------------------------

    [Fact]
    public void Converter_GivesAGestureOnlyForConcreteSupportedChords()
    {
        var converter = ShortcutChordToKeyGestureConverter.Instance;
        object? Convert(object? value) => converter.Convert(value, typeof(KeyGesture), null, CultureInfo.InvariantCulture);

        Assert.Equal(new KeyGesture(Key.N, KeyModifiers.Control), Convert(new ShortcutChord(Ctrl, "N")));
        Assert.Equal(new KeyGesture(Key.D1, KeyModifiers.Meta), Convert((ShortcutChord?)new ShortcutChord(Meta, "D1")));
        Assert.Null(Convert(null));
        Assert.Null(Convert(new ShortcutChord(ShortcutModifiers.Primary, "N")));
        Assert.Null(Convert(new ShortcutChord(Ctrl, "Escape")));
        Assert.Null(Convert("Ctrl+N"));
    }

    // ---------------------------------------------------------------------
    // Host platform
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void HeadlessHost_IsStandard_AndFormatsLikeTheMenus()
    {
        var platform = new AvaloniaShortcutPlatform();

        // The platform reads the running application's hotkey configuration, not a built-in fallback.
        var configuration = global::Avalonia.Application.Current?.PlatformSettings?.HotkeyConfiguration;
        Assert.NotNull(configuration);
        Assert.Equal(configuration!.CommandModifiers, platform.CommandModifiers);
        Assert.Equal(ShortcutPlatformFlavor.Standard, platform.Flavor);
        Assert.Equal("Ctrl", platform.PrimaryModifierDisplay);
        Assert.Equal("Ctrl+1", platform.FormatForDisplay(new ShortcutChord(Ctrl, "D1")));
        Assert.Equal("Ctrl+Shift+N", platform.FormatForDisplay(new ShortcutChord(Ctrl | Shift, "N")));
        Assert.Equal(
            new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift).ToString("p", null),
            platform.FormatForDisplay(new ShortcutChord(Ctrl | Shift, "N")));
    }

    [AvaloniaFact]
    public void HeadlessHost_ReservesTextViewControlAndSystemGestures()
    {
        var platform = new AvaloniaShortcutPlatform();

        AssertReserved(platform, new(Ctrl, "A"), ShortcutReservationKind.TextEditing);
        AssertReserved(platform, new(Ctrl, "C"), ShortcutReservationKind.TextEditing);
        AssertReserved(platform, new(Ctrl, "X"), ShortcutReservationKind.TextEditing);
        AssertReserved(platform, new(Ctrl, "V"), ShortcutReservationKind.TextEditing);
        AssertReserved(platform, new(Ctrl, "Z"), ShortcutReservationKind.TextEditing, ShortcutTextRole.Undo);
        AssertReserved(platform, new(Ctrl, "Y"), ShortcutReservationKind.TextEditing, ShortcutTextRole.Redo);
        AssertReserved(platform, new(Ctrl | Shift, "Z"), ShortcutReservationKind.TextEditing, ShortcutTextRole.Redo);
        AssertReserved(platform, new(Ctrl, "F"), ShortcutReservationKind.Storage);
        AssertReserved(platform, new(Ctrl, "C"), ShortcutReservationKind.Storage);
        AssertReserved(platform, new(None, "F3"), ShortcutReservationKind.Storage);
        AssertReserved(platform, new(Shift, "F3"), ShortcutReservationKind.Storage);
        AssertReserved(platform, new(None, "F2"), ShortcutReservationKind.Controls);
        AssertReserved(platform, new(None, "F4"), ShortcutReservationKind.Controls);
        AssertReserved(platform, new(Alt, "F4"), ShortcutReservationKind.System);
        AssertReserved(platform, new(None, "F10"), ShortcutReservationKind.System);
        AssertReserved(platform, new(Shift, "F10"), ShortcutReservationKind.System);

        // Only chords a shortcut could use are listed (Ctrl+Insert and Shift+Insert are not), once each.
        Assert.All(platform.Reservations, reservation => Assert.True(ShortcutKeyVocabulary.IsKnown(reservation.Chord.Key)));
        Assert.Equal(platform.Reservations.Count, platform.Reservations.Distinct().Count());
        Assert.DoesNotContain(platform.Reservations, reservation => reservation.Chord == new ShortcutChord(Meta, "Q"));
    }

    [AvaloniaFact]
    public void HeadlessHost_PolicyBlocksViewAndTextGestures()
    {
        var platform = new AvaloniaShortcutPlatform();
        var tool = new ShortcutDefinition("OpenTool", null, null);
        var undo = new ShortcutDefinition("Undo", null, null, ShortcutTextRole.Undo);

        Assert.Equal(ShortcutValidationKind.Reserved, ShortcutPolicy.CheckUsable(tool, new(Ctrl, "F"), platform).Kind);
        Assert.Equal(ShortcutReservationKind.Storage, ShortcutPolicy.CheckUsable(tool, new(Ctrl, "F"), platform).ReservedFor);
        Assert.Equal(ShortcutReservationKind.TextEditing, ShortcutPolicy.CheckUsable(tool, new(Ctrl, "C"), platform).ReservedFor);
        Assert.Equal(ShortcutReservationKind.TextEditing, ShortcutPolicy.CheckUsable(tool, new(Ctrl, "Z"), platform).ReservedFor);
        Assert.Equal(ShortcutValidationKind.Ok, ShortcutPolicy.CheckUsable(undo, new(Ctrl, "Z"), platform).Kind);
        Assert.Equal(ShortcutValidationKind.Ok, ShortcutPolicy.CheckUsable(tool, new(Ctrl, "N"), platform).Kind);
        Assert.Equal(ShortcutValidationKind.Ok, ShortcutPolicy.CheckUsable(tool, new(None, "F5"), platform).Kind);
    }

    [AvaloniaFact]
    public void InjectedMetaCommandModifier_GivesTheAppleConvention()
    {
        var platform = AppleHost();

        Assert.Equal(ShortcutPlatformFlavor.Apple, platform.Flavor);
        Assert.True(ShortcutChord.TryParse("Primary+N", out var portable));
        var concrete = portable.Resolve(platform.Flavor);
        Assert.Equal(new ShortcutChord(Meta, "N"), concrete);
        Assert.Equal(new KeyGesture(Key.N, KeyModifiers.Meta), ShortcutGestureMapper.ToKeyGesture(concrete));
        // The headless format provider labels Meta "Cmd"; macOS registers "⌘".
        Assert.Equal("Cmd", platform.PrimaryModifierDisplay);
        Assert.Equal("Cmd+N", platform.FormatForDisplay(concrete));

        AssertReserved(platform, new(Meta, "C"), ShortcutReservationKind.TextEditing);
        AssertReserved(platform, new(Meta | Shift, "Z"), ShortcutReservationKind.TextEditing, ShortcutTextRole.Redo);
        AssertReserved(platform, new(Meta, "Q"), ShortcutReservationKind.System);
        AssertReserved(platform, new(Meta, "M"), ShortcutReservationKind.System);
        // The storage views bind literal Ctrl, so Ctrl+F stays theirs while Cmd+F is free.
        AssertReserved(platform, new(Ctrl, "F"), ShortcutReservationKind.Storage);
        var tool = new ShortcutDefinition("OpenTool", null, null);
        Assert.Equal(ShortcutValidationKind.Ok, ShortcutPolicy.CheckUsable(tool, new(Meta, "F"), platform).Kind);
        Assert.Equal(ShortcutValidationKind.Reserved, ShortcutPolicy.CheckUsable(tool, new(Ctrl, "F"), platform).Kind);
        Assert.DoesNotContain(platform.Reservations, reservation => reservation.Chord == new ShortcutChord(Alt, "F4"));
    }

    private static void AssertReserved(
        IShortcutPlatform platform,
        ShortcutChord chord,
        ShortcutReservationKind kind,
        ShortcutTextRole role = ShortcutTextRole.None) =>
        Assert.Contains(new ShortcutReservation(chord, kind, role), platform.Reservations);
}
