using System.Numerics;
using Griff.Editing;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>
/// Creates a training pair to try a fighter's moves on: a fighter to play and a training dummy facing it, on a floor of
/// their own, with the dummy's health, the fighter's combo and its special moves written over them. The fighter walks on
/// the arrows or A and D (back blocks, down crouches, up jumps), punches with P (U, J or the gamepad's X) and kicks with K
/// (I, K or the gamepad's A); its moves are a jab, a kick, a low kick (blocked only crouching) and a kick in the air, a
/// palm strike on a quarter circle forward (236P), an uppercut on a dragon punch (623P) and a rush on a charge (back held,
/// then forward and K: [4]6K), and the jab, kick and low kick cancel into the specials once they connect. Both have the
/// same moves and boxes; the dummy just stands there until its Auto Block or Controllable is set. Pictures go in
/// Assets/Fighting, to repaint; Entity &gt; Edit Moves edits the moves, and the Clip Editor their boxes. All in one undo
/// step.
/// </summary>
[Category("Fighting")]
public sealed class TrainingPreset : EntityPreset
{
    public override string Id => "Entity.CreateTrainingPair";

    public override string Text => "Create Training Pair";

    public override int Order => 43;

    public override string? Icon => "Person";

    public override string EntityName(Scene scene) => SparringPreset.Free(scene, "Training");

    // On its own: a training pair isn't part of anything else.
    public override Entity? InsideOf(Entity? selected) => null;

    public override IReadOnlyDictionary<string, byte[]> Files() => FightingArt.FighterFiles();

    /// <summary>The fighter's moves, as its Moves lists them.</summary>
    public static IReadOnlyList<string> FighterMoves { get; } =
    [
        "Jab: 5P frames 3/2/7 cancels special",
        "Kick: 5K frames 6/3/14 cancels special",
        "Low Kick: 2K frames 5/3/14 low cancels special",
        "Air Kick: j.K clip Kick frames 3/10/2",
        "Uppercut: 623P frames 3/10/26 push 1,8",
        "Palm: 236P frames 10/4/20 chip 0.25",
        "Rush: [4]6K clip Palm frames 8/8/18 push 7,0",
    ];

    public override void Configure(Entity entity, Scene scene)
    {
        var camera = Views.FindCamera(scene);
        var at = camera is null ? Vector2.Zero : new Vector2(camera.Transform.Position.X, camera.Transform.Position.Y);
        entity.Transform.Position = new Vector3(at, 0);
    }

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -2, 0);
        var ground = floor.AddComponent<Sprite>();
        (ground.Size, ground.Tint) = (new Vector2(18, 1), Color.FromBytes(0x5A, 0x4E, 0x6A));
        floor.AddComponent<BoxCollider>().Size = new Vector2(18, 1);
        yield return floor;

        yield return Make("Fighter", -1.2f, Color.White, controllable: true);
        yield return Make("Dummy", 1.2f, Color.FromBytes(0xFF, 0xB0, 0xA8), controllable: false);

        yield return Readout("Dummy Health", new Vector2(0, 3.1f), "Dummy", "Health", "Dummy {0} / {1}", 0.45f);
        // Nothing until a second hit lands: "3 hit combo".
        yield return Readout("Combo", new Vector2(0, 2.55f), "Fighter", "Combo Hits", "{0:0' hit combo';;#}", 0.4f);
        var moves = new Entity("Specials");
        moves.Transform.Position = new Vector3(0, -2.9f, 0);
        var text = moves.AddComponent<Text>();
        (text.Value, text.Size) = ("236P Palm   623P Uppercut   [4]6K Rush   2K Low Kick", 0.28f);
        yield return moves;
    }

    private static Entity Readout(string name, Vector2 at, string track, string output, string value, float size)
    {
        var readout = new Entity(name);
        readout.Transform.Position = new Vector3(at, 0);
        var text = readout.AddComponent<Text>();
        (text.Value, text.Size, text.Track, text.Output) = (value, size, track, output);
        return readout;
    }

    /// <summary>The fighter's boxes, frame by frame: hurtboxes on its body and head (lower crouching), and each move's hitbox.</summary>
    public static IReadOnlyList<FrameBox> FighterBoxes()
    {
        var boxes = new List<FrameBox>();
        void Standing(string clip, int frame, float lean = 0)
        {
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(0, -0.38f), new Vector2(0.5f, 1.25f)));
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(lean / 16, 0.56f), Vector2.Zero, multiplier: 1.5f).AsCircle(0.28f));
        }

        void Crouched(string clip, int frame)
        {
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(0, -0.6f), new Vector2(0.55f, 0.8f)));
            boxes.Add(FrameBox.Hurtbox(clip, frame, new Vector2(0, 0.13f), Vector2.Zero, multiplier: 1.5f).AsCircle(0.28f));
        }

        Standing("Idle", 0);
        Standing("Idle", 1);
        Standing("Walk", 0);
        Standing("Walk", 1);
        Crouched("Crouch", 0);
        Standing("Jump", 0);
        Standing("Block", 0, -1);
        Crouched("CrouchBlock", 0);
        Standing("Hurt", 0, -2);
        for (var frame = 0; frame < 3; frame++)
        {
            Standing("Jab", frame);
            Standing("Kick", frame);
            Crouched("LowKick", frame);
            Standing("Palm", frame);
            if (frame == 0)
                Crouched("Uppercut", frame);
            else
                Standing("Uppercut", frame);
        }

        // Each move's fist, foot or palms where they reach, and the arm or leg out there can be hit too.
        boxes.Add(FrameBox.Hitbox("Jab", 1, new Vector2(0.58f, 0.25f), new Vector2(0.32f, 0.26f), damage: 2, knockback: new Vector2(2, 0), stun: 14));
        boxes.Add(FrameBox.Hurtbox("Jab", 1, new Vector2(0.3f, 0.25f), new Vector2(0.3f, 0.2f)));
        boxes.Add(FrameBox.Hitbox("Kick", 1, new Vector2(0.6f, -0.19f), new Vector2(0.42f, 0.3f), damage: 3, knockback: new Vector2(3, 0), stun: 18));
        boxes.Add(FrameBox.Hurtbox("Kick", 1, new Vector2(0.32f, -0.3f), new Vector2(0.4f, 0.25f)));
        boxes.Add(FrameBox.Hitbox("LowKick", 1, new Vector2(0.6f, -0.86f), new Vector2(0.46f, 0.26f), damage: 2, knockback: new Vector2(2, 0), stun: 16));
        boxes.Add(FrameBox.Hurtbox("LowKick", 1, new Vector2(0.32f, -0.8f), new Vector2(0.4f, 0.2f)));
        boxes.Add(FrameBox.Hitbox("Palm", 1, new Vector2(0.56f, 0.04f), new Vector2(0.42f, 0.5f), damage: 4, knockback: new Vector2(4, 1), stun: 22));
        boxes.Add(FrameBox.Hitbox("Uppercut", 1, new Vector2(0.3f, 0.6f), new Vector2(0.36f, 0.6f), damage: 5, knockback: new Vector2(2, 6), stun: 30));
        boxes.Add(FrameBox.Hitbox("Uppercut", 2, new Vector2(0.2f, 0.75f), new Vector2(0.36f, 0.6f), damage: 5, knockback: new Vector2(2, 6), stun: 30));
        return boxes;
    }

    private static Entity Make(string name, float x, Color tint, bool controllable)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x, -0.5f, 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Columns, sprite.Rows, sprite.Tint, sprite.FlipX) =
            (FightingArt.Fighter, new Vector2(1.5f, 2), FightingArt.FighterColumns, FightingArt.FighterRows, tint, x > 0);
        var animation = entity.AddComponent<Animation>();
        (animation.Clips, animation.Clip) = (FightingArt.FighterClips, "Idle");
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.5f, 2);
        entity.AddComponent<Body>();
        var health = entity.AddComponent<Health>();
        // The dummy gets up again a second after it's knocked out; the fighter is the player's.
        (health.Max, health.Current, health.InvincibleTime, health.Respawn, health.RespawnDelay) = (30, 30, 0, !controllable, 1);
        var boxes = entity.AddComponent<Hitboxes>();
        (boxes.Boxes, boxes.HitStop) = (FrameBox.FormatAll(FighterBoxes()), 0.06f);
        var fighter = entity.AddComponent<FightingCharacter>();
        (fighter.Moves, fighter.Controllable) = (FighterMoves, controllable);
        return entity;
    }
}
