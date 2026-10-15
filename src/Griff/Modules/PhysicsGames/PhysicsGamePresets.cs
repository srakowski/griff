using System.Numerics;
using Griff.Editing;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Creates a slingshot on the left with three shots waiting behind it, ready to pull back and launch: drag its pouch back
/// with the mouse and let go (or pull it about with the arrows and press Space). The camera follows each shot as it flies:
/// a camera with no Follow gets one following the slingshot, and a scene with nothing solid to stand on gets the ground
/// too. All in one undo step.
/// </summary>
[Category("Physics Games")]
public sealed class SlingshotPreset : EntityPreset
{
    public override string Id => "Entity.CreateSlingshot";

    public override string Text => "Create Slingshot";

    public override int Order => 34;

    public override string? Icon => "Bounce";

    public override string EntityName(Scene scene) => PhysicsGameNames.Free(scene, "Slingshot");

    // Beside whatever's picked, never inside a slingshot's shots or a tower.
    public override Entity? InsideOf(Entity? selected) => PhysicsGameNames.Outside(selected);

    public override void Configure(Entity entity, Scene scene)
    {
        entity.AddComponent<Slingshot>().Power = 24;
        entity.Transform.Position = new Vector3(-7, PhysicsGameNames.GroundAt(scene, -7), 0);
    }

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        // Three red balls in a row behind it, as the game lays them out.
        for (var i = 0; i < 3; i++)
        {
            var shot = new Entity("Shot");
            shot.Transform.Position = new Vector3(-0.95f - i * 0.9f, 0.35f, 0);
            var sprite = shot.AddComponent<Sprite>();
            (sprite.Size, sprite.Tint) = (new Vector2(0.7f), Color.FromBytes(0xE0, 0x3A, 0x3A));
            shot.AddComponent<CircleCollider>().Radius = 0.35f;
            var body = shot.AddComponent<Body>();
            (body.Rotates, body.Mass, body.Bounciness) = (true, 2, 0.2f);
            yield return shot;
        }
    }

    public override IEnumerable<IEdit> EditScene(Scene scene)
    {
        if (!PhysicsGameNames.HasGround(scene))
            yield return new AddEntityEdit(scene, PhysicsGameNames.Ground(scene));

        // The camera follows the slingshot, and each shot from it as it flies.
        if (SceneRenderer.FindCamera(scene) is { } camera && camera.GetComponent<Follow>() is null)
        {
            var follow = new Follow { Target = EntityName(scene), Smoothing = 3, Offset = new Vector2(6, 2) };
            yield return new AddComponentEdit(camera, follow);
        }
    }
}

/// <summary>
/// Creates a little tower to knock down on the right: two storeys of wooden posts and planks (with a pane of glass and a
/// stone slab on top), every piece a Breakable that cracks and splits on hard knocks, standing asleep until something hits
/// it, and a green target inside that pops easily. Each piece broken sends Score with its points, and a scene with no score
/// gets one on the camera that adds them up. All in one undo step.
/// </summary>
[Category("Physics Games")]
public sealed class TowerPreset : EntityPreset
{
    public override string Id => "Entity.CreateTower";

    public override string Text => "Create Tower";

    public override int Order => 35;

    public override string? Icon => "Blocks";

    public override string EntityName(Scene scene) => PhysicsGameNames.Free(scene, "Tower");

    public override Entity? InsideOf(Entity? selected) => PhysicsGameNames.Outside(selected);

    public override void Configure(Entity entity, Scene scene) =>
        entity.Transform.Position = new Vector3(6, PhysicsGameNames.GroundAt(scene, 6), 0);

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        // Each storey: two posts and a plank across them; glass between the first storey's posts, the target on its floor.
        yield return Piece("Post", new Vector2(-1.1f, 1), new Vector2(0.35f, 2), Material.Wood);
        yield return Piece("Post", new Vector2(1.1f, 1), new Vector2(0.35f, 2), Material.Wood);
        yield return Piece("Plank", new Vector2(0, 2.175f), new Vector2(3, 0.35f), Material.Wood);
        yield return Piece("Post", new Vector2(-0.8f, 3.35f), new Vector2(0.35f, 2), Material.Wood);
        yield return Piece("Post", new Vector2(0.8f, 3.35f), new Vector2(0.35f, 2), Material.Wood);
        yield return Piece("Pane", new Vector2(0, 3.35f), new Vector2(0.25f, 1.6f), Material.Glass);
        yield return Piece("Slab", new Vector2(0, 4.525f), new Vector2(2.4f, 0.35f), Material.Stone);

        var target = new Entity("Target");
        target.Transform.Position = new Vector3(0, 0.45f, 0);
        var sprite = target.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint) = (new Vector2(0.9f), Color.FromBytes(0x6C, 0xC2, 0x4A));
        target.AddComponent<CircleCollider>().Radius = 0.45f;
        var body = target.AddComponent<Body>();
        (body.Rotates, body.Mass, body.Asleep) = (true, 1, true);
        var breakable = target.AddComponent<Breakable>();
        (breakable.Strength, breakable.MinHit, breakable.Split, breakable.Points, breakable.BreakMessage) = (3, 1.5f, 0, 5000, "Score");
        yield return target;
    }

    public override IEnumerable<IEdit> EditScene(Scene scene)
    {
        if (!PhysicsGameNames.HasGround(scene))
            yield return new AddEntityEdit(scene, PhysicsGameNames.Ground(scene));

        // A score that adds up what's broken, at the top of the camera's view.
        if (!scene.Entities.Any(e => e.GetComponent<Counter>() is { Message: "Score" }) && SceneRenderer.FindCamera(scene) is { } camera)
        {
            var score = new Entity(PhysicsGameNames.Free(scene, "Score"));
            var size = camera.GetComponent<Camera>()?.Size ?? SceneRenderer.DefaultCameraSize;
            score.Transform.Position = new Vector3(0, size - 0.8f, 1);
            var text = score.AddComponent<Text>();
            (text.Value, text.Size, text.Order) = ("Score: 0", 0.7f, 100);
            score.AddComponent<Counter>();
            yield return new AddEntityEdit(scene, score, camera);
        }
    }

    private enum Material
    {
        Wood,
        Glass,
        Stone,
    }

    private static Entity Piece(string name, Vector2 at, Vector2 size, Material material)
    {
        var (color, mass, strength, minHit, points) = material switch
        {
            Material.Glass => (Color.FromBytes(0xA8, 0xDC, 0xF0, 0xC8), 0.6f, 4f, 1.5f, 300),
            Material.Stone => (Color.FromBytes(0x8E, 0x92, 0x9C), 3f, 40f, 6f, 1000),
            _ => (Color.FromBytes(0xB8, 0x80, 0x4A), 1f, 12f, 3f, 500),
        };
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint) = (size, color);
        entity.AddComponent<BoxCollider>().Size = size;
        var body = entity.AddComponent<Body>();
        (body.Rotates, body.Mass, body.Asleep) = (true, mass * size.X * size.Y, true);
        var breakable = entity.AddComponent<Breakable>();
        (breakable.Strength, breakable.MinHit, breakable.Points, breakable.BreakMessage) = (strength, minHit, points, "Score");
        return entity;
    }
}

/// <summary>Names and places the physics game presets use.</summary>
internal static class PhysicsGameNames
{
    /// <summary><paramref name="name"/>, or with a number after it if the scene has one already ("Slingshot 2").</summary>
    public static string Free(Scene scene, string name)
    {
        var taken = scene.Entities.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(name))
            return name;
        for (var i = 2; ; i++)
        {
            if (!taken.Contains($"{name} {i}"))
                return $"{name} {i}";
        }
    }

    /// <summary>What a new slingshot or tower goes inside, given what's picked: not a slingshot, a tower or anything in them.</summary>
    public static Entity? Outside(Entity? selected)
    {
        var parent = selected;
        while (parent is not null && (parent.GetComponent<Slingshot>() is not null || parent.GetComponent<Body>() is not null ||
                                      parent.Children.Any(c => c.GetComponent<Breakable>() is not null)))
            parent = parent.Parent;
        return parent;
    }

    /// <summary>Whether the scene has something solid that doesn't move to stand things on.</summary>
    public static bool HasGround(Scene scene) => scene.Entities.Any(e =>
        e.GetComponent<Tilemap>() is { Solid: true } || (e.GetComponent<Collider>() is { IsTrigger: false } && e.GetComponent<Body>() is null && !InTower(e)));

    /// <summary>The top of the highest solid thing that doesn't move under <paramref name="x"/>, or 0.</summary>
    public static float GroundAt(Scene scene, float x)
    {
        var top = float.NegativeInfinity;
        foreach (var entity in scene.Entities)
        {
            if (entity.GetComponent<Collider>() is not { IsTrigger: false } collider || entity.GetComponent<Body>() is not null || InTower(entity))
                continue;
            var bounds = collider.WorldBounds;
            if (bounds.Min.X <= x && bounds.Max.X >= x && bounds.Max.Y < 20)
                top = MathF.Max(top, bounds.Max.Y);
        }

        return float.IsFinite(top) ? top : 0;
    }

    /// <summary>Ground to stand a slingshot and towers on: 60 wide along y = 0.</summary>
    public static Entity Ground(Scene scene)
    {
        var ground = new Entity(Free(scene, "Ground"));
        ground.Transform.Position = new Vector3(0, -0.5f, 0);
        var sprite = ground.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint) = (new Vector2(60, 1), Color.FromBytes(0x5B, 0x8C, 0x3A));
        ground.AddComponent<BoxCollider>().Size = new Vector2(60, 1);
        return ground;
    }

    private static bool InTower(Entity entity) => entity.GetComponent<Breakable>() is not null;
}
