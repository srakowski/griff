using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Panels: pause menus and options that open over the game, take the input, and give it back.</summary>
public class PanelTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    public PanelTests()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
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

    private void PressPad(Game game, GamepadButton button)
    {
        _input.SetGamepad([button], Vector2.Zero);
        Tick(game);
        _input.SetGamepad([], Vector2.Zero);
        Tick(game);
    }

    private Panel AddPanel(string name, Entity? parent = null, string? openOn = null)
    {
        var entity = new Entity(name);
        var panel = entity.AddComponent<Panel>();
        panel.OpenOn = openOn;
        _scene.Add(entity, parent);
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

    // A player whose script counts the frames it updates on.
    private Ticker AddTicker(string name, Entity? parent = null)
    {
        var entity = new Entity(name);
        var ticker = entity.AddComponent<Ticker>();
        _scene.Add(entity, parent);
        return ticker;
    }

    private sealed class Ticker : Behavior
    {
        public int Updates { get; set; }

        protected override void Update() => Updates++;
    }

    [Fact]
    public void Panels_AreHiddenWhenTheGameStarts_UnlessOpen()
    {
        var hidden = AddPanel("Pause");
        var shown = AddPanel("Title");
        shown.Open = true;

        var game = CreateGame();

        Assert.False(hidden.Entity!.IsEnabled);
        Assert.True(shown.Entity!.IsEnabled);
        Assert.Equal([shown], game.OpenPanels);
    }

    [Fact]
    public void ScriptsInAHiddenPanel_DontStartUntilItOpens()
    {
        var panel = AddPanel("Pause");
        var ticker = AddTicker("Menu Script", panel.Entity);
        var game = CreateGame();

        Tick(game, 3);
        Assert.Equal(0, ticker.Updates);

        game.OpenPanel(panel);
        Tick(game);
        Assert.Equal(1, ticker.Updates);
    }

    [Fact]
    public void OpenOn_OpensThePanel_PausesTheGame_AndPressingItAgainResumes()
    {
        var panel = AddPanel("Paused", openOn: "Pause");
        AddButton("Resume", Vector2.Zero, panel.Entity, ButtonAction.ClosePanel);
        var player = AddTicker("Player");
        var game = CreateGame();
        Tick(game);

        Press(game, Key.P);
        Assert.True(panel.Entity!.IsEnabled);
        Assert.True(panel.Open);
        Assert.True(game.IsPaused);
        var updates = player.Updates;
        var time = game.Time.Total;
        Tick(game, 5);
        Assert.Equal(updates, player.Updates);
        Assert.Equal(time, game.Time.Total);

        Press(game, Key.P);
        Assert.False(panel.Entity.IsEnabled);
        Assert.False(game.IsPaused);
        Tick(game);
        Assert.True(player.Updates > updates);
        Assert.True(game.Time.Total > time);
    }

    [Fact]
    public void Escape_ClosesThePanel_AndTheSamePressDoesntOpenItAgain()
    {
        var panel = AddPanel("Paused", openOn: "Pause");
        AddButton("Quit To Menu", Vector2.Zero, panel.Entity);
        var game = CreateGame();

        Press(game, Key.Escape);
        Assert.True(panel.Entity!.IsEnabled);
        Press(game, Key.Escape);
        Assert.False(panel.Entity.IsEnabled);
        Assert.Null(game.TopPanel);
    }

    [Fact]
    public void GamepadStartOpens_AndBCloses()
    {
        var panel = AddPanel("Paused", openOn: "Pause");
        AddButton("Resume", Vector2.Zero, panel.Entity, ButtonAction.ClosePanel);
        var game = CreateGame();

        PressPad(game, GamepadButton.Start);
        Assert.True(panel.Entity!.IsEnabled);
        PressPad(game, GamepadButton.B);
        Assert.False(panel.Entity.IsEnabled);
    }

    [Fact]
    public void AnEscapeButtonOnThePanel_IsWhatEscapeClicks()
    {
        var panel = AddPanel("Game Over");
        panel.Open = true;
        var menu = AddButton("Menu", Vector2.Zero, panel.Entity);
        menu.Shortcut = Key.Escape;
        var clicks = 0;
        menu.Clicked += _ => clicks++;
        var game = CreateGame();

        Press(game, Key.Escape);

        Assert.Equal(1, clicks);
        Assert.True(panel.Entity!.IsEnabled);
    }

    [Fact]
    public void CloseOnBackOff_IgnoresEscape()
    {
        var panel = AddPanel("Choose");
        panel.Open = true;
        panel.CloseOnBack = false;
        AddButton("Yes", Vector2.Zero, panel.Entity);
        var game = CreateGame();

        Press(game, Key.Escape);

        Assert.True(panel.Entity!.IsEnabled);
    }

    [Fact]
    public void WhileAPanelIsOpen_ControlsOutsideItIgnoreInput()
    {
        var hud = AddButton("Hud Button", new Vector2(0, 3));
        hud.Shortcut = Key.H;
        var hudClicks = 0;
        hud.Clicked += _ => hudClicks++;
        var panel = AddPanel("Paused");
        var resume = AddButton("Resume", Vector2.Zero, panel.Entity);
        var game = CreateGame();
        game.OpenPanel(panel);
        Tick(game);

        // The mouse on the HUD button, its shortcut, and the arrows: none of them reach it.
        _input.MouseWorld = new Vector2(0, 3);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Press(game, Key.H);
        Press(game, Key.Up);

        Assert.Equal(0, hudClicks);
        Assert.False(hud.IsHovered);
        Assert.Equal(resume, game.Focused);
    }

    [Fact]
    public void OpeningAPanel_PicksItsTopControl_OrItsFirst()
    {
        var panel = AddPanel("Paused");
        var resume = AddButton("Resume", new Vector2(0, 1), panel.Entity);
        var quit = AddButton("Quit", new Vector2(0, -1), panel.Entity);
        var game = CreateGame();

        game.OpenPanel(panel);
        Assert.Equal(resume, game.Focused);

        game.ClosePanel();
        panel.First = "Quit";
        game.OpenPanel(panel);
        Assert.Equal(quit, game.Focused);
    }

    [Fact]
    public void ClosingAPanel_GivesBackWhatWasPickedBefore()
    {
        var play = AddButton("Play", new Vector2(0, 1));
        var options = AddButton("Options", new Vector2(0, -1), action: ButtonAction.OpenPanel);
        options.Panel = "Options Panel";
        var panel = AddPanel("Options Panel");
        AddButton("Back", Vector2.Zero, panel.Entity, ButtonAction.ClosePanel);
        var game = CreateGame();

        Press(game, Key.Down);   // picks Play
        Press(game, Key.Down);   // then Options
        Assert.Equal(options, game.Focused);
        Press(game, Key.Enter);
        Assert.Equal(panel, game.TopPanel);

        Press(game, Key.Enter);  // Back
        Assert.Null(game.TopPanel);
        Assert.Equal(options, game.Focused);
        Assert.NotEqual(play, game.Focused);
    }

    [Fact]
    public void PanelsStack_AndClosingOneClosesThoseInsideIt()
    {
        var pause = AddPanel("Paused", openOn: "Pause");
        var options = AddPanel("Options", pause.Entity);
        var game = CreateGame();

        game.OpenPanel(pause);
        game.OpenPanel("Options");
        Assert.Equal([pause, options], game.OpenPanels);
        Assert.True(options.IsTop);
        Assert.False(pause.IsTop);

        game.ClosePanel(pause);
        Assert.Empty(game.OpenPanels);
        Assert.False(options.Entity!.IsEnabled);

        // Opening the pause menu again doesn't bring the options back with it.
        game.OpenPanel(pause);
        Assert.Equal([pause], game.OpenPanels);
    }

    [Fact]
    public void OpenOn_DoesNothingWhileAnotherPanelIsOpen()
    {
        var gameOver = AddPanel("Game Over");
        gameOver.Open = true;
        gameOver.CloseOnBack = false;
        AddButton("Retry", Vector2.Zero, gameOver.Entity);
        var pause = AddPanel("Paused", openOn: "Pause");
        var game = CreateGame();

        Press(game, Key.P);

        Assert.False(pause.Entity!.IsEnabled);
        Assert.Equal([gameOver], game.OpenPanels);
    }

    [Fact]
    public void ShowingAPanelsEntityFromAScript_OpensIt()
    {
        var panel = AddPanel("Game Over");
        panel.PauseGame = false;
        var opened = 0;
        panel.Opened += _ => opened++;
        var game = CreateGame();
        Tick(game);

        panel.Entity!.IsEnabled = true;
        Tick(game);

        Assert.Equal(panel, game.TopPanel);
        Assert.True(panel.Open);
        Assert.False(game.IsPaused);
        Assert.Equal(1, opened);
    }

    [Fact]
    public void APanelThatDoesntPause_LetsTheGameRunBehindIt()
    {
        var panel = AddPanel("Game Over");
        panel.PauseGame = false;
        var player = AddTicker("Player");
        var game = CreateGame();
        game.OpenPanel(panel);

        Tick(game, 3);

        Assert.Equal(3, player.Updates);
    }

    [Fact]
    public void WhilePaused_InteractDoesntWorkLeversOrDialogue()
    {
        var player = new Entity("Player");
        player.AddComponent<BoxCollider>().Layer = "Player";
        player.AddComponent<Body>().GravityScale = 0;
        _scene.Add(player);
        var lever = new Entity("Lever");
        lever.AddComponent<BoxCollider>().IsTrigger = true;
        var @switch = lever.AddComponent<Switch>();
        _scene.Add(lever);
        var panel = AddPanel("Paused", openOn: "Pause");
        AddButton("Resume", Vector2.Zero, panel.Entity, ButtonAction.ClosePanel);
        var game = CreateGame();
        Tick(game, 2);

        Press(game, Key.P);
        Press(game, Key.E);
        Assert.False(@switch.IsOn);

        Press(game, Key.P);
        Press(game, Key.E);
        Assert.True(@switch.IsOn);
    }

    [Fact]
    public void ClosePanelButton_ClosesThePanelItsOn()
    {
        var pause = AddPanel("Paused");
        var options = AddPanel("Options", pause.Entity);
        var back = AddButton("Back", Vector2.Zero, options.Entity, ButtonAction.ClosePanel);
        var game = CreateGame();
        game.OpenPanel(pause);
        game.OpenPanel(options);

        Press(game, Key.Enter);

        Assert.Equal([pause], game.OpenPanels);
        Assert.NotEqual(back, game.Focused);
    }

    [Fact]
    public void OpenPanelButton_WithNoPanelSet_SaysSo()
    {
        AddButton("Options", Vector2.Zero, action: ButtonAction.OpenPanel);
        var game = CreateGame();
        Press(game, Key.Down);

        _input.SetKey(Key.Enter, true);
        var error = Assert.Throws<InvalidOperationException>(() => game.Tick(Frame));
        Assert.Contains("Panel isn't set", error.Message);
    }

    [Fact]
    public void LeavingTheScene_Unpauses()
    {
        var panel = AddPanel("Paused");
        var next = new Scene();
        var game = CreateGame();
        game.SceneLoader = _ => next;
        game.OpenPanel(panel);
        Assert.True(game.IsPaused);

        game.LoadScene("Next");
        Tick(game);

        Assert.False(game.IsPaused);
        Assert.Empty(game.OpenPanels);
    }

    [Fact]
    public void Panels_SaveWhetherTheyreOpen()
    {
        var panel = AddPanel("Paused");
        var serializer = new Griff.Scenes.Serialization.SceneSerializer(Griff.Scenes.Serialization.ComponentRegistry.CreateDefault());
        var game = CreateGame();
        game.OpenPanel(panel);

        var copy = serializer.LoadFromString(serializer.SaveToString(_scene));
        var restored = copy.Entities.Single(e => e.Name == "Paused").GetComponent<Panel>()!;
        var rewound = new Game(copy, new InputState());

        Assert.True(restored.Open);
        Assert.Equal([restored], rewound.OpenPanels);
        Assert.True(rewound.IsPaused);
    }

    [Fact]
    public void Panel_OpensOnItsMessage_UnlessWhatItsInIsHidden()
    {
        var solved = AddPanel("Solved");
        solved.OpenMessage = "Solved";
        var room = new Entity("Room");
        _scene.Add(room);
        var inner = AddPanel("Inner", room);
        inner.OpenMessage = "Solved";
        AddButton("Next", Vector2.Zero, solved.Entity);
        var game = CreateGame();
        Tick(game);
        room.IsEnabled = false;
        Assert.False(solved.Entity!.IsEnabled);

        Assert.Equal(0, game.Send("Other"));
        Tick(game);
        Assert.False(solved.Entity.IsEnabled);

        Assert.Equal(1, game.Send("Solved"));
        Tick(game);
        Assert.True(solved.Entity.IsEnabled);
        Assert.True(game.IsPaused);
        Assert.Equal(solved, game.TopPanel);
        Assert.Equal("Next", game.Focused!.Entity!.Name);
        Assert.False(inner.Entity!.IsEnabled);

        // Once it's open, sending it again opens nothing more.
        Assert.Equal(0, game.Send("Solved"));
    }
}
