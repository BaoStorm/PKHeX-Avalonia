using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Gen 4 battle video editor. The committed Pt/HGSS fixtures have no initialized video slots, so tests seed a
/// synthetic, checksum-valid video block and its General-block key through public Core APIs. This is storage-level
/// evidence only; no genuine in-game video is claimed.
/// </summary>
public class BattleVideo4EditorTests
{
    private const int VideoBlockLength = 0x1D60;

    private static T LoadSave<T>(string name) where T : SAV4
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../savefiles", name));
        var save = Assert.IsType<T>(SaveUtil.GetSaveFile(path));
        save.State.Edited = false;
        return save;
    }

    private static (int VideoOffset, int Extra) Layout(SAV4 save) => save switch
    {
        SAV4Pt => (0x24000, 0x2820),
        SAV4HGSS => (0x27000, 0x230C),
        _ => throw new ArgumentException("No battle video blocks"),
    };

    private static PK4 Member(ushort species, string nickname, string ot, byte level)
    {
        var pk = new PK4 { Species = species, OriginalTrainerName = ot, Language = (int)LanguageID.English };
        pk.Nickname = nickname;
        pk.IsNicknamed = true;
        pk.CurrentLevel = level;
        return pk;
    }

    /// <summary>Builds an encrypted .bv4 payload with valid seed and footer checksums.</summary>
    private static byte[] MakeVideo(ushort blockId, uint key, params PK4[][] teams)
    {
        var data = new byte[BattleVideo4.SIZE_USED];
        var video = new BattleVideo4(data) { IsDecrypted = true };
        video.Key = key;
        for (int t = 0; t < teams.Length; t++)
        {
            var span = video.GetTrainerTeam(t);
            span[0] = 6;
            span[2] = (byte)teams[t].Length;
            for (int i = 0; i < teams[t].Length; i++)
            {
                var slot = span.Slice(4 + (i * BattleVideo4.SizeVideoPoke), BattleVideo4.SizeVideoPoke);
                BattleVideo4.DeflateFromPK4(teams[t][i].Data, slot);
                // Core's DeflateFromPK4 omits species/held item (InflateToPK4 reads them from 0x6..0xA).
                teams[t][i].Data[8..0xC].CopyTo(slot[6..0xA]);
            }
        }
        video.Magic = 0x20060623;
        video.BlockSize = 0x1D50;
        video.BlockID = blockId;
        video.RefreshChecksums();
        video.Encrypt();
        Assert.Equal(BattleVideo4DecryptionState.Encrypted, BattleVideo4.DetectEncryption(data.ToArray()));
        return data;
    }

    /// <summary>Writes a video into partition 0 and points the General-block key at it so Core treats the slot as active.</summary>
    private static void Seed(SAV4 save, int slot, byte[] video)
    {
        var (offset, extra) = Layout(save);
        var block = offset + (slot * 0x2000);
        video.CopyTo(save.Data[block..]);
        var key = BinaryPrimitives.ReadUInt32LittleEndian(video);
        const int otherBlocks = 5; // ExtraBlocks.Count - 1
        BinaryPrimitives.WriteUInt32LittleEndian(save.General[(extra + (4 * (1 + slot)))..], key);
        save.General[extra + (2 * 4 * otherBlocks) + 1 + slot] = 0; // prefer the first partition
        Assert.NotNull(save.GetBattleVideo(slot));
    }

    private static T SeededSave<T>(string name) where T : SAV4
    {
        var save = LoadSave<T>(name);
        Seed(save, 0, MakeVideo(2, 0x11111111,
            [Member(25, "SPARKY", "ADA", 50), Member(6, "BLAZE", "ADA", 55)],
            [Member(9, "TANK", "BOB", 52)]));
        Seed(save, 1, MakeVideo(3, 0x22222222, [Member(150, "MEW2", "CY", 70)], [Member(151, "MEW", "DEE", 30)]));
        save.State.Edited = false;
        return save;
    }

    private static byte[] Snapshot(SAV4 save) => save.Data.ToArray();

    [Fact]
    public void DiamondPearlAndUninitializedSlotsAreUnavailable()
    {
        Assert.False(BattleVideo4Session.IsSupported(LoadSave<SAV4DP>("gen4_diamond.sav")));
        var session = new BattleVideo4Session(LoadSave<SAV4Pt>("gen4_platinum.sav"));
        Assert.All(session.Slots, s => Assert.False(s.IsAvailable));
        Assert.Equal(BattleVideo4ImportError.SlotUnavailable, session.Import(0, new byte[BattleVideo4.SIZE_USED]));
    }

    [Theory]
    [InlineData("gen4_platinum.sav")]
    [InlineData("gen4_heartgold.sav")]
    public void PreviewShowsTrainersTeamsAndStatus(string name)
    {
        var save = name.Contains("platinum") ? (SAV4)SeededSave<SAV4Pt>(name) : SeededSave<SAV4HGSS>(name);
        using var vm = new BattleVideo4EditorViewModel(save);
        Assert.Equal($"01 - ADA{LocalizedStrings.Instance["BattleVideo4_Versus"]}BOB", vm.Slots[0].Label);
        Assert.False(vm.Slots[2].IsAvailable);
        Assert.Equal(2, vm.Players.Count);
        Assert.Equal(["SPARKY", "BLAZE"], vm.Players[0].Members.Select(m => m.Nickname));
        Assert.Equal(GameInfo.Strings.Species[25], vm.Players[0].Members[0].Species);
        Assert.Equal(50, vm.Players[0].Members[0].Level);
        Assert.Contains("0x0002", vm.SelectedStatus);
        Assert.Contains(LocalizedStrings.Instance["BattleVideo4_ChecksumValid"], vm.SelectedStatus);

        vm.SelectedSlot = vm.Slots[3];
        Assert.False(vm.HasSelectedVideo);
        Assert.Empty(vm.Players);
        Assert.False(vm.ImportCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportImportRoundTripKeepsDestinationMetadataAndSurvivesSaveReopen(bool decrypted)
    {
        var save = SeededSave<SAV4HGSS>("gen4_heartgold.sav");
        using var vm = new BattleVideo4EditorViewModel(save) { ExportDecrypted = decrypted };
        var exported = vm.GetExportData(1)!;
        Assert.Equal(decrypted ? BattleVideo4DecryptionState.Decrypted : BattleVideo4DecryptionState.Encrypted,
            BattleVideo4.DetectEncryption(exported.ToArray()));
        var before = Snapshot(save);

        Assert.Equal(BattleVideo4ImportError.None, vm.ImportData(0, exported));
        Assert.Equal(before, Snapshot(save)); // staged only
        Assert.True(vm.Slots[0].IsChanged);
        Assert.EndsWith(" *", vm.Slots[0].Label);
        Assert.True(vm.SaveCommand.CanExecute(null));

        vm.SaveCommand.Execute(null);
        Assert.True(save.State.Edited);
        var live = save.GetBattleVideo(0)!;
        Assert.Equal(2, live.BlockID); // slot keeps its own block ID, not the source's 3
        Assert.Equal(0x11111111u, live.Key); // and its key, or Core would treat the slot as empty
        Assert.True(live.ChecksumValid);
        Assert.Equal(["CY", "DEE"], live.GetTrainerNames().Where(n => n.Length != 0));

        var reopened = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(save.Write()));
        Assert.Equal(live.Data.ToArray(), reopened.GetBattleVideo(0)!.Data.ToArray());
        Assert.Equal(save.GetBattleVideo(1)!.Data.ToArray(), reopened.GetBattleVideo(1)!.Data.ToArray());
    }

    [Theory]
    [InlineData(0, BattleVideo4ImportError.InvalidSize)]
    [InlineData(1, BattleVideo4ImportError.InvalidSize)]
    [InlineData(2, BattleVideo4ImportError.InvalidData)]
    public void InvalidFilesAreRejectedWithoutStaging(int kind, BattleVideo4ImportError expected)
    {
        var save = SeededSave<SAV4Pt>("gen4_platinum.sav");
        using var vm = new BattleVideo4EditorViewModel(save);
        var good = vm.GetExportData(1)!;
        var file = kind switch
        {
            0 => good[..^1],
            1 => [.. good, 0],
            _ => Corrupt(good),
        };
        Assert.Equal(expected, vm.ImportData(0, file));
        Assert.False(vm.Slots[0].IsChanged);
        Assert.True(vm.IsMessageError);
        Assert.False(vm.SaveCommand.CanExecute(null));

        static byte[] Corrupt(byte[] data) { var copy = data.ToArray(); copy[0x1300] ^= 0xFF; return copy; }
    }

    [Fact]
    public void CancelAndResetDiscardStagedImports()
    {
        var save = SeededSave<SAV4Pt>("gen4_platinum.sav");
        var before = Snapshot(save);
        using var vm = new BattleVideo4EditorViewModel(save);
        vm.ImportData(0, vm.GetExportData(1)!);
        vm.ResetSlotCommand.Execute(null);
        Assert.False(vm.Slots[0].IsChanged);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.ImportData(0, vm.GetExportData(1)!);
        var closed = false;
        vm.CloseRequested = () => closed = true;
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(before, Snapshot(save));
        Assert.False(save.State.Edited);
    }

    [Fact]
    public void SaveWritesOnlyChangedSlotAndPreservesUnrelatedLiveEdits()
    {
        var save = SeededSave<SAV4Pt>("gen4_platinum.sav");
        using var vm = new BattleVideo4EditorViewModel(save);
        vm.ImportData(0, vm.GetExportData(1)!);
        save.Money = 123456; // independent live edit made while the editor is open
        var before = Snapshot(save);
        vm.SaveCommand.Execute(null);

        Assert.Equal(123456u, save.Money);
        var (offset, _) = Layout(save);
        var after = save.Data.ToArray();
        for (int i = 0; i < after.Length; i++)
            if (i < offset || i >= offset + VideoBlockLength) Assert.True(before[i] == after[i], $"byte 0x{i:X} changed");
    }

    [Fact]
    public void ConflictingLiveSlotChangeRejectsSaveWithoutPartialWrites()
    {
        var save = SeededSave<SAV4Pt>("gen4_platinum.sav");
        using var vm = new BattleVideo4EditorViewModel(save);
        Assert.Equal(BattleVideo4ImportError.None, vm.ImportData(0, vm.GetExportData(1)!));
        Assert.Equal(BattleVideo4ImportError.None, vm.ImportData(1, MakeVideo(9, 0x33333333, [Member(1, "SEED", "EVE", 5)])));
        Assert.True(vm.Slots[1].IsChanged);
        save.GetBattleVideo(1)!.Data[0x20] ^= 1; // later slot changed after staging
        var before = Snapshot(save);
        var closed = false;
        vm.CloseRequested = () => closed = true;

        vm.SaveCommand.Execute(null);
        Assert.False(closed);
        Assert.Equal(before, Snapshot(save));
        Assert.False(save.State.Edited);
        Assert.Equal(LocalizedStrings.Instance["BattleVideo4_Conflict"], vm.Message);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void MenuActionFollowsBattleVideoCapability()
    {
        using var app = new HeadlessAppFixture();
        app.LoadSaveInstance(LoadSave<SAV4Pt>("gen4_platinum.sav"));
        var action = Assert.Single(app.ViewModel.ToolMenuGroups.SelectMany(group => group.Items),
            item => item.Title == LocalizedStrings.Instance["Dialog_BattleVideo4"]);
        Assert.True(action.IsAvailable);
        app.LoadSaveInstance(LoadSave<SAV4HGSS>("gen4_heartgold.sav"));
        Assert.True(action.IsAvailable);
        app.LoadSaveInstance(LoadSave<SAV4DP>("gen4_diamond.sav"));
        Assert.False(action.IsAvailable);
    }

    [AvaloniaTheory]
    [InlineData(760, 560)]
    [InlineData(460, 380)]
    public void ViewShowsTeamsAndKeepsActionsReachable(double width, double height)
    {
        using var app = new HeadlessAppFixture();
        using var vm = new BattleVideo4EditorViewModel(SeededSave<SAV4Pt>("gen4_platinum.sav"));
        var view = new BattleVideo4EditorView { DataContext = vm, Width = width, Height = height };
        app.Window.Content = view;
        app.Window.Width = width;
        app.Window.Height = height;
        app.Pump();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("SPARKY", texts);
        var buttons = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        foreach (var key in new[] { "Common_Save", "Common_Cancel", "BattleVideo4_Import", "BattleVideo4_Export" })
        {
            var button = Assert.Single(buttons, b => Equals(b.Content, LocalizedStrings.Instance[key]));
            var origin = button.TranslatePoint(default, view)!.Value;
            Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= width + 0.5, $"{key} clipped at {origin}");
            Assert.True(origin.Y >= 0 && origin.Y + button.Bounds.Height <= height + 0.5, $"{key} clipped at {origin}");
        }
    }
}
