using System.Diagnostics.CodeAnalysis;
using Avalonia.Input;
using PKHeX.Application.Shortcuts;

namespace PKHeX.Avalonia.Input;

/// <summary>
/// Converts between the framework-free <see cref="ShortcutChord"/> and Avalonia's
/// <see cref="KeyGesture"/> / key events. The key is mapped by enum name, never through
/// <see cref="KeyGesture.Parse"/>: that parser reads a bare <c>"1"</c> as the numeric enum value 1
/// (<see cref="Key.Cancel"/>), so <c>"Ctrl+1"</c> never matched the digit key.
/// </summary>
public static class ShortcutGestureMapper
{
    private const KeyModifiers ConcreteKeyModifiers =
        KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta;

    /// <summary>
    /// Builds the gesture for a concrete chord (no <see cref="ShortcutModifiers.Primary"/>; resolve it
    /// first) whose key is in <see cref="ShortcutKeyVocabulary"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The chord is not concrete or its key is not supported.</exception>
    public static KeyGesture ToKeyGesture(ShortcutChord concrete)
    {
        if (!TryToKeyGesture(concrete, out var gesture))
        {
            throw new ArgumentException(
                $"'{concrete}' is not a concrete chord with a supported key; resolve Primary before binding.",
                nameof(concrete));
        }

        return gesture;
    }

    /// <summary>Non-throwing form of <see cref="ToKeyGesture"/>.</summary>
    public static bool TryToKeyGesture(ShortcutChord concrete, [NotNullWhen(true)] out KeyGesture? gesture)
    {
        gesture = null;
        if ((concrete.Modifiers & ShortcutModifiers.Primary) != 0 || !ShortcutKeyVocabulary.IsKnown(concrete.Key))
            return false;
        if (!Enum.TryParse<Key>(concrete.Key, ignoreCase: false, out var key))
            return false;

        gesture = new KeyGesture(key, ToKeyModifiers(concrete.Modifiers));
        return true;
    }

    /// <summary>
    /// The chord a key press stands for, or <see langword="null"/> while only a modifier, a lock key or
    /// an IME/dead-key placeholder is pressed. Keys outside the vocabulary are returned as-is (by enum
    /// name); <see cref="ShortcutPolicy"/> rejects them with a reason the user can read.
    /// </summary>
    public static ShortcutChord? FromKeyEvent(Key key, KeyModifiers modifiers)
    {
        if (IsNonShortcutKey(key))
            return null;

        return new ShortcutChord(ToShortcutModifiers(modifiers), key.ToString());
    }

    /// <summary>The chord of an Avalonia gesture, or <see langword="null"/> for a modifier-only gesture.</summary>
    public static ShortcutChord? FromGesture(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        return FromKeyEvent(gesture.Key, gesture.KeyModifiers);
    }

    /// <summary>Maps the concrete modifier bits; <see cref="ShortcutModifiers.Primary"/> is dropped.</summary>
    public static KeyModifiers ToKeyModifiers(ShortcutModifiers modifiers) =>
        (KeyModifiers)((int)modifiers & (int)ConcreteKeyModifiers);

    /// <summary>Maps Avalonia modifiers; the bit values are shared with <see cref="ShortcutModifiers"/>.</summary>
    public static ShortcutModifiers ToShortcutModifiers(KeyModifiers modifiers) =>
        (ShortcutModifiers)(int)(modifiers & ConcreteKeyModifiers);

    /// <summary>True for keys that never complete a shortcut on their own.</summary>
    public static bool IsNonShortcutKey(Key key) => key switch
    {
        Key.None
            or Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin
            or Key.ImeProcessed or Key.System or Key.DeadCharProcessed
            or Key.CapsLock or Key.NumLock or Key.Scroll => true,
        _ => false,
    };
}
