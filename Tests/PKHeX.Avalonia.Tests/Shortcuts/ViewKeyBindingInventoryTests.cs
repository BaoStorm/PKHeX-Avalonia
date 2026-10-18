using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PKHeX.Application.Shortcuts;
using PKHeX.Avalonia.Input;

namespace PKHeX.Avalonia.Tests.Shortcuts;

/// <summary>
/// Keeps <see cref="ViewScopedShortcuts"/> in step with the views (issue #224). A view binding runs
/// before a main-window shortcut with the same chord while focus is inside that view, so every
/// shortcut-capable chord a view binds must be reserved, or the editor would offer a shortcut that
/// silently does nothing there.
/// </summary>
public sealed class ViewKeyBindingInventoryTests
{
    // Gesture="…" on KeyBinding and HotKey="…" on buttons/menu items; InputGesture is display-only.
    private static readonly Regex GestureAttribute =
        new("(?<![A-Za-z.:])(?:Gesture|HotKey)=\"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant);

    [Fact]
    public void EveryShortcutCapableViewGesture_IsReserved()
    {
        var reserved = ViewScopedShortcuts.StorageGestures
            .Concat(ViewScopedShortcuts.ControlGestures)
            .Select(reservation => reservation.Chord)
            .ToHashSet();
        var missing = new List<string>();
        var checkedGestures = 0;

        foreach (var (relative, text) in ReadViews())
        {
            foreach (Match match in GestureAttribute.Matches(text))
            {
                var value = match.Groups["value"].Value;
                // Bound gestures are configurable shortcuts; Escape, Enter, arrows, Delete and the
                // like are outside the shortcut vocabulary and can never be assigned.
                if (value.StartsWith('{') || !ShortcutChord.TryParse(value, out var chord))
                    continue;

                checkedGestures++;
                if (!reserved.Contains(chord))
                    missing.Add($"{relative}: {value}");
            }
        }

        Assert.True(checkedGestures > 0, "No shortcut-capable view gestures were found; the scan is broken.");
        Assert.True(missing.Count == 0,
            "Add these view gestures to ViewScopedShortcuts so the shortcut editor reserves them:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void MainWindow_DeclaresOnlyTheFixedEscapeBinding()
    {
        var mainWindow = File.ReadAllText(Path.Combine(FindRepoRoot(), "PKHeX.Avalonia", "Views", "MainWindow.axaml"));

        // The configurable shortcuts run from KeyboardShortcutBinder; literal Ctrl gestures here would
        // bypass the user's settings (and "Ctrl+1" parsed as Key.Cancel, not the digit key).
        Assert.DoesNotContain("Gesture=\"Ctrl+", mainWindow);
        Assert.Contains("<KeyBinding Gesture=\"Escape\" Command=\"{Binding CloseToolLauncherCommand}\" />", mainWindow);

        // Every menu gesture shows the resolved shortcut instead of a literal.
        var inputGestures = Regex.Matches(mainWindow, "InputGesture=\"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["value"].Value)
            .ToArray();
        Assert.NotEmpty(inputGestures);
        Assert.All(inputGestures, value => Assert.StartsWith("{Binding ", value, StringComparison.Ordinal));
        Assert.Contains("<Setter Property=\"InputGesture\" Value=\"{Binding Shortcut, Converter={x:Static conv:ShortcutChordToKeyGestureConverter.Instance}}\" />", mainWindow);
    }

    private static IEnumerable<(string Relative, string Text)> ReadViews()
    {
        var root = FindRepoRoot();
        var host = Path.Combine(root, "PKHeX.Avalonia");
        foreach (var path in Directory.EnumerateFiles(host, "*.axaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                continue;
            yield return (relative, File.ReadAllText(path));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "PKHeX.Avalonia")))
        {
            var parent = Directory.GetParent(dir)
                ?? throw new DirectoryNotFoundException("Could not find repository root");
            dir = parent.FullName;
        }

        return dir;
    }
}
