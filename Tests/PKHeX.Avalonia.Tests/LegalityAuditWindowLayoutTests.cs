using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Moq;
using PKHeX.Avalonia.Services;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class LegalityAuditWindowLayoutTests
{
    [AvaloniaFact]
    public async Task ToolHostHonorsAuditMinimumAfterResizeAndReopen()
    {
        var owner = new Window { Width = 900, Height = 600 };
        var service = new WindowService(() => owner);
        owner.Show();
        try
        {
            var vm = CreatePopulatedAudit();
            await vm.RunCommand.ExecuteAsync(null);
            service.ShowTool(vm, "Audit sizing regression");
            var first = Assert.Single(owner.OwnedWindows);
            await PKHeX.Testing.HeadlessRenderSettling.Settle(first);
            Assert.Equal(700, first.MinWidth);
            Assert.Equal(400, first.MinHeight);
            AssertAuditLayout(first, Assert.IsType<LegalityAuditView>(first.Content));

            first.Width = 474;
            first.Height = 304;
            await PKHeX.Testing.HeadlessRenderSettling.Settle(first);
            Assert.True(first.ClientSize.Width >= 700);
            Assert.True(first.ClientSize.Height >= 400);
            AssertAuditLayout(first, Assert.IsType<LegalityAuditView>(first.Content));
            first.Close();

            service.ShowTool(vm, "Audit reopened");
            var reopened = Assert.Single(owner.OwnedWindows);
            await PKHeX.Testing.HeadlessRenderSettling.Settle(reopened);
            Assert.True(reopened.ClientSize.Width >= 700);
            Assert.True(reopened.ClientSize.Height >= 400);
            AssertAuditLayout(reopened, Assert.IsType<LegalityAuditView>(reopened.Content));
        }
        finally
        {
            service.CloseAllTools();
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(474, 304, 700, 400)]
    [InlineData(1700, 1000, 1000, 700)]
    public async Task StaleRememberedDimensionsAreClampedBeforeShowing(int width, int height, int expectedWidth, int expectedHeight)
    {
        var view = new LegalityAuditView { DataContext = CreatePopulatedAudit() };
        var window = new Window { Content = view, Width = width, Height = height, MaxWidth = 1000, MaxHeight = 700 };
        WindowService.ConstrainToolBounds(window);
        Assert.Equal(expectedWidth, window.Width);
        Assert.Equal(expectedHeight, window.Height);
        window.Show();
        try
        {
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            AssertAuditLayout(window, view);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SmallerWorkAreaUsesScrollableContentWithoutExceedingAvailableClientSize()
    {
        var view = new LegalityAuditView { DataContext = CreatePopulatedAudit() };
        var window = new Window { Content = view, Width = 700, Height = 400, MaxWidth = 600, MaxHeight = 300 };
        WindowService.ConstrainToolBounds(window);
        Assert.Equal(600, window.MinWidth);
        Assert.Equal(300, window.MinHeight);
        var scroll = Assert.IsType<ScrollViewer>(window.Content);
        window.Show();
        try
        {
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            Assert.Equal(600, window.ClientSize.Width);
            Assert.Equal(300, window.ClientSize.Height);
            Assert.True(scroll.Extent.Width >= 700);
            Assert.True(scroll.Extent.Height >= 400);
            Assert.True(scroll.Viewport.Width < scroll.Extent.Width);
            Assert.True(scroll.Viewport.Height < scroll.Extent.Height);
            scroll.Offset = new Vector(double.MaxValue, double.MaxValue);
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            Assert.True(scroll.Offset.X > 0);
            Assert.True(scroll.Offset.Y > 0);
            var status = view.FindControl<TextBlock>("AuditStatus")!;
            var bottomRight = status.TranslatePoint(new Point(status.Bounds.Width, status.Bounds.Height), window)!.Value;
            Assert.InRange(bottomRight.X, 1, window.ClientSize.Width);
            Assert.InRange(bottomRight.Y, 1, window.ClientSize.Height);
            scroll.Offset = new Vector(0, scroll.Offset.Y);
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            var start = status.TranslatePoint(default, window)!.Value;
            Assert.InRange(start.X, 0, window.ClientSize.Width);
            Assert.InRange(start.Y, 0, window.ClientSize.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, 0, 420, 300)]
    [InlineData(360, 0, 420, 300)]
    [InlineData(480, 300, 1100, 600)]
    public void OtherToolPreferencesAndUnwrappedContentArePreserved(int minWidth, int minHeight, int width, int height)
    {
        var view = new Border { MinWidth = minWidth, MinHeight = minHeight };
        var window = new Window { Content = view, Width = width, Height = height, MaxWidth = 1200, MaxHeight = 760 };
        WindowService.ConstrainToolBounds(window);
        Assert.Same(view, window.Content);
        Assert.Equal(width, window.Width);
        Assert.Equal(height, window.Height);
    }

    internal static LegalityAuditViewModel CreatePopulatedAudit()
    {
        var save = new SAV3E();
        for (int i = 0; i < 30; i++)
        {
            var pokemon = new PK3 { Species = (ushort)Species.Mudkip, CurrentLevel = 5, Nickname = $"Audit{i:D2}" };
            pokemon.RefreshChecksum();
            save.SetBoxSlotAtIndex(pokemon, 0, i);
        }
        return new LegalityAuditViewModel(save, Mock.Of<IDialogService>());
    }

    private static void AssertAuditLayout(Window window, LegalityAuditView view)
    {
        Assert.Equal(window.ClientSize.Width, view.Bounds.Width);
        Assert.Equal(window.ClientSize.Height, view.Bounds.Height);
        foreach (var control in view.GetVisualDescendants().OfType<Button>())
            AssertVisibleInClient(window, control);
        AssertVisibleInClient(window, view.GetVisualDescendants().OfType<ComboBox>().Single());
        AssertVisibleInClient(window, view.GetVisualDescendants().OfType<TextBox>().First());
        AssertVisibleInClient(window, view.FindControl<TextBlock>("AuditStatus")!);
        var grid = view.FindControl<DataGrid>("AuditGrid")!;
        AssertVisibleInClient(window, grid);
        Assert.True(grid.Bounds.Height >= 100);
        Assert.Equal(5, grid.Columns.Count);
        Assert.All(grid.Columns, column => Assert.True(column.ActualWidth > 0));
    }

    private static void AssertVisibleInClient(Window window, Control control)
    {
        var origin = control.TranslatePoint(default, window)!.Value;
        Assert.True(origin.X >= 0 && origin.Y >= 0 &&
            origin.X + control.Bounds.Width <= window.ClientSize.Width + 1 &&
            origin.Y + control.Bounds.Height <= window.ClientSize.Height + 1,
            $"{control.Name ?? control.GetType().Name}: {origin} + {control.Bounds.Size} outside {window.ClientSize}");
    }
}
