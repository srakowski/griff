using System.Windows;
using System.Windows.Input;
using Griff.Editor.Core.Commands;

namespace Griff.Editor.Features.Play;

/// <summary>
/// The Game menu's shortcuts over a running game. While a game plays it takes the keys pressed in its window before the
/// editor's shortcuts see them; these (Pause, Stop, Step Frame, Play Fullscreen...) reach past it, so they work over the
/// game in the Game tab and fullscreen alike. A key whose command can't run now (F5's Play, while playing) is the game's.
/// </summary>
public sealed class PlayShortcuts(CommandRegistry commands, PlayMode play)
{
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
            if (!gesture.Matches(window, e) || !command.CanExecute(null))
                continue;
            e.Handled = true;
            command.Execute(null);
            return;
        }
    }
}
