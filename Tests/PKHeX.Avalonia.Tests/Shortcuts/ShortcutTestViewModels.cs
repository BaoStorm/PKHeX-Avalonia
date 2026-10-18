using Moq;
using PKHeX.Application.Shortcuts;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// Builds a <see cref="MainWindowViewModel"/> with mocked ports, so the shortcut catalog and the
/// shortcut editor can be tested without the composed app.
/// </summary>
internal static class ShortcutTestViewModels
{
    public static MainWindowViewModel CreateMainViewModel(
        AppSettings? settings = null,
        FakeSettingsStore? store = null,
        KeyboardShortcutService? service = null,
        LanguageService? languageService = null,
        IDialogService? dialogService = null,
        IWindowService? windowService = null)
    {
        settings ??= new AppSettings();
        store ??= new FakeSettingsStore();
        return new MainWindowViewModel(
            new Mock<ISaveFileGateway>().Object,
            dialogService ?? new Mock<IDialogService>().Object,
            windowService ?? new Mock<IWindowService>().Object,
            new Mock<ISpriteRenderer>().Object,
            new Mock<ISlotService>().Object,
            new Mock<IClipboardService>().Object,
            new Mock<IQrCodeService>().Object,
            UpdateTestDoubles.Coordinator(),
            new Mock<ISaveBackupService>().Object,
            settings,
            store,
            new Mock<IThemeService>().Object,
            new Mock<IUiDensityService>().Object,
            new UndoRedoService(),
            languageService ?? new LanguageService(),
            new Mock<IAutoLegalityService>().Object,
            new Mock<PKHeX.Application.Abstractions.LiveHex.ILiveHexService>().Object,
            new Mock<ILivingDexService>().Object,
            new Mock<PKHeX.Application.Abstractions.GiftRecords.IGiftRecordProvider>().Object,
            keyboardShortcuts: service);
    }

    /// <summary>
    /// A main view model and an open shortcut editor over the same settings and store, on the
    /// Windows/Linux fallback platform (Ctrl is the command modifier).
    /// </summary>
    public static (MainWindowViewModel Main, KeyboardShortcutsViewModel Editor, KeyboardShortcutService Service) CreateEditor(
        AppSettings settings,
        FakeSettingsStore store,
        IDialogService? dialogs = null,
        IShortcutPlatform? platform = null)
    {
        var service = new KeyboardShortcutService(settings, store, platform ?? StandardShortcutPlatform.Instance);
        var main = CreateMainViewModel(settings, store, service);
        var editor = new KeyboardShortcutsViewModel(main.Shortcuts, service, dialogs ?? new Mock<IDialogService>().Object);
        return (main, editor, service);
    }
}
