using System.Collections.Generic;
using System.Text.Json;
using PKHeX.Application.Shortcuts;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// Resolution of stored overrides against the catalog defaults by <see cref="ShortcutResolver"/>
/// (issue #224): precedence, tolerated bad data, and chord collisions.
/// </summary>
public sealed class ShortcutResolverTests
{
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
    private const ShortcutModifiers Shift = ShortcutModifiers.Shift;
    private const ShortcutModifiers Meta = ShortcutModifiers.Meta;

    private static readonly IReadOnlyList<ShortcutDefinition> Definitions =
    [
        ShortcutDefinition.Same("OpenEncounterDatabase", "Primary+N"),
        ShortcutDefinition.Same("OpenBoxReport", "Primary+R"),
        ShortcutDefinition.Same("Undo", "Primary+Z", ShortcutTextRole.Undo),
        new("Redo", "Primary+Y", "Primary+Shift+Z", ShortcutTextRole.Redo),
        ShortcutDefinition.Same("OpenMisc3", null),
        new("OpenBatchEditor", "Primary+M", null),
    ];

    private static ShortcutTable Resolve(Dictionary<string, string>? overrides, IShortcutPlatform? platform = null) =>
        ShortcutResolver.Resolve(Definitions, overrides, platform ?? FakeShortcutPlatform.StandardDesktop());

    [Fact]
    public void WithoutOverrides_UsesThePlatformDefaults()
    {
        var table = Resolve(null);

        Assert.Equal(Definitions.Count, table.Count);
        Assert.Equal(Definitions.Select(d => d.Id), table.Ordered.Select(r => r.Id));

        var encounter = table["OpenEncounterDatabase"];
        Assert.Equal(new ShortcutChord(Ctrl, "N"), encounter.Effective);
        Assert.Equal(new ShortcutChord(Ctrl, "N"), encounter.Default);
        Assert.Equal(ShortcutSource.Default, encounter.Source);
        Assert.Null(encounter.Issue);

        Assert.Equal(new ShortcutChord(Ctrl, "Y"), table["Redo"].Effective);
        Assert.Equal(new ShortcutChord(Ctrl, "Z"), table["Undo"].Effective);

        var misc = table["OpenMisc3"];
        Assert.Null(misc.Effective);
        Assert.Null(misc.Default);
        Assert.Equal(ShortcutSource.None, misc.Source);
        Assert.Null(table.EffectiveFor("OpenMisc3"));
        Assert.Null(table.EffectiveFor("NotAnAction"));
    }

    [Fact]
    public void AppleFlavor_ResolvesPrimaryToCommandAndUsesAppleDefaults()
    {
        var table = Resolve(new Dictionary<string, string> { ["OpenBoxReport"] = "Primary+Shift+R" }, FakeShortcutPlatform.Apple());

        Assert.Equal(new ShortcutChord(Meta, "N"), table["OpenEncounterDatabase"].Effective);
        Assert.Equal(new ShortcutChord(Meta | Shift, "Z"), table["Redo"].Effective);
        Assert.Equal(new ShortcutChord(Meta | Shift, "R"), table["OpenBoxReport"].Effective);
        Assert.Equal(ShortcutSource.User, table["OpenBoxReport"].Source);
        Assert.Null(table["OpenBatchEditor"].Effective); // no Apple default: Cmd+M minimizes
    }

    [Fact]
    public void Override_ReplacesTheDefault()
    {
        var table = Resolve(new Dictionary<string, string> { ["OpenEncounterDatabase"] = "Primary+Shift+N" });

        var encounter = table["OpenEncounterDatabase"];
        Assert.Equal(new ShortcutChord(Ctrl | Shift, "N"), encounter.Effective);
        Assert.Equal(new ShortcutChord(Ctrl, "N"), encounter.Default);
        Assert.Equal(ShortcutSource.User, encounter.Source);
        Assert.Null(encounter.Issue);
    }

    [Fact]
    public void Override_CanAssignAnActionWithoutADefault()
    {
        var table = Resolve(new Dictionary<string, string> { ["OpenMisc3"] = "ctrl + shift + m" });

        Assert.Equal(new ShortcutChord(Ctrl | Shift, "M"), table["OpenMisc3"].Effective);
        Assert.Equal(ShortcutSource.User, table["OpenMisc3"].Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyOverride_RemovesTheShortcut(string stored)
    {
        var table = Resolve(new Dictionary<string, string> { ["OpenEncounterDatabase"] = stored });

        var encounter = table["OpenEncounterDatabase"];
        Assert.Null(encounter.Effective);
        Assert.Equal(new ShortcutChord(Ctrl, "N"), encounter.Default);
        Assert.Equal(ShortcutSource.None, encounter.Source);
        Assert.Null(encounter.Issue);
    }

    [Fact]
    public void JsonNullOverride_IsToleratedAsTheDefault()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            "{ \"KeyboardShortcuts\": { \"OpenEncounterDatabase\": null, \"OpenBoxReport\": \"Primary+Shift+R\" } }");
        Assert.NotNull(settings);

        var table = Resolve(settings.KeyboardShortcuts);

        Assert.Equal(new ShortcutChord(Ctrl, "N"), table["OpenEncounterDatabase"].Effective);
        Assert.Equal(ShortcutSource.Default, table["OpenEncounterDatabase"].Source);
        Assert.Null(table["OpenEncounterDatabase"].Issue);
        Assert.Equal(new ShortcutChord(Ctrl | Shift, "R"), table["OpenBoxReport"].Effective);
    }

    [Theory]
    [InlineData("Ctrl+Banana")]
    [InlineData("Primary+N+M")]
    [InlineData("Primary+")]
    public void InvalidStored_KeepsTheDefaultAndCarriesTheRawValue(string stored)
    {
        var table = Resolve(new Dictionary<string, string> { ["OpenEncounterDatabase"] = stored });

        var encounter = table["OpenEncounterDatabase"];
        Assert.Equal(new ShortcutChord(Ctrl, "N"), encounter.Effective);
        Assert.Equal(ShortcutSource.Default, encounter.Source);
        Assert.NotNull(encounter.Issue);
        Assert.Equal(ShortcutIssueKind.InvalidStored, encounter.Issue.Kind);
        Assert.Equal(stored, encounter.Issue.Raw);
    }

    [Theory]
    [InlineData("Primary+C")] // reserved for text editing
    [InlineData("N")] // needs the command modifier
    [InlineData("Ctrl+Alt+E")] // AltGr
    [InlineData("F2")] // grid cell edit
    [InlineData("Primary+Y")] // text redo, not undo
    public void UnusableStored_KeepsTheDefaultAndCarriesTheRawValue(string stored)
    {
        var table = Resolve(new Dictionary<string, string> { ["Undo"] = stored });

        var undo = table["Undo"];
        Assert.Equal(new ShortcutChord(Ctrl, "Z"), undo.Effective);
        Assert.Equal(ShortcutSource.Default, undo.Source);
        Assert.NotNull(undo.Issue);
        Assert.Equal(ShortcutIssueKind.UnusableStored, undo.Issue.Kind);
        Assert.Equal(stored, undo.Issue.Raw);
    }

    [Fact]
    public void PrimaryNextToItsOwnModifier_IsUnusable_AndSuppressesNoDefault()
    {
        // "Primary+Ctrl+N" is Cmd+Ctrl+N on macOS. On Windows/Linux Primary is Ctrl, so the chord
        // would collapse into plain Ctrl+N and take the Encounter Database default.
        var overrides = new Dictionary<string, string> { ["OpenMisc3"] = "Primary+Ctrl+N" };

        var standard = Resolve(overrides);
        var misc = standard["OpenMisc3"];
        Assert.Null(misc.Effective);
        Assert.Equal(ShortcutIssueKind.UnusableStored, misc.Issue!.Kind);
        Assert.Equal("Primary+Ctrl+N", misc.Issue.Raw);
        Assert.Equal(new ShortcutChord(Ctrl, "N"), standard["OpenEncounterDatabase"].Effective);
        Assert.Null(standard["OpenEncounterDatabase"].Issue);

        // On macOS it is a valid Ctrl+Cmd chord.
        var apple = Resolve(overrides, FakeShortcutPlatform.Apple());
        Assert.Equal(new ShortcutChord(Ctrl | Meta, "N"), apple["OpenMisc3"].Effective);
        Assert.Equal(ShortcutSource.User, apple["OpenMisc3"].Source);

        // The same rule on macOS, where Primary is Cmd.
        var ambiguousOnApple = Resolve(new Dictionary<string, string> { ["OpenMisc3"] = "Primary+Meta+N" }, FakeShortcutPlatform.Apple());
        Assert.Equal(ShortcutIssueKind.UnusableStored, ambiguousOnApple["OpenMisc3"].Issue!.Kind);
        Assert.Equal(new ShortcutChord(Meta, "N"), ambiguousOnApple["OpenEncounterDatabase"].Effective);
    }

    [Fact]
    public void TextRoleOverride_MayUseItsTextGesture()
    {
        var table = Resolve(new Dictionary<string, string> { ["Redo"] = "Primary+Shift+Z" });

        Assert.Equal(new ShortcutChord(Ctrl | Shift, "Z"), table["Redo"].Effective);
        Assert.Equal(ShortcutSource.User, table["Redo"].Source);
    }

    [Fact]
    public void UnknownIds_AreIgnoredAndNotCollided()
    {
        var overrides = new Dictionary<string, string>
        {
            ["SomeFutureAction"] = "Primary+N",
            ["AnotherFutureAction"] = "garbage",
        };

        var table = Resolve(overrides);

        Assert.Equal(Definitions.Count, table.Count);
        Assert.False(table.TryGet("SomeFutureAction", out _));
        Assert.Equal(new ShortcutChord(Ctrl, "N"), table["OpenEncounterDatabase"].Effective);
        Assert.Null(table["OpenEncounterDatabase"].Issue);
    }

    [Fact]
    public void SingleUserOverride_WinsOverADefaultOnTheSameChord()
    {
        var table = Resolve(new Dictionary<string, string> { ["OpenBoxReport"] = "Primary+N" });

        var report = table["OpenBoxReport"];
        Assert.Equal(new ShortcutChord(Ctrl, "N"), report.Effective);
        Assert.Equal(ShortcutSource.User, report.Source);
        Assert.Null(report.Issue);

        var encounter = table["OpenEncounterDatabase"];
        Assert.Null(encounter.Effective);
        Assert.Equal(new ShortcutChord(Ctrl, "N"), encounter.Default);
        Assert.Equal(ShortcutSource.None, encounter.Source);
        Assert.NotNull(encounter.Issue);
        Assert.Equal(ShortcutIssueKind.SuppressedDefault, encounter.Issue.Kind);
        Assert.Equal("OpenBoxReport", encounter.Issue.OtherActionId);
    }

    [Fact]
    public void TwoUserOverridesOnTheSameChord_AreBothInactive()
    {
        var table = Resolve(new Dictionary<string, string>
        {
            ["OpenBoxReport"] = "Primary+Shift+K",
            ["OpenMisc3"] = "ctrl+shift+k",
        });

        var report = table["OpenBoxReport"];
        Assert.Null(report.Effective);
        Assert.Equal(ShortcutIssueKind.DuplicateStored, report.Issue?.Kind);
        Assert.Equal("Primary+Shift+K", report.Issue?.Raw);
        Assert.Equal("OpenMisc3", report.Issue?.OtherActionId);

        var misc = table["OpenMisc3"];
        Assert.Null(misc.Effective);
        Assert.Equal(ShortcutIssueKind.DuplicateStored, misc.Issue?.Kind);
        Assert.Equal("ctrl+shift+k", misc.Issue?.Raw);
        Assert.Equal("OpenBoxReport", misc.Issue?.OtherActionId);
    }

    [Fact]
    public void TwoUserOverridesOnADefaultsChord_SuppressTheDefaultToo()
    {
        var table = Resolve(new Dictionary<string, string>
        {
            ["OpenBoxReport"] = "Primary+N",
            ["OpenMisc3"] = "Primary+N",
        });

        Assert.Null(table["OpenBoxReport"].Effective);
        Assert.Null(table["OpenMisc3"].Effective);
        Assert.Equal(ShortcutIssueKind.DuplicateStored, table["OpenBoxReport"].Issue?.Kind);
        Assert.Equal(ShortcutIssueKind.DuplicateStored, table["OpenMisc3"].Issue?.Kind);

        var encounter = table["OpenEncounterDatabase"];
        Assert.Null(encounter.Effective);
        Assert.Equal(ShortcutIssueKind.SuppressedDefault, encounter.Issue?.Kind);
    }

    [Fact]
    public void CollidingDefaults_AreAllInactive()
    {
        IReadOnlyList<ShortcutDefinition> buggy =
        [
            ShortcutDefinition.Same("First", "Primary+J"),
            ShortcutDefinition.Same("Second", "Primary+J"),
            ShortcutDefinition.Same("Third", "Primary+L"),
        ];

        var table = ShortcutResolver.Resolve(buggy, null, FakeShortcutPlatform.StandardDesktop());

        Assert.Null(table["First"].Effective);
        Assert.Null(table["Second"].Effective);
        Assert.Equal(ShortcutIssueKind.DuplicateStored, table["First"].Issue?.Kind);
        Assert.Equal("Second", table["First"].Issue?.OtherActionId);
        Assert.Equal("First", table["Second"].Issue?.OtherActionId);
        Assert.Equal(new ShortcutChord(Ctrl, "L"), table["Third"].Effective);
    }

    [Fact]
    public void Resolve_NeverMutatesTheOverrides()
    {
        var overrides = new Dictionary<string, string>
        {
            ["OpenEncounterDatabase"] = "garbage",
            ["OpenBoxReport"] = "Primary+N",
            ["OpenMisc3"] = "",
            ["FutureAction"] = "Primary+Q",
        };
        var before = overrides.ToDictionary(pair => pair.Key, pair => pair.Value);

        _ = Resolve(overrides);

        Assert.Equal(before, overrides);
    }

    [Fact]
    public void DuplicateDefinitionIds_AreRejected()
    {
        IReadOnlyList<ShortcutDefinition> duplicated =
        [
            ShortcutDefinition.Same("OpenEncounterDatabase", "Primary+N"),
            ShortcutDefinition.Same("OpenEncounterDatabase", "Primary+J"),
        ];

        Assert.Throws<ArgumentException>(() => ShortcutResolver.Resolve(duplicated, null, StandardShortcutPlatform.Instance));
    }
}
