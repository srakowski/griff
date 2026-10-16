using System.Numerics;
using Griff.Input;
using Griff.Modules.PhysicsGames;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Griff.Tests.Runtime;

namespace Griff.Tests.Projects;

/// <summary>
/// The Pinball template, Midnight Midway: a title screen with the best score, and a table played with three balls, the
/// keys, a controller, the mouse or a finger; its top lanes raising the multiplier, drop targets earning an extra ball once
/// a game, a ball save, a tilt, game over with the best kept, a pause menu that keeps its keys to itself; the same to the
/// bit every time, across a rewind, with no garbage, and exported for the desktop and the web.
/// </summary>
public sealed class PinballTemplateTests : IDisposable
{
    private const float Frame = 1 / 60f;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-pinball-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<Project> CreateProject() => await Project.CreateAsync(_root, "Midway", ProjectTemplates.Get("Pinball"));

    private Game Start(Project project, string scene, SaveData? saves = null)
    {
        Scene Load(string name) => GameLoader.LoadScene(project, _registry, name);
        var game = new Game(Load(scene), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = Load,
            SceneName = scene,
            Components = _registry,
            ViewAspect = 16 / 9f,
            SaveData = saves ?? new SaveData(),
        };
        game.UseProjectSettings(project);
        return game;
    }

    private void Run(Game game, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            Step(game);
    }

    private void Step(Game game)
    {
        game.Tick(Frame);
        _input.EndFrame();
    }

    private void Tap(Game game, Key key)
    {
        _input.SetKey(key, true);
        Step(game);
        _input.SetKey(key, false);
        Run(game, 0.1f);
    }

    private static T Of<T>(Game game, string name) where T : Component => game.Find(name)!.GetComponent<T>()!;

    private static Drain DrainOf(Game game) => Of<Drain>(game, "Drain");

    private static Plunger PlungerOf(Game game) => Of<Plunger>(game, "Plunger");

    private static Entity BallOf(Game game) => game.Find("Ball")!;

    private static int ScoreOf(Game game) => Of<Counter>(game, "Score").Value;

    private static int MultiplierOf(Game game) => Of<Counter>(game, "Multiplier").Value;

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // Holds the plunger's button for `seconds` and lets go.
    private void Plunge(Game game, float seconds = 1)
    {
        for (var i = 0; i < 60 * 5 && !PlungerOf(game).Loaded; i++)
            Step(game);
        _input.SetKey(Key.Space, true);
        Run(game, seconds);
        _input.SetKey(Key.Space, false);
        Step(game);
    }

    // Drops the ball into the drain: the next is served (or it's saved, or the game's over).
    private void Drain(Game game)
    {
        var drain = DrainOf(game);
        for (var i = 0; i < 60 * 5 && !string.IsNullOrEmpty(drain.Serving); i++)
            Step(game);
        var ball = BallOf(game);
        ball.Transform.Position = new Vector3(Middle(game), -9.6f, ball.Transform.Position.Z);
        ball.GetComponent<Body>()!.Velocity = new Vector2(0, -2);
        Run(game, 0.2f);
    }

    private static float Middle(Game game) => (At(game.Find("Left Flipper")!).X + At(game.Find("Right Flipper")!).X) / 2;

    // Lights every lane of the top set (as a ball rolling through each would) and waits for them to go out again.
    private void LightTheLanes(Game game)
    {
        foreach (var letter in "MID")
            Of<Lane>(game, $"Lane {letter}").Light();
        Run(game, 1.2f);
    }

    private void KnockDownTheTargets(Game game)
    {
        foreach (var target in game.Find("Drop Targets")!.Children)
            target.GetComponent<Target>()!.Light();
        Run(game, 1.7f);
    }

    // ----- The title screen

    [Fact]
    public async Task The_title_shows_the_best_and_Enter_plays_the_table()
    {
        var project = await CreateProject();
        var saves = new SaveData();
        var game = Start(project, Project.DefaultSceneName, saves);
        Run(game, 0.2f);
        Assert.Equal("No best yet", Of<Text>(game, "Best").Value);

        saves.Set(PinballTemplate.BestKey, 123456);
        var again = Start(project, Project.DefaultSceneName, saves);
        Run(again, 0.2f);
        Assert.Equal("Best: 123,456", Of<Text>(again, "Best").Value);
        Tap(again, Key.Enter);
        Run(again, 0.6f);
        Assert.Equal(PinballTemplate.TableScene, again.SceneName);
        Run(again, 0.3f);
        Assert.True(PlungerOf(again).Loaded);
        Assert.Equal("Ball 1 of 3", Of<Text>(again, "Balls").Shown);
        Assert.Equal("Best 123,456", Of<Text>(again, "Best").Shown);
    }

    // ----- Scoring

    [Fact]
    public async Task The_top_lanes_raise_the_multiplier_to_five_times_and_a_lost_ball_puts_it_back()
    {
        var project = await CreateProject();
        var game = Start(project, PinballTemplate.TableScene);
        Run(game, 0.3f);
        var lights = Of<Stars>(game, "Multiplier Lights");
        Assert.Equal((1, 1), (MultiplierOf(game), lights.Shown));

        // Each time all three are lit, the multiplier goes up one, and the set's bonus is scored at it.
        var expected = 0;
        for (var times = 1; times <= 5; times++)
        {
            LightTheLanes(game);
            var multiplier = Math.Min(1 + times, PinballTemplate.MaxMultiplier);
            expected += 1000 * multiplier;
            Assert.Equal(multiplier, MultiplierOf(game));
            Assert.Equal(expected, ScoreOf(game));
        }

        Assert.Equal(5, lights.Shown);
        Assert.Equal("Multiplier 5x", Of<Text>(game, "Multiplier").Value);

        // A bumper's 100 is 500 now, and so is everything else.
        game.Send("Score", 100);
        Assert.Equal(expected + 500, ScoreOf(game));

        // The ball lost (no ball save: it was never shot), the multiplier goes back to 1x.
        Drain(game);
        Assert.Equal(2, DrainOf(game).Ball);
        Assert.Equal(1, MultiplierOf(game));
        Run(game, 0.2f);
        Assert.Equal(1, lights.Shown);
    }

    [Fact]
    public async Task The_drop_targets_knocked_down_twice_give_an_extra_ball_once_a_game()
    {
        var project = await CreateProject();
        var game = Start(project, PinballTemplate.TableScene);
        Run(game, 0.3f);
        var drain = DrainOf(game);

        KnockDownTheTargets(game);
        Assert.Equal(250 * 0 + 1000, ScoreOf(game));
        Assert.Equal(3, drain.Balls);
        Assert.DoesNotContain(game.Scene.Entities, e => e.Name == "Extra Ball");

        KnockDownTheTargets(game);
        Assert.Equal(4, drain.Balls);
        Assert.Equal("Ball 1 of 4", Of<Text>(game, "Balls").Shown);
        var callout = Assert.Single(game.Scene.Entities, e => e.Name == "Extra Ball");
        Assert.Equal("EXTRA BALL!", callout.GetComponent<Text>()!.Value);

        // Not again this game.
        KnockDownTheTargets(game);
        KnockDownTheTargets(game);
        Assert.Equal(4, drain.Balls);
        Assert.Equal(4000, ScoreOf(game));
    }

    [Fact]
    public async Task A_ball_lost_just_after_its_shot_is_saved_with_the_shoot_again_light_and_a_callout()
    {
        var project = await CreateProject();
        var game = Start(project, PinballTemplate.TableScene);
        Run(game, 0.3f);
        var lamp = Of<Stars>(game, "Shoot Again");
        Assert.Equal(0, lamp.Shown);

        Plunge(game);
        Run(game, 0.2f);
        Assert.Equal(1, lamp.Shown);
        Assert.True(At(BallOf(game)).Y > -4, $"The ball's at {At(BallOf(game))}.");
        Drain(game);
        Assert.Equal(1, DrainOf(game).Ball);
        Assert.Contains(game.Scene.Entities, e => e.Name == "Ball Saved");
        Assert.Equal(0, lamp.Shown);

        // Served again and shot by itself, with no ball save this time: lost again, it's lost.
        Run(game, 2);
        Assert.Equal(2, PlungerOf(game).Launches);
        Assert.Equal(0, lamp.Shown);
        Assert.False(DrainOf(game).Saving);
        Drain(game);
        Assert.Equal(2, DrainOf(game).Ball);

        // The next ball has its own.
        Plunge(game);
        Run(game, 0.2f);
        Assert.Equal(1, lamp.Shown);
    }

    [Fact]
    public async Task The_last_ball_lost_opens_game_over_with_the_score_and_best_kept_for_next_time()
    {
        var project = await CreateProject();
        var path = Path.Combine(_root, "save.json");
        var game = Start(project, PinballTemplate.TableScene, new SaveData(path));
        Run(game, 0.3f);
        game.Send("Score", 5000);
        for (var ball = 0; ball < PinballTemplate.Balls; ball++)
            Drain(game);
        Assert.True(DrainOf(game).GameOver);
        Run(game, 0.2f);
        Assert.Equal("Game Over", game.TopPanel?.Entity!.Name);
        Assert.Equal("5,000", Of<Text>(game, "Final Score").Shown);
        Assert.Equal("Best 5,000", Of<Text>(game, "Best Score").Shown);
        Assert.Equal("New best!", Of<Text>(game, "New Best").Shown);
        Assert.Equal(5000, game.SaveData.GetInt(PinballTemplate.BestKey));

        // Play Again (Enter): a worse game keeps the best, and isn't a new one.
        Tap(game, Key.Enter);
        Run(game, 0.6f);
        Assert.Null(game.TopPanel);
        Assert.Equal((0, 1), (ScoreOf(game), DrainOf(game).Ball));
        game.Send("Score", 3000);
        for (var ball = 0; ball < PinballTemplate.Balls; ball++)
            Drain(game);
        Run(game, 0.2f);
        Assert.Equal("Game Over", game.TopPanel?.Entity!.Name);
        Assert.Equal("", Of<Text>(game, "New Best").Shown);
        Assert.Equal("Best 5,000", Of<Text>(game, "Best Score").Shown);

        // A better one: passing the best says so as it happens, with a callout.
        Tap(game, Key.Enter);
        Run(game, 0.6f);
        var heard = new List<object?>();
        game.On(PinballTemplate.NewBest, heard.Add);
        game.Send("Score", 4000);
        Run(game, 0.1f);
        Assert.Empty(heard);
        game.Send("Score", 4000);
        Run(game, 0.1f);
        Assert.Equal([8000], heard);
        Assert.Contains(game.Scene.Entities, e => e.Name == "New Best" && e.GetComponent<Lifetime>() is not null);

        // Title (Esc) on game over; another run reads the best from the file.
        for (var ball = 0; ball < PinballTemplate.Balls; ball++)
            Drain(game);
        Run(game, 0.2f);
        Tap(game, Key.Escape);
        Run(game, 0.6f);
        Assert.Equal("Main", game.SceneName);
        var next = Start(project, Project.DefaultSceneName, new SaveData(path));
        Run(next, 0.2f);
        Assert.Equal("Best: 8,000", Of<Text>(next, "Best").Value);
    }

    // ----- Tilt, pause, and every way to play

    [Fact]
    public async Task Nudging_too_often_tilts_the_table_dead_until_the_ball_drains()
    {
        var project = await CreateProject();
        var game = Start(project, PinballTemplate.TableScene);
        Run(game, 0.3f);
        var tilt = Of<Tilt>(game, "Table");
        for (var i = 0; i < 4; i++)
            Tap(game, Key.N);
        Assert.True(tilt.Tilted);

        // The flippers stay down, and a bumper knocks nothing and scores nothing.
        _input.SetKey(Key.LeftShift, true);
        Run(game, 0.2f);
        Assert.False(Of<Flipper>(game, "Left Flipper").Up);
        _input.SetKey(Key.LeftShift, false);
        var bumper = game.Find("Bumpers")!.Children[0];
        var ball = BallOf(game);
        ball.Transform.Position = new Vector3(At(bumper) + new Vector2(0, 0.95f), 0);
        ball.GetComponent<Body>()!.Velocity = new Vector2(0, -3);
        Run(game, 0.3f);
        Assert.Equal(0, bumper.GetComponent<Bumper>()!.Hits);
        Assert.Equal(0, ScoreOf(game));

        // Draining puts it right.
        Drain(game);
        Assert.False(tilt.Tilted);
        _input.SetKey(Key.LeftShift, true);
        Run(game, 0.2f);
        Assert.True(Of<Flipper>(game, "Left Flipper").Up);
        _input.SetKey(Key.LeftShift, false);
    }

    [Fact]
    public async Task The_pause_menu_keeps_its_keys_from_the_plunger_and_the_nudge()
    {
        var project = await CreateProject();
        var game = Start(project, PinballTemplate.TableScene);
        Run(game, 0.5f);
        var plunger = PlungerOf(game);
        var tilt = Of<Tilt>(game, "Table");

        // Up and down move about the menu; nothing pulls or nudges underneath.
        Tap(game, Key.Escape);
        Assert.Equal("Resume", game.Focused!.Entity!.Name);
        Tap(game, Key.Down);
        Tap(game, Key.Up);
        Assert.Equal((0, 0, 0), (plunger.Pull, tilt.Nudges, plunger.Launches));
        Assert.Equal("Resume", game.Focused!.Entity!.Name);

        // Space clicks Resume, and held on, it doesn't pull the plunger; let go, nothing's shot.
        _input.SetKey(Key.Space, true);
        Run(game, 0.5f);
        Assert.False(game.IsPaused);
        Assert.Equal(0, plunger.Pull);
        _input.SetKey(Key.Space, false);
        Run(game, 0.3f);
        Assert.Equal(0, plunger.Launches);
        Assert.True(plunger.Loaded);

        // A controller: Start pauses, B goes back out without nudging, A on Resume doesn't pull the plunger either.
        _input.SetGamepad([GamepadButton.Start], Vector2.Zero);
        Step(game);
        _input.SetGamepad([], Vector2.Zero);
        Run(game, 0.1f);
        Assert.True(game.IsPaused);
        _input.SetGamepad([GamepadButton.B], Vector2.Zero);
        Step(game);
        _input.SetGamepad([], Vector2.Zero);
        Run(game, 0.2f);
        Assert.False(game.IsPaused);
        Assert.Equal(0, tilt.Nudges);
        _input.SetGamepad([GamepadButton.Start], Vector2.Zero);
        Step(game);
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Run(game, 0.5f);
        Assert.False(game.IsPaused);
        Assert.Equal(0, plunger.Pull);
        _input.SetGamepad([], Vector2.Zero);
        Run(game, 0.3f);
        Assert.Equal(0, plunger.Launches);

        // Pressed afresh, it pulls.
        Plunge(game);
        Assert.Equal(1, plunger.Launches);
    }

    [Fact]
    public async Task The_keys_a_controller_the_mouse_and_a_finger_each_shoot_and_flip()
    {
        var project = await CreateProject();
        var game = Start(project, PinballTemplate.TableScene);
        Run(game, 0.3f);
        var plunger = PlungerOf(game);
        var left = Of<Flipper>(game, "Left Flipper");
        var right = Of<Flipper>(game, "Right Flipper");

        void Shot(string how, Action pull, Action letGo)
        {
            for (var i = 0; i < 60 * 15 && !plunger.Loaded; i++)
                Step(game);
            Assert.True(plunger.Loaded, $"{how}: no ball on the plunger.");
            var launches = plunger.Launches;
            pull();
            Run(game, 0.8f);
            letGo();
            Run(game, 0.2f);
            Assert.True(plunger.Launches == launches + 1 && At(BallOf(game)).Y > -5, $"{how} didn't shoot the ball: it's at {At(BallOf(game))}.");

            // Back onto the plunger for the next, the ball save giving it back (or the next ball served).
            Drain(game);
        }

        void Flips(string how, Action press, Action release, Flipper flipper)
        {
            press();
            Run(game, 0.1f);
            Assert.True(flipper.Up, $"{how} didn't flip the {flipper.Side} flipper.");
            release();
            Run(game, 0.2f);
            Assert.False(flipper.Up);
        }

        // The keys.
        Shot("Space", () => _input.SetKey(Key.Space, true), () => _input.SetKey(Key.Space, false));
        Shot("The down arrow", () => _input.SetKey(Key.Down, true), () => _input.SetKey(Key.Down, false));
        Flips("Left Shift", () => _input.SetKey(Key.LeftShift, true), () => _input.SetKey(Key.LeftShift, false), left);
        Flips("Z", () => _input.SetKey(Key.Z, true), () => _input.SetKey(Key.Z, false), left);
        Flips("Right Shift", () => _input.SetKey(Key.RightShift, true), () => _input.SetKey(Key.RightShift, false), right);
        Flips("/", () => _input.SetKey(Key.Slash, true), () => _input.SetKey(Key.Slash, false), right);

        // A controller: the right stick pulled down and let spring back, A held; the shoulders and triggers flip.
        Shot("The right stick", () => _input.SetGamepad([], Vector2.Zero, new Vector2(0, -1)), () => _input.SetGamepad([], Vector2.Zero, Vector2.Zero));
        Shot("A", () => _input.SetGamepad([GamepadButton.A], Vector2.Zero), () => _input.SetGamepad([], Vector2.Zero));
        Flips("The left shoulder", () => _input.SetGamepad([GamepadButton.LeftShoulder], Vector2.Zero), () => _input.SetGamepad([], Vector2.Zero), left);
        Flips("The right trigger", () => _input.SetGamepad([], Vector2.Zero, Vector2.Zero, 0, 1), () => _input.SetGamepad([], Vector2.Zero), right);

        // The mouse (a finger is the mouse): held on the plunger pulls it; held either side of the table flips that side.
        _input.IsMouseInView = true;
        Shot("The mouse on the plunger", () =>
        {
            _input.MouseWorld = plunger.Tip - new Vector2(0, 0.2f);
            _input.SetMouseButton(MouseButton.Left, true);
        }, () => _input.SetMouseButton(MouseButton.Left, false));
        Flips("A click on the left", () =>
        {
            _input.MouseWorld = new Vector2(-3, -2);
            _input.SetMouseButton(MouseButton.Left, true);
        }, () => _input.SetMouseButton(MouseButton.Left, false), left);
        Flips("A click on the right", () =>
        {
            _input.MouseWorld = new Vector2(2, 3);
            _input.SetMouseButton(MouseButton.Left, true);
        }, () => _input.SetMouseButton(MouseButton.Left, false), right);
        _input.SetMouseButton(MouseButton.Left, false);
        Assert.Null(game.TopPanel);

        // Fingers on a touch screen, on a new table: one each side holds both flippers up, and one on the plunger pulls it
        // meanwhile.
        game = Start(project, PinballTemplate.TableScene);
        Run(game, 0.3f);
        (plunger, left, right) = (PlungerOf(game), Of<Flipper>(game, "Left Flipper"), Of<Flipper>(game, "Right Flipper"));
        _input.SetTouch(1, new Vector2(-3, -2), down: true);
        _input.SetTouch(2, new Vector2(2, 3), down: true);
        Run(game, 0.1f);
        Assert.True(left.Up && right.Up, "Two fingers didn't hold both flippers up.");
        _input.SetTouch(2, new Vector2(2, 3), down: false);
        Run(game, 0.2f);
        Assert.True(left.Up && !right.Up);
        Assert.True(plunger.Loaded);
        var before = plunger.Launches;
        _input.SetTouch(3, plunger.Tip - new Vector2(0, 0.2f), down: true);
        Run(game, 0.6f);
        Assert.True(plunger.Pull > 0.4f && left.Up, $"A finger on the plunger pulled it {plunger.Pull:0.00} while another held the left flipper.");
        _input.SetTouch(3, plunger.Tip - new Vector2(0, 0.2f), down: false);
        Run(game, 0.1f);
        Assert.Equal(before + 1, plunger.Launches);
        _input.SetTouch(1, new Vector2(-3, -2), down: false);
        Run(game, 0.2f);
        Assert.False(left.Up);
        Assert.True(At(BallOf(game)).Y > -5, $"The finger shot the ball only to {At(BallOf(game))}.");
    }

    // ----- To the bit, rewinds and garbage

    // Shoots, flips both flippers now and then, nudges once, and lights the top lanes once (as a wire might) so the
    // multiplier's up.
    private static void Play(Game game, InputState input, int from, int to)
    {
        for (var frame = from; frame < to; frame++)
        {
            input.SetKey(Key.Space, frame % 600 is >= 20 and < 80);
            input.SetKey(Key.LeftShift, frame % 50 is >= 10 and < 25);
            input.SetKey(Key.RightShift, frame % 70 is >= 30 and < 40);
            input.SetKey(Key.N, frame == 200);
            if (frame == 150)
            {
                foreach (var letter in "MID")
                    game.Find($"Lane {letter}")!.GetComponent<Lane>()!.Light();
            }

            game.Tick(Frame);
            input.EndFrame();
        }
    }

    private static string Moving(Scene scene) => string.Join("\n", scene.Entities
        .Where(e => e.GetComponent<Body>() is { Frozen: false })
        .Select(e => $"{e.Name} {BitConverter.SingleToInt32Bits(e.Transform.Position.X)} {BitConverter.SingleToInt32Bits(e.Transform.Position.Y)} {BitConverter.SingleToInt32Bits(e.Transform.Rotation.Z)}"));

    [Fact]
    public async Task A_game_plays_out_the_same_to_the_bit_twice_and_across_a_rewind_with_the_multiplier_up()
    {
        var project = await CreateProject();
        (string Moving, int Score, int Ball) Whole()
        {
            var input = new InputState();
            var game = new Game(GameLoader.LoadScene(project, _registry, PinballTemplate.TableScene), input)
            {
                PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
                Components = _registry,
                SaveData = new SaveData(),
            };
            game.UseProjectSettings(project);
            Play(game, input, 0, 1800);
            return (Moving(game.Scene), ScoreOf(game), DrainOf(game).Ball);
        }

        var first = Whole();
        var second = Whole();
        Assert.Equal(first, second);
        Assert.True(first.Score > 2000, $"Scored {first.Score}.");

        // Saved with the multiplier up, and played on from there with the same buttons: the same to the bit.
        var serializer = new SceneSerializer(_registry);
        var input = new InputState();
        var game = new Game(GameLoader.LoadScene(project, _registry, PinballTemplate.TableScene), input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            Components = _registry,
            SaveData = new SaveData(),
        };
        game.UseProjectSettings(project);
        Play(game, input, 0, 400);
        Assert.Equal(2, MultiplierOf(game));
        var timeline = new Timeline(serializer, capacity: 4, framesPerSnapshot: 1);
        timeline.Record(game.Scene, game.Time.Total);
        var restored = timeline.Restore(0);
        var replayInput = new InputState();
        var replay = new Game(restored, replayInput)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            Components = _registry,
            SaveData = new SaveData(),
        };
        replay.UseProjectSettings(project);
        Play(game, input, 400, 1800);
        Play(replay, replayInput, 400, 1800);
        Assert.Equal(Moving(game.Scene), Moving(restored));
        Assert.Equal(ScoreOf(game), restored.Entities.Single(e => e.Name == "Score").GetComponent<Counter>()!.Value);
        Assert.Equal(first.Score, ScoreOf(game));
    }

    [Fact]
    public async Task Playing_makes_no_garbage_but_writing_the_new_score_and_a_balls_comings_and_goings()
    {
        var project = await CreateProject();

        // Shot, flipped at and knocked about for half a minute, drained, saved and served. Once the table has settled in (its
        // first second: the ball coming to rest on the plunger), a step allocates nothing while the ball's in play: only as
        // the score changes (its text, and the best's, written again) and as a ball drains, is saved, served and shot (with
        // its callout popping up and going), and the step after. Steps are counted on this thread (and the workers' for it),
        // and only those no garbage collection ran during: another thread's (other tests running alongside) can add to this
        // thread's count what it never allocated. The game plays the same every time, so it's played again, a fresh table
        // each, until every step has been counted, and the least a step allocated in any play is what counts: the runtime
        // first runs methods compiled quickly, which can allocate what the fully compiled ones don't, and while other tests
        // keep the machine busy it takes a while to compile them fully.
        StepKind[]? kinds = null;
        var least = new long[Frames];
        Array.Fill(least, -1);
        bool Settled() => kinds is not null && Enumerable.Range(0, Frames).All(i => kinds[i] switch
        {
            StepKind.Quiet => least[i] == 0,
            StepKind.Scoring => least[i] >= 0,
            _ => true,
        });
        for (var play = 0; play < 20 && !Settled(); play++)
        {
            if (play > 0)
                Thread.Sleep(50);
            var (kind, bytes) = PlayForGarbage(Start(project, PinballTemplate.TableScene));
            kinds ??= kind;
            Assert.Equal(kinds, kind);
            for (var i = 0; i < Frames; i++)
            {
                if (bytes[i] >= 0)
                    least[i] = least[i] < 0 ? bytes[i] : Math.Min(least[i], bytes[i]);
            }
        }

        var quiet = Enumerable.Range(0, Frames).Where(i => kinds![i] == StepKind.Quiet).ToList();
        var scoring = Enumerable.Range(0, Frames).Where(i => kinds![i] == StepKind.Scoring).ToList();
        Assert.True(scoring.Count > 5 && quiet.Count > 60 * 20, $"Only {scoring.Count} scores and {quiet.Count} quiet steps.");
        var uncounted = quiet.Count(i => least[i] < 0);
        Assert.True(uncounted <= quiet.Count / 10, $"{uncounted} of the {quiet.Count} quiet steps always had a garbage collection run during them.");
        var noisy = quiet.Where(i => least[i] > 0).Select(i => $"frame {i}: {least[i]} bytes").ToList();
        Assert.True(noisy.Count == 0, $"Steps with the ball in play allocated: {string.Join("; ", noisy)}");
        var scored = scoring.Where(i => least[i] >= 0).ToList();
        Assert.True(scored.Count > 5, $"Only {scored.Count} of the {scoring.Count} scores were counted (garbage collections ran in the rest).");
        var allocated = scored.Sum(i => least[i]);
        Assert.True(allocated / scored.Count < 600, $"{scored.Count} scores allocated {allocated:N0} bytes.");
    }

    // Half a minute of the table played, a step at a time.
    private const int Frames = 60 * 31;

    private enum StepKind
    {
        // The first second, a ball draining, saved, served or shot (a callout up), and the step after: not counted.
        Other,
        // The ball in play, nothing changing: none may allocate.
        Quiet,
        // Only the score changed.
        Scoring,
    }

    // Plays the table with the same buttons each time: what each step was, and what it allocated (on this thread and the
    // workers' for it), or -1 if a garbage collection ran during it.
    private (StepKind[] Kinds, long[] Bytes) PlayForGarbage(Game game)
    {
        var score = Of<Counter>(game, "Score");
        var drain = DrainOf(game);
        var plunger = PlungerOf(game);
        (int, int, string?, bool, int, int) State() => (score.Value, drain.Ball, drain.Serving, drain.Saving, plunger.Launches, game.Scene.Entities.Count);
        var kinds = new StepKind[Frames];
        var bytes = new long[Frames];
        var after = false;
        for (var frame = 0; frame < Frames; frame++)
        {
            _input.SetKey(Key.Space, frame % 600 is >= 20 and < 80);
            _input.SetKey(Key.LeftShift, frame % 50 is >= 10 and < 25);
            _input.SetKey(Key.RightShift, frame % 70 is >= 30 and < 40);
            var was = State();
            var callouts = game.Scene.Entities.Any(e => e.GetComponent<Lifetime>() is not null);
            var (before, collections) = (Workers.AllocatedBytes(), GC.CollectionCount(0));
            Step(game);
            bytes[frame] = GC.CollectionCount(0) == collections ? Workers.AllocatedBytes() - before : -1;
            var now = State();
            if (frame < 60)
                kinds[frame] = StepKind.Other;
            else if (now.Item1 != was.Item1 && now.Item2 == was.Item2 && now.Item3 == was.Item3 && now.Item5 == was.Item5 && now.Item6 == was.Item6)
                kinds[frame] = StepKind.Scoring;
            else
                kinds[frame] = now != was || after || callouts ? StepKind.Other : StepKind.Quiet;
            after = now != was;
        }

        foreach (var key in new[] { Key.Space, Key.LeftShift, Key.RightShift })
            _input.SetKey(key, false);
        _input.EndFrame();
        return (kinds, bytes);
    }

    // ----- Sounds and exports

    [Fact]
    public async Task The_template_comes_with_its_own_sounds_hooked_up()
    {
        var project = await CreateProject();
        foreach (var sound in new[] { PinballTemplate.MultiplierSound, PinballTemplate.ExtraBallSound, PinballTemplate.BellSound, PinballTemplate.NewBestSound,
                     PinballTemplate.GameOverSound, "Pinball/flip.wav", "Pinball/bumper.wav", "Pinball/launch.wav", "Pinball/tilt.wav" })
            Assert.True(Griff.Audio.Wav.Decode(await File.ReadAllBytesAsync(Path.Combine(project.AssetsPath, sound))) is { Seconds: > 0 }, sound);

        var game = Start(project, PinballTemplate.TableScene);
        Assert.Equal(PinballTemplate.GameOverSound, Of<Panel>(game, "Game Over").Sound);
        Assert.Equal(PinballTemplate.BellSound, Of<Target>(game, "Bell").Sound);
        Assert.Contains(game.Find("Multiplier")!.Components, c => c is SoundPlayer { Sound: PinballTemplate.MultiplierSound, Message: PinballTemplate.LanesLit });
        Assert.Contains(game.Find("Score")!.Components, c => c is SoundPlayer { Sound: PinballTemplate.NewBestSound });
        Assert.Contains(game.Find("Drain")!.Components, c => c is SoundPlayer { Sound: PinballTemplate.ExtraBallSound });
        Assert.NotNull(Of<Flipper>(game, "Left Flipper").Sound);
        Assert.NotNull(Of<Tilt>(game, "Table").TiltSound);
    }

    [Fact]
    public async Task Exports_for_the_desktop_and_the_web_carry_its_pictures_and_sounds_and_play()
    {
        var project = await CreateProject();
        string[] wanted = ["Assets/Textures/midway-playfield.png", "Assets/Textures/bell.png", "Assets/Pinball/ball.png", "Assets/Pinball/flip.wav",
            "Assets/Sounds/multiplier.wav", "Assets/Scenes/Table.scene", "Assets/Prefabs/Ball Saved.prefab"];

        // The desktop: one .exe, holding the game's files, whose table plays as in the editor.
        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");
        var desktop = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(desktop.Success, desktop.Error);
        var extracted = GamePackage.Extract(desktop.Executable!, Path.Combine(_root, "cache"), "Midway")!;
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(extracted, file)), file));
        var loaded = await GameLoader.LoadAsync(extracted);
        Assert.Empty(loaded.Problems);
        var game = new Game(loaded.LoadScene("Table"), _input) { SceneLoader = loaded.LoadScene, PrefabLoader = loaded.LoadPrefab, SceneName = "Table" };
        game.UseProjectSettings(loaded.Project);
        Run(game, 0.3f);
        Plunge(game);
        Run(game, 0.3f);
        Assert.True(At(BallOf(game)).Y > 0, $"The ball's at {At(BallOf(game))}.");
        Run(game, 3);
        Assert.True(ScoreOf(game) > 0);

        // The web: the files the browser fetches are listed.
        var web = Path.Combine(_root, "web-player");
        Directory.CreateDirectory(Path.Combine(web, "_framework"));
        File.WriteAllText(Path.Combine(web, WebExporter.Page), "<html>");
        var site = WebExporter.Export(project, web, Path.Combine(_root, "web-out"));
        Assert.True(site.Success, site.Error);
        var gameFolder = Path.Combine(_root, "web-out", ExportLayout.GameFolder);
        var listed = File.ReadAllLines(Path.Combine(gameFolder, ExportLayout.WebFileList));
        Assert.All(wanted, file => Assert.Contains(file, listed));
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(gameFolder, file)), file));
    }
}
