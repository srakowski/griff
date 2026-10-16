using System.Numerics;
using Griff.Audio;
using Griff.Data;
using Griff.Hosting;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Hosting;

/// <summary>
/// The one game loop every host runs (the desktop's window, the browser's page, a dedicated server): a frame takes the
/// steps its time holds, builds the picture and draws it on the host's view, pumps the host's sound, takes a save's
/// picture, and an error that ends a frame pauses the game. And the one factory every host makes its games with
/// (<see cref="LoadedGame.NewGame"/>), which hands a game what its host gives it.
/// </summary>
public sealed class GameLoopTests
{
    private sealed class View : IGameView
    {
        public int Rendered, Captured;

        public float Aspect => 2;

        public void Render(RenderFrame frame) => Rendered++;

        public (byte[] Pixels, int Width, int Height)? Capture(RenderFrame frame)
        {
            Captured++;
            return (new byte[4 * 4 * 4], 4, 4);
        }
    }

    private sealed class Sound : IFrameSound
    {
        public int Pumped;

        public void Pump() => Pumped++;
    }

    private sealed class Counter : Behavior
    {
        public int Updates { get; set; }

        public bool Throws { get; set; }

        protected override void Update()
        {
            Updates++;
            if (Throws)
                throw new InvalidOperationException("Broken on purpose.");
        }
    }

    private static (Game Game, Counter Counter) NewGame()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        scene.Add(camera);
        var thing = new Entity("Thing");
        var counter = thing.AddComponent<Counter>();
        scene.Add(thing);
        var components = ComponentRegistry.CreateDefault();
        components.Register<Counter>();
        return (new LoadedGame(null, components, scene, new ProblemLog()).NewGame(), counter);
    }

    [Fact]
    public void A_frame_takes_its_steps_draws_them_and_pumps_the_sound()
    {
        var (game, counter) = NewGame();
        var (view, sound) = (new View(), new Sound());
        var loop = new GameLoop(game, view, sound);
        var (ticks, ends) = (0, 0);
        loop.Ticked += (_, _) => ticks++;
        loop.FrameEnded += (_, _) => ends++;
        loop.Start();

        loop.Frame(3.0 / game.Time.StepRate);

        Assert.Equal(3, counter.Updates);
        Assert.Equal(2, game.ViewAspect);
        Assert.Equal((1, 1, 1, 1), (view.Rendered, sound.Pumped, ticks, ends));
        Assert.NotNull(loop.LastFrame);
        var times = loop.ReadTimes();
        Assert.Equal((3L, 1L), (times.Steps, times.Frames));
        Assert.Equal(default, loop.ReadTimes());
    }

    [Fact]
    public void Paused_it_takes_no_steps_and_is_still_drawn()
    {
        var (game, counter) = NewGame();
        var view = new View();
        var loop = new GameLoop(game, view) { IsPaused = true };
        var ends = 0;
        loop.FrameEnded += (_, _) => ends++;
        loop.Start();

        loop.Frame(1);
        Assert.Equal((0, 1, 0), (counter.Updates, view.Rendered, ends));

        // Stepped through while paused, it takes exactly one.
        loop.Step();
        Assert.Equal((1, 2, 1), (counter.Updates, view.Rendered, ends));
    }

    [Fact]
    public void An_error_ends_the_frame_and_pauses_the_game()
    {
        var (game, counter) = NewGame();
        var loop = new GameLoop(game, new View());
        Exception? faulted = null;
        loop.Faulted += (_, ex) => faulted = ex;
        loop.Start();
        counter.Throws = true;

        loop.Frame(5.0 / game.Time.StepRate);

        Assert.Equal("Broken on purpose.", faulted?.Message);
        Assert.True(loop.IsPaused);
        Assert.Equal(1, counter.Updates);
        Assert.Null(GameLoop.Running);
    }

    [Fact]
    public void A_saved_game_gets_its_picture_from_the_view_or_none_without_one()
    {
        var (game, _) = NewGame();
        var view = new View();
        var loop = new GameLoop(game, view);
        loop.Start();
        Assert.True(game.SaveGame("one"));
        Assert.True(game.PictureWanted);
        loop.Frame(0);
        Assert.Equal(1, view.Captured);
        Assert.False(game.PictureWanted);

        // A dedicated server draws nothing: its saves have no picture, and still save.
        var (server, _) = NewGame();
        var headless = new GameLoop(server);
        headless.Start();
        Assert.True(server.SaveGame("one"));
        headless.Frame(1.0 / server.Time.StepRate);
        Assert.False(server.PictureWanted);
        Assert.Null(headless.LastFrame);
    }

    [Fact]
    public void The_mouse_is_put_in_the_world_through_the_last_frame()
    {
        var (game, _) = NewGame();
        var loop = new GameLoop(game, new View());
        loop.Start();
        loop.Frame(0);
        loop.Input.IsMouseInView = false;

        loop.PointAt(new Vector2(1, 0));
        loop.Frame(1.0 / game.Time.StepRate);
        Assert.True(loop.Input.IsMouseInView);
        // Half the view's width from the middle, with the camera at the origin.
        Assert.True(loop.Input.MouseWorld.X > 0);
        Assert.Equal(0, loop.Input.MouseWorld.Y, 3);

        // An extension the host added (as the editor adds an agent's pointer) sets what the game reads after the real mouse,
        // until the real one moves.
        var pointer = new Pointer { At = new Vector2(-3, 2) };
        loop.Add(pointer);
        loop.Frame(1.0 / game.Time.StepRate);
        Assert.Equal(new Vector2(-3, 2), loop.Input.MouseWorld);
        loop.PointAt(new Vector2(0.5f, 0));
        loop.Frame(1.0 / game.Time.StepRate);
        Assert.Null(pointer.At);
        Assert.True(loop.Input.MouseWorld.X > 0);
    }

    [Theory]
    [InlineData(FaultHold.Resumed)]
    [InlineData(FaultHold.Abandoned)]
    public void An_extension_can_hold_a_game_where_it_threw(FaultHold hold)
    {
        var (game, counter) = NewGame();
        var loop = new GameLoop(game, new View());
        var holder = new Holder(hold);
        loop.Add(holder);
        Exception? faulted = null;
        loop.Faulted += (_, ex) => faulted = ex;
        loop.Start();
        counter.Throws = true;

        loop.Frame(1.0 / game.Time.StepRate);

        // Asked before anything unwound; let go, the game plays on and the error is said; gone, nothing is.
        Assert.Equal("Broken on purpose.", holder.Asked?.Message);
        Assert.False(loop.IsPaused);
        Assert.Equal(hold == FaultHold.Resumed ? "Broken on purpose." : null, faulted?.Message);
    }

    // Points the mouse, as the editor's playtest pointer does.
    private sealed class Pointer : IGameLoopExtension
    {
        public Vector2? At { get; set; }

        public void BeforeSteps(GameLoop loop)
        {
            if (At is { } at)
                (loop.Input.MouseWorld, loop.Input.IsMouseInView) = (at, true);
        }

        public void PointerMoved(GameLoop loop) => At = null;
    }

    // Holds a game where it threw, as the editor's debugger does, and says what became of it.
    private sealed class Holder(FaultHold hold) : IGameLoopExtension
    {
        public Exception? Asked { get; private set; }

        public FaultHold Fault(GameLoop loop, Exception exception)
        {
            Asked = exception;
            return hold;
        }
    }

    [Fact]
    public void A_server_runs_with_nobody_watching_until_stopped()
    {
        var (game, counter) = NewGame();
        var loop = new GameLoop(game);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        loop.Run(TimeSpan.FromMilliseconds(5), stop.Token);

        Assert.True(counter.Updates > 0);
        Assert.True(loop.IsRunning);
        loop.Stop();
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public void A_host_hands_the_game_it_makes_its_input_sound_saves_and_tables()
    {
        var input = new InputState();
        var audio = new MixerAudio(new Mixer(), new AssetSoundLibrary(new AssetFolder(Path.GetTempPath())));
        var saves = new SaveData();
        var tables = new TableSet();
        var problems = new List<Problem>();
        var content = new LoadedGame(null, ComponentRegistry.CreateDefault(), new Scene(), new ProblemLog()) { SceneName = "Start", StepRate = 30 };

        var game = content.NewGame(new GameHostOptions
        {
            Input = input, Audio = audio, SaveData = saves, Tables = tables, Problems = problems.Add,
        });

        Assert.Same(input, game.Input);
        Assert.Same(audio, game.Audio);
        Assert.Same(saves, game.SaveData);
        Assert.Same(tables, game.Tables);
        Assert.Equal(("Start", 30), (game.SceneName, game.Time.StepRate));
        game.Problems.Report("Something's off.");
        Assert.Equal("Something's off.", Assert.Single(problems).Message);

        // Left out, each is the game's own; and with nowhere to load scenes or prefabs from, it has no loaders.
        var plain = content.NewGame();
        Assert.False(plain.Network.OverNetwork);
        Assert.NotSame(input, plain.Input);
        Assert.Null(plain.SceneLoader);
        Assert.Null(plain.PrefabLoader);
    }

    [Fact]
    public void A_game_without_a_project_loads_scenes_and_prefabs_as_its_content_says()
    {
        var content = new LoadedGame(null, ComponentRegistry.CreateDefault(), new Scene(), new ProblemLog())
        {
            Scenes = name =>
            {
                var scene = new Scene();
                scene.Add(new Entity(name));
                return scene;
            },
            Prefabs = name => new Entity(name),
        };

        var game = content.NewGame();

        Assert.Equal("Arena", game.SceneLoader!("Arena").Entities[0].Name);
        Assert.Equal("Bullet", game.PrefabLoader!("Bullet").Name);
    }
}
