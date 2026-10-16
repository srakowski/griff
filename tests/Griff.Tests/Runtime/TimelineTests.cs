using System.Numerics;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Griff.Templates;

namespace Griff.Tests.Runtime;

public class TimelineTests
{
    private readonly SceneSerializer _serializer = new(ComponentRegistry.CreateDefault());

    [Fact]
    public void Rewind_RestoresEarlierState_AndResumingBranches()
    {
        var scene = new Scene();
        var ball = new Entity("Ball");
        ball.AddComponent<BoxCollider>();
        var body = ball.AddComponent<Body>();
        body.GravityScale = 0;
        body.Velocity = new Vector2(1, 0);
        scene.Add(ball);
        var game = new Game(scene, new InputState());
        var timeline = new Timeline(_serializer, capacity: 100, framesPerSnapshot: 10);

        for (var frame = 0; frame < 60; frame++)
        {
            game.Tick(1 / 60.0);
            timeline.Record(scene, game.Time.Total);
        }

        Assert.Equal(6, timeline.Count);
        Assert.Equal(1f, ball.Transform.Position.X, 2);

        // Go back to the snapshot taken at about a third of a second.
        var restored = timeline.Restore(1);
        var restoredBall = restored.Find(ball.Id)!;
        Assert.Equal(0.333f, restoredBall.Transform.Position.X, 2);
        Assert.Equal(new Vector2(1, 0), restoredBall.GetComponent<Body>()!.Velocity);

        // Change the past and resume: the old future is gone.
        timeline.TruncateAfter(1);
        Assert.Equal(2, timeline.Count);
        restoredBall.GetComponent<Body>()!.Velocity = new Vector2(0, 2);
        var resumed = new Game(restored, new InputState());
        for (var frame = 0; frame < 30; frame++)
            resumed.Tick(1 / 60.0);

        Assert.Equal(0.333f, restoredBall.Transform.Position.X, 2);
        Assert.Equal(1f, restoredBall.Transform.Position.Y, 2);
    }

    [Fact]
    public void Capacity_DropsOldestSnapshots()
    {
        var scene = new Scene();
        var timeline = new Timeline(_serializer, capacity: 3, framesPerSnapshot: 1);

        for (var i = 0; i < 10; i++)
            timeline.Record(scene, i);

        Assert.Equal([7.0, 8.0, 9.0], timeline.Times);
    }

    // ----- Snapshots are the scene file, kept in binary -----

    // What rewinding used to give: the scene saved as a file and loaded again.
    private string AsFileLoads(Scene scene) => _serializer.SaveToString(_serializer.LoadFromString(_serializer.SaveToString(scene)));

    [Fact]
    public void A_snapshot_restores_every_component_as_its_scene_file_loads_odd_values_and_all()
    {
        // Values a file can't hold as they are (numbers that aren't finite, a color's fractions of a step, lines that
        // are null), components of a type no longer known, and values kept from a property since removed.
        var scene = _serializer.LoadFromString("""
            {"formatVersion": 1, "entities": [
              {"id": "6f1b6a52-4c0e-4c55-9d0c-1d2a3b4c5d6e", "name": "Old", "prefab": "Prefabs/Old.prefab", "locked": true,
               "components": [
                 {"type": "Transform", "position": [1, 2, 3], "rotation": [0, 0, 0], "scale": [1, 1, 1], "wobble": [1,2]},
                 {"type": "Griff.Nothing", "speed": 3, "names": ["a", "b"]},
                 {"type": "Sprite", "image": "a.png", "legacy": {"keep": [1, 2.5, "x"]}}]}]}
            """);
        var values = 0;
        foreach (var descriptor in _serializer.Components.All.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            var entity = new Entity(descriptor.DisplayName) { IsEnabled = values % 2 == 0, IsHidden = values % 3 == 0 };
            var component = descriptor.Type == typeof(Transform) ? entity.Transform : entity.AddComponent(descriptor.CreateInstance());
            foreach (var property in descriptor.Properties)
            {
                try
                {
                    property.SetValue(component, OddValue(property.Type, values++));
                }
                catch (Exception)
                {
                    // A setter that won't take it keeps its value: the snapshot has to match whatever it is.
                }
            }

            scene.Add(entity, values % 4 == 0 ? scene.Roots[0] : null);
        }

        var timeline = new Timeline(_serializer);
        timeline.Record(scene, 0, force: true);

        Assert.Equal(AsFileLoads(scene), _serializer.SaveToString(timeline.Restore(0)));
        var restored = timeline.Restore(0);
        Assert.Equal(scene.Entities.Select(e => e.Id), restored.Entities.Select(e => e.Id));
        Assert.IsType<MissingComponent>(restored.Roots[0].Components[1]);
    }

    private static object? OddValue(Type type, int n) => type switch
    {
        _ when type == typeof(bool) => n % 2 == 0,
        _ when type == typeof(int) => n % 3 == 0 ? -7 : n,
        _ when type == typeof(float) => (n % 5) switch { 0 => float.NaN, 1 => float.PositiveInfinity, 2 => -0f, _ => 1.2345678f * n },
        _ when type == typeof(double) => (n % 4) switch { 0 => double.NegativeInfinity, _ => 0.1 + n },
        _ when type == typeof(string) => (n % 3) switch { 0 => null, 1 => "", _ => $"hé \"{n}\" \U0001F600" },
        _ when type.IsEnum => n % 2 == 0 ? Enum.ToObject(type, 99) : Enum.GetValues(type).GetValue(Enum.GetValues(type).Length - 1),
        _ when type == typeof(Color) => new Color(0.3337f, 0.5f + n % 7 * 0.01f, 1.2f, -0.1f),
        _ when type == typeof(Vector2) => n % 4 == 0 ? new Vector2(1, float.NaN) : new Vector2(0.1f * n, -n),
        _ when type == typeof(Vector3) => new Vector3(0.3f, n, 1e-9f),
        _ when type == typeof(Vector4) => new Vector4(n, 2, 3, 4),
        _ when type == typeof(Quaternion) => Quaternion.CreateFromYawPitchRoll(0.1f * n, 0.2f, 0.3f),
        _ when type == typeof(IReadOnlyList<string>) => n % 3 == 0 ? null : new List<string> { "ab", null!, "ü" },
        _ => throw new NotSupportedException(type.Name),
    };

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task Rewinding_a_template_to_any_snapshot_gives_what_the_scene_file_would_and_plays_on_the_same(string template)
    {
        var root = Path.Combine(Path.GetTempPath(), "griff-timeline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = await Project.CreateAsync(root, "Rewind", ProjectTemplates.Get(template));
            var registry = ComponentRegistry.CreateDefault();
            Assert.True(new ScriptHost(registry).ReloadFolder(project.AssetsPath, []).Success);
            var serializer = new SceneSerializer(registry);
            var prefabs = new PrefabLibrary(project, registry);
            Game Start(Scene scene) => new(scene, new InputState())
            {
                PrefabLoader = prefabs.Instantiate,
                SceneLoader = name => GameLoader.LoadScene(project, registry, name),
                Components = registry,
                ViewAspect = 16 / 9f,
            };

            // Play the start scene for two seconds, a snapshot every step, keeping the scene file of each step too.
            Rand.Seed(7);
            var game = Start(GameLoader.LoadScene(project, registry, Project.DefaultSceneName));
            var timeline = new Timeline(serializer, framesPerSnapshot: 1);
            var files = new List<string>();
            game.Stepped += (_, _) =>
            {
                timeline.Record(game.Scene, game.Time.Total);
                files.Add(serializer.SaveToString(game.Scene));
            };
            for (var step = 0; step < 120; step++)
                game.Advance(1 / 60.0);
            Assert.Equal(120, timeline.Count);

            string Loaded(string file) => serializer.SaveToString(serializer.LoadFromString(file));
            foreach (var index in new[] { 0, 1, 59, 119 })
                Assert.Equal(Loaded(files[index]), serializer.SaveToString(timeline.Restore(index)));

            // Resumed from a snapshot, it plays on just as it would from the scene file.
            string PlayOn(Scene scene)
            {
                Rand.Seed(11);
                var resumed = Start(scene);
                for (var step = 0; step < 60; step++)
                    resumed.Advance(1 / 60.0);
                return System.Text.RegularExpressions.Regex.Replace(serializer.SaveToString(resumed.Scene), "\"id\": \"[^\"]*\"", "");
            }

            // (Entities it spawns get new ids each time.)
            Assert.Equal(PlayOn(serializer.LoadFromString(files[59])), PlayOn(timeline.Restore(59)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    public static TheoryData<string> Templates => [.. ProjectTemplates.All.Select(t => t.Name)];

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task Rewinding_a_template_partway_plays_on_with_the_same_input_to_the_same_last_bit(string template)
    {
        var root = Path.Combine(Path.GetTempPath(), "griff-rewind-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = await Project.CreateAsync(root, "Rewind", ProjectTemplates.Get(template));
            var registry = ComponentRegistry.CreateDefault();
            Assert.True(new ScriptHost(registry).ReloadFolder(project.AssetsPath, []).Success);
            var serializer = new SceneSerializer(registry);

            // Five seconds of presses, clicks and mouse movement, each frame's as a whole (so the input can be set to any
            // frame's), played through once with a snapshot two seconds in.
            const int Steps = 300, RewindAfter = 120;
            Key[] keys = [Key.Left, Key.Right, Key.Up, Key.Down, Key.Space, Key.Z, Key.X, Key.Enter, Key.W, Key.A, Key.S, Key.D, Key.E, Key.D1, Key.D2];
            var random = new Random(3);
            var frames = new List<(bool[] Keys, Vector2 Mouse, bool Click)>();
            var (held, click) = (new bool[keys.Length], false);
            for (var frame = 0; frame < Steps; frame++)
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    if (random.Next(8) == 0)
                        held[i] = random.Next(2) == 0;
                }

                if (random.Next(10) == 0)
                    click = random.Next(2) == 0;
                frames.Add(([.. held], new Vector2(random.NextSingle() * 16 - 8, random.NextSingle() * 10 - 5), click));
            }

            var input = new InputState();
            void Press(int frame)
            {
                var (down, mouse, clicked) = frames[frame];
                for (var i = 0; i < keys.Length; i++)
                    input.SetKey(keys[i], down[i]);
                input.MouseWorld = mouse;
                input.SetMouseButton(MouseButton.Left, clicked);
            }

            var start = template == "Multiplayer" ? "Arena" : Project.DefaultSceneName;
            Rand.Seed(7);
            var game = new Game(GameLoader.LoadScene(project, registry, start), input)
            {
                PrefabLoader = new PrefabLibrary(project, registry).Instantiate,
                SceneLoader = name => GameLoader.LoadScene(project, registry, name),
                SceneName = start,
                Components = registry,
                ViewAspect = 16 / 9f,
            };
            var timeline = new Timeline(serializer, framesPerSnapshot: 1000);
            void Play(int from)
            {
                for (var frame = from; frame < Steps; frame++)
                {
                    Press(frame);
                    game.Tick(1 / 60.0);
                    input.EndFrame();
                    if (frame == RewindAfter - 1)
                        timeline.Record(game, force: true);
                }
            }

            Play(0);
            var first = Projects.GameFingerprint.Describe(game.SceneName, game.Scene, registry);
            var (time, steps) = (game.Time.Total, game.Time.Frame);

            // Back to the snapshot, the input as it was then, and on with the same presses.
            timeline.Restore(0, game);
            Press(RewindAfter - 1);
            input.EndFrame();
            Play(RewindAfter);

            var again = Projects.GameFingerprint.Describe(game.SceneName, game.Scene, registry);
            if (Environment.GetEnvironmentVariable("GRIFF_FINGERPRINT_DUMP") is { Length: > 0 } dump)
            {
                File.WriteAllText(Path.Combine(dump, template + ".first.txt"), first);
                File.WriteAllText(Path.Combine(dump, template + ".again.txt"), again);
            }

            Assert.Equal((time, steps), (game.Time.Total, game.Time.Frame));
            Assert.Equal(first, again);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Unchanged_entities_share_their_records_with_the_last_snapshot()
    {
        var scene = new Scene();
        for (var i = 0; i < 1000; i++)
        {
            var wall = new Entity($"Wall {i}");
            wall.AddComponent<Sprite>().Image = "Textures/wall.png";
            wall.Transform.Position = new Vector3(i, 0, 0);
            scene.Add(wall);
        }

        var ball = new Entity("Ball");
        scene.Add(ball, scene.Roots[500]);
        var timeline = new Timeline(_serializer, framesPerSnapshot: 1);
        timeline.Record(scene, 0);
        timeline.Complete();
        var first = timeline.Bytes;

        ball.Transform.Position = new Vector3(0, 1, 0);
        timeline.Record(scene, 1);
        timeline.Complete();

        // The second adds the ball's record and a list of where the rest are: far less than the first.
        Assert.True(timeline.Bytes - first < first / 2, $"{first} bytes, then {timeline.Bytes - first}");
        Assert.Equal(AsFileLoads(scene), _serializer.SaveToString(timeline.Restore(1)));
        ball.Transform.Position = Vector3.Zero;
        Assert.Equal(AsFileLoads(scene), _serializer.SaveToString(timeline.Restore(0)));
    }

    [Fact]
    public void Entities_that_come_and_go_and_shared_records_past_the_oldest_snapshot_still_restore()
    {
        var scene = new Scene();
        var still = new Entity("Still");
        scene.Add(still);
        var timeline = new Timeline(_serializer, capacity: 3, framesPerSnapshot: 1);
        var files = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            // One comes in at the front each time, and every other time the oldest goes.
            var spawned = new Entity($"Spawned {i}");
            spawned.Transform.Position = new Vector3(i, 0, 0);
            scene.Add(spawned, index: 0);
            if (i % 2 == 1)
                scene.Remove(scene.Roots[^2]);
            timeline.Record(scene, i);
            files.Add(AsFileLoads(scene));
        }

        Assert.Equal(3, timeline.Count);
        for (var i = 0; i < 3; i++)
            Assert.Equal(files[9 + i], _serializer.SaveToString(timeline.Restore(i)));

        // Rewound and resumed: the new future shares with what's left.
        timeline.TruncateAfter(0);
        scene = timeline.Restore(0);
        scene.Roots[0].Transform.Position = new Vector3(5, 5, 5);
        timeline.Record(scene, 20);
        Assert.Equal(2, timeline.Count);
        Assert.Equal(files[9], _serializer.SaveToString(timeline.Restore(0)));
        Assert.Equal(AsFileLoads(scene), _serializer.SaveToString(timeline.Restore(1)));
    }

    [Fact]
    public void A_big_scene_is_snapshotted_less_often_to_stay_within_the_memory_budget()
    {
        var scene = new Scene();
        for (var i = 0; i < 500; i++)
            scene.Add(new Entity($"Thing {i}"));
        const long budget = 200_000;
        var timeline = new Timeline(_serializer, capacity: 100, framesPerSnapshot: 2) { MemoryBudget = budget };

        for (var step = 0; step < 600; step++)
        {
            // Everything moves every step, so nothing is shared.
            foreach (var thing in scene.Roots)
                thing.Transform.Position = new Vector3(step, 0, 0);
            timeline.Record(scene, step);
            Assert.True(timeline.Bytes <= budget || timeline.Count == 1);
        }

        // A snapshot of 500 entities takes a few thousand bytes (each keeps the bytes of its position that changed, and
        // every tenth all of each), so 100 of them can't fit: they're further apart, and the timeline still reaches back
        // about as far as 100 snapshots two steps apart would.
        Assert.True(timeline.Spacing > 2);
        Assert.True(timeline.Count < 100);
        var times = timeline.Times.ToList();
        Assert.Equal(timeline.Spacing, times[^1] - times[^2]);
        Assert.InRange(times[^1] - times[0], 160, 220);
    }

    // ----- Copied out at once, settled on another thread -----

    private static Scene Crowd(int count, List<string> lines)
    {
        var scene = new Scene();
        for (var i = 0; i < count; i++)
        {
            var thing = new Entity($"Thing {i}");
            thing.Transform.Position = new Vector3(i, 0, 0);
            var sprite = thing.AddComponent<Sprite>();
            sprite.Image = $"Sprites/{i % 7}.png";
            sprite.Tint = new Color(0.5f, i % 3 * 0.25f, 1);
            if (i % 100 == 0)
                thing.AddComponent<Dialogue>().Lines = lines;
            scene.Add(thing, i % 10 == 9 ? scene.Roots[^1] : null);
        }

        return scene;
    }

    [Fact]
    public void A_snapshot_holds_the_scene_as_it_was_taken_while_it_changes_as_the_snapshot_settles()
    {
        // A list a script set and goes on changing in place, as well as everything else changing.
        var lines = new List<string> { "Hello!", "Bye." };
        var scene = Crowd(3000, lines);
        var timeline = new Timeline(_serializer, framesPerSnapshot: 1);
        var files = new List<string>();
        for (var step = 0; step < 4; step++)
        {
            files.Add(AsFileLoads(scene));
            timeline.Record(scene, step);
            Assert.Equal(step + 1, timeline.Count);

            // Straight after it's taken, before it can have settled.
            foreach (var thing in scene.Entities.ToList())
            {
                thing.Transform.Position += new Vector3(0, 1, 0);
                if (thing.GetComponent<Sprite>() is { } sprite)
                    sprite.Image = $"Sprites/moved {step}.png";
            }

            lines.Add($"Line {step}");
            lines[0] = $"Changed {step}";
            scene.Remove(scene.Roots[step]);
            scene.Add(new Entity($"New {step}"), index: 0);
        }

        for (var i = 0; i < 4; i++)
            Assert.Equal(files[i], _serializer.SaveToString(timeline.Restore(i)));
    }

    [Fact]
    public void Snapshots_keep_what_changed_and_restore_from_the_last_whole_one_through_comings_goings_and_drops()
    {
        var lines = new List<string> { "Hello!" };
        var scene = Crowd(400, lines);
        var timeline = new Timeline(_serializer, capacity: 25, framesPerSnapshot: 1);
        var files = new List<string>();
        var sizes = new List<long>();
        for (var step = 0; step < 60; step++)
        {
            // Most move a little; some are renamed (their strings change), come, go, or swap places.
            var roots = scene.Roots;
            foreach (var thing in roots)
                thing.Transform.Position += new Vector3(0.1f, 0, 0);
            roots[step % roots.Count].Name = $"Renamed {step}";
            if (step % 7 == 3)
                scene.Remove(roots[step % roots.Count]);
            if (step % 5 == 1)
                scene.Add(new Entity($"Spawned {step}"), index: step % 50);
            if (step % 11 == 2)
            {
                var moved = scene.Roots[^1];
                scene.Remove(moved);
                scene.Add(moved, index: 3);
            }

            var bytes = timeline.Bytes;
            timeline.Record(scene, step);
            timeline.Complete();
            sizes.Add(timeline.Bytes - bytes);
            files.Add(AsFileLoads(scene));
        }

        Assert.Equal(25, timeline.Count);
        for (var i = 0; i < 25; i++)
            Assert.Equal(files[35 + i], _serializer.SaveToString(timeline.Restore(i)));

        // A snapshot of things that moved keeps the bytes of their positions that changed: far less than a whole one.
        Assert.True(sizes[1] * 4 < sizes[0], $"whole {sizes[0]} bytes, then {sizes[1]}");
        Assert.True(timeline.Bytes < sizes[0] * 25 / 2, $"{timeline.Bytes} bytes kept");
    }

    [Fact]
    public void Truncating_while_a_snapshot_settles_keeps_it_and_the_new_future_restores_as_the_file_would()
    {
        var scene = Crowd(2000, ["Hi"]);
        var timeline = new Timeline(_serializer, framesPerSnapshot: 1);
        var files = new List<string>();
        for (var step = 0; step < 3; step++)
        {
            foreach (var thing in scene.Roots)
                thing.Transform.Position += Vector3.UnitY;
            files.Add(AsFileLoads(scene));
            timeline.Record(scene, step);
        }

        // Rewound to the one still settling, and to one before it, then resumed.
        Assert.Equal(files[2], _serializer.SaveToString(timeline.Restore(2)));
        timeline.TruncateAfter(1);
        Assert.Equal(2, timeline.Count);
        scene = timeline.Restore(1);
        foreach (var thing in scene.Roots)
            thing.Transform.Position += Vector3.UnitX;
        timeline.Record(scene, 10);
        Assert.Equal(AsFileLoads(scene), _serializer.SaveToString(timeline.Restore(2)));
        Assert.Equal(files[0], _serializer.SaveToString(timeline.Restore(0)));
        Assert.Equal(files[1], _serializer.SaveToString(timeline.Restore(1)));
    }

    [ComponentName("Test.Swapped")]
    private sealed class Before : Component
    {
        public float Speed { get; set; }

        public string Gone { get; set; } = "";
    }

    [ComponentName("Test.Swapped")]
    private sealed class After : Component
    {
        public float Speed { get; set; }

        public int Added { get; set; } = 4;
    }

    [Fact]
    public void A_script_rebuilt_or_removed_while_playing_rewinds_by_name_as_the_scene_file_would()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Before>();
        var serializer = new SceneSerializer(registry);
        var scene = new Scene();
        var thing = new Entity("Thing");
        var before = thing.AddComponent<Before>();
        (before.Speed, before.Gone) = (2.5f, "old");
        scene.Add(thing);
        var timeline = new Timeline(serializer);
        timeline.Record(scene, 0, force: true);
        var file = serializer.SaveToString(scene);

        // Rebuilt: its new type takes the values it has names for and keeps the rest.
        registry.Unregister(typeof(Before));
        registry.Register<After>();
        var restored = timeline.Restore(0);
        var after = Assert.IsType<After>(restored.Roots[0].Components[1]);
        Assert.Equal(2.5f, after.Speed);
        Assert.Equal(4, after.Added);
        Assert.Equal(serializer.SaveToString(serializer.LoadFromString(file)), serializer.SaveToString(restored));

        // Removed: it's kept as a missing component, data and all.
        registry.Unregister(typeof(After));
        restored = timeline.Restore(0);
        Assert.IsType<MissingComponent>(restored.Roots[0].Components[1]);
        Assert.Equal(serializer.SaveToString(serializer.LoadFromString(file)), serializer.SaveToString(restored));
    }

    // The moment between two steps is remembered (as a snapshot is copied out, but neither settled nor kept), and kept as a
    // snapshot only when asked: when the step after it went wrong, the timeline holds exactly the moment before that step.
    [Fact]
    public void Remembering_each_step_keeps_exactly_the_moment_before_the_step_that_went_wrong()
    {
        var scene = new Scene();
        var ball = new Entity("Ball");
        ball.AddComponent<BoxCollider>();
        var body = ball.AddComponent<Body>();
        body.GravityScale = 0;
        body.Velocity = new Vector2(6, 0);
        scene.Add(ball);
        var game = new Game(scene, new InputState());
        // Remembered however long copying it out takes: how long that is depends on the machine and how busy it is (tests
        // running alongside), and a scene too slow to copy is the last part of this test, with no budget at all.
        var timeline = new Timeline(_serializer, capacity: 100, framesPerSnapshot: 10) { RememberBudget = TimeSpan.MaxValue };
        timeline.Record(scene, 0, force: true);

        // 13 steps: snapshots after steps 10 (and the first), the moment after step 13 remembered.
        float before = 0;
        for (var step = 1; step <= 13; step++)
        {
            game.Tick(1 / 60.0);
            if (!timeline.Record(scene, game.Time.Total))
                timeline.Remember(scene, game.Time.Total);
            before = ball.Transform.Position.X;
        }

        Assert.Equal(2, timeline.Count);
        // The next step goes wrong part way: the ball has moved on.
        game.Tick(1 / 60.0);
        Assert.NotEqual(before, ball.Transform.Position.X);
        var (index, exact) = timeline.KeepRemembered(timeline.Count - 1)!.Value;
        Assert.True(exact);
        Assert.Equal(2, index);
        Assert.Equal(3, timeline.Count);
        Assert.Equal(before, timeline.Restore(index).Find(ball.Id)!.Transform.Position.X, 4);
        // Asked again, it's the same snapshot.
        Assert.Equal((2, true), timeline.KeepRemembered(timeline.Count - 1));
        var sequence = timeline.SequenceAt(index);
        Assert.Equal(index, timeline.IndexOfSequence(sequence));

        // A step that took a snapshot is remembered as that snapshot.
        for (var step = 0; step < 7; step++)
        {
            game.Tick(1 / 60.0);
            if (!timeline.Record(scene, game.Time.Total))
                timeline.Remember(scene, game.Time.Total);
        }

        Assert.Equal((3, true), timeline.KeepRemembered(timeline.Count - 1));
        Assert.Equal(4, timeline.Count);

        // From a rewound moment: the snapshot it went back to, and after a step there, that step's start (the old future goes).
        timeline.RememberSnapshot(1);
        Assert.Equal((1, true), timeline.KeepRemembered(1));
        var restored = timeline.Restore(1);
        var replay = new Game(restored, new InputState());
        replay.Tick(1 / 60.0);
        timeline.Remember(restored, 1);
        var x = restored.Find(ball.Id)!.Transform.Position.X;
        replay.Tick(1 / 60.0);
        Assert.Equal((2, true), timeline.KeepRemembered(1));
        Assert.Equal(3, timeline.Count);
        Assert.Equal(x, timeline.Restore(2).Find(ball.Id)!.Transform.Position.X, 4);

        // A scene too slow to copy out every step isn't remembered: the last snapshot before it stands in.
        var slow = new Timeline(_serializer, framesPerSnapshot: 10) { RememberBudget = TimeSpan.Zero };
        slow.Record(scene, 0, force: true);
        slow.Record(scene, 0.1, force: true);
        slow.Remember(scene, 0.2);
        Assert.Equal((1, false), slow.KeepRemembered(1));
        Assert.Equal(2, slow.Count);
    }
}
