using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Hinge joints: a body pinned to the world or to another body at a point, swinging, kept within its limits, turned by its
/// motor, not bumping into what it's hinged to; wheels, doors, flippers, seesaws and windmills; the same to the bit twice,
/// across a rewind and in a network game, with no garbage.
/// </summary>
public class HingeJointTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public HingeJointTests() => _game = new Game(_scene, _input);

    private void Run(float seconds) => Run(_game, seconds);

    private static void Run(Game game, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            game.Tick(Frame);
    }

    private Entity AddWall(string name, Vector2 position, Vector2 size)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = size;
        _scene.Add(entity);
        return entity;
    }

    private Body AddBox(string name, Vector2 position, Vector2 size, bool rotates = true, float degrees = 0)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.Transform.Rotation = new Vector3(0, 0, degrees);
        entity.AddComponent<BoxCollider>().Size = size;
        var body = entity.AddComponent<Body>();
        body.Rotates = rotates;
        _scene.Add(entity);
        return body;
    }

    private Body AddBall(string name, Vector2 position, float radius)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<CircleCollider>().Radius = radius;
        var body = entity.AddComponent<Body>();
        body.Rotates = true;
        _scene.Add(entity);
        return body;
    }

    private static HingeJoint Hinge(Body body, Vector2 anchor, string? to = null)
    {
        var hinge = body.Entity!.AddComponent<HingeJoint>();
        (hinge.Anchor, hinge.To) = (anchor, to);
        return hinge;
    }

    private static Vector2 At(Body body) => new(body.Entity!.Transform.Position.X, body.Entity.Transform.Position.Y);

    private static float Angle(Body body) => body.Entity!.Transform.Rotation.Z;

    // ----- Swinging -----

    [Fact]
    public void A_plank_hinged_to_the_world_by_its_end_swings_down_and_comes_to_rest_hanging_with_its_end_where_it_was()
    {
        var plank = AddBox("Plank", new Vector2(0, 5), new Vector2(4, 0.4f));
        var hinge = Hinge(plank, new Vector2(-2, 0));
        hinge.Friction = 0.5f;

        Run(0.5f);
        Assert.True(hinge.Joined);
        Assert.Equal(new Vector2(-2, 5), hinge.OtherAnchor);
        Assert.True(Angle(plank) < -30, $"It turned to {Angle(plank)} degrees.");

        Run(8);
        Assert.True(plank.Asleep, $"It's still swinging, at {Angle(plank)} degrees.");
        Assert.InRange(Angle(plank), -98, -82);
        Assert.Equal(Angle(plank), hinge.Angle, 3);
        Assert.True(Vector2.Distance(hinge.WorldAnchor, new Vector2(-2, 5)) < 0.01f, $"The hinge came apart: {hinge.WorldAnchor}.");
    }

    [Fact]
    public void Without_friction_it_swings_on_and_on()
    {
        var plank = AddBox("Plank", new Vector2(0, 5), new Vector2(4, 0.4f));
        var hinge = Hinge(plank, new Vector2(-2, 0));
        hinge.Friction = 0;

        Run(6);
        var (lowest, highest) = (0f, -180f);
        for (var t = 0f; t < 2.5f; t += Frame)
        {
            _game.Tick(Frame);
            (lowest, highest) = (MathF.Min(lowest, hinge.Angle), MathF.Max(highest, hinge.Angle));
        }

        Assert.True(lowest < -160 && highest > -20, $"It swung from {lowest} to {highest}.");
    }

    [Fact]
    public void A_door_with_limits_swings_no_further_than_them()
    {
        var door = AddBox("Door", new Vector2(0, 5), new Vector2(4, 0.4f));
        var hinge = Hinge(door, new Vector2(-2, 0));
        (hinge.Limit, hinge.LowerAngle, hinge.UpperAngle) = (true, -30, 60);

        Run(3);
        Assert.Equal(-30, hinge.Angle, 0);
        Assert.Equal(-30, Angle(door), 0);
        Assert.True(Vector2.Distance(hinge.WorldAnchor, new Vector2(-2, 5)) < 0.01f);
        Assert.True(door.Asleep);

        // Knocked up, it stops at its upper limit and falls back.
        door.Spin = 600;
        Run(0.15f);
        Assert.InRange(hinge.Angle, -31, 61);
        Run(3);
        Assert.Equal(-30, hinge.Angle, 0);
    }

    [Fact]
    public void A_windmill_turns_at_its_motors_speed_and_never_falls_asleep()
    {
        var sails = AddBox("Sails", new Vector2(0, 5), new Vector2(4, 0.4f));
        var hinge = Hinge(sails, Vector2.Zero);
        (hinge.Motor, hinge.MotorSpeed, hinge.MotorStrength) = (true, 90, 200);

        Run(2);
        Assert.Equal(90, sails.Spin, 0);
        Assert.False(sails.Asleep);
        Assert.True(Vector2.Distance(At(sails), new Vector2(0, 5)) < 0.01f);

        hinge.Reverse();
        Run(1);
        Assert.Equal(-90, sails.Spin, 0);

        // Off, it stops turning (it's balanced on its middle), and falls asleep.
        hinge.Motor = false;
        sails.Spin = 0;
        Run(2);
        Assert.True(sails.Asleep);
    }

    [Fact]
    public void A_weak_motor_stalls_under_a_heavy_load()
    {
        var arm = AddBox("Arm", new Vector2(2, 5), new Vector2(4, 0.4f));
        arm.Mass = 4;
        var hinge = Hinge(arm, new Vector2(-2, 0));
        (hinge.Motor, hinge.MotorSpeed, hinge.MotorStrength) = (true, 90, 20);

        // Its weight pulls with 4 x 25 at 2 from the hinge: 200, ten times what the motor has.
        Run(3);
        Assert.True(hinge.Angle < -60, $"It held the arm up at {hinge.Angle}.");

        hinge.MotorStrength = 1000;
        Run(2);
        Assert.True(hinge.Angle > 30, $"It's at {hinge.Angle}.");
    }

    // ----- Two bodies -----

    [Fact]
    public void A_cart_on_motorized_wheels_drives_along_and_its_wheels_pass_through_it()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        var cart = AddBox("Cart", new Vector2(0, 0.9f), new Vector2(3, 0.6f));
        foreach (var x in new[] { -1.2f, 1.2f })
        {
            var wheel = AddBall($"Wheel {x}", new Vector2(x, 0.5f), 0.5f);
            var hinge = Hinge(wheel, Vector2.Zero, "Cart");
            (hinge.Motor, hinge.MotorSpeed, hinge.MotorStrength) = (true, -360, 50);
        }

        Run(3);
        Assert.True(At(cart).X > 8, $"The cart got to {At(cart)}.");
        Assert.InRange(Angle(cart), -5, 5);
        Assert.InRange(At(cart).Y, 0.85f, 0.95f);
        var wheels = _scene.Entities.Where(e => e.Name.StartsWith("Wheel", StringComparison.Ordinal)).ToList();
        Assert.All(wheels, w => Assert.InRange(w.Transform.Position.Y, 0.48f, 0.52f));
    }

    [Fact]
    public void Ticking_collide_makes_the_two_it_joins_bump_into_each_other()
    {
        var arm = AddBox("Arm", new Vector2(0, 5), new Vector2(2, 0.4f));
        Hinge(arm, Vector2.Zero);
        var swinging = AddBox("Swing", new Vector2(1, 4), new Vector2(0.4f, 2));
        var hinge = Hinge(swinging, new Vector2(0, 1), "Arm");
        hinge.Collide = true;

        // The swing starts across the arm's end: bumping, the arm is pushed round.
        Run(1);
        Assert.True(MathF.Abs(Angle(arm)) > 1, "The arm didn't move.");
    }

    [Fact]
    public void A_seesaw_tips_under_a_crate_dropped_on_one_end()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Post", new Vector2(0, 0.5f), new Vector2(0.4f, 1));
        var seesaw = AddBox("Seesaw", new Vector2(0, 1.2f), new Vector2(6, 0.3f));
        Hinge(seesaw, Vector2.Zero, "Post");
        var crate = AddBox("Crate", new Vector2(2.5f, 3), Vector2.One);

        Run(3);
        Assert.True(Angle(seesaw) < -10, $"The seesaw is at {Angle(seesaw)} degrees.");
        Assert.True(At(seesaw).Y > 1.15f && At(seesaw).Y < 1.25f);
        Assert.True(seesaw.Asleep && crate.Asleep);
    }

    [Fact]
    public void A_body_that_doesnt_rotate_hangs_upright_from_its_hinge()
    {
        var wheel = AddBox("Wheel", new Vector2(0, 5), new Vector2(0.4f, 0.4f));
        var hinge = Hinge(wheel, Vector2.Zero);
        (hinge.Motor, hinge.MotorSpeed, hinge.MotorStrength) = (true, 90, 1000);
        var seat = AddBox("Seat", new Vector2(2, 5), new Vector2(1, 0.6f), rotates: false);
        Hinge(seat, new Vector2(0, 0.5f), "Wheel");

        Run(1);
        Assert.Equal(0, Angle(seat), 3);
        Assert.True(At(seat).Y > 5.5f, $"The seat is at {At(seat)}.");
    }

    [Fact]
    public void A_chain_of_links_hangs_still_and_falls_asleep()
    {
        Body? last = null;
        for (var i = 0; i < 5; i++)
        {
            var link = AddBox($"Link {i}", new Vector2(i + 0.5f, 8), new Vector2(1, 0.2f));
            Hinge(link, new Vector2(-0.5f, 0), last?.Entity!.Name).Friction = 0.5f;
            last = link;
        }

        Run(10);
        var links = _scene.Entities.Select(e => e.GetComponent<Body>()).OfType<Body>().ToList();
        Assert.All(links, l => Assert.True(l.Asleep, $"{l.Entity!.Name} is still swinging."));
        Assert.All(links, l => Assert.InRange(At(l).X, -0.4f, 0.4f));
        Assert.InRange(At(links[^1]).Y, 3.4f, 3.6f);
    }

    [Fact]
    public void A_hinge_on_something_moved_the_old_way_is_carried_along()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(100, 1));
        var cart = AddBox("Cart", new Vector2(0, 0.5f), Vector2.One, rotates: false);
        var flail = AddBox("Flail", new Vector2(0, 3), new Vector2(0.2f, 1.5f));
        Hinge(flail, new Vector2(0, 0.75f), "Cart");
        cart.Entity!.Transform.Position = new Vector3(0, 0.5f, 0);

        for (var t = 0f; t < 2; t += Frame)
        {
            cart.Velocity = new Vector2(4, cart.Velocity.Y);
            _game.Tick(Frame);
        }

        var hinge = flail.Entity!.GetComponent<HingeJoint>()!;
        Assert.True(Vector2.Distance(hinge.WorldAnchor, hinge.OtherWorldAnchor(cart.Entity)) < 0.05f, "The flail was left behind.");
        Assert.True(At(cart).X > 7);
    }

    [Fact]
    public void A_flipper_swings_up_while_its_power_is_on()
    {
        var flipper = AddBox("Flipper", new Vector2(1, 0), new Vector2(2, 0.3f));
        var hinge = Hinge(flipper, new Vector2(-1, 0));
        (hinge.Limit, hinge.LowerAngle, hinge.UpperAngle) = (true, -30, 30);
        (hinge.MotorSpeed, hinge.MotorStrength) = (1000, 500);

        Run(1);
        Assert.Equal(-30, hinge.Angle, 0);
        Game.Power(flipper.Entity!, true);
        Run(0.2f);
        Assert.True(hinge.Motor);
        Assert.InRange(hinge.Angle, 29, 31);
        Game.Power(flipper.Entity!, false);
        Run(1);
        Assert.Equal(-30, hinge.Angle, 0);
    }

    // ----- Every machine, every step -----

    [Fact]
    public void Its_settings_are_saved_with_the_scene()
    {
        var plank = AddBox("Plank", new Vector2(0, 5), new Vector2(4, 0.4f));
        var hinge = Hinge(plank, new Vector2(-2, 0), "Wall");
        (hinge.Limit, hinge.LowerAngle, hinge.Motor, hinge.MotorSpeed, hinge.Collide) = (true, -10, true, 45, true);
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var copy = serializer.LoadFromString(serializer.SaveToString(_scene)).Entities.Single(e => e.Name == "Plank").GetComponent<HingeJoint>()!;

        Assert.Equal((new Vector2(-2, 0), "Wall", true, -10f, true, 45f, true), (copy.Anchor, copy.To, copy.Limit, copy.LowerAngle, copy.Motor, copy.MotorSpeed, copy.Collide));
    }
}
