using System.Numerics;
using Griff.Scenes;

namespace Griff.Templates;

// The BunnyMark template: a benchmark rather than a game. Bunnies pour in from the top left and bounce about, more and
// more of them while the game keeps up 60 frames a second, until it finds how many this computer holds there. Each bunny
// is an ordinary entity, a Sprite and a script, so the count says how much an ordinary game can put on screen.
public static partial class ProjectTemplates
{
    // A benchmark stays quiet, so it times nothing but itself.
    private sealed class BunnyMarkTemplate : ProjectTemplate
    {
        public override string Description => "A benchmark: bunnies pour in and bounce about, more and more while the game keeps up 60 frames a second, until it finds how many this computer holds. Each one is an ordinary entity, a sprite and a script. The count is shown and saved, with the best so far. Space to run it again.";

        public override int Order => 140;

        protected override bool HasSounds => false;

        protected override Scene BuildStartScene() => BunnyMarkScene();

        protected override Dictionary<string, Entity> BuildPrefabs() => BunnyMarkPrefabs();

        public override IReadOnlyDictionary<string, string> Scripts() => new Dictionary<string, string>
        {
            ["Scripts/Bunny.cs"] = BunnyScript,
            ["Scripts/BunnyMark.cs"] = BunnyMarkScript,
        };

        public override IReadOnlyDictionary<string, byte[]> Images() => TemplateArt.BunnyMarkFiles();
    }

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
        /// different amount, as in the original BunnyMark. BunnyMark adds them by the thousand. A crowd script: it changes
        /// nothing but its own bunny, so the game runs thousands of them at once, across the cores.
        /// </summary>
        [Category("Benchmark")]
        [Crowd]
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
        /// matter. Then it holds that many for a while, timing every frame, and shows and saves the count ("held", and the
        /// "best" so far) with how the hold went: the frame rate ("fps"), its 1% low ("low") and the garbage collections
        /// ("collections"). Space starts it again.
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

            /// <summary>Seconds it holds the count it found, timing every frame.</summary>
            public float HoldSeconds { get; set; } = 10;

            /// <summary>Holds this many bunnies straight away rather than finding the count (0 finds it): to try a count.</summary>
            public int HoldCount { get; set; }

            /// <summary>Bunnies bouncing now.</summary>
            public int Count => _bunnies.Count;

            /// <summary>The most bunnies that have kept up the frame rate so far: the result, once the run is done.</summary>
            public int Held { get; private set; }

            /// <summary>The frame rate over the last count timed (the hold's, once it's done).</summary>
            public float Fps { get; private set; }

            /// <summary>
            /// The hold's 1% low: the frame rate of its slowest frames, from the time 99 frames in 100 came within. A
            /// frame that misses the screen's beat takes twice as long, so a low under the frame rate means some did.
            /// </summary>
            public float Low { get; private set; }

            /// <summary>The garbage collections during the hold: 0 when nothing a frame does makes garbage.</summary>
            public int Collections { get; private set; }

            /// <summary>True once the run has found its count.</summary>
            public bool IsDone { get; private set; }

            private readonly List<Entity> _bunnies = new();
            private readonly List<double> _frameTimes = new();
            private long _shownFrom;
            private double _shownSince;

            protected override void Start() => StartCoroutine(Run());

            protected override void Update()
            {
                if (Input.WasPressed(Key.Space))
                {
                    StopCoroutines();
                    StartCoroutine(Run());
                }

                // The frame rate shown is counted on its own, a few times a second, so it moves even between steps. Frames
                // are what the screen shows: the game itself always takes 60 steps a second, as long as it keeps up.
                if (Time.RealTotal - _shownSince >= 0.25)
                {
                    Show("Stats", $"{(Time.DrawnFrames - _shownFrom) / (Time.RealTotal - _shownSince):0} fps\n{Count:N0} bunnies");
                    (_shownFrom, _shownSince) = (Time.DrawnFrames, Time.RealTotal);
                }
            }

            private IEnumerator Run()
            {
                (IsDone, Held, Low, Collections) = (false, 0, 0, 0);
                RemoveTo(0);
                if (HoldCount > 0)
                {
                    Add(HoldCount);
                    Held = HoldCount;
                }
                else
                {
                    yield return FindCount();
                }

                Show("Status", $"Holding {Held:N0} bunnies for {HoldSeconds:0} seconds, timing every frame...");
                yield return Hold();
                IsDone = true;
                Game.SaveData.Set("fps", (int)MathF.Round(Fps));
                Game.SaveData.Set("low", (int)MathF.Round(Low));
                Game.SaveData.Set("collections", Collections);
                Game.SaveData.Set("held", Held);
                var hold = $"{Fps:0} fps, 1% low {Low:0} fps, {Collections} garbage collections. Space to run it again.";
                if (HoldCount > 0)
                {
                    Show("Status", $"Held {Held:N0} bunnies for {HoldSeconds:0} s: {hold}");
                    yield break;
                }

                var best = Math.Max(Held, Game.SaveData.GetInt("best"));
                Game.SaveData.Set("best", best);
                Show("Status", $"Holds {Held:N0} bunnies at {TargetFps:0} fps (best {best:N0}); held for {HoldSeconds:0} s: {hold}");
            }

            // Adds bunnies while the frame rate keeps up, and takes the last lot away when it falls behind, until a step
            // would be too small to matter.
            private IEnumerator FindCount()
            {
                Show("Status", $"Adding bunnies while the game keeps up {TargetFps:0} fps...");
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
            }

            // Times every frame the screen shows for HoldSeconds, and counts the garbage collections meanwhile, after two
            // seconds to settle (the runtime optimizes code that's just got busy). Frames with no step between them (a
            // screen faster than the game's steps) share the time they took.
            private IEnumerator Hold()
            {
                yield return Wait(2);
                _frameTimes.Clear();
                _frameTimes.EnsureCapacity((int)(HoldSeconds * 500));
                var (frames, since) = (Time.DrawnFrames, Time.RealTotal);
                var (start, collections) = (since, GC.CollectionCount(0));
                while (Time.RealTotal - start < HoldSeconds)
                {
                    yield return null;
                    var drawn = Time.DrawnFrames - frames;
                    for (var i = 0; i < drawn; i++)
                        _frameTimes.Add((Time.RealTotal - since) / drawn);
                    if (drawn > 0)
                        (frames, since) = (Time.DrawnFrames, Time.RealTotal);
                }

                Collections = GC.CollectionCount(0) - collections;
                if (_frameTimes.Count == 0)
                    yield break;
                Fps = (float)(_frameTimes.Count / _frameTimes.Sum());
                _frameTimes.Sort();
                Low = (float)(1 / _frameTimes[(int)(_frameTimes.Count * 0.99)]);
            }

            // Times the frames the screen shows at the count now. New bunnies start on the next step and gone ones go at the
            // end of this one, so the two steps that add or take them away aren't timed.
            private IEnumerator TimeFrames()
            {
                yield return null;
                yield return null;
                var (frames, since) = (Time.DrawnFrames, Time.RealTotal);
                yield return WaitUntil(() => Time.RealTotal - since >= Window);
                Fps = (float)((Time.DrawnFrames - frames) / (Time.RealTotal - since));
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
