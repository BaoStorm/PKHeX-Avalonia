using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Models.Events;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Render.Tests;

public sealed class Gen4EventWorkspaceRenderTests
{
    [AvaloniaTheory]
    [InlineData("gen4_diamond.sav", "en", false)]
    [InlineData("gen4_platinum.sav", "de", true)]
    [InlineData("gen4_heartgold.sav", "ja", false)]
    [InlineData("gen4_platinum.sav", "pt-BR", true)]
    public async Task OriginalDsEventsRenderInTheCompactMainWorkspaceWithReachableLabelsAndApply(string file, string language, bool dark)
    {
        var previousLanguage = LocalizedStrings.Instance.CurrentLanguage;
        var previousData = GameInfo.CurrentLanguage;
        var previousStrings = GameInfo.Strings;
        var previousFiltered = GameInfo.FilteredSources;
        var previousTheme = global::Avalonia.Application.Current!.RequestedThemeVariant;
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var root = Path.Combine(Path.GetTempPath(), "pkhex-events-render-" + Guid.NewGuid().ToString("N"));
        var paths = new Mock<IAppPaths>();
        paths.SetupGet(path => path.ConfigDirectory).Returns(Path.Combine(root, "config"));
        paths.SetupGet(path => path.DataDirectory).Returns(Path.Combine(root, "data"));
        paths.SetupGet(path => path.ConfigFilePath).Returns(Path.Combine(root, "config.json"));
        paths.SetupGet(path => path.LegacyConfigFilePath).Returns(Path.Combine(root, "legacy.json"));
        using var services = (ServiceProvider)App.BuildServiceProvider(paths.Object, Mock.Of<ISettingsStore>(), new AppSettings(), overrides =>
        {
            overrides.AddSingleton(Mock.Of<IDialogService>());
            overrides.AddSingleton(Mock.Of<IWindowService>());
        });
        var gateway = services.GetRequiredService<ISaveFileGateway>();
        Window? window = null;
        try
        {
            global::Avalonia.Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var main = services.GetRequiredService<MainWindowViewModel>();
            main.LanguageService.SetLanguage(language);
            main.SelectedTheme = dark ? AppTheme.Dark : AppTheme.Light;
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tests/savefiles", file))) directory = directory.Parent;
            Assert.NotNull(directory);
            var save = Assert.IsAssignableFrom<SAV4>(SaveUtil.GetSaveFile(File.ReadAllBytes(Path.Combine(directory.FullName, "Tests/savefiles", file))));
            gateway.OpenLoadedSave(save, "fixture.sav");
            Assert.True(main.HasSave);
            save.State.Edited = false;
            var before = save.Data.ToArray();
            var general = save.General.ToArray();
            var vm = Assert.IsType<CategorizedEventEditorViewModel>(main.EventFlagsEditor);
            main.SelectSaveNavigationCommand.Execute(null);
            window = new MainWindow { DataContext = main, Width = 900, Height = 600 };
            window.Show();
            await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
            var view = window.GetVisualDescendants().OfType<CategorizedEventEditor>().Single(control => control.IsEffectivelyVisible);
            var grid = view.FindControl<DataGrid>("EventGrid")!;

            foreach (var (kind, index) in new[] { (EventDataKind.Flag, 214), (EventDataKind.Flag, 304), (EventDataKind.Work, save is SAV4HGSS ? 304 : 64), (EventDataKind.Flag, save.EventFlagCount - 1) })
            {
                vm.SelectedBank = vm.Banks.Single(bank => bank.Kind == kind);
                vm.SelectedIndex = index;
                var row = vm.SelectedRow!;
                // Let the bank's ItemsSource binding reach the virtualized grid before
                // asking it to realize a row from that new bank.
                await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
                grid.ScrollIntoView(row, grid.Columns[0]);
                await PKHeX.Testing.HeadlessRenderSettling.Settle(window);
                Assert.True(grid.Bounds.Height >= 40, $"Events grid height {grid.Bounds.Height} for {file}/{language}");
                var realized = Assert.Single(grid.GetVisualDescendants().OfType<DataGridRow>(), control => ReferenceEquals(control.DataContext, row));
                AssertVisible(window, realized);
                var indexInput = view.GetVisualDescendants().OfType<NumericUpDown>().Single();
                var presenter = indexInput.GetVisualDescendants().OfType<TextPresenter>().Single();
                Assert.Equal(index.ToString(CultureInfo.CurrentCulture), presenter.Text);
                var line = Assert.Single(presenter.TextLayout.TextLines);
                var viewport = presenter.GetVisualAncestors().OfType<ScrollContentPresenter>().First();
                Assert.True(line.WidthIncludingTrailingWhitespace <= viewport.Viewport.Width + 1,
                    $"Index {index} text width {line.WidthIncludingTrailingWhitespace} exceeds viewport {viewport.Viewport.Width}");
                Assert.NotEmpty(row.Name);
                foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(button => ReferenceEquals(button.Command, vm.ApplyCommand) || ReferenceEquals(button.Command, vm.CancelCommand) || ReferenceEquals(button.Command, vm.ResetCommand)))
                    AssertVisible(window, button);
                var details = view.FindControl<ContentControl>("EditorDetails")!;
                AssertVisible(window, details);
                Capture(window, $"events4-{Path.GetFileNameWithoutExtension(file)}-{language}-{(dark ? "dark" : "light")}-{kind}-{index}");
            }
            vm.ApplyCommand.Execute(null);
            Assert.Equal(before, save.Data.ToArray());
            Assert.Equal(general, save.General.ToArray());
            Assert.False(save.State.Edited);
        }
        finally
        {
            gateway.CloseSave();
            window?.Close();
            if (window is not null) window.Content = null;
            LocalizedStrings.Instance.SetLanguage(previousLanguage);
            GameInfo.CurrentLanguage = previousData;
            GameInfo.Strings = previousStrings;
            GameInfo.FilteredSources = previousFiltered;
            global::Avalonia.Application.Current.RequestedThemeVariant = previousTheme;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
        }
    }

    private static void AssertVisible(Window window, Control control)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.ClientSize.Width + 1 && point.Y + control.Bounds.Height <= window.ClientSize.Height + 1,
            $"{control.GetType().Name} at {point} + {control.Bounds.Size} outside {window.ClientSize}");
    }

    private static void Capture(Window window, string name)
    {
        using var frame = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        frame.Render(window);
        using var stream = new MemoryStream();
        frame.Save(stream);
        using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
        Assert.Contains(pixels.Pixels, pixel => pixel != pixels.GetPixel(0, 0));
        if (Environment.GetEnvironmentVariable("PKHEX_HEADLESS_CAPTURE_DIR") is { } path)
        {
            Directory.CreateDirectory(path);
            frame.Save(Path.Combine(path, name + ".png"));
        }
    }
}
