using System.Numerics;
using Griff.Hosting;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// The display settings an options menu changes with no code: the window's size, VSync, the frame cap and the HUD's size,
/// remembered between runs; and how a frame cap paces a host's frames.
/// </summary>
public class DisplaySettingsTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    public DisplaySettingsTests()
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

    private (Entity Entity, Button Button) AddButton(string name, Vector2 position)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var button = entity.AddComponent<Button>();
        button.Size = new Vector2(2, 1);
        _scene.Add(entity);
        return (entity, button);
    }

    private (Choice Choice, Text Text) AddChoice(GameSetting setting)
    {
        var (entity, _) = AddButton("Choice", Vector2.Zero);
        var text = entity.AddComponent<Text>();
        var choice = entity.AddComponent<Choice>();
        choice.Setting = setting;
        return (choice, text);
    }

    [Fact]
    public void Defaults_KeepTodaysBehavior_AndWriteNothing()
    {
        var save = new SaveData();
        var game = CreateGame(save);
        Tick(game);

        Assert.Null(game.WindowSize);
        Assert.True(game.VSync);
        Assert.Equal(0, game.FrameCap);
        Assert.Equal(1, game.UiScale);
        Assert.Empty(save.Keys);
    }

    [Fact]
    public void DisplaySettings_AreSaved_AndComeBackNextTime()
    {
        var save = new SaveData();
        var game = CreateGame(save);
        var resized = 0;
        game.WindowSizeChanged += (_, _) => resized++;

        game.WindowSize = new WindowSize(1600, 900);
        game.WindowSize = new WindowSize(1600, 900);
        game.VSync = false;
        game.FrameCap = 60;
        game.UiScale = 1.25f;
        Tick(game);

        Assert.Equal(1, resized);
        Assert.Equal("1600x900", save.GetString(Game.WindowSizeSaveKey));
        Assert.False(save.GetBool(Game.VSyncSaveKey, true));
        Assert.Equal(60, save.GetInt(Game.FrameCapSaveKey));
        Assert.Equal(1.25f, save.GetFloat(Game.UiScaleSaveKey), 4);

        var next = new Game(new Scene(), new InputState());
        var told = false;
        next.WindowSizeChanged += (_, _) => told = true;
        next.SaveData = save;
        Assert.Equal(new WindowSize(1600, 900), next.WindowSize);
        Assert.True(told);
        Assert.False(next.VSync);
        Assert.Equal(60, next.FrameCap);
        Assert.Equal(1.25f, next.UiScale, 4);
    }

    [Fact]
    public void FrameCap_AndUiScale_StayInRange()
    {
        var game = CreateGame();
        game.FrameCap = -5;
        Assert.Equal(0, game.FrameCap);
        game.UiScale = 100;
        Assert.Equal(4, game.UiScale);
        game.UiScale = float.NaN;
        Assert.Equal(1, game.UiScale);
        Assert.Throws<ArgumentOutOfRangeException>(() => game.WindowSize = new WindowSize(0, 720));
    }

    [Theory]
    [InlineData("1600x900", 1600, 900)]
    [InlineData(" 1920 X 1080 ", 1920, 1080)]
    public void WindowSize_ReadsAsItsWritten(string text, int width, int height)
    {
        Assert.True(WindowSize.TryParse(text, out var size));
        Assert.Equal(new WindowSize(width, height), size);
        Assert.Equal($"{width}x{height}", size.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("big")]
    [InlineData("1600x")]
    [InlineData("0x900")]
    [InlineData("-1600x900")]
    public void WindowSize_ThatIsntOne_IsntRead(string? text) => Assert.False(WindowSize.TryParse(text, out _));

    [Fact]
    public void WindowSizes_AreThoseThatFitTheScreen()
    {
        // A 1080p screen's work area, less a window's frame: 1920x1080 itself doesn't fit in a window.
        Assert.Equal(["960x540", "1024x576", "1152x648", "1280x720", "1366x768", "1600x900"],
            WindowSize.Fitting(new WindowSize(1904, 1001)).Select(s => s.ToString()));
        // A 1440p screen's.
        Assert.Equal("1920x1080", WindowSize.Fitting(new WindowSize(2544, 1361))[^1].ToString());
        // A 4K screen's has room for them all.
        Assert.Equal(WindowSize.Common, WindowSize.Fitting(new WindowSize(3840, 2160)));
        // A tiny screen still offers the smallest.
        Assert.Equal([new WindowSize(960, 540)], WindowSize.Fitting(new WindowSize(800, 600)));
        // No screen to ask (a browser): up to 1920x1080.
        Assert.Equal(new WindowSize(1920, 1080), WindowSize.Fitting(null)[^1]);
    }

    [Fact]
    public void WindowSizes_IncludeThePickedOne_IfItDoesntFit()
    {
        var game = CreateGame();
        game.DisplayRoom = new WindowSize(1904, 1001);
        Assert.Equal(new WindowSize(1600, 900), game.WindowSizes[^1]);

        // Picked on a bigger screen, or by a script.
        game.WindowSize = new WindowSize(1920, 1080);
        Assert.Equal(new WindowSize(1920, 1080), game.WindowSizes[^1]);
        game.WindowSize = new WindowSize(800, 600);
        Assert.Equal(new WindowSize(800, 600), game.WindowSizes[0]);
    }

    [Fact]
    public void Choice_SetToWindowSize_OffersTheSizesThatFit_AndPicksOne()
    {
        var (choice, text) = AddChoice(GameSetting.WindowSize);
        choice.Format = "Window: {0}";
        var save = new SaveData();
        var game = CreateGame(save);
        game.DisplayRoom = new WindowSize(1904, 1001);
        Tick(game);

        Assert.Equal(["960x540", "1024x576", "1152x648", "1280x720", "1366x768", "1600x900"], choice.Options);
        // Until the player picks one, the size a built game opens at.
        Assert.Equal("Window: 1280x720", text.Value);
        Assert.Null(game.WindowSize);

        Press(game, Key.Down);
        Press(game, Key.Right);
        Assert.Equal(new WindowSize(1366, 768), game.WindowSize);
        Assert.Equal("1366x768", save.GetString(Game.WindowSizeSaveKey));
        Assert.Equal("Window: 1366x768", text.Value);

        // Set from a script: the choice follows.
        game.WindowSize = new WindowSize(1600, 900);
        Tick(game);
        Assert.Equal("Window: 1600x900", text.Value);
    }

    [Fact]
    public void Choice_SetToFrameCap_OffersTheCaps_AndPicksOne()
    {
        var (choice, text) = AddChoice(GameSetting.FrameCap);
        var save = new SaveData();
        var game = CreateGame(save);
        Tick(game);

        Assert.Equal(["Off", "30", "60", "120", "144", "165", "240"], choice.Options);
        Assert.Equal("Off", text.Value);

        Press(game, Key.Down);
        Press(game, Key.Right);
        Assert.Equal(30, game.FrameCap);
        Assert.Equal(30, save.GetInt(Game.FrameCapSaveKey));

        Press(game, Key.Left);
        Assert.Equal(0, game.FrameCap);
        Assert.Equal("Off", text.Value);

        // A cap of a script's own is offered too, where it goes.
        game.FrameCap = 75;
        Tick(game);
        Assert.Equal(["Off", "30", "60", "75", "120", "144", "165", "240"], choice.Options);
        Assert.Equal("75", text.Value);
    }

    [Fact]
    public void Toggle_SetToVSync_TurnsItOffAndOn()
    {
        var (entity, _) = AddButton("VSync", Vector2.Zero);
        var toggle = entity.AddComponent<Toggle>();
        toggle.Setting = GameSetting.VSync;
        var save = new SaveData();
        var game = CreateGame(save);
        Tick(game);
        Assert.True(toggle.IsOn);

        Press(game, Key.Down);
        Press(game, Key.Enter);
        Assert.False(game.VSync);
        Assert.False(save.GetBool(Game.VSyncSaveKey, true));
    }

    [Fact]
    public void Slider_SetToUiScale_IsTheScale()
    {
        var entity = new Entity("Slider");
        var slider = entity.AddComponent<Slider>();
        (slider.Min, slider.Max, slider.Step, slider.Setting) = (0.75f, 1.5f, 0.05f, GameSetting.UiScale);
        _scene.Add(entity);
        var game = CreateGame();
        Tick(game);
        Assert.Equal(1, slider.Value, 4);

        game.UiScale = 1.25f;
        Tick(game);
        Assert.Equal(1.25f, slider.Value, 4);
    }

    [Fact]
    public void UiScale_DrawsTheHudBigger_AndItClicksWhereItsDrawn()
    {
        var (entity, button) = AddButton("Menu", Vector2.Zero);
        var anchor = entity.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.TopLeft, new Vector2(1.5f, -1));
        var clicks = 0;
        button.Clicked += _ => clicks++;
        var game = CreateGame();
        game.UiScale = 1.5f;
        Tick(game);

        // The view is 20 by 10 around the middle: its top-left corner is (-10, 5), and the offset is drawn 1.5 times too.
        Assert.Equal(1.5f, entity.Transform.WorldMatrix.M11, 4);
        Assert.Equal(new Vector2(-10 + 2.25f, 5 - 1.5f), new Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y));

        // Clicked near its drawn right edge, past where it would end at its own size.
        _input.MouseWorld = new Vector2(-10 + 2.25f + 1.4f, 5 - 1.5f);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void FrameSchedule_WithNoCap_TakesEveryChance()
    {
        var schedule = new FrameSchedule();
        Assert.Equal(60, Taken(schedule, 1 / 60.0, 1));
        Assert.Equal(0, schedule.Wait(0.5));
    }

    [Theory]
    // In step with a 60 Hz screen, a cap of 30 draws every other frame, and one of 60 or more draws them all.
    [InlineData(30, 60, 30)]
    [InlineData(60, 60, 60)]
    [InlineData(240, 60, 60)]
    // A cap that doesn't divide the screen's rate still averages it.
    [InlineData(60, 144, 60)]
    [InlineData(120, 144, 120)]
    [InlineData(30, 165, 30)]
    // VSync off, a chance as soon as the last frame is done (every millisecond): the cap is what keeps them.
    [InlineData(144, 1000, 144)]
    [InlineData(60, 1000, 60)]
    public void FrameSchedule_KeepsToTheCap(int cap, int chancesPerSecond, int expected)
    {
        var schedule = new FrameSchedule { Cap = cap };
        Assert.InRange(Taken(schedule, 1.0 / chancesPerSecond, 2) / 2.0, expected - 1, expected + 1);
    }

    [Fact]
    public void FrameSchedule_At30OnA60HzScreen_TakesEveryOtherFrame()
    {
        var schedule = new FrameSchedule { Cap = 30 };
        var taken = Enumerable.Range(0, 8).Select(i => schedule.Take(10 + i / 60.0)).ToList();
        Assert.Equal([true, false, true, false, true, false, true, false], taken);
    }

    [Fact]
    public void FrameSchedule_SaysHowLongToWait_AndStartsAgainOnceBehind()
    {
        var schedule = new FrameSchedule { Cap = 50 };
        Assert.True(schedule.Take(1));
        Assert.Equal(0.02, schedule.Wait(1), 6);
        Assert.Equal(0.015, schedule.Wait(1.005), 6);
        Assert.False(schedule.Take(1.01));
        Assert.True(schedule.Take(1.02));

        // A frame that took half a second: the next is due a cap's interval later, not a burst to catch up.
        Assert.True(schedule.Take(1.5));
        Assert.False(schedule.Take(1.505));
        Assert.Equal(0.02, schedule.Wait(1.5), 6);
    }

    // How many of the chances, `every` seconds apart over `seconds`, it takes.
    private static int Taken(FrameSchedule schedule, double every, double seconds)
    {
        var taken = 0;
        var chances = (int)Math.Round(seconds / every);
        for (var i = 0; i < chances; i++)
        {
            // A little jitter, as a screen's frames come.
            var jitter = (i % 3 - 1) * every * 0.02;
            if (schedule.Take(100 + i * every + jitter))
                taken++;
        }

        return taken;
    }
}
