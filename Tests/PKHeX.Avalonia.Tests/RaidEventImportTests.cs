using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PKHeX.Avalonia.Views;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Raid-event package import against unchanged public Events Gallery packages (see Fixtures/RaidEvents/README.md)
/// and the committed public save fixtures. Variants (subsets, renames, truncation) are derived in temp folders.
/// </summary>
public sealed class RaidEventImportTests : IDisposable
{
    private static readonly string FixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Fixtures/RaidEvents"));
    private static readonly string SwshPackage = Path.Combine(FixtureRoot, "swsh-index-117");
    private static readonly string SvPackage = Path.Combine(FixtureRoot, "sv-148-meowscarada-r2");
    private readonly List<string> _temp = [];

    private static T LoadSave<T>(string name) where T : SaveFile
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../savefiles", name));
        var save = Assert.IsType<T>(SaveUtil.GetSaveFile(path));
        save.State.Edited = false;
        return save;
    }

    private static Dictionary<uint, byte[]> Snapshot(ISCBlockArray save) =>
        save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());

    private string Derive(string source, Action<string>? mutate = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pkhex-raid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _temp.Add(folder);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        mutate?.Invoke(folder);
        return folder;
    }

    public void Dispose()
    {
        foreach (var folder in _temp)
            try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    [Fact]
    public void PublicFixtures_AreTheExpectedUnchangedPackages()
    {
        Assert.Equal(RaidEventPackageLayout.SwordShieldCrownTundra, RaidEventPackageReader.ReadFolder(SwshPackage).Package!.Layout);
        var sv = RaidEventPackageReader.ReadFolder(SvPackage).Package!;
        Assert.Equal(RaidEventPackageLayout.ScarletViolet300, sv.Layout);
        Assert.Equal(RaidEventGame.ScarletViolet, sv.Game);
        Assert.Equal(5, sv.Files.Count);
        Assert.NotNull(sv.EventIdentifier);
    }

    [Fact]
    public void SwordShieldCrownTundraPackage_ReplacesOnlyNewsTablesAndSurvivesExportReopen()
    {
        var save = LoadSave<SAV8SWSH>("gen8_sword_ct_public.main");
        var before = Snapshot(save);
        var package = RaidEventPackageReader.ReadFolder(SwshPackage).Package!;
        var plan = RaidEventImportSession.Create(save, package);
        Assert.True(plan.IsValid, plan.Error.ToString());
        Assert.Empty(plan.Session!.Warnings);
        Assert.Equal(6, plan.Session.Blocks.Count);
        Assert.Equal(before, Snapshot(save)); // staging writes nothing
        Assert.False(save.State.Edited);

        Assert.Equal(RaidEventCommitResult.Applied, plan.Session.TryCommit());
        Assert.True(save.State.Edited);
        var keys = package.Files.ToDictionary(f => f.BlockKey, f => f.Data);
        foreach (var block in save.AllBlocks)
            Assert.Equal(keys.TryGetValue(block.Key, out var data) ? data : before[block.Key], block.Data.ToArray());

        var reopened = Assert.IsType<SAV8SWSH>(SaveUtil.GetSaveFile(save.Write()));
        foreach (var file in package.Files)
            Assert.Equal(file.Data, reopened.AllBlocks.Single(b => b.Key == file.BlockKey).Data.ToArray());
        Assert.Equal(save.RaidGalar.GetRaid(0).Seed, reopened.RaidGalar.GetRaid(0).Seed);
    }

    [Fact]
    public void ScarletVioletPackage_ImportsIntoIndigoDiskSaveAndSurvivesExportReopen()
    {
        var save = LoadSave<SAV9SV>("gen9_violet_indigo_public.main");
        var before = Snapshot(save);
        var package = RaidEventPackageReader.ReadFolder(SvPackage).Package!;
        var plan = RaidEventImportSession.Create(save, package);
        Assert.True(plan.IsValid, plan.Error.ToString());
        Assert.Empty(plan.Session!.Warnings);
        Assert.Equal(RaidEventCommitResult.Applied, plan.Session.TryCommit());

        var keys = package.Files.ToDictionary(f => f.BlockKey, f => f.Data);
        foreach (var block in save.AllBlocks)
            Assert.Equal(keys.TryGetValue(block.Key, out var data) ? data : before[block.Key], block.Data.ToArray());
        var reopened = Assert.IsType<SAV9SV>(SaveUtil.GetSaveFile(save.Write()));
        Assert.Equal(2, reopened.SaveRevision);
        foreach (var file in package.Files)
            Assert.Equal(file.Data, reopened.AllBlocks.Single(b => b.Key == file.BlockKey).Data.ToArray());
    }

    [Theory]
    [InlineData("gen9_scarlet.main")]
    [InlineData("gen9_scarlet_teal_public.main")]
    public void ScarletVioletPackage_NewerThanSaveIsRejected(string name)
    {
        var save = LoadSave<SAV9SV>(name);
        var plan = RaidEventImportSession.Create(save, RaidEventPackageReader.ReadFolder(SvPackage).Package!);
        Assert.Equal(RaidEventPackageError.PackageNewerThanSave, plan.Error);
        Assert.Null(plan.Session);
    }

    [Theory]
    [InlineData("", RaidEventPackageLayout.ScarletVioletUnversioned)]
    [InlineData("_1_3_0", RaidEventPackageLayout.ScarletViolet130)]
    [InlineData("_2_0_0", RaidEventPackageLayout.ScarletViolet200)]
    public void OlderScarletVioletLayout_RequiresAcknowledgedWarningOnNewerSave(string suffix, RaidEventPackageLayout layout)
    {
        var folder = Derive(SvPackage, f =>
        {
            foreach (var file in Directory.GetFiles(f))
                File.Move(file, Path.Combine(f, Path.GetFileName(file).Replace("_3_0_0", suffix)));
        });
        var package = RaidEventPackageReader.ReadFolder(folder).Package!;
        Assert.Equal(layout, package.Layout);
        var plan = RaidEventImportSession.Create(LoadSave<SAV9SV>("gen9_violet_indigo_public.main"), package);
        Assert.Equal([RaidEventImportWarning.OlderPackageFormat], plan.Session!.Warnings);
    }

    [Fact]
    public void OlderSwordShieldLayout_LeavesDlcTablesUntouchedAndWarns()
    {
        var folder = Derive(SwshPackage, f =>
        {
            File.Delete(Path.Combine(f, "normal_encount_rigel1"));
            File.Delete(Path.Combine(f, "normal_encount_rigel2"));
        });
        var save = LoadSave<SAV8SWSH>("gen8_sword_ct_public.main");
        var before = Snapshot(save);
        var package = RaidEventPackageReader.ReadFolder(folder).Package!;
        Assert.Equal(RaidEventPackageLayout.SwordShieldBase, package.Layout);
        var session = RaidEventImportSession.Create(save, package).Session!;
        Assert.Equal([RaidEventImportWarning.DlcTablesNotInPackage], session.Warnings);
        Assert.Equal(2, session.Blocks.Count(b => b.Change == RaidEventBlockChange.NotInPackage));

        Assert.Equal(RaidEventCommitResult.Applied, session.TryCommit());
        foreach (var (_, key) in RaidEventPackageReader.SwordShieldDlcTables)
            Assert.Equal(before[key], save.AllBlocks.Single(b => b.Key == key).Data.ToArray());
    }

    [Fact]
    public void IsleOfArmorLayout_IsIdentified()
    {
        var folder = Derive(SwshPackage, f => File.Delete(Path.Combine(f, "normal_encount_rigel2")));
        Assert.Equal(RaidEventPackageLayout.SwordShieldIsleOfArmor, RaidEventPackageReader.ReadFolder(folder).Package!.Layout);
    }

    public static TheoryData<string, RaidEventPackageError> MalformedSwordShield => new()
    {
        { "rigel1-missing", RaidEventPackageError.MissingFile },
        { "base-missing", RaidEventPackageError.MissingFile },
        { "truncated", RaidEventPackageError.WrongLength },
        { "extended", RaidEventPackageError.WrongLength },
        { "unknown", RaidEventPackageError.UnknownFile },
        { "mixed", RaidEventPackageError.MixedGames },
        { "empty", RaidEventPackageError.Empty },
    };

    [Theory]
    [MemberData(nameof(MalformedSwordShield))]
    public void MalformedSwordShieldPackages_AreRejectedBeforeAnySaveIsInvolved(string kind, RaidEventPackageError expected)
    {
        var folder = Derive(SwshPackage, f =>
        {
            switch (kind)
            {
                case "rigel1-missing": File.Delete(Path.Combine(f, "normal_encount_rigel1")); break;
                case "base-missing": File.Delete(Path.Combine(f, "dai_encount")); break;
                case "truncated":
                    var data = File.ReadAllBytes(Path.Combine(f, "drop_rewards"));
                    File.WriteAllBytes(Path.Combine(f, "drop_rewards"), data[..^1]); break;
                case "extended":
                    File.WriteAllBytes(Path.Combine(f, "bonus_rewards"), [.. File.ReadAllBytes(Path.Combine(f, "bonus_rewards")), 0]); break;
                case "unknown": File.WriteAllBytes(Path.Combine(f, "notes.bin"), [1]); break;
                case "mixed": File.Copy(Path.Combine(SvPackage, "raid_enemy_array_3_0_0"), Path.Combine(f, "raid_enemy_array_3_0_0")); break;
                case "empty": foreach (var file in Directory.GetFiles(f)) File.Delete(file); break;
            }
        });
        var result = RaidEventPackageReader.ReadFolder(folder);
        Assert.Equal(expected, result.Error);
        Assert.Null(result.Package);
    }

    [Theory]
    [InlineData("mixed-versions", RaidEventPackageError.MixedVersions)]
    [InlineData("unsupported", RaidEventPackageError.UnsupportedVersion)]
    [InlineData("missing", RaidEventPackageError.MissingFile)]
    [InlineData("wrong-size", RaidEventPackageError.WrongLength)]
    public void MalformedScarletVioletPackages_AreRejected(string kind, RaidEventPackageError expected)
    {
        var folder = Derive(SvPackage, f =>
        {
            switch (kind)
            {
                // Mirrors the gallery folder that ships both raid_priority_array_2_0_0 and _3_0_0.
                case "mixed-versions": File.Copy(Path.Combine(f, "raid_priority_array_3_0_0"), Path.Combine(f, "raid_priority_array_2_0_0")); break;
                case "unsupported":
                    foreach (var file in Directory.GetFiles(f))
                        File.Move(file, Path.Combine(f, Path.GetFileName(file).Replace("_3_0_0", "_4_0_0")));
                    break;
                case "missing": File.Delete(Path.Combine(f, "event_raid_identifier_3_0_0")); break;
                // Mirrors the gallery 1.3.0 lottery table published at the fixed-reward size.
                case "wrong-size": File.WriteAllBytes(Path.Combine(f, "lottery_reward_item_array_3_0_0"), new byte[27456]); break;
            }
        });
        Assert.Equal(expected, RaidEventPackageReader.ReadFolder(folder).Error);
    }

    [Fact]
    public void SidecarsAreIgnoredAndGalleryFilesSubfolderIsFollowed()
    {
        var parent = Derive(SvPackage, f =>
        {
            var files = Path.Combine(f, "Files");
            Directory.CreateDirectory(files);
            foreach (var file in Directory.GetFiles(f)) File.Move(file, Path.Combine(files, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(files, "raid_enemy_array_3_0_0.json"), "{}");
            File.WriteAllText(Path.Combine(files, ".DS_Store"), "x");
        });
        var result = RaidEventPackageReader.ReadFolder(parent);
        Assert.True(result.IsValid, result.Error.ToString());
        Assert.EndsWith("Files", result.Package!.Folder);
    }

    [Fact]
    public void WrongGameAndUnsupportedSavesAreRejected()
    {
        var swsh = RaidEventPackageReader.ReadFolder(SwshPackage).Package!;
        var sv = RaidEventPackageReader.ReadFolder(SvPackage).Package!;
        Assert.Equal(RaidEventPackageError.WrongGame, RaidEventImportSession.Create(LoadSave<SAV9SV>("gen9_violet_indigo_public.main"), swsh).Error);
        Assert.Equal(RaidEventPackageError.WrongGame, RaidEventImportSession.Create(LoadSave<SAV8SWSH>("gen8_sword_ct_public.main"), sv).Error);
        Assert.Equal(RaidEventPackageError.WrongGame, RaidEventImportSession.Create(new SAV8LA(), sv).Error);
    }

    [Fact]
    public void ConflictingLiveEdit_RejectsWholeCommitWithoutPartialWrites()
    {
        var save = LoadSave<SAV8SWSH>("gen8_sword_ct_public.main");
        var package = RaidEventPackageReader.ReadFolder(SwshPackage).Package!;
        var session = RaidEventImportSession.Create(save, package).Session!;
        // Change the LAST table in the package so a non-atomic writer would already have written the earlier ones.
        var last = save.AllBlocks.Single(b => b.Key == package.Files[^1].BlockKey);
        last.Data[0] ^= 0xFF;
        var before = Snapshot(save);

        Assert.Equal(RaidEventCommitResult.Conflict, session.TryCommit());
        Assert.Equal(before, Snapshot(save));
        Assert.False(save.State.Edited);
    }

    [Fact]
    public void ReimportingIdenticalPackage_IsNoOpAndKeepsEditedFlag()
    {
        var save = LoadSave<SAV9SV>("gen9_violet_indigo_public.main");
        var package = RaidEventPackageReader.ReadFolder(SvPackage).Package!;
        Assert.Equal(RaidEventCommitResult.Applied, RaidEventImportSession.Create(save, package).Session!.TryCommit());
        save.State.Edited = false;
        var before = Snapshot(save);

        var again = RaidEventImportSession.Create(save, package).Session!;
        Assert.False(again.HasChanges);
        Assert.All(again.Blocks, b => Assert.Equal(RaidEventBlockChange.Identical, b.Change));
        Assert.Equal(RaidEventCommitResult.NoChanges, again.TryCommit());
        Assert.Equal(before, Snapshot(save));
        Assert.False(save.State.Edited);
    }

    [AvaloniaFact]
    public void ViewModel_CancelDiscardsStagedPackage()
    {
        var save = LoadSave<SAV8SWSH>("gen8_sword_ct_public.main");
        var before = Snapshot(save);
        using var vm = new RaidEventImportViewModel(save);
        var closed = false;
        vm.CloseRequested = () => closed = true;
        vm.LoadPackage(SwshPackage);
        Assert.True(vm.IsStaged);
        Assert.True(vm.CanApply);
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(before, Snapshot(save));
        Assert.False(save.State.Edited);
    }

    [AvaloniaFact]
    public void ViewModel_WarningsGateApplyUntilAcknowledged()
    {
        var folder = Derive(SwshPackage, f => File.Delete(Path.Combine(f, "normal_encount_rigel2")));
        var save = LoadSave<SAV8SWSH>("gen8_sword_ct_public.main");
        using var vm = new RaidEventImportViewModel(save);
        vm.LoadPackage(folder);
        Assert.True(vm.RequiresAcknowledgement);
        Assert.Single(vm.Warnings);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        vm.IsAcknowledged = true;
        Assert.True(vm.ApplyCommand.CanExecute(null));
        vm.ApplyCommand.Execute(null);
        Assert.True(save.State.Edited);
    }

    [AvaloniaFact]
    public void ViewModel_ConflictDropsStagedPackageAndReportsIt()
    {
        var save = LoadSave<SAV9SV>("gen9_violet_indigo_public.main");
        using var vm = new RaidEventImportViewModel(save);
        var closed = false;
        vm.CloseRequested = () => closed = true;
        vm.LoadPackage(SvPackage);
        save.AllBlocks.Single(b => b.Key == 0x0520A1B0).Data[10] ^= 1;
        vm.ApplyCommand.Execute(null);
        Assert.False(closed);
        Assert.False(vm.IsStaged);
        Assert.True(vm.IsError);
        Assert.Equal(LocalizedStrings.Instance["RaidEventImport_Conflict"], vm.StatusText);
    }

    [AvaloniaFact]
    public void ViewModel_ErrorsUseLocalizedMessages()
    {
        using var vm = new RaidEventImportViewModel(LoadSave<SAV9SV>("gen9_scarlet.main"));
        vm.LoadPackage(SvPackage);
        Assert.False(vm.IsStaged);
        Assert.Equal(LocalizedStrings.Instance["RaidEventImport_Error_PackageNewerThanSave"], vm.StatusText);
        foreach (var error in Enum.GetValues<RaidEventPackageError>().Where(e => e != RaidEventPackageError.None))
            Assert.NotEqual($"RaidEventImport_Error_{error}", LocalizedStrings.Instance[$"RaidEventImport_Error_{error}"]);
        foreach (var layout in Enum.GetValues<RaidEventPackageLayout>())
            Assert.NotEqual($"RaidEventImport_Layout_{layout}", LocalizedStrings.Instance[$"RaidEventImport_Layout_{layout}"]);
    }

    [AvaloniaFact]
    public void MenuActionFollowsRaidPackageCapability()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(LoadSave<SAV9SV>("gen9_violet_indigo_public.main"));
        var actions = app.ViewModel.ToolMenuGroups.SelectMany(group => group.Items)
            .Where(item => item.Title == LocalizedStrings.Instance["Dialog_RaidEventImport"]).ToList();
        Assert.Equal(2, actions.Count);
        Assert.Single(actions, a => a.IsAvailable);

        app.LoadSaveInstance(LoadSave<SAV8SWSH>("gen8_sword_ct_public.main"));
        Assert.Single(actions, a => a.IsAvailable);

        app.LoadSaveInstance(new SAV6XY());
        Assert.DoesNotContain(actions, a => a.IsAvailable);
    }

    [AvaloniaTheory]
    [InlineData(640, 540)]
    [InlineData(420, 360)]
    public void View_ShowsStagedTablesAndKeepsFooterActionsReachable(double width, double height)
    {
        using var app = new HeadlessAppFixture();
        var save = LoadSave<SAV8SWSH>("gen8_sword_ct_public.main");
        var folder = Derive(SwshPackage, f => File.Delete(Path.Combine(f, "normal_encount_rigel2")));
        using var vm = new RaidEventImportViewModel(save);
        vm.LoadPackage(folder);
        var view = new RaidEventImportView { DataContext = vm, Width = width, Height = height };
        app.Window.Content = view;
        app.Window.Width = width;
        app.Window.Height = height;
        app.Pump();

        var texts = app.Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("normal_encount_rigel1", texts);
        Assert.Contains(LocalizedStrings.Instance["RaidEventImport_Change_NotInPackage"], texts);
        var buttons = app.Window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        var apply = Assert.Single(buttons, b => Equals(b.Content, LocalizedStrings.Instance["RaidEventImport_Apply"]));
        var check = Assert.Single(app.Window.GetVisualDescendants().OfType<CheckBox>(), c => c.IsEffectivelyVisible);
        foreach (var control in new Control[] { apply, check })
        {
            var origin = control.TranslatePoint(default, view)!.Value;
            Assert.True(origin.Y >= 0 && origin.Y + control.Bounds.Height <= height + 0.5, $"{control} clipped at {origin}");
            Assert.True(origin.X >= 0 && origin.X + control.Bounds.Width <= width + 0.5, $"{control} clipped at {origin}");
        }
    }
}
