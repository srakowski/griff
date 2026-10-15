using System.Numerics;
using Griff.Editing;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>
/// Creates a sparring pair to try hitboxes on: a brawler to play (the arrows or WASD to move, Space to jump, X or the
/// gamepad's X to punch) and a sandbag beside it, on a floor of their own, with a line over them showing the sandbag's
/// health. The brawler's sheet has Idle, Run and Attack clips; every frame has hurtboxes on its body and its head (which
/// takes half as much again), and the punch's two frames with the fist out have a hitbox: 2 damage, knockback, and 18
/// steps of hitstun, once a punch. Turn on Game &gt; Show Hitboxes to see them as it plays, and open the Clip Editor to
/// change them. Pictures go in Assets/Fighting, to repaint. All in one undo step.
/// </summary>
[Category("Fighting")]
public sealed class SparringPreset : EntityPreset
{
    public override string Id => "Entity.CreateSparring";

    public override string Text => "Create Sparring Pair";

    public override int Order => 36;

    public override string? Icon => "Person";

    public override string EntityName(Scene scene) => Free(scene, "Sparring");

    // On its own: a sparring pair isn't part of anything else.
    public override Entity? InsideOf(Entity? selected) => null;

    public override IReadOnlyDictionary<string, byte[]> Files() => FightingArt.Files();

    // Where the camera looks.
    private Vector2 _at;

    public override void Configure(Entity entity, Scene scene)
    {
        var camera = SceneRenderer.FindCamera(scene);
        _at = camera is null ? Vector2.Zero : new Vector2(camera.Transform.Position.X, camera.Transform.Position.Y);
        entity.Transform.Position = new Vector3(_at, 0);
    }

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -2, 0);
        var ground = floor.AddComponent<Sprite>();
        (ground.Size, ground.Tint) = (new Vector2(14, 1), Color.FromBytes(0x6B, 0x5A, 0x4A));
        floor.AddComponent<BoxCollider>().Size = new Vector2(14, 1);
        yield return floor;

        yield return Brawler();
        yield return Sandbag();

        var readout = new Entity("Sandbag Health");
        readout.Transform.Position = new Vector3(0, 2.6f, 0);
        var text = readout.AddComponent<Text>();
        (text.Value, text.Size, text.Track, text.Output) = ("Sandbag {0} / {1}", 0.5f, "Sandbag", "Health");
        yield return readout;
    }

    /// <summary>The brawler's boxes, frame by frame: hurtboxes on its body and head, and the punch's hitbox and outstretched arm.</summary>
    public static IReadOnlyList<FrameBox> BrawlerBoxes()
    {
        var boxes = new List<FrameBox>();
        void Body(string clip, int frame)
        {
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(0, -0.38f), new Vector2(0.5f, 1.25f)));
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(0.03f, 0.53f), Vector2.Zero, multiplier: 1.5f).AsCircle(0.28f));
        }

        Body("Idle", 0);
        Body("Idle", 1);
        Body("Run", 0);
        Body("Run", 1);
        for (var frame = 0; frame < 4; frame++)
            Body("Attack", frame);
        // The fist out on the punch's second and third frames: one hit a punch, whichever of them lands it.
        foreach (var (frame, fist, arm, reach) in new[] { (1, 0.47f, 0.28f, 0.31f), (2, 0.59f, 0.34f, 0.44f) })
        {
            boxes.Add(FrameBox.Hitbox("Attack", frame, new Vector2(fist, 0.22f), new Vector2(0.34f), damage: 2, knockback: new Vector2(4, 3), stun: 18));
            boxes.Add(FrameBox.Hurtbox("Attack", frame, new Vector2(arm, 0.22f), new Vector2(reach, 0.25f)));
        }

        return boxes;
    }

    private static Entity Brawler()
    {
        var brawler = new Entity("Brawler");
        brawler.Transform.Position = new Vector3(-0.3f, -0.5f, 0);
        var sprite = brawler.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Columns, sprite.Rows) = (FightingArt.Brawler, new Vector2(1.5f, 2), 4, 2);
        var animation = brawler.AddComponent<Animation>();
        (animation.Clips, animation.Clip) = (FightingArt.BrawlerClips, "Idle");
        brawler.AddComponent<BoxCollider>().Size = new Vector2(0.5f, 2);
        brawler.AddComponent<Body>();
        brawler.AddComponent<PlatformerMovement>();
        var health = brawler.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (10, 10, 0);
        // The Attack action plays the Attack clip (no hitbox child: its frames' boxes hit), as long as the clip lasts.
        var attack = brawler.AddComponent<Attack>();
        (attack.Hitbox, attack.Windup, attack.ActiveTime, attack.Cooldown, attack.HitStop, attack.Target) = ("", 0, 4 / 12f, 0.1f, 0, "");
        var boxes = brawler.AddComponent<Hitboxes>();
        (boxes.Boxes, boxes.HitStop) = (FrameBox.FormatAll(BrawlerBoxes()), 0.06f);
        return brawler;
    }

    private static Entity Sandbag()
    {
        var sandbag = new Entity("Sandbag");
        sandbag.Transform.Position = new Vector3(0.8f, -0.5f, 0);
        var sprite = sandbag.AddComponent<Sprite>();
        (sprite.Image, sprite.Size) = (FightingArt.Sandbag, new Vector2(1.5f, 2));
        sandbag.AddComponent<BoxCollider>().Size = new Vector2(0.9f, 2);
        sandbag.AddComponent<Body>().Friction = 12;
        var health = sandbag.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime, health.Respawn, health.RespawnDelay) = (20, 20, 0, true, 1);
        // No animation: its one picture's boxes.
        sandbag.AddComponent<Hitboxes>().Boxes = [FrameBox.Hurtbox(FrameBox.OwnFrames, 0, new Vector2(0, 0.12f), new Vector2(0.88f, 1.38f)).ToString()];
        return sandbag;
    }

    internal static string Free(Scene scene, string name)
    {
        var taken = scene.Entities.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
            return name;
        for (var i = 2; ; i++)
        {
            if (!taken.Contains($"{name} {i}"))
                return $"{name} {i}";
        }
    }
}
