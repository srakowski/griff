using System.Numerics;
using Griff.Input;
using Griff.Modules.Fighting;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>
/// Hitboxes and hurtboxes for each animation frame: their lines read and written; the boxes of the frame an entity shows,
/// mirrored with its sprite; hits landing frame by frame, once per group a swing, on the hurtbox that takes the most,
/// with damage, knockback, hitstun, messages, outputs and events; teams, invincibility, trades and things hit through
/// their colliders; with an Attack; the same to the bit twice and across a rewind, in a network game, with no garbage,
/// and the boxes drawn over the game when shown.
/// </summary>
public sealed class HitboxTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly List<(string Message, object? Data)> _heard = [];

    public HitboxTests()
    {
        _game = new Game(_scene, new InputState()) { PrefabLoader = name => new Entity(name) };
        _game.On("Punched", data => _heard.Add(("Punched", data)));
        _game.On("Ouch", data => _heard.Add(("Ouch", data)));
    }

    private void Step(int steps = 1)
    {
        for (var i = 0; i < steps; i++)
            _game.Tick(Frame);
    }

    // A fighter at x playing "Punch" (four frames at 10 a second, once) with a fist out on frames 1 and 2.
    private Entity Puncher(float x = 0, string clips = "Punch 0-3 @10 once", params FrameBox[] boxes)
    {
        var entity = new Entity("Puncher");
        entity.Transform.Position = new Vector3(x, 0, 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Size, sprite.Columns) = (new Vector2(1, 2), 8);
        var animation = entity.AddComponent<Animation>();
        (animation.Clips, animation.Clip, animation.FramesPerSecond) = (clips, "Punch", 10);
        var hitboxes = entity.AddComponent<Hitboxes>();
        hitboxes.HitStop = 0;
        hitboxes.Boxes = FrameBox.FormatAll(boxes.Length > 0 ? boxes :
        [
            FrameBox.Hitbox("Punch", 1, new Vector2(0.8f, 0), new Vector2(0.4f), damage: 2, knockback: new Vector2(3, 1), stun: 10),
            FrameBox.Hitbox("Punch", 2, new Vector2(0.9f, 0), new Vector2(0.4f), damage: 2, knockback: new Vector2(3, 1), stun: 10),
        ]);
        _scene.Add(entity);
        return entity;
    }

    // Something to hit at x: a hurtbox 0.6 across over its whole height, and 10 hit points that every hit lands on.
    private Entity Target(float x = 1, string name = "Target", params FrameBox[] boxes)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x, 0, 0);
        entity.AddComponent<Sprite>().Size = new Vector2(1, 2);
        var health = entity.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (10, 10, 0);
        entity.AddComponent<Hitboxes>().Boxes = FrameBox.FormatAll(boxes.Length > 0 ? boxes : [FrameBox.Hurtbox(FrameBox.OwnFrames, 0, Vector2.Zero, new Vector2(0.6f, 2))]);
        _scene.Add(entity);
        return entity;
    }

    private static Hitboxes BoxesOf(Entity entity) => entity.GetComponent<Hitboxes>()!;

    private static float HealthOf(Entity entity) => entity.GetComponent<Health>()!.Current;

    [Fact]
    public void Lines_are_read_and_written_with_what_isnt_the_default()
    {
        Assert.True(FrameBox.TryParse("Punch 2 hit at 0.6,0.3 size 0.5x0.25 damage 3 knockback 4,2 stun 12 group 2", out var hit, out _));
        Assert.Equal(new FrameBox("Punch", 2, FrameBoxKind.Hit, new Vector2(0.6f, 0.3f), new Vector2(0.5f, 0.25f), 0, 3, new Vector2(4, 2), 12, 2), hit);
        Assert.Equal("Punch 2 hit at 0.6,0.3 size 0.5x0.25 damage 3 knockback 4,2 stun 12 group 2", hit.ToString());

        Assert.True(FrameBox.TryParse("Idle 0 hurt at 0,0.7 radius 0.3 multiplier 1.5", out var head, out _));
        Assert.True(head.IsCircle);
        Assert.Equal(new Vector2(0.6f), head.Size);
        Assert.Equal(1.5f, head.Multiplier);
        Assert.Equal("Idle 0 hurt at 0,0.7 radius 0.3 multiplier 1.5", head.ToString());
        Assert.Equal("* 0 hurt at 0,0 size 1x2", FrameBox.Hurtbox(FrameBox.OwnFrames, 0, Vector2.Zero, new Vector2(1, 2)).ToString());
        Assert.Equal("Jab 1 hit at -0.125,0 size 0.333x1", FrameBox.Hitbox("Jab", 1, new Vector2(-0.125f, 0), new Vector2(1 / 3f, 1)).ToString());

        Assert.False(FrameBox.TryParse("Punch 2 hit at 1,1", out _, out var noSize));
        Assert.Equal("it needs a size (size 1x2) or a radius (radius 0.5)", noSize);
        Assert.False(FrameBox.TryParse("Punch two hit at 1,1 size 1x1", out _, out _));
        Assert.False(FrameBox.TryParse("Punch 2 kick at 1,1 size 1x1", out _, out var kick));
        Assert.Equal("\"kick\" isn't hit or hurt", kick);
        Assert.False(FrameBox.TryParse("Punch 2 hit at 1,1 size -1x1", out _, out _));

        // A line that doesn't make sense is skipped; the rest still work.
        var boxes = new Hitboxes { Boxes = ["Punch 1 hit at 1,0 size 1x1", "nonsense", "Punch 1 hurt at 0,0 size 1x2"] };
        Assert.Equal(2, boxes.BoxList.Count);
        Assert.Equal(2, boxes.On("punch", 1).Length);
        Assert.Equal(0, boxes.On("Punch", 0).Length);
    }

    [Fact]
    public void An_entity_has_the_boxes_of_the_frame_it_shows()
    {
        var puncher = Puncher(boxes:
        [
            FrameBox.Hurtbox("Punch", 0, Vector2.Zero, Vector2.One),
            FrameBox.Hitbox("Punch", 1, new Vector2(1, 0), Vector2.One),
            FrameBox.Hurtbox(FrameBox.OwnFrames, 0, Vector2.Zero, new Vector2(2)),
        ]);
        var boxes = BoxesOf(puncher);
        var animation = puncher.GetComponent<Animation>()!;

        Step();
        Assert.Equal(("Punch", 0, 0), boxes.Showing());
        Assert.Equal(FrameBoxKind.Hurt, boxes.Now()[0].Kind);
        animation.Time = 0.15f;
        Assert.Equal(("Punch", 1, 0), boxes.Showing());
        Assert.True(boxes.Now()[0].IsHit);
        animation.Time = 5;
        Assert.Equal(("Punch", 3, 0), boxes.Showing());
        Assert.Equal(0, boxes.Now().Length);

        // No clip playing: the animation's own frames; no animation: its one picture.
        animation.Clip = null;
        animation.Time = 0;
        Assert.Equal((FrameBox.OwnFrames, 0, 0), boxes.Showing());
        Assert.Equal(new Vector2(2), boxes.Now()[0].Size);
        puncher.RemoveComponent(animation);
        Assert.Equal((FrameBox.OwnFrames, 0, 0), boxes.Showing());
    }

    [Fact]
    public void A_swing_hits_on_the_step_its_frame_comes_and_once_however_many_frames_touch()
    {
        var puncher = Puncher();
        var target = Target();
        var (boxes, hurt) = (BoxesOf(puncher), BoxesOf(target));

        // The step the fist's frame shows is the step it lands, not one sooner or later.
        var landedOn = -1;
        for (var step = 0; step < 40; step++)
        {
            var before = boxes.Hits;
            Step();
            var showing = boxes.Showing().Frame;
            if (boxes.Hits > before)
            {
                Assert.Equal(-1, landedOn);
                landedOn = step;
                Assert.Equal(1, showing);
                Assert.True(boxes.JustHit);
                Assert.True(hurt.JustGotHit);
                Assert.Equal(10, hurt.Hitstun);
            }
            else if (landedOn < 0)
                Assert.Equal(0, showing);
        }

        Assert.True(landedOn > 0);
        Assert.Equal(8, HealthOf(target));
        Assert.Equal(1, boxes.Hits);
        Assert.False(boxes.JustHit);
        Assert.False(hurt.JustGotHit);
        Assert.Equal(0, hurt.Hitstun);
        Assert.Single(boxes.Struck);

        // The clip starting over is a new swing: it hits again.
        puncher.GetComponent<Animation>()!.Play("Punch", restart: true);
        Step(40);
        Assert.Equal(6, HealthOf(target));
        Assert.Equal(2, boxes.Hits);
    }

    [Fact]
    public void Each_group_hits_once_and_a_looping_clip_hits_again_each_time_round()
    {
        var puncher = Puncher(boxes:
        [
            FrameBox.Hitbox("Punch", 1, new Vector2(0.8f, 0), new Vector2(0.4f), damage: 1, group: 1),
            FrameBox.Hitbox("Punch", 2, new Vector2(0.8f, 0), new Vector2(0.4f), damage: 1, group: 2),
            FrameBox.Hitbox("Punch", 3, new Vector2(0.8f, 0), new Vector2(0.4f), damage: 1, group: 2),
        ]);
        var target = Target();
        Step(40);
        Assert.Equal(8, HealthOf(target));

        // Going round again is a new swing.
        var animation = puncher.GetComponent<Animation>()!;
        animation.Clips = "Punch 0-3 @10 loop";
        animation.Play("Punch", restart: true);
        Step(24 * 3);
        Assert.Equal(2, HealthOf(target));
    }

    [Fact]
    public void A_hit_lands_on_the_hurtbox_that_takes_the_most_and_misses_frames_with_none()
    {
        Puncher();
        // A head over the body: both touch the fist, and the head takes half as much again.
        var target = Target(boxes:
        [
            FrameBox.Hurtbox(FrameBox.OwnFrames, 0, Vector2.Zero, new Vector2(0.6f, 2)),
            FrameBox.Hurtbox(FrameBox.OwnFrames, 0, new Vector2(0, 0.1f), Vector2.Zero, multiplier: 1.5f).AsCircle(0.3f),
        ]);
        HitInfo? hit = null;
        BoxesOf(target).GotHit += h => hit = h;
        Step(40);
        Assert.Equal(7, HealthOf(target));
        Assert.Equal(3, hit!.Value.Damage);

        // A frame with no hurtboxes can't be hit (a dodge).
        var dodger = Target(1, "Dodger", FrameBox.Hurtbox("Hit", 0, Vector2.Zero, new Vector2(0.6f, 2)));
        _scene.Remove(target);
        var swing = _scene.Entities.Single(e => e.Name == "Puncher").GetComponent<Animation>()!;
        swing.Play("Punch", restart: true);
        Step(40);
        Assert.Equal(10, HealthOf(dodger));
    }

    [Fact]
    public void Facing_left_mirrors_the_boxes_and_the_knockback()
    {
        var puncher = Puncher();
        puncher.GetComponent<Sprite>()!.FlipX = true;
        var right = Target(1, "Right");
        var left = Target(-1, "Left");
        var body = left.AddComponent<Body>();
        body.GravityScale = 0;
        HitInfo? hit = null;
        BoxesOf(puncher).Hit += h => hit = h;
        Step(9);

        Assert.Equal(10, HealthOf(right));
        Assert.Equal(8, HealthOf(left));
        Assert.Equal(new Vector2(-3, 1), hit!.Value.Knockback);
        Assert.Equal(left, hit.Value.Target);
        Assert.Equal(puncher, hit.Value.Attacker);
        Assert.Equal(10, hit.Value.Stun);
        Assert.Equal(-0.85f, hit.Value.Point.X, 3);
        Assert.Equal(new Vector2(-3, 1), body.Velocity);
    }

    [Fact]
    public void Messages_outputs_and_actions()
    {
        var puncher = Puncher();
        var target = Target();
        (BoxesOf(puncher).HitMessage, BoxesOf(target).GotHitMessage) = ("Punched", "Ouch");
        var landed = false;
        for (var i = 0; i < 40 && !landed; i++)
        {
            Step();
            landed = BoxesOf(puncher).JustHit;
        }

        Assert.Equal([("Ouch", (object?)2f), ("Punched", 2f)], _heard);
        Assert.True(Read(target, "Got Hit") is true);
        Assert.True(Read(target, "Stunned") is true);
        Assert.Equal(10, Read(target, "Hitstun"));
        Assert.True(Read(puncher, "Hit") is true);
        Assert.Equal(1, Read(puncher, "Hits"));
        Step();
        Assert.True(Read(puncher, "Hit") is false);

        // Its actions: stun it for a while, or end the stun.
        var hurt = BoxesOf(target);
        Assert.Equal(1, _game.Do(target, "Recover"));
        Assert.Equal(0, hurt.Hitstun);
        Assert.Equal(1, _game.Do(target, "Stun", 30f));
        Assert.Equal(30, hurt.Hitstun);
        Step();
        Assert.Equal(29, hurt.Hitstun);
        Assert.Equal(", hitstun 29", ((IPlaytestReport)hurt).Report());
        Assert.Equal(", 1 hit landed", ((IPlaytestReport)BoxesOf(puncher)).Report());

        static object? Read(Entity entity, string output)
        {
            var reader = new OutputReader();
            Assert.True(reader.Find(entity, null, output), output);
            return reader.Output!.ValueType == typeof(bool) ? reader.Number != 0 : reader.Output.ValueType == typeof(int) ? (object)(int)reader.Number : reader.Number;
        }
    }

    [Fact]
    public void Teammates_dont_hit_each_other_nor_does_anything_hit_what_cant_be_hurt_just_now()
    {
        var puncher = Puncher();
        var target = Target();
        (BoxesOf(puncher).Team, BoxesOf(target).Team) = ("Red", "red");
        Step(40);
        Assert.Equal(10, HealthOf(target));

        // Still invincible from the last hit: the swing goes on and lands once it can be hurt again.
        BoxesOf(target).Team = "Blue";
        var health = target.GetComponent<Health>()!;
        health.InvincibleTime = 0.2f;
        health.Damage(1);
        puncher.GetComponent<Animation>()!.Play("Punch", restart: true);
        Step(9);
        Assert.Equal(9, HealthOf(target));
        Assert.Equal(0, BoxesOf(puncher).Hits);
        Step(20);
        Assert.Equal(7, HealthOf(target));
        Assert.Equal(1, BoxesOf(puncher).Hits);
    }

    [Fact]
    public void Two_fighters_striking_at_once_both_land()
    {
        var left = Puncher(-0.45f, boxes:
        [
            FrameBox.Hitbox("Punch", 1, new Vector2(0.4f, 0), new Vector2(0.4f), damage: 2),
            FrameBox.Hurtbox("Punch", 1, Vector2.Zero, new Vector2(0.8f, 2)),
        ]);
        var right = Puncher(0.45f, boxes:
        [
            FrameBox.Hitbox("Punch", 1, new Vector2(0.4f, 0), new Vector2(0.4f), damage: 3),
            FrameBox.Hurtbox("Punch", 1, Vector2.Zero, new Vector2(0.8f, 2)),
        ]);
        right.GetComponent<Sprite>()!.FlipX = true;
        foreach (var fighter in new[] { left, right })
        {
            var health = fighter.AddComponent<Health>();
            (health.Max, health.Current, health.InvincibleTime) = (10, 10, 0.5f);
        }

        Step(12);
        Assert.Equal(7, HealthOf(left));
        Assert.Equal(8, HealthOf(right));
    }

    [Fact]
    public void Things_on_its_layers_with_a_health_and_no_boxes_are_hit_through_their_colliders()
    {
        var puncher = Puncher();
        BoxesOf(puncher).Layers = "Enemy";
        var slime = new Entity("Slime");
        slime.Transform.Position = new Vector3(1, 0, 0);
        slime.AddComponent<BoxCollider>().Layer = "Enemy";
        var health = slime.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (3, 3, 0);
        _scene.Add(slime);
        var crate = new Entity("Crate");
        crate.Transform.Position = new Vector3(1, 0, 0);
        crate.AddComponent<BoxCollider>().Layer = "Default";
        crate.AddComponent<Health>().InvincibleTime = 0;
        _scene.Add(crate);

        Step(40);
        Assert.Equal(1, health.Current);
        Assert.Equal(3, crate.GetComponent<Health>()!.Current);
        Assert.Equal(1, BoxesOf(puncher).Hits);
    }

    [Fact]
    public void An_attack_plays_its_clip_whose_frames_hit_each_swing()
    {
        var puncher = Puncher(clips: "Idle 4, Attack 0-3 @10 once", boxes:
        [
            FrameBox.Hitbox("Attack", 1, new Vector2(0.8f, 0), new Vector2(0.4f), damage: 1),
        ]);
        puncher.GetComponent<Animation>()!.Clip = "Idle";
        var attack = puncher.AddComponent<Attack>();
        (attack.Action, attack.Hitbox, attack.Windup, attack.ActiveTime, attack.Cooldown, attack.HitStop) = ("", "", 0, 0.4f, 0, 0);
        attack.Target = "Target";
        attack.Range = 2;
        var target = Target();

        // An enemy's attack swings as its target comes within range, again and again: each swing's frames hit once.
        Step(60);
        Assert.True(BoxesOf(puncher).Hits >= 2);
        Assert.Equal(10 - BoxesOf(puncher).Hits, HealthOf(target));
    }

    // A computer brawler and a sandbag from Create Sparring Pair, the brawler punching it as it comes in range.
    private static Scene Sparring(out Entity brawler, out Entity sandbag)
    {
        var scene = new Scene();
        var preset = new SparringPreset();
        var root = new Entity(preset.EntityName(scene));
        preset.Configure(root, scene);
        scene.Add(root);
        foreach (var child in preset.CreateChildren(scene))
            scene.Add(child, root);
        brawler = scene.Entities.Single(e => e.Name == "Brawler");
        sandbag = scene.Entities.Single(e => e.Name == "Sandbag");
        var attack = brawler.GetComponent<Attack>()!;
        (attack.Action, attack.Target, attack.Range) = ("", "Sandbag", 1.6f);
        return scene;
    }

    private static string Snapshot(Scene scene) => string.Join("; ", scene.Entities.Select(e =>
        $"{e.Name} {e.Transform.Position.X:R},{e.Transform.Position.Y:R} {e.GetComponent<Health>()?.Current:R} {e.GetComponent<Hitboxes>()?.Hits} " +
        $"{e.GetComponent<Hitboxes>()?.Hitstun} {e.GetComponent<Animation>()?.Time:R}"));

    [Fact]
    public void The_sparring_pair_fights_the_same_to_the_bit_twice_and_across_a_rewind()
    {
        static string Play(int frames)
        {
            var scene = Sparring(out _, out var sandbag);
            var game = new Game(scene, new InputState());
            for (var i = 0; i < frames; i++)
                game.Tick(Frame);
            Assert.True(sandbag.GetComponent<Health>()!.Current < 20);
            return Snapshot(scene);
        }

        Assert.Equal(Play(240), Play(240));

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var live = Sparring(out var brawler, out _);
        var game = new Game(live, new InputState());
        var timeline = new Timeline(serializer, capacity: 100, framesPerSnapshot: 7);
        for (var frame = 0; frame < 63; frame++)
        {
            game.Tick(Frame);
            timeline.Record(live, game.Time.Total);
        }

        for (var frame = 0; frame < 120; frame++)
            game.Tick(Frame);

        // A snapshot in the middle of a punch: its frame, what it has struck and the sandbag's hitstun come back with it.
        var restored = timeline.Restore(5);
        var replay = new Game(restored, new InputState());
        Assert.True(brawler.GetComponent<Hitboxes>()!.Hits > 0);
        for (var frame = 0; frame < 120 + 63 - 42; frame++)
            replay.Tick(Frame);
        Assert.Equal(Snapshot(live), Snapshot(restored));
    }

    [Fact]
    public void In_a_network_game_the_host_lands_the_hits_and_players_see_them()
    {
        static Scene Networked()
        {
            var scene = Sparring(out _, out _);
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>().Player = "";
            scene.Add(settings);
            return scene;
        }

        var world = new NetWorld(Networked);
        world.Run(3);
        var (host, client) = (world.Host.Scene, world.Client.Scene);
        Entity Find(Scene scene, string name) => scene.Entities.Single(e => e.Name == name);
        var hits = BoxesOf(Find(host, "Brawler")).Hits;
        Assert.True(hits > 0);
        Assert.Equal(hits, BoxesOf(Find(client, "Brawler")).Hits);
        Assert.Equal(HealthOf(Find(host, "Sandbag")), HealthOf(Find(client, "Sandbag")));
    }

    [Fact]
    public void Fighting_makes_no_garbage_until_a_hit_lands()
    {
        var puncher = Puncher(clips: "Punch 0-3 @10 loop", boxes:
        [
            FrameBox.Hitbox("Punch", 1, new Vector2(0.8f, 0), new Vector2(0.4f)),
            FrameBox.Hurtbox("Punch", 0, Vector2.Zero, new Vector2(0.6f, 2)),
            FrameBox.Hurtbox("Punch", 1, Vector2.Zero, new Vector2(0.6f, 2)),
        ]);
        // Out of reach, with boxes on every frame: every step finds the boxes and tests them.
        Target(3);
        Step(10);
        var perStep = Allocations.PerCall(() => _game.Tick(Frame), 30);
        Assert.Equal(0, BoxesOf(puncher).Hits);
        Assert.True(perStep == 0, $"A step allocated {perStep:N0} bytes.");
    }

    [Fact]
    public void Shown_the_boxes_in_play_are_drawn_over_the_game()
    {
        Puncher();
        Target();
        Step(8);
        Assert.False(_game.ShowHitboxes);
        int Drawn(Color color) => SceneRenderer.Build(_game, 16f / 9).Sprites.Count(d => d.Tint.R == color.R && d.Tint.G == color.G && d.Tint.B == color.B);
        Assert.Equal(0, Drawn(HitboxesSystem.HitColor));

        _game.ShowHitboxes = true;
        Assert.True(Drawn(HitboxesSystem.HitColor) > 0);
        Assert.True(Drawn(HitboxesSystem.StunColor) > 0);
        Step(30);
        Assert.Equal(0, Drawn(HitboxesSystem.HitColor));
        Assert.True(Drawn(HitboxesSystem.HurtColor) > 0);
    }

    [Fact]
    public void The_boxes_are_a_debug_drawing_called_Hitboxes_that_a_host_switches_by_name()
    {
        var drawing = Assert.Single(_game.DebugDrawings);
        Assert.Equal(FightingGame.HitboxesDrawing, drawing.DrawingName);
        _game.ShowDebugDrawing("hitboxes", true);
        Assert.True(_game.ShowHitboxes);
        _game.ShowDebugDrawing("Hitboxes", false);
        Assert.False(_game.ShowHitboxes);
    }
}
