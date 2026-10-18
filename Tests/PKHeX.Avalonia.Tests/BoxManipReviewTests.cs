using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Moq;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class BoxManipReviewTests
{
    private static SAV7SM CreateSave()
    {
        var sav = new SAV7SM();
        for (int box = 0; box < 3; box++)
        for (int slot = 0; slot < 4; slot++)
        {
            var pk = new PK7 { Species = (ushort)(10 + (box * 10) + (3 - slot)), CurrentLevel = 5, HeldItem = slot == 0 ? 1 : 0 };
            pk.RefreshChecksum();
            sav.SetBoxSlotAtIndex(pk, box, slot);
        }
        sav.State.Edited = false;
        return sav;
    }

    private static byte[] BoxBytes(SaveFile sav, int box) =>
        Enumerable.Range(0, sav.BoxSlotCount).SelectMany(slot => sav.GetBoxSlotAtIndex(box, slot).Data.ToArray()).ToArray();

    private static int Occupied(SaveFile sav, int box) =>
        Enumerable.Range(0, sav.BoxSlotCount).Count(slot => sav.GetBoxSlotAtIndex(box, slot).Species != 0);

    private static Mock<IDialogService> Dialogs(bool confirm)
    {
        var mock = new Mock<IDialogService>();
        mock.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(confirm);
        return mock;
    }

    private static BoxManipItemViewModel Find(BoxManipViewModel vm, BoxManipType type) =>
        vm.SortActions.Concat(vm.ClearActions).Concat(vm.ModifyActions).First(a => a.Type == type);

    [Fact]
    public void EveryManipulationHasLocalizedNameAndDescription()
    {
        foreach (var type in Enum.GetValues<BoxManipType>())
        {
            Assert.NotEqual($"BoxManip_Action_{type}", LocalizedStrings.Instance[$"BoxManip_Action_{type}"]);
            Assert.NotEqual($"BoxManip_Desc_{type}", LocalizedStrings.Instance[$"BoxManip_Desc_{type}"]);
        }
        var vm = new BoxManipViewModel(CreateSave(), Dialogs(true).Object, () => { });
        Assert.All(vm.SortActions.Concat(vm.ClearActions).Concat(vm.ModifyActions), a => Assert.NotEqual(a.Type.ToString(), a.Name));
        Assert.All(vm.ClearActions, a => Assert.Equal(BoxManipCategory.Delete, a.Category));
    }

    [Fact]
    public void DefaultsToTheCurrentBoxAndSelectingDoesNotMutate()
    {
        var sav = CreateSave();
        var before = sav.Data.ToArray();
        var vm = new BoxManipViewModel(sav, Dialogs(true).Object, () => { }, currentBox: 2);
        Assert.False(vm.ApplyToAllBoxes);
        Assert.Equal(3, vm.CurrentBoxNumber);
        Assert.False(vm.ApplyCommand.CanExecute(null));

        vm.SelectedClear = Find(vm, BoxManipType.DeleteAll);
        Assert.True(vm.IsDestructive);
        Assert.Contains("3", vm.ScopeText);
        Assert.Equal(LocalizedStrings.Instance.Format("BoxManip_ImpactDelete", 4, 4), vm.ImpactText);
        vm.SelectedSort = Find(vm, BoxManipType.SortSpecies); // switching tabs replaces the staged action
        Assert.Null(vm.SelectedClear);
        Assert.Equal(BoxManipType.SortSpecies, vm.SelectedAction!.Type);
        Assert.Equal(before, sav.Data.ToArray());
        Assert.False(sav.State.Edited);
    }

    [Fact]
    public async Task DeclinedConfirmationChangesNothing()
    {
        var sav = CreateSave();
        var before = sav.Data.ToArray();
        var dialogs = Dialogs(false);
        var vm = new BoxManipViewModel(sav, dialogs.Object, () => { });
        vm.SelectedClear = Find(vm, BoxManipType.DeleteAll);
        await vm.ApplyCommand.ExecuteAsync(null);

        dialogs.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.Equal(before, sav.Data.ToArray());
        Assert.Equal(LocalizedStrings.Instance["BoxManip_Cancelled"], vm.StatusText);
        Assert.NotNull(vm.SelectedAction);
    }

    [Fact]
    public async Task CurrentBoxDeleteOnlyTouchesThatBoxAndUndoRestoresIt()
    {
        var sav = CreateSave();
        var undo = new UndoRedoService();
        undo.Initialize(sav);
        var box0 = BoxBytes(sav, 0);
        var box1 = BoxBytes(sav, 1);
        var completed = 0;
        var vm = new BoxManipViewModel(sav, Dialogs(true).Object, () => completed++, currentBox: 1, undoRedo: undo);
        vm.SelectedClear = Find(vm, BoxManipType.DeleteItemless);
        Assert.Equal(LocalizedStrings.Instance.Format("BoxManip_ImpactDelete", 3, 4), vm.ImpactText);
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, Occupied(sav, 1));
        Assert.Equal(box0, BoxBytes(sav, 0));
        Assert.Equal(4, Occupied(sav, 2));
        Assert.True(sav.State.Edited);
        Assert.Equal(1, completed);
        Assert.Null(vm.SelectedAction); // must be chosen again before another apply
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.True(undo.CanUndo);

        undo.Undo();
        Assert.Equal(box1, BoxBytes(sav, 1));
    }

    [Fact]
    public async Task AllBoxesDeleteUsesTheStrongerPromptAndClearsEveryBox()
    {
        var sav = CreateSave();
        string? prompt = null;
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((_, message, _, _) => prompt = message).ReturnsAsync(true);
        var vm = new BoxManipViewModel(sav, dialogs.Object, () => { }) { ApplyToAllBoxes = true };
        vm.SelectedClear = Find(vm, BoxManipType.DeleteAll);
        var expected = LocalizedStrings.Instance.Format("BoxManip_ConfirmDeleteAll", vm.SelectedName, vm.ScopeText, vm.ImpactText);
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(expected, prompt);
        Assert.All(Enumerable.Range(0, 3), box => Assert.Equal(0, Occupied(sav, box)));
    }

    [Fact]
    public async Task SortAndModifyRunWithoutConfirmationAndStayInScope()
    {
        var sav = CreateSave();
        var dialogs = Dialogs(true);
        var box1 = BoxBytes(sav, 1);
        var vm = new BoxManipViewModel(sav, dialogs.Object, () => { });
        vm.SelectedSort = Find(vm, BoxManipType.SortSpecies);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Equal([10, 11, 12, 13], Enumerable.Range(0, 4).Select(s => (int)sav.GetBoxSlotAtIndex(0, s).Species));
        Assert.Equal(box1, BoxBytes(sav, 1));

        vm.SelectedModify = Find(vm, BoxManipType.ModifyMaxLevel);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.All(Enumerable.Range(0, 4), s => Assert.Equal(100, sav.GetBoxSlotAtIndex(0, s).CurrentLevel));
        Assert.Equal(box1, BoxBytes(sav, 1));
        dialogs.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SecondApplyWhileConfirmationIsOpenIsBlocked()
    {
        var sav = CreateSave();
        var pending = new TaskCompletionSource<bool>();
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(pending.Task);
        var vm = new BoxManipViewModel(sav, dialogs.Object, () => { });
        vm.SelectedClear = Find(vm, BoxManipType.DeleteAll);

        var first = vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        vm.ApplyCommand.Execute(null); // a double-click arriving while the prompt is open
        pending.SetResult(true);
        await first;

        dialogs.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.Equal(0, Occupied(sav, 0));
        Assert.Equal(4, Occupied(sav, 1));
    }

    [AvaloniaTheory]
    [InlineData(680, 500)]
    [InlineData(460, 380)]
    public void ViewKeepsReviewAndApplyReachable(double width, double height)
    {
        using var app = new HeadlessAppFixture();
        var vm = new BoxManipViewModel(CreateSave(), Dialogs(true).Object, () => { });
        vm.SelectedSort = Find(vm, BoxManipType.SortLevel);
        var view = new BoxManipView { DataContext = vm, Width = width, Height = height };
        app.Window.Content = view;
        app.Window.Width = width;
        app.Window.Height = height;
        app.Pump();

        var apply = Assert.Single(view.GetVisualDescendants().OfType<Button>(),
            b => b.IsEffectivelyVisible && Equals(b.Content, LocalizedStrings.Instance["BoxManip_Apply"]));
        Assert.Equal(vm.ApplyAutomationName, global::Avalonia.Automation.AutomationProperties.GetName(apply));
        var origin = apply.TranslatePoint(default, view)!.Value;
        Assert.True(origin.X >= 0 && origin.X + apply.Bounds.Width <= width + 0.5, $"Apply clipped at {origin}");
        Assert.True(origin.Y >= 0 && origin.Y + apply.Bounds.Height <= height + 0.5, $"Apply clipped at {origin}");
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == vm.SelectedDescription);
    }
}
