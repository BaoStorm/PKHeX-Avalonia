using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Application;
using PKHeX.Application.Shortcuts;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// Persistence rules of <see cref="KeyboardShortcutService"/> (issue #224): portable storage, only
/// overrides stored, unknown IDs preserved, and exactly one save per effective change.
/// </summary>
public sealed class KeyboardShortcutServiceTests
{
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
    private const ShortcutModifiers Shift = ShortcutModifiers.Shift;
    private const ShortcutModifiers Meta = ShortcutModifiers.Meta;

    private static readonly IReadOnlyList<ShortcutDefinition> Definitions =
    [
        ShortcutDefinition.Same("OpenEncounterDatabase", "Primary+N"),
        ShortcutDefinition.Same("OpenBoxReport", "Primary+R"),
        ShortcutDefinition.Same("Undo", "Primary+Z", ShortcutTextRole.Undo),
        ShortcutDefinition.Same("OpenMisc3", null),
    ];

    private readonly AppSettings _settings = new();
    private readonly FakeSettingsStore _store = new();
    private int _changedCount;

    private KeyboardShortcutService CreateService(IShortcutPlatform? platform = null)
    {
        var service = new KeyboardShortcutService(_settings, _store, platform ?? FakeShortcutPlatform.StandardDesktop());
        service.Changed += (_, _) => _changedCount++;
        return service;
    }

    private static Dictionary<string, ShortcutChord?> Change(string id, ShortcutChord? chord) => new() { [id] = chord };

    [Fact]
    public void Apply_StoresThePortableForm()
    {
        var service = CreateService();

        var changed = service.Apply(Definitions, Change("OpenEncounterDatabase", new ShortcutChord(Ctrl | Shift, "N")));

        Assert.True(changed);
        Assert.Equal("Primary+Shift+N", _settings.KeyboardShortcuts["OpenEncounterDatabase"]);
        Assert.Same(_settings, _store.Saved);
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(1, _changedCount);

        var resolved = service.Resolve(Definitions)["OpenEncounterDatabase"];
        Assert.Equal(new ShortcutChord(Ctrl | Shift, "N"), resolved.Effective);
        Assert.Equal(ShortcutSource.User, resolved.Source);
    }

    [Fact]
    public void Apply_OnApple_StoresCommandAsPrimaryAndKeepsLiteralCtrl()
    {
        var service = CreateService(FakeShortcutPlatform.Apple());

        service.Apply(Definitions, new Dictionary<string, ShortcutChord?>
        {
            ["OpenEncounterDatabase"] = new ShortcutChord(Meta | Shift, "N"),
            ["OpenMisc3"] = new ShortcutChord(Ctrl | Meta, "M"),
        });

        Assert.Equal("Primary+Shift+N", _settings.KeyboardShortcuts["OpenEncounterDatabase"]);
        Assert.Equal("Primary+Ctrl+M", _settings.KeyboardShortcuts["OpenMisc3"]);
        Assert.Equal(new ShortcutChord(Ctrl | Meta, "M"), service.Resolve(Definitions)["OpenMisc3"].Effective);
    }

    [Fact]
    public void Apply_BackToTheDefaultRemovesTheOverride()
    {
        _settings.KeyboardShortcuts["OpenEncounterDatabase"] = "Primary+Shift+N";
        var service = CreateService();

        var changed = service.Apply(Definitions, Change("OpenEncounterDatabase", new ShortcutChord(Ctrl, "N")));

        Assert.True(changed);
        Assert.False(_settings.KeyboardShortcuts.ContainsKey("OpenEncounterDatabase"));
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(ShortcutSource.Default, service.Resolve(Definitions)["OpenEncounterDatabase"].Source);
    }

    [Fact]
    public void Apply_ClearingADefaultStoresAnEmptyString()
    {
        var service = CreateService();

        var changed = service.Apply(Definitions, Change("OpenEncounterDatabase", null));

        Assert.True(changed);
        Assert.Equal(string.Empty, _settings.KeyboardShortcuts["OpenEncounterDatabase"]);
        var resolved = service.Resolve(Definitions)["OpenEncounterDatabase"];
        Assert.Null(resolved.Effective);
        Assert.Equal(ShortcutSource.None, resolved.Source);
    }

    [Fact]
    public void Apply_ClearingAnActionWithoutADefaultRemovesTheEntry()
    {
        _settings.KeyboardShortcuts["OpenMisc3"] = "Primary+Shift+M";
        var service = CreateService();

        var changed = service.Apply(Definitions, Change("OpenMisc3", null));

        Assert.True(changed);
        Assert.False(_settings.KeyboardShortcuts.ContainsKey("OpenMisc3"));
    }

    [Fact]
    public void Apply_PreservesUnknownIdsAndUntouchedValues()
    {
        _settings.KeyboardShortcuts["FutureAction"] = "Primary+J";
        _settings.KeyboardShortcuts["OpenBoxReport"] = "garbage";
        var service = CreateService();

        service.Apply(Definitions, new Dictionary<string, ShortcutChord?>
        {
            ["OpenEncounterDatabase"] = new ShortcutChord(Ctrl | Shift, "N"),
            ["FutureAction"] = null, // not in the catalog: never touched
        });

        Assert.Equal("Primary+J", _settings.KeyboardShortcuts["FutureAction"]);
        Assert.Equal("garbage", _settings.KeyboardShortcuts["OpenBoxReport"]);
        Assert.Equal("Primary+Shift+N", _settings.KeyboardShortcuts["OpenEncounterDatabase"]);
    }

    [Fact]
    public void ResetAll_RemovesKnownOverridesButKeepsUnknownIds()
    {
        _settings.KeyboardShortcuts["FutureAction"] = "Primary+J";
        _settings.KeyboardShortcuts["OpenEncounterDatabase"] = "Primary+Shift+N";
        _settings.KeyboardShortcuts["OpenBoxReport"] = "";
        _settings.KeyboardShortcuts["OpenMisc3"] = "Primary+Shift+M";
        var service = CreateService();

        // The editor's Reset all sends every known action back to its platform default.
        var reset = Definitions.ToDictionary(d => d.Id, d => d.ResolveDefault(service.Platform.Flavor));
        var changed = service.Apply(Definitions, reset);

        Assert.True(changed);
        Assert.Equal(new Dictionary<string, string> { ["FutureAction"] = "Primary+J" }, _settings.KeyboardShortcuts);
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void Apply_WithoutAnEffectiveChange_DoesNotSave()
    {
        _settings.KeyboardShortcuts["OpenBoxReport"] = "Primary+Shift+R";
        var service = CreateService();

        var changed = service.Apply(Definitions, new Dictionary<string, ShortcutChord?>
        {
            ["OpenEncounterDatabase"] = new ShortcutChord(Ctrl, "N"), // already the default
            ["OpenMisc3"] = null, // already unassigned
            ["OpenBoxReport"] = new ShortcutChord(Ctrl | Shift, "R"), // already stored
            ["FutureAction"] = new ShortcutChord(Ctrl, "J"), // unknown
        });

        Assert.False(changed);
        Assert.Null(_store.Saved);
        Assert.Equal(0, _store.SaveCount);
        Assert.Equal(0, _changedCount);

        Assert.False(service.Apply(Definitions, new Dictionary<string, ShortcutChord?>()));
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public void Apply_ManyChangesSaveOnceAndRaiseChangedOnce()
    {
        var service = CreateService();

        var changed = service.Apply(Definitions, new Dictionary<string, ShortcutChord?>
        {
            ["OpenEncounterDatabase"] = new ShortcutChord(Ctrl | Shift, "N"),
            ["OpenBoxReport"] = null,
            ["OpenMisc3"] = new ShortcutChord(Ctrl | Shift, "M"),
        });

        Assert.True(changed);
        Assert.Equal(3, _settings.KeyboardShortcuts.Count);
        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(1, _changedCount);
    }

    [Theory]
    [InlineData(Ctrl, "C")] // reserved for text editing
    [InlineData(ShortcutModifiers.None, "N")] // needs a modifier
    [InlineData(ShortcutModifiers.Primary, "N")] // not concrete
    [InlineData(Ctrl, "NumPad1")] // outside the vocabulary
    public void Apply_UnusableChordThrowsAndChangesNothing(ShortcutModifiers modifiers, string key)
    {
        var service = CreateService();

        Assert.Throws<InvalidOperationException>(() => service.Apply(Definitions, new Dictionary<string, ShortcutChord?>
        {
            ["OpenBoxReport"] = new ShortcutChord(Ctrl | Shift, "R"),
            ["OpenEncounterDatabase"] = new ShortcutChord(modifiers, key),
        }));

        Assert.Empty(_settings.KeyboardShortcuts);
        Assert.Equal(0, _store.SaveCount);
        Assert.Equal(0, _changedCount);
    }

    [Fact]
    public void Apply_TextRoleActionMayUseItsTextGesture()
    {
        var service = CreateService();

        // Undo may sit on Ctrl+Z (its default); a different action may not.
        Assert.False(service.Apply(Definitions, Change("Undo", new ShortcutChord(Ctrl, "Z"))));
        Assert.Throws<InvalidOperationException>(() =>
            service.Apply(Definitions, Change("OpenMisc3", new ShortcutChord(Ctrl, "Z"))));
    }

    [Fact]
    public void Resolve_ToleratesANullMapFromJson()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{ \"KeyboardShortcuts\": null }");
        Assert.NotNull(settings);
        Assert.NotNull(settings.KeyboardShortcuts);

        var service = new KeyboardShortcutService(settings, _store, StandardShortcutPlatform.Instance);

        Assert.Equal(new ShortcutChord(Ctrl, "N"), service.Resolve(Definitions)["OpenEncounterDatabase"].Effective);
        Assert.True(service.Apply(Definitions, Change("OpenEncounterDatabase", null)));
        Assert.Equal(string.Empty, settings.KeyboardShortcuts["OpenEncounterDatabase"]);
    }

    [Fact]
    public void IsEditorOpen_IsAPlainFlag()
    {
        var service = CreateService();
        Assert.False(service.IsEditorOpen);

        service.IsEditorOpen = true;
        Assert.True(service.IsEditorOpen);
    }

    [Fact]
    public void AddApplication_RegistersTheServiceWithTheHostPlatformOrTheFallback()
    {
        var withoutHost = new ServiceCollection().AddApplication();
        withoutHost.AddSingleton(_settings);
        withoutHost.AddSingleton<ISettingsStore>(_store);
        using (var provider = withoutHost.BuildServiceProvider())
        {
            var service = provider.GetRequiredService<KeyboardShortcutService>();
            Assert.Same(StandardShortcutPlatform.Instance, service.Platform);
            Assert.Same(service, provider.GetRequiredService<KeyboardShortcutService>());
        }

        var apple = FakeShortcutPlatform.Apple();
        var withHost = new ServiceCollection().AddApplication();
        withHost.AddSingleton(_settings);
        withHost.AddSingleton<ISettingsStore>(_store);
        withHost.AddSingleton<IShortcutPlatform>(apple);
        using (var provider = withHost.BuildServiceProvider())
        {
            Assert.Same(apple, provider.GetRequiredService<KeyboardShortcutService>().Platform);
        }
    }
}
