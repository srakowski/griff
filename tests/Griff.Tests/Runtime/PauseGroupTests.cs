using System.Numerics;
using Griff.Input;
using Griff.Modules.Match3;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Pausing part of the game while the rest runs on: pause groups, and panels and dialogues that pause a group.</summary>
public sealed class PauseGroupTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public PauseGroupTests()
    {
        _game = new Game(_scene, _input) { PrefabLoader = name => new Entity(name) };
        _game.Physics.Gravity = Vector2.Zero;
    }

    private sealed class Ticker : Behavior
    {
        public int Updates { get; private set; }

        public int Fixed { get; private set; }

        public int Late { get; private set; }

        protected override void Update() => Updates++;

        protected override void FixedUpdate() => Fixed++;

        protected override void LateUpdate() => Late++;
    }

    private void Run(int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private Entity Add(string name, Entity? parent = null)
    {
        var entity = new Entity(name);
        _scene.Add(entity, parent);
        return entity;
    }

    // A thing that moves by itself every way the game moves things: a body drifting, a mover, an animation and a script.
    private (Body Body, Mover? Mover, Animation Animation, Ticker? Script) Busy(string name, Entity? parent, float y = 0, bool script = true, bool mover = true)
    {
        var entity = Add(name, parent);
        entity.Transform.Position = new Vector3(0, y, 0);
        var drifter = Add(name + " Drifter", entity);
        var body = drifter.AddComponent<Body>();
        drifter.AddComponent<BoxCollider>();
        body.Velocity = new Vector2(2, 0);
        Mover? moves = null;
        if (mover)
        {
            moves = Add(name + " Platform", entity).AddComponent<Mover>();
            (moves.Points, moves.Speed, moves.Smooth) = ("10 0", 2, false);
        }

        entity.AddComponent<Sprite>();
        var animation = entity.AddComponent<Animation>();
        return (body, moves, animation, script ? entity.AddComponent<Ticker>() : null);
    }

    private static Vector3 At(Component component) => component.Entity!.Transform.Position;

    [Fact]
    public void A_paused_group_holds_still_while_the_rest_of_the_game_runs_on()
    {
        var arcade = Add("Arcade");
        var group = arcade.AddComponent<PauseGroup>();
        var inside = Busy("Inside", arcade);
        var outside = Busy("Outside", null, y: 10);
        var fired = 0;
        _game.After(0.5f, () => fired++, owner: inside.Body.Entity);
        Run(5);
        var (body, platform, time, updates) = (At(inside.Body), At(inside.Mover!), inside.Animation.Time, inside.Script!.Updates);
        Assert.True(updates > 0);

        group.Paused = true;
        Run(60);

        Assert.True(inside.Body.Entity!.IsPaused);
        Assert.False(outside.Body.Entity!.IsPaused);
        Assert.False(_game.IsPaused);
        Assert.Equal(body, At(inside.Body));
        Assert.Equal(new Vector2(2, 0), inside.Body.Velocity); // kept for when it goes on
        Assert.Equal(platform, At(inside.Mover!));
        Assert.Equal(time, inside.Animation.Time);
        Assert.InRange(inside.Script.Updates - updates, 0, 1);
        Assert.Equal(0, fired);
        Assert.True(At(outside.Body).X > 1.9f);
        Assert.True(At(outside.Mover!).X > 1.9f);
        Assert.True(outside.Animation.Time > 0.9f);
        Assert.True(outside.Script!.Updates > 60 && outside.Script.Late > 60 && outside.Script.Fixed > 60);

        // Resumed, it picks up where it was: the timer goes off half a second of running later, not at once.
        group.Resume();
        Run(10);
        Assert.Equal(0, fired);
        Assert.True(At(inside.Body).X > body.X);
        Run(30);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void A_paused_body_is_still_solid_to_what_runs_into_it()
    {
        var group = Add("Frozen Room");
        group.AddComponent<PauseGroup>().Paused = true;
        var wall = Add("Crate", group);
        wall.AddComponent<Body>();
        wall.AddComponent<BoxCollider>();
        var runner = Add("Runner");
        runner.Transform.Position = new Vector3(-3, 0, 0);
        runner.AddComponent<BoxCollider>();
        runner.AddComponent<Body>().Velocity = new Vector2(5, 0);

        Run(60);

        Assert.Equal(Vector3.Zero, wall.Transform.Position);
        Assert.InRange(runner.Transform.Position.X, -1.1f, -0.9f);
    }

    [Fact]
    public void A_paused_player_takes_no_input_and_a_paused_patrol_walks_nowhere()
    {
        var level = Add("Level");
        level.AddComponent<PauseGroup>().Paused = true;
        var player = Add("Player", level);
        player.AddComponent<BoxCollider>();
        player.AddComponent<Body>();
        player.AddComponent<TopDownMovement>();
        var guard = Add("Guard", level);
        guard.Transform.Position = new Vector3(0, 5, 0);
        guard.AddComponent<BoxCollider>();
        guard.AddComponent<Body>();
        guard.AddComponent<Patrol>();

        _input.SetKey(Key.Right, true);
        Run(30);

        Assert.Equal(Vector3.Zero, player.Transform.Position);
        Assert.Equal(new Vector3(0, 5, 0), guard.Transform.Position);
    }

    [Fact]
    public void Pause_Resume_and_Toggle_are_actions_a_trigger_does()
    {
        var arcade = Add("Arcade");
        var group = arcade.AddComponent<PauseGroup>();
        var button = Add("Button");
        AddTrigger(button, "Hold", "Pause", "Arcade");
        AddTrigger(button, "Go", "Resume", "Arcade");
        AddTrigger(button, "Flip", "Toggle", "Arcade");
        Assert.Equal(["Pause", "Resume", "Toggle"], ComponentActions.Of(typeof(PauseGroup)).Select(a => a.Name));
        Run();

        _game.Send("Hold");
        Run();
        Assert.True(group.Paused && arcade.IsPaused);
        _game.Send("Go");
        Run();
        Assert.False(group.Paused || arcade.IsPaused);
        _game.Send("Flip");
        Run();
        Assert.True(arcade.IsPaused);
    }

    private static void AddTrigger(Entity on, string message, string action, string targets)
    {
        var trigger = on.AddComponent<Trigger>();
        (trigger.Message, trigger.Do, trigger.Targets) = (message, action, targets);
    }

    [Fact]
    public void A_pause_group_says_when_it_pauses_and_resumes_and_a_switch_runs_it_while_on()
    {
        var arcade = Add("Arcade");
        var group = arcade.AddComponent<PauseGroup>();
        (group.PauseMessage, group.ResumeMessage) = ("ArcadePaused", "ArcadeResumed");
        var heard = new List<string>();
        _game.On("ArcadePaused", _ => heard.Add("paused"));
        _game.On("ArcadeResumed", _ => heard.Add("resumed"));
        Run();

        _game.Pause(arcade);
        Run();
        _game.Resume(arcade);
        Run();
        Assert.Equal(["paused", "resumed"], heard);

        // Powered, it runs; off, it's paused.
        Assert.Contains(ComponentActions.PoweredOf(typeof(PauseGroup)), p => p.Property.Name == nameof(PauseGroup.Paused) && p.Inverted);
        Game.Power(arcade, on: false);
        Run();
        Assert.True(arcade.IsPaused);
        Game.Power(arcade, on: true);
        Run();
        Assert.False(arcade.IsPaused);
    }

    [Fact]
    public void Pausing_from_a_script_gives_the_entity_a_pause_group()
    {
        var arcade = Add("Arcade");
        var child = Add("Cabinet", arcade);
        Run();

        _game.Pause(arcade);
        Run();

        Assert.NotNull(arcade.GetComponent<PauseGroup>());
        Assert.True(child.IsPaused);
        Assert.Contains(arcade, _scene.PausedGroups);
    }

    [Fact]
    public void A_panel_can_pause_just_a_group_while_its_open()
    {
        var arcade = Add("Arcade");
        var inside = Busy("Inside", arcade);
        var outside = Busy("Outside", null, y: 10);
        var menu = Add("Menu");
        var panel = menu.AddComponent<Panel>();
        panel.Pauses = "Arcade";
        Run();

        _game.OpenPanel(panel);
        var held = At(inside.Body);
        Run(30);

        Assert.False(_game.IsPaused);
        Assert.True(arcade.IsPaused);
        Assert.Equal(held, At(inside.Body));
        Assert.True(At(outside.Body).X > 0.9f);

        _game.ClosePanel(panel);
        Assert.False(arcade.IsPaused);
        Run(5);
        Assert.True(At(inside.Body).X > held.X);
    }

    [Fact]
    public void A_panel_that_pauses_the_whole_game_still_does()
    {
        var thing = Busy("Thing", null);
        var panel = Add("Pause Menu").AddComponent<Panel>();
        Run();

        _game.OpenPanel(panel);
        var held = At(thing.Body);
        Run(10);

        Assert.True(_game.IsPaused);
        Assert.Empty(_scene.PausedGroups);
        Assert.Equal(held, At(thing.Body));
    }

    [Fact]
    public void A_dialogue_can_pause_just_a_group_while_its_said()
    {
        var enemies = Add("Enemies");
        var inside = Busy("Inside", enemies);
        var outside = Busy("Outside", null, y: 10);
        var king = Add("King");
        var dialogue = king.AddComponent<Dialogue>();
        dialogue.Lines = ["Hello.", "Goodbye."];
        dialogue.Pauses = "Enemies";
        Run();

        _game.StartDialogue(dialogue);
        var held = At(inside.Body);
        var free = At(outside.Body);
        Run(10);

        Assert.Equal(1, _game.Time.Scale);
        Assert.Equal(held, At(inside.Body));
        Assert.True(At(outside.Body).X > free.X);

        _game.EndDialogue(finished: true);
        Assert.False(enemies.IsPaused);
    }

    [Fact]
    public void A_paused_board_stands_still()
    {
        var arcade = Add("Arcade");
        var board = Add("Board", arcade);
        board.AddComponent<Tilemap>();
        var match = board.AddComponent<MatchBoard>();
        Run(2);
        var moves = match.MovedIn;

        arcade.AddComponent<PauseGroup>().Paused = true;
        Run(5);

        Assert.Equal(moves, match.MovedIn);
    }

    [Fact]
    public void Rewinding_to_a_moment_a_group_was_paused_pauses_it_again()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var arcade = Add("Arcade");
        var inside = Busy("Inside", arcade, script: false);
        var menu = Add("Menu");
        var panel = menu.AddComponent<Panel>();
        panel.Pauses = "Arcade";
        var timeline = new Timeline(serializer, capacity: 10, framesPerSnapshot: 1);
        Run();
        _game.OpenPanel(panel);
        Run(3);
        timeline.Record(_scene, _game.Time.Total, force: true);
        _game.ClosePanel(panel);
        Run(10);

        var restored = timeline.Restore(timeline.Count - 1);
        var again = new Game(restored, new InputState());
        again.Physics.Gravity = Vector2.Zero;
        var copy = restored.Find(inside.Body.Entity!.Id)!;
        var held = copy.Transform.Position;
        for (var i = 0; i < 10; i++)
            again.Tick(Frame);

        Assert.True(copy.IsPaused);
        Assert.Equal(held, copy.Transform.Position);
    }

    [Fact]
    public void Running_lists_leave_out_what_is_paused()
    {
        var arcade = Add("Arcade");
        var inside = Add("Inside", arcade).AddComponent<Mover>();
        var outside = Add("Outside").AddComponent<Mover>();
        Assert.Same(_scene.Active<Mover>().Items, _scene.Running<Mover>().Items);

        _game.Pause(arcade);
        Run();

        Assert.Equal([outside], _scene.Running<Mover>());
        Assert.Equal([inside, outside], _scene.Active<Mover>());
        Assert.Same(_scene.Running<Mover>().Items, _scene.Running<Mover>().Items);
    }

    [Fact]
    public void A_frame_with_a_group_paused_allocates_nothing()
    {
        var arcade = Add("Arcade");
        for (var i = 0; i < 50; i++)
        {
            // A mover makes garbage of its own, paused or not.
            Busy("In " + i, arcade, y: i * 3, mover: false);
            Busy("Out " + i, null, y: -3 - i * 3, mover: false);
        }

        var group = arcade.AddComponent<PauseGroup>();
        Run(10);
        Assert.Equal(0, Allocations.PerCall(() => _game.Tick(Frame), 10));
        group.Paused = true;
        Run(10);

        var perFrame = Allocations.PerCall(() => _game.Tick(Frame), 10);

        Assert.True(perFrame == 0, $"A frame allocated {perFrame:N0} bytes.");
    }
}
