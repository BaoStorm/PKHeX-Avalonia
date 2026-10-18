using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Input;

/// <summary>
/// Keeps the main window's configurable <see cref="KeyBinding"/>s in step with
/// <see cref="MainWindowViewModel.ShortcutBindings"/>. Only the bindings this binder created are
/// ever removed: the list also holds XAML bindings (Escape for the launcher) and the bindings
/// <see cref="HotKeyManager"/> adds, so it is never cleared.
/// </summary>
/// <remarks>
/// Rebinding while a shortcut runs is safe: the keyboard device matches bindings on a copy of the list.
/// </remarks>
internal sealed class KeyboardShortcutBinder : IDisposable
{
    private readonly Window _window;
    private readonly MainWindowViewModel _viewModel;
    private readonly List<KeyBinding> _owned = [];
    private bool _disposed;

    public KeyboardShortcutBinder(Window window, MainWindowViewModel viewModel)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _viewModel.ShortcutBindingsChanged += OnShortcutBindingsChanged;
        Rebind();
    }

    /// <summary>The bindings this binder currently owns on the window.</summary>
    internal IReadOnlyList<KeyBinding> OwnedBindings => _owned;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.ShortcutBindingsChanged -= OnShortcutBindingsChanged;
        RemoveOwned();
    }

    private void OnShortcutBindingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Rebind();
        else
            Dispatcher.UIThread.Post(Rebind);
    }

    private void Rebind()
    {
        if (_disposed)
            return;

        RemoveOwned();
        foreach (var entry in _viewModel.ShortcutBindings)
        {
            var binding = new KeyBinding
            {
                Gesture = ShortcutGestureMapper.ToKeyGesture(entry.Chord),
                Command = new ShortcutKeyCommand(entry, _window),
            };
            _owned.Add(binding);
            _window.KeyBindings.Add(binding);
        }
    }

    private void RemoveOwned()
    {
        foreach (var binding in _owned)
            _window.KeyBindings.Remove(binding);
        _owned.Clear();
    }
}
