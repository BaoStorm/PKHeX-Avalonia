using System.Buffers.Binary;
using System.Security.Cryptography;
using PKHeX.Avalonia.Tests.Fixtures;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public sealed class MailBoxAuthorEncodingTests
{
    private static readonly string[] Gen3Files = ["gen3_ruby.sav", "gen3_emerald.sav", "gen3_firered.sav"];
    private const int PcMailIndex = 6;

    public static IEnumerable<object[]> EncodingChanges =>
        from file in Gen3Files
        from targetJapanese in new[] { false, true }
        from author in new[] { "", "A", "ASH", "ABCDE", "ABCDEFG", "ABCDEFGH", "サトシ", "あいうえお", "あいうえおか" }
        from editName in new[] { false, true }
        select new object[] { file, targetJapanese, author, editName };

    [Theory, MemberData(nameof(EncodingChanges))]
    public void ExplicitEncodingChangesSurviveActualSaveExportAndReopen(string file, bool targetJapanese, string author, bool editName)
    {
        var path = SavePath(file);
        var fixtureHash = SHA256.HashData(File.ReadAllBytes(path));
        var save = LoadSave(file);
        SeedMail(save, !targetJapanese, editName ? (targetJapanese ? "OLD" : "ピカチュウ") : author);
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        var expectedInput = editName ? author : vm.AuthorName;
        var language = targetJapanese ? LanguageID.Japanese : LanguageID.English;
        vm.SelectedLanguageIndex = vm.Languages.ToList().FindIndex(item => item.Value == (int)language);
        if (editName)
            vm.AuthorName = author;

        AssertSnapshot(save, before);
        Assert.False(save.State.Edited);
        vm.SaveCommand.Execute(null);

        var expected = ExpectedAuthor(expectedInput, targetJapanese);
        var expectedSave = before.Select(buffer => buffer.ToArray()).ToArray();
        expected.CopyTo(expectedSave[2].AsSpan(MailOffset(save) + 0x12, 8));
        AssertSnapshot(save, expectedSave); // Only the author's eight bytes may change.
        Assert.Equal((byte)language, save.LargeBlock.GetMail(PcMailIndex).AuthorLanguage);
        Assert.Equal(StringConverter3.GetString(expected.AsSpan(0, 7), targetJapanese), vm.AuthorName);

        save.State.Edited = false;
        vm.SaveCommand.Execute(null);
        AssertSnapshot(save, expectedSave);
        Assert.False(save.State.Edited); // Normalized padding/length must not make Save dirty again.

        var reopened = Assert.IsAssignableFrom<SAV3>(SaveUtil.GetSaveFile(save.Write(BinaryExportSetting.None)));
        Assert.Equal(save.GetType(), reopened.GetType());
        var mail = reopened.LargeBlock.GetMail(PcMailIndex);
        Assert.Equal((byte)language, mail.AuthorLanguage);
        Assert.Equal(vm.AuthorName, mail.AuthorName);
        if (author.Length == 0 && editName)
            Assert.True(string.IsNullOrWhiteSpace(mail.AuthorName));
        Assert.Equal((ushort)30027, mail.AppearPKM);
        var raw = new byte[Mail3.SIZE];
        mail.CopyTo(raw);
        Assert.Equal(expectedSave[2].AsSpan(MailOffset(save), Mail3.SIZE).ToArray(), raw);
        Assert.Equal(fixtureHash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    public static IEnumerable<object[]> NameChanges =>
        from file in Gen3Files
        from japanese in new[] { false, true }
        from author in new[] { "", "A", "ABCDEFGH", "サトシ", "あいうえおか" }
        select new object[] { file, japanese, author };

    [Theory, MemberData(nameof(NameChanges))]
    public void ExplicitNameEditsKeepTheSelectedEncodingIncludingBlankInternationalAuthors(string file, bool japanese, string author)
    {
        var save = LoadSave(file);
        SeedMail(save, japanese, japanese ? "ピカチュウ" : "OLD");
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        Assert.False(string.IsNullOrWhiteSpace(vm.AuthorName));
        vm.AuthorName = author;
        AssertSnapshot(save, before);
        vm.SaveCommand.Execute(null);

        var expectedSave = before.Select(buffer => buffer.ToArray()).ToArray();
        ExpectedAuthor(author, japanese).CopyTo(expectedSave[2].AsSpan(MailOffset(save) + 0x12, 8));
        AssertSnapshot(save, expectedSave);
        var reopened = Assert.IsAssignableFrom<SAV3>(SaveUtil.GetSaveFile(save.Write(BinaryExportSetting.None)));
        var mail = reopened.LargeBlock.GetMail(PcMailIndex);
        Assert.Equal(japanese ? (byte)LanguageID.Japanese : (byte)LanguageID.English, mail.AuthorLanguage);
        Assert.Equal(vm.AuthorName, mail.AuthorName);
        if (author.Length == 0)
            Assert.True(string.IsNullOrWhiteSpace(mail.AuthorName));
    }

    [Theory]
    [InlineData("gen3_ruby.sav")]
    [InlineData("gen3_emerald.sav")]
    [InlineData("gen3_firered.sav")]
    public void Gen3OffersOnlyTheTwoPersistableAuthorEncodings(string file)
    {
        var save = LoadSave(file);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);

        Assert.Equal(new[] { (int)LanguageID.Japanese, (int)LanguageID.English }, vm.Languages.Select(item => item.Value));
        Assert.Equal(LocalizedStrings.Instance["MailBoxEditor_International"], vm.Languages.Single(item => item.Value == (int)LanguageID.English).Text);
    }

    public static IEnumerable<object[]> UntouchedAuthors =>
        from file in Gen3Files
        from rawAuthor in new byte[][]
        {
            [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], // Original empty/Japanese.
            [0xFF, 0x71, 0xA6, 0x19, 0xA3, 0x00, 0x71, 0x99], // Empty international with opaque trash.
            [0xBB, 0xCD, 0xC2, 0xFF, 0x6A, 0x00, 0x23, 0xFF], // International, noncanonical padding.
            [0xBB, 0xCD, 0xC2, 0xFF, 0x6A, 0xFF, 0x23, 0x99], // Japanese, noncanonical padding.
        }
        select new object[] { file, rawAuthor };

    [Theory, MemberData(nameof(UntouchedAuthors))]
    public void UnchangedAuthorsAndUnrelatedEditsPreserveEveryOriginalNameByte(string file, byte[] rawAuthor)
    {
        var save = LoadSave(file);
        SeedMail(save, false, "ASH");
        rawAuthor.CopyTo(save.Large.Slice(MailOffset(save) + 0x12, 8));
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        var originalLanguage = vm.SelectedLanguageIndex;
        vm.SelectedLanguageIndex = originalLanguage == 0 ? 1 : 0;
        vm.SelectedLanguageIndex = originalLanguage;
        vm.SaveCommand.Execute(null);
        AssertSnapshot(save, before);
        Assert.False(save.State.Edited);

        vm.AuthorTid ^= 0x1234;
        vm.SaveCommand.Execute(null);
        var expected = before.Select(buffer => buffer.ToArray()).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(expected[2].AsSpan(MailOffset(save) + 0x1A), vm.AuthorTid);
        AssertSnapshot(save, expected);
        var reopened = Assert.IsAssignableFrom<SAV3>(SaveUtil.GetSaveFile(save.Write(BinaryExportSetting.None)));
        Assert.Equal(rawAuthor, reopened.Large.Slice(MailOffset(reopened) + 0x12, 8).ToArray());
    }

    public static IEnumerable<object[]> InvalidLanguageChoices =>
        from file in Gen3Files from selection in new[] { -1, 2, 100, 3 } select new object[] { file, selection };

    [Theory, MemberData(nameof(InvalidLanguageChoices))]
    public void UnsupportedOrInvalidEncodingSelectionsCannotRewriteStoredAuthor(string file, int selection)
    {
        var save = LoadSave(file);
        SeedMail(save, true, "ASH");
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        if (selection == 3)
        {
            vm.Languages.Add(new ComboItem("French", (int)LanguageID.French));
            selection = vm.Languages.Count - 1;
        }
        vm.SelectedLanguageIndex = selection;
        vm.SaveCommand.Execute(null);
        AssertSnapshot(save, before);
        Assert.False(save.State.Edited);

        vm.Message00 ^= 0x1234;
        vm.SaveCommand.Execute(null);
        var expected = before.Select(buffer => buffer.ToArray()).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(expected[2].AsSpan(MailOffset(save)), vm.Message00);
        AssertSnapshot(save, expected);
        var reopened = Assert.IsAssignableFrom<SAV3>(SaveUtil.GetSaveFile(save.Write(BinaryExportSetting.None)));
        Assert.Equal((byte)LanguageID.Japanese, reopened.LargeBlock.GetMail(PcMailIndex).AuthorLanguage);
    }

    [Theory]
    [InlineData("gen3_ruby.sav")]
    [InlineData("gen3_emerald.sav")]
    [InlineData("gen3_firered.sav")]
    public void EncodingEditsAndDeletionAreStagedUntilSave(string file)
    {
        var save = LoadSave(file);
        SeedMail(save, false, "ASH");
        var before = Snapshot(save);
        using var data = new GameDataScope(save);
        var vm = new MailBoxEditorViewModel(save);
        vm.SelectedMail = vm.PcMail[0];
        vm.SelectedLanguageIndex = vm.Languages.ToList().FindIndex(item => item.Value == (int)LanguageID.Japanese);
        vm.AuthorName = "サトシ";
        AssertSnapshot(save, before);
        vm.DeleteMailCommand.Execute(null);
        AssertSnapshot(save, before);
        Assert.False(save.State.Edited);

        var reopened = new MailBoxEditorViewModel(save);
        reopened.SelectedMail = reopened.PcMail[0];
        Assert.Equal((int)LanguageID.English, reopened.Languages[reopened.SelectedLanguageIndex].Value);
        Assert.StartsWith("ASH", reopened.AuthorName);
    }

    private static byte[] ExpectedAuthor(string author, bool japanese)
    {
        var raw = new byte[8];
        if (author.Length == 0)
        {
            if (japanese)
            {
                Array.Fill(raw, (byte)0xFF);
                return raw;
            }
            author = "      ";
        }
        var limit = japanese ? 5 : 7;
        StringConverter3.SetString(raw.AsSpan(0, limit + 1), author, limit, japanese, StringConverterOption.ClearFF);
        if (japanese)
            raw[6] = 0;
        else
            raw.AsSpan(0, 6).Replace<byte>(0xFF, 0);
        raw[7] = 0xFF;
        return raw;
    }

    private static void SeedMail(SAV3 save, bool japanese, string author)
    {
        var raw = save.Large.Slice(MailOffset(save), Mail3.SIZE);
        raw.Fill(0xA5);
        var mail = new Mail3(raw.ToArray(), -1)
        {
            AuthorLanguage = japanese ? (byte)LanguageID.Japanese : (byte)LanguageID.English,
            AuthorName = !japanese && author.Length == 0 ? "      " : author,
        };
        mail.CopyTo(raw);
        BinaryPrimitives.WriteUInt16LittleEndian(raw[0x1E..], 30027);
        BinaryPrimitives.WriteUInt16LittleEndian(raw[0x20..], 121);
        save.State.Edited = false;
    }

    private static string SavePath(string file) => Path.Combine(Assert.IsType<string>(SaveFileFixture.FindSaveFilesPath()), file);

    private static SAV3 LoadSave(string file)
    {
        var save = Assert.IsAssignableFrom<SAV3>(SaveFileFixture.LoadSave(SavePath(file)));
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

    private static byte[][] Snapshot(SAV3 save) => [save.Data.ToArray(), save.Small.ToArray(), save.Large.ToArray(), save.Storage.ToArray()];

    private static void AssertSnapshot(SAV3 save, byte[][] expected)
    {
        var actual = Snapshot(save);
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
