using System.Numerics;
using Griff.Input;
using Griff.Modules.PhysicsGames;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>
/// Pinball: flippers snapping up on their hinges and hitting the ball hard and the same every time, never letting a fast
/// ball through; bumpers and kickers knocking it away, scoring and flashing; the plunger pulled by holding, a stick or the
/// mouse and shooting as hard as it was pulled; nudges, warnings and the tilt; drop targets and lanes in sets; the drain
/// losing, saving and serving balls; the Pinball Table preset played; focus and turns; to the bit twice, across a rewind
/// and in a network game, with no garbage.
/// </summary>
public sealed class PinballTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<(string Message, object? Data)> _heard = [];

    public PinballTests()
    {
        _game = new Game(_scene, _input);
        foreach (var message in new[] { "Score", "Flipped", "Danger", "Tilted", "BallLost", "BallSaved", "GameOver", "Launched" })
            _game.On(message, data => _heard.Add((message, data)));
    }

    private void Run(float seconds) => Run(_game, _input, seconds);

    private static void Run(Game game, InputState input, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            Step(game, input);
    }

    private void Step() => Step(_game, _input);

    private static void Step(Game game, InputState input)
    {
        game.Tick(Frame);
        input.EndFrame();
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    private int Score => _heard.Where(h => h.Message == "Score").Sum(h => h.Data is int points ? points : 1);

    private Entity Add(Entity entity)
    {
        _scene.Add(entity);
        return entity;
    }

    private Body AddBall(Vector2 at, Vector2 velocity = default)
    {
        var ball = Add(PinballParts.Ball(at));
        var body = ball.GetComponent<Body>()!;
        body.Velocity = velocity;
        return body;
    }

    private Flipper AddFlipper(FlipperSide side, Vector2 pivot) => Add(PinballParts.Flipper(side, pivot, pivot: true)).GetComponent<Flipper>()!;

    private Entity AddWall(Vector2 at, Vector2 size)
    {
        var wall = new Entity("Wall");
        wall.Transform.Position = new Vector3(at, 0);
        wall.AddComponent<BoxCollider>().Size = size;
        return Add(wall);
    }

    private static Entity Table(Scene scene)
    {
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        scene.Add(camera);
        var preset = Griff.Editing.EntityPreset.All.Single(p => p.Id == "Entity.CreatePinballTable");
        var entity = new Entity(preset.EntityName(scene));
        preset.Configure(entity, scene);
        var children = preset.CreateChildren(scene).ToList();
        var edits = preset.EditScene(scene).ToList();
        scene.Add(entity);
        foreach (var child in children)
            scene.Add(child, entity);
        foreach (var edit in edits)
            edit.Apply();
        return entity;
    }

    private static Entity Named(Scene scene, string name) => scene.Entities.Single(e => e.Name == name);

    // How far a flipper has swung up from resting (degrees, up positive whichever side it's on).
    private static float Swung(Flipper flipper) => flipper.Entity!.GetComponent<HingeJoint>()!.Angle * flipper.UpWay;

    // ----- Flippers -----

    [Fact]
    public void A_flipper_snaps_up_while_its_button_is_held_and_drops_as_its_let_go()
    {
        var left = AddFlipper(FlipperSide.Left, new Vector2(-2, 0));
        var right = AddFlipper(FlipperSide.Right, new Vector2(2, 0));
        Run(0.2f);
        Assert.InRange(Swung(left), -1, 1);
        Assert.False(left.Up);

        _input.SetKey(Key.LeftShift, true);
        Step();
        Step();
        Step();
        Assert.True(left.Up);
        Assert.InRange(Swung(left), 54, 56);
        Assert.InRange(Swung(right), -1, 1);
        Run(0.5f);
        Assert.InRange(Swung(left), 54, 56);

        _input.SetKey(Key.LeftShift, false);
        _input.SetKey(Key.Slash, true);
        Run(0.1f);
        Assert.False(left.Up);
        Assert.True(right.Up);
        Assert.InRange(Swung(left), -1, 1);
        Assert.InRange(Swung(right), 54, 56);
        Assert.Equal(1, left.Flips);
        Assert.Equal(1, right.Flips);

        // Its actions: Flip swings it up for a moment, Raise holds it until Lower; and a wire's power holds it up too.
        _input.SetKey(Key.Slash, false);
        _game.Do(left.Entity!, "Flip");
        Run(0.1f);
        Assert.True(left.Up);
        Run(0.2f);
        Assert.False(left.Up);
        _game.Do(left.Entity!, "Raise");
        Run(0.5f);
        Assert.True(left.Up);
        _game.Do(left.Entity!, "Lower");
        Run(0.1f);
        Assert.False(left.Up);
        Assert.InRange(Swung(left), -1, 1);
    }

    [Fact]
    public void The_mouse_or_a_finger_on_one_side_flips_that_sides_flipper()
    {
        var left = AddFlipper(FlipperSide.Left, new Vector2(-2, 0));
        var right = AddFlipper(FlipperSide.Right, new Vector2(2, 0));
        Run(0.1f);
        _input.MouseWorld = new Vector2(3, 4);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(0.1f);
        Assert.True(right.Up);
        Assert.False(left.Up);
        _input.SetMouseButton(MouseButton.Left, false);
        _input.MouseWorld = new Vector2(-6, -2);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(0.1f);
        Assert.True(left.Up);
        Assert.False(right.Up);
    }

    [Fact]
    public void A_flipper_hits_the_ball_harder_further_along_it_and_the_same_every_time()
    {
        static (Vector2 Velocity, float Highest) Hit(float wait)
        {
            var scene = new Scene();
            var input = new InputState();
            var game = new Game(scene, input);
            scene.Add(PinballParts.Flipper(FlipperSide.Left, new Vector2(-2, 0), pivot: true));
            var ball = PinballParts.Ball(new Vector2(-1.7f, 0.45f));
            scene.Add(ball);
            Run(game, input, wait);
            input.SetKey(Key.LeftShift, true);
            var fastest = Vector2.Zero;
            var highest = float.MinValue;
            for (var i = 0; i < 40; i++)
            {
                Step(game, input);
                var velocity = ball.GetComponent<Body>()!.Velocity;
                if (velocity.Length() > fastest.Length())
                    fastest = velocity;
                highest = MathF.Max(highest, At(ball).Y);
            }

            return (fastest, highest);
        }

        // The ball rolls down the resting flipper towards its tip: flipped later, it's further along and flies faster.
        var near = Hit(0.1f);
        var far = Hit(0.5f);
        Assert.True(far.Velocity.Length() > near.Velocity.Length() + 10, $"Near the pivot {near.Velocity.Length():0.0}, near the tip {far.Velocity.Length():0.0}.");
        Assert.True(far.Velocity.Length() > 35, $"Near the tip it only hits {far.Velocity.Length():0.0}.");
        Assert.True(far.Velocity.Y > 20, $"It goes {far.Velocity}.");
        Assert.True(far.Highest > 10);
        Assert.Equal(far, Hit(0.5f));
    }

    [Fact]
    public void A_flipper_made_of_its_components_alone_swings_up_to_its_sides_limit()
    {
        var entity = new Entity("Flipper");
        entity.AddComponent<BoxCollider>().Size = new Vector2(2, 0.3f);
        entity.AddComponent<Body>();
        entity.AddComponent<HingeJoint>().Anchor = new Vector2(-1, 0);
        var flipper = entity.AddComponent<Flipper>();
        Add(entity);
        Run(0.2f);
        Assert.InRange(Swung(flipper), -1, 1);
        _input.SetKey(Key.Z, true);
        Run(0.2f);
        Assert.True(entity.GetComponent<Body>()!.Rotates);
        Assert.InRange(Swung(flipper), 53, 57);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(150)]
    public void A_fast_ball_never_goes_through_a_flipper_resting_or_swinging(float speed)
    {
        // Thrown down at the paddle from above, the flipper pressed a step or two either side of it arriving, along it.
        for (var along = 0.4f; along <= 1.6f; along += 0.4f)
        {
            for (var press = -3; press <= 6; press++)
            {
                var scene = new Scene();
                var input = new InputState();
                var game = new Game(scene, input);
                var flipper = PinballParts.Flipper(FlipperSide.Left, new Vector2(-2, 0), pivot: true);
                scene.Add(flipper);
                game.Tick(Frame);
                var hinge = flipper.GetComponent<HingeJoint>()!;
                var pivot = hinge.WorldAnchor;
                var aim = pivot + new Vector2(MathF.Cos(-30 * MathF.PI / 180), MathF.Sin(-30 * MathF.PI / 180)) * along;
                var arrive = 10;
                var ball = PinballParts.Ball(aim + new Vector2(0, speed * Frame * arrive + 0.5f));
                scene.Add(ball);
                var body = ball.GetComponent<Body>()!;
                (body.Velocity, body.GravityScale) = (new Vector2(0, -speed), 0);
                for (var i = 0; i < 40; i++)
                {
                    if (i == arrive + press)
                        input.SetKey(Key.LeftShift, true);
                    Step(game, input);

                    // Never below the paddle's line within its length.
                    var turn = HingeJoint.TurnOf(flipper) * MathF.PI / 180;
                    var line = new Vector2(MathF.Cos(turn), MathF.Sin(turn));
                    var from = At(ball) - pivot;
                    var length = Vector2.Dot(from, line);
                    var side = line.X * from.Y - line.Y * from.X;
                    Assert.False(length > 0 && length < PinballArt.FlipperLength && side < 0,
                        $"At {speed} along {along:0.0}, pressed {press} steps from arriving, the ball went through to {At(ball)} on step {i}.");
                }
            }
        }
    }

    [Fact]
    public void Untick_controllable_or_focus_something_else_and_it_ignores_the_buttons()
    {
        var left = AddFlipper(FlipperSide.Left, new Vector2(-2, 0));
        left.Controllable = false;
        _input.SetKey(Key.LeftShift, true);
        Run(0.2f);
        Assert.False(left.Up);
        _game.Do(left.Entity!, "Focus");
        Run(0.1f);
        Assert.True(left.Controllable);
        Assert.True(left.Up);
    }

    // ----- Bumpers and kickers -----

    [Fact]
    public void A_bumper_knocks_the_ball_away_hard_lights_up_and_scores()
    {
        var bumper = new Entity("Bumper");
        PinballParts.MakeBumper(bumper, Vector2.Zero);
        Add(bumper);
        var bumps = bumper.GetComponent<Bumper>()!;
        var ball = AddBall(new Vector2(-1.5f, 0.3f), new Vector2(4, 0));
        ball.GravityScale = 0;
        var lit = false;
        for (var i = 0; i < 30 && bumps.Hits == 0; i++)
        {
            Step();
            lit |= bumps.Lit;
        }

        Assert.Equal(1, bumps.Hits);
        Assert.True(bumps.Lit);
        Assert.True(ball.Velocity.Length() >= bumps.Strength - 0.01f, $"It was knocked away at {ball.Velocity}.");
        Assert.True(ball.Velocity.X < -10, $"It was knocked {ball.Velocity}, not away.");
        Assert.Equal(100, Score);
        Run(0.4f);
        Assert.False(bumps.Lit);
        Assert.Equal(1, bumps.Hits);

        // Off, it's just a wall.
        bumps.On = false;
        ball.Velocity = new Vector2(6, 0);
        Run(1);
        Assert.Equal(1, bumps.Hits);
    }

    [Fact]
    public void A_kicker_knocks_away_only_what_touches_its_kicking_side()
    {
        var kicker = Add(PinballParts.Kicker(FlipperSide.Left, Vector2.Zero));
        var kicks = kicker.GetComponent<Bumper>()!;

        // Against its back: nothing.
        var back = AddBall(new Vector2(-1.5f, 0), new Vector2(5, 0));
        back.GravityScale = 0;
        Run(0.6f);
        Assert.Equal(0, kicks.Hits);
        _scene.Remove(back.Entity!);

        // Onto its rubber, from up and to the right: knocked away up and to the right.
        var front = AddBall(new Vector2(1.2f, 0.9f), new Vector2(-4, -3));
        front.GravityScale = 0;
        for (var i = 0; i < 60 && kicks.Hits == 0; i++)
            Step();
        Assert.Equal(1, kicks.Hits);
        Assert.True(front.Velocity.X > 8 && front.Velocity.Y > 4, $"Kicked {front.Velocity}.");
        Assert.Equal(10, Score);
    }

    // ----- The plunger -----

    private (Plunger Plunger, Body Ball) AddPlunger()
    {
        var entity = new Entity("Plunger");
        PinballParts.MakePlunger(entity);
        Add(entity);
        AddWall(new Vector2(-0.7f, 5), new Vector2(0.2f, 12));
        AddWall(new Vector2(0.7f, 5), new Vector2(0.2f, 12));
        var ball = AddBall(new Vector2(0, PinballParts.BallRadius + 0.01f));
        return (entity.GetComponent<Plunger>()!, ball);
    }

    private float Shot(Body ball, Action shoot)
    {
        shoot();
        var fastest = 0f;
        for (var i = 0; i < 10; i++)
        {
            Step();
            fastest = MathF.Max(fastest, ball.Velocity.Y);
        }

        return fastest;
    }

    [Fact]
    public void Held_longer_the_plunger_shoots_the_ball_harder()
    {
        var (plunger, ball) = AddPlunger();
        Run(0.3f);
        Assert.True(plunger.Loaded);
        _input.SetKey(Key.Space, true);
        Run(0.25f);
        Assert.InRange(plunger.Pull, 0.2f, 0.3f);
        Assert.True(plunger.Pulling);
        var gentle = Shot(ball, () => _input.SetKey(Key.Space, false));
        Assert.InRange(gentle, plunger.SpeedFor(0.2f), plunger.SpeedFor(0.3f));
        Assert.False(plunger.Loaded);
        Assert.Equal(0, plunger.Pull);

        // Back down onto it, held for longer than it takes to pull it all the way: full power.
        Run(3);
        Assert.True(plunger.Loaded);
        _input.SetKey(Key.Down, true);
        Run(1.5f);
        Assert.Equal(1, plunger.Pull);
        var hard = Shot(ball, () => _input.SetKey(Key.Down, false));
        Assert.InRange(hard, plunger.Power - 0.5f, plunger.Power + 0.01f);
        Assert.Equal(2, plunger.Launches);
    }

    [Fact]
    public void A_stick_pulls_the_plunger_as_far_as_its_pushed_and_shoots_as_it_springs_back()
    {
        var (plunger, ball) = AddPlunger();
        Run(0.3f);
        _input.SetGamepad([], Vector2.Zero, new Vector2(0, -0.7f));
        Run(0.2f);
        var pushed = -_input.RightStick.Y;
        Assert.True(pushed > 0.5f);
        Assert.Equal(pushed, plunger.Pull);
        var speed = Shot(ball, () => _input.SetGamepad([], Vector2.Zero, Vector2.Zero));
        Assert.InRange(speed, plunger.SpeedFor(pushed) - 0.5f, plunger.SpeedFor(pushed) + 0.01f);

        // Let back slowly, it doesn't shoot.
        Run(3);
        for (var push = 0.5f; push >= 0; push -= 0.05f)
        {
            _input.SetGamepad([], Vector2.Zero, new Vector2(0, -push));
            Step();
        }

        Run(0.3f);
        Assert.True(plunger.Loaded);
        Assert.Equal(1, plunger.Launches);
    }

    [Fact]
    public void The_mouse_held_on_the_plunger_pulls_it_and_its_launch_action_shoots_at_full_power()
    {
        var (plunger, ball) = AddPlunger();
        Run(0.3f);
        _input.MouseWorld = new Vector2(0.2f, -0.5f);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(0.5f);
        Assert.InRange(plunger.Pull, 0.45f, 0.55f);
        var speed = Shot(ball, () => _input.SetMouseButton(MouseButton.Left, false));
        Assert.InRange(speed, plunger.SpeedFor(0.45f), plunger.SpeedFor(0.55f));

        Run(3);
        Assert.True(plunger.Loaded);
        var full = Shot(ball, () => _game.Do(plunger.Entity!, "Launch"));
        Assert.InRange(full, plunger.Power - 0.5f, plunger.Power + 0.01f);
    }

    [Fact]
    public void The_plunger_draws_itself_pulled_back_as_far_as_its_pulled()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        Add(camera);
        var (plunger, _) = AddPlunger();
        Run(0.2f);
        var drawn = Griff.Rendering.SceneRenderer.Build(_game, 16 / 9f).Sprites.Count(d => d.Entity == plunger.Entity);
        Assert.True(drawn >= 8, $"It drew {drawn} pieces.");
        var top = Griff.Rendering.SceneRenderer.Build(_game, 16 / 9f).Sprites.Where(d => d.Entity == plunger.Entity).Max(d => d.World.M42);
        _input.SetKey(Key.Space, true);
        Run(1.1f);
        var pulledTop = Griff.Rendering.SceneRenderer.Build(_game, 16 / 9f).Sprites.Where(d => d.Entity == plunger.Entity).Max(d => d.World.M42);
        Assert.True(pulledTop < top - plunger.Travel * 0.8f, $"Its knob went from {top} to {pulledTop}.");
    }

    [Fact]
    public void Pieces_made_from_the_menu_go_into_the_picked_table_not_into_another_piece()
    {
        var table = Table(_scene);
        var flipper = Named(_scene, "Left Flipper");
        var lane = Named(_scene, "Lanes").Children[0];
        var bumper = Griff.Editing.EntityPreset.All.Single(p => p.Id == "Entity.CreateBumper");
        Assert.Same(table, bumper.InsideOf(flipper));
        Assert.Same(table, bumper.InsideOf(lane));
        Assert.Same(table, bumper.InsideOf(Named(_scene, "Lanes")));
        Assert.Same(table, bumper.InsideOf(table));
        Assert.Null(bumper.InsideOf(null));

        // A plunger comes with a ball on it (inside it), unless there's one already, and shoots it.
        var scene = new Scene();
        var input = new InputState();
        var game = new Game(scene, input);
        var preset = Griff.Editing.EntityPreset.All.Single(p => p.Id == "Entity.CreatePlunger");
        var entity = new Entity(preset.EntityName(scene));
        preset.Configure(entity, scene);
        var children = preset.CreateChildren(scene).ToList();
        scene.Add(entity);
        foreach (var child in children)
            scene.Add(child, entity);
        var ball = entity.Children.Single();
        Run(game, input, 0.3f);
        Assert.True(entity.GetComponent<Plunger>()!.Loaded);
        game.Do(entity, "Launch");
        Run(game, input, 0.2f);
        Assert.True(At(ball).Y > -1, $"The ball's at {At(ball)}.");
        Assert.Empty(preset.CreateChildren(scene));
    }

    // ----- Nudging and tilting -----

    [Fact]
    public void A_nudge_pushes_the_ball_and_too_many_close_together_tilt_the_table()
    {
        var table = Add(new Entity("Table"));
        var tilt = table.AddComponent<Tilt>();
        (tilt.Within, tilt.WarningMessage, tilt.TiltMessage) = ("Table", "Danger", "Tilted");
        var left = Add(PinballParts.Flipper(FlipperSide.Left, new Vector2(-2, -3), pivot: true)).GetComponent<Flipper>()!;
        _scene.Remove(left.Entity!);
        _scene.Add(left.Entity!, table);
        var bumper = new Entity("Bumper");
        PinballParts.MakeBumper(bumper, new Vector2(3, 3));
        _scene.Add(bumper, table);
        var ball = PinballParts.Ball(new Vector2(0, 2));
        _scene.Add(ball, table);
        var body = ball.GetComponent<Body>()!;
        body.GravityScale = 0;
        Run(0.1f);

        // One nudge: the ball's pushed up; no warning.
        _input.SetKey(Key.N, true);
        Step();
        _input.SetKey(Key.N, false);
        Assert.InRange(body.Velocity.Y, tilt.NudgeStrength - 0.01f, tilt.NudgeStrength + 0.01f);
        Assert.Equal(1, tilt.Nudges);
        Assert.Empty(_heard);

        // Holding left as it's nudged pushes it that way too.
        body.Velocity = Vector2.Zero;
        Run(2);
        _input.SetKey(Key.Left, true);
        _input.SetKey(Key.N, true);
        Step();
        _input.SetKey(Key.N, false);
        _input.SetKey(Key.Left, false);
        Assert.True(body.Velocity.X < -1 && body.Velocity.Y > 1, $"Pushed {body.Velocity}.");

        // Quick nudges: a warning each, then the tilt.
        Run(2);
        for (var i = 0; i < 4; i++)
        {
            _game.Do(table, "Nudge");
            Run(0.2f);
        }

        Assert.Equal(2, _heard.Count(h => h.Message == "Danger"));
        Assert.Single(_heard, h => h.Message == "Tilted");
        Assert.True(tilt.Tilted);

        // Tilted: the flipper won't go up and the bumper's dead.
        _input.SetKey(Key.LeftShift, true);
        body.Velocity = new Vector2(3, 1) * 2;
        Run(1.5f);
        Assert.False(left.Up);
        Assert.Equal(0, bumper.GetComponent<Bumper>()!.Hits);

        // Put right, it all works again.
        _game.Do(table, "Reset");
        Run(0.1f);
        Assert.False(tilt.Tilted);
        Assert.True(left.Up);
    }

    // ----- Targets and lanes -----

    [Fact]
    public void Drop_targets_drop_when_hit_and_score_more_once_the_whole_set_is_down()
    {
        var bank = Add(new Entity("Drop Targets"));
        foreach (var target in PinballParts.DropTargets(Vector2.Zero, upright: true))
            _scene.Add(target, bank);
        var targets = bank.Children.Select(e => e.GetComponent<Target>()!).ToList();
        var ball = AddBall(new Vector2(2, -0.75f), new Vector2(-8, 0));
        ball.GravityScale = 0;
        Run(0.6f);
        Assert.True(targets[0].Down && targets[0].Lit);
        Assert.Equal(250, Score);
        Assert.True(ball.Velocity.X > 0, $"It didn't bounce off: {ball.Velocity}.");

        // Through where the first was, the ball goes on past.
        ball.Entity!.Transform.Position = new Vector3(2, -0.75f, 0);
        ball.Velocity = new Vector2(-8, 0);
        Run(0.6f);
        Assert.True(ball.Entity!.Transform.WorldPosition.X < -0.5f, $"The ball's at {At(ball.Entity!)}.");
        Assert.Equal(1, targets[0].Hits);

        foreach (var y in new[] { 0f, 0.75f })
        {
            ball.Entity!.Transform.Position = new Vector3(2, y, 0);
            ball.Velocity = new Vector2(-8, 0);
            Run(0.6f);
        }

        Assert.All(targets, t => Assert.True(t.Down));
        Assert.Equal(3 * 250 + 1000, Score);
        Run(1.6f);
        Assert.All(targets, t => Assert.False(t.Down || t.Lit));
        Assert.All(targets, t => Assert.Equal(1, t.Hits));
    }

    [Fact]
    public void A_target_takes_a_hit_harder_than_its_min_hit()
    {
        var entity = new Entity("Target");
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.25f, 1);
        var target = entity.AddComponent<Target>();
        target.MinHit = 3;
        Add(entity);
        var ball = AddBall(new Vector2(1, 0), new Vector2(-1.5f, 0));
        ball.GravityScale = 0;
        Run(1);
        Assert.Equal(0, target.Hits);
        ball.Entity!.Transform.Position = new Vector3(1, 0, 0);
        ball.Velocity = new Vector2(-6, 0);
        Run(0.5f);
        Assert.Equal(1, target.Hits);
        Assert.True(target.Lit);
        Assert.False(target.Down);
    }

    [Fact]
    public void A_ball_rolling_through_a_lane_lights_it_once_and_the_flippers_move_the_lights_along()
    {
        var set = Add(new Entity("Lanes"));
        foreach (var lane in PinballParts.Lanes(Vector2.Zero, 2))
            _scene.Add(lane, set);
        var lanes = set.Children.Select(e => e.GetComponent<Lane>()!).ToList();
        Add(PinballParts.Flipper(FlipperSide.Left, new Vector2(-2, -6), pivot: true));
        Add(PinballParts.Flipper(FlipperSide.Right, new Vector2(2, -6), pivot: true));
        var ball = AddBall(new Vector2(-2, 2), new Vector2(0, -3));
        ball.GravityScale = 0;
        Run(1.5f);
        Assert.Equal([true, false, false], lanes.Select(l => l.Lit));
        Assert.Equal(1, lanes[0].Passes);
        Assert.Equal(100, Score);

        // The right flipper moves the light one lane right; the left one back (round from the end).
        _input.SetKey(Key.RightShift, true);
        Step();
        _input.SetKey(Key.RightShift, false);
        Assert.Equal([false, true, false], lanes.Select(l => l.Lit));
        Run(0.2f);
        _input.SetKey(Key.LeftShift, true);
        Step();
        _input.SetKey(Key.LeftShift, false);
        Run(0.2f);
        _input.SetKey(Key.LeftShift, true);
        Step();
        _input.SetKey(Key.LeftShift, false);
        Assert.Equal([false, false, true], lanes.Select(l => l.Lit));

        // All three lit: the set scores and goes out.
        _game.Do(set.Children[0], "Light");
        _game.Do(set.Children[1], "Light");
        Step();
        Assert.Equal(100 + 1000, Score);
        Run(1.1f);
        Assert.All(lanes, l => Assert.False(l.Lit));
    }

    // ----- The drain -----

    private (Drain Drain, Plunger Plunger, Body Ball) AddDrainAndPlunger(int balls = 3)
    {
        var (plunger, ball) = AddPlunger();
        var entity = new Entity("Drain");
        entity.Transform.Position = new Vector3(-5, -5, 0);
        var mouth = entity.AddComponent<BoxCollider>();
        (mouth.Size, mouth.IsTrigger) = (new Vector2(4, 1), true);
        var drain = entity.AddComponent<Drain>();
        (drain.Balls, drain.DrainMessage, drain.SaveMessage, drain.BallSave) = (balls, "BallLost", "BallSaved", 0);
        Add(entity);
        return (drain, plunger, ball);
    }

    private void DropIn(Body ball, Drain drain)
    {
        ball.Entity!.Transform.Position = new Vector3(At(drain.Entity!) + new Vector2(0, 2), 0);
        ball.Velocity = new Vector2(0, -5);
        Run(0.4f);
    }

    [Fact]
    public void A_drained_ball_is_served_on_the_plunger_and_the_last_ends_the_game()
    {
        var (drain, plunger, ball) = AddDrainAndPlunger();
        Run(0.3f);
        Assert.Equal(1, drain.Ball);
        Assert.Equal(3, drain.BallsLeft);
        DropIn(ball, drain);
        Assert.False(ball.Entity!.IsEnabled);
        Assert.Equal(2, drain.Ball);
        Assert.Equal(2, drain.BallsLeft);
        Assert.Equal(("BallLost", (object?)2), _heard.Last());

        // Served a second after it drained, resting on the plunger.
        Run(1);
        Assert.True(ball.Entity.IsEnabled);
        Run(0.3f);
        Assert.True(plunger.Loaded);
        Assert.True(Vector2.Distance(At(ball.Entity), plunger.Tip + new Vector2(0, PinballParts.BallRadius)) < 0.05f);

        // An extra ball, then the rest drain; the last is game over.
        _game.Do(drain.Entity!, "ExtraBall");
        Step();
        Assert.Equal(4, drain.Balls);
        for (var i = 0; i < 3; i++)
        {
            DropIn(ball, drain);
            Run(1.5f);
        }

        Assert.True(drain.GameOver);
        Assert.Equal(0, drain.BallsLeft);
        Assert.Contains(_heard, h => h.Message == "GameOver");
        Assert.False(ball.Entity.IsEnabled);
    }

    [Fact]
    public void A_ball_lost_soon_after_its_shot_is_saved_and_shot_again()
    {
        var (drain, plunger, ball) = AddDrainAndPlunger();
        drain.BallSave = 3;
        Run(0.3f);

        // Not shot yet: no ball save.
        Assert.False(drain.Saving);
        _game.Do(plunger.Entity!, "Launch");
        Run(0.1f);
        Assert.True(drain.Saving);
        DropIn(ball, drain);
        Assert.Equal(1, drain.Ball);
        Assert.Contains(_heard, h => h.Message == "BallSaved");

        // Back on the plunger and shot again by itself; this time the save runs out.
        Run(1.4f);
        Assert.True(ball.Velocity.Y > 20 || ball.Entity!.Transform.WorldPosition.Y > 3, $"Not shot again: {At(ball.Entity!)} at {ball.Velocity}.");
        Run(4);
        Assert.False(drain.Saving);
        DropIn(ball, drain);
        Assert.Equal(2, drain.Ball);
    }

    [Fact]
    public void With_other_balls_on_the_table_a_drained_ball_just_goes()
    {
        var (drain, _, _) = AddDrainAndPlunger();
        Run(0.3f);
        var second = AddBall(At(drain.Entity!) + new Vector2(0, 2), new Vector2(0, -5));
        Run(0.4f);
        Assert.Null(second.Entity!.Scene);
        Assert.Equal(1, drain.Ball);
        Assert.DoesNotContain(_heard, h => h.Message == "BallLost");
    }

    [Fact]
    public void Draining_puts_a_tilted_table_right()
    {
        var (drain, _, ball) = AddDrainAndPlunger();
        var tilt = drain.Entity!.AddComponent<Tilt>();
        Run(0.3f);
        _game.Do(drain.Entity!, "Tilt");
        Step();
        Assert.True(tilt.Tilted);
        DropIn(ball, drain);
        Assert.False(tilt.Tilted);
    }

    // ----- The table to play -----

    [Fact]
    public void Create_Pinball_Table_makes_a_table_to_play_straight_away()
    {
        var table = Table(_scene);
        var camera = Named(_scene, "Main Camera");
        Assert.Equal(PinballParts.CameraSize, camera.GetComponent<Camera>()!.Size);
        Assert.Equal(["Score", "Balls"], camera.Children.Select(e => e.Name));
        var balls = Named(_scene, "Balls").GetComponent<Counter>()!;
        var ball = Named(_scene, "Ball");
        var plunger = Named(_scene, "Plunger").GetComponent<Plunger>()!;
        var drain = Named(_scene, "Drain").GetComponent<Drain>()!;
        Run(0.5f);
        Assert.True(plunger.Loaded);
        Assert.Equal("Ball 1 of 3", camera.Children[1].GetComponent<Text>()!.Value);

        // Shot hard: up the lane, round the top and down through the bumpers and lanes, scoring.
        _input.SetKey(Key.Space, true);
        Run(1.1f);
        _input.SetKey(Key.Space, false);
        Run(0.5f);
        Assert.True(At(ball).Y > 4, $"The ball's at {At(ball)}.");
        Run(6);
        var score = camera.Children[0].GetComponent<Counter>()!;
        Assert.True(score.Value > 0, "Nothing scored.");

        // Nobody flips: sooner or later it drains (saved once), and the next comes onto the plunger.
        for (var i = 0; i < 40 && drain.Ball == 1; i++)
            Run(1);
        Assert.Equal(2, drain.Ball);
        Run(1.5f);
        Assert.True(plunger.Loaded);
        Assert.Equal("Ball 2 of 3", camera.Children[1].GetComponent<Text>()!.Value);
        Assert.Equal(table, ball.Parent);
    }

    [Theory]
    [InlineData(0.03f)]
    [InlineData(-0.03f)]
    public void A_ball_dropped_on_the_top_of_an_upright_wall_of_the_preset_rolls_off_it(float offset)
    {
        var table = Table(_scene);
        var walls = Named(_scene, "Walls");
        var uprights = walls.Children.Where(w => w.Children.Any(c => c.Name == "Cap")).ToList();
        Assert.Equal(["Lane Divider", "Post", "Post"], uprights.Select(w => w.Name).Order());

        foreach (var wall in uprights)
        {
            var cap = wall.Children.Single(c => c.Name == "Cap");
            var top = cap.Transform.WorldPosition.Y + cap.GetComponent<CircleCollider>()!.Radius;
            var x = cap.Transform.WorldPosition.X + offset;
            var ball = AddBall(new Vector2(x, top + 0.8f));
            Run(4);

            // Off it and down (as far as the drain, which takes it out of the scene), not resting on its top.
            if (ball.Entity!.Scene is null)
                continue;
            var at = At(ball.Entity!);
            Assert.True(at.Y < top - 0.5f, $"The ball rests on {wall.Name} at {at} (top {top}).");
            _scene.Remove(ball.Entity!);
            Run(0.1f);
        }

        Assert.NotNull(table);
    }

    // ----- To the bit, rewinds, network games and garbage -----

    private static Scene Played()
    {
        var scene = new Scene();
        Table(scene);
        return scene;
    }

    // Shoots, then flips both flippers now and then, and nudges once.
    private static void Play(Game game, InputState input, int from, int to)
    {
        for (var frame = from; frame < to; frame++)
        {
            input.SetKey(Key.Space, frame is >= 20 and < 80);
            input.SetKey(Key.LeftShift, frame % 50 is >= 10 and < 25);
            input.SetKey(Key.RightShift, frame % 70 is >= 30 and < 40);
            input.SetKey(Key.N, frame == 200);
            Step(game, input);
        }
    }

    private static Vector3[] Moving(Scene scene) => scene.Entities.Where(e => e.GetComponent<Body>() is { Frozen: false }).Select(e => e.Transform.Position).ToArray();

    [Fact]
    public void A_game_plays_out_the_same_to_the_last_bit_twice_and_across_a_rewind()
    {
        static (Vector3[] Moving, int Score) Whole()
        {
            var scene = Played();
            var input = new InputState();
            Play(new Game(scene, input), input, 0, 600);
            return (Moving(scene), Named(scene, "Score").GetComponent<Counter>()!.Value);
        }

        var first = Whole();
        var second = Whole();
        Assert.Equal(first.Moving, second.Moving);
        Assert.Equal(first.Score, second.Score);
        Assert.True(first.Score > 0);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = Played();
        var input = new InputState();
        var game = new Game(scene, input);
        var timeline = new Timeline(serializer, capacity: 100, framesPerSnapshot: 10);
        for (var frame = 0; frame < 300; frame++)
        {
            Play(game, input, frame, frame + 1);
            timeline.Record(scene, game.Time.Total);
        }

        Play(game, input, 300, 600);

        // Snapshot 24 is the scene as frame 250 ended: played on from there with the same buttons, it ends the same.
        var restored = timeline.Restore(24);
        var replayInput = new InputState();
        var replay = new Game(restored, replayInput);
        Play(replay, replayInput, 250, 600);
        Assert.Equal(Moving(scene), Moving(restored));
        Assert.Equal(Named(scene, "Score").GetComponent<Counter>()!.Value, Named(restored, "Score").GetComponent<Counter>()!.Value);
    }

    [Fact]
    public void In_a_network_game_a_players_buttons_work_the_table_on_the_host()
    {
        static Scene Networked()
        {
            var scene = Played();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>().Player = "";
            scene.Add(settings);
            return scene;
        }

        var world = new NetWorld(Networked);
        world.Run(1);
        var hostBall = Named(world.Host.Scene, "Ball");
        var keys = NetWorld.Keys(world.Client);
        keys.SetKey(Key.Space, true);
        world.Run(1);
        Assert.True(Named(world.Host.Scene, "Plunger").GetComponent<Plunger>()!.Pull > 0.8f);
        keys.SetKey(Key.Space, false);
        world.Run(0.6);
        Assert.True(At(hostBall).Y > 0, $"The host's ball is at {At(hostBall)}.");

        keys.SetKey(Key.Z, true);
        world.Run(0.3);
        Assert.True(Named(world.Host.Scene, "Left Flipper").GetComponent<Flipper>()!.Up);
        Assert.True(Named(world.Client.Scene, "Left Flipper").GetComponent<Flipper>()!.Up);
        var clientBall = Named(world.Client.Scene, "Ball");
        Assert.True(Vector2.Distance(At(clientBall), At(hostBall)) < 2, $"The player sees the ball at {At(clientBall)}, the host has it at {At(hostBall)}.");
    }

    [Fact]
    public void Knocks_and_their_messages_make_no_garbage()
    {
        // A ball bouncing back and forth between a bumper and a wall, the bumper scoring each knock.
        var bumper = new Entity("Bumper");
        PinballParts.MakeBumper(bumper, Vector2.Zero);
        Add(bumper);
        AddWall(new Vector2(4, 0), new Vector2(0.5f, 10));
        var ball = AddBall(new Vector2(2, 0), new Vector2(-5, 0));
        (ball.GravityScale, ball.Bounciness) = (0, 1);
        Run(2);
        var hits = bumper.GetComponent<Bumper>()!.Hits;
        var knocking = Allocations.PerCall(Step, 60);
        Assert.True(bumper.GetComponent<Bumper>()!.Hits > hits + 2);
        Assert.True(knocking == 0, $"A step allocated {knocking:N0} bytes knocking the ball about.");
    }

    [Fact]
    public void Losing_serving_and_multiball_make_no_garbage()
    {
        // On the Pinball Table: the ball lost into the drain, held, and served onto the plunger; and two extra balls of a
        // multiball falling in and going, the one on the plunger still in play. Once the table has settled not a step of it
        // allocates (the extra balls are new each time, so the steps spawning them aren't counted). Steps are counted on this
        // thread (and the workers' for it), and only those no garbage collection ran during: another thread's (other tests
        // running alongside) can add to this thread's count what it never allocated.
        var table = Table(_scene);
        var ball = Named(_scene, "Ball");
        var drain = Named(_scene, "Drain").GetComponent<Drain>()!;
        (drain.Balls, drain.BallSave) = (100_000, 0);
        // The HUD's "Ball 2 of 3" says something new each time a ball is lost, a new string, and pops (a tween): it's turned
        // off, so what's counted is the table's own work.
        Named(_scene, "Balls").IsEnabled = false;
        Run(0.5f);
        var mouth = At(drain.Entity!) - At(table);
        var (bytes, counted) = (0L, 0);

        void Measure(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                var (before, collections) = (Workers.AllocatedBytes(), GC.CollectionCount(0));
                Step();
                var allocated = Workers.AllocatedBytes() - before;
                if (GC.CollectionCount(0) == collections)
                    (bytes, counted) = (bytes + allocated, counted + 1);
            }
        }

        void Round()
        {
            var lost = drain.Ball;
            ball.Transform.Teleport(mouth + new Vector2(0, 0.3f));
            ball.GetComponent<Body>()!.Velocity = new Vector2(0, -5);
            Measure(30);
            Assert.False(ball.IsEnabled);
            Assert.Equal(lost + 1, drain.Ball);
            Measure(90);
            Assert.True(ball.IsEnabled);

            var extras = new List<Entity>();
            for (var i = 0; i < 2; i++)
            {
                var extra = PinballParts.Ball(mouth + new Vector2(i - 0.5f, 0.3f));
                extra.GetComponent<Body>()!.Velocity = new Vector2(0, -5);
                _game.Spawn(extra, table);
                extras.Add(extra);
                Step();
                Measure(3 + i * 20);
            }

            Assert.All(extras, extra => Assert.Null(extra.Scene));
            Assert.Equal(lost + 1, drain.Ball);
            Assert.True(ball.IsEnabled);
        }

        for (var i = 0; i < 3; i++)
            Round();

        // The runtime compiles what's run often again, better, as the game goes, which can take a while with other tests
        // keeping the machine busy: until then the quickly compiled code can allocate what the better doesn't.
        var least = (Bytes: long.MaxValue, Counted: 0);
        for (var attempt = 0; attempt < 20 && least.Bytes > 0; attempt++)
        {
            Thread.Sleep(attempt > 0 ? 50 : 0);
            (bytes, counted) = (0, 0);
            Round();
            Round();
            if (counted > 0 && bytes < least.Bytes)
                least = (bytes, counted);
        }

        Assert.True(least.Counted > 100, $"Only {least.Counted} steps were counted (garbage collections ran in the rest).");
        Assert.True(least.Bytes == 0, $"Losing, serving and multiball allocated {least.Bytes:N0} bytes in {least.Counted} steps.");
    }

    [Fact]
    public void Playing_makes_no_garbage()
    {
        Table(_scene);
        Run(0.5f);
        _input.SetKey(Key.Space, true);
        var pulling = Allocations.PerCall(Step, 20);
        Run(0.5f);
        _input.SetKey(Key.Space, false);
        Run(1);
        _input.SetKey(Key.LeftShift, true);
        _input.SetKey(Key.RightShift, true);
        var flipping = Allocations.PerCall(Step, 20);
        _input.SetKey(Key.LeftShift, false);
        var rolling = Allocations.PerCall(Step, 20);
        Assert.True(pulling == 0 && flipping == 0 && rolling == 0, $"A step allocated {pulling:N0} bytes pulling, {flipping:N0} flipping, {rolling:N0} rolling.");
    }
}
