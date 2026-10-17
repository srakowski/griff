using System.Windows.Input;
using Griff.Editor.Features.Play;
using Griff.Runtime;

namespace Griff.Editor.Tests;

/// <summary>
/// Over a running game the Game menu's shortcuts give way to the game's own keys: Step Frame (F10) is for a paused game,
/// and a plain key an input action is bound to is the game's, but Pause, Stop and Play Fullscreen always win.
/// </summary>
public sealed class PlayShortcutsTests
{
    private static InputState InputWith(string action, Griff.Input.Key key)
    {
        var input = new InputState();
        input.Bind(action, key);
        return input;
    }

    [Fact]
    public void StepFrame_IsTheGames_WhilePlaying_EvenWithNothingBound() =>
        Assert.True(PlayShortcuts.GameKeeps("Game.Step", new KeyGesture(Key.F10), new InputState()));

    [Fact]
    public void AKeyTheGameBinds_IsTheGames_ForAnyOtherCommand()
    {
        var input = InputWith("Menu", Griff.Input.Key.F9);
        Assert.True(PlayShortcuts.GameKeeps("Game.Other", new KeyGesture(Key.F9), input));
        Assert.False(PlayShortcuts.GameKeeps("Game.Other", new KeyGesture(Key.F8), input));
    }

    [Theory]
    [InlineData("Game.Pause", Key.F6, ModifierKeys.None, Griff.Input.Key.F6)]
    [InlineData("Game.Stop", Key.F5, ModifierKeys.Shift, Griff.Input.Key.F5)]
    [InlineData("Game.Fullscreen", Key.Enter, ModifierKeys.Alt, Griff.Input.Key.Enter)]
    public void PauseStopAndFullscreen_AlwaysWin_EvenWhenTheGameBindsTheKey(string id, Key key, ModifierKeys modifiers, Griff.Input.Key bound) =>
        Assert.False(PlayShortcuts.GameKeeps(id, new KeyGesture(key, modifiers), InputWith("Menu", bound)));

    [Fact]
    public void AShortcutWithModifiers_IsNotTakenByAPlainKeyBinding() =>
        Assert.False(PlayShortcuts.GameKeeps("Game.KeepChanges", new KeyGesture(Key.K, ModifierKeys.Control), InputWith("Menu", Griff.Input.Key.K)));
}
