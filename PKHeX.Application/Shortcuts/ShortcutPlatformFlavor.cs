namespace PKHeX.Application.Shortcuts;

/// <summary>
/// Keyboard convention of the running platform. Decides which concrete modifier the portable
/// <see cref="ShortcutModifiers.Primary"/> placeholder stands for.
/// </summary>
public enum ShortcutPlatformFlavor
{
    /// <summary>Windows and Linux: the command modifier is Control.</summary>
    Standard,

    /// <summary>macOS: the command modifier is Command (⌘, <see cref="ShortcutModifiers.Meta"/>).</summary>
    Apple,
}
