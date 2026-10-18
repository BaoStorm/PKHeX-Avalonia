using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moq;
using PKHeX.Avalonia.Controls;
using PKHeX.Avalonia.Views;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// The shortcut editor and its Settings entry fit their windows in every UI language (issue #224):
/// header, search, the first row's controls and the footer actions stay inside the client area at
/// the default size and at the minimum size.
/// </summary>
public sealed class KeyboardShortcutsViewLayoutTests
{
    public static IEnumerable<object[]> EditorCases => LocalizedStrings.SupportedLanguages.SelectMany(language =>
        new[] { new object[] { language, 640, 580 }, new object[] { language, 480, 400 } });

    public static IEnumerable<object[]> Languages => LocalizedStrings.SupportedLanguages.Select(language => new object[] { language });

    [AvaloniaTheory]
    [MemberData(nameof(EditorCases))]
    public void Editor_KeepsEveryControlInsideTheWindow(string language, int width, int height)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        Window? window = null;
        KeyboardShortcutsViewModel? editor = null;
        try
        {
            // The main view model applies its own startup language, so switch after building it.
            (_, editor, _) = ShortcutTestViewModels.CreateEditor(new AppSettings(), new FakeSettingsStore());
            LocalizedStrings.Instance.SetLanguage(language);
            // Give the first row a conflict, so its message line and Reassign button are laid out too.
            var first = editor.Rows[0];
            first.RecordCommand.Execute(editor.Rows.First(row => row.HasShortcut && row != first).Chord);
            Assert.True(first.HasPendingConflict);

            var view = new KeyboardShortcutsView { DataContext = editor };
            window = new Window { Content = view, Width = width, Height = height };
            window.Show();
            Pump(window);
            Assert.Equal(language, LocalizedStrings.Instance.CurrentLanguage);

            AssertInside(view, view.FindControl<Border>("ShortcutsHeader")!);
            AssertInside(view, view.FindControl<TextBox>("ShortcutsSearch")!);
            AssertInside(view, view.FindControl<TextBlock>("ShortcutsSummary")!);
            var resetAll = view.FindControl<Button>("ShortcutsResetAll")!;
            var cancel = view.FindControl<Button>("ShortcutsCancel")!;
            var save = view.FindControl<Button>("ShortcutsSave")!;
            AssertInside(view, resetAll);
            AssertInside(view, cancel);
            AssertInside(view, save);
            Assert.True(Right(resetAll, view) <= Left(cancel, view), "Reset All overlaps Cancel.");
            Assert.True(Right(cancel, view) <= Left(save, view), "Cancel overlaps Save.");

            var recorder = view.GetVisualDescendants().OfType<ShortcutRecorder>().First(control => ReferenceEquals(control.DataContext, first));
            var rowRoot = recorder.GetVisualAncestors().OfType<Grid>().First();
            var actions = rowRoot.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("shortcut-row-action"))
                .ToArray();
            Assert.Equal(2, actions.Length);
            var reassign = rowRoot.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, first.ReassignCommand));
            // The Reassign button's name keeps its visible text and adds the row it moves the shortcut to.
            Assert.Equal(first.ReassignAutomationName, new ButtonAutomationPeer(reassign).GetName());
            Assert.Contains((string)reassign.Content!, first.ReassignAutomationName);
            var list = view.FindControl<ScrollViewer>("ShortcutsList")!;
            foreach (var control in new Control[] { recorder, actions[0], actions[1], reassign })
            {
                AssertInside(view, control);
                AssertInside(list, control);
            }

            // The title column keeps room to read next to the recorder and its buttons.
            var title = rowRoot.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.True(title.Bounds.Width >= 120, $"Title column is only {title.Bounds.Width}px wide.");
            Assert.True(list.Viewport.Height >= recorder.Bounds.Height * 2, $"Row list viewport is only {list.Viewport.Height}px high.");
        }
        finally
        {
            window?.Close();
            editor?.Dispose();
            LocalizedStrings.Instance.SetLanguage(previous);
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Languages))]
    public void SettingsSection_IsReachableAtTheMinimumSettingsSize(string language)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            var opened = 0;
            var settings = new AppSettings();
            var vm = new SettingsViewModel(settings, new FakeSettingsStore(), new Mock<IThemeService>().Object,
                new Mock<IUiDensityService>().Object, new LanguageService(), UpdateTestDoubles.Coordinator(),
                openKeyboardShortcuts: () =>
                {
                    opened++;
                    return Task.CompletedTask;
                });
            var view = new SettingsView { DataContext = vm };
            window = new Window { Content = view, Width = 390, Height = 420 };
            window.Show();
            Pump(window);
            Assert.Equal(language, LocalizedStrings.Instance.CurrentLanguage);

            var section = view.FindControl<Border>("KeyboardShortcutsSection")!;
            Assert.True(section.IsVisible);
            var button = view.FindControl<Button>("OpenKeyboardShortcutsButton")!;
            button.BringIntoView();
            Pump(window);

            var scroll = section.GetVisualAncestors().OfType<ScrollViewer>().First();
            AssertInside(scroll, button);
            AssertInside(view, button);
            Assert.True(button.IsEffectivelyEnabled);

            // Label in Name (WCAG 2.5.3): the accessible name contains the visible "Customize..." text.
            var name = new ButtonAutomationPeer(button).GetName();
            Assert.Equal(LocalizedStrings.Instance["Settings_KeyboardShortcuts_OpenName"], name);
            Assert.Contains(((string)button.Content!).TrimEnd('.', '…').Trim(), name, StringComparison.CurrentCultureIgnoreCase);

            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
            Pump(window);
            Assert.Equal(1, opened);
        }
        finally
        {
            window?.Close();
            LocalizedStrings.Instance.SetLanguage(previous);
        }
    }

    [AvaloniaFact]
    public void SettingsSection_IsHiddenWithoutAnEditor()
    {
        var vm = new SettingsViewModel(new AppSettings(), new FakeSettingsStore(), new Mock<IThemeService>().Object,
            new Mock<IUiDensityService>().Object, new LanguageService(), UpdateTestDoubles.Coordinator());
        var view = new SettingsView { DataContext = vm };
        var window = new Window { Content = view, Width = 390, Height = 492 };
        try
        {
            window.Show();
            Pump(window);
            Assert.False(view.FindControl<Border>("KeyboardShortcutsSection")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertInside(Visual container, Control control)
    {
        Assert.True(control.IsEffectivelyVisible, $"{Describe(control)} is not visible.");
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, $"{Describe(control)} has no size.");
        var origin = control.TranslatePoint(default, container)
            ?? throw new InvalidOperationException($"{Describe(control)} is not inside {container.GetType().Name}.");
        const double tolerance = 0.5;
        Assert.True(origin.X >= -tolerance && origin.Y >= -tolerance
                    && origin.X + control.Bounds.Width <= container.Bounds.Width + tolerance
                    && origin.Y + control.Bounds.Height <= container.Bounds.Height + tolerance,
            $"{Describe(control)} at {origin} size {control.Bounds.Size} is outside {container.GetType().Name} {container.Bounds.Size}.");
    }

    private static double Left(Control control, Visual container) => control.TranslatePoint(default, container)!.Value.X;

    private static double Right(Control control, Visual container) => Left(control, container) + control.Bounds.Width;

    private static string Describe(Control control) =>
        string.IsNullOrEmpty(control.Name) ? control.GetType().Name : $"{control.GetType().Name} '{control.Name}'";
}
