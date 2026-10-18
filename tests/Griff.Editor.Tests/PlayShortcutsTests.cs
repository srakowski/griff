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
        Assert.True(PlayShortcuts.GameKeeps(typeof(StepCommand), new KeyGesture(Key.F10), new InputState()));

    [Fact]
    public void AKeyTheGameBinds_IsTheGames_ForAnyOtherCommand()
    {
        var input = InputWith("Menu", Griff.Input.Key.F9);
        Assert.True(PlayShortcuts.GameKeeps(typeof(Griff.Editor.Features.Audio.MuteCommand), new KeyGesture(Key.F9), input));
        Assert.False(PlayShortcuts.GameKeeps(typeof(Griff.Editor.Features.Audio.MuteCommand), new KeyGesture(Key.F8), input));
    }

    [Theory]
    [InlineData(typeof(PauseCommand), Key.F6, ModifierKeys.None, Griff.Input.Key.F6)]
    [InlineData(typeof(StopCommand), Key.F5, ModifierKeys.Shift, Griff.Input.Key.F5)]
    [InlineData(typeof(PlayFullscreenCommand), Key.Enter, ModifierKeys.Alt, Griff.Input.Key.Enter)]
    public void PauseStopAndFullscreen_AlwaysWin_EvenWhenTheGameBindsTheKey(Type command, Key key, ModifierKeys modifiers, Griff.Input.Key bound) =>
        Assert.False(PlayShortcuts.GameKeeps(command, new KeyGesture(key, modifiers), InputWith("Menu", bound)));

    [Fact]
    public void AShortcutWithModifiers_IsNotTakenByAPlainKeyBinding() =>
        Assert.False(PlayShortcuts.GameKeeps(typeof(KeepPlayChangesCommand), new KeyGesture(Key.K, ModifierKeys.Control), InputWith("Menu", Griff.Input.Key.K)));
}
