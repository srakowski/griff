using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>
/// Lands hits with no code: after each physics step, every <see cref="Hitboxes"/> has the boxes of the frame its entity
/// shows, and each of its hitboxes touching an opposing hurtbox (or the collider of something with a Health on its Layers)
/// is a hit, once per group a swing. Every hit of the step is found first and then landed, so two fighters striking each
/// other at once both land. Where the game is run (offline, or the host); hitstun counts down a step at a time. With
/// <see cref="Show"/> on, it draws every box in play over the game.
/// </summary>
internal sealed class HitboxesSystem : GameSystem
{
    /// <summary>The colors the boxes are drawn in: hitboxes red, hurtboxes blue, hurtboxes in hitstun yellow.</summary>
    internal static readonly Color HitColor = new(1f, 0.3f, 0.33f);
    internal static readonly Color HurtColor = new(0.3f, 0.64f, 1f);
    internal static readonly Color StunColor = new(1f, 0.82f, 0.25f);

    private readonly List<Landing> _landings = [];
    private readonly List<int> _groupsDone = [];
    private readonly List<SpriteDraw> _draws = [];

    public override string Name => "Hitboxes";

    /// <summary>Draws every box in play over the game: the editor's Game &gt; Show Hitboxes, or <c>Game.ShowHitboxes</c>.</summary>
    public bool Show { get; set; }

    // A hit found this step, landed once they've all been found.
    private readonly record struct Landing(Hitboxes Attacker, Entity Target, Hitboxes? TargetBoxes, FrameBox Box, float Multiplier, Vector2 Point);

    // After physics, before touch damage and health: where everything is this step.
    internal void Run()
    {
        var game = Game;
        if (game.Time.Delta <= 0)
            return;
        var all = game.Active<Hitboxes>();

        // Hitstun counts down, last step's hits are over, and a clip starting over (or going round) is a new swing.
        for (var i = 0; i < all.Count; i++)
        {
            var boxes = all[i];
            if (boxes.Entity!.IsPaused)
                continue;
            if (boxes.JustHit)
                boxes.JustHit = false;
            if (boxes.JustGotHit)
                boxes.JustGotHit = false;
            if (boxes.Hitstun > 0)
                boxes.Hitstun--;
            Follow(boxes);
        }

        _landings.Clear();
        for (var i = 0; i < all.Count; i++)
        {
            var attacker = all[i];
            var entity = attacker.Entity!;
            // A fighter's hitboxes hit only while its move is active.
            if (entity.IsPaused || !FightersSystem.CanStrike(entity) || !HasHitboxes(attacker.Now()))
                continue;

            for (var j = 0; j < all.Count; j++)
            {
                var target = all[j];
                if (target != attacker && Opposes(attacker, target))
                    FindHits(attacker, target.Entity!, target);
            }

            if (!string.IsNullOrWhiteSpace(attacker.Layers))
                FindHitsOnColliders(attacker);
        }

        foreach (var landing in _landings)
            Land(landing);
        _landings.Clear();
    }

    // The swing under way: the same clip carrying on, or a new one (what it hit can be hit again).
    private static void Follow(Hitboxes boxes)
    {
        var (clip, _, loop) = boxes.Showing();
        var time = boxes.Entity!.GetComponent<Animation>()?.Time ?? 0;
        if (!string.Equals(clip, boxes.SwingClip, StringComparison.OrdinalIgnoreCase) || time < boxes.SwingTime || loop != boxes.SwingLoop)
        {
            boxes.ForgetStruck();
            if (!string.Equals(clip, boxes.SwingClip, StringComparison.Ordinal))
                boxes.SwingClip = clip;
            boxes.SwingLoop = loop;
        }

        boxes.SwingTime = time;
    }

    private static bool HasHitboxes(ReadOnlySpan<FrameBox> boxes)
    {
        foreach (var box in boxes)
        {
            if (box.IsHit)
                return true;
        }

        return false;
    }

    // Teams apart (or no team), not inside one another, and allowed to in a network game.
    private bool Opposes(Hitboxes attacker, Hitboxes target)
    {
        var (a, b) = (attacker.Entity!, target.Entity!);
        if (b.IsPaused || a.IsSelfOrDescendantOf(b) || b.IsSelfOrDescendantOf(a))
            return false;
        if (!string.IsNullOrWhiteSpace(attacker.Team) && !string.IsNullOrWhiteSpace(target.Team) &&
            string.Equals(attacker.Team.Trim(), target.Team.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        return Game.CanHurt(a, b);
    }

    // Each group of the attacker's hitboxes on this frame hits the target once, on the hurtbox that takes the most.
    private void FindHits(Hitboxes attacker, Entity target, Hitboxes boxes)
    {
        var hurts = boxes.Now();
        if (hurts.IsEmpty)
            return;
        _groupsDone.Clear();
        var hits = attacker.Now();
        foreach (var hit in hits)
        {
            if (!hit.IsHit || _groupsDone.Contains(hit.Group) || attacker.HasStruck(hit.Group, target))
                continue;
            var (hitShape, hitRound) = World(attacker.Entity!, hit);
            var best = -1;
            var point = Vector2.Zero;
            for (var k = 0; k < hurts.Length; k++)
            {
                var hurt = hurts[k];
                if (hurt.IsHit || (best >= 0 && hurt.Multiplier <= hurts[best].Multiplier))
                    continue;
                var (hurtShape, hurtRound) = World(target, hurt);
                if (Touches(hitShape, hitRound, hurtShape, hurtRound, out var at))
                    (best, point) = (k, at);
            }

            if (best < 0)
                continue;
            _groupsDone.Add(hit.Group);
            _landings.Add(new Landing(attacker, target, boxes, hit, hurts[best].Multiplier, point));
        }
    }

    // Things on its Layers with a Health and no boxes of their own, hit through their colliders.
    private void FindHitsOnColliders(Hitboxes attacker)
    {
        var entity = attacker.Entity!;
        var layers = CollisionLayers.Parse(attacker.Layers);
        var hits = attacker.Now();
        var own = Health.Of(entity);
        foreach (var health in Game.Scene.RunningNow<Health>())
        {
            var target = health.Entity!;
            if (health == own || target.IsPaused || target.GetComponent<Hitboxes>() is not null || target.IsSelfOrDescendantOf(entity) ||
                !Game.CanHurt(entity, target))
                continue;
            _groupsDone.Clear();
            foreach (var collider in target.SelfAndDescendants().SelectMany(e => e.GetComponents<Collider>()))
            {
                if (!collider.Entity!.IsActive || !CollisionLayers.Contains(layers, collider.Layer) || Health.Of(collider.Entity!) != health)
                    continue;
                var bounds = collider.WorldBounds;
                foreach (var hit in hits)
                {
                    if (!hit.IsHit || _groupsDone.Contains(hit.Group) || attacker.HasStruck(hit.Group, target))
                        continue;
                    var (shape, round) = World(entity, hit);
                    if (!Touches(shape, round, bounds, false, out var at))
                        continue;
                    _groupsDone.Add(hit.Group);
                    _landings.Add(new Landing(attacker, target, null, hit, 1, at));
                }
            }
        }
    }

    // A hit lands: damage, knockback, hitstun, messages, a sound, an effect and a hit stop. Not on what can't be hurt just now.
    private void Land(Landing landing)
    {
        var (attacker, target, boxes, box, multiplier, point) = landing;
        if (target.Scene != Game.Scene || attacker.HasStruck(box.Group, target))
            return;
        var health = Health.Of(target);
        if (health is { IsDead: true } or { IsInvincible: true })
            return;

        var game = Game;
        var from = attacker.Entity!;
        var fighter = from.GetComponent<FightingCharacter>();
        if (target.GetComponent<FightingCharacter>() is { } guard && FightersSystem.Blocks(guard, from, fighter?.Current?.Hits ?? MoveHeight.Mid))
        {
            Block(landing, guard, fighter, health);
            return;
        }

        attacker.Strike(box.Group, target);
        var wasStunned = boxes is { Hitstun: > 0 };
        var damage = box.Damage * multiplier;
        if (health is not null && damage > 0)
            health.Damage(damage, from);

        var knockback = new Vector2(box.Knockback.X * FacingOf(from), box.Knockback.Y);
        if (knockback != Vector2.Zero && target.GetComponent<Body>() is { } body)
            body.Velocity = knockback;

        var hit = new HitInfo(from, target, damage, knockback, box.Stun, box.Group, point);
        attacker.Hits++;
        attacker.JustHit = true;
        if (fighter is not null)
        {
            fighter.Connected = true;
            FightersSystem.CountCombo(fighter, target, wasStunned, damage);
        }

        if (boxes is not null)
        {
            if (box.Stun > 0)
                boxes.Hitstun = box.Stun;
            boxes.JustGotHit = true;
            if (!string.IsNullOrWhiteSpace(boxes.GotHitMessage))
                game.Send(boxes.GotHitMessage.Trim(), damage, from: target);
        }

        if (!string.IsNullOrWhiteSpace(attacker.HitMessage))
            game.Send(attacker.HitMessage.Trim(), damage, from: from);
        if (!string.IsNullOrWhiteSpace(attacker.HitSound))
            game.PlaySoundAt(attacker.HitSound.Trim(), point);
        if (!string.IsNullOrWhiteSpace(attacker.Effect) && game.PrefabLoader is not null)
            game.Spawn(attacker.Effect.Trim(), point);
        if (attacker.HitStop > 0)
            game.HitStop(attacker.HitStop);
        attacker.RaiseHit(hit);
        boxes?.RaiseGotHit(hit);
    }

    // A fighter blocks the hit: chip damage, a little pushback, blockstun, and its own sound, effect and message.
    private void Block(Landing landing, FightingCharacter guard, FightingCharacter? fighter, Health? health)
    {
        var (attacker, target, _, box, multiplier, point) = landing;
        var game = Game;
        var from = attacker.Entity!;
        var move = fighter?.Current;
        attacker.Strike(box.Group, target);
        var chip = box.Damage * multiplier * Math.Clamp(move?.Chip ?? guard.ChipDamage, 0, 1);
        if (health is not null && chip > 0)
            health.Damage(chip, from);
        var push = box.Knockback.X * FacingOf(from) * Math.Clamp(guard.BlockPushback, 0, 1);
        if (push != 0 && target.GetComponent<Body>() is { } body)
            body.Velocity = new Vector2(push, body.Velocity.Y);
        var blockstun = move is { Blockstun: > 0 } ? move.Blockstun : (int)MathF.Round(box.Stun * 2 / 3f);
        guard.Blockstun = Math.Max(guard.Blockstun, blockstun);
        (guard.JustBlocked, guard.Blocking) = (true, true);
        if (fighter is not null)
            fighter.Connected = true;

        if (!string.IsNullOrWhiteSpace(guard.BlockMessage))
            game.Send(guard.BlockMessage.Trim(), chip, from: target);
        if (!string.IsNullOrWhiteSpace(guard.BlockSound))
            game.PlaySoundAt(guard.BlockSound.Trim(), point);
        if (!string.IsNullOrWhiteSpace(guard.BlockEffect) && game.PrefabLoader is not null)
            game.Spawn(guard.BlockEffect.Trim(), point);
        if (attacker.HitStop > 0)
            game.HitStop(attacker.HitStop / 2);
        guard.RaiseBlocked(new HitInfo(from, target, chip, new Vector2(push, 0), blockstun, box.Group, point));
    }

    /// <summary>Which way <paramref name="entity"/> faces along X: 1 to the right, -1 when its sprite is flipped (or it's mirrored).</summary>
    internal static float FacingOf(Entity entity)
    {
        var flipped = entity.GetComponent<Sprite>() is { FlipX: true };
        var mirrored = entity.Transform.WorldMatrix.M11 < 0;
        return flipped != mirrored ? -1 : 1;
    }

    /// <summary>
    /// Where <paramref name="box"/> is in the world on <paramref name="entity"/>: the box round it (mirrored as its sprite
    /// is flipped, and as big as the entity is scaled), and whether it's a circle (then the box's half size is its radius).
    /// </summary>
    internal static (Box Shape, bool Round) World(Entity entity, in FrameBox box) => World(entity.Transform.WorldMatrix, entity.GetComponent<Sprite>(), box);

    internal static (Box Shape, bool Round) World(in Matrix4x4 world, Sprite? sprite, in FrameBox box)
    {
        var center = box.Center;
        if (sprite is { FlipX: true })
            center.X = -center.X;
        if (sprite is { FlipY: true })
            center.Y = -center.Y;
        var at = Vector2.Transform(center, world);
        var (x, y) = (new Vector2(world.M11, world.M12), new Vector2(world.M21, world.M22));
        if (box.IsCircle)
            return (new Box(at, new Vector2(box.Radius * MathF.Max(x.Length(), y.Length()))), true);
        var half = box.Size / 2;
        return (new Box(at, new Vector2(MathF.Abs(x.X) * half.X + MathF.Abs(y.X) * half.Y, MathF.Abs(x.Y) * half.X + MathF.Abs(y.Y) * half.Y)), false);
    }

    // Whether two shapes share any area, and the middle of where they do.
    private static bool Touches(Box a, bool aRound, Box b, bool bRound, out Vector2 point)
    {
        bool touching;
        if (aRound && bRound)
            touching = new Circle(a.Center, a.HalfSize.X).Overlaps(new Circle(b.Center, b.HalfSize.X));
        else if (aRound)
            touching = b.Overlaps(a.Center, a.HalfSize.X);
        else if (bRound)
            touching = a.Overlaps(b.Center, b.HalfSize.X);
        else
            touching = a.Overlaps(b);
        var (min, max) = (Vector2.Max(a.Min, b.Min), Vector2.Min(a.Max, b.Max));
        point = (min + max) / 2;
        return touching;
    }

    // ----- Showing the boxes in play -----

    protected override void Draw(FrameDrawing frame)
    {
        if (!Show)
            return;
        _draws.Clear();
        var thick = frame.LineWidth * 1.5f;
        foreach (var boxes in frame.Scene.RunningNow<Hitboxes>())
        {
            var entity = boxes.Entity!;
            if (!frame.ShowsInWorld(entity))
                continue;
            var world = entity.Transform.WorldMatrix;
            var sprite = entity.GetComponent<Sprite>();
            foreach (var box in boxes.Now())
            {
                var color = box.IsHit ? HitColor : boxes.Hitstun > 0 ? StunColor : HurtColor;
                // A hitbox whose group has hit already this swing is spent: drawn faint.
                var spent = box.IsHit && (boxes.Struck.Count > 0 && Spent(boxes, box.Group) || !FightersSystem.CanStrike(entity));
                var (shape, round) = World(world, sprite, box);
                if (round)
                {
                    _draws.AddRange(FrameDrawing.Circle(shape.Center, shape.HalfSize.X, thick, color with { A = spent ? 0.35f : 0.95f }));
                    _draws.Add(FrameDrawing.Quad(shape.Center, shape.HalfSize * 1.4f, color with { A = spent ? 0.06f : 0.18f }));
                }
                else
                {
                    _draws.Add(FrameDrawing.Quad(shape.Center, shape.HalfSize * 2, color with { A = spent ? 0.08f : 0.25f }));
                    _draws.AddRange(FrameDrawing.Outline(shape.Center, shape.HalfSize * 2, thick, color with { A = spent ? 0.35f : 0.95f }));
                }
            }
        }

        if (_draws.Count > 0)
            frame.AddOverWorld(_draws);
    }

    private static bool Spent(Hitboxes boxes, int group)
    {
        foreach (var line in boxes.Struck)
        {
            var space = line.IndexOf(' ');
            if (space > 0 && int.TryParse(line.AsSpan(0, space), out var struck) && struck == group)
                return true;
        }

        return false;
    }
}
