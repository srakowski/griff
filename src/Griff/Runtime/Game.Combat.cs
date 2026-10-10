using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Close-up combat: attacks that swing a hitbox out and back.
public sealed partial class Game
{
    // Hitboxes out this frame and the attack each belongs to, so touch damage hurts each thing once per swing.
    private readonly Dictionary<Entity, Attack> _swinging = [];

    // After scripts, so a script's Swing() starts this frame; before physics, so a hitbox coming out touches things straight away.
    private void RunAttacks()
    {
        _swinging.Clear();
        foreach (var attack in Active<Attack>())
        {
            var owner = attack.Entity!;
            var hitbox = HitboxOf(attack);
            if (hitbox is not null)
                attack.Resting ??= (hitbox.Transform.Position, hitbox.GetComponent<BoxCollider>()?.Size);

            // Nothing swings while game time stands still: a pause, a hit stop, a conversation.
            if (Time.Delta > 0 && !PausedThisFrame)
            {
                var action = attack.Action?.Trim();
                if (!string.IsNullOrEmpty(action))
                {
                    if (InputOf(owner).WasPressed(action))
                        attack.Queued = true;
                }
                else if (attack.Phase == AttackPhase.Ready && TargetInRange(attack) is { } target)
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
            if (owner.GetComponent<Animation>() is { } animation)
                PlayFirst(animation, AttackClips(owner));
        }

        attack.PhaseTime += Time.Delta;
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
            Audio.Play(attack.Sound.Trim());
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
    private static Vector2 FacingOf(Entity entity)
    {
        if (entity.GetComponent<PlatformerMovement>() is { } platformer)
            return new Vector2(platformer.Facing < 0 ? -1 : 1, 0);
        if (entity.GetComponent<TopDownMovement>() is { } topDown && topDown.Facing != Vector2.Zero)
        {
            var f = topDown.Facing;
            return MathF.Abs(f.Y) > MathF.Abs(f.X) ? new Vector2(0, MathF.Sign(f.Y)) : new Vector2(MathF.Sign(f.X), 0);
        }

        return new Vector2(entity.GetComponent<Sprite>()?.FlipX == true ? -1 : 1, 0);
    }

    private static void FaceTowards(Entity entity, Entity target)
    {
        var dx = target.Transform.WorldPosition.X - entity.Transform.WorldPosition.X;
        if (MathF.Abs(dx) > 1e-3f)
            Face(entity, MathF.Sign(dx));
    }

    private Entity? TargetInRange(Attack attack)
    {
        var from = attack.Entity!.Transform.WorldPosition;
        if (string.IsNullOrWhiteSpace(attack.Target) || Nearest(attack.Target.Trim(), new Vector2(from.X, from.Y)) is not { } target || !Rendering.SceneRenderer.IsActive(target))
            return null;
        var to = target.Transform.WorldPosition;
        return Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y)) <= attack.Range ? target : null;
    }

    // The hitbox: the entity inside the attacker with the attack's Hitbox name.
    private static Entity? HitboxOf(Attack attack) =>
        string.IsNullOrWhiteSpace(attack.Hitbox)
            ? null
            : attack.Entity!.SelfAndDescendants().FirstOrDefault(e => e != attack.Entity && e.Name == attack.Hitbox.Trim());

    // An "Attack" clip, facing up or down first for a top-down attacker.
    private static string[] AttackClips(Entity owner)
    {
        var facing = FacingOf(owner);
        return MathF.Abs(facing.Y) > MathF.Abs(facing.X) ? ["Attack" + (facing.Y > 0 ? "Up" : "Down"), "Attack"] : ["Attack"];
    }

    /// <summary>True while an attack is under way and the entity has a clip for it, so movement leaves its animation alone.</summary>
    private static bool IsPlayingAttack(Entity entity, Animation animation) =>
        entity.GetComponent<Attack>() is { IsAttacking: true } && AttackClips(entity).Any(animation.HasClip);

    // A hitbox touching something hurts it once per swing; true if this touch should be skipped.
    private bool AlreadyStruck(Entity source, Entity hurt, out Attack? swing)
    {
        if (!_swinging.TryGetValue(source, out swing))
            return false;
        return !swing.Struck.Add(hurt);
    }

    private void Landed(Attack swing, Entity hurt)
    {
        swing.RaiseHit(hurt);
        if (swing.HitStop > 0)
            HitStop(swing.HitStop);
    }
}
