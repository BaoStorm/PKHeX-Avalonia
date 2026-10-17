using Avalonia.Controls;
using System.Globalization;
using System.Buffers.Binary;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;
using PKHeX.Presentation.ViewModels;
using Xunit;

namespace PKHeX.Avalonia.Tests;

public sealed class BoxLayoutEditorTests
{
    public static IEnumerable<object[]> Mappings()
    {
        yield return ["rs", 12, new[] { 24, 12, 25, 26 }];
        yield return ["e", 12, new[] { 24, 12, 25, 15, 27 }];
        yield return ["frlg", 12, new[] { 28, 12, 29, 15 }];
        yield return ["rsbox", 12, new[] { 24, 12, 25, 26, 30, 29, 31, 32, 33 }];
        yield return ["dp", 16, new[] { 34, 35, 36, 37, 38, 39, 40, 41 }];
        yield return ["pt", 16, new[] { 42, 43, 36, 44, 38, 39, 40, 41 }];
        yield return ["hgss", 16, new[] { 45, 46, 47, 48, 38, 49, 50, 51 }];
        yield return ["bw", 16, new[] { 52, 53, 54, 55, 56, 57, 58, 59 }];
        yield return ["b2w2", 16, new[] { 54, 55, 60, 61, 62, 63, 52, 53 }];
        yield return ["xy", 24, Array.Empty<int>()];
        yield return ["sm", 16, Array.Empty<int>()];
    }

    [Theory]
    [MemberData(nameof(Mappings))]
    public void GameChoicesUseTheCompleteUpstreamCatalogMapping(string game, int prefix, int[] extra)
    {
        var choices = BoxWallpaperChoices.Create(CreateSave(game));
        var indexes = Enumerable.Range(0, prefix).Concat(extra).ToArray();
        Assert.Equal(indexes.Length, choices.Count);
        for (int id = 0; id < choices.Count; id++)
        {
            Assert.Equal(id, choices[id].Value);
            Assert.Equal(GameInfo.Strings.wallpapernames[indexes[id]], choices[id].Text);
        }
    }

    [Fact]
    public void BdspKeepsBothDiamondPearlAndPlatinumSpecialSets()
    {
        var choices = BoxWallpaperChoices.Create(new SAV8BS());
        Assert.Equal(32, choices.Count);
        int[] dp = [34, 35, 36, 37, 38, 39, 40, 41];
        int[] pt = [42, 43, 36, 44, 38, 39, 40, 41];
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(GameInfo.Strings.wallpapernames[dp[i]], choices[i + 16].Text);
            Assert.Equal(LocalizedStrings.Instance.Format("BoxLayoutEditor_PlatinumWallpaper", GameInfo.Strings.wallpapernames[pt[i]]), choices[i + 24].Text);
            Assert.Equal(i + 24, choices[i + 24].Value);
        }
    }

    [Theory]
    [InlineData("swsh", 19)] [InlineData("pla", 19)]
    [InlineData("sv", 20)] [InlineData("za", 20)]
    public void ModernGamesUseNumberedWallpapersRatherThanUnrelatedOlderNames(string game, int count)
    {
        var choices = BoxWallpaperChoices.Create(CreateSave(game));
        Assert.Equal(count, choices.Count);
        for (int id = 0; id < choices.Count; id++)
        {
            Assert.Equal(id, choices[id].Value);
            Assert.Equal(LocalizedStrings.Instance.Format("BoxLayoutEditor_WallpaperNumber", id + 1), choices[id].Text);
        }
    }

    [Theory]
    [InlineData("gen3_ruby.sav")] [InlineData("gen3_emerald.sav")] [InlineData("gen3_firered.sav")]
    [InlineData("gen4_diamond.sav")] [InlineData("gen4_platinum.sav")] [InlineData("gen4_heartgold.sav")]
    [InlineData("gen5_black.sav")] [InlineData("gen5_white2.sav")]
    [InlineData("gen6_x.main")] [InlineData("gen7_sun.main")]
    [InlineData("gen8_sword_ct_public.main")] [InlineData("gen8b_brilliantdiamond.bin")]
    [InlineData("gen8a_legendsarceus.main")] [InlineData("gen9_violet.main")]
    [InlineData("gen9a_legendsza.main")]
    public void OpenCancelAndUnchangedSavePreserveEverySourceByte(string file)
    {
        var save = LoadSave(file);
        // Include unexported edits in the separate Gen 3 buffers to detect serialization.
        ((IBoxDetailWallpaper)save).SetBoxWallpaper(0, 3);
        var before = Snapshot(save);
        save.State.Edited = false;
        using (var vm = new BoxLayoutEditorViewModel(save))
        {
            AssertSnapshot(save, before);
            vm.Boxes[0].Wallpaper = -1;
            vm.Boxes[0].SelectedWallpaper = null;
            vm.SaveCommand.Execute(null);
            AssertSnapshot(save, before);
            Assert.False(save.State.Edited);
        }
        using var canceled = new BoxLayoutEditorViewModel(save);
        canceled.Boxes[0].Wallpaper = 1;
        canceled.CancelCommand.Execute(null);
        AssertSnapshot(save, before);
        Assert.False(save.State.Edited);
    }

    [Theory]
    [InlineData("gen3_emerald.sav", 16)]
    [InlineData("gen4_heartgold.sav", 23)]
    [InlineData("gen5_white2.sav", 22)]
    [InlineData("gen8b_brilliantdiamond.bin", 31)]
    [InlineData("gen9_violet.main", 19)]
    public void ChoosingWallpaperCommitsTheRawIdAndSurvivesExportReopen(string file, int id)
    {
        var save = LoadSave(file);
        var before = ((IBoxDetailWallpaper)save).GetBoxWallpaper(0);
        using var vm = new BoxLayoutEditorViewModel(save);
        vm.Boxes[0].SelectedWallpaper = vm.Boxes[0].WallpaperChoices.Single(choice => choice.Value == id);
        Assert.Equal(before, ((IBoxDetailWallpaper)save).GetBoxWallpaper(0));
        vm.SaveCommand.Execute(null);
        Assert.Equal(id, ((IBoxDetailWallpaper)save).GetBoxWallpaper(0));
        var reopened = Assert.IsAssignableFrom<IBoxDetailWallpaper>(SaveUtil.GetSaveFile(save.Write(BinaryExportSetting.None).ToArray()));
        Assert.Equal(id, reopened.GetBoxWallpaper(0));
    }

    [Fact]
    public void RsboxMyWallpaperUsesIdTwentyAndUpdatesBothHalves()
    {
        var save = new SAV3RSBox();
        using var vm = new BoxLayoutEditorViewModel(save);
        vm.Boxes[0].SelectedWallpaper = vm.Boxes[0].WallpaperChoices.Single(choice => choice.Value == 20);
        Assert.Equal(20, vm.Boxes[1].Wallpaper);
        Assert.Equal(0, save.GetBoxWallpaper(0));
        vm.SaveCommand.Execute(null);
        Assert.Equal(20, save.GetBoxWallpaper(0));
        Assert.Equal(20, save.GetBoxWallpaper(1));
    }

    [Fact]
    public void BlankJapaneseRsboxRetainsItsNameEncoding()
    {
        var save = new SAV3RSBox(japanese: true);
        using var vm = new BoxLayoutEditorViewModel(save);
        vm.Boxes[0].Name = "あいうえお";
        vm.SaveCommand.Execute(null);
        Assert.EndsWith("あいうえお", save.GetBoxName(0));
    }

    [AvaloniaTheory]
    [InlineData("gen3_emerald.sav")] [InlineData("gen8b_brilliantdiamond.bin")] [InlineData("gen9_violet.main")]
    public async Task RealSelectorRetainsUnknownRawIdsAcrossLanguageAndSourceRefresh(string file)
    {
        var language = LocalizedStrings.Instance.CurrentLanguage;
        var dataLanguage = GameInfo.CurrentLanguage;
        var strings = GameInfo.Strings;
        var filtered = GameInfo.FilteredSources;
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        Window? window = null;
        try
        {
            var save = LoadSave(file);
            ((IBoxDetailWallpaper)save).SetBoxWallpaper(0, 200);
            var before = Snapshot(save);
            save.State.Edited = false;
            using var vm = new BoxLayoutEditorViewModel(save);
            var view = new BoxLayoutEditor { DataContext = vm };
            window = new Window { Content = view, Width = 900, Height = 650 };
            window.Show();
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            var selector = view.GetVisualDescendants().OfType<ComboBox>().Single(control => ReferenceEquals(control.DataContext, vm.Boxes[0]));
            Assert.Equal(200, Assert.IsType<BoxWallpaperChoice>(selector.SelectedItem).Value);
            foreach (var locale in LocalizedStrings.SupportedLanguages)
            {
                new LanguageService().SetLanguage(locale);
                LocalizedStrings.Instance.SetLanguage(locale);
                WeakReferenceMessenger.Default.Send(new LanguageChangedMessage(locale));
                await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
                Assert.Equal(200, Assert.IsType<BoxWallpaperChoice>(selector.SelectedItem).Value);
                Assert.Equal(LocalizedStrings.Instance.Format("BoxLayoutEditor_UnknownWallpaper", 200), Assert.IsType<BoxWallpaperChoice>(selector.SelectedItem).Text);
                Assert.Contains(selector.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == vm.Boxes[0].SelectedWallpaper!.Text);
            }
            selector.SelectedIndex = -1;
            Assert.Equal(200, vm.Boxes[0].Wallpaper);
            vm.SaveCommand.Execute(null);
            AssertSnapshot(save, before);
            Assert.False(save.State.Edited);
        }
        finally
        {
            window?.Close();
            GameInfo.CurrentLanguage = dataLanguage;
            GameInfo.Strings = strings;
            GameInfo.FilteredSources = filtered;
            LocalizedStrings.Instance.SetLanguage(language);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
        }
    }

    private static SaveFile CreateSave(string game) => game switch
    {
        "rs" => new SAV3RS(), "e" => new SAV3E(), "frlg" => new SAV3FRLG(), "rsbox" => new SAV3RSBox(),
        "dp" => new SAV4DP(), "pt" => new SAV4Pt(), "hgss" => new SAV4HGSS(), "bw" => new SAV5BW(),
        "b2w2" => new SAV5B2W2(), "xy" => new SAV6XY(), "sm" => new SAV7SM(),
        "swsh" => new SAV8SWSH(), "pla" => new SAV8LA(), "sv" => new SAV9SV(), "za" => new SAV9ZA(),
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    [AvaloniaTheory]
    [InlineData("gen3_emerald.sav")] [InlineData("gen8b_brilliantdiamond.bin")] [InlineData("gen9_violet.main")]
    public async Task RealSelectorCanRestoreOnlyItsOriginalUnknownWallpaper(string file)
    {
        var save = LoadSave(file);
        var wallpaper = (IBoxDetailWallpaper)save;
        wallpaper.SetBoxWallpaper(0, 200);
        using var vm = new BoxLayoutEditorViewModel(save);
        var view = new BoxLayoutEditor { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 650 };
        try
        {
            window.Show();
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            var selector = view.GetVisualDescendants().OfType<ComboBox>().Single(control => ReferenceEquals(control.DataContext, vm.Boxes[0]));
            var unknown = Assert.IsType<BoxWallpaperChoice>(selector.SelectedItem);
            var known = vm.Boxes[0].WallpaperChoices.Single(choice => choice.Value == 16);
            selector.SelectedItem = known;
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            Assert.Equal(16, vm.Boxes[0].Wallpaper);
            Assert.Same(known, selector.SelectedItem);
            selector.SelectedItem = unknown;
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            Assert.Same(unknown, selector.SelectedItem);
            Assert.Equal(200, vm.Boxes[0].Wallpaper);
            vm.Boxes[0].Wallpaper = 201;
            Assert.Equal(200, vm.Boxes[0].Wallpaper);
            Assert.Equal(200, wallpaper.GetBoxWallpaper(0));
            selector.SelectedItem = known;
            vm.SaveCommand.Execute(null);
            Assert.Equal(16, wallpaper.GetBoxWallpaper(0));
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("gen3_emerald.sav")] [InlineData("gen8b_brilliantdiamond.bin")] [InlineData("gen9_violet.main")]
    public void RestoringAnOriginalUnknownChoicePreservesItsEncodedBytes(string file)
    {
        var save = LoadSave(file);
        ((IBoxDetailWallpaper)save).SetBoxWallpaper(0, 200);
        var before = Snapshot(save);
        using var vm = new BoxLayoutEditorViewModel(save);
        var row = vm.Boxes[0];
        var unknown = row.SelectedWallpaper;
        row.SelectedWallpaper = row.WallpaperChoices.Single(choice => choice.Value == 16);
        row.SelectedWallpaper = unknown;
        vm.SaveCommand.Execute(null);
        AssertSnapshot(save, before);
    }

    [Fact]
    public void RsboxEitherHalfCanRestoreItsOriginalUnknownWallpaper()
    {
        var save = new SAV3RSBox();
        save.SetBoxWallpaper(0, 200);
        var image = save.MyWallpaper.ToArray();
        using var vm = new BoxLayoutEditorViewModel(save);
        var original = vm.Boxes[1].SelectedWallpaper;
        vm.Boxes[0].SelectedWallpaper = vm.Boxes[0].WallpaperChoices.Single(choice => choice.Value == 20);
        Assert.Equal(20, vm.Boxes[1].Wallpaper);
        vm.Boxes[1].SelectedWallpaper = original;
        Assert.Equal(200, vm.Boxes[0].Wallpaper);
        vm.SaveCommand.Execute(null);
        Assert.Equal(200, save.GetBoxWallpaper(0));
        Assert.Equal(200, save.GetBoxWallpaper(1));
        Assert.Equal(image, save.MyWallpaper.ToArray());
    }

    private static SaveFile LoadSave(string file) => Assert.IsAssignableFrom<SaveFile>(SaveFileFixture.LoadSave(Path.Combine(Assert.IsType<string>(SaveFileFixture.FindSaveFilesPath()), file)));

    private static byte[][] Snapshot(SaveFile save) => save switch
    {
        SAV3 gen3 => [save.Data.ToArray(), gen3.Small.ToArray(), gen3.Large.ToArray(), gen3.Storage.ToArray()],
        ISCBlockArray blocks => [save.Data.ToArray(), .. blocks.AllBlocks.Select(BlockSnapshot)],
        _ => [save.Data.ToArray()],
    };

    private static byte[] BlockSnapshot(SCBlock block)
    {
        var data = new byte[block.Data.Length + 6];
        BinaryPrimitives.WriteUInt32LittleEndian(data, block.Key);
        data[4] = (byte)block.Type;
        data[5] = (byte)block.SubType;
        block.Data.CopyTo(data.AsSpan(6));
        return data;
    }

    private static void AssertSnapshot(SaveFile save, byte[][] before)
    {
        var after = Snapshot(save);
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++) Assert.Equal(before[i], after[i]);
    }

    [Fact]
    public void Cancel_DoesNotMutateLiveSave()
    {
        var sav = new SAV4Pt();
        var names = (IBoxDetailName)sav;
        var wallpaper = (IBoxDetailWallpaper)sav;
        names.SetBoxName(0, "Original");
        wallpaper.SetBoxWallpaper(0, 3);
        sav.State.Edited = false;
        var editedBefore = sav.State.Edited;

        var vm = new BoxLayoutEditorViewModel(sav);
        vm.Boxes[0].Name = "Temporary";
        vm.Boxes[0].Wallpaper = 7;
        vm.UnlockedBoxes = Math.Min(sav.BoxCount, 1);
        vm.CancelCommand.Execute(null);

        Assert.Equal("Original", names.GetBoxName(0));
        Assert.Equal(3, wallpaper.GetBoxWallpaper(0));
        Assert.Equal(editedBefore, sav.State.Edited);
    }

    [Fact]
    public void Save_CommitsWorkingCopyAndRejectsInvalidWallpaper()
    {
        var sav = new SAV4Pt();
        var wallpaper = (IBoxDetailWallpaper)sav;
        wallpaper.SetBoxWallpaper(0, 3);
        sav.State.Edited = false;

        var vm = new BoxLayoutEditorViewModel(sav);
        vm.Boxes[0].Name = "Commit";
        vm.Boxes[0].Wallpaper = -1;
        vm.SaveCommand.Execute(null);

        Assert.Equal("Commit", ((IBoxDetailName)sav).GetBoxName(0));
        Assert.Equal(3, wallpaper.GetBoxWallpaper(0));
        Assert.True(sav.State.Edited);
    }

    [Fact]
    public void Reorder_MovesPokemonNamesAndWallpapersTogetherOnSave()
    {
        var sav = new SAV4Pt();
        var names = (IBoxDetailName)sav;
        var wallpapers = (IBoxDetailWallpaper)sav;
        sav.SetBoxSlotAtIndex(new PK4 { Species = (ushort)Species.Bulbasaur, CurrentLevel = 5 }, 0, 0);
        sav.SetBoxSlotAtIndex(new PK4 { Species = (ushort)Species.Charmander, CurrentLevel = 5 }, 1, 0);
        names.SetBoxName(0, "First");
        names.SetBoxName(1, "Second");
        wallpapers.SetBoxWallpaper(0, 2);
        wallpapers.SetBoxWallpaper(1, 7);

        var vm = new BoxLayoutEditorViewModel(sav);
        vm.MoveDownCommand.Execute(vm.Boxes[0]);

        Assert.Equal("Second", vm.Boxes[0].Name);
        Assert.Equal("First", vm.Boxes[1].Name);
        Assert.Equal(7, vm.Boxes[0].Wallpaper);
        Assert.Equal(2, vm.Boxes[1].Wallpaper);

        vm.SaveCommand.Execute(null);

        Assert.Equal((ushort)Species.Charmander, sav.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal((ushort)Species.Bulbasaur, sav.GetBoxSlotAtIndex(1, 0).Species);
        Assert.Equal("Second", names.GetBoxName(0));
        Assert.Equal("First", names.GetBoxName(1));
        Assert.Equal(7, wallpapers.GetBoxWallpaper(0));
        Assert.Equal(2, wallpapers.GetBoxWallpaper(1));
    }
}
