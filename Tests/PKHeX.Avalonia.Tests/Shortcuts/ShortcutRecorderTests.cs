using System.Linq;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PKHeX.Application.Shortcuts;
using PKHeX.Avalonia.Controls;
using PKHeX.Avalonia.Views;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// The key recorder of the shortcut editor (issue #224), driven with real key events inside the real
/// editor view, so the dialog's own Escape binding takes part in the routing.
/// </summary>
public sealed class ShortcutRecorderTests
{
    private const string Encounter = "OpenEncounterDatabase";

    [AvaloniaFact]
    public void Enter_StartsRecording_ModifiersShowProgress_AndTheChordIsRecorded()
    {
        // Free Ctrl+N so it can be recorded on another action without a conflict.
        var settings = new AppSettings();
        settings.KeyboardShortcuts[Encounter] = string.Empty;
        using var host = Host.Create(settings, ShortcutActionId.CloseFile);
        Assert.Equal(LocalizedStrings.Instance["Shortcuts_None"], host.Recorder.Content);

        host.Press(PhysicalKey.Enter);
        Assert.True(host.Recorder.IsRecording);
        Assert.True(host.Row.IsRecording);
        Assert.True(host.Editor.IsRecording);
        Assert.Equal(LocalizedStrings.Instance["Shortcuts_Recording"], host.Recorder.Content);

        // A modifier alone shows what is held and records nothing.
        host.Down(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        Assert.Equal("Ctrl+…", host.Recorder.PartialText);
        Assert.Equal("Ctrl+…", host.Recorder.Content);
        Assert.True(host.Recorder.IsRecording);
        Assert.Null(host.Row.Chord);

        host.Press(PhysicalKey.N, RawInputModifiers.Control);
        host.Up(PhysicalKey.ControlLeft);

        Assert.Equal(new ShortcutChord(ShortcutModifiers.Control, "N"), host.Row.Chord);
        Assert.False(host.Recorder.IsRecording);
        Assert.False(host.Row.IsRecording);
        Assert.Null(host.Recorder.PartialText);
        Assert.Equal("Ctrl+N", host.Recorder.Content);
    }

    [AvaloniaFact]
    public void ReleasingAModifier_RemovesItFromTheProgress()
    {
        using var host = Host.Create(new AppSettings(), Encounter);
        host.Press(PhysicalKey.Enter);

        host.Down(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        host.Down(PhysicalKey.ShiftLeft, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal("Ctrl+Shift+…", host.Recorder.Content);

        // Some platforms still report the released modifier in the key-up event; it is not held.
        host.Up(PhysicalKey.ControlLeft, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal("Shift+…", host.Recorder.PartialText);
        Assert.Equal("Shift+…", host.Recorder.Content);

        host.Up(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
        Assert.Null(host.Recorder.PartialText);
        Assert.Equal(LocalizedStrings.Instance["Shortcuts_Recording"], host.Recorder.Content);
        Assert.True(host.Recorder.IsRecording);

        // What the button showed is what gets recorded.
        host.Down(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        host.Press(PhysicalKey.E, RawInputModifiers.Control);
        Assert.Equal(new ShortcutChord(ShortcutModifiers.Control, "E"), host.Row.Chord);
    }

    [AvaloniaFact]
    public void AccessibleName_FollowsTheShortcutAndTheRecordingState()
    {
        using var host = Host.Create(new AppSettings(), Encounter);
        var peer = new ButtonAutomationPeer(host.Recorder);
        var title = host.Row.Title;
        Assert.Equal(LocalizedStrings.Instance.Format("Shortcuts_RecorderName", title, "Ctrl+N"), peer.GetName());

        host.Press(PhysicalKey.Enter);
        Assert.Equal(LocalizedStrings.Instance.Format("Shortcuts_RecorderName", title, LocalizedStrings.Instance["Shortcuts_Recording"]), peer.GetName());

        host.Press(PhysicalKey.E, RawInputModifiers.Control);
        Assert.Equal(LocalizedStrings.Instance.Format("Shortcuts_RecorderName", title, "Ctrl+E"), peer.GetName());

        // The row's message is a polite live region that now names the new shortcut.
        var message = host.View.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => ReferenceEquals(text.DataContext, host.Row) && text.Text == host.Row.Message);
        Assert.Equal(LocalizedStrings.Instance.Format("Shortcuts_Assigned", "Ctrl+E"), message.Text);
        Assert.Equal(global::Avalonia.Automation.AutomationLiveSetting.Polite, global::Avalonia.Automation.AutomationProperties.GetLiveSetting(message));
        Assert.True(message.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Click_StartsRecording()
    {
        using var host = Host.Create(new AppSettings(), Encounter);

        ((IInvokeProvider)new ButtonAutomationPeer(host.Recorder)).Invoke();
        host.Pump();

        Assert.True(host.Recorder.IsRecording);
        Assert.True(host.Row.IsRecording);
    }

    [AvaloniaFact]
    public void Escape_CancelsOnlyTheRecording_ThenClosesTheEditor()
    {
        using var host = Host.Create(new AppSettings(), Encounter);
        var closed = 0;
        host.Editor.CloseRequested = () => closed++;

        host.Press(PhysicalKey.Enter);
        Assert.True(host.Recorder.IsRecording);

        host.Press(PhysicalKey.Escape);
        Assert.False(host.Recorder.IsRecording);
        Assert.Equal(0, closed);
        Assert.Equal(new ShortcutChord(ShortcutModifiers.Control, "N"), host.Row.Chord);

        // Not recording: the dialog's Escape binding cancels the editor.
        host.Press(PhysicalKey.Escape);
        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public void Backspace_ClearsTheShortcut()
    {
        using var host = Host.Create(new AppSettings(), Encounter);

        host.Press(PhysicalKey.Enter);
        host.Press(PhysicalKey.Backspace);

        Assert.Null(host.Row.Chord);
        Assert.False(host.Recorder.IsRecording);
        Assert.Equal(LocalizedStrings.Instance["Shortcuts_None"], host.Recorder.Content);
    }

    [AvaloniaFact]
    public void Tab_EndsRecording_WithoutBeingConsumed_SoFocusMoves()
    {
        using var host = Host.Create(new AppSettings(), Encounter);
        host.Press(PhysicalKey.Enter);
        Assert.True(host.Recorder.IsRecording);

        host.Press(PhysicalKey.Tab);

        Assert.False(host.Recorder.IsRecording);
        Assert.Equal(new ShortcutChord(ShortcutModifiers.Control, "N"), host.Row.Chord);
        var focused = TopLevel.GetTopLevel(host.Window)!.FocusManager!.GetFocusedElement();
        Assert.NotNull(focused);
        Assert.NotSame(host.Recorder, focused);
    }

    [AvaloniaFact]
    public void LosingFocus_StopsRecording()
    {
        using var host = Host.Create(new AppSettings(), Encounter);
        host.Press(PhysicalKey.Enter);
        Assert.True(host.Recorder.IsRecording);

        host.View.FindControl<TextBox>("ShortcutsSearch")!.Focus();
        host.Pump();

        Assert.False(host.Recorder.IsRecording);
        Assert.False(host.Editor.IsRecording);
        Assert.Equal(new ShortcutChord(ShortcutModifiers.Control, "N"), host.Row.Chord);
    }

    [AvaloniaFact]
    public void RejectedChord_ShowsTheReason_AndKeepsTheShortcut()
    {
        using var host = Host.Create(new AppSettings(), Encounter);

        host.Press(PhysicalKey.Enter);
        host.Press(PhysicalKey.C, RawInputModifiers.Control);

        Assert.False(host.Recorder.IsRecording);
        Assert.Equal(new ShortcutChord(ShortcutModifiers.Control, "N"), host.Row.Chord);
        Assert.True(host.Row.HasMessage);
        Assert.Equal("Ctrl+N", host.Recorder.Content);
    }

    /// <summary>The real editor view in a headless window, focused on one row's recorder.</summary>
    private sealed class Host : IDisposable
    {
        private Host(Window window, KeyboardShortcutsView view, KeyboardShortcutsViewModel editor, KeyboardShortcutRowViewModel row, ShortcutRecorder recorder)
        {
            Window = window;
            View = view;
            Editor = editor;
            Row = row;
            Recorder = recorder;
        }

        public Window Window { get; }
        public KeyboardShortcutsView View { get; }
        public KeyboardShortcutsViewModel Editor { get; }
        public KeyboardShortcutRowViewModel Row { get; }
        public ShortcutRecorder Recorder { get; }

        public static Host Create(AppSettings settings, string actionId)
        {
            var (_, editor, _) = ShortcutTestViewModels.CreateEditor(settings, new FakeSettingsStore());
            var view = new KeyboardShortcutsView { DataContext = editor };
            var window = new Window { Content = view, Width = 640, Height = 580 };
            window.Show();
            PumpWindow(window);
            var row = Assert.Single(editor.Rows, candidate => candidate.ActionId == actionId);
            var host = new Host(window, view, editor, row, FindRecorder(view, row));
            host.Recorder.Focus();
            host.Pump();
            Assert.True(host.Recorder.IsFocused);
            return host;
        }

        public void Pump() => PumpWindow(Window);

        private static void PumpWindow(Window window)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        public void Down(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPressQwerty(key, modifiers);
            Pump();
        }

        public void Up(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyReleaseQwerty(key, modifiers);
            Pump();
        }

        public void Press(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Down(key, modifiers);
            Up(key, modifiers);
        }

        public void Dispose()
        {
            Window.Close();
            Editor.Dispose();
            Dispatcher.UIThread.RunJobs();
        }

        private static ShortcutRecorder FindRecorder(KeyboardShortcutsView view, KeyboardShortcutRowViewModel row) =>
            Assert.Single(view.GetVisualDescendants().OfType<ShortcutRecorder>(),
                recorder => ReferenceEquals(recorder.DataContext, row));
    }
}
