using System.Numerics;
using Griff.Scenes;

namespace Griff.Projects;

// The BunnyMark template: a benchmark rather than a game. Bunnies pour in from the top left and bounce about, more and
// more of them while the game keeps up 60 frames a second, until it finds how many this computer holds there. Each bunny
// is an ordinary entity, a Sprite and a script, so the count says how much an ordinary game can put on screen.
public static partial class ProjectTemplates
{
    private static readonly Color BunnySky = Color.FromBytes(0x1B, 0x22, 0x33);
    private static readonly Color BunnyText = Color.FromBytes(0xF4, 0xF4, 0xF8);
    private static readonly Color BunnyDim = Color.FromBytes(0xA4, 0xAC, 0xC0);

    private static Scene BunnyMarkScene()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 5;
        view.Background = BunnySky;
        scene.Add(camera);

        var bunnyMark = new Entity("BunnyMark");
        bunnyMark.AddComponent(Script("BunnyMark"));
        scene.Add(bunnyMark);

        // The frame rate and the count, top left, and how the run is going (then its result) along the bottom, each on a
        // dark band so they read over the bunnies.
        scene.Add(HudBand("Stats Band", Anchor.TopLeft, new Vector2(1.6f, -0.55f), new Vector2(3.4f, 1.1f)));
        var stats = Label("Stats", Vector2.Zero, "60 fps\n0 bunnies", 0.38f, BunnyText);
        stats.GetComponent<Text>()!.Align = TextAlign.Left;
        stats.AddComponent<ScreenAnchor>().Offset = new Vector2(0.25f, -0.55f);
        scene.Add(stats);

        scene.Add(HudBand("Status Band", Anchor.Bottom, new Vector2(0, 0.45f), new Vector2(40, 0.9f)));
        var status = Label("Status", Vector2.Zero, "Adding bunnies while the game keeps up 60 fps...", 0.36f, BunnyDim);
        var anchor = status.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.45f);
        scene.Add(status);
        return scene;
    }

    private static Entity HudBand(string name, Anchor anchor, Vector2 offset, Vector2 size)
    {
        var band = new Entity(name);
        var sprite = band.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Color.FromBytes(0x0C, 0x0F, 0x18) with { A = 0.8f };
        sprite.Order = 99;
        var screen = band.AddComponent<ScreenAnchor>();
        screen.Anchor = anchor;
        screen.Offset = offset;
        return band;
    }

    private static Dictionary<string, Entity> BunnyMarkPrefabs() => new()
    {
        ["Prefabs/Bunny.prefab"] = BunnyPrefab(),
    };

    private static Entity BunnyPrefab()
    {
        var bunny = new Entity("Bunny");
        var sprite = bunny.AddComponent<Sprite>();
        sprite.Image = TemplateArt.Bunny;
        sprite.Size = new Vector2(0.39f, 0.5f);
        bunny.AddComponent(Script("Bunny"));
        return bunny;
    }

    private const string BunnyScript = """
        /// <summary>
        /// Falls, bounces off the floor and the sides of the view, and never settles: each landing hops it back up by a
        /// different amount, as in the original BunnyMark. BunnyMark adds them by the thousand.
        /// </summary>
        [Category("Benchmark")]
        public class Bunny : Behavior
        {
            /// <summary>Units a second, right and up. A bunny added without one takes a random one.</summary>
            public Vector2 Velocity { get; set; }

            /// <summary>How fast it falls faster, in units a second every second.</summary>
            public float Gravity { get; set; } = 25;

            /// <summary>Half the width and height of the box it bounces about in, around the middle of the scene.</summary>
            public Vector2 Area { get; set; } = new(8.7f, 4.75f);

            protected override void Start()
            {
                if (Velocity == Vector2.Zero)
                    Velocity = new Vector2(Rand.Range(0f, 8f), Rand.Range(-4f, 4f));
            }

            protected override void Update()
            {
                var velocity = Velocity;
                velocity.Y -= Gravity * Time.Delta;
                var position = Transform.Position;
                position.X += velocity.X * Time.Delta;
                position.Y += velocity.Y * Time.Delta;

                if (position.X > Area.X || position.X < -Area.X)
                {
                    position.X = Math.Clamp(position.X, -Area.X, Area.X);
                    velocity.X = -velocity.X;
                }

                if (position.Y < -Area.Y)
                {
                    position.Y = -Area.Y;
                    velocity.Y = -velocity.Y * 0.85f;
                    if (Rand.Chance(0.5f))
                        velocity.Y += Rand.Range(0f, 5f);
                }
                else if (position.Y > Area.Y)
                {
                    position.Y = Area.Y;
                    velocity.Y = 0;
                }

                Transform.Position = position;
                Velocity = velocity;
            }
        }

        """;

    private const string BunnyMarkScript = """
        /// <summary>
        /// Finds how many bunnies this computer keeps bouncing at 60 frames a second. It adds bunnies while the game keeps
        /// up; when it falls behind, it takes the last lot away and tries half as many, until a step is too small to
        /// matter. Then it shows the count and saves it ("held", and the "best" so far). Space starts it again.
        /// </summary>
        [Category("Benchmark")]
        public class BunnyMark : Behavior
        {
            /// <summary>The frame rate the bunnies have to keep up.</summary>
            public float TargetFps { get; set; } = 60;

            /// <summary>Seconds each count is timed for.</summary>
            public float Window { get; set; } = 1;

            /// <summary>Bunnies to start with.</summary>
            public int StartCount { get; set; } = 100;

            /// <summary>How many each step adds, as a share of those already bouncing (0.5 is half as many again).</summary>
            public float Growth { get; set; } = 0.5f;

            /// <summary>The run ends once a step would add less than this share.</summary>
            public float Precision { get; set; } = 0.02f;

            /// <summary>The prefab it adds.</summary>
            [Names(NameKind.Prefab)]
            public string Prefab { get; set; } = "Bunny";

            /// <summary>Bunnies bouncing now.</summary>
            public int Count => _bunnies.Count;

            /// <summary>The most bunnies that have kept up the frame rate so far: the result, once the run is done.</summary>
            public int Held { get; private set; }

            /// <summary>The frame rate over the last count timed.</summary>
            public float Fps { get; private set; }

            /// <summary>True once the run has found its count.</summary>
            public bool IsDone { get; private set; }

            private readonly List<Entity> _bunnies = new();
            private bool _timing;
            private int _frames;
            private float _seconds;
            private int _shownFrames;
            private float _shownSeconds;

            protected override void Start() => StartCoroutine(Run());

            protected override void Update()
            {
                if (Input.WasPressed(Key.Space))
                {
                    StopCoroutines();
                    StartCoroutine(Run());
                }

                if (_timing)
                {
                    _frames++;
                    _seconds += Time.UnscaledDelta;
                }

                // The frame rate shown is counted on its own, a few times a second, so it moves even between steps.
                _shownFrames++;
                _shownSeconds += Time.UnscaledDelta;
                if (_shownSeconds >= 0.25f)
                {
                    Show("Stats", $"{_shownFrames / _shownSeconds:0} fps\n{Count:N0} bunnies");
                    (_shownFrames, _shownSeconds) = (0, 0);
                }
            }

            private IEnumerator Run()
            {
                (IsDone, Held) = (false, 0);
                Show("Status", $"Adding bunnies while the game keeps up {TargetFps:0} fps...");
                RemoveTo(0);
                var step = Growth;
                Add(StartCount);
                while (true)
                {
                    yield return TimeFrames();
                    // A frame missed in the second still counts as keeping up.
                    if (Fps >= TargetFps * 0.97f)
                    {
                        Held = Count;
                        Add(Math.Max(1, (int)(Count * step)));
                        continue;
                    }

                    RemoveTo(Held);
                    step /= 2;
                    if (step < Precision)
                        break;
                    Add(Math.Max(1, (int)(Math.Max(Held, StartCount) * step)));
                }

                IsDone = true;
                var best = Math.Max(Held, Game.SaveData.GetInt("best"));
                Game.SaveData.Set("best", best);
                Game.SaveData.Set("held", Held);
                Show("Status", $"Holds {Held:N0} bunnies at {TargetFps:0} fps (best {best:N0}). Space to run it again.");
            }

            // Times the frames at the count now. New bunnies start on the next frame and gone ones go at the end of this
            // one, so the two frames that add or take them away aren't timed.
            private IEnumerator TimeFrames()
            {
                yield return null;
                yield return null;
                (_frames, _seconds, _timing) = (0, 0, true);
                yield return WaitUntil(() => _seconds >= Window);
                _timing = false;
                Fps = _frames / _seconds;
            }

            private void Add(int count)
            {
                // They bounce about the whole view, from the top left corner where they come in.
                var view = Game.Find<Camera>()?.Size ?? 5;
                var area = new Vector2(view * Game.ViewAspect - 0.2f, view - 0.25f);
                for (var i = 0; i < count; i++)
                {
                    var bunny = Game.Spawn(Prefab, new Vector2(-area.X, area.Y));
                    if (bunny.GetComponent<Bunny>() is { } hops)
                        hops.Area = area;
                    _bunnies.Add(bunny);
                }
            }

            private void RemoveTo(int count)
            {
                while (_bunnies.Count > count)
                {
                    Game.Destroy(_bunnies[^1]);
                    _bunnies.RemoveAt(_bunnies.Count - 1);
                }
            }

            private void Show(string name, string text)
            {
                if (Game.Find(name)?.GetComponent<Text>() is { } shown)
                    shown.Value = text;
            }
        }

        """;
}
