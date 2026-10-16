using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Combat with no code: attacks that swing a hitbox out and back, what hurts by touching (Damage On Touch), and health
/// taking its hurts and deaths. Keeps the attacks swinging now.
/// </summary>
internal sealed class CombatSystem : GameSystem
{
    public override string Name => "Combat";

    // Hitboxes out this frame and the attack each belongs to, so touch damage hurts each thing once per swing.
    private readonly Dictionary<Entity, Attack> _swinging = [];

    // After scripts, so a script's Swing() starts this frame; before physics, so a hitbox coming out touches things straight away.
    internal void RunAttacks()
    {
        _swinging.Clear();
        foreach (var attack in Game.Active<Attack>())
        {
            var owner = attack.Entity!;
            var hitbox = HitboxOf(attack);
            if (hitbox is not null)
                attack.Resting ??= (hitbox.Transform.Position, hitbox.GetComponent<BoxCollider>()?.Size);

            // Nothing swings while game time stands still: a pause, a hit stop, a conversation.
            if (Game.Time.Delta > 0 && !Game.PausedThisFrame)
            {
                var action = attack.Action?.Trim();
                if (!string.IsNullOrEmpty(action))
                {
                    if (Game.InputOf(owner).WasPressed(action))
                        attack.Queued = true;
                }
                else if (attack.Phase == AttackPhase.Ready && !Game.Waiting(owner) && TargetInRange(attack) is { } target)
                {
                    FaceTowards(owner, target);
                    attack.Queued = true;
                }

                if (attack.Queued && attack.Phase == AttackPhase.Ready)
                    attack.Swing();
                if (attack.Phase != AttackPhase.Ready)
                    Advance(attack, owner);
            }

            if (hitbox is null)
                continue;
            if (hitbox.IsEnabled != attack.IsHitting)
                hitbox.IsEnabled = attack.IsHitting;
            if (attack.IsHitting)
            {
                Aim(attack, hitbox);
                _swinging[hitbox] = attack;
            }
        }
    }

    // Through the phases by this frame's game time; a phase with no time passes straight on to the next.
    private void Advance(Attack attack, Entity owner)
    {
        if (attack.Phase == AttackPhase.Windup && attack.PhaseTime == 0)
        {
            attack.Queued = false;
            Game.UseAction(owner);
            // From its first frame each swing, even straight after the last (so the frames that hit come round again).
            if (owner.GetComponent<Animation>() is { } animation && AttackClips(owner).FirstOrDefault(animation.HasClip) is { } clip)
                animation.Play(clip, restart: true);
        }

        attack.PhaseTime += Game.Time.Delta;
        while (true)
        {
            var length = attack.Phase switch
            {
                AttackPhase.Windup => attack.Windup,
                AttackPhase.Active => attack.ActiveTime,
                _ => attack.Cooldown,
            };
            if (attack.PhaseTime < length)
                return;
            attack.PhaseTime -= Math.Max(0, length);
            attack.Phase = attack.Phase switch
            {
                AttackPhase.Windup => AttackPhase.Active,
                AttackPhase.Active => AttackPhase.Cooldown,
                _ => AttackPhase.Ready,
            };

            if (attack.Phase == AttackPhase.Ready)
            {
                attack.PhaseTime = 0;
                return;
            }

            if (attack.Phase == AttackPhase.Active)
                Strike(attack, owner);
        }
    }

    // The hitbox comes out: a sound, a lunge forward.
    private void Strike(Attack attack, Entity owner)
    {
        if (!string.IsNullOrWhiteSpace(attack.Sound))
            Game.Audio.Play(attack.Sound.Trim());
        if (attack.Lunge != 0 && owner.GetComponent<Body>() is { } body)
            body.Velocity += FacingOf(owner) * attack.Lunge;
        attack.RaiseSwung();
    }

    // Placed facing right in the editor, the hitbox is mirrored to face left, or turned up or down (top-down games).
    private static void Aim(Attack attack, Entity hitbox)
    {
        if (attack.Resting is not var (position, size))
            return;
        var facing = FacingOf(attack.Entity!);
        var vertical = MathF.Abs(facing.Y) > MathF.Abs(facing.X);
        var reach = MathF.Abs(position.X);
        var placed = vertical
            ? new Vector3(-position.Y * MathF.Sign(facing.Y), reach * MathF.Sign(facing.Y), position.Z)
            : new Vector3(reach * (facing.X < 0 ? -1 : 1), position.Y, position.Z);
        if (hitbox.Transform.Position != placed)
            hitbox.Transform.Position = placed;
        if (size is { } resting && hitbox.GetComponent<BoxCollider>() is { } collider)
        {
            var turned = vertical ? new Vector2(resting.Y, resting.X) : resting;
            if (collider.Size != turned)
                collider.Size = turned;
        }
    }

    // Which way an entity faces: its movement's facing, else its sprite's (drawn facing right, flipped to face left).
    internal static Vector2 FacingOf(Entity entity)
    {
        if (entity.GetComponent<PlatformerMovement>() is { } platformer)
            return new Vector2(platformer.Facing < 0 ? -1 : 1, 0);
        if (entity.GetComponent<TopDownMovement>() is { } topDown && topDown.Facing != Vector2.Zero)
        {
            var f = topDown.Facing;
            return MathF.Abs(f.Y) > MathF.Abs(f.X) ? new Vector2(0, MathF.Sign(f.Y)) : new Vector2(MathF.Sign(f.X), 0);
        }

        if (entity.GetComponent<GridMovement>() is { } grid && grid.Facing != Vector2.Zero)
        {
            var cells = GridCells.Of(grid.FindMap());
            if (!cells.IsIsometric && !cells.IsHex)
                return grid.Facing.X != 0 ? new Vector2(MathF.Sign(grid.Facing.X), 0) : new Vector2(0, MathF.Sign(grid.Facing.Y));

            // On an isometric or hex map, the way it faces on the screen: a step up to the right swings right on an
            // isometric map, and up on a hex map, where it's steeper.
            var seen = cells.InWorld(grid.Facing);
            return MathF.Abs(seen.X) > MathF.Abs(seen.Y) ? new Vector2(MathF.Sign(seen.X), 0) : new Vector2(0, MathF.Sign(seen.Y));
        }

        return new Vector2(entity.GetComponent<Sprite>()?.FlipX == true ? -1 : 1, 0);
    }

    private static void FaceTowards(Entity entity, Entity target)
    {
        // A grid mover turns to face it up or down too, so the hitbox swings its way.
        if (entity.GetComponent<GridMovement>() is { } grid)
            grid.Facing = GridCells.Of(grid.FindMap()).Toward(Game.Flat(target.Transform.WorldPosition) - Game.Flat(entity.Transform.WorldPosition), grid.Diagonal);
        var dx = target.Transform.WorldPosition.X - entity.Transform.WorldPosition.X;
        if (MathF.Abs(dx) > 1e-3f)
            Game.Face(entity, MathF.Sign(dx));
    }

    private Entity? TargetInRange(Attack attack)
    {
        var from = attack.Entity!.Transform.WorldPosition;
        if (string.IsNullOrWhiteSpace(attack.Target) || Game.Nearest(attack.Target.Trim(), new Vector2(from.X, from.Y)) is not { } target || !target.IsActive)
            return null;
        var to = target.Transform.WorldPosition;
        return Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y)) <= attack.Range ? target : null;
    }

    // The hitbox: the entity inside the attacker with the attack's Hitbox name.
    private static Entity? HitboxOf(Attack attack) =>
        string.IsNullOrWhiteSpace(attack.Hitbox)
            ? null
            : EntityNames.Find(attack.Entity!.Scene!, attack.Hitbox, new NameLookup(attack.Entity, Scope: NameScope.Inside, ExceptFrom: true));

    // An "Attack" clip, facing up or down first for a top-down attacker.
    private static string[] AttackClips(Entity owner)
    {
        var facing = FacingOf(owner);
        return MathF.Abs(facing.Y) > MathF.Abs(facing.X) ? ["Attack" + (facing.Y > 0 ? "Up" : "Down"), "Attack"] : ["Attack"];
    }

    /// <summary>True while an attack is under way and the entity has a clip for it, so movement leaves its animation alone.</summary>
    internal static bool IsPlayingAttack(Entity entity, Animation animation) =>
        entity.GetComponent<Attack>() is { IsAttacking: true } && AttackClips(entity).Any(animation.HasClip);

    // A hitbox touching something hurts it once per swing; true if this touch should be skipped.
    internal bool AlreadyStruck(Entity source, Entity hurt, out Attack? swing)
    {
        if (!_swinging.TryGetValue(source, out swing))
            return false;
        return !swing.Struck.Add(hurt);
    }

    internal void Landed(Attack swing, Entity hurt)
    {
        swing.RaiseHit(hurt);
        if (swing.HitStop > 0)
            Game.HitStop(swing.HitStop);
    }

    // Each pair that touched this frame (bumped, or overlapping as a trigger) counts once, however many times physics saw it.
    internal void RunTouchDamage()
    {
        foreach (var (source, target) in Game.Touches)
        {
            // Nothing paused hurts or is hurt by a touch (see Entity.IsPaused).
            if (source.Scene != Game.Scene || target.Scene != Game.Scene || !Game.Runs(source) || source.GetComponent<DamageOnTouch>() is not { } touch ||
                source.IsPaused || target.IsPaused)
                continue;
            if (touch.DestroyOnWall && IsWall(target) && !(Health.Of(target) is { } struck && struck != Health.Of(source) && touch.Hurts(Game.LayerOf(target))))
            {
                Game.Destroy(source);
                continue;
            }

            if (Health.Of(target) is not { } health || health == Health.Of(source) || !touch.Hurts(Game.LayerOf(target)) || !Game.CanHurt(source, health.Entity!))
                continue;

            var hurt = health.Entity!;
            if (AlreadyStruck(source, hurt, out var swing))
                continue;
            var landed = touch.Kill ? !health.IsDead && KillNow(health) : health.Damage(touch.RollAmount(), source);
            if (landed && touch.Knockback > 0 && hurt.GetComponent<Body>() is { } body)
            {
                var away = hurt.Transform.WorldPosition - source.Transform.WorldPosition;
                var direction = new Vector2(away.X, away.Y);
                body.Velocity = (direction.LengthSquared() > 1e-8f ? Vector2.Normalize(direction) : Vector2.UnitY) * touch.Knockback;
            }

            if (landed && swing is not null)
                Landed(swing, hurt);
            if (touch.DestroyOnHit)
                Game.Destroy(source);
        }

        Game.Touches.Clear();

        static bool KillNow(Health health)
        {
            health.Kill();
            return true;
        }
    }

    // Something solid to run into: a solid tilemap, or a collider that isn't a trigger.
    internal static bool IsWall(Entity entity) =>
        entity.GetComponent<Tilemap>() is { Solid: true } || entity.GetComponents<Collider>().Any(c => !c.IsTrigger);

    // Deaths from this frame (touches, scripts, triggers) send their message and are destroyed along with everything else.
    internal void RunHealth()
    {
        foreach (var health in Game.Active<Health>())
        {
            health.Home ??= new Vector2(health.Entity!.Transform.WorldPosition.X, health.Entity.Transform.WorldPosition.Y);
            health.Advance(Time.Delta);
            var at = new Vector2(health.Entity!.Transform.WorldPosition.X, health.Entity.Transform.WorldPosition.Y);
            if (health.Hurt)
            {
                health.Hurt = false;
                if (!string.IsNullOrWhiteSpace(health.HurtSound))
                    Game.PlaySoundAt(health.HurtSound.Trim(), at);
            }

            if (!health.TakeDeath())
                continue;
            Game.Net.CountDefeat(health.Entity!, health.LastHitBy);
            if (!string.IsNullOrWhiteSpace(health.DeathSound))
                Game.PlaySoundAt(health.DeathSound.Trim(), at);
            if (!string.IsNullOrWhiteSpace(health.DeathMessage))
                Game.Send(health.DeathMessage, health.Entity, from: health.Entity);
            if (health.Respawn)
                health.Entity!.IsEnabled = false;
            else if (health.DestroyOnDeath)
                Game.Destroy(health.Entity!);
        }
    }
}
