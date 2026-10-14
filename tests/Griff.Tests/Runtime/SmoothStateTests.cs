using System.Numerics;
using Griff.Input;
using Griff.Modules.FallingBlocks;
using Griff.Modules.FirstPerson;
using Griff.Modules.Match3;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// The rest of what moves once a step, drawn between the game's last two steps like transforms are: match-3 gems sliding,
/// shrinking, falling and landing, a falling-block piece sliding and falling and full rows clearing, a bar filling and its
/// trail draining, a first-person weapon's kick, muzzle flash and coming up, the camera's zoom, turn and shake, lights,
/// sprites and text growing, the screen effects, and a system's HUD kept still on the screen as the camera moves.
/// </summary>
public sealed class SmoothStateTests
{
    private const double Step = 1 / 60.0;
    private const float Aspect = 16 / 9f;

    private static RenderFrame Draw(Game game) => SceneRenderer.Build(game, Aspect);

    private static Game Play(Scene scene) => new(scene, new InputState());

    /// <summary>
    /// One step of a game, drawn right after it (what moves as the step before left it), half a step on, and as it is (as
    /// the step left it), with what was probed after it.
    /// </summary>
    private readonly record struct Stepped<T>(RenderFrame At, RenderFrame Half, RenderFrame End, T Probe);

    // Takes `count` steps one at a time from a game that hasn't been drawn between steps yet, drawing each three ways.
    private static List<Stepped<T>> Steps<T>(Game game, int count, Func<T> probe)
    {
        var steps = new List<Stepped<T>>();
        game.Advance(Step / 2);
        for (var i = 0; i < count; i++)
        {
            game.Advance(Step / 2);
            var value = probe();
            var at = Draw(game);
            game.ShowAsIs();
            var end = Draw(game);
            game.Advance(Step / 2);
            steps.Add(new Stepped<T>(at, Draw(game), end, value));
        }

        return steps;
    }

    // Where a draw is and how big, each way.
    private static float[] Shape(SpriteDraw draw) => [draw.World.M41, draw.World.M42, draw.World.M11, draw.World.M22];

    // True if every draw of `half` is halfway between the same draw of `from` and `to` (its middle and size, each way); false
    // if any isn't. How many changed at all comes back in `changed`.
    private static bool Halfway(List<SpriteDraw> from, List<SpriteDraw> half, List<SpriteDraw> to, out int changed)
    {
        changed = 0;
        var halfway = true;
        for (var i = 0; i < half.Count; i++)
        {
            var (a, m, b) = (Shape(from[i]), Shape(half[i]), Shape(to[i]));
            var moved = false;
            for (var c = 0; c < a.Length; c++)
            {
                moved |= MathF.Abs(a[c] - b[c]) > 1e-4f;
                halfway &= MathF.Abs(m[c] - (a[c] + b[c]) / 2) < 1e-4f;
            }

            if (moved)
                changed++;
        }

        return halfway;
    }

    // True if every draw of `half` is between the same draw of `from` and `to`, each way; how many are strictly between
    // (somewhere along the way, at neither end) comes back in `inside`.
    private static bool Between(List<SpriteDraw> from, List<SpriteDraw> half, List<SpriteDraw> to, out int inside)
    {
        inside = 0;
        var between = true;
        for (var i = 0; i < half.Count; i++)
        {
            var (a, m, b) = (Shape(from[i]), Shape(half[i]), Shape(to[i]));
            var strictly = false;
            for (var c = 0; c < a.Length; c++)
            {
                between &= m[c] >= MathF.Min(a[c], b[c]) - 1e-4f && m[c] <= MathF.Max(a[c], b[c]) + 1e-4f;
                strictly |= MathF.Abs(a[c] - b[c]) > 1e-4f && MathF.Abs(m[c] - a[c]) > 1e-5f && MathF.Abs(m[c] - b[c]) > 1e-5f;
            }

            if (strictly)
                inside++;
        }

        return between;
    }

    private static List<SpriteDraw> Of(RenderFrame frame, Entity entity) => frame.Sprites.Where(s => s.Entity == entity).ToList();

    private static Vector2 Flat(SpriteDraw draw) => new(draw.World.M41, draw.World.M42);

    // ----- Boards -----

    [Fact]
    public void A_match3_boards_gems_slide_shrink_fall_and_land_between_steps()
    {
        var scene = new Scene();
        var entity = new Entity("Board");
        // No line yet; swapping the bottom right gem with the one above it lines up the bottom row.
        entity.AddComponent<Tilemap>().Tiles = ["012", "120", "001"];
        var board = entity.AddComponent<MatchBoard>();
        (board.Columns, board.Rows, board.Gems, board.Deal, board.Refill, board.ShuffleWhenStuck) = (3, 3, "0-2", false, false, false);
        // A third of a step past a whole number of steps, so the step each one ends in is part of the way along.
        (board.SwapTime, board.ClearTime, board.FallTime) = (0.16f, 0.16f, 0.16f);
        (board.Background, board.Highlight) = (Color.Transparent, Color.Transparent);
        scene.Add(entity);
        var game = Play(scene);
        board.Swap(2, 1, 2, 2);

        var steps = Steps(game, 50, () => board.IsBusy);
        Assert.False(steps[^1].Probe);
        var (insideSteps, landings, vanished) = (0, 0, false);
        for (var k = 1; k < steps.Count - 1; k++)
        {
            var (from, half, to) = (Of(steps[k].At, entity), Of(steps[k].Half, entity), Of(steps[k].End, entity));
            if (half.Count == 9 && to.Count == 6)
            {
                // The bottom row cleared away in this step: it shrinks the rest of the way, drawn under the rest.
                Assert.Equal(9, from.Count);
                Assert.All(half.Take(3), (gem, i) => Assert.InRange(gem.World.M11, 1e-4f, from[i].World.M11 - 1e-4f));
                vanished = true;
                continue;
            }

            if (from.Count != half.Count || half.Count != to.Count)
                continue;
            Assert.True(Between(from, half, to, out var inside), $"Step {k} isn't drawn between the steps.");
            if (inside > 0)
                insideSteps++;

            // A gem that came home in this step (moving into it, still after it) has the rest of the way to go halfway through.
            var after = Of(steps[k + 1].End, entity);
            var moving = Enumerable.Range(0, to.Count).Where(i => Vector2.Distance(Flat(from[i]), Flat(to[i])) > 1e-4f).ToList();
            if (moving.Count > 0 && after.Count == to.Count && Enumerable.Range(0, to.Count).All(i => Vector2.Distance(Flat(to[i]), Flat(after[i])) < 1e-5f))
            {
                Assert.All(moving, i => Assert.True(Vector2.Distance(Flat(half[i]), Flat(to[i])) > 1e-5f, $"Step {k}: gem {i} is already home."));
                landings++;
            }
        }

        Assert.True(vanished);
        // The slide in, then the two rows above falling into the gap.
        Assert.Equal(2, landings);
        Assert.True(insideSteps > 20, $"Only {insideSteps} steps were drawn part way.");
    }

    [Fact]
    public void A_falling_piece_falls_and_slides_between_steps_and_full_rows_shrink_away_as_the_rest_drop()
    {
        var scene = new Scene();
        var entity = new Entity("Board");
        // The O comes in in the middle and lands in the gap at the bottom, filling the bottom row.
        entity.AddComponent<Tilemap>().Tiles = ["....", "....", "....", "0...", "....", "0..0"];
        var board = entity.AddComponent<FallingBoard>();
        (board.Columns, board.Rows, board.Pieces, board.Preview, board.Seed) = (4, 6, "O", 0, 1);
        (board.FallTime, board.LockTime, board.ClearTime) = (0.05f, 0.1f, 0.16f);
        (board.Background, board.Ghost) = (Color.Transparent, Color.Transparent);
        scene.Add(entity);
        var game = Play(scene);

        var steps = Steps(game, 60, () => board.PieceAt);
        var (fell, shrank, dropped) = (0, 0, false);
        for (var k = 1; k < steps.Count; k++)
        {
            var (from, half, to) = (Of(steps[k].At, entity), Of(steps[k].Half, entity), Of(steps[k].End, entity));
            if (half.Count == 11 && to.Count == 7)
            {
                // The bottom row went: its blocks shrink the rest of the way away, and the three blocks above it drop a row,
                // halfway there. The next piece comes in as it is.
                Assert.Equal(11, from.Count);
                Assert.All(half.Take(4), (block, i) => Assert.InRange(block.World.M11, 1e-4f, from[i].World.M11 - 1e-4f));
                for (var i = 0; i < 3; i++)
                {
                    Assert.Equal(to[i].World.M41, half[4 + i].World.M41, 4);
                    Assert.Equal(to[i].World.M42 + 1, from[4 + i].World.M42, 4);
                    Assert.Equal(to[i].World.M42 + 0.5f, half[4 + i].World.M42, 4);
                }

                Assert.True(Halfway(from[^4..], half[^4..], to[^4..], out var spawned) && spawned == 0);
                dropped = true;
                continue;
            }

            if (from.Count != half.Count || half.Count != to.Count)
                continue;
            Assert.True(Halfway(from, half, to, out var changed), $"Step {k} isn't drawn halfway between the steps.");
            if (changed == 0)
                continue;
            if (steps[k].Probe is null)
                shrank++;
            else
                fell++;
        }

        Assert.True(fell >= 4, $"The piece fell between steps in only {fell}.");
        Assert.True(shrank >= 8, $"The full row shrank in only {shrank} steps.");
        Assert.True(dropped);

        // A piece slid over is drawn halfway there too.
        var slid = new Scene();
        var other = new Entity("Board");
        other.AddComponent<Tilemap>();
        var sliding = other.AddComponent<FallingBoard>();
        (sliding.Columns, sliding.Rows, sliding.Pieces, sliding.Preview, sliding.FallTime) = (6, 8, "O", 0, 10);
        (sliding.Background, sliding.Ghost) = (Color.Transparent, Color.Transparent);
        slid.Add(other);
        var playing = Play(slid);
        playing.Tick(Step);
        sliding.Move(1);
        var moved = Steps(playing, 3, () => sliding.PieceAt);
        Assert.Equal((3, 0), moved[0].Probe);
        Assert.True(Halfway(Of(moved[0].At, other), Of(moved[0].Half, other), Of(moved[0].End, other), out var across));
        Assert.Equal(4, across);
    }

    // ----- Bars and weapons -----

    // Drains a bar like a timer, then drops it to a fifth all at once, leaving a trail that drains after a moment.
    private sealed class Drain : Behavior
    {
        private int _steps;

        protected override void Update()
        {
            var bar = Entity!.GetComponent<Bar>()!;
            if (++_steps == 30)
                bar.Value = 0.2f;
            else if (_steps < 30)
                bar.Value -= 0.5f * Time.Delta;
        }
    }

    [Fact]
    public void A_bar_fills_and_its_trail_drains_between_steps()
    {
        var scene = new Scene();
        var entity = new Entity("Timer");
        entity.AddComponent<Bar>();
        entity.AddComponent<Drain>();
        scene.Add(entity);
        var game = Play(scene);
        var bar = entity.GetComponent<Bar>()!;

        var steps = Steps(game, 90, () => (bar.Fraction, bar.TrailFraction));
        var (draining, trailing) = (0, 0);
        for (var k = 1; k < steps.Count; k++)
        {
            var (from, half, to) = (Of(steps[k].At, entity), Of(steps[k].Half, entity), Of(steps[k].End, entity));
            if (from.Count != half.Count || half.Count != to.Count)
                continue;
            Assert.True(Halfway(from, half, to, out var changed), $"Step {k} isn't drawn halfway between the steps.");
            if (changed > 0 && steps[k].Probe.Fraction < steps[k - 1].Probe.Fraction)
                draining++;
            if (changed > 0 && steps[k].Probe.Fraction == steps[k - 1].Probe.Fraction)
                trailing++;
        }

        Assert.True(draining > 20, $"The bar drained between steps in only {draining}.");
        Assert.True(trailing > 10, $"The trail drained between steps in only {trailing}.");
    }

    // Takes out its second weapon a few steps in, and fires it once it's up.
    private sealed class Trigger : Behavior
    {
        private int _steps;

        protected override void Update()
        {
            var second = Entity!.GetComponents<Weapon>().Last();
            if (++_steps == 3)
                Game.Equip(second);
            else if (_steps == 25)
                Game.Fire(second);
        }
    }

    [Fact]
    public void A_first_person_weapon_comes_up_kicks_and_flashes_between_steps()
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
        player.AddComponent<Weapon>();
        player.AddComponent<Weapon>().Kick = 0.3f;
        player.AddComponent<Trigger>();
        scene.Add(player);
        var game = Play(scene);

        // Everything else stands still, so whatever changes is the weapon.
        var steps = Steps(game, 45, () => 0);
        var (raising, kicking, flashing) = (0, 0, false);
        for (var k = 1; k < steps.Count; k++)
        {
            var (from, half, to) = (steps[k].At.Sprites.ToList(), steps[k].Half.Sprites.ToList(), steps[k].End.Sprites.ToList());
            if (from.Count != half.Count || half.Count != to.Count)
            {
                // It fired: the flash shows from the first frame after, growing in.
                if (to.Count > from.Count && !flashing)
                {
                    Assert.Equal(to.Count, half.Count);
                    flashing = true;
                }

                continue;
            }

            Assert.True(Halfway(from, half, to, out var changed), $"Step {k} isn't drawn halfway between the steps.");
            if (changed > 0 && k < 20)
                raising++;
            if (changed > 0 && k > 24)
                kicking++;
        }

        Assert.True(raising >= 10, $"It came up between steps in only {raising}.");
        Assert.True(kicking >= 6, $"It kicked back between steps in only {kicking}.");
        Assert.True(flashing);
    }

    // ----- The camera, lights, sizes and the screen -----

    // Zooms out and turns the camera, and grows a sprite, text and a light, a little every step.
    private sealed class Grow : Behavior
    {
        protected override void Update()
        {
            if (Entity!.GetComponent<Camera>() is { } camera)
            {
                camera.Size += 2 * Time.Delta;
                Transform.Rotation += new Vector3(0, 0, 20 * Time.Delta);
            }

            if (Entity.GetComponent<Sprite>() is { } sprite)
                sprite.Size += new Vector2(1, 0.5f) * Time.Delta;
            if (Entity.GetComponent<Text>() is { } text)
                text.Size += Time.Delta;
            if (Entity.GetComponent<Light>() is { } light)
                light.Radius += 3 * Time.Delta;
            if (Entity.GetComponent<ScreenEffects>() is { } effects)
                effects.Saturation -= 0.5f * Time.Delta;
        }
    }

    private static (float Size, float Turn, Vector2 Center) ViewOf(RenderFrame frame)
    {
        var m = frame.ViewProjection;
        Matrix4x4.Invert(m, out var toWorld);
        var center = Vector4.Transform(new Vector4(0, 0, 0, 1), toWorld);
        return (1 / MathF.Sqrt(m.M12 * m.M12 + m.M22 * m.M22), MathF.Atan2(m.M21, m.M11), new Vector2(center.X, center.Y));
    }

    [Fact]
    public void The_cameras_zoom_turn_and_shake_lights_sizes_and_screen_effects_change_between_steps()
    {
        var scene = new Scene();
        var cameraEntity = new Entity("Main Camera");
        var camera = cameraEntity.AddComponent<Camera>();
        camera.Ambient = new Color(0.2f, 0.2f, 0.3f);
        cameraEntity.AddComponent<ScreenEffects>();
        cameraEntity.AddComponent<Grow>();
        scene.Add(cameraEntity);
        var thing = new Entity("Thing");
        thing.AddComponent<Sprite>();
        thing.AddComponent<Text>().Value = "Hi";
        var light = thing.AddComponent<Light>();
        light.Flicker = 0.5f;
        thing.AddComponent<Grow>();
        scene.Add(thing);
        var game = Play(scene);
        game.Tick(Step);
        camera.Shake(0.5f, 10);

        var steps = Steps(game, 12, () => 0);
        for (var k = 1; k < steps.Count; k++)
        {
            var (from, half, to) = (steps[k].At, steps[k].Half, steps[k].End);
            var (a, m, b) = (ViewOf(from), ViewOf(half), ViewOf(to));
            Assert.Equal((a.Size + b.Size) / 2, m.Size, 4);
            Assert.Equal((a.Turn + b.Turn) / 2, m.Turn, 4);
            Assert.True(Vector2.Distance((a.Center + b.Center) / 2, m.Center) < 1e-4f, $"Step {k}: the shake isn't drawn halfway.");
            Assert.True(a.Center != b.Center, "The camera isn't shaking.");

            Assert.True(Halfway(Of(from, thing), Of(half, thing), Of(to, thing), out var grown), $"Step {k}: the sprite and text aren't drawn halfway.");
            Assert.Equal(2, grown);

            var (la, lm, lb) = (from.Lights.Single(), half.Lights.Single(), to.Lights.Single());
            Assert.Equal((la.Radius + lb.Radius) / 2, lm.Radius, 4);
            Assert.Equal((la.Color.R + lb.Color.R) / 2, lm.Color.R, 4);
            Assert.Equal((from.Look.Saturation + to.Look.Saturation) / 2, half.Look.Saturation, 4);
        }

        // Held still, it's all drawn as it is.
        game.ShowAsIs();
        var still = Draw(game);
        Assert.Equal(camera.Size, ViewOf(still).Size, 4);
        Assert.Equal(light.Radius, still.Lights.Single().Radius, 4);
    }

    // A HUD laid out over the camera's view as it is each step: a box a quarter of the way up and right of the middle.
    private sealed class Hud : GameSystem
    {
        public static readonly Entity Box = new("HUD Box");

        private Vector2 _center;
        private float _half;

        protected override void LayOutHud()
        {
            var camera = SceneRenderer.FindCamera(Game.Scene)!;
            _center = new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
            _half = camera.GetComponent<Camera>()!.Size;
        }

        protected override void Draw(FrameDrawing frame)
        {
            List<SpriteDraw> draws = [Quads.Rect(Box, _center + new Vector2(0.5f * frame.Aspect, 0.25f) * _half, Vector2.One, Color.White)];
            frame.PinToView(draws, _center, _half);
            frame.AddOverWorld(draws);
        }
    }

    // Pans the camera right as it zooms out.
    private sealed class Pan : Behavior
    {
        protected override void Update()
        {
            Transform.Position += new Vector3(6 * Time.Delta, 0, 0);
            Entity!.GetComponent<Camera>()!.Size += 2 * Time.Delta;
        }
    }

    [Fact]
    public void A_systems_hud_pinned_to_the_view_keeps_still_on_the_screen_as_the_camera_moves_zooms_and_shakes_between_steps()
    {
        var scene = new Scene();
        var cameraEntity = new Entity("Main Camera");
        var camera = cameraEntity.AddComponent<Camera>();
        cameraEntity.AddComponent<Pan>();
        scene.Add(cameraEntity);
        var game = Play(scene);
        game.AddSystem(new Hud());
        game.Tick(Step);
        camera.Shake(0.5f, 10);

        foreach (var step in Steps(game, 10, () => 0))
        {
            foreach (var frame in new[] { step.At, step.Half, step.End })
            {
                var box = frame.Sprites.Single(s => s.Entity == Hud.Box);
                var clip = Vector4.Transform(new Vector4(box.World.M41, box.World.M42, 0, 1), frame.ViewProjection);
                Assert.Equal(0.5f, clip.X, 4);
                Assert.Equal(0.25f, clip.Y, 4);
            }
        }
    }
}
