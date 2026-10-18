using System.Globalization;
using Avalonia.Data.Converters;
using PKHeX.Application.Shortcuts;
using PKHeX.Avalonia.Input;

namespace PKHeX.Avalonia.Converters;

/// <summary>
/// Converts a concrete <see cref="ShortcutChord"/> to the <see cref="global::Avalonia.Input.KeyGesture"/>
/// a <c>MenuItem.InputGesture</c> displays. Anything else (no shortcut, a portable chord, an
/// unsupported key) converts to <see langword="null"/>, which shows no gesture.
/// </summary>
public sealed class ShortcutChordToKeyGestureConverter : IValueConverter
{
    public static ShortcutChordToKeyGestureConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ShortcutChord chord && ShortcutGestureMapper.TryToKeyGesture(chord, out var gesture) ? gesture : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
