using System.Numerics;
using Griff.Agent;
using Griff.Editing;
using Griff.Modules.Contraptions;
using Griff.Modules.Match3;
using Griff.Modules.Strategy;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Agent;

public class SceneToolsTests
{
    private sealed class FakeHost : IAgentScene
    {
        public Scene? Scene { get; } = new();

        public string? ScenePath => "Scenes/Main.scene";

        public bool IsPlaying { get; set; }

        public ComponentRegistry Components { get; } = ComponentRegistry.CreateDefault();

        public EditHistory History { get; } = new();

        public IReadOnlyList<Entity> Selection { get; set; } = [];

        public void Execute(IEdit edit) => History.Execute(edit);

        public IDisposable BeginGroup(string description) => History.BeginGroup(description);

        public Entity? InstantiatePrefab(string name)
        {
            if (name is not ("Coin" or "Prefabs/Coin.prefab"))
                return null;
            var coin = new Entity("Coin") { Prefab = "Prefabs/Coin.prefab" };
            coin.AddComponent<Sprite>();
            return coin;
        }

        public KnownNames? Names { get; set; }
    }

    private readonly FakeHost _host = new();
    private readonly Dictionary<string, AgentTool> _tools;

    public SceneToolsTests() => _tools = new SceneTools(_host).GetTools().ToDictionary(t => t.Name);

    // As the server runs them: a mistake comes back as an error result.
    private ToolResult Run(string tool, string json)
    {
        try
        {
            return _tools[tool].Invoke(ToolArgs.Parse(json)).GetAwaiter().GetResult();
        }
        catch (AgentException ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }

    private Entity Named(string name) => _host.Scene!.Entities.Single(e => e.Name == name);

    [Fact]
    public void Create_BuildsComponentsChildrenAndPlacement_AsOneUndoStep()
    {
        var result = Run("entity_create", """
            {"entity": {"name": "Hero", "position": [2, 3], "scale": 2,
              "components": {"Sprite": {"size": [1, 2], "tint": "#FF0000"}, "PlatformerMovement": {"speed": 9}},
              "children": [{"name": "Lantern", "position": {"x": 0.5, "y": 1}, "components": {"Light": {"radius": 6}}}]}}
            """);

        Assert.False(result.IsError, result.Text);
        var hero = Named("Hero");
        Assert.Equal(new Vector3(2, 3, 0), hero.Transform.Position);
        Assert.Equal(new Vector3(2, 2, 1), hero.Transform.Scale);
        Assert.Equal(new Vector2(1, 2), hero.GetComponent<Sprite>()!.Size);
        Assert.Equal(Color.Red, hero.GetComponent<Sprite>()!.Tint);
        Assert.Equal(9, hero.GetComponent<PlatformerMovement>()!.Speed);
        // What it needs came along with it.
        Assert.NotNull(hero.GetComponent<Body>());
        Assert.NotNull(hero.GetComponent<Collider>());
        Assert.Contains("because PlatformerMovement needs it", result.Text);
        var lantern = Assert.Single(hero.Children);
        Assert.Equal(6, lantern.GetComponent<Light>()!.Radius);
        Assert.Equal(new Vector3(0.5f, 1, 0), lantern.Transform.Position);
        Assert.Same(hero, Assert.Single(_host.Selection));

        _host.History.Undo();
        Assert.Equal(0, _host.Scene!.Count);
    }

    [Fact]
    public void Create_ManyAtOnce_UnderAParent()
    {
        Run("entity_create", """{"entity": {"name": "Coins"}}""");

        var result = Run("entity_create", """{"parent": "Coins", "entities": [{"name": "A"}, {"name": "B", "components": [{"type": "Pickup", "amount": 5}]}]}""");

        Assert.False(result.IsError, result.Text);
        Assert.Equal(["A", "B"], Named("Coins").Children.Select(c => c.Name));
        Assert.Equal(5, Named("B").GetComponent<Pickup>()!.Amount);
    }

    [Fact]
    public void Create_FromAPrefab()
    {
        var result = Run("entity_create", """{"entity": {"prefab": "Coin", "name": "Gold Coin", "position": [4, 0]}}""");

        Assert.False(result.IsError, result.Text);
        Assert.NotNull(Named("Gold Coin").GetComponent<Sprite>());
    }

    [Fact]
    public void Create_ExplainsMistakes()
    {
        Assert.Contains("no component \"Sprit\"", Run("entity_create", """{"entity": {"components": {"Sprit": {}}}}""").Text);
        Assert.Contains("has no property \"colour\"", Run("entity_create", """{"entity": {"components": {"Sprite": {"colour": 1}}}}""").Text);
        Assert.Contains("Expected", Run("entity_create", """{"entity": {"components": {"Sprite": {"size": "big"}}}}""").Text);
        Assert.Equal(0, _host.Scene!.Count);
    }

    [Fact]
    public void Update_SetsPropertiesAddsAndRemovesComponents_AsOneUndoStep()
    {
        Run("entity_create", """{"entity": {"name": "Torch", "components": {"Sprite": {}, "Light": {"radius": 3}}}}""");
        Run("entity_create", """{"entity": {"name": "Wall"}}""");
        var torch = Named("Torch");
        var undoBefore = _host.History.UndoDescription;

        var result = Run("entity_update", """
            {"entity": "Torch", "changes": {"name": "Big Torch", "parent": "Wall", "position": [1, 1],
              "components": {"Light": {"radius": 8, "flicker": 0.2}, "ParticleEmitter": {}}, "remove": ["Sprite"]}}
            """);

        Assert.False(result.IsError, result.Text);
        Assert.Equal("Big Torch", torch.Name);
        Assert.Same(Named("Wall"), torch.Parent);
        Assert.Equal(8, torch.GetComponent<Light>()!.Radius);
        Assert.Equal(0.2f, torch.GetComponent<Light>()!.Flicker);
        Assert.NotNull(torch.GetComponent<ParticleEmitter>());
        Assert.Null(torch.GetComponent<Sprite>());

        _host.History.Undo();
        Assert.Equal(undoBefore, _host.History.UndoDescription);
        Assert.Equal("Torch", torch.Name);
        Assert.Null(torch.Parent);
        Assert.Equal(3, torch.GetComponent<Light>()!.Radius);
        Assert.NotNull(torch.GetComponent<Sprite>());
        Assert.Null(torch.GetComponent<ParticleEmitter>());
    }

    [Fact]
    public void Update_FailsOnAChangeItDoesNotKnow_ChangingNothing()
    {
        Run("entity_create", """{"entity": {"name": "Torch", "components": {"Light": {"radius": 3}}}}""");
        var undoBefore = _host.History.UndoDescription;

        // A component's properties go in "components": named at the top, they'd otherwise be left out without a word.
        var result = Run("entity_update", """{"entity": "Torch", "changes": {"name": "Big Torch", "Light": {"radius": 8}}}""");
        Assert.True(result.IsError);
        Assert.Contains("\"components\"", result.Text);
        var typo = Run("entity_update", """{"updates": [{"entity": "Torch", "changes": {"positon": [1, 1]}}]}""");
        Assert.True(typo.IsError);
        Assert.Contains("positon", typo.Text);

        Assert.Equal("Torch", Named("Torch").Name);
        Assert.Equal(3, Named("Torch").GetComponent<Light>()!.Radius);
        Assert.Equal(undoBefore, _host.History.UndoDescription);
    }

    [Fact]
    public void Update_TheSecondOfAKind()
    {
        Run("entity_create", """{"entity": {"name": "Lamp", "components": [{"type": "Light", "radius": 1}, {"type": "Light", "radius": 2}]}}""");

        Run("entity_update", """{"entity": "Lamp", "changes": {"components": {"Light#2": {"radius": 9}}}}""");

        Assert.Equal([1f, 9f], Named("Lamp").GetComponents<Light>().Select(l => l.Radius));
    }

    [Fact]
    public void Get_ShowsChangedValuesInTheFormCreateTakes()
    {
        Run("entity_create", """{"entity": {"name": "Sign", "position": [1, 2], "components": {"Dialogue": {"lines": ["Hello", "there"]}}}}""");

        var json = Run("entity_get", """{"entity": "Sign"}""").Text;

        Assert.Contains("\"type\": \"Dialogue\"", json);
        Assert.Contains("\"Hello\"", json);
        Assert.Contains("\"position\": [", json);
        Assert.DoesNotContain("\"scale\"", json); // unchanged from the default
    }

    [Fact]
    public void Get_WhilePlaying_ShowsWhatComponentsSayAboutThemselves()
    {
        Run("entity_create", """{"entity": {"name": "Music", "components": {"BeatClock": {}, "Spawner": {"prefab": "Coin"}}}}""");

        Assert.DoesNotContain("\"live\"", Run("entity_get", """{"entity": "Music"}""").Text);

        _host.IsPlaying = true;
        var json = Run("entity_get", """{"entity": "Music"}""").Text;

        Assert.Contains("\"live\": {", json);
        Assert.Contains("\"isPlaying\": false", json);
        Assert.Contains("\"beat\": -1", json);
        Assert.Contains("\"secondsPerBeat\": 0.5", json);
        Assert.Contains("\"spawned\": 0", json);
        // Lists aren't values to show.
        Assert.DoesNotContain("\"alive\"", json);

        // And component_describe says what they mean.
        var described = Run("component_describe", """{"component": "BeatClock"}""").Text;
        Assert.Contains("While the game runs", described);
        Assert.Contains("- beat: a whole number. The beat it's on", described);
    }

    /// <summary>Counts the steps the game has taken.</summary>
    public sealed class StepCounter : Component
    {
        /// <summary>Steps taken.</summary>
        public long Steps => 1234567890123;
    }

    /// <summary>Drifts along.</summary>
    [Griff.Runtime.Crowd]
    public sealed class Drifter : Griff.Runtime.Behavior
    {
        /// <summary>How fast, in units a second.</summary>
        public float Speed { get; set; } = 1;
    }

    [Fact]
    public void Describing_a_crowd_script_says_it_changes_nothing_but_its_own_entity()
    {
        _host.Components.Register<Drifter>();

        var described = Run("component_describe", """{"component": "Drifter"}""").Text;

        Assert.Contains("A crowd script ([Crowd]): the game runs a crowd of these at once", described);
        Assert.Contains("- speed: a number", described);
        Assert.DoesNotContain("crowd", Run("component_describe", """{"component": "Sprite"}""").Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Goes off.</summary>
    public sealed class Bomb : Griff.Runtime.Behavior
    {
        public int Fuses { get; set; }

        [Action]
        public void Explode() => Fuses++;

        [Action]
        public void Delay(float seconds) => Fuses += (int)seconds;
    }

    [Fact]
    public void Actions_reach_component_describe_and_components_list_by_themselves_scripts_too()
    {
        _host.Components.Register<Bomb>();

        var health = Run("component_describe", """{"component": "Health"}""").Text;
        Assert.Contains("Actions (what anything can make it do with no code", health);
        Assert.Contains("- Damage (done with a number, its amount: a trigger's with, 1 without one). Takes amount hit points away", health);
        Assert.Contains("- Kill. Dies straight away", health);
        var bomb = Run("component_describe", """{"component": "Bomb"}""").Text;
        Assert.Contains("- Explode", bomb);
        Assert.Contains("- Delay (done with a number, its seconds", bomb);
        Assert.Contains("Actions: Explode, Delay.", Run("components_list", """{"search": "Bomb"}""").Text);

        var trigger = Run("component_describe", """{"component": "Trigger"}""").Text;
        Assert.Contains("- do: text = null", trigger);
        Assert.Contains("Names an action of the entities targets names, or this entity without any", trigger);
        Assert.Contains("- SetFlag (done with a name, its flag", trigger);
    }

    /// <summary>Flies.</summary>
    public sealed class Ship : Griff.Runtime.Behavior
    {
        /// <summary>Fuel left in the tank.</summary>
        [Output(OutOf = nameof(Tank))]
        public float Fuel { get; set; }

        public float Tank { get; set; } = 100;

        /// <summary>How many of a crate it carries.</summary>
        [Output("Crates", Example = "Ore")]
        public int Carried(string kind) => kind.Length;
    }

    [Fact]
    public void Outputs_reach_component_describe_and_components_list_by_themselves_scripts_too()
    {
        _host.Components.Register<Ship>();

        var board = Run("component_describe", """{"component": "TurnOrder"}""").Text;
        Assert.Contains("Outputs (what anything can read with no code", board);
        Assert.Contains("- Turn (a name). The side whose turn it is", board);
        Assert.Contains("- Round (a whole number). How many times the turns have come round", board);
        Assert.Contains("- Left (a whole number, read by a side after a colon, e.g. \"Left:Enemy\")", board);
        var ship = Run("component_describe", """{"component": "Ship"}""").Text;
        Assert.Contains("- Fuel (a number, out of its tank)", ship);
        Assert.Contains("- Crates (a whole number, read by a kind after a colon, e.g. \"Crates:Ore\")", ship);
        Assert.Contains("Outputs: Fuel, Crates:Ore.", Run("components_list", """{"search": "Ship"}""").Text);

        var counter = Run("component_describe", """{"component": "Counter"}""").Text;
        Assert.Contains("Names an output of the entity track names, or without one this entity or the nearest it's inside with it", counter);
        Assert.Contains("- Reached (true or false)", counter);
    }

    [Fact]
    public void Counters_bars_and_texts_read_outputs_made_like_any_component_and_one_nothing_has_is_reported()
    {
        Run("entity_create", """{"entity": {"name": "Turns", "components": {"TurnOrder": {"sides": "Knights, Orcs"}}}}""");
        Run("entity_create", """{"entity": {"name": "Label", "components": {"Text": {"track": "Turns", "output": "Turn", "value": "{0} to move"}}}}""");
        var text = Named("Label").GetComponent<Text>()!;
        Assert.Equal(("Turns", "Turn"), (text.Track, text.Output));
        Assert.DoesNotContain("Text.Output", Run("entity_get", """{"entity": "Label"}""").Text);

        Run("entity_create", """{"entity": {"name": "Rounds", "components": {"Counter": {"track": "Turns", "output": "Turn"}}}}""");
        Assert.Contains("Counter.Output", Run("entity_get", """{"entity": "Rounds"}""").Text);
        Run("entity_update", """{"entity": "Rounds", "changes": {"components": {"Counter": {"output": "Lives"}}}}""");
        Assert.Contains("Nothing it reads has an output called \"Lives\"", Run("scene_tree", "{}").Text);
    }

    [Fact]
    public void Triggers_are_made_like_any_component_and_a_do_nothing_can_do_is_reported()
    {
        Run("entity_create", """{"entity": {"name": "Boss", "components": {"Health": {"max": 10}}}}""");
        var made = Run("entity_create", """{"entity": {"name": "Rules", "components": {"Trigger": {"message": "Score", "do": "Damage", "targets": "Boss", "with": "2"}}}}""").Text;
        Assert.DoesNotContain("Trigger.Do", made);
        var trigger = Named("Rules").GetComponent<Trigger>()!;
        Assert.Equal(("Score", "Damage", "Boss", "2"), (trigger.Message, trigger.Do, trigger.Targets, trigger.With));
        Assert.Contains("When Score arrives, Damage 2 on Boss.", Run("entity_get", """{"entity": "Rules"}""").Text);

        Run("entity_update", """{"entity": "Rules", "changes": {"components": {"Trigger": {"do": "Explode"}}}}""");
        Assert.Contains("Trigger.Do", Run("entity_get", """{"entity": "Rules"}""").Text);
        Assert.Contains("Nothing it's done on has an action called \"Explode\"", Run("scene_tree", "{}").Text);
    }

    [Fact]
    public void Long_numbers_a_component_shows_while_playing_are_shown_too()
    {
        // Time.Frame and Time.DrawnFrames are longs: a script showing them is read like any other number.
        _host.Components.Register<StepCounter>();
        Run("entity_create", """{"entity": {"name": "Counter", "components": {"StepCounter": {}}}}""");
        _host.IsPlaying = true;

        Assert.Contains("\"steps\": 1234567890123", Run("entity_get", """{"entity": "Counter"}""").Text);
        Assert.Contains("- steps: a whole number", Run("component_describe", """{"component": "StepCounter"}""").Text);
    }

    [Fact]
    public void Cells_a_component_shows_while_playing_are_x_y_arrays()
    {
        Assert.Equal("[2,3]", AgentValues.ToJson((2, 3))!.ToJsonString());
        Assert.Equal("[x, y], whole numbers or null", AgentValues.Describe(typeof((int X, int Y)?)));

        Run("entity_create", """{"entity": {"name": "Board", "components": {"MatchBoard": {}}}}""");
        _host.IsPlaying = true;
        Assert.Contains("\"picked\": null", Run("entity_get", """{"entity": "Board"}""").Text);
        Assert.Contains("- cursor: [x, y], whole numbers or null. The cell this machine's player's keyboard cursor is on",
            Run("component_describe", """{"component": "MatchBoard"}""").Text);
    }

    [Fact]
    public void Describing_a_component_says_how_it_draws_and_what_it_sends()
    {
        Assert.Contains("Draws itself (its entity draws at Order 0", Run("component_describe", """{"component": "Sprite"}""").Text);
        Assert.Contains("Draws itself, along with the rest of its entity", Run("component_describe", """{"component": "Trail"}""").Text);
        Assert.Contains("Draws itself over the whole world", Run("component_describe", """{"component": "Wire"}""").Text);
        Assert.Contains("Changes how its entity is drawn", Run("component_describe", """{"component": "Button"}""").Text);
        Assert.Contains("each machine keeps its own entity", Run("component_describe", """{"component": "Camera"}""").Text);
        Assert.Contains("the player it moves for runs it", Run("component_describe", """{"component": "PlatformerMovement"}""").Text);
        var worker = Run("component_describe", """{"component": "Worker"}""").Text;
        Assert.Contains("- load: a whole number. How much it's carrying. (Sent in a network game.)", worker);
    }

    [Fact]
    public void A_match_boards_moves_and_goals_are_set_like_any_field_and_read_back_live()
    {
        Run("entity_create", """{"entity": {"name": "Board", "components": {"MatchBoard": {"moves": 20, "targetScore": 1000, "collect": "0:15", "swaps": 5, "cleared": "0:15"}}}}""");
        var board = Named("Board").GetComponent<MatchBoard>()!;
        Assert.Equal((20, 1000, "0:15"), (board.Moves, board.TargetScore, board.Collect));
        Assert.Equal(15, board.MovesLeft);

        _host.IsPlaying = true;
        var got = Run("entity_get", """{"entity": "Board"}""").Text;
        Assert.Contains("\"movesLeft\": 15", got);
        Assert.Contains("\"goalsMet\": false", got);

        var described = Run("component_describe", """{"component": "MatchBoard"}""").Text;
        Assert.Contains("## Score and Goals", described);
        Assert.Contains("- collect: text = null. Gems to clear to win the level", described);
        Assert.Contains("- countKind: a whole number = -1. Count the things of one kind", Run("component_describe", """{"component": "Counter"}""").Text);
    }

    [Fact]
    public void Get_SaysWhichMapATilemapIsALayerOf()
    {
        Run("entity_create", """{"entity": {"name": "Level", "components": {"Tilemap": {"image": "Tiles/ground.png", "tiles": ["00"]}}}}""");
        Run("entity_create", """{"parent": "Level", "entity": {"name": "Decor", "components": {"Tilemap": {"solid": false, "tiles": ["11"]}}}}""");

        Assert.Same(Named("Level").GetComponent<Tilemap>(), Named("Decor").GetComponent<Tilemap>()!.LayerOf);
        Assert.Contains("\"note\": \"A layer of Level", Run("entity_get", """{"entity": "Decor"}""").Text);
        Assert.DoesNotContain("\"note\"", Run("entity_get", """{"entity": "Level"}""").Text);
    }

    [Fact]
    public void Select_PicksSeveral_AddsToThem_AndClears()
    {
        Run("entity_create", """{"entities": [{"name": "A"}, {"name": "B"}, {"name": "C"}]}""");

        var result = Run("select", """{"entities": ["C", "A"]}""");
        Assert.Equal(["C", "A"], _host.Selection.Select(e => e.Name));
        Assert.Contains("Selected 2 entities", result.Text);
        Assert.Contains("selected: C #", Run("scene_tree", "{}").Text);

        Run("select", """{"entity": "B", "add": true}""");
        Assert.Equal(["C", "A", "B"], _host.Selection.Select(e => e.Name));
        // Adding one that's already selected makes it the primary one.
        Run("select", """{"entity": "C", "add": true}""");
        Assert.Equal(["A", "B", "C"], _host.Selection.Select(e => e.Name));

        Assert.Contains("Nothing is selected", Run("select", "{}").Text);
        Assert.Empty(_host.Selection);
        Assert.True(Run("select", """{"entities": ["Nope"]}""").IsError);
    }

    [Fact]
    public void Tree_FindDeleteAndClone()
    {
        Run("entity_create", """{"entity": {"name": "Level", "children": [{"name": "Spike", "position": [3, 0], "components": {"DamageOnTouch": {}}}]}}""");

        Assert.Contains("  - Spike #", Run("scene_tree", "{}").Text);
        Assert.Contains("Level/Spike", Run("entity_find", """{"component": "DamageOnTouch"}""").Text);
        Assert.Contains("Nothing matches", Run("entity_find", """{"near": [50, 50]}""").Text);

        Run("entity_clone", """{"entity": "Level/Spike", "positions": [[4, 0], [5, 0]]}""");
        Assert.Equal(3, Named("Level").Children.Count);
        Assert.Equal(new Vector3(5, 0, 0), Named("Level").Children[2].Transform.Position);

        Assert.Contains("3 entities are called \"Spike\"", Run("entity_delete", """{"entities": ["Spike"]}""").Text);

        // Given nothing to delete, it says so rather than deleting nothing; "entity" names one, like the other tools.
        var nothing = Run("entity_delete", """{"entitie": "Level"}""");
        Assert.True(nothing.IsError);
        Assert.Contains("\"entities\"", nothing.Text);
        Assert.Contains("Deleted Spike", Run("entity_delete", """{"entity": "Level/Spike"}""").Text);
        Assert.Equal(2, Named("Level").Children.Count);

        Run("entity_delete", """{"entities": ["Level"]}""");
        Assert.Equal(0, _host.Scene!.Count);
    }

    [Fact]
    public void Entities_CanBeNamedByIdPrefixOrPath()
    {
        Run("entity_create", """{"entity": {"name": "A", "children": [{"name": "B"}]}}""");
        var b = Named("B");

        Assert.Same(b, SceneTools.Resolve(_host.Scene!, "A/B"));
        Assert.Same(b, SceneTools.Resolve(_host.Scene!, "#" + b.Id.ToString("N")[..6]));
        Assert.Same(b, SceneTools.Resolve(_host.Scene!, b.Id.ToString()));
        Assert.Throws<AgentException>(() => SceneTools.Resolve(_host.Scene!, "C"));
    }

    [Fact]
    public void Describe_ListsEveryPropertyWithItsKindAndDefault()
    {
        var text = Run("component_describe", """{"component": "PlatformerMovement"}""").Text;

        Assert.Contains("# PlatformerMovement (Movement)", text);
        Assert.Contains("Requires: Body", text);
        Assert.Contains("- jumpHeight: a number = ", text);
        Assert.Contains("- airJumps: a whole number = 0", text);
    }

    [Fact]
    public void Describe_ListsPropertiesUnderTheirGroups_AndSaysWhatTheyNeed()
    {
        var text = Run("component_describe", """{"component": "PlatformerMovement"}""").Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var dash = text.IndexOf("## Dash");
        Assert.True(dash > text.FindIndex(l => l.StartsWith("- speed:", StringComparison.Ordinal)));
        Assert.StartsWith("- dash:", text[dash + 1]);
        Assert.StartsWith("- dashSpeed:", text[dash + 2]);
        Assert.Contains("(Only used when dash is on.)", text[dash + 2]);
        Assert.Contains("## Sounds and Effects", text);

        var button = Run("component_describe", """{"component": "Button"}""").Text;
        Assert.Contains("(Only used when action is OpenPanel.)", button);
    }

    [Fact]
    public void Describe_SaysWhichPropertiesAreDistances()
    {
        var text = Run("component_describe", """{"component": "Unit"}""").Text.Split('\n');

        Assert.Contains("(A distance:", text.Single(l => l.StartsWith("- sight:", StringComparison.Ordinal)));
        Assert.DoesNotContain("(A distance:", text.Single(l => l.StartsWith("- speed:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Describe_SaysWhichPropertiesNameEntities()
    {
        var text = Run("component_describe", """{"component": "Switch"}""").Text.Split('\n');

        Assert.Contains("(Names entities: their names, separated by commas.)", text.Single(l => l.StartsWith("- targets:", StringComparison.Ordinal)));
        Assert.Contains("(Names an entity.)", text.Single(l => l.StartsWith("- prompt:", StringComparison.Ordinal)));
        Assert.DoesNotContain("(Names", text.Single(l => l.StartsWith("- key:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Rename_RenamesItInTheFieldsThatNameIt_AndSaysSo()
    {
        Run("entity_create", """
            {"entities": [{"name": "Player"}, {"name": "Bridge"},
              {"name": "Camera", "components": {"Follow": {"target": "Player"}}},
              {"name": "Lever", "components": {"Switch": {"targets": "Bridge, Player"}}}]}
            """);

        var result = Run("entity_update", """{"entity": "Player", "changes": {"name": "Hero"}}""");

        Assert.False(result.IsError, result.Text);
        Assert.Equal("Hero", Named("Camera").GetComponent<Follow>()!.Target);
        Assert.Equal("Bridge, Hero", Named("Lever").GetComponent<Switch>()!.Targets);
        Assert.Contains("Camera's Follow.Target, Lever's Switch.Targets", result.Text);
        _host.History.Undo();
        Assert.Equal("Player", Named("Camera").GetComponent<Follow>()!.Target);
        Assert.NotNull(Named("Player"));
    }

    [Fact]
    public void PrefabsLayersAndMessagesThatMatchNothing_ShowInTheTreeAndOnTheEntity()
    {
        _host.Names = new KnownNames(["Bullet"], ["Main"], ["Fire"], ["Player"], ["Score"], []);
        Run("entity_create", """
            {"entities": [{"name": "Turret", "components": {"Shooter": {"prefab": "Bulet", "action": "Fire"}}},
              {"name": "Hud", "components": {"Counter": {"message": "Coins"}}},
              {"name": "Gem", "components": {"Pickup": {"layers": "Player, Hero", "message": "Score"}}}]}
            """);

        var tree = Run("scene_tree", "{}").Text;
        Assert.Contains("Names that match nothing in the project", tree);
        Assert.Contains("Shooter.Prefab: There's no prefab called \"Bulet\" in the project.", tree);
        Assert.Contains("Counter.Message: Nothing sends \"Coins\"", tree);
        Assert.Contains("Pickup.Layers: Nothing is on the layer \"Hero\"", tree);
        Assert.DoesNotContain("Shooter.Action", tree);

        var turret = Run("entity_get", """{"entity": "Turret"}""").Text;
        Assert.Contains("\"unmatched\"", turret);
        Assert.Contains("Shooter.Prefab", turret);

        Run("entity_update", """{"entity": "Turret", "changes": {"components": {"Shooter": {"prefab": "Bullet"}}}}""");
        Assert.DoesNotContain("unmatched", Run("entity_get", """{"entity": "Turret"}""").Text);
    }

    [Fact]
    public void Describe_ListsTheFontsAFontCanName_AsThePickerGroupsThem()
    {
        _host.Names = new KnownNames([], [], [], [], [], [], fonts: () => Griff.Fonts.FontChoices.For(null));

        var text = Run("component_describe", """{"component": "Text"}""").Text;
        var font = text.Split('\n').Single(l => l.StartsWith("- font:", StringComparison.Ordinal));
        Assert.Contains("(Names a font.)", font);
        Assert.Contains("Safe to ship: \"Selawik Semibold\" (built in)", font);
        Assert.Contains("\"Fonts/PressStart2P-Regular.ttf\" (Press Start 2P; pixel, free: copied into Assets)", font);
        Assert.Contains("Art_AddFreeFont", font);
        if (Griff.Fonts.InstalledFonts.Families.Count > 0)
        {
            Assert.Contains("Only on this PC (Installed fonts aren't shipped", font);
            Assert.True(font.IndexOf("Only on this PC", StringComparison.Ordinal) > font.IndexOf("JetBrains Mono Bold", StringComparison.Ordinal));
        }

        // A font that's nowhere is flagged, as the Inspector flags it.
        Run("entity_create", """{"entities": [{"name": "Title", "components": {"Text": {"font": "Fonts/PressStart2P-Regular.ttf"}}}]}""");
        Assert.Contains("Text.Font: There's no font file in Assets or font installed on this PC called", Run("scene_tree", "{}").Text);
    }

    [Fact]
    public void Describe_SaysWhatAFieldNames()
    {
        var spawner = Run("component_describe", """{"component": "Spawner"}""").Text;
        Assert.Contains("(Names a prefab.)", spawner);
        var pickup = Run("component_describe", """{"component": "Pickup"}""").Text;
        Assert.Contains("(Names collision layers: their names, separated by commas.)", pickup);
        Assert.Contains("(Names a message.)", pickup);
        var box = Run("component_describe", """{"component": "BoxCollider"}""").Text;
        Assert.Contains("(Names a collision layer: this entity's own, which other fields can name.)", box);
    }

    [Fact]
    public void NamesThatMatchNothing_ShowInTheTreeAndOnTheEntity()
    {
        Run("entity_create", """
            {"entities": [{"name": "Player"}, {"name": "Camera", "components": {"Follow": {"target": "Playr"}}},
              {"name": "Lever", "components": {"Switch": {"targets": "Gate, Player"}}}]}
            """);

        var tree = Run("scene_tree", "{}").Text;
        Assert.Contains("Names that match nothing", tree);
        Assert.Contains("Follow.Target: \"Playr\"", tree);
        Assert.Contains("Switch.Targets: \"Gate\"", tree);

        var camera = Run("entity_get", """{"entity": "Camera"}""").Text;
        Assert.Contains("\"unmatched\"", camera);
        Assert.Contains("Nothing in the scene is called", camera);
        Assert.Contains("Playr", camera);
        Assert.DoesNotContain("unmatched", Run("entity_get", """{"entity": "Player"}""").Text);

        Run("entity_update", """{"entity": "Camera", "changes": {"components": {"Follow": {"target": "Player"}}}}""");
        Assert.DoesNotContain("Follow.Target", Run("scene_tree", "{}").Text);
    }

    [Fact]
    public void ListComponents_ByCategoryAndSearch()
    {
        Assert.Contains("## Movement", Run("components_list", "{}").Text);
        var lights = Run("components_list", """{"search": "torch"}""").Text;
        Assert.Contains("Light", lights);
        Assert.DoesNotContain("## Strategy", lights);
    }

    /// <summary>A component that didn't exist when the tools were written: a stand-in for any future feature or script.</summary>
    [Category("Gameplay")]
    public sealed class Jetpack : Component
    {
        public float Fuel { get; set; } = 10;

        public Vector2 Thrust { get; set; } = new(0, 12);
    }

    [Fact]
    public void ANewComponent_IsUsableAtOnce_WithNoToolWrittenForIt()
    {
        _host.Components.Register<Jetpack>();

        var created = Run("entity_create", """{"entity": {"name": "Pilot", "components": {"Jetpack": {"fuel": 3, "thrust": [0, 20]}}}}""");
        var described = Run("component_describe", """{"component": "Jetpack"}""").Text;
        Run("entity_update", """{"entity": "Pilot", "changes": {"components": {"Jetpack": {"fuel": 1.5}}}}""");

        Assert.False(created.IsError, created.Text);
        Assert.Equal(1.5f, Named("Pilot").GetComponent<Jetpack>()!.Fuel);
        Assert.Equal(new Vector2(0, 20), Named("Pilot").GetComponent<Jetpack>()!.Thrust);
        Assert.Contains("- thrust: [x, y] = [0,12]", described);
        Assert.Contains("Jetpack", Run("components_list", """{"category": "Gameplay"}""").Text);
    }

    // An agent that knew a component or property by its old name still reaches it, and sees what was kept.
    [Fact]
    public void FormerNamesVersionsAndKeptValues_ReachAgents()
    {
        _host.Components.Register<Griff.Tests.Scenes.Lantern>();
        _host.Components.Register<Griff.Tests.Scenes.Timer>();

        var described = Run("component_describe", """{"component": "Lamp"}""").Text;
        var created = Run("entity_create", """{"entity": {"name": "Torch", "components": {"Lantern": {"range": 4}}}}""");

        Assert.StartsWith("# Lantern", described);
        Assert.Contains("Formerly saved as: ", described);
        Assert.Contains("(Formerly Range, Reach.)", described);
        Assert.Contains("Saved data version: 3", Run("component_describe", """{"component": "Timer"}""").Text);
        Assert.False(created.IsError, created.Text);
        Assert.Equal(4f, Named("Torch").GetComponent<Griff.Tests.Scenes.Lantern>()!.Radius);

        // A value it has no property for is shown as kept, and the output goes back in as it is.
        var data = System.Text.Json.JsonDocument.Parse("""{"type": "Griff.Tests.Scenes.Lantern", "radius": 2, "flicker": 0.5}""").RootElement;
        var lamp = new Entity("Lamp");
        lamp.AddComponent(new SceneSerializer(_host.Components).DeserializeComponent(data));
        _host.Scene!.Add(lamp);
        var shown = Run("entity_get", """{"entity": "Lamp"}""").Text;
        Assert.Contains("\"kept\": {", shown);
        Assert.Contains("\"flicker\": 0.5", shown);
        var again = Run("entity_create", $$"""{"entity": {{shown}}}""");
        Assert.False(again.IsError, again.Text);
    }

    [Fact]
    public void Locked_IsCreatedShownAndUpdated()
    {
        Run("entity_create", """{"entity": {"name": "Backdrop", "locked": true, "components": {"Sprite": {}}}}""");
        Run("entity_create", """{"entity": {"name": "Ground"}}""");
        var backdrop = Named("Backdrop");

        Assert.True(backdrop.IsLocked);
        Assert.Contains("Backdrop", Run("scene_tree", "{}").Text.Split('\n').Single(l => l.Contains("locked")));
        Assert.Contains("\"locked\": true", Run("entity_get", """{"entity": "Backdrop"}""").Text);

        var result = Run("entity_update", """{"updates": [{"entity": "Backdrop", "changes": {"locked": false}}, {"entity": "Ground", "changes": {"locked": true}}]}""");
        Assert.False(result.IsError, result.Text);
        Assert.False(backdrop.IsLocked);
        Assert.True(Named("Ground").IsLocked);

        _host.History.Undo();
        Assert.True(backdrop.IsLocked);
        Assert.False(Named("Ground").IsLocked);
    }

    [Fact]
    public void Hidden_IsCreatedShownAndUpdated()
    {
        Run("entity_create", """{"entity": {"name": "Pause Menu", "hidden": true, "components": {"Sprite": {}}}}""");
        Run("entity_create", """{"entity": {"name": "Dialogue"}}""");
        var menu = Named("Pause Menu");

        Assert.True(menu.IsHidden);
        Assert.Contains("Pause Menu", Run("scene_tree", "{}").Text.Split('\n').Single(l => l.Contains("hidden")));
        Assert.Contains("\"hidden\": true", Run("entity_get", """{"entity": "Pause Menu"}""").Text);

        var result = Run("entity_update", """{"updates": [{"entity": "Pause Menu", "changes": {"hidden": false}}, {"entity": "Dialogue", "changes": {"hidden": true}}]}""");
        Assert.False(result.IsError, result.Text);
        Assert.False(menu.IsHidden);
        Assert.True(Named("Dialogue").IsHidden);

        _host.History.Undo();
        Assert.True(menu.IsHidden);
        Assert.False(Named("Dialogue").IsHidden);
    }

    [Fact]
    public void PrefabCopies_ShowWhichPrefabTheyreCopiesOf()
    {
        Run("entity_create", """{"entity": {"prefab": "Coin", "name": "Gold"}}""");

        Assert.Equal("Prefabs/Coin.prefab", Named("Gold").Prefab);
        Assert.Contains(Run("scene_tree", "{}").Text.Split('\n'), l => l.Contains("Gold #") && l.Contains("copy of Coin"));
        Assert.Contains("\"prefab\": \"Prefabs/Coin.prefab\"", Run("entity_get", """{"entity": "Gold"}""").Text);
        Assert.DoesNotContain("overrides", Run("entity_get", """{"entity": "Gold"}""").Text);
    }

    [Fact]
    public void PrefabCopies_ListWhatTheyChangeFromThePrefab()
    {
        Run("entity_create", """{"entity": {"prefab": "Coin", "name": "Gold", "position": [3, 1]}}""");
        Assert.False(Run("entity_update", """{"entity": "Gold", "changes": {"components": {"Sprite": {"tint": "#FFA300"}, "Light": {"radius": 2}}}}""").IsError);

        var text = Run("entity_get", """{"entity": "Gold"}""").Text;
        Assert.Contains("\"overrides\"", text);
        Assert.Contains("\"Sprite.Tint\"", text);
        Assert.Contains("\"added Light\"", text);
        // Where it's placed is its own, not a change.
        Assert.DoesNotContain("Transform.Position", text);
    }

    [Fact]
    public void Get_SaysWhatAComponentNeedsThatItsEntityLacks()
    {
        var player = new Entity("Player");
        player.AddComponent<PlatformerMovement>();
        player.AddComponent<Body>();
        _host.Scene!.Add(player);

        var json = Run("entity_get", """{"entity": "Player"}""").Text;

        Assert.Contains("\"needs\": \"It needs a Collider, which Player doesn't have, and won't work until one is added.\"", json);
        // Its Body needs one too; nothing else is flagged.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(json, "\"needs\""));
    }

    [Fact]
    public void Update_RemovingWhatAnotherNeeds_SaysSo()
    {
        Run("entity_create", """{"entity": {"name": "Hero", "components": {"PlatformerMovement": {}, "Sprite": {}}}}""");

        var result = Run("entity_update", """{"entity": "Hero", "changes": {"remove": ["Body"]}}""");
        Assert.False(result.IsError, result.Text);
        Assert.Contains("Note: PlatformerMovement on Hero needs a Body, and won't work until it has one again.", result.Text);

        // Removing what needs it too, or putting another in its place, says nothing.
        Assert.DoesNotContain("Note:", Run("entity_update", """{"entity": "Hero", "changes": {"remove": ["Sprite"]}}""").Text);
        Assert.DoesNotContain("Note:", Run("entity_update", """{"entity": "Hero", "changes": {"remove": ["BoxCollider", "PlatformerMovement"]}}""").Text);
    }
}
