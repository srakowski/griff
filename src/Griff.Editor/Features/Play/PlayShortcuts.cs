using System.Windows;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Platform.Windows;
using Griff.Runtime;

namespace Griff.Editor.Features.Play;

/// <summary>
/// The Game menu's shortcuts over a running game. While a game plays it takes the keys pressed in its window before the
/// editor's shortcuts see them; these reach past it, so they work over the game in the Game tab and fullscreen alike,
/// with these exceptions, which are the game's: a key whose command can't run now (F5's Play, while playing), Step Frame
/// (F10), which is for a paused game, and any other plain key (no Ctrl, Alt or Shift) that one of the game's input
/// actions is bound to. Pause (F6), Stop (Shift+F5) and Play Fullscreen (Alt+Enter) always win, even if the game binds
/// them: they are the player's way out.
/// </summary>
public sealed class PlayShortcuts(CommandRegistry commands, PlayMode play)
{
    /// <summary>The commands that keep their shortcut over a running game whatever it binds: the way out of it.</summary>
    private static readonly string[] AlwaysWin = ["Game.Pause", "Game.Stop", "Game.Fullscreen"];

    /// <summary>The commands for a paused game only: their shortcut is the game's while it plays.</summary>
    private static readonly string[] PausedOnly = ["Game.Step"];

    private (KeyGesture Gesture, ICommand Command)[]? _shortcuts;

    /// <summary>Has the Game menu's shortcuts take <paramref name="window"/>'s keys first: before a game listens to it.</summary>
    public void Attach(Window window) => window.PreviewKeyDown += (_, e) => OnPreviewKeyDown(window, e);

    private void OnPreviewKeyDown(Window window, KeyEventArgs e)
    {
        if (!play.GameTakesKeys)
            return;

        _shortcuts ??= [.. commands.All
            .Where(c => c.Placement?.Menu == Menus.Game)
            .SelectMany(c => new[] { c.Gesture, c.AlternateGesture }.OfType<KeyGesture>().Select(g => (g, (ICommand)c)))];
        foreach (var (gesture, command) in _shortcuts)
        {
            if (!gesture.Matches(window, e) || !command.CanExecute(null) ||
                (command is EditorCommand c && GameKeeps(c.Id, gesture, play.Game?.Input)))
                continue;
            e.Handled = true;
            command.Execute(null);
            return;
        }
    }

    /// <summary>
    /// Whether <paramref name="gesture"/>, the shortcut of the command <paramref name="commandId"/>, is the running game's
    /// rather than the editor's: the command is for a paused game, or the key has no modifiers and one of the
    /// <paramref name="input"/>'s actions is bound to it. Never for Pause, Stop and Play Fullscreen.
    /// </summary>
    internal static bool GameKeeps(string commandId, KeyGesture gesture, IInput? input)
    {
        if (AlwaysWin.Contains(commandId))
            return false;
        if (PausedOnly.Contains(commandId))
            return true;
        if (input is null || gesture.Modifiers != ModifierKeys.None)
            return false;
        var key = KeyMap.ToGriff(gesture.Key);
        return key != Griff.Input.Key.None && input.Actions.Any(a => input.BindingsOf(a).Any(b => b.Key == key));
    }
}
