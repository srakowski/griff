using System.Numerics;
using Griff.Agent;
using Griff.Editing;
using Griff.Input;
using Griff.Modules.FallingBlocks;
using Griff.Modules.Match3;
using Griff.Modules.Strategy;
using Griff.Modules.Tactics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scripting;

namespace Griff.Tests.Runtime;

/// <summary>
/// Modules: genres that live in a place of their own and plug in through extension points, which script components and
/// systems can use the same way.
/// </summary>
public class GameModuleTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    private void Run(Game game, int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    [Fact]
    public void Modules_AreFound_ByName()
    {
        Assert.Equal(["Falling Blocks", "Match-3", "Strategy", "Tactics"], GameModule.All.Select(m => m.Name));
        Assert.Equal(["Griff.Modules.FallingBlocks", "Griff.Modules.Match3", "Griff.Modules.Strategy", "Griff.Modules.Tactics"],
            GameModule.All.Select(m => m.Namespace));
    }

    [Fact]
    public void Of_FindsTheModuleATypeBelongsTo()
    {
        Assert.Equal("Match-3", GameModule.Of(typeof(MatchBoard))?.Name);
        Assert.Equal("Falling Blocks", GameModule.Of(typeof(FallingBoardPreset))?.Name);
        Assert.Equal("Strategy", GameModule.Of(typeof(Commander))?.Name);
        Assert.Equal("Tactics", GameModule.Of(typeof(TacticsUnitPreset))?.Name);
        Assert.Null(GameModule.Of(typeof(Sprite)));
        Assert.Null(GameModule.Of(typeof(Game)));
    }

    [Fact]
    public void Presets_AreFound_InMenuOrder_EachInItsModule()
    {
        Assert.Equal(["Entity.CreateTacticsUnit", "Entity.CreateMatchBoard", "Entity.CreateFallingBoard"], EntityPreset.All.Select(p => p.Id));
        Assert.All(EntityPreset.All, p => Assert.NotNull(GameModule.Of(p.GetType())));
        Assert.All(EntityPreset.All, p => Assert.False(DocComments.Summary(p.GetType()).IsEmpty));
    }

    [Fact]
    public void MatchBoardPreset_MakesABoardDealtAlready()
    {
        var preset = EntityPreset.All.Single(p => p.Id == "Entity.CreateMatchBoard");
        var entity = new Entity(preset.EntityName(_scene));
        preset.Configure(entity, _scene);

        Assert.Equal("Board", entity.Name);
        var board = entity.GetComponent<MatchBoard>()!;
        Assert.Equal(new Vector3(-board.Columns / 2f, board.Rows / 2f, 0), entity.Transform.Position);
        Assert.Equal(board.Rows, entity.GetComponent<Tilemap>()!.Tiles.Count);
    }

    [Fact]
    public void TacticsUnitPreset_GivesAnEmptySceneTurnsAndControls()
    {
        var preset = EntityPreset.All.Single(p => p.Id == "Entity.CreateTacticsUnit");
        var unit = new Entity(preset.EntityName(_scene));
        preset.Configure(unit, _scene);
        foreach (var edit in preset.EditScene(_scene))
            edit.Apply();

        Assert.Equal("Blue Unit", unit.Name);
        Assert.NotNull(unit.GetComponent<TacticsUnit>());
        Assert.Equal("Blue", unit.GetComponent<TurnTaker>()!.Side);
        Assert.NotNull(_scene.FindNamed("Turns")?.GetComponent<TurnOrder>());
        Assert.NotNull(_scene.FindNamed("End Turn")?.GetComponent<Button>());
        Assert.NotNull(_scene.FindNamed("Tactics")?.GetComponent<TacticsControls>());
    }

    [Fact]
    public void Scripts_NameModulesTypesAndTheirGameMembers_WithoutUsings()
    {
        const string source = """
            public class Trainer : Behavior
            {
                protected override void Update()
                {
                    var board = Game.Find<MatchBoard>();
                    if (Game.Find<Producer>() is { } producer && Game.FactionOf("Player") is { } faction && faction.Resources is not null)
                        Game.Train(producer, "Worker");
                    var picked = Game.Selected.Count + (Game.Find<TacticsUnit>()?.Move ?? 0) + (Game.Find<FallingBoard>()?.Score ?? 0);
                }
            }
            """;

        Assert.All(GameModule.All, m => Assert.Contains($"global using {m.Namespace};", ScriptCompiler.ImplicitUsings));
        var result = ScriptCompiler.Compile([new ScriptSource("Trainer.cs", source)]);
        Assert.True(result.Success, string.Join("\n", result.Errors));
    }

    // ----- Extension points, from a script's side -----

    [Fact]
    public void ACounter_CountsFromAnyCounterSource()
    {
        var lives = new Entity("Lives");
        lives.AddComponent<Lives>().Left = 3;
        _scene.Add(lives);
        var shown = new Entity("Shown");
        shown.AddComponent<Text>();
        var counter = shown.AddComponent<Counter>();
        (counter.CountScore, counter.Format) = (true, "{0} / {2}");
        _scene.Add(shown);
        var game = new Game(_scene, _input);

        Run(game);

        Assert.Equal("3 / 5", shown.GetComponent<Text>()!.Value);
    }

    [Fact]
    public void AGridDriver_StepsItsMover_AndTheKeysDont()
    {
        var mover = new Entity("Mover");
        mover.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        mover.AddComponent<Body>().GravityScale = 0;
        mover.AddComponent<GridMovement>().StepTime = 0.1f;
        var driver = mover.AddComponent<StepsRight>();
        driver.Steps = 2;
        _scene.Add(mover);
        var game = new Game(_scene, _input);

        _input.SetKey(Key.Up, true);
        Run(game, 30);

        Assert.Equal(2, mover.Transform.Position.X, 3);
        Assert.Equal(0, mover.Transform.Position.Y, 3);
    }

    [Fact]
    public void ATurnReadyComponent_HearsEachOfItsSidesTurnsBegin()
    {
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Sides, order.Between) = ("Blue, Red", 0);
        _scene.Add(turns);
        var blue = new Entity("Blue");
        blue.AddComponent<TurnTaker>().Side = "Blue";
        var ready = blue.AddComponent<TurnCounter>();
        _scene.Add(blue);
        var red = new Entity("Red");
        red.AddComponent<TurnTaker>().Side = "Red";
        _scene.Add(red);
        var game = new Game(_scene, _input);

        Run(game);
        Assert.Equal(1, ready.Turns);
        order.EndTurn();
        Run(game);
        Assert.Equal(1, ready.Turns);
        order.EndTurn();
        Run(game);

        Assert.Equal(2, ready.Turns);
    }

    [Fact]
    public void APlaytestReport_IsAddedToWhatAnAgentIsTold()
    {
        var thing = new Entity("Thing");
        thing.AddComponent<Lives>().Left = 2;
        _scene.Add(thing);
        var game = new Game(_scene, _input);
        Run(game);

        Assert.Equal(", 2 lives left", Playtesting.Reports(thing));
        Assert.EndsWith(", lives shown.", Playtesting.Mouse(game));
    }

    [Fact]
    public void ASystemClaimingBack_HoldsAPanelThatOpensOnAKey()
    {
        var pause = new Entity("Paused") { IsEnabled = false };
        pause.AddComponent<Panel>().OpenOn = "Pause";
        _scene.Add(pause);
        var game = new Game(_scene, _input);
        var placing = game.AddSystem(new Placing { Busy = true });
        Run(game);

        _input.SetKey(Key.P, true);
        Run(game);
        _input.SetKey(Key.P, false);
        Run(game);
        Assert.False(pause.IsEnabled);

        placing.Busy = false;
        _input.SetKey(Key.P, true);
        Run(game);
        Assert.True(pause.IsEnabled);
    }

    public sealed class Lives : Component, ICounterSource, IPlaytestReport
    {
        public int Left { get; set; }

        (int Value, int Total)? ICounterSource.CountFor(Counter counter) => counter.CountScore ? (Left, 5) : null;

        string IPlaytestReport.Report() => $", {Left} lives left";

        string IPlaytestReport.MouseReport() => ", lives shown";
    }

    public sealed class StepsRight : Component, IGridDriver
    {
        public int Steps { get; set; }

        (int X, int Y)? IGridDriver.NextStep(GridMovement movement) => Steps-- > 0 ? (1, 0) : null;
    }

    public sealed class TurnCounter : Component, ITurnReady
    {
        public int Turns { get; private set; }

        void ITurnReady.TurnBegins() => Turns++;
    }

    public sealed class Placing : GameSystem
    {
        public bool Busy { get; set; }

        protected override bool ClaimsBack => Busy;
    }
}
