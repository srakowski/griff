using System.Numerics;
using Griff.Agent;
using Griff.Editing;
using Griff.Input;
using Griff.Modules.BlockWorlds;
using Griff.Modules.Contraptions;
using Griff.Modules.Dungeons;
using Griff.Modules.FallingBlocks;
using Griff.Modules.FirstPerson;
using Griff.Modules.Match3;
using Griff.Modules.Puzzles;
using Griff.Modules.Strategy;
using Griff.Modules.Tactics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
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
        Assert.Equal(["Adventures", "Battles", "Block Worlds", "Cards", "Contraptions", "Dungeons", "Falling Blocks", "Fighting", "First Person", "Match-3", "Physics Games", "Puzzles", "Rhythm", "Strategy", "Tactics", "Visual Novels"],
            GameModule.All.Select(m => m.Name));
        Assert.Equal(["Griff.Modules.Adventures", "Griff.Modules.Battles", "Griff.Modules.BlockWorlds", "Griff.Modules.Cards", "Griff.Modules.Contraptions", "Griff.Modules.Dungeons", "Griff.Modules.FallingBlocks", "Griff.Modules.Fighting",
            "Griff.Modules.FirstPerson", "Griff.Modules.Match3", "Griff.Modules.PhysicsGames", "Griff.Modules.Puzzles", "Griff.Modules.Rhythm", "Griff.Modules.Strategy", "Griff.Modules.Tactics", "Griff.Modules.VisualNovels"],
            GameModule.All.Select(m => m.Namespace));
    }

    [Fact]
    public void Of_FindsTheModuleATypeBelongsTo()
    {
        Assert.Equal("Match-3", GameModule.Of(typeof(MatchBoard))?.Name);
        Assert.Equal("Falling Blocks", GameModule.Of(typeof(FallingBoardPreset))?.Name);
        Assert.Equal("Strategy", GameModule.Of(typeof(Commander))?.Name);
        Assert.Equal("Tactics", GameModule.Of(typeof(TacticsUnitPreset))?.Name);
        Assert.Equal("Block Worlds", GameModule.Of(typeof(Hotbar))?.Name);
        Assert.Equal("Contraptions", GameModule.Of(typeof(Wire))?.Name);
        Assert.Equal("Dungeons", GameModule.Of(typeof(DungeonGenerator))?.Name);
        Assert.Equal("First Person", GameModule.Of(typeof(Weapon))?.Name);
        Assert.Equal("Puzzles", GameModule.Of(typeof(MoveHistory))?.Name);
        Assert.Equal("Cards", GameModule.Of(typeof(Griff.Modules.Cards.Hand))?.Name);
        Assert.Equal("Adventures", GameModule.Of(typeof(Griff.Modules.Adventures.Hotspot))?.Name);
        Assert.Equal("Physics Games", GameModule.Of(typeof(Griff.Modules.PhysicsGames.Breakable))?.Name);
        Assert.Null(GameModule.Of(typeof(TurnOrder)));
        Assert.Null(GameModule.Of(typeof(Trail)));
        Assert.Null(GameModule.Of(typeof(Sprite)));
        Assert.Null(GameModule.Of(typeof(Game)));
    }

    [Fact]
    public void Presets_AreFound_InMenuOrder_EachInItsModule()
    {
        Assert.Equal(["Entity.CreateTacticsUnit", "Entity.CreateGoal", "Entity.CreateMatchBoard", "Entity.CreateFallingBoard", "Entity.CreateDungeon",
            "Entity.CreateDeck", "Entity.CreatePlayZone", "Entity.CreateCard",
            "Entity.CreateWalkArea", "Entity.CreateHotspot", "Entity.CreateWalker", "Entity.CreateItem", "Entity.CreateStage", "Entity.CreateCharacter", "Entity.CreateNoteLanes", "Entity.CreateReadingMenus", "Entity.CreateSlingshot", "Entity.CreateTower",
            "Entity.CreatePinballTable", "Entity.CreateSparring", "Entity.CreateFlippers", "Entity.CreateBumper", "Entity.CreateKickers", "Entity.CreatePlunger", "Entity.CreateDropTargets", "Entity.CreateLanes", "Entity.CreateTrainingPair"],
            EntityPreset.All.Where(p => GameModule.Of(p.GetType()) is not null).Select(p => p.Id));
        // The rest are Griff's own (Basics, UI, Audio...), each under the category it names.
        Assert.All(EntityPreset.All.Where(p => GameModule.Of(p.GetType()) is null), p => Assert.Single(p.GetType().GetCustomAttributes(typeof(Griff.Scenes.CategoryAttribute), false)));
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
        Assert.Equal("Blue", unit.GetComponent<TurnTaker>()!.Team);
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
                    var seen = Game.FirstPersonViewer ?? Game.Eyes;
                    if (Game.CurrentWeapon is { } weapon)
                        Game.Fire(weapon);
                    if (Game.Tools is { } tools && Game.Grabbed is null && Game.Find<Hotbar>() is { SelectedItem: null })
                        Game.Undo();
                    var moves = (Game.Find<MoveHistory>()?.Moves ?? 0) + (Game.Find<DungeonGenerator>()?.Floor ?? 0) + (Game.Find<Goal>()?.IsCovered == true ? 1 : 0);
                    var parts = Game.FindAll<Wire>().Count + Game.FindAll<Block>().Count + Game.FindAll<Blocks>().Count;
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
        (order.Teams, order.Between) = ("Blue, Red", 0);
        _scene.Add(turns);
        var blue = new Entity("Blue");
        blue.AddComponent<TurnTaker>().Team = "Blue";
        var ready = blue.AddComponent<TurnCounter>();
        _scene.Add(blue);
        var red = new Entity("Red");
        red.AddComponent<TurnTaker>().Team = "Red";
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

    [Fact]
    public void AComponentThatHearsMessages_HearsThemWhereTheGameRuns()
    {
        var bell = new Entity("Bell");
        var ear = bell.AddComponent<Ear>();
        _scene.Add(bell);
        var game = new Game(_scene, _input);

        Assert.Equal(1, game.Send("Ding", 3));
        Assert.Equal(0, game.Send("Dong"));

        Assert.Equal(["Ding 3", "Dong "], ear.Heard);
    }

    [Fact]
    public void ASystemSeeingThroughSomeonesEyes_IsWhereTheGameIsSeenFrom()
    {
        var hero = new Entity("Hero");
        _scene.Add(hero);
        var game = new Game(_scene, _input);
        Assert.Null(game.Eyes);

        game.AddSystem(new Looker { Through = hero });

        Assert.Same(hero, game.Eyes);
        Assert.True(game.LooksThrough(hero));
        Assert.False(game.LooksThrough(new Entity("Someone Else")));
    }

    [Fact]
    public void ASystemsHud_IsLaidOutEachFrame_AndItsButtonsAndNoticeAreTheGames()
    {
        var game = new Game(_scene, _input);
        var hud = game.AddSystem(new Hud());
        Run(game, 3);

        Assert.Equal(3, hud.LaidOut);
        var button = Assert.Single(game.HudButtons);
        Assert.Equal(("Badge", "Go"), (button.Kind, button.Label));
        Assert.Equal("Go!", game.Notice);
    }

    [Fact]
    public void ASystem_HearsOfAGridStepBeforeItsTaken()
    {
        var mover = new Entity("Mover");
        mover.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        mover.AddComponent<Body>().GravityScale = 0;
        mover.AddComponent<GridMovement>().StepTime = 0.1f;
        mover.AddComponent<StepsRight>().Steps = 2;
        _scene.Add(mover);
        var game = new Game(_scene, _input);
        var steps = game.AddSystem(new Steps());

        Run(game, 30);

        Assert.Equal([0f, 1f], steps.From);
    }

    [Fact]
    public void ASystem_KeepsWhatTheSceneLeavesBehind_AsTheGameStops()
    {
        var game = new Game(_scene, _input);
        var keeper = game.AddSystem(new Keeper());
        Run(game);

        game.Stop();

        Assert.Equal(1, keeper.Ended);
    }

    [Fact]
    public void ASystemThatSeesTheWorldItsOwnWay_DrawsItBeforeEverySystemDrawsOverIt()
    {
        var game = new Game(_scene, _input);
        var log = new List<string>();
        game.AddSystem(new Overlay { Log = log });
        game.AddSystem(new OwnView { Log = log });
        Run(game);

        Griff.Rendering.SceneRenderer.Build(game, 16 / 9f);

        Assert.Equal(["view", "overlay", "own view's overlay"], log);
    }

    [Fact]
    public void AComponentClaimingTheNumberKeys_KeepsThemFromTheTools()
    {
        var player = new Entity("Player");
        var tools = player.AddComponent<SandboxTools>();
        _scene.Add(player);
        var game = new Game(_scene, _input);
        Run(game);

        _input.SetKey(Key.D2, true);
        Run(game);
        _input.SetKey(Key.D2, false);
        Run(game);
        Assert.Equal(1, tools.Selected);

        player.AddComponent<Picker>();
        _input.SetKey(Key.D3, true);
        Run(game);

        Assert.Equal(1, tools.Selected);
    }

    [Fact]
    public void AModulesComponent_ListsItselfOnACoreComponentsWorksWith()
    {
        var pushable = ComponentRegistry.CreateDefault().Get(typeof(Pushable));

        var goal = Assert.Single(pushable.WorksWith, w => w.Type == typeof(Goal));
        Assert.Equal("places on the grid to push it to", goal.Note);
    }

    [Fact]
    public void Actions_AreFound_EachInItsModule()
    {
        Assert.Equal(["Entity.GenerateDungeon"], EntityAction.All.Select(a => a.Id));
        Assert.All(EntityAction.All, a => Assert.NotNull(GameModule.Of(a.GetType())));
        Assert.All(EntityAction.All, a => Assert.False(DocComments.Summary(a.GetType()).IsEmpty));
    }

    [Fact]
    public void GoalPreset_GivesTheFirstGoalAMoveHistory()
    {
        var preset = EntityPreset.All.Single(p => p.Id == "Entity.CreateGoal");
        var first = new Entity(preset.EntityName(_scene));
        preset.Configure(first, _scene);
        _scene.Add(first);
        var second = new Entity(preset.EntityName(_scene));
        preset.Configure(second, _scene);

        Assert.Equal("Solved", first.GetComponent<Goal>()!.AllCoveredMessage);
        Assert.NotNull(first.GetComponent<MoveHistory>());
        Assert.NotNull(second.GetComponent<Goal>());
        Assert.Null(second.GetComponent<MoveHistory>());
    }

    [Fact]
    public void GenerateDungeon_FillsTheMapAndPutsThePlayerInTheFirstRoom()
    {
        var preset = EntityPreset.All.Single(p => p.Id == "Entity.CreateDungeon");
        var dungeon = new Entity(preset.EntityName(_scene));
        preset.Configure(dungeon, _scene);
        _scene.Add(dungeon);
        var player = new Entity("Player");
        _scene.Add(player);
        var generator = dungeon.GetComponent<DungeonGenerator>()!;
        generator.Seed = 7;
        var action = EntityAction.All.Single(a => a.Id == "Entity.GenerateDungeon");

        Assert.False(action.AppliesTo(player));
        Assert.True(action.AppliesTo(dungeon));
        foreach (var edit in action.Edits(dungeon, _scene, ComponentRegistry.CreateDefault()))
            edit.Apply();

        var layout = DungeonGen.Generate(generator, 7);
        var map = dungeon.GetComponent<Tilemap>()!;
        Assert.Equal(layout.Tiles, map.Tiles);
        var at = Vector2.Transform(map.CellCenter(layout.Players[0].X, layout.Players[0].Y), dungeon.Transform.WorldMatrix);
        Assert.Equal(at.X, player.Transform.Position.X, 3);
        Assert.Equal(at.Y, player.Transform.Position.Y, 3);
    }

    public sealed class Ear : Component, IHearsMessages
    {
        public List<string> Heard { get; } = [];

        bool IHearsMessages.Hear(string message, object? data)
        {
            Heard.Add($"{message} {data}");
            return message == "Ding";
        }
    }

    public sealed class Picker : Component, IClaimsNumberKeys
    {
    }

    public sealed class Looker : GameSystem, ISeesThroughEyes
    {
        public Entity? Through { get; set; }

        public Entity? Eyes => Through;

        public bool LooksThrough(Entity entity) => entity == Through;
    }

    public sealed class Hud : GameSystem, IHudLayout, INoticeSource
    {
        private readonly List<HudButton> _buttons = [];

        public int LaidOut { get; private set; }

        public void LayOutHud()
        {
            LaidOut++;
            _buttons.Clear();
            _buttons.Add(new HudButton("Badge", 0, "Go", new HudRect(0, 0, 1, 1), () => { }));
        }

        public IReadOnlyList<HudButton> HudButtons => _buttons;

        public string? Notice => "Go!";
    }

    public sealed class Steps : GameSystem, IGridStepListener
    {
        public List<float> From { get; } = [];

        public void GridStepStarting(GridMovement mover) => From.Add(MathF.Round(mover.Entity!.Transform.Position.X, 2));
    }

    public sealed class Keeper : GameSystem, ISceneEndListener
    {
        public int Ended { get; private set; }

        public void SceneEnding() => Ended++;
    }

    public sealed class Overlay : GameSystem
    {
        public List<string> Log { get; init; } = [];

        protected override void Draw(FrameDrawing frame) => Log.Add("overlay");
    }

    public sealed class OwnView : GameSystem, IViewDrawer
    {
        public List<string> Log { get; init; } = [];

        public void DrawView(FrameDrawing frame) => Log.Add("view");

        protected override void Draw(FrameDrawing frame) => Log.Add("own view's overlay");
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

    public sealed class Placing : GameSystem, IClaimsBack
    {
        public bool Busy { get; set; }

        public bool ClaimsBack => Busy;
    }
}
