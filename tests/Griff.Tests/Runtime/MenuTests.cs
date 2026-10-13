using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Menus with the keyboard and gamepad, sliders, toggles, and settings that are remembered.</summary>
public class MenuTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    public MenuTests()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
    }

    private Game CreateGame(SaveData? saveData = null)
    {
        var game = new Game(_scene, _input) { ViewAspect = 2 };
        if (saveData is not null)
            game.SaveData = saveData;
        return game;
    }

    private void Tick(Game game, int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Press(Game game, Key key)
    {
        _input.SetKey(key, true);
        Tick(game);
        _input.SetKey(key, false);
        Tick(game);
    }

    private void PressPad(Game game, GamepadButton button)
    {
        _input.SetGamepad([button], Vector2.Zero);
        Tick(game);
        _input.SetGamepad([], Vector2.Zero);
        Tick(game);
    }

    private (Entity Entity, Button Button) AddButton(string name, Vector2 position, Key shortcut = Key.None)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<Sprite>().Size = new Vector2(2, 1);
        var button = entity.AddComponent<Button>();
        button.Size = new Vector2(2, 1);
        button.Shortcut = shortcut;
        _scene.Add(entity);
        return (entity, button);
    }

    private Slider AddSlider(Vector2 position, GameSetting setting = GameSetting.None)
    {
        var entity = new Entity("Slider");
        entity.Transform.Position = new Vector3(position, 0);
        var slider = entity.AddComponent<Slider>();
        slider.Size = new Vector2(4, 0.5f);
        slider.Value = 0.5f;
        slider.Setting = setting;
        _scene.Add(entity);
        return slider;
    }


    [Fact]
    public void ArrowKeys_PickTheTopButtonFirst_ThenMoveBetweenButtons()
    {
        var (_, play) = AddButton("Play", new Vector2(0, 1));
        var (_, options) = AddButton("Options", new Vector2(0, -0.5f));
        var (_, quit) = AddButton("Quit", new Vector2(0, -2));
        var game = CreateGame();
        Tick(game);
        Assert.Null(game.Focused);

        Press(game, Key.Down);
        Assert.Same(play, game.Focused);
        Assert.True(play.IsFocused);

        Press(game, Key.Down);
        Press(game, Key.S);
        Assert.Same(quit, game.Focused);
        Assert.False(play.IsFocused);

        // Nothing further down: it stays put.
        Press(game, Key.Down);
        Assert.Same(quit, game.Focused);

        Press(game, Key.Up);
        Assert.Same(options, game.Focused);
    }

    [Fact]
    public void LeftAndRight_MoveBetweenButtonsSideBySide()
    {
        var (_, retry) = AddButton("Retry", new Vector2(-2, 0));
        var (_, menu) = AddButton("Menu", new Vector2(2, 0));
        var game = CreateGame();

        Press(game, Key.Right);
        Assert.Same(retry, game.Focused); // the first press picks the top-left one
        Press(game, Key.Right);
        Assert.Same(menu, game.Focused);
        Press(game, Key.Left);
        Assert.Same(retry, game.Focused);
    }

    [Fact]
    public void Enter_ClicksThePickedButton_InsteadOfAnotherButtonsEnterShortcut()
    {
        var (_, play) = AddButton("Play", new Vector2(0, 1), shortcut: Key.Enter);
        var (_, quit) = AddButton("Quit", new Vector2(0, -1));
        var playClicks = 0;
        var quitClicks = 0;
        play.Clicked += _ => playClicks++;
        quit.Clicked += _ => quitClicks++;
        var game = CreateGame();

        // Nothing picked: Enter is Play's shortcut.
        Press(game, Key.Enter);
        Assert.Equal((1, 0), (playClicks, quitClicks));

        Press(game, Key.Down);
        Press(game, Key.Down);
        Assert.Same(quit, game.Focused);
        Press(game, Key.Enter);
        Assert.Equal((1, 1), (playClicks, quitClicks));

        Press(game, Key.Space);
        Assert.Equal((1, 2), (playClicks, quitClicks));
    }

    [Fact]
    public void Gamepad_PicksTheFirstButtonStraightAway_DPadMoves_AClicks_BGoesBack()
    {
        var (_, play) = AddButton("Play", new Vector2(0, 1));
        var (_, back) = AddButton("Back", new Vector2(0, -1), shortcut: Key.Escape);
        var playClicks = 0;
        var backClicks = 0;
        play.Clicked += _ => playClicks++;
        back.Clicked += _ => backClicks++;
        var game = CreateGame();

        PressPad(game, GamepadButton.Start); // any use of the controller
        Assert.Same(play, game.Focused);

        PressPad(game, GamepadButton.A);
        Assert.Equal(1, playClicks);

        PressPad(game, GamepadButton.DPadDown);
        Assert.Same(back, game.Focused);

        PressPad(game, GamepadButton.DPadUp);
        PressPad(game, GamepadButton.B);
        Assert.Equal((1, 1), (playClicks, backClicks));
    }

    [Fact]
    public void Stick_MovesOncePerPush_AndRepeatsWhileHeld()
    {
        AddButton("A", new Vector2(0, 2));
        AddButton("B", new Vector2(0, 1));
        AddButton("C", new Vector2(0, 0));
        var (_, d) = AddButton("D", new Vector2(0, -1));
        var game = CreateGame();
        PressPad(game, GamepadButton.Start);
        Assert.Equal("A", game.Focused!.Entity!.Name);

        _input.SetGamepad([], new Vector2(0, -1));
        Tick(game);
        Assert.Equal("B", game.Focused!.Entity!.Name);
        Tick(game, 10);
        Assert.Equal("B", game.Focused!.Entity!.Name);

        // Held: after a pause it moves on by itself, and keeps going.
        Tick(game, 30);
        Assert.Same(d, game.Focused);
    }

    [Fact]
    public void ADirectionAlreadyHeldWhenTheMenuAppears_DoesNothingUntilPressedAgain()
    {
        var (panel, _) = AddButton("Retry", new Vector2(0, 0));
        panel.IsEnabled = false;
        var game = CreateGame();

        _input.SetKey(Key.Down, true); // still steering when the game ends
        Tick(game, 5);
        panel.IsEnabled = true;
        Tick(game, 60);
        Assert.Null(game.Focused);

        _input.SetKey(Key.Down, false);
        Tick(game);
        Press(game, Key.Down);
        Assert.NotNull(game.Focused);
    }

    [Fact]
    public void MovingTheMouse_HandsTheMenuBackToIt()
    {
        AddButton("Play", new Vector2(0, 1));
        var game = CreateGame();
        _input.MouseWorld = new Vector2(8, 4);
        Tick(game);
        Press(game, Key.Down);
        Assert.NotNull(game.Focused);

        _input.MouseWorld = new Vector2(7, 4);
        Tick(game);
        Assert.Null(game.Focused);
    }

    [Fact]
    public void ButtonsThatArentNavigable_AreSkipped()
    {
        var (_, hud) = AddButton("Pause", new Vector2(0, 4));
        hud.Navigable = false;
        var (_, play) = AddButton("Play", new Vector2(0, 0));
        var game = CreateGame();

        Press(game, Key.Down);
        Assert.Same(play, game.Focused);
        Press(game, Key.Up);
        Assert.Same(play, game.Focused);
    }

    [Fact]
    public void Focus_PicksAControlFromAScript()
    {
        AddButton("Retry", new Vector2(-2, 0));
        var (_, menu) = AddButton("Menu", new Vector2(2, 0));
        var game = CreateGame();
        Tick(game);

        game.Focus(menu);
        Tick(game);
        Assert.True(menu.IsFocused);
        Assert.Throws<ArgumentException>(() => game.Focus(new Sprite()));
    }

    [Fact]
    public void FocusedButtons_AreLitUp()
    {
        var (_, play) = AddButton("Play", Vector2.Zero);
        var game = CreateGame();
        _input.MouseWorld = new Vector2(9, 4); // not over it
        Tick(game);
        var color = new Color(0.2f, 0.4f, 0.6f);
        Assert.Equal(color, play.Shade(color));

        game.Focus(play);
        Tick(game);
        Assert.NotEqual(color, play.Shade(color));
    }

    [Fact]
    public void Slider_DraggingSetsTheValue_SnappedToItsStep()
    {
        var slider = AddSlider(Vector2.Zero);
        var changes = 0;
        slider.Changed += _ => changes++;
        var game = CreateGame();
        Tick(game);

        // The slider spans x = -2..2: three quarters of the way along is 1.
        _input.MouseWorld = new Vector2(0, 0);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        Assert.True(slider.IsDragging);
        _input.MouseWorld = new Vector2(1.04f, 0.8f); // wandering off it vertically still drags
        Tick(game);
        Assert.Equal(0.8f, slider.Value, 4);

        _input.MouseWorld = new Vector2(9, 0);
        Tick(game);
        Assert.Equal(1f, slider.Value, 4);

        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Assert.False(slider.IsDragging);
        _input.MouseWorld = new Vector2(-9, 0);
        Tick(game);
        Assert.Equal(1f, slider.Value, 4);
        Assert.True(changes >= 2);
    }

    [Fact]
    public void Slider_LeftAndRightStepItOncePicked_AndTheWheelTurnsIt()
    {
        var slider = AddSlider(Vector2.Zero);
        slider.Step = 0.25f;
        var game = CreateGame();
        Press(game, Key.Down);
        Assert.Same(slider, game.Focused);

        Press(game, Key.Right);
        Assert.Equal(0.75f, slider.Value, 4);
        Press(game, Key.Right);
        Press(game, Key.Right);
        Assert.Equal(1f, slider.Value, 4);
        Press(game, Key.Left);
        Assert.Equal(0.75f, slider.Value, 4);

        _input.MouseWorld = Vector2.Zero;
        _input.AddScroll(-1);
        Tick(game);
        Assert.Equal(0.5f, slider.Value, 4);
    }

    [Fact]
    public void Slider_Snap_KeepsWithinItsRange()
    {
        var slider = new Slider { Min = 10, Max = 20, Step = 5 };
        Assert.Equal(15, slider.Snap(13.6f));
        Assert.Equal(20, slider.Snap(99));
        Assert.Equal(10, slider.Snap(float.NaN));

        slider.Step = 0;
        Assert.Equal(13.6f, slider.Snap(13.6f), 4);
        slider.Value = 12.5f;
        Assert.Equal(0.25f, slider.Fraction, 4);
    }

    [Fact]
    public void Slider_BoundToTheMusicVolume_ShowsAndChangesIt_AndItsRememberedNextTime()
    {
        var save = new SaveData();
        var game = CreateGame(save);
        game.Audio.MusicVolume = 0.3f;
        var slider = AddSlider(Vector2.Zero, GameSetting.MusicVolume);
        Tick(game);
        Assert.Equal(0.3f, slider.Value, 4);

        Press(game, Key.Down);
        Press(game, Key.Right);
        Assert.Equal(0.4f, game.Audio.MusicVolume, 4);
        Assert.Equal(0.4f, save.GetFloat(Game.MusicVolumeSaveKey), 4);

        // Next run: a fresh game with the same save data plays at the chosen volume.
        var next = new Game(new Scene(), new InputState()) { Audio = new SilentAudio(), SaveData = save };
        Assert.Equal(0.4f, next.Audio.MusicVolume, 4);
    }

    [Fact]
    public void Slider_BoundToASetting_SpreadsItOverItsOwnRange()
    {
        var game = CreateGame();
        game.Audio.SoundVolume = 0.5f;
        var slider = AddSlider(Vector2.Zero, GameSetting.SoundVolume);
        slider.Min = 0;
        slider.Max = 10;
        slider.Step = 1;
        Tick(game);
        Assert.Equal(5, slider.Value, 4);

        Press(game, Key.Down);
        Press(game, Key.Right);
        Assert.Equal(6, slider.Value, 4);
        Assert.Equal(0.6f, game.Audio.SoundVolume, 4);
    }

    [Fact]
    public void ShakingTheCamera_DoesntCountAsTheMouseMoving()
    {
        AddButton("Play", new Vector2(0, 1));
        var game = CreateGame();
        Tick(game);
        Press(game, Key.Down);
        Assert.NotNull(game.Focused);

        // The platform works out the mouse's world position through the shaken view.
        game.ShakeCamera(1, 1);
        for (var i = 0; i < 10; i++)
        {
            var shake = _scene.Entities.First(e => e.Name == "Camera").GetComponent<Camera>()!.ShakeOffset;
            _input.MouseWorld = shake;
            Tick(game);
        }

        Assert.NotNull(game.Focused);
    }

    [Fact]
    public void Slider_SaveAs_KeepsItsValue_ForNextTime()
    {
        var save = new SaveData();
        var slider = AddSlider(Vector2.Zero);
        slider.SaveAs = "difficulty";
        var game = CreateGame(save);
        Press(game, Key.Down);
        Press(game, Key.Left);
        Assert.Equal(0.4f, save.GetFloat("difficulty"), 4);

        // The same slider in a fresh scene starts from the saved value.
        _scene.Remove(slider.Entity!);
        var again = AddSlider(Vector2.Zero);
        again.SaveAs = "difficulty";
        Tick(CreateGame(save));
        Assert.Equal(0.4f, again.Value, 4);
    }

    [Fact]
    public void Toggle_FlipsWhenClicked_AndSaysSoOnItsLabel()
    {
        var (entity, _) = AddButton("Shake", Vector2.Zero);
        var text = entity.AddComponent<Text>();
        var toggle = entity.AddComponent<Toggle>();
        toggle.OnText = "Screen shake: On";
        toggle.OffText = "Screen shake: Off";
        toggle.SaveAs = "shake";
        var save = new SaveData();
        var game = CreateGame(save);
        Tick(game);
        Assert.Equal("Screen shake: On", text.Value);

        Press(game, Key.Down);
        Press(game, Key.Enter);
        Assert.False(toggle.IsOn);
        Assert.Equal("Screen shake: Off", text.Value);
        Assert.False(save.GetBool("shake", true));

        Press(game, Key.Enter);
        Assert.True(toggle.IsOn);
        Assert.True(save.GetBool("shake"));
    }

    [Fact]
    public void Toggle_BoundToSound_MutesAndUnmutes()
    {
        var (entity, _) = AddButton("Sound", Vector2.Zero);
        entity.AddComponent<Toggle>().Setting = GameSetting.Sound;
        var save = new SaveData();
        var game = CreateGame(save);
        Tick(game);
        Assert.True(entity.GetComponent<Toggle>()!.IsOn);

        Press(game, Key.Down);
        Press(game, Key.Enter);
        Assert.True(game.Audio.Muted);
        Assert.True(save.GetBool(Game.MutedSaveKey));

        // Muted from a script: the toggle follows.
        game.Audio.Muted = false;
        Tick(game);
        Assert.True(entity.GetComponent<Toggle>()!.IsOn);
        Assert.False(save.GetBool(Game.MutedSaveKey, true));
    }

    [Fact]
    public void Fullscreen_TellsTheHost_AndIsRemembered()
    {
        var save = new SaveData();
        var game = CreateGame(save);
        var changes = 0;
        game.FullscreenChanged += (_, _) => changes++;

        game.Fullscreen = true;
        game.Fullscreen = true;
        Assert.Equal(1, changes);
        Assert.True(save.GetBool(Game.FullscreenSaveKey));

        var next = new Game(new Scene(), new InputState());
        var told = false;
        next.FullscreenChanged += (_, _) => told = true;
        next.SaveData = save;
        Assert.True(next.Fullscreen);
        Assert.True(told);
    }

    [Fact]
    public void Settings_ChangedFromScripts_AreSaved_AndOnlyWhatChanged()
    {
        var save = new SaveData();
        var game = CreateGame(save);
        Tick(game);
        Assert.Empty(save.Keys);

        game.Audio.Volume = 0.5f;
        Tick(game);
        Assert.Equal(["griff.volume"], save.Keys);
        Assert.Equal(0.5f, save.GetFloat(Game.VolumeSaveKey), 4);
    }

    [Fact]
    public void SavedSettings_ApplyToAudioSetLater()
    {
        var save = new SaveData();
        save.Set(Game.SoundVolumeSaveKey, 0.25f);
        save.Set(Game.MutedSaveKey, true);

        // As the hosts do it: save data first or audio first, the settings end up on the audio that plays.
        var game = CreateGame(save);
        var audio = new SilentAudio();
        game.Audio = audio;
        Assert.Equal(0.25f, audio.SoundVolume, 4);
        Assert.True(audio.Muted);

        var other = new Game(new Scene(), new InputState()) { Audio = new SilentAudio() };
        other.SaveData = save;
        Assert.Equal(0.25f, other.Audio.SoundVolume, 4);
    }
}
