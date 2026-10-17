using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Play;
using Griff.Editor.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Game &gt; Play Fullscreen (Alt+Enter) plays the game over the whole screen and back to the Game tab, still playing: its
/// state is the play mode's <c>IsFullscreen</c>, which the game's own <c>Game.Fullscreen</c> follows and sets, and agents
/// reach it as a tool. (The window itself needs a screen; with none, as here, the state is all there is.)
/// </summary>
public sealed class PlayFullscreenTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-fullscreen-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // Lets what was put off to after the game's step (a change the game asked for) happen.
    private static void Settle() => EditorHost.Pump(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task);

    [Fact]
    public void PlayFullscreen_PlaysFullscreen_AndAgainGoesBackToTheTab_StillPlaying() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Full"));
        var play = services.GetRequiredService<PlayMode>();
        var command = services.GetRequiredService<CommandRegistry>().Get("Game.Fullscreen");

        Assert.Equal(Key.Enter, command.Gesture!.Key);
        Assert.Equal(ModifierKeys.Alt, command.Gesture.Modifiers);
        Assert.Equal(Menus.Game, command.Placement!.Menu);
        Assert.True(command.CanExecute());
        Assert.False(command.IsChecked);

        // Not playing yet, it plays, fullscreen.
        EditorHost.Pump(command.ExecuteAsync());
        try
        {
            Assert.True(play.IsPlaying);
            Assert.True(play.IsFullscreen);
            Assert.True(command.IsChecked);
            Assert.True(play.Game!.Fullscreen);

            // Again: back in the tab, the same game playing on.
            var game = play.Game;
            EditorHost.Pump(command.ExecuteAsync());
            Assert.True(play.IsPlaying);
            Assert.False(play.IsPaused);
            Assert.False(play.IsFullscreen);
            Assert.False(command.IsChecked);
            Assert.Same(game, play.Game);
            Assert.False(game.Fullscreen);

            // While playing it toggles; paused, the game stays fullscreen (the timeline shows over it to rewind).
            EditorHost.Pump(command.ExecuteAsync());
            Assert.True(play.IsFullscreen);
            play.TogglePause();
            Assert.True(play.IsPaused);
            Assert.True(play.IsFullscreen);
            Assert.True(command.CanExecute());
        }
        finally
        {
            play.Stop();
        }

        // Stop brings it back; it only goes fullscreen while playing.
        Assert.False(play.IsFullscreen);
        play.IsFullscreen = true;
        Assert.False(play.IsFullscreen);
    });

    [Fact]
    public void TheGamesOwnFullscreen_FollowsAndSetsIt_AndPlayStartsInTheTab() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Toggled"));
        var play = services.GetRequiredService<PlayMode>();

        play.Play();
        try
        {
            // A Fullscreen toggle (or a script) sets the game's own setting: the editor goes fullscreen once the step is done.
            Assert.False(play.Game!.Fullscreen);
            play.Game.Fullscreen = true;
            Settle();
            Assert.True(play.IsFullscreen);

            play.Game.Fullscreen = false;
            Settle();
            Assert.False(play.IsFullscreen);
        }
        finally
        {
            play.Stop();
        }

        // A saved "fullscreen" (from a built game's habit, or a script) doesn't throw Play out of the Game tab, and the
        // game's setting says what's showing.
        var saved = new Griff.Runtime.SaveData(Griff.Runtime.SaveData.EditorPath(workspace.Project!.RootPath));
        saved.Set(Griff.Runtime.Game.FullscreenSaveKey, true);
        saved.Flush();
        play.Play();
        try
        {
            Settle();
            Assert.False(play.IsFullscreen);
            Assert.False(play.Game!.Fullscreen);
        }
        finally
        {
            play.Stop();
        }
    });

    [Fact]
    public void PlayFullscreen_IsAToolForAgents_AndItsShortcutIsItsOwn() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Agents"));
        var play = services.GetRequiredService<PlayMode>();
        var tools = services.GetRequiredService<AgentServer>().Tools;
        Assert.Contains("whole of the screen", tools["Game_Fullscreen"].Description);

        // No other command answers to Alt+Enter, in the editor's window or the fullscreen one (which takes them all).
        var commands = services.GetRequiredService<CommandRegistry>().All;
        var altEnter = MainWindow.Shortcuts(commands).Where(s => s.Gesture.Key == Key.Enter && s.Gesture.Modifiers == ModifierKeys.Alt).ToList();
        Assert.Equal(["Game.Fullscreen"], altEnter.Select(s => s.Command.Id));

        try
        {
            EditorHost.Pump(tools["Game_Fullscreen"].Invoke(Griff.Agent.ToolArgs.Empty));
            Assert.True(play.IsFullscreen);

            // While it runs the game takes the keys (the Game menu's shortcuts reach past it); paused, they're the editor's.
            Assert.True(play.GameTakesKeys);
            play.TogglePause();
            Assert.False(play.GameTakesKeys);
        }
        finally
        {
            play.Stop();
        }

        Assert.False(play.GameTakesKeys);
    });
}
