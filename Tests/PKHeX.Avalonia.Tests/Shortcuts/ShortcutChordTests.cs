using PKHeX.Application.Shortcuts;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>Parsing, storage formatting and platform resolution of <see cref="ShortcutChord"/> (issue #224).</summary>
public sealed class ShortcutChordTests
{
    private const ShortcutModifiers Primary = ShortcutModifiers.Primary;
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
    private const ShortcutModifiers Shift = ShortcutModifiers.Shift;
    private const ShortcutModifiers Alt = ShortcutModifiers.Alt;
    private const ShortcutModifiers Meta = ShortcutModifiers.Meta;

    [Theory]
    [InlineData("ctrl + n", Ctrl, "N")]
    [InlineData("Control+N", Ctrl, "N")]
    [InlineData("CTRL+n", Ctrl, "N")]
    [InlineData("  Primary + shift + n ", Primary | Shift, "N")]
    [InlineData("Shift+Primary+N", Primary | Shift, "N")]
    [InlineData("Alt+F5", Alt, "F5")]
    [InlineData("Option+A", Alt, "A")]
    [InlineData("opt+a", Alt, "A")]
    [InlineData("Cmd+f12", Meta, "F12")]
    [InlineData("command+K", Meta, "K")]
    [InlineData("Win+E", Meta, "E")]
    [InlineData("super+E", Meta, "E")]
    [InlineData("Meta+E", Meta, "E")]
    [InlineData("F5", ShortcutModifiers.None, "F5")]
    public void TryParse_IsCaseAndWhitespaceTolerant(string text, ShortcutModifiers modifiers, string key)
    {
        Assert.True(ShortcutChord.TryParse(text, out var chord));
        Assert.Equal(new ShortcutChord(modifiers, key), chord);
    }

    [Theory]
    [InlineData("1", ShortcutModifiers.None, "D1")]
    [InlineData("Ctrl+1", Ctrl, "D1")]
    [InlineData("Primary+3", Primary, "D3")]
    [InlineData("Ctrl+d2", Ctrl, "D2")]
    [InlineData("Ctrl+D0", Ctrl, "D0")]
    public void TryParse_MapsBareDigitsToTopRowDigitKeys(string text, ShortcutModifiers modifiers, string key)
    {
        // Avalonia's KeyGesture.Parse reads a bare "1" as Key.Cancel; the model must never produce that.
        Assert.True(ShortcutChord.TryParse(text, out var chord));
        Assert.Equal(new ShortcutChord(modifiers, key), chord);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+")]
    [InlineData("+N")]
    [InlineData("Ctrl++N")]
    [InlineData("Ctrl+N+M")]
    [InlineData("N+M")]
    [InlineData("Hyper+N")]
    [InlineData("Ctrl+NumPad1")]
    [InlineData("Ctrl+OemPlus")]
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+10")]
    public void TryParse_RejectsMalformedText(string? text)
    {
        Assert.False(ShortcutChord.TryParse(text, out _));
    }

    [Fact]
    public void ToStorageString_UsesFixedModifierOrder()
    {
        var all = new ShortcutChord(Meta | Alt | Shift | Ctrl | Primary, "N");
        Assert.Equal("Primary+Ctrl+Shift+Alt+Meta+N", all.ToStorageString());

        Assert.True(ShortcutChord.TryParse("shift + primary + n", out var portable));
        Assert.Equal("Primary+Shift+N", portable.ToStorageString());
        Assert.Equal("Alt+F4", new ShortcutChord(Alt, "F4").ToStorageString());
        Assert.Equal("Ctrl+D1", new ShortcutChord(Ctrl, "D1").ToStorageString());
        Assert.Equal("Primary+Shift+N", portable.ToString());
    }

    [Theory]
    [InlineData("Primary+N")]
    [InlineData("Primary+Shift+Z")]
    [InlineData("Ctrl+Alt+Meta+F12")]
    [InlineData("F5")]
    [InlineData("Primary+D9")]
    public void ToStorageString_RoundTripsThroughTryParse(string text)
    {
        Assert.True(ShortcutChord.TryParse(text, out var chord));
        Assert.True(ShortcutChord.TryParse(chord.ToStorageString(), out var again));
        Assert.Equal(chord, again);
        Assert.Equal(text, chord.ToStorageString());
    }

    [Fact]
    public void Resolve_MapsPrimaryToEachPlatformsCommandModifier()
    {
        var portable = new ShortcutChord(Primary | Shift, "N");

        Assert.Equal(new ShortcutChord(Ctrl | Shift, "N"), portable.Resolve(ShortcutPlatformFlavor.Standard));
        Assert.Equal(new ShortcutChord(Meta | Shift, "N"), portable.Resolve(ShortcutPlatformFlavor.Apple));
    }

    [Theory]
    [InlineData(ShortcutPlatformFlavor.Standard)]
    [InlineData(ShortcutPlatformFlavor.Apple)]
    public void ResolveAndToPortable_RoundTripOnBothFlavors(ShortcutPlatformFlavor flavor)
    {
        foreach (var text in new[] { "Primary+N", "Primary+Shift+Z", "Primary+Alt+D1", "F5", "Shift+F7" })
        {
            Assert.True(ShortcutChord.TryParse(text, out var portable));
            var concrete = portable.Resolve(flavor);
            Assert.Equal(ShortcutModifiers.None, concrete.Modifiers & Primary);
            Assert.Equal(portable, concrete.ToPortable(flavor));
        }
    }

    [Fact]
    public void ToPortable_KeepsLiteralCtrlOnApple()
    {
        // A deliberate Ctrl chord recorded on macOS must not turn into Cmd on the next start.
        var literalCtrl = new ShortcutChord(Ctrl, "N");
        Assert.Equal(literalCtrl, literalCtrl.ToPortable(ShortcutPlatformFlavor.Apple));
        Assert.Equal(literalCtrl, literalCtrl.Resolve(ShortcutPlatformFlavor.Apple));

        var ctrlAndCmd = new ShortcutChord(Ctrl | Meta, "N");
        var portable = ctrlAndCmd.ToPortable(ShortcutPlatformFlavor.Apple);
        Assert.Equal("Primary+Ctrl+N", portable.ToStorageString());
        Assert.Equal(ctrlAndCmd, portable.Resolve(ShortcutPlatformFlavor.Apple));
    }

    [Fact]
    public void ToPortable_KeepsLiteralMetaOnStandard()
    {
        var win = new ShortcutChord(Meta, "E");
        Assert.Equal(win, win.ToPortable(ShortcutPlatformFlavor.Standard));

        var ctrl = new ShortcutChord(Ctrl | Shift, "N");
        Assert.Equal("Primary+Shift+N", ctrl.ToPortable(ShortcutPlatformFlavor.Standard).ToStorageString());
    }

    [Fact]
    public void IsFunctionKey_OnlyForF1ToF12()
    {
        Assert.True(new ShortcutChord(ShortcutModifiers.None, "F1").IsFunctionKey);
        Assert.True(new ShortcutChord(Shift, "F12").IsFunctionKey);
        Assert.False(new ShortcutChord(Ctrl, "F").IsFunctionKey);
        Assert.False(new ShortcutChord(Ctrl, "D1").IsFunctionKey);
        Assert.False(new ShortcutChord(Ctrl, "F13").IsFunctionKey);
        Assert.False(default(ShortcutChord).IsFunctionKey);
        Assert.Equal(string.Empty, default(ShortcutChord).ToStorageString());
    }

    [Fact]
    public void Vocabulary_IsLettersTopRowDigitsAndFunctionKeys()
    {
        var names = ShortcutKeyVocabulary.Names;

        Assert.Equal(26 + 10 + 12, names.Count);
        Assert.Equal(names.Count, new HashSet<string>(names, StringComparer.Ordinal).Count);
        Assert.Contains("A", names);
        Assert.Contains("Z", names);
        Assert.Contains("D0", names);
        Assert.Contains("D9", names);
        Assert.Contains("F1", names);
        Assert.Contains("F12", names);
        Assert.DoesNotContain("NumPad1", names);
        Assert.DoesNotContain("OemPlus", names);
        Assert.DoesNotContain("F13", names);
        Assert.DoesNotContain("1", names);
        Assert.All(names, name => Assert.True(ShortcutKeyVocabulary.IsKnown(name)));
        Assert.False(ShortcutKeyVocabulary.IsKnown("n"));
    }

    [Theory]
    [InlineData("n", "N")]
    [InlineData(" f12 ", "F12")]
    [InlineData("7", "D7")]
    [InlineData("d7", "D7")]
    public void Vocabulary_TryNormalize_ReturnsCanonicalName(string token, string canonical)
    {
        Assert.True(ShortcutKeyVocabulary.TryNormalize(token, out var result));
        Assert.Equal(canonical, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("NumPad7")]
    [InlineData("Enter")]
    public void Vocabulary_TryNormalize_RejectsOtherKeys(string? token)
    {
        Assert.False(ShortcutKeyVocabulary.TryNormalize(token, out _));
    }
}
