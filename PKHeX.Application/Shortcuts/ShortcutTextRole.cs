namespace PKHeX.Application.Shortcuts;

/// <summary>
/// Text-editing meaning an application action shares with the platform's text gestures. An action
/// with a role may use the matching reserved text gesture (for example the application Undo may use
/// Ctrl+Z); the host then lets a focused text box keep that gesture for its own text undo.
/// </summary>
public enum ShortcutTextRole
{
    None,
    Undo,
    Redo,
}
