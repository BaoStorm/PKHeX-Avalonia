using Moq;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public sealed class PokemonEditorRerollPidTests
{
    [Theory]
    [InlineData(4, 2)] [InlineData(4, 3)] [InlineData(4, -1)] [InlineData(4, 256)]
    [InlineData(5, 2)] [InlineData(5, 3)] [InlineData(5, -1)] [InlineData(5, 256)]
    public void InvalidDualGender_DisablesAndRejectsDirectExecution(int format, int gender)
    {
        var original = Entity(format, format == 4 ? GameVersion.Pt : GameVersion.B2);
        var source = original.Data.ToArray();
        var vm = Editor(original).VM;
        vm.Gender = gender;

        AssertRejectedWithoutChanges(vm);

        Assert.Equal(gender, vm.Gender);
        Assert.Equal(source, original.Data.ToArray());
    }

    [Theory]
    [InlineData(GameVersion.E, 25)] [InlineData(GameVersion.E, 255)]
    [InlineData(GameVersion.Pt, 25)] [InlineData(GameVersion.Pt, 255)]
    public void InvalidTransferredNature_DisablesAndRejectsDirectExecution(GameVersion origin, int nature)
    {
        var original = Entity(5, origin);
        original.Nature = (Nature)nature;
        original.RefreshChecksum();
        var source = original.Data.ToArray();
        var vm = Editor(original).VM;

        AssertRejectedWithoutChanges(vm);

        Assert.Equal(nature, vm.Nature);
        Assert.Equal(source, original.Data.ToArray());
    }

    [Theory]
    [InlineData(0)] [InlineData(650)] [InlineData(ushort.MaxValue)]
    public void InvalidLegacySpecies_DisablesAndRejectsDirectExecution(ushort species)
    {
        var original = Entity(5, GameVersion.B2);
        original.Species = species;
        var vm = Editor(original).VM;

        AssertRejectedWithoutChanges(vm);
    }

    [Theory]
    [InlineData(GameVersion.FR, 28)] [InlineData(GameVersion.FR, 31)] [InlineData(GameVersion.FR, -1)]
    [InlineData(GameVersion.LG, 28)] [InlineData(GameVersion.LG, 31)]
    public void InvalidFrlgUnownForm_DisablesAndRejectsDirectExecution(GameVersion origin, int form)
    {
        // The transferred format stores form separately. Do not call the Gen3
        // Unown form setter with an impossible form; it also searches indefinitely.
        var vm = Editor(Entity(5, origin, Species.Unown)).VM;
        vm.Form = form;

        AssertRejectedWithoutChanges(vm);

        Assert.Equal(form, vm.Form);
    }

    [Theory]
    [InlineData(3, 0)] [InlineData(3, 1)]
    [InlineData(4, 0)] [InlineData(4, 1)]
    [InlineData(5, 0)] [InlineData(5, 1)]
    public void ValidLegacyReroll_PreservesGenderNatureAndAbilityBit(int format, int gender)
    {
        var origin = format switch { 3 => GameVersion.E, 4 => GameVersion.Pt, _ => GameVersion.B2 };
        var original = Entity(format, origin, gender: gender);
        var source = original.Data.ToArray();
        var (vm, sprites, _) = Editor(original);
        var nature = vm.Nature;
        var bit = original.PIDAbility;
        sprites.Invocations.Clear();

        Assert.True(vm.RerollPidCommand.CanExecute(null));
        vm.RerollPidCommand.Execute(null);

        sprites.Verify(renderer => renderer.GetSprite(It.IsAny<PKM>(), false), Times.AtLeastOnce);
        var prepared = vm.PreparePKM();
        Assert.Equal(gender, prepared.Gender);
        Assert.Equal(nature, (int)prepared.Nature);
        Assert.Equal(bit, prepared.PIDAbility);
        Assert.Equal(prepared.PID.ToString("X8"), vm.Pid);
        Assert.Equal(prepared.Data.ToArray(), vm.PreparePKM().Data.ToArray());
        Assert.Equal(source, original.Data.ToArray());
    }

    [Theory]
    [InlineData(GameVersion.E, 0)] [InlineData(GameVersion.E, 24)]
    [InlineData(GameVersion.Pt, 0)] [InlineData(GameVersion.Pt, 24)]
    public void ValidTransferredNature_RerollsWithTheOriginConstraints(GameVersion origin, int nature)
    {
        var original = Entity(5, origin);
        original.Nature = (Nature)nature;
        var vm = Editor(original).VM;

        Assert.True(vm.RerollPidCommand.CanExecute(null));
        vm.RerollPidCommand.Execute(null);

        Assert.Equal((uint)nature, vm.TargetPKM.PID % 25);
        Assert.Equal(original.Gender, vm.TargetPKM.Gender);
        Assert.Equal(original.PID & 1, vm.TargetPKM.PID & 1);
    }

    [Theory]
    [InlineData(GameVersion.FR, 0)] [InlineData(GameVersion.FR, 27)]
    [InlineData(GameVersion.LG, 0)] [InlineData(GameVersion.LG, 27)]
    public void ValidFrlgUnownForm_RerollsToTheRequestedLetter(GameVersion origin, int form)
    {
        var vm = Editor(Entity(5, origin, Species.Unown)).VM;
        vm.Form = form;

        Assert.True(vm.RerollPidCommand.CanExecute(null));
        vm.RerollPidCommand.Execute(null);

        Assert.Equal(form, EntityPID.GetUnownForm3(vm.TargetPKM.PID));
        Assert.Equal((uint)vm.Nature, vm.TargetPKM.PID % 25);
    }

    [Theory]
    [InlineData(5, GameVersion.Any, Species.Eevee, 0)]
    [InlineData(9, GameVersion.Any, Species.Eevee, 0)]
    [InlineData(9, GameVersion.X, Species.Unown, 31)]
    [InlineData(9, GameVersion.SV, (Species)650, 0)]
    [InlineData(9, GameVersion.X, (Species)0, 0)]
    public void UnconstrainedOrigins_RemainAvailableWithLegacyInfeasibleValues(int format, GameVersion origin, Species species, int form)
    {
        var original = Entity(format, origin, species);
        original.Nature = (Nature)255;
        original.Gender = 2;
        var (vm, sprites, _) = Editor(original);
        vm.Form = form;
        sprites.Invocations.Clear();

        Assert.True(vm.RerollPidCommand.CanExecute(null));
        vm.RerollPidCommand.Execute(null);

        sprites.Verify(renderer => renderer.GetSprite(It.IsAny<PKM>(), false), Times.AtLeastOnce);
        Assert.Equal(255, vm.Nature);
        Assert.Equal(2, vm.Gender);
        Assert.Equal(form, vm.Form);
    }

    [Theory]
    [InlineData(GameVersion.B2)] [InlineData(GameVersion.E)]
    public void SingleGenderSpecies_DoesNotRequireMaleOrFemale(GameVersion origin)
    {
        var vm = Editor(Entity(5, origin, Species.Magnemite)).VM;
        vm.Gender = 2;

        Assert.True(vm.RerollPidCommand.CanExecute(null));
        vm.RerollPidCommand.Execute(null);

        Assert.Equal(2, vm.Gender);
    }

    [Fact]
    public void Gen5Origin_DoesNotRequirePidDerivedNature()
    {
        var original = Entity(5, GameVersion.B2);
        original.Nature = (Nature)255;
        var vm = Editor(original).VM;

        Assert.True(vm.RerollPidCommand.CanExecute(null));
        vm.RerollPidCommand.Execute(null);

        Assert.Equal(255, vm.Nature);
    }

    [Fact]
    public void NonFrlgUnown_DoesNotRequirePidDerivedForm()
    {
        var vm = Editor(Entity(5, GameVersion.E, Species.Unown)).VM;
        vm.Form = 31;

        Assert.True(vm.RerollPidCommand.CanExecute(null));
        vm.RerollPidCommand.Execute(null);

        Assert.Equal(31, vm.Form);
    }

    [Theory]
    [InlineData(nameof(PokemonEditorViewModel.Gender))]
    [InlineData(nameof(PokemonEditorViewModel.Nature))]
    [InlineData(nameof(PokemonEditorViewModel.Form))]
    [InlineData(nameof(PokemonEditorViewModel.Species))]
    [InlineData(nameof(PokemonEditorViewModel.OriginGame))]
    public void InputChanges_NotifyCommandAvailability(string property)
    {
        var vm = Editor(Entity(5, GameVersion.B2)).VM;
        var notifications = 0;
        vm.RerollPidCommand.CanExecuteChanged += (_, _) => notifications++;

        switch (property)
        {
            case nameof(vm.Gender): vm.Gender = 2; break;
            case nameof(vm.Nature): vm.Nature = 1; break;
            case nameof(vm.Form): vm.Form = 1; break;
            case nameof(vm.Species): vm.Species = (int)Species.Magnemite; break;
            case nameof(vm.OriginGame): vm.OriginGame = (int)GameVersion.Any; break;
        }

        Assert.True(notifications > 0);
    }

    [Fact]
    public void OriginChangesAndEntityReload_RefreshAvailabilityAndUseCurrentOrigin()
    {
        var vm = Editor(Entity(5, GameVersion.B2)).VM;
        vm.Gender = 2;
        Assert.False(vm.RerollPidCommand.CanExecute(null));
        var notifications = new List<bool>();
        vm.RerollPidCommand.CanExecuteChanged += (_, _) => notifications.Add(vm.RerollPidCommand.CanExecute(null));

        vm.OriginGame = (int)GameVersion.Any;
        Assert.True(vm.RerollPidCommand.CanExecute(null));
        // OriginGame changes can leave the stored Version stale until validation.
        // The generator must use the same current input as the availability guard.
        vm.RerollPidCommand.Execute(null);
        Assert.True(notifications.Last());
        vm.OriginGame = (int)GameVersion.B2;
        Assert.False(vm.RerollPidCommand.CanExecute(null));
        Assert.False(notifications.Last());
        AssertRejectedWithoutChanges(vm);

        var replacement = Entity(5, GameVersion.B2, Species.Magnemite);
        Assert.True(vm.LoadPKM(replacement));
        Assert.True(vm.RerollPidCommand.CanExecute(null));
        Assert.True(notifications.Last());
        vm.RefreshLanguage();
        Assert.True(vm.RerollPidCommand.CanExecute(null));
        Assert.True(notifications.Last());
    }

    private static void AssertRejectedWithoutChanges(PokemonEditorViewModel vm)
    {
        var before = vm.TargetPKM.Data.ToArray();
        var pid = vm.Pid;
        var input = (vm.Species, vm.Form, vm.Gender, vm.Nature, vm.OriginGame);
        Assert.False(vm.RerollPidCommand.CanExecute(null));

        // RelayCommand.Execute itself does not enforce CanExecute.
        vm.RerollPidCommand.Execute(null);

        Assert.Equal(before, vm.TargetPKM.Data.ToArray());
        Assert.Equal(pid, vm.Pid);
        Assert.Equal(input, (vm.Species, vm.Form, vm.Gender, vm.Nature, vm.OriginGame));
    }

    private static PKM Entity(int format, GameVersion origin, Species species = Species.Eevee, int gender = 0)
    {
        PKM pk = format switch { 3 => new PK3(), 4 => new PK4(), 5 => new PK5(), 9 => new PK9(), _ => throw new ArgumentOutOfRangeException(nameof(format)) };
        pk.Species = (ushort)species;
        pk.Version = origin;
        pk.PID = gender == 0 ? 0x12345678u : 0x12340000u;
        pk.Gender = (byte)gender;
        if (format >= 5) pk.Nature = Nature.Hardy;
        pk.Language = (int)LanguageID.English;
        pk.TID16 = 0x1111; pk.SID16 = 0x2222;
        pk.OriginalTrainerName = "TEST";
        pk.CurrentLevel = 50; pk.MetLevel = 20; pk.Ball = (byte)Ball.Poke;
        pk.ClearNickname(); pk.RefreshAbility(0); pk.ResetPartyStats(); pk.RefreshChecksum();
        return pk;
    }

    private static (PokemonEditorViewModel VM, Mock<ISpriteRenderer> SpriteRenderer, Mock<IDialogService> DialogService) Editor(PKM pk)
    {
        SaveFile save = pk.Format switch { 3 => new SAV3E(), 4 => new SAV4Pt(), 5 => new SAV5B2W2(), 9 => new SAV9SV(), _ => throw new ArgumentOutOfRangeException(nameof(pk)) };
        GameInfo.FilteredSources = new FilteredGameDataSource(save, GameInfo.Sources);
        return TestHelpers.CreateTestViewModel(pk, save);
    }
}
