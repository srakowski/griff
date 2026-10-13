using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class FrameTests
{
    private readonly Scene _scene = new();

    private Game CreateGame() => new(_scene, new InputState());

    // The order a frame takes its steps in. Changing it changes how every game plays, so change this list on purpose.
    [Fact]
    public void Frame_TakesGriffsStepsInTheirOrder()
    {
        string[] expected =
        [
            "Begin: Panels", "Begin: Time", "Begin: Input", "Begin: Debug Drawing", "Begin: Network", "Begin: Dungeons",
            "Begin: Script Start",
            "Logic: Worlds", "Logic: Menus", "Logic: Lobby", "Logic: Switch Targets", "Logic: Turns", "Logic: Tactics",
            "Logic: Beat Clocks", "Logic: Move History", "Logic: Enemies", "Logic: Movement", "Logic: First Person",
            "Logic: Item Uses",
            "Scripts: Scripts",
            "Simulate: Sandbox HUD", "Simulate: Blocks", "Simulate: Tools", "Simulate: Other Players' Tools",
            "Simulate: Dragging", "Simulate: Match Boards", "Simulate: Falling Boards", "Simulate: Strategy",
            "Simulate: Attacks", "Simulate: Idle Grid Movers", "Simulate: Timers", "Simulate: Coroutines",
            "Simulate: Tweens", "Simulate: Lifetimes", "Simulate: Spawners", "Simulate: Shooters", "Simulate: Movers",
            "Simulate: Animations", "Simulate: Animated Tiles", "Simulate: Particles", "Simulate: Camera Shake",
            "Simulate: Sprite Effects", "Simulate: Prompts", "Simulate: Typewriters", "Simulate: Pickup Spin",
            "Physics: Thrusters", "Physics: Welds", "Physics: Physics", "Physics: Ropes", "Physics: Carrying",
            "Resolve: Pickups", "Resolve: Checkpoints", "Resolve: Doors", "Resolve: Dialogues", "Resolve: Switches",
            "Resolve: Goals", "Resolve: Wiring", "Resolve: Bouncers", "Resolve: Crumbles", "Resolve: Touch Damage",
            "Resolve: Health", "Resolve: Respawns", "Resolve: Bars", "Resolve: Counters", "Resolve: Fade",
            "Resolve: Screen Flash", "Resolve: Destroys", "Resolve: Scene Change",
            "Present: Rooms", "Present: Followers", "Present: Late Scripts", "Present: Screen Anchors", "Present: Trails",
            "Present: Lights", "Present: Weather", "Present: Prompts", "Present: Sound Sources", "Present: Sound Players",
            "Present: Patrol Walks",
            "End: Audio", "End: Settings", "End: Network",
        ];

        Assert.Equal(expected, CreateGame().Frame.Select(s => s.ToString()));
    }

    [Fact]
    public void Frame_PhasesRunInOrder()
    {
        var phases = CreateGame().Frame.Select(s => s.Phase).ToList();

        Assert.Equal(phases.Order(), phases);
        Assert.Equal(Enum.GetValues<FramePhase>(), phases.Distinct());
    }

    [Fact]
    public void GenreSteps_BelongToTheSystemsThatKeepTheirState()
    {
        var game = CreateGame();

        Assert.Equal(["Levels", "Puzzles", "Dragging", "Panels", "Strategy", "First Person", "Sandbox"], game.Systems.Select(s => s.Name));
        Assert.Equal("Strategy", game.Frame.Single(s => s.Name == "Strategy").System?.Name);
        Assert.Equal("Levels", game.Frame.Single(s => s.Name == "Doors").System?.Name);
        Assert.Null(game.Frame.Single(s => s.Name == "Physics").System);
    }

    [Fact]
    public void AddedSystem_RunsEveryFrame_AfterGriffsStepsInItsPhase()
    {
        var log = new List<string>();
        var entity = new Entity("Probe");
        entity.AddComponent<OrderProbe>().Log = log;
        _scene.Add(entity);
        var game = CreateGame();
        var system = game.AddSystem(new Recorder(FramePhase.Scripts, log));

        game.Tick(0.016);
        game.Tick(0.016);

        Assert.Equal(["Update", "Recorder", "LateUpdate", "Update", "Recorder", "LateUpdate"], log);
        Assert.Contains(system, game.Systems);
        var steps = game.Frame.Select(s => s.ToString()).ToList();
        Assert.Equal(steps.IndexOf("Scripts: Scripts") + 1, steps.IndexOf("Scripts: Recorder"));
    }

    [Fact]
    public void AddedSystems_RunInTheOrderTheyWereAdded()
    {
        var log = new List<string>();
        var game = CreateGame();
        game.AddSystem(new Recorder(FramePhase.Simulate, log));
        game.AddSystem(new OtherRecorder(log));

        game.Tick(0.016);

        Assert.Equal(["Recorder", "OtherRecorder"], log);
    }

    [Fact]
    public void AddingASystemOfTheSameType_ReplacesIt()
    {
        var log = new List<string>();
        var game = CreateGame();
        var first = game.AddSystem(new Recorder(FramePhase.Logic, log));
        var second = game.AddSystem(new Recorder(FramePhase.Logic, log));

        game.Tick(0.016);

        Assert.Equal(["Recorder"], log);
        Assert.DoesNotContain(first, game.Systems);
        Assert.Same(second, game.GetSystem<Recorder>());
    }

    [Fact]
    public void RemoveSystem_StopsIt_ButNotGriffsOwn()
    {
        var log = new List<string>();
        var game = CreateGame();
        var system = game.AddSystem(new Recorder(FramePhase.Logic, log));

        Assert.True(game.RemoveSystem(system));
        Assert.False(game.RemoveSystem(game.Systems[0]));
        game.Tick(0.016);

        Assert.Empty(log);
        Assert.Null(game.GetSystem<Recorder>());
        Assert.Equal(7, game.Systems.Count);
    }

    [Fact]
    public void ScriptsSystem_RunsFromTheFrameItsAdded_AndGoesWithItsEntity()
    {
        var log = new List<string>();
        var entity = new Entity("Level");
        entity.AddComponent<SystemAdder>().Log = log;
        _scene.Add(entity);
        var game = CreateGame();

        game.Tick(0.016);
        Assert.Equal(["Recorder"], log);

        game.Destroy(entity);
        game.Tick(0.016);
        game.Tick(0.016);

        Assert.Equal(["Recorder", "Recorder"], log);
        Assert.Null(game.GetSystem<Recorder>());
    }

    [Fact]
    public void SceneChange_SystemsForgetTheOldScene_AndScriptsOnesGoWithTheirScript()
    {
        var log = new List<string>();
        var level = new Entity("Level");
        level.AddComponent<SystemAdder>().Log = log;
        _scene.Add(level);
        var next = new Scene();
        var game = new Game(_scene, new InputState()) { SceneLoader = _ => next };
        var lasting = game.AddSystem(new OtherRecorder(log));

        game.Tick(0.016);
        game.LoadScene("Next");
        game.Tick(0.016);

        Assert.Null(game.GetSystem<Recorder>());
        Assert.Same(lasting, game.GetSystem<OtherRecorder>());
        Assert.Equal(1, lasting.ScenesChanged);
    }

    [Fact]
    public void SceneChange_GriffsSystemsForgetWhatTheyKept()
    {
        var next = new Scene();
        var game = new Game(_scene, new InputState()) { SceneLoader = _ => next };
        var strategy = game.Systems.Single(s => s.Name == "Strategy");
        var state = strategy.GetType().GetProperty("State")!.GetValue(strategy);

        game.LoadScene("Next");
        game.Tick(0.016);

        Assert.NotSame(state, strategy.GetType().GetProperty("State")!.GetValue(strategy));
    }

    [Fact]
    public void LateUpdate_SeesWhereThingsEndedUp_AndScreenAnchorsFollowItsCamera()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        var ball = new Entity("Ball");
        ball.AddComponent<Body>();
        ball.AddComponent<BoxCollider>();
        var watcher = ball.AddComponent<CameraOnBall>();
        var badge = new Entity("Badge");
        badge.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        _scene.Add(camera);
        _scene.Add(ball);
        _scene.Add(badge);
        var game = CreateGame();

        game.Tick(0.1);

        // Update saw the ball before it fell; LateUpdate after, and the badge is pinned to where the camera went.
        Assert.Equal(0, watcher.SeenInUpdate);
        Assert.True(watcher.SeenInLateUpdate < 0);
        Assert.Equal(ball.Transform.Position.Y, camera.Transform.Position.Y);
        Assert.Equal(camera.Transform.Position.Y, badge.Transform.Position.Y, 4);
    }

    [Fact]
    public void LateUpdate_DoesNotRunOnScriptsThatHaventStarted()
    {
        var game = CreateGame();
        game.Tick(0.016);
        var entity = new Entity();
        var probe = entity.AddComponent<OrderProbe>();
        probe.Log = [];
        game.Spawn(entity);

        game.Tick(0.016);
        Assert.Equal(["Update", "LateUpdate"], probe.Log);
    }

    public sealed class Recorder(FramePhase phase, List<string> log) : GameSystem
    {
        public override FramePhase Phase => phase;

        protected override void Update() => log.Add(Name);
    }

    public sealed class OtherRecorder(List<string> log) : GameSystem
    {
        public int ScenesChanged { get; private set; }

        public override FramePhase Phase => FramePhase.Simulate;

        protected override void Update() => log.Add(Name);

        protected override void SceneChanged() => ScenesChanged++;
    }

    public class OrderProbe : Behavior
    {
        [DoNotSerialize]
        public List<string>? Log { get; set; }

        protected override void Update() => Log?.Add("Update");

        protected override void LateUpdate() => Log?.Add("LateUpdate");
    }

    public class SystemAdder : Behavior
    {
        [DoNotSerialize]
        public List<string> Log { get; set; } = [];

        protected override void Start() => AddSystem(new Recorder(FramePhase.Simulate, Log));
    }

    public class CameraOnBall : Behavior
    {
        [DoNotSerialize]
        public float SeenInUpdate { get; private set; } = float.NaN;

        [DoNotSerialize]
        public float SeenInLateUpdate { get; private set; } = float.NaN;

        protected override void Update() => SeenInUpdate = Transform.Position.Y;

        protected override void LateUpdate()
        {
            SeenInLateUpdate = Transform.Position.Y;
            var camera = Game.Find("Camera")!.Transform;
            camera.Position = camera.Position with { Y = Transform.Position.Y };
        }
    }
}
