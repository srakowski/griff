using System.Numerics;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Projects;

/// <summary>The Tactics template: a menu, and a battle between your knights and archers and the computer's orcs and goblins, side by side in turns.</summary>
public sealed class TacticsTemplateTests : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tactics-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<Game> Create(string? scene = null)
    {
        var project = await Project.CreateAsync(_root, "Skirmish", ProjectTemplates.Get("Tactics"));
        Scene Load(string name) => GameLoader.LoadScene(project, _registry, name);
        var game = new Game(Load(scene ?? Project.DefaultSceneName), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = Load,
            SceneName = scene ?? Project.DefaultSceneName,
            ViewAspect = 16 / 9f,
        };
        _input.MouseWorld = new Vector2(100, 100);
        Run(game, 0.1);
        return game;
    }

    private void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Tap(Game game, Key key, double after = 0.25)
    {
        _input.SetKey(key, true);
        Run(game, Frame);
        _input.SetKey(key, false);
        Run(game, after);
    }

    private void Click(Game game, Vector2 at, double after = 0.1)
    {
        _input.MouseWorld = at;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(game, Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(game, after);
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    private static TurnOrder Turns(Game game) => game.Find("Turns")!.GetComponent<TurnOrder>()!;

    private static List<Entity> Side(Game game, string side) =>
        game.Scene.Entities.Where(e => e.GetComponent<TacticsUnit>() is not null && e.GetComponent<TurnTaker>()!.Side == side).ToList();

    [Fact]
    public async Task Play_OpensABattle_OfKnightsAndArchersAgainstOrcsAndGoblins_WithTheComputerPlayingRed()
    {
        var game = await Create();
        Tap(game, Key.Enter, after: 0.8);
        Assert.Equal("Battle", game.SceneName);

        Assert.Equal(["Archer", "Archer 2", "Knight", "Knight 2"], Side(game, "Blue").Select(e => e.Name).Order());
        Assert.Equal(["Goblin", "Goblin 2", "Orc", "Orc 2", "Orc 3"], Side(game, "Red").Select(e => e.Name).Order());
        var order = Turns(game);
        Assert.Equal(("Blue, Red", "Red", "Blue"), (order.Sides, order.ComputerSides, order.Current));
        Assert.Equal("Blue's turn  ·  Round 1", game.Find("Turns")!.GetComponent<Text>()!.Value);
        Assert.NotNull(game.Find("Tactics")!.GetComponent<TacticsControls>());

        // Archers shoot, swordsmen swing, and picking a unit blips.
        Assert.Equal("Sounds/shoot.wav", game.Find("Archer")!.GetComponent<TacticsUnit>()!.AttackSound);
        Assert.Equal("Sounds/swing.wav", game.Find("Orc")!.GetComponent<TacticsUnit>()!.AttackSound);
        Assert.Equal("Sounds/select.wav", game.Find("Tactics")!.GetComponent<TacticsControls>()!.SelectSound);
        Assert.Equal(TacticsStance.Hold, game.Find("Goblin 2")!.GetComponent<TacticsUnit>()!.Stance);
    }

    [Fact]
    public async Task A_knight_is_picked_and_sent_with_clicks_and_the_computer_answers_on_its_turn()
    {
        var game = await Create("Battle");
        var knight = game.Find("Knight")!;
        var from = At(knight);
        var orc = game.Find("Orc 2")!;
        var orcFrom = At(orc);

        Click(game, from);
        Assert.Equal(knight, game.Find("Tactics")!.GetComponent<TacticsControls>()!.Selected);
        Click(game, from + new Vector2(3, 0), after: 0.8);
        Assert.Equal(from + new Vector2(3, 0), At(knight));

        Tap(game, Key.Tab, after: 0.2);
        Assert.Equal("Red", Turns(game).Current);
        Run(game, 8);
        Assert.Equal("Blue", Turns(game).Current);
        Assert.NotEqual(orcFrom, At(orc));
    }

    [Fact]
    public async Task Beating_every_orc_and_goblin_opens_Victory()
    {
        var game = await Create("Battle");
        foreach (var foe in Side(game, "Red"))
            foe.GetComponent<Health>()!.Kill();
        Run(game, 0.5);

        Assert.True(game.Find("Victory")!.IsEnabled);
        Assert.False(game.Find("Defeat")!.IsEnabled);
    }

    [Fact]
    public async Task Ending_every_turn_without_fighting_loses_to_the_computer()
    {
        var game = await Create("Battle");
        for (var i = 0; i < 40 && !game.Find("Defeat")!.IsEnabled; i++)
        {
            if (Turns(game).Current == "Blue" && !Turns(game).IsBetweenTurns)
                Tap(game, Key.Tab, after: 0.1);
            Run(game, 4);
        }

        Assert.True(game.Find("Defeat")!.IsEnabled);
        Assert.Empty(Side(game, "Blue"));
    }
}
