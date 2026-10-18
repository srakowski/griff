using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Panels and the game's own keys: scripts that claim Back before a panel opens on it, presses a panel uses up so scripts
/// don't see them, and buttons that work over panels.
/// </summary>
public class PanelInputTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    public PanelInputTests()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
        _input.UseControls(new Dictionary<string, IReadOnlyList<InputBinding>>
        {
            ["DropTool"] = [Key.Escape, MouseButton.Right],
            ["Skip"] = [Key.Escape],
            ["GameMenu"] = [Key.F10],
            ["Map"] = [Key.M],
        });
    }

    private Game CreateGame() => new(_scene, _input) { ViewAspect = 2 };

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

    private void Click(Game game, Vector2 at)
    {
        _input.MouseWorld = at;
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
    }

    private Panel AddPanel(string name, string? openOn = null, bool pauses = true)
    {
        var entity = new Entity(name);
        var panel = entity.AddComponent<Panel>();
        panel.OpenOn = openOn;
        panel.PauseGame = pauses;
        _scene.Add(entity);
        return panel;
    }

    private Button AddButton(string name, Vector2 position, Entity? parent = null, ButtonAction action = ButtonAction.None)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var button = entity.AddComponent<Button>();
        button.Size = new Vector2(2, 1);
        button.Action = action;
        _scene.Add(entity, parent);
        return button;
    }

    private T AddScript<T>(string name) where T : Behavior, new()
    {
        var entity = new Entity(name);
        var script = entity.AddComponent<T>();
        _scene.Add(entity);
        return script;
    }

    // A builder's tool: Esc (DropTool) puts it down, and while it's in hand the script has Back.
    private sealed class BuildTool : Behavior
    {
        public bool Held { get; set; } = true;

        public int Drops { get; set; }

        protected override bool ClaimsBack => Held;

        protected override void Update()
        {
            if (Input.WasPressed("DropTool"))
            {
                Held = false;
                Drops++;
            }
        }
    }

    // Counts the presses it sees of the actions and keys a panel might use.
    private sealed class Watcher : Behavior
    {
        public int Skips { get; set; }

        public int Maps { get; set; }

        public int BufferedMaps { get; set; }

        public int Enters { get; set; }

        public int Jumps { get; set; }

        protected override void Update()
        {
            Skips += Input.WasPressed("Skip") ? 1 : 0;
            Maps += Input.WasPressed("Map") ? 1 : 0;
            BufferedMaps += Input.WasPressed("Map", buffer: 0.5f) ? 1 : 0;
            Enters += Input.WasPressed(Key.Enter) ? 1 : 0;
            Jumps += Input.WasPressed("Jump") ? 1 : 0;
        }
    }

    [Fact]
    public void AScriptThatClaimsBack_KeepsThePausePanelShut_EscPutsTheToolDown_AndTheNextEscOpensIt()
    {
        var menu = AddPanel("Game Menu", openOn: "Pause");
        var tool = AddScript<BuildTool>("Build Tool");
        var game = CreateGame();
        Tick(game);

        Press(game, Key.Escape);
        Assert.False(menu.Entity!.IsEnabled);
        Assert.False(tool.Held);
        Assert.Equal(1, tool.Drops);

        Press(game, Key.Escape);
        Assert.Equal(menu, game.TopPanel);
        // The press that opened the menu didn't reach the script either.
        Assert.Equal(1, tool.Drops);
    }

    [Fact]
    public void AClaimOnlyCounts_FromAScriptThatsRunning_AndOnlyForBack()
    {
        var menu = AddPanel("Game Menu", openOn: "Pause");
        var tool = AddScript<BuildTool>("Build Tool");
        var game = CreateGame();
        Tick(game);

        // P opens the menu whatever the script holds: it claims Esc and B, not the menu's other keys.
        Press(game, Key.P);
        Assert.Equal(menu, game.TopPanel);
        Press(game, Key.P);
        Assert.Null(game.TopPanel);

        // A script whose entity is off claims nothing.
        tool.Entity!.IsEnabled = false;
        Press(game, Key.Escape);
        Assert.Equal(menu, game.TopPanel);

        // Nor one held still behind a panel that pauses the game: Esc closes it.
        tool.Entity.IsEnabled = true;
        Tick(game);
        Press(game, Key.Escape);
        Assert.Null(game.TopPanel);
        Assert.True(tool.Held);
    }

    [Fact]
    public void BehindAPanelThatLetsTheGameGoOn_AClaimingScriptGetsEscBeforeThePanelCloses()
    {
        var shop = AddPanel("Shop", pauses: false);
        var tool = AddScript<BuildTool>("Build Tool");
        var game = CreateGame();
        game.OpenPanel(shop);
        Tick(game);

        Press(game, Key.Escape);
        Assert.Equal(shop, game.TopPanel);
        Assert.False(tool.Held);

        Press(game, Key.Escape);
        Assert.Null(game.TopPanel);
        Assert.Equal(1, tool.Drops);
    }

    [Fact]
    public void EscThatClosesAPausingPanel_IsntSeenByScriptsThatFrame()
    {
        var letter = AddPanel("Letter");
        var watcher = AddScript<Watcher>("Tour");
        var game = CreateGame();
        game.OpenPanel(letter);
        Tick(game);

        Press(game, Key.Escape);
        Assert.Null(game.TopPanel);
        Assert.Equal(0, watcher.Skips);

        // The next Esc, with nothing open to close, is the game's.
        Press(game, Key.Escape);
        Assert.Equal(1, watcher.Skips);
    }

    [Fact]
    public void ThePressThatOpensOrClosesAPanelOnItsOpenOn_IsUsedUp_EvenBuffered()
    {
        var map = AddPanel("Map", openOn: "Map", pauses: false);
        var watcher = AddScript<Watcher>("Player");
        var game = CreateGame();
        Tick(game);

        Press(game, Key.M);
        Assert.Equal(map, game.TopPanel);
        Press(game, Key.M);
        Assert.Null(game.TopPanel);
        Tick(game, 3);

        Assert.Equal(0, watcher.Maps);
        Assert.Equal(0, watcher.BufferedMaps);
    }

    [Fact]
    public void EnterOnAPickedButtonThatClosesThePanel_IsUsedUp_ButAShortcutThatOpensNothingStillReachesTheGame()
    {
        var pause = AddPanel("Paused");
        AddButton("Resume", Vector2.Zero, pause.Entity, ButtonAction.ClosePanel);
        var jump = AddButton("Jump Button", new Vector2(0, -3));
        jump.ShortcutAction = "Jump";
        var jumpClicks = 0;
        jump.Clicked += _ => jumpClicks++;
        var watcher = AddScript<Watcher>("Player");
        var game = CreateGame();
        game.OpenPanel(pause);
        Tick(game);

        Press(game, Key.Enter);
        Assert.Null(game.TopPanel);
        Assert.Equal(0, watcher.Enters);

        // An on-screen button for Jump clicks, and the player's script still jumps.
        Press(game, Key.Space);
        Assert.Equal(1, jumpClicks);
        Assert.Equal(1, watcher.Jumps);
    }

    [Fact]
    public void AnOverPanelsButton_OpensTheGameMenuOverAPausingLetter_ByItsShortcutOrAClick_AndClosingItLeavesTheLetter()
    {
        var letter = AddPanel("Letter");
        letter.Open = true;
        var ok = AddButton("Ok", Vector2.Zero, letter.Entity, ButtonAction.ClosePanel);
        var menu = AddPanel("Game Menu");
        AddButton("Quit", Vector2.Zero, menu.Entity, ButtonAction.Quit);
        var bar = AddButton("Game", new Vector2(-6, 4), action: ButtonAction.OpenPanel);
        bar.Panel = "Game Menu";
        bar.ShortcutAction = "GameMenu";
        bar.Navigable = true;
        var game = CreateGame();
        Tick(game);

        // Off, the letter keeps it from working.
        Press(game, Key.F10);
        Assert.Equal([letter], game.OpenPanels);

        bar.OverPanels = true;
        Press(game, Key.F10);
        Assert.Equal([letter, menu], game.OpenPanels);
        Press(game, Key.Escape);
        Assert.Equal([letter], game.OpenPanels);
        Assert.True(game.IsPaused);

        Click(game, new Vector2(-6, 4));
        Assert.Equal([letter, menu], game.OpenPanels);
        Press(game, Key.Escape);

        // The arrows stay on the letter.
        Press(game, Key.Up);
        Press(game, Key.Left);
        Assert.Equal(ok, game.Focused);
    }

    // A builder's script that lays a wall where the world is clicked, not the UI.
    private sealed class Builder : Behavior
    {
        public int Walls { get; set; }

        protected override void Update()
        {
            if (Input.WasPressed(MouseButton.Left) && !Game.MouseOnUi)
                Walls++;
        }
    }

    private Panel AddPopup(string name)
    {
        var popup = AddPanel(name, pauses: false);
        popup.Popup = true;
        popup.Entity!.AddComponent<Sprite>().Size = new Vector2(4, 3);
        return popup;
    }

    [Fact]
    public void APressOffAPopup_ClosesIt_AndNothingElseHearsIt()
    {
        var popup = AddPopup("Fly Out");
        var hud = AddButton("Hud Button", new Vector2(-6, 4));
        hud.OverPanels = true;
        var hudClicks = 0;
        hud.Clicked += _ => hudClicks++;
        var builder = AddScript<Builder>("Build Tool");
        var game = CreateGame();
        game.OpenPanel(popup);
        Tick(game);

        // A press on its backdrop leaves it open.
        Click(game, new Vector2(1.5f, 1));
        Assert.Equal(popup, game.TopPanel);

        Click(game, new Vector2(6, -3));
        Assert.Null(game.TopPanel);
        Assert.Equal(0, builder.Walls);

        // Even on a button that works over panels, the press only closes it.
        game.OpenPanel(popup);
        Tick(game);
        Click(game, new Vector2(-6, 4));
        Assert.Null(game.TopPanel);
        Assert.Equal(0, hudClicks);

        // With it shut, the world's click is the script's.
        Click(game, new Vector2(6, -3));
        Assert.Equal(1, builder.Walls);
    }

    [Fact]
    public void ClickingAButtonOnAPopup_DoesWhatItDoes_ThenClosesIt()
    {
        var popup = AddPopup("Fly Out");
        var belt = AddButton("Belt", new Vector2(0, 0.5f), popup.Entity);
        var picked = 0;
        belt.Clicked += _ => picked++;
        var contracts = AddPanel("Contracts");
        var open = AddButton("Open Contracts", new Vector2(0, -0.8f), popup.Entity, ButtonAction.OpenPanel);
        open.Panel = "Contracts";
        var game = CreateGame();
        game.OpenPanel(popup);
        Tick(game);

        Click(game, new Vector2(0, 0.5f));
        Assert.Equal(1, picked);
        Assert.Empty(game.OpenPanels);

        // One that opens a window opens it in the pop-up's place: closing it shows nothing underneath.
        game.OpenPanel(popup);
        Tick(game);
        Click(game, new Vector2(0, -0.8f));
        Assert.Equal([contracts], game.OpenPanels);
        Press(game, Key.Escape);
        Assert.Empty(game.OpenPanels);
    }
}
