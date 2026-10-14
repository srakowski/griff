using System.Numerics;
using Griff.Input;
using Griff.Modules.Contraptions;
using Griff.Modules.FirstPerson;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// What a component draws from its own state, not its transform, is drawn between the game's last two steps as well:
/// particles, trails, weather, ropes and wires, debug shapes, the screen's fades and flashes, sprites fading, and a
/// first-person view looking up and down. Scripts do the same with <see cref="Smoothed{T}"/> and
/// <see cref="Drawing.Blend"/>, and the transform's rules for what's drawn as it is hold for all of it.
/// </summary>
public sealed class SmoothDrawingTests
{
    private const double Step = 1 / 60.0;
    private const float Aspect = 16 / 9f;

    private static RenderFrame Draw(Game game) => SceneRenderer.Build(game, Aspect);

    // Three frames: just after a step (drawn at the step before it), half a step on, and just after the next step (drawn
    // where the first one left things). For anything moving steadily, the middle frame is halfway between the others.
    private static (RenderFrame Before, RenderFrame Middle, RenderFrame After) ThreeFrames(Game game)
    {
        game.Advance(Step);
        var before = Draw(game);
        game.Advance(Step / 2);
        var middle = Draw(game);
        game.Advance(Step / 2);
        return (before, middle, Draw(game));
    }

    private static Game Play(Scene scene) => new(scene, new InputState());

    // Moves right at its speed, every step.
    private sealed class Slider : Behavior
    {
        public float Speed { get; set; } = 6;

        protected override void Update() => Transform.Position += new Vector3(Speed * Time.Delta, 0, 0);
    }

    // ----- Smoothed values -----

    // A needle turning 90 degrees a second, drawn as a line from its entity.
    private sealed class Needle : Behavior, IDrawable
    {
        private Smoothed<float> _angle;

        public float Angle => _angle.Value;

        protected override void Update() => _angle.Set(this, _angle.Value + 90 * Time.Delta);

        public void Draw(Drawing drawing)
        {
            var angle = drawing.Blend.TurnOf(_angle) * MathF.PI / 180;
            drawing.Line(Vector2.Zero, new Vector2(MathF.Cos(angle), MathF.Sin(angle)), 0.1f, Color.Red);
        }
    }

    private static float AngleOf(SpriteDraw line) => MathF.Atan2(line.World.M12, line.World.M11) * 180 / MathF.PI;

    [Fact]
    public void A_scripts_smoothed_value_is_drawn_between_what_it_was_and_is()
    {
        var entity = new Entity("Gauge");
        var needle = entity.AddComponent<Needle>();
        var scene = new Scene();
        scene.Add(entity);
        var game = Play(scene);
        game.Advance(Step);
        game.Advance(Step);
        Assert.Equal(3, needle.Angle, 4);

        // Half a step on, it's drawn halfway from 1.5 degrees (as the last step began) to 3.
        game.Advance(Step / 2);
        Assert.Equal(2.25f, AngleOf(Draw(game).Sprites.Single(s => s.Entity == entity)), 3);

        // Held still, or in the editor, it's drawn as it is.
        game.ShowAsIs();
        Assert.Equal(3, AngleOf(Draw(game).Sprites.Single(s => s.Entity == entity)), 3);
        Assert.Equal(3, AngleOf(SceneRenderer.Build(scene, Aspect).Sprites.Single(s => s.Entity == entity)), 3);
    }

    [Fact]
    public void A_smoothed_value_remembers_what_it_was_as_the_step_it_last_changed_in_began()
    {
        var owner = new Entity("Owner").AddComponent<Slider>();
        var scene = new Scene();
        scene.Add(owner.Entity!);
        var value = new Smoothed<float>(1);
        value.Set(owner, 2);
        Assert.Equal(0, value.ChangedIn);

        var game = Play(scene);
        game.Advance(Step);
        value.Set(owner, 3);
        value.Set(owner, 4);
        Assert.Equal((2f, 4f, 1L), (value.Was, value.Value, value.ChangedIn));

        // Not changing it in a step leaves it changed in the one it last changed in, so it's drawn as it is after.
        value.Set(owner, 4);
        game.Advance(Step);
        Assert.Equal(1, value.ChangedIn);
        Assert.Equal(1, StepBlend.AsIs.Amount);
        Assert.Equal(4, StepBlend.AsIs.Of(value));
    }

    // ----- Particles, trails, weather -----

    private static ParticleEmitter Emitter(Scene scene, Action<ParticleEmitter>? setUp = null)
    {
        var entity = new Entity("Sparks");
        var emitter = entity.AddComponent<ParticleEmitter>();
        (emitter.Emitting, emitter.BurstOnStart, emitter.Speed, emitter.SpeedVariation, emitter.Spread, emitter.Direction, emitter.Lifetime) =
            (false, 1, 6, 0, 0, 0, 10);
        (emitter.StartSize, emitter.EndSize, emitter.StartColor, emitter.EndColor) = (0.2f, 0.2f, Color.White, Color.White);
        setUp?.Invoke(emitter);
        scene.Add(entity);
        return emitter;
    }

    [Fact]
    public void Particles_are_drawn_between_where_they_were_and_are()
    {
        var scene = new Scene();
        var emitter = Emitter(scene);
        var game = Play(scene);
        game.Advance(Step);
        game.Advance(Step);
        Assert.Equal(0.2f, emitter.Particles[0].Position.X, 4);

        game.Advance(Step / 2);
        var drawn = Draw(game).Sprites.Single(s => s.Entity == emitter.Entity);
        Assert.Equal(0.15f, drawn.World.Translation.X, 4);

        // Drawn on their own (the editor's Scene view), they're where they are.
        Assert.Equal(0.2f, emitter.ParticleDraws().Single().World.Translation.X, 4);
    }

    [Fact]
    public void A_particle_fading_is_drawn_between_how_faded_it_was_and_is()
    {
        var scene = new Scene();
        var emitter = Emitter(scene, e => (e.Speed, e.Lifetime, e.EndColor) = (0, 1, Color.White with { A = 0 }));
        var game = Play(scene);

        var (before, middle, after) = ThreeFrames(game);

        float AlphaIn(RenderFrame frame) => frame.Sprites.Single(s => s.Entity == emitter.Entity).Tint.A;
        Assert.True(AlphaIn(before) > AlphaIn(middle) && AlphaIn(middle) > AlphaIn(after));
        Assert.Equal((AlphaIn(before) + AlphaIn(after)) / 2, AlphaIn(middle), 4);
    }

    [Fact]
    public void Particles_of_an_emitter_that_jumped_are_drawn_where_they_are()
    {
        var scene = new Scene();
        var emitter = Emitter(scene);
        var game = Play(scene);
        game.Advance(Step);
        game.Advance(Step);
        emitter.Entity!.Transform.Teleport(new Vector2(5, 0));

        game.Advance(Step / 2);

        Assert.Equal(0.2f, Draw(game).Sprites.Single(s => s.Entity == emitter.Entity).World.Translation.X, 4);
    }

    [Fact]
    public void A_trails_copies_fade_between_steps_and_a_new_one_shows_once_its_sprite_is_drawn_there()
    {
        var scene = new Scene();
        var dasher = new Entity("Dasher");
        dasher.AddComponent<Sprite>();
        dasher.AddComponent<Slider>().Speed = 12;
        var trail = dasher.AddComponent<Trail>();
        (trail.Interval, trail.Fade, trail.MinSpeed) = (0.005f, 1, 0);
        scene.Add(dasher);
        var game = Play(scene);
        for (var i = 0; i < 4; i++)
            game.Advance(Step);

        // The copy left in the last step is where the sprite is now, ahead of where it's drawn: it isn't shown yet.
        game.Advance(Step / 2);
        var copies = Draw(game).Sprites.Where(s => s.Entity == dasher && s.Tint.A < 1).ToList();
        var newest = trail.Copies[^1].World.Translation.X;
        Assert.DoesNotContain(copies, c => c.World.Translation.X == newest);
        Assert.Equal(trail.Copies.Count - 1, copies.Count);

        // The others are drawn part way through fading: between how faded they were as the last step began and are.
        var oldest = trail.Copies[0];
        var drawn = copies.Single(c => c.World.Translation.X == oldest.World.Translation.X).Tint.A;
        Assert.Equal(trail.Opacity * (1 - (oldest.Age - (float)Step / 2) / trail.Fade), drawn, 4);
    }

    [Fact]
    public void Rain_falls_between_steps()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        scene.Add(camera);
        var sky = new Entity("Sky");
        var rain = sky.AddComponent<Weather>();
        (rain.Kind, rain.Amount) = (WeatherKind.Rain, 0.2f);
        scene.Add(sky);
        var game = Play(scene);
        game.Advance(Step);

        var (before, middle, after) = ThreeFrames(game);

        // Each drop is drawn halfway between where it was drawn a step apart (but for any wrapping round the view).
        var (a, b, c) = (Drops(before), Drops(middle), Drops(after));
        Assert.Equal(a.Count, b.Count);
        Assert.True(a.Count > 20);
        var between = Enumerable.Range(0, a.Count).Count(i => Vector2.Distance((a[i] + c[i]) / 2, b[i]) < 1e-3f);
        Assert.True(between > a.Count * 0.9, $"{between} of {a.Count} drops were drawn between steps.");
        // (A drop wrapping round the area jumps, so it's drawn where it lands: one step early, out of view.)
        Assert.True(Enumerable.Range(0, a.Count).Count(i => a[i] != c[i]) > a.Count * 0.9, "The rain didn't fall.");

        List<Vector2> Drops(RenderFrame frame) =>
            frame.Sprites.Where(s => s.Entity == sky).Select(s => new Vector2(s.World.M41, s.World.M42)).ToList();
    }

    // ----- Ropes, wires and debug shapes -----

    private static Entity Part(Scene scene, string name, Vector2 at)
    {
        var part = new Entity(name);
        part.Transform.Position = new Vector3(at, 0);
        part.AddComponent<Slider>();
        scene.Add(part);
        return part;
    }

    [Fact]
    public void Ropes_and_wires_are_drawn_between_the_steps_their_ends_moved_in()
    {
        var scene = new Scene();
        var left = Part(scene, "Left", new Vector2(0, 0));
        var right = Part(scene, "Right", new Vector2(0, 2));
        right.AddComponent<Rope>().To = "Left";
        var wire = left.AddComponent<Wire>();
        // (Into a port nothing here has: powering it off would hide it.)
        (wire.From, wire.Input) = ("Right", "Brightness");
        var game = Play(scene);

        var (before, middle, after) = ThreeFrames(game);

        float RopeAt(RenderFrame frame) => frame.Sprites.First(s => s.Entity == right).World.Translation.X;
        float WireAt(RenderFrame frame) => frame.Sprites.First(s => s.Entity == left).World.Translation.X;
        Assert.True(RopeAt(after) > RopeAt(before));
        Assert.Equal((RopeAt(before) + RopeAt(after)) / 2, RopeAt(middle), 4);
        Assert.Equal((WireAt(before) + WireAt(after)) / 2, WireAt(middle), 4);
    }

    // Moves, then marks where it is with a debug line and circle, every step.
    private sealed class Marker : Behavior
    {
        public bool Extra { get; set; }

        protected override void Update()
        {
            Transform.Position += new Vector3(6 * Time.Delta, 0, 0);
            var at = new Vector2(Transform.Position.X, Transform.Position.Y);
            Debug.Line(at, at + Vector2.UnitY, Color.Red);
            Debug.Circle(at, 0.5f + (float)Time.Frame / 100);
            if (Extra && Time.Frame % 2 == 0)
                Debug.Text(at, "even");
        }
    }

    [Fact]
    public void Debug_shapes_drawn_each_step_keep_up_with_what_theyre_drawn_over()
    {
        var scene = new Scene();
        var marked = new Entity("Marked");
        marked.AddComponent<Sprite>();
        marked.AddComponent<Marker>();
        scene.Add(marked);
        var game = Play(scene);
        game.Advance(Step);
        game.Advance(Step);

        game.Advance(Step / 2);
        var frame = Draw(game);

        // The line starts where its sprite is drawn, not where the sprite is.
        var sprite = frame.Sprites.Single(s => s.Entity == marked).World.Translation.X;
        Assert.Equal(0.15f, sprite, 4);
        var line = frame.Sprites.Single(s => s.Entity.Name == "Screen" && Math.Abs(s.World.M12) > 0.5f);
        Assert.Equal(sprite, line.World.Translation.X, 4);
    }

    [Fact]
    public void Debug_shapes_that_differ_from_the_step_before_are_drawn_as_they_are()
    {
        var scene = new Scene();
        var marked = new Entity("Marked");
        marked.AddComponent<Marker>().Extra = true;
        scene.Add(marked);
        var game = Play(scene);
        game.Advance(Step);
        game.Advance(Step);

        // The second step drew a label the first didn't: its shapes can't be matched up, so they're drawn as they are.
        game.Advance(Step / 2);
        var line = Draw(game).Sprites.Single(s => s.Entity.Name == "Screen" && Math.Abs(s.World.M12) > 0.5f);
        Assert.Equal(0.2f, line.World.Translation.X, 4);
    }

    // ----- Fades -----

    private static SpriteDraw ScreenCover(RenderFrame frame) => frame.Sprites.Last(s => s.Entity.Name == "Screen");

    [Fact]
    public void A_scene_change_fade_and_a_screen_flash_are_drawn_between_steps()
    {
        var scene = new Scene();
        var game = Play(scene);
        game.SceneLoader = _ => new Scene();
        game.Advance(Step);
        game.LoadScene("Next", fade: 1);
        game.Advance(Step);

        var (before, middle, after) = ThreeFrames(game);
        Assert.True(ScreenCover(after).Tint.A > ScreenCover(before).Tint.A);
        Assert.Equal((ScreenCover(before).Tint.A + ScreenCover(after).Tint.A) / 2, ScreenCover(middle).Tint.A, 4);

        var flashed = Play(new Scene());
        flashed.Advance(Step);
        flashed.FlashScreen(Color.White, 1);
        flashed.Advance(Step);
        (before, middle, after) = ThreeFrames(flashed);
        Assert.True(ScreenCover(after).Tint.A < ScreenCover(before).Tint.A);
        Assert.Equal((ScreenCover(before).Tint.A + ScreenCover(after).Tint.A) / 2, ScreenCover(middle).Tint.A, 4);
    }

    [Fact]
    public void Sprites_and_text_fading_are_drawn_between_steps_and_a_new_one_as_it_is()
    {
        var scene = new Scene();
        var ghost = new Entity("Ghost");
        ghost.AddComponent<Sprite>();
        ghost.AddComponent<Text>().Value = "Boo";
        scene.Add(ghost);
        var game = Play(scene);
        game.Advance(Step);
        game.FadeTo(ghost, 0, 1, Ease.Linear);

        var (before, middle, after) = ThreeFrames(game);
        foreach (var text in new[] { false, true })
        {
            float Alpha(RenderFrame frame) => frame.Sprites.Single(s => s.Entity == ghost && (s.Text is not null) == text).Tint.A;
            Assert.True(Alpha(after) < Alpha(before));
            Assert.Equal((Alpha(before) + Alpha(after)) / 2, Alpha(middle), 4);
        }

        // Something spawned already fading is drawn as it is.
        var spawned = new Entity("Spawned");
        spawned.AddComponent<Sprite>();
        game.Stepped += (_, _) =>
        {
            if (spawned.Scene is null)
            {
                game.Spawn(spawned);
                spawned.GetComponent<Sprite>()!.Tint = Color.White with { A = 0.5f };
            }
        };
        game.Advance(Step);
        game.Advance(Step / 2);
        Assert.Equal(0.5f, Draw(game).Sprites.Single(s => s.Entity == spawned).Tint.A);
    }

    // ----- First person -----

    // Nods its head up 12 degrees a step.
    private sealed class Nodder : Behavior
    {
        protected override void Update() => Entity!.GetComponent<FirstPersonMovement>()!.Pitch += 12;
    }

    private static (Game Game, Entity Player) Corridor(bool nodding)
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        var view = camera.AddComponent<FirstPersonView>();
        (view.Columns, view.Minimap, view.Fog, view.Crosshair) = (32, false, Color.Transparent, Color.Transparent);
        scene.Add(camera);
        var walls = new Entity("Walls");
        walls.Transform.Position = new Vector3(0, 1, 0);
        walls.AddComponent<Tilemap>().Tiles = ["000000", ".....0", "000000"];
        scene.Add(walls);
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(0.5f, -0.5f, 0);
        player.AddComponent<CircleCollider>().Radius = 0.25f;
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<FirstPersonMovement>().MouseLook = false;
        if (nodding)
            player.AddComponent<Nodder>();
        scene.Add(player);
        return (Play(scene), player);
    }

    [Fact]
    public void A_first_person_view_looks_up_and_down_between_steps()
    {
        var (nodding, _) = Corridor(nodding: true);
        nodding.Advance(Step);
        nodding.Advance(Step);
        nodding.Advance(Step / 2);
        var between = Draw(nodding);

        // The same view looking up 18 degrees (halfway from 12 to 24), drawn as it is.
        var (still, player) = Corridor(nodding: false);
        still.Advance(Step);
        still.Advance(Step);
        player.GetComponent<FirstPersonMovement>()!.Pitch = 18;
        still.ShowAsIs();
        var expected = Draw(still);

        Assert.Equal(expected.Sprites.Count, between.Sprites.Count);
        for (var i = 0; i < expected.Sprites.Count; i++)
            Assert.True(Vector3.Distance(expected.Sprites[i].World.Translation, between.Sprites[i].World.Translation) < 1e-3f, $"Draw {i} is somewhere else.");
    }
}
