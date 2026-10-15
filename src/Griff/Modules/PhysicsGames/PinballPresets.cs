using System.Numerics;
using Griff.Editing;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Creates a whole little pinball table to play straight away: walls, a plunger in its lane on the right with the ball on it,
/// two flippers over the drain, three bumpers, two kickers, three lanes along the top and a bank of three drop targets, the
/// tilt, and the score and ball shown beside it, with pictures and sounds. Hold Space (or the down arrow) and let go to shoot,
/// Shift or Z and / to flip, N to nudge. The camera is moved and widened to see all of it. All in one undo step.
/// </summary>
[Category("Physics Games")]
public sealed class PinballTablePreset : EntityPreset
{
    public override string Id => "Entity.CreatePinballTable";

    public override string Text => "Create Pinball Table";

    public override int Order => 36;

    public override string? Icon => "Gamepad";

    public override string EntityName(Scene scene) => PhysicsGameNames.Free(scene, "Pinball Table");

    public override Entity? InsideOf(Entity? selected) => null;

    public override IReadOnlyDictionary<string, byte[]> Files() => PinballArt.Files();

    private string _name = "Pinball Table";

    public override void Configure(Entity entity, Scene scene)
    {
        _name = entity.Name;
        var tilt = entity.AddComponent<Tilt>();
        (tilt.Within, tilt.NudgeSound, tilt.WarningSound, tilt.TiltSound) = (entity.Name, PinballArt.NudgeSound, PinballArt.WarningSound, PinballArt.TiltSound);
    }

    public override IEnumerable<Entity> CreateChildren(Scene scene) => PinballParts.Table(_name);

    public override IEnumerable<IEdit> EditScene(Scene scene)
    {
        if (SceneRenderer.FindCamera(scene) is not { } camera)
            yield break;
        yield return new FitCameraEdit(camera, PinballParts.CameraSize, Vector3.Zero);

        // The score and the ball, on the camera beside the table.
        if (!scene.Entities.Any(e => e.GetComponent<Counter>() is { Message: "Score" }))
            yield return new AddEntityEdit(scene, PinballParts.Readout(PhysicsGameNames.Free(scene, "Score"), new Vector2(-9.5f, 6), counter => counter.Format = "Score: {0}"), camera);
        yield return new AddEntityEdit(scene, PinballParts.Readout(PhysicsGameNames.Free(scene, "Balls"), new Vector2(-9.5f, 4.6f), counter =>
        {
            (counter.Message, counter.Track, counter.Output, counter.Format) = (null, "Drain", "Ball", "Ball {0} of {2}");
        }), camera);
    }
}

/// <summary>
/// Creates a pair of pinball flippers near the bottom of the view, each a paddle on a hinge with its limits set: the left one
/// flips on Left Shift, Z or the gamepad's left shoulder, the right one on Right Shift, / or the right shoulder. Put a ball
/// above them and play. All in one undo step.
/// </summary>
[Category("Physics Games")]
public sealed class FlippersPreset : EntityPreset
{
    public override string Id => "Entity.CreateFlippers";

    public override string Text => "Create Flippers";

    public override int Order => 37;

    public override string? Icon => "LeftRight";

    public override Entity? InsideOf(Entity? selected) => PinballParts.TableFor(selected);

    public override string EntityName(Scene scene) => PhysicsGameNames.Free(scene, "Flippers");

    public override IReadOnlyDictionary<string, byte[]> Files() => PinballArt.Files();

    public override void Configure(Entity entity, Scene scene) => entity.Transform.Position = new Vector3(0, -3, 0);

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        yield return PinballParts.Flipper(FlipperSide.Left, new Vector2(-2, 0));
        yield return PinballParts.Flipper(FlipperSide.Right, new Vector2(2, 0));
    }
}

/// <summary>
/// Creates a pop bumper: a round bumper that knocks the ball straight away from its middle as it flashes, pops and scores 100
/// (its Message, Score, for a Counter). All in one undo step.
/// </summary>
[Category("Physics Games")]
public sealed class BumperPreset : EntityPreset
{
    public override string Id => "Entity.CreateBumper";

    public override string Text => "Create Bumper";

    public override int Order => 38;

    public override string? Icon => "Star";

    public override Entity? InsideOf(Entity? selected) => PinballParts.TableFor(selected);

    public override string EntityName(Scene scene) => PhysicsGameNames.Free(scene, "Bumper");

    public override IReadOnlyDictionary<string, byte[]> Files() => PinballArt.Files();

    public override void Configure(Entity entity, Scene scene) => PinballParts.MakeBumper(entity, Vector2.Zero);
}

/// <summary>
/// Creates a pair of slingshot kickers: triangles whose rubber sides (facing up and in) knock the ball away hard and score
/// 10 each. All in one undo step.
/// </summary>
[Category("Physics Games")]
public sealed class KickersPreset : EntityPreset
{
    public override string Id => "Entity.CreateKickers";

    public override string Text => "Create Kickers";

    public override int Order => 39;

    public override string? Icon => "Bolt";

    public override Entity? InsideOf(Entity? selected) => PinballParts.TableFor(selected);

    public override string EntityName(Scene scene) => PhysicsGameNames.Free(scene, "Kickers");

    public override IReadOnlyDictionary<string, byte[]> Files() => PinballArt.Files();

    public override void Configure(Entity entity, Scene scene) => entity.Transform.Position = new Vector3(0, -1, 0);

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        yield return PinballParts.Kicker(FlipperSide.Left, new Vector2(-2.5f, 0));
        yield return PinballParts.Kicker(FlipperSide.Right, new Vector2(2.5f, 0));
    }
}

/// <summary>
/// Creates a plunger with a ball resting on it, ready to shoot: hold Space (or the down arrow, the gamepad's A, or the mouse on
/// it) to pull it back and let go, or pull the gamepad's right stick down and let it spring back. All in one undo step.
/// </summary>
[Category("Physics Games")]
public sealed class PlungerPreset : EntityPreset
{
    public override string Id => "Entity.CreatePlunger";

    public override string Text => "Create Plunger";

    public override int Order => 40;

    public override string? Icon => "MoveUp";

    public override string EntityName(Scene scene) => PhysicsGameNames.Free(scene, "Plunger");

    public override IReadOnlyDictionary<string, byte[]> Files() => PinballArt.Files();

    public override Entity? InsideOf(Entity? selected) => PinballParts.TableFor(selected);

    public override void Configure(Entity entity, Scene scene)
    {
        entity.Transform.Position = new Vector3(4, -3, 0);
        PinballParts.MakePlunger(entity);
    }

    // A ball resting on its tip (inside it, so it's where the tip is wherever the plunger goes), unless the scene has one.
    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        if (!scene.Entities.Any(e => e.GetComponent<CircleCollider>() is not null && e.GetComponent<Body>() is not null))
            yield return PinballParts.Ball(new Vector2(0, PinballParts.BallRadius + 0.01f));
    }
}

/// <summary>
/// Creates a bank of three drop targets: each the ball hits scores 250 and drops out of the way, and knocking down all three
/// scores 1,000 more and brings them back up. All in one undo step.
/// </summary>
[Category("Physics Games")]
public sealed class DropTargetsPreset : EntityPreset
{
    public override string Id => "Entity.CreateDropTargets";

    public override string Text => "Create Drop Targets";

    public override int Order => 41;

    public override string? Icon => "Flag";

    public override Entity? InsideOf(Entity? selected) => PinballParts.TableFor(selected);

    public override string EntityName(Scene scene) => PhysicsGameNames.Free(scene, "Drop Targets");

    public override IReadOnlyDictionary<string, byte[]> Files() => PinballArt.Files();

    public override IEnumerable<Entity> CreateChildren(Scene scene) => PinballParts.DropTargets(Vector2.Zero, upright: false);

    public override void Configure(Entity entity, Scene scene) => entity.Transform.Position = new Vector3(0, 2, 0);
}

/// <summary>
/// Creates three rollover lanes side by side: a ball rolling through one lights it and scores 100, lighting all three scores
/// 1,000 more, and the flippers move the lit ones along. All in one undo step.
/// </summary>
[Category("Physics Games")]
public sealed class LanesPreset : EntityPreset
{
    public override string Id => "Entity.CreateLanes";

    public override string Text => "Create Lanes";

    public override int Order => 42;

    public override string? Icon => "Lightbulb";

    public override Entity? InsideOf(Entity? selected) => PinballParts.TableFor(selected);

    public override string EntityName(Scene scene) => PhysicsGameNames.Free(scene, "Lanes");

    public override IReadOnlyDictionary<string, byte[]> Files() => PinballArt.Files();

    public override IEnumerable<Entity> CreateChildren(Scene scene) => PinballParts.Lanes(Vector2.Zero, 2.2f);

    public override void Configure(Entity entity, Scene scene) => entity.Transform.Position = new Vector3(0, 4, 0);
}

/// <summary>Sets a camera's size and moves it, as one step to undo.</summary>
internal sealed class FitCameraEdit(Entity camera, float size, Vector3 position) : IEdit
{
    private readonly float _size = camera.GetComponent<Camera>()?.Size ?? SceneRenderer.DefaultCameraSize;
    private readonly Vector3 _position = camera.Transform.Position;

    public string Description => "Fit the camera";

    public void Apply() => Set(Math.Max(size, _size), position with { Z = _position.Z });

    public void Revert() => Set(_size, _position);

    private void Set(float to, Vector3 at)
    {
        if (camera.GetComponent<Camera>() is { } view)
            view.Size = to;
        camera.Transform.Position = at;
    }
}

/// <summary>The pinball pieces the presets make, in the table's own units (a table 10 wide and 18 tall round its middle).</summary>
internal static class PinballParts
{
    public const float BallRadius = 0.3f;

    /// <summary>How big a camera needs to be (half its height) to see the whole table.</summary>
    public const float CameraSize = 10;

    // The playfield's sides, its middle (left of the shooter lane), and where the flippers turn.
    private const float Left = -5;
    private const float Right = 5;
    private const float Divider = 3.8f;
    private const float Middle = (Left + Divider - 0.15f) / 2;
    private const float PivotY = -6.5f;
    private const float PivotX = Middle - 2.025f;

    private static readonly Color Chrome = Color.FromBytes(0xA8, 0xB4, 0xC8);

    /// <summary>
    /// What a new piece goes inside, given what's picked: the picked entity or the nearest it's inside that isn't a piece
    /// (something with a collider or a body) or a set of them (a pair of flippers, a bank of targets): a table, or nothing.
    /// </summary>
    public static Entity? TableFor(Entity? selected)
    {
        var parent = selected;
        while (parent is not null && parent.GetComponent<Tilt>() is null &&
               (parent.GetComponent<Collider>() is not null || parent.GetComponent<Body>() is not null || parent.Children.Any(IsPiece)))
            parent = parent.Parent;
        return parent;

        static bool IsPiece(Entity child) => child.GetComponent<Flipper>() is not null || child.GetComponent<Bumper>() is not null ||
                                             child.GetComponent<Target>() is not null || child.GetComponent<Lane>() is not null ||
                                             child.GetComponent<Plunger>() is not null;
    }

    /// <summary>A whole table inside an entity called <paramref name="table"/>.</summary>
    public static IEnumerable<Entity> Table(string table)
    {
        var field = new Entity("Playfield");
        field.Transform.Position = new Vector3(0, 0, -1);
        var picture = field.AddComponent<Sprite>();
        (picture.Image, picture.Size, picture.Order) = (PinballArt.Playfield, new Vector2(10, 18), -20);
        yield return field;

        // The walls round it, the top corners that turn the ball off the shooter lane, the divider beside the lane, and the
        // guides down to the flippers.
        var walls = new Entity("Walls");
        foreach (var wall in new[]
                 {
                     Wall("Left Wall", new Vector2(Left - 0.2f, -9.6f), new Vector2(Left - 0.2f, 9.4f), 0.4f),
                     Wall("Right Wall", new Vector2(Right + 0.2f, -9.6f), new Vector2(Right + 0.2f, 9.4f), 0.4f),
                     Wall("Top Wall", new Vector2(Left - 0.4f, 9.2f), new Vector2(Right + 0.4f, 9.2f), 0.4f),
                     Wall("Top Left Corner", new Vector2(Left, 7.2f), new Vector2(Left + 1.8f, 9), 0.4f),
                     Wall("Top Right Corner", new Vector2(Right - 1.8f, 9), new Vector2(Right, 7.2f), 0.4f),
                     Wall("Lane Divider", new Vector2(Divider, -9.6f), new Vector2(Divider, 5), 0.3f),
                     Wall("Left Guide", new Vector2(Left, -4.4f), new Vector2(PivotX - 0.45f, PivotY + 0.4f), 0.25f),
                     Wall("Right Guide", new Vector2(Divider - 0.15f, -4.4f), new Vector2(2 * Middle - PivotX + 0.45f, PivotY + 0.4f), 0.25f),
                     Wall("Post", new Vector2(-1.8f, 6.9f), new Vector2(-1.8f, 7.9f), 0.2f),
                     Wall("Post", new Vector2(0.4f, 6.9f), new Vector2(0.4f, 7.9f), 0.2f),
                 })
            Put(walls, wall);
        yield return walls;

        yield return Flipper(FlipperSide.Left, new Vector2(PivotX, PivotY), pivot: true);
        yield return Flipper(FlipperSide.Right, new Vector2(2 * Middle - PivotX, PivotY), pivot: true);
        yield return Kicker(FlipperSide.Left, new Vector2(-3.9f, -2.9f));
        yield return Kicker(FlipperSide.Right, new Vector2(2 * Middle + 3.9f, -2.9f));

        var bumpers = new Entity("Bumpers");
        foreach (var at in new[] { new Vector2(-2.4f, 3.2f), new Vector2(1, 3.2f), new Vector2(Middle, 5.4f) })
        {
            var bumper = new Entity("Bumper");
            MakeBumper(bumper, at);
            Put(bumpers, bumper);
        }

        yield return bumpers;

        var lanes = new Entity("Lanes");
        foreach (var lane in Lanes(new Vector2(Middle, 7.4f), 2.2f))
            Put(lanes, lane);
        yield return lanes;

        var targets = new Entity("Drop Targets");
        foreach (var target in DropTargets(new Vector2(Left + 0.25f, 1.1f), upright: true))
            Put(targets, target);
        yield return targets;

        var plunger = new Entity("Plunger");
        plunger.Transform.Position = new Vector3((Divider + 0.15f + Right) / 2, -8, 0);
        MakePlunger(plunger);
        yield return plunger;
        yield return Ball(new Vector2((Divider + 0.15f + Right) / 2, -8 + BallRadius + 0.01f));

        // The drain under the flippers, and a floor under it.
        var drain = new Entity("Drain");
        drain.Transform.Position = new Vector3(Middle, -9.7f, 0);
        var mouth = drain.AddComponent<BoxCollider>();
        (mouth.Size, mouth.IsTrigger) = (new Vector2(Divider - Left, 1), true);
        var draining = drain.AddComponent<Drain>();
        (draining.Sound, draining.SaveSound, draining.Within) = (PinballArt.DrainSound, PinballArt.SaveSound, table);
        yield return drain;
        yield return Wall("Floor", new Vector2(Left - 0.4f, -10.6f), new Vector2(Right + 0.4f, -10.6f), 0.4f);
    }

    // Puts a child in an entity that isn't in a scene yet.
    private static void Put(Entity parent, Entity child)
    {
        parent.ChildList.Add(child);
        child.Parent = parent;
    }

    // A wall from one point to another, `thick` across, in chrome.
    private static Entity Wall(string name, Vector2 from, Vector2 to, float thick)
    {
        var wall = new Entity(name);
        var (middle, along) = ((from + to) / 2, to - from);
        wall.Transform.Position = new Vector3(middle, 0);
        var size = along.X == 0 || along.Y == 0 ? Vector2.Abs(along) + new Vector2(thick) : new Vector2(along.Length() + thick, thick);
        var sprite = wall.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint) = (size, Chrome);
        wall.AddComponent<BoxCollider>().Size = size;

        // A slanting one is turned, with a body that rotates (so its box turns with it) and is frozen (so it holds still).
        if (along.X != 0 && along.Y != 0)
        {
            wall.Transform.Rotation = new Vector3(0, 0, MathF.Atan2(along.Y, along.X) * 180 / MathF.PI);
            var body = wall.AddComponent<Body>();
            (body.Rotates, body.Frozen) = (true, true);
        }

        return wall;
    }

    /// <summary>
    /// A flipper on the <paramref name="side"/> given, resting 30 degrees down and swinging 55 up: its paddle's thick end (and
    /// hinge) at <paramref name="at"/> if <paramref name="pivot"/>, else its middle there.
    /// </summary>
    public static Entity Flipper(FlipperSide side, Vector2 at, bool pivot = false)
    {
        var left = side == FlipperSide.Left;
        var flipper = new Entity(left ? "Left Flipper" : "Right Flipper");
        var turn = left ? -30f : 30f;
        var half = PinballArt.FlipperLength / 2;
        var radians = turn * MathF.PI / 180;
        var along = new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * half * (left ? 1 : -1);
        var middle = pivot ? at + along : at;
        flipper.Transform.Position = new Vector3(middle, 0);
        flipper.Transform.Rotation = new Vector3(0, 0, turn);

        var sprite = flipper.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.FlipX, sprite.Order) = (PinballArt.Flipper, new Vector2(PinballArt.FlipperLength + 2 * PinballArt.FlipperRadius, 2 * PinballArt.FlipperRadius), !left, 2);
        flipper.AddComponent<PolygonCollider>().Points = PinballArt.FlipperPoints(mirrored: !left);
        var body = flipper.AddComponent<Body>();
        (body.Rotates, body.Mass, body.GravityScale, body.Grip) = (true, 3, 0, 0.5f);
        var hinge = flipper.AddComponent<HingeJoint>();
        (hinge.Anchor, hinge.Limit, hinge.Friction) = (new Vector2(left ? -half : half, 0), true, 0);
        (hinge.LowerAngle, hinge.UpperAngle) = left ? (0f, 55f) : (-55f, 0f);
        var flip = flipper.AddComponent<Flipper>();
        (flip.Side, flip.Sound) = (side, PinballArt.FlipSound);
        return flipper;
    }

    /// <summary>A slingshot kicker on the <paramref name="side"/> given (its rubber facing up and in), its middle at <paramref name="at"/>.</summary>
    public static Entity Kicker(FlipperSide side, Vector2 at)
    {
        var left = side == FlipperSide.Left;
        var kicker = new Entity(left ? "Left Kicker" : "Right Kicker");
        kicker.Transform.Position = new Vector3(at, 0);
        var sprite = kicker.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.FlipX, sprite.Order) = (PinballArt.Kicker, new Vector2(0.9f, 1.6f), !left, 1);
        kicker.AddComponent<PolygonCollider>().Points = PinballArt.KickerPoints(mirrored: !left);
        var bumper = kicker.AddComponent<Bumper>();
        (bumper.Direction, bumper.Strength, bumper.Points, bumper.Pop, bumper.Sound) = (new Vector2(left ? 0.87f : -0.87f, 0.49f), 13, 10, 1, PinballArt.KickerSound);
        return kicker;
    }

    /// <summary>Makes <paramref name="entity"/> a pop bumper at <paramref name="at"/>.</summary>
    public static void MakeBumper(Entity entity, Vector2 at)
    {
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (PinballArt.Bumper, new Vector2(1.2f), 1);
        entity.AddComponent<CircleCollider>().Radius = 0.55f;
        entity.AddComponent<Bumper>().Sound = PinballArt.BumperSound;
    }

    /// <summary>Makes <paramref name="entity"/> a plunger, its tip at its position.</summary>
    public static void MakePlunger(Entity entity)
    {
        var tip = entity.AddComponent<BoxCollider>();
        (tip.Size, tip.Offset) = (new Vector2(0.8f, 0.3f), new Vector2(0, -0.15f));
        var plunger = entity.AddComponent<Plunger>();
        (plunger.PullSound, plunger.LaunchSound) = (PinballArt.PullSound, PinballArt.LaunchSound);
    }

    /// <summary>A steel ball resting at <paramref name="at"/>.</summary>
    public static Entity Ball(Vector2 at)
    {
        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(at, 0);
        var sprite = ball.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (PinballArt.Ball, new Vector2(2 * BallRadius), 3);
        ball.AddComponent<CircleCollider>().Radius = BallRadius;
        var body = ball.AddComponent<Body>();
        (body.Rotates, body.Mass, body.Bounciness, body.RollingResistance, body.Grip) = (true, 1, 0.3f, 0.02f, 0.3f);
        return ball;
    }

    /// <summary>Three drop targets in a row from <paramref name="at"/>: up the left wall (<paramref name="upright"/>), or across.</summary>
    public static IEnumerable<Entity> DropTargets(Vector2 at, bool upright)
    {
        for (var i = 0; i < 3; i++)
        {
            var target = new Entity("Drop Target");
            var offset = (i - 1) * 0.75f;
            target.Transform.Position = new Vector3(at + (upright ? new Vector2(0, offset) : new Vector2(offset, 0)), 0);
            if (!upright)
                target.Transform.Rotation = new Vector3(0, 0, 90);
            var sprite = target.AddComponent<Sprite>();
            (sprite.Image, sprite.Size, sprite.Order) = (PinballArt.Target, new Vector2(0.25f, 0.6f), 1);
            target.AddComponent<BoxCollider>().Size = new Vector2(0.25f, 0.6f);
            var dropping = target.AddComponent<Target>();
            (dropping.Drops, dropping.Sound) = (true, PinballArt.TargetSound);
            yield return target;
        }
    }

    /// <summary>Three rollover lanes, <paramref name="gap"/> apart, round <paramref name="at"/>.</summary>
    public static IEnumerable<Entity> Lanes(Vector2 at, float gap)
    {
        for (var i = 0; i < 3; i++)
        {
            var lane = new Entity("Lane");
            lane.Transform.Position = new Vector3(at + new Vector2((i - 1) * gap, 0), 0);
            var sprite = lane.AddComponent<Sprite>();
            (sprite.Image, sprite.Size) = (PinballArt.Light, new Vector2(0.5f));
            var mouth = lane.AddComponent<BoxCollider>();
            (mouth.Size, mouth.IsTrigger) = (new Vector2(0.7f, 1), true);
            lane.AddComponent<Lane>().Sound = PinballArt.LaneSound;
            yield return lane;
        }
    }

    /// <summary>A line of text for the camera's corner showing a Counter, set up by <paramref name="setUp"/>.</summary>
    public static Entity Readout(string name, Vector2 at, Action<Counter> setUp)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 1);
        var text = entity.AddComponent<Text>();
        (text.Value, text.Size, text.Order) = ("", 0.8f, 100);
        var counter = entity.AddComponent<Counter>();
        setUp(counter);
        return entity;
    }
}
