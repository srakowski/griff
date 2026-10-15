using System.Numerics;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Web;

namespace Griff.Tests.Web;

/// <summary>
/// What a whole game needs in the browser besides drawing: controllers through the Gamepad API, saves kept per game, and
/// network play saying it isn't there rather than failing some other way. (tools\web.ps1 -Check plays exported games in a
/// headless browser for the rest: sound, touch, sizes, fullscreen, saves surviving a reload.)
/// </summary>
public sealed class WebGameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-web-game-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void AControllerInTheStandardLayout_IsGriffs()
    {
        var input = new InputState();

        // A (0), Start (9) and d-pad left (14) held; left stick up and right, right stick down; left trigger most of the way.
        WebGamepad.Apply(input, true, (1 << 0) | (1 << 9) | (1 << 14), 0.6, -0.8, 0, 0.9, 0.75, 0.1);

        Assert.True(input.IsGamepadConnected);
        Assert.True(input.IsDown(GamepadButton.A));
        Assert.True(input.WasPressed(GamepadButton.Start));
        Assert.True(input.IsDown(GamepadButton.DPadLeft));
        Assert.False(input.IsDown(GamepadButton.B));
        Assert.True(input.IsDown(GamepadButton.LeftTrigger));
        Assert.False(input.IsDown(GamepadButton.RightTrigger));
        Assert.True(input.LeftStick.X > 0.3f && input.LeftStick.Y > 0.5f, $"Left stick {input.LeftStick}");
        Assert.True(input.RightStick.Y < -0.5f, $"Right stick {input.RightStick}");

        WebGamepad.Apply(input, false, 0, 0, 0, 0, 0, 0, 0);
        Assert.False(input.IsGamepadConnected);
        Assert.False(input.IsDown(GamepadButton.A));
        Assert.Equal(Vector2.Zero, input.LeftStick);
    }

    [Fact]
    public void EveryButton_HasItsPlaceInTheStandardLayout()
    {
        var input = new InputState();
        WebGamepad.Apply(input, true, 0xFFFF, 0, 0, 0, 0, 1, 1);

        var missing = Enum.GetValues<GamepadButton>().Where(b => !input.IsDown(b)).ToList();
        Assert.True(missing.Count == 0, "Not reached: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task Saves_AreKeptPerGame_AndSurviveExportingAgain()
    {
        var hop = await Project.CreateAsync(Path.Combine(_root, "a"), "Space Hop");
        var other = await Project.CreateAsync(Path.Combine(_root, "b"), "Space Hop");

        Assert.StartsWith("griff:Space Hop:", WebSaves.KeyOf(hop), StringComparison.Ordinal);
        Assert.Equal(WebSaves.KeyOf(hop), WebSaves.KeyOf(await Project.LoadAsync(hop.RootPath)));
        // Two games of the same name, on one site (itch.io serves every game's page from the same address), keep their own.
        Assert.NotEqual(WebSaves.KeyOf(hop), WebSaves.KeyOf(other));
    }

    [Fact]
    public void WhereNetworkPlayIsUnavailable_HostingAndJoining_SaySo()
    {
        var game = new Game(new Scene(), new InputState());
        game.Net.Unavailable = NetSession.BrowserUnavailable;
        var ended = new List<string>();
        game.Net.Ended += ended.Add;

        Assert.False(game.Net.Host());
        Assert.Equal(NetSession.BrowserUnavailable, game.Net.Status);
        Assert.False(game.Net.HostServer());
        game.Net.Join("192.168.1.5");

        Assert.Equal(NetSession.BrowserUnavailable, game.Net.Status);
        Assert.Equal([NetSession.BrowserUnavailable], ended);
        Assert.False(game.Net.IsOnline);
        Assert.False(game.Net.IsConnecting);
        Assert.Contains("browser", NetSession.BrowserUnavailable, StringComparison.Ordinal);
        // Not in a browser, it can.
        Assert.Null(new Game(new Scene(), new InputState()).Net.Unavailable);
    }

    [Fact]
    public void AHostButton_InTheBrowser_SaysNetworkPlayIsntThere_AndStaysPut()
    {
        var scene = new Scene();
        var host = new Entity("Host Game");
        var button = host.AddComponent<Button>();
        (button.Action, button.Scene, button.Shortcut) = (ButtonAction.HostGame, "Arena", Key.Enter);
        host.AddComponent<Text>();
        scene.Add(host);
        var input = new InputState();
        var game = new Game(scene, input) { SceneName = "Main", SceneLoader = _ => new Scene() };
        game.Net.Unavailable = NetSession.BrowserUnavailable;

        input.SetKey(Key.Enter, true);
        game.Advance(1 / 60.0);
        game.Advance(1 / 60.0);

        Assert.Equal(NetSession.BrowserUnavailable, game.Net.Status);
        Assert.Equal("Main", game.SceneName);
    }

    [Fact]
    public void ScenesSetUpForNetworkPlay_AreKnown()
    {
        Assert.False(NetSession.IsUsedIn(Scene(e => e.AddComponent<Button>().Action = ButtonAction.LoadScene)));
        Assert.True(NetSession.IsUsedIn(Scene(e => e.AddComponent<Multiplayer>())));
        Assert.True(NetSession.IsUsedIn(Scene(e => e.AddComponent<Networked>())));
        Assert.True(NetSession.IsUsedIn(Scene(e => e.AddComponent<Button>().Action = ButtonAction.JoinGame)));
        Assert.True(NetSession.IsUsedIn(Scene(e => e.AddComponent<Button>().Action = ButtonAction.HostGame)));
        Assert.True(NetSession.IsUsedIn(Scene(e => e.AddComponent<TextField>().Use = TextFieldUse.JoinAddress)));
        Assert.False(NetSession.IsUsedIn(Scene(e => e.AddComponent<TextField>().Use = TextFieldUse.PlayerName)));

        static Scene Scene(Action<Entity> setUp)
        {
            var scene = new Scene();
            var parent = new Entity("Menu");
            var child = new Entity("Thing");
            setUp(child);
            scene.Add(parent);
            scene.Add(child, parent);
            return scene;
        }
    }
}
