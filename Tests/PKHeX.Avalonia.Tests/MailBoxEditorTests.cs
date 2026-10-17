using System.Buffers.Binary;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public sealed class MailBoxEditorTests
{
    private static readonly string[] Gen3Files = ["gen3_ruby.sav", "gen3_emerald.sav", "gen3_firered.sav"];
    private const int PcMailIndex = 6;

    public static IEnumerable<object[]> UnownForms =>
        from file in Gen3Files from form in Enumerable.Range(0, 28) select new object[] { file, form };

    [Theory, MemberData(nameof(UnownForms))]
    public void EveryGen3UnownFormLoadsAndSurvivesUnrelatedEdits(string file, int form)
    {
        var save = Assert.IsAssignableFrom<SAV3>(LoadSave(file));
        var appearance = (ushort)(30000 + form);
        SeedGen3Mail(save, appearance);
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];

        var selected = vm.Species[vm.SelectedSpeciesIndex];
        Assert.Equal(appearance, selected.Value);
        var formName = form < 26 ? ((char)('A' + form)).ToString() : form == 26 ? "!" : "?";
        Assert.Equal($"{GameInfo.Strings.Species[(ushort)Species.Unown]} ({formName})", selected.Text);
        Assert.Equal(28, vm.Species.Count(item => item.Value is >= 30000 and <= 30027));
        Assert.DoesNotContain(vm.Species, item => item.Value == SpeciesConverter.GetInternal3((ushort)Species.Unown));

        vm.SaveCommand.Execute(null);
        AssertSnapshot(save, before);
        Assert.False(save.State.Edited);

        vm.AuthorTid ^= 0xFFFF;
        vm.SaveCommand.Execute(null);
        var expected = CloneSnapshot(before);
        BinaryPrimitives.WriteUInt16LittleEndian(expected[2].AsSpan(MailOffset(save) + 0x1A), vm.AuthorTid);
        AssertSnapshot(save, expected);
        Assert.Equal(appearance, save.LargeBlock.GetMail(PcMailIndex).AppearPKM);
    }

    [Theory, MemberData(nameof(UnownForms))]
    public void EveryGen3UnownFormCanBeExplicitlySelected(string file, int form)
    {
        var save = Assert.IsAssignableFrom<SAV3>(LoadSave(file));
        SeedGen3Mail(save, 1);
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        var appearance = 30000 + form;
        vm.SelectedSpeciesIndex = vm.Species.ToList().FindIndex(item => item.Value == appearance);

        AssertSnapshot(save, before); // Edits remain staged until Save.
        vm.SaveCommand.Execute(null);

        var expected = CloneSnapshot(before);
        BinaryPrimitives.WriteUInt16LittleEndian(expected[2].AsSpan(MailOffset(save) + 0x1E), (ushort)appearance);
        AssertSnapshot(save, expected);
        Assert.Equal(appearance, save.LargeBlock.GetMail(PcMailIndex).AppearPKM);
    }

    public static IEnumerable<object[]> OrdinarySpecies =>
        from file in Gen3Files
        from pair in new (ushort National, ushort Internal)[] { (1, 1), (25, 25), (251, 251), (252, 277), (358, 411), (386, 410) }
        select new object[] { file, pair.National, pair.Internal };

    [Theory, MemberData(nameof(OrdinarySpecies))]
    public void Gen3OrdinarySpeciesUseStoredInternalValues(string file, ushort national, ushort stored)
    {
        var save = Assert.IsAssignableFrom<SAV3>(LoadSave(file));
        SeedGen3Mail(save, stored);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        Assert.Equal(stored, vm.Species[vm.SelectedSpeciesIndex].Value);
        Assert.Equal(GameInfo.Strings.Species[national], vm.Species[vm.SelectedSpeciesIndex].Text);
        vm.SelectedSpeciesIndex = vm.Species.ToList().FindIndex(item => item.Value == 30000);
        vm.SaveCommand.Execute(null);
        vm.SelectedSpeciesIndex = vm.Species.ToList().FindIndex(item => item.Value == stored);
        vm.SaveCommand.Execute(null);
        Assert.Equal(stored, save.LargeBlock.GetMail(PcMailIndex).AppearPKM);
    }

    public static IEnumerable<object[]> UnknownAndEmptyAppearances =>
        from file in Gen3Files from stored in new ushort[] { 0, 252, 30028, 65000, 65535 }
        select new object[] { file, stored };

    [Theory, MemberData(nameof(UnknownAndEmptyAppearances))]
    public void Gen3UnknownAndEmptyRawValuesArePreserved(string file, ushort stored)
    {
        var save = Assert.IsAssignableFrom<SAV3>(LoadSave(file));
        SeedGen3Mail(save, stored, mailType: stored == 0 ? (ushort)0 : (ushort)121);
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        Assert.Equal(stored, vm.Species[vm.SelectedSpeciesIndex].Value);
        var originalIndex = vm.SelectedSpeciesIndex;
        vm.SelectedSpeciesIndex = vm.Species.ToList().FindIndex(item => item.Value == 25);
        vm.SelectedSpeciesIndex = originalIndex;
        vm.SaveCommand.Execute(null);
        AssertSnapshot(save, before);
        Assert.False(save.State.Edited);

        vm.Message00 ^= 0x1234;
        vm.SaveCommand.Execute(null);
        var expected = CloneSnapshot(before);
        BinaryPrimitives.WriteUInt16LittleEndian(expected[2].AsSpan(MailOffset(save)), vm.Message00);
        AssertSnapshot(save, expected);
        Assert.Equal(stored, save.LargeBlock.GetMail(PcMailIndex).AppearPKM);
    }

    [Theory]
    [InlineData("gen2_gold.sav")]
    [InlineData("gen2_crystal.sav")]
    [InlineData("gen3_ruby.sav")]
    [InlineData("gen3_emerald.sav")]
    [InlineData("gen3_firered.sav")]
    [InlineData("gen4_diamond.sav")]
    [InlineData("gen4_platinum.sav")]
    [InlineData("gen4_heartgold.sav")]
    [InlineData("gen5_black.sav")]
    [InlineData("gen5_white2.sav")]
    public void PublicSavesRemainByteIdenticalWhenSavingWithoutEdits(string file)
    {
        var save = LoadSave(file);
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SaveCommand.Execute(null);
        foreach (var entry in vm.PartyMail.Concat(vm.PcMail))
        {
            vm.SelectedMail = entry;
            vm.SaveCommand.Execute(null);
            AssertSnapshot(save, before);
            Assert.False(save.State.Edited);
        }

        if (save.Generation != 3)
            Assert.All(vm.Species, item => Assert.InRange(item.Value, 0, save.MaxSpeciesID));
    }

    [Theory]
    [InlineData("gen3_ruby.sav")]
    [InlineData("gen3_emerald.sav")]
    [InlineData("gen3_firered.sav")]
    public void UncommittedEditsAndDeletionNeverTouchSource(string file)
    {
        var save = Assert.IsAssignableFrom<SAV3>(LoadSave(file));
        SeedGen3Mail(save, 30027);
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        vm.SelectedSpeciesIndex = vm.Species.ToList().FindIndex(item => item.Value == 30000);
        vm.AuthorTid ^= 0xFFFF;
        vm.DeleteMailCommand.Execute(null);
        AssertSnapshot(save, before);
        Assert.False(save.State.Edited);
    }

    [Fact]
    public void Gen3ChoicesUseTheirOwnSaveContext()
    {
        var save = Assert.IsAssignableFrom<SAV3>(LoadSave("gen3_emerald.sav"));
        using var data = new GameDataScope(new SAV2());
        var vm = new MailBoxEditorViewModel(save);
        Assert.Contains(vm.Species, item => item.Value == 277 && item.Text == GameInfo.Strings.Species[252]);
        Assert.Contains(vm.Species, item => item.Value == 30027);
        Assert.DoesNotContain(vm.Species, item => item.Value > 30027);
    }

    [Theory]
    [InlineData("gen2_crystal.sav")]
    [InlineData("gen4_platinum.sav")]
    [InlineData("gen5_white2.sav")]
    public void OtherGenerationsStillCommitStagedMailEdits(string file)
    {
        var save = LoadSave(file);
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        vm.AuthorTid ^= 0xFFFF;
        if (save.Generation == 2)
        {
            vm.MessageText1 = "HELLO";
            vm.SelectedSpeciesIndex = vm.Species.ToList().FindIndex(item => item.Value == 25);
        }
        else
            vm.Message00 ^= 0x1234;
        AssertSnapshot(save, before);
        vm.SaveCommand.Execute(null);

        MailDetail mail = save switch
        {
            SAV2 gen2 => new Mail2(gen2, 6),
            SAV4 gen4 => gen4.GetMail(0),
            SAV5 gen5 => gen5.GetMail(0),
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        };
        Assert.Equal(vm.AuthorTid, mail.AuthorTID);
        if (save.Generation == 2)
        {
            Assert.Equal("HELLO", mail.GetMessage(false));
            Assert.Equal(25, mail.AppearPKM);
        }
        else
            Assert.Equal(vm.Message00, mail.GetMessage(0, 0));
    }

    [Fact]
    public void Gen3DeletionCommitsBlankRecordOnly()
    {
        var save = Assert.IsAssignableFrom<SAV3>(LoadSave("gen3_emerald.sav"));
        SeedGen3Mail(save, 30027);
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        vm.DeleteMailCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        var expected = CloneSnapshot(before);
        new Mail3().CopyTo(expected[2].AsSpan(MailOffset(save), Mail3.SIZE));
        AssertSnapshot(save, expected);
        Assert.True(save.LargeBlock.GetMail(PcMailIndex).IsEmpty);
    }

    private static SaveFile LoadSave(string file)
    {
        var directory = Assert.IsType<string>(SaveFileFixture.FindSaveFilesPath());
        var save = Assert.IsAssignableFrom<SaveFile>(SaveFileFixture.LoadSave(Path.Combine(directory, file)));
        save.State.Edited = false;
        return save;
    }

    private static int MailOffset(SAV3 save) => (save switch
    {
        SAV3RS => 0x2B4C,
        SAV3E => 0x2BE0,
        SAV3FRLG => 0x2CD0,
        _ => throw new ArgumentOutOfRangeException(nameof(save)),
    }) + (PcMailIndex * Mail3.SIZE);

    private static void SeedGen3Mail(SAV3 save, ushort appearance, ushort mailType = 121)
    {
        var raw = save.Large.Slice(MailOffset(save), Mail3.SIZE);
        raw.Fill(0xA5);
        // A valid, terminated author name followed by noncanonical padding to preserve.
        new byte[] { 0xBB, 0xCD, 0xC2, 0xFF, 0x6A, 0x00, 0x23, 0xFF }.CopyTo(raw[0x12..]);
        BinaryPrimitives.WriteUInt16LittleEndian(raw[0x1E..], appearance);
        BinaryPrimitives.WriteUInt16LittleEndian(raw[0x20..], mailType);
        save.State.Edited = false;
    }

    private static byte[][] Snapshot(SaveFile save) => save is SAV3 gen3
        ? [save.Data.ToArray(), gen3.Small.ToArray(), gen3.Large.ToArray(), gen3.Storage.ToArray()]
        : [save.Data.ToArray()];

    private static byte[][] CloneSnapshot(byte[][] snapshot) => snapshot.Select(buffer => buffer.ToArray()).ToArray();

    private static void AssertSnapshot(SaveFile save, byte[][] expected)
    {
        var actual = Snapshot(save);
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], actual[index]);
    }

    private sealed class GameDataScope : IDisposable
    {
        private readonly FilteredGameDataSource _previous = GameInfo.FilteredSources;

        public GameDataScope(SaveFile save) => GameInfo.FilteredSources = new FilteredGameDataSource(save, GameInfo.Sources);

        public void Dispose() => GameInfo.FilteredSources = _previous;
    }
}
