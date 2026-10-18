using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Input;

/// <summary>
/// Runs the main window's configurable shortcuts, kept in step with
/// <see cref="MainWindowViewModel.ShortcutBindings"/>. Each shortcut is a <see cref="KeyBinding"/>
/// that this binder matches from the key events itself. The bindings are deliberately not added to
/// <see cref="InputElement.KeyBindings"/>.
/// </summary>
/// <remarks>
/// <para>
/// Key bindings run before the key event is raised, so they hide the key from the window's
/// <see cref="IAccessKeyHandler"/>. For a chord with Alt (Alt+F5, or Cmd+Option+N on macOS), the
/// handler then never learns that Alt was part of a chord, and releasing Alt opens the menu bar,
/// which takes keyboard focus. The access-key handler must see every key first.
/// </para>
/// <para>
/// The access-key handler listens for the tunnelling key event on the window. Tunnelling handlers of
/// one element run in reverse order of registration, so a tunnelling handler added to the window
/// later would run before it. The binder therefore matches keys in two places:
/// </para>
/// <list type="bullet">
/// <item>A tunnelling handler on the window's content, the next element on the route. The access-key
/// handler has seen the key, and the shortcut still runs before the focused control, as a window
/// key binding did.</item>
/// <item>A bubbling class handler on the window, for a key raised outside the content: the window
/// itself when no element has focus. Class handlers of an element run before its instance handlers,
/// so the access-key handler's bubbling handler, which runs Alt access keys, comes after it.</item>
/// </list>
/// <para>
/// Without a focused element the access-key handler ignores key presses but still opens the menu bar
/// when Alt is released after an earlier Alt chord. After an Alt chord that ran from the window
/// handler, the binder therefore consumes the Alt release before the access-key handler sees it.
/// </para>
/// <para>
/// Only keys that come from this window are handled. A native popup (a menu or a context menu) is a
/// separate top level whose key events still route through the window's content; window key
/// bindings never ran for it either.
/// </para>
/// <para>
/// Rebinding while a shortcut runs is safe: the handlers match on a copy of the list.
/// </para>
/// </remarks>
internal sealed class KeyboardShortcutBinder : IDisposable
{
    private readonly Window _window;
    private readonly InputElement _content;
    private readonly MainWindowViewModel _viewModel;
    private readonly IDisposable _windowHandler;
    private readonly List<KeyBinding> _owned = [];
    private KeyEventArgs? _seenByContent;
    private bool _consumeAltRelease;
    private bool _disposed;

    /// <exception cref="InvalidOperationException">The window has no content yet.</exception>
    public KeyboardShortcutBinder(Window window, MainWindowViewModel viewModel)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _content = window.Content as InputElement
            ?? throw new InvalidOperationException("The window's content must exist before its shortcuts are bound.");

        _viewModel.ShortcutBindingsChanged += OnShortcutBindingsChanged;
        _content.AddHandler(InputElement.KeyDownEvent, OnContentKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _windowHandler = InputElement.KeyDownEvent.AddClassHandler<Window>(OnWindowKeyDown, RoutingStrategies.Bubble);
        // Added after the access-key handler's, so it runs first (tunnelling handlers run in reverse).
        _window.AddHandler(InputElement.KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        Rebind();
    }

    /// <summary>The shortcut bindings this binder currently matches.</summary>
    internal IReadOnlyList<KeyBinding> OwnedBindings => _owned;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.ShortcutBindingsChanged -= OnShortcutBindingsChanged;
        _content.RemoveHandler(InputElement.KeyDownEvent, OnContentKeyDown);
        _windowHandler.Dispose();
        _window.RemoveHandler(InputElement.KeyUpEvent, OnWindowKeyUp);
        _owned.Clear();
        _seenByContent = null;
        _consumeAltRelease = false;
    }

    private void OnContentKeyDown(object? sender, KeyEventArgs e)
    {
        _seenByContent = e;
        TryHandle(e);
    }

    // Keys that tunnelled through the content were matched there, handled or not.
    private void OnWindowKeyDown(Window window, KeyEventArgs e)
    {
        if (!ReferenceEquals(window, _window) || ReferenceEquals(e, _seenByContent))
            return;

        TryHandle(e);
        if (e.Handled && (e.KeyModifiers & KeyModifiers.Alt) != 0)
            _consumeAltRelease = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftAlt or Key.RightAlt))
            return;

        if (_consumeAltRelease)
            e.Handled = true;
        _consumeAltRelease = false;
    }

    private void TryHandle(KeyEventArgs e)
    {
        if (_disposed || e.Handled || _owned.Count == 0)
            return;
        if (!ReferenceEquals(TopLevel.GetTopLevel(e.Source as Visual), _window))
            return;

        foreach (var binding in _owned.ToArray())
        {
            if (e.Handled)
                break;
            binding.TryHandle(e);
        }
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

        _owned.Clear();
        foreach (var entry in _viewModel.ShortcutBindings)
        {
            _owned.Add(new KeyBinding
            {
                Gesture = ShortcutGestureMapper.ToKeyGesture(entry.Chord),
                Command = new ShortcutKeyCommand(entry, _window),
            });
        }
    }
}
