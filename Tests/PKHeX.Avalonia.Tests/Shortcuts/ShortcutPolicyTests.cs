using System.Collections.Generic;
using PKHeX.Application.Shortcuts;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>Assignment rules of <see cref="ShortcutPolicy"/> on both platform flavors (issue #224).</summary>
public sealed class ShortcutPolicyTests
{
    private const ShortcutModifiers None = ShortcutModifiers.None;
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
    private const ShortcutModifiers Shift = ShortcutModifiers.Shift;
    private const ShortcutModifiers Alt = ShortcutModifiers.Alt;
    private const ShortcutModifiers Meta = ShortcutModifiers.Meta;

    private static readonly ShortcutDefinition Plain = ShortcutDefinition.Same("OpenEncounterDatabase", "Primary+N");
    private static readonly ShortcutDefinition Undo = ShortcutDefinition.Same("Undo", "Primary+Z", ShortcutTextRole.Undo);
    private static readonly ShortcutDefinition Redo = new("Redo", "Primary+Y", "Primary+Shift+Z", ShortcutTextRole.Redo);

    private static ShortcutValidation Check(ShortcutModifiers modifiers, string key, IShortcutPlatform? platform = null, ShortcutDefinition? definition = null) =>
        ShortcutPolicy.CheckUsable(definition ?? Plain, new ShortcutChord(modifiers, key), platform ?? FakeShortcutPlatform.StandardDesktop());

    [Theory]
    [InlineData(None, "N")]
    [InlineData(Shift, "N")]
    [InlineData(Alt, "N")]
    [InlineData(Alt | Shift, "D1")]
    public void LettersAndDigitsNeedTheCommandModifier(ShortcutModifiers modifiers, string key)
    {
        // Bare letters, Shift+letter and Alt+letter type text or open localized menu access keys.
        var result = Check(modifiers, key);
        Assert.Equal(ShortcutValidationKind.NeedsModifier, result.Kind);
    }

    [Theory]
    [InlineData(None, "F5")]
    [InlineData(Shift, "F7")]
    [InlineData(Ctrl, "N")]
    [InlineData(Ctrl | Shift, "N")]
    [InlineData(Ctrl, "D1")]
    [InlineData(Ctrl, "F3")]
    public void FunctionKeysAndCommandChordsAreUsable(ShortcutModifiers modifiers, string key)
    {
        Assert.Equal(ShortcutValidationKind.Ok, Check(modifiers, key).Kind);
    }

    [Theory]
    [InlineData(None, "F2", ShortcutReservationKind.Controls)]
    [InlineData(None, "F3", ShortcutReservationKind.Storage)]
    [InlineData(Shift, "F3", ShortcutReservationKind.Storage)]
    [InlineData(None, "F4", ShortcutReservationKind.Controls)]
    [InlineData(None, "F10", ShortcutReservationKind.System)]
    [InlineData(Shift, "F10", ShortcutReservationKind.System)]
    [InlineData(Alt, "F4", ShortcutReservationKind.System)]
    [InlineData(Ctrl, "F", ShortcutReservationKind.Storage)]
    [InlineData(Ctrl, "C", ShortcutReservationKind.TextEditing)]
    [InlineData(Ctrl, "V", ShortcutReservationKind.TextEditing)]
    [InlineData(Ctrl, "A", ShortcutReservationKind.TextEditing)]
    [InlineData(Ctrl, "X", ShortcutReservationKind.TextEditing)]
    public void ReservedChordsReportTheirOwner(ShortcutModifiers modifiers, string key, ShortcutReservationKind owner)
    {
        var result = Check(modifiers, key);

        Assert.Equal(ShortcutValidationKind.Reserved, result.Kind);
        Assert.Equal(owner, result.ReservedFor);
    }

    [Fact]
    public void TextUndoAndRedoGesturesOnlyServeTheMatchingRole()
    {
        var ctrlZ = new ShortcutChord(Ctrl, "Z");
        var ctrlY = new ShortcutChord(Ctrl, "Y");
        var ctrlShiftZ = new ShortcutChord(Ctrl | Shift, "Z");

        foreach (IShortcutPlatform platform in new IShortcutPlatform[] { FakeShortcutPlatform.StandardDesktop(), StandardShortcutPlatform.Instance })
        {
            Assert.Equal(ShortcutValidationKind.Ok, ShortcutPolicy.CheckUsable(Undo, ctrlZ, platform).Kind);
            Assert.Equal(ShortcutValidationKind.Reserved, ShortcutPolicy.CheckUsable(Plain, ctrlZ, platform).Kind);
            Assert.Equal(ShortcutValidationKind.Reserved, ShortcutPolicy.CheckUsable(Redo, ctrlZ, platform).Kind);

            Assert.Equal(ShortcutValidationKind.Ok, ShortcutPolicy.CheckUsable(Redo, ctrlY, platform).Kind);
            Assert.Equal(ShortcutValidationKind.Ok, ShortcutPolicy.CheckUsable(Redo, ctrlShiftZ, platform).Kind);
            var undoOnCtrlY = ShortcutPolicy.CheckUsable(Undo, ctrlY, platform);
            Assert.Equal(ShortcutValidationKind.Reserved, undoOnCtrlY.Kind);
            Assert.Equal(ShortcutReservationKind.TextEditing, undoOnCtrlY.ReservedFor);
        }
    }

    [Fact]
    public void CtrlAltIsAltGrOnStandardButCmdAltIsFineOnApple()
    {
        Assert.Equal(ShortcutValidationKind.AltGr, Check(Ctrl | Alt, "E").Kind);
        Assert.Equal(ShortcutValidationKind.AltGr, Check(Ctrl | Alt | Shift, "E").Kind);
        Assert.Equal(ShortcutValidationKind.Ok, Check(Meta | Alt, "E", FakeShortcutPlatform.Apple()).Kind);
    }

    [Fact]
    public void WindowsKeyIsReservedForTheSystemOnStandard()
    {
        var win = Check(Meta, "E");
        Assert.Equal(ShortcutValidationKind.Reserved, win.Kind);
        Assert.Equal(ShortcutReservationKind.System, win.ReservedFor);

        var ctrlWin = Check(Ctrl | Meta, "N", StandardShortcutPlatform.Instance);
        Assert.Equal(ShortcutValidationKind.Reserved, ctrlWin.Kind);
        Assert.Equal(ShortcutReservationKind.System, ctrlWin.ReservedFor);
    }

    [Fact]
    public void AppleRulesUseCommandAndKeepTheViewsLiteralCtrlGestures()
    {
        var apple = FakeShortcutPlatform.Apple();

        foreach (var key in new[] { "Q", "M", "H", "W" })
        {
            var result = Check(Meta, key, apple);
            Assert.Equal(ShortcutValidationKind.Reserved, result.Kind);
            Assert.Equal(ShortcutReservationKind.System, result.ReservedFor);
        }

        Assert.Equal(ShortcutValidationKind.Ok, Check(Meta, "F", apple).Kind);
        Assert.Equal(ShortcutValidationKind.Ok, Check(Meta, "N", apple).Kind);
        Assert.Equal(ShortcutValidationKind.Ok, Check(Ctrl | Meta, "N", apple).Kind);

        var literalCtrlF = Check(Ctrl, "F", apple);
        Assert.Equal(ShortcutValidationKind.Reserved, literalCtrlF.Kind);
        Assert.Equal(ShortcutReservationKind.Storage, literalCtrlF.ReservedFor);

        Assert.Equal(ShortcutValidationKind.NeedsModifier, Check(Ctrl, "N", apple).Kind);
        Assert.Equal(ShortcutValidationKind.NeedsModifier, Check(Shift, "N", apple).Kind);
        Assert.Equal(ShortcutValidationKind.Reserved, Check(Meta, "C", apple).Kind);
        Assert.Equal(ShortcutValidationKind.Ok, Check(Meta, "Z", apple, Undo).Kind);
        Assert.Equal(ShortcutValidationKind.Ok, Check(Meta | Shift, "Z", apple, Redo).Kind);
    }

    [Theory]
    [InlineData(ShortcutModifiers.Primary, "N")]
    [InlineData(Ctrl, "NumPad1")]
    [InlineData(Ctrl, "n")]
    [InlineData(Ctrl, "1")]
    [InlineData(Ctrl, "F13")]
    [InlineData((ShortcutModifiers)32 | Ctrl, "N")]
    public void UnknownKeysAndNonConcreteChordsAreUnsupported(ShortcutModifiers modifiers, string key)
    {
        Assert.Equal(ShortcutValidationKind.UnsupportedKey, Check(modifiers, key).Kind);
    }

    [Fact]
    public void UnsupportedKeyIsReportedForADefaultChord()
    {
        var result = ShortcutPolicy.CheckUsable(Plain, default, FakeShortcutPlatform.StandardDesktop());
        Assert.Equal(ShortcutValidationKind.UnsupportedKey, result.Kind);
    }

    [Fact]
    public void Validate_ReportsTheConflictingAction()
    {
        var draft = new Dictionary<string, ShortcutChord?>
        {
            ["OpenEncounterDatabase"] = new ShortcutChord(Ctrl, "N"),
            ["OpenToolLauncher"] = new ShortcutChord(Ctrl, "K"),
        };

        var result = ShortcutPolicy.Validate(Plain, new ShortcutChord(Ctrl, "K"), FakeShortcutPlatform.StandardDesktop(), draft);

        Assert.Equal(ShortcutValidationKind.Conflict, result.Kind);
        Assert.Equal("OpenToolLauncher", result.ConflictingActionId);
    }

    [Fact]
    public void Validate_SameChordOnTheSameActionIsUnchanged()
    {
        var draft = new Dictionary<string, ShortcutChord?> { ["OpenEncounterDatabase"] = new ShortcutChord(Ctrl, "N") };

        var result = ShortcutPolicy.Validate(Plain, new ShortcutChord(Ctrl, "N"), FakeShortcutPlatform.StandardDesktop(), draft);

        Assert.Equal(ShortcutValidationKind.Unchanged, result.Kind);
        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void Validate_UnassignedActionsNeverConflict()
    {
        var draft = new Dictionary<string, ShortcutChord?>
        {
            ["OpenEncounterDatabase"] = null,
            ["OpenBoxReport"] = null,
            ["OpenMisc3"] = null,
        };

        var result = ShortcutPolicy.Validate(Plain, new ShortcutChord(Ctrl | Shift, "N"), FakeShortcutPlatform.StandardDesktop(), draft);

        Assert.Equal(ShortcutValidationKind.Ok, result.Kind);
        Assert.Null(result.ConflictingActionId);
    }

    [Fact]
    public void Validate_RunsTheUsabilityRulesFirst()
    {
        // Even a chord another action holds is reported as reserved rather than as a conflict.
        var draft = new Dictionary<string, ShortcutChord?> { ["Other"] = new ShortcutChord(Ctrl, "C") };

        var result = ShortcutPolicy.Validate(Plain, new ShortcutChord(Ctrl, "C"), FakeShortcutPlatform.StandardDesktop(), draft);

        Assert.Equal(ShortcutValidationKind.Reserved, result.Kind);
    }

    [Fact]
    public void StandardFallbackPlatform_FormatsForDisplay()
    {
        var platform = StandardShortcutPlatform.Instance;

        Assert.Equal(ShortcutPlatformFlavor.Standard, platform.Flavor);
        Assert.Equal("Ctrl", platform.PrimaryModifierDisplay);
        Assert.Equal("Ctrl+Shift+N", platform.FormatForDisplay(new ShortcutChord(Ctrl | Shift, "N")));
        Assert.Equal("Ctrl+1", platform.FormatForDisplay(new ShortcutChord(Ctrl, "D1")));
        Assert.Equal("Ctrl+Shift+Alt+F5", platform.FormatForDisplay(new ShortcutChord(Alt | Shift | Ctrl, "F5")));
        Assert.All(platform.Reservations, reservation =>
            Assert.Equal(ShortcutReservationKind.TextEditing, reservation.Kind));
    }
}
