using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Breaks things with no code: after each physics step, every hard knock a <see cref="Breakable"/> took adds to its damage
/// (and its Health's), and one whose damage reaches its strength, whose Health has died or that's been told to breaks: it
/// splits into pieces or vanishes in dust, with its sound and messages. Where the game is run (offline, or the host); the
/// pieces, the dust and the cracks reach players' machines as everything else does.
/// </summary>
internal sealed class BreakablesSystem : GameSystem
{
    // How hard pieces fly apart from where the thing broke, in world units a second, and spin as they go (degrees a second).
    private const float Burst = 1.5f;
    private const float PieceSpin = 90;

    public override string Name => "Breakables";

    // After physics: the knocks it found.
    internal void Run()
    {
        var game = Game;
        if (game.Time.Delta <= 0)
            return;
        var scene = game.Scene;
        var impacts = game.Physics.ImpactList;
        for (var i = 0; i < impacts.Count; i++)
        {
            var impact = impacts[i];
            var entity = impact.Struck;
            if (entity.Scene != scene || entity.GetComponent<Breakable>() is not { Broken: false } breakable || !entity.IsActive ||
                entity.IsPaused || !Game.Runs(entity))
                continue;
            if (impact.Strength >= breakable.MinHit)
                Hurt(breakable, impact.Strength, impact.By);
        }

        // What was told to break, to take damage or to mend, and what's breakable whose Health has died.
        foreach (var breakable in game.RunHere<Breakable>())
        {
            var entity = breakable.Entity!;
            if (entity.IsPaused)
                continue;
            if (breakable.RepairRequested)
            {
                breakable.RepairRequested = false;
                Repair(breakable);
                continue;
            }

            if (!entity.IsActive || breakable.Broken)
            {
                (breakable.BreakRequested, breakable.Requested) = (false, 0);
                continue;
            }

            if (breakable.Requested > 0)
            {
                var amount = breakable.Requested;
                breakable.Requested = 0;
                Hurt(breakable, amount, null);
            }

            if (breakable.BreakRequested || (entity.GetComponent<Health>() is { IsDead: true } && !breakable.Broken))
            {
                breakable.BreakRequested = false;
                if (!breakable.Broken)
                    Break(breakable, null);
            }

            ShowStage(breakable);
        }
    }

    // A hit that counts: its damage added up (and taken off its Health), and broken once that's enough.
    private void Hurt(Breakable breakable, float damage, Entity? by)
    {
        var entity = breakable.Entity!;
        breakable.Damage += damage;
        var health = entity.GetComponent<Health>();
        health?.Damage(damage, by);
        breakable.RaiseHit(damage, by);
        if (!string.IsNullOrWhiteSpace(breakable.DamageMessage))
            Game.Send(breakable.DamageMessage.Trim(), damage, from: entity);

        if ((breakable.Strength > 0 && breakable.Damage >= breakable.Strength) || health is { IsDead: true })
        {
            Break(breakable, by);
            return;
        }

        if (!string.IsNullOrWhiteSpace(breakable.HitSound))
            Game.PlaySoundAt(breakable.HitSound.Trim(), Middle(entity));
        ShowStage(breakable);
    }

    // On a sprite sheet of stages, the frame of the stage it's at.
    private static void ShowStage(Breakable breakable)
    {
        var stage = breakable.Stage;
        if (stage == breakable.ShownStage)
            return;
        breakable.ShownStage = stage;
        var entity = breakable.Entity!;
        if (Breakable.UsesFrames(entity) && entity.GetComponent<Sprite>() is { } sprite)
            sprite.Frame = Math.Min(stage, sprite.Columns * sprite.Rows - 1);
    }

    // Broken: its pieces (or dust, or its effect), its sound and message, and it's taken away (or turned off).
    private void Break(Breakable breakable, Entity? by)
    {
        var entity = breakable.Entity!;
        breakable.Broken = true;
        var middle = Middle(entity);
        if (breakable.Split >= 2)
            MakePieces(breakable);
        if (!string.IsNullOrWhiteSpace(breakable.Effect))
            SpawnEffect(breakable, middle);
        else if (breakable.Dust > 0)
            MakeDust(breakable, middle);

        if (!string.IsNullOrWhiteSpace(breakable.Sound))
            Game.PlaySoundAt(breakable.Sound.Trim(), middle);
        if (breakable.ShowPoints && breakable.Points != 0)
            PointsPopup.Spawn(Game, middle, breakable.Points, breakable.PointsColor, 0.55f);
        if (!string.IsNullOrWhiteSpace(breakable.BreakMessage))
            Game.Send(breakable.BreakMessage.Trim(), breakable.Points != 0 ? breakable.Points : entity, from: entity);
        breakable.RaiseBroke();

        if (breakable.Remove)
            Game.Destroy(entity);
        else
            entity.IsEnabled = false;
    }

    // Mended: whole again, and back if it was left turned off.
    private static void Repair(Breakable breakable)
    {
        var entity = breakable.Entity!;
        (breakable.Damage, breakable.Requested, breakable.BreakRequested) = (0, 0, false);
        if (breakable.Broken)
        {
            breakable.Broken = false;
            if (!entity.IsEnabled)
                entity.IsEnabled = true;
        }

        ShowStage(breakable);
    }

    // Where it is in the world: the middle of what it's made of.
    private static Vector2 Middle(Entity entity) =>
        entity.GetComponent<Body>() is { } body ? body.CenterOfMass : entity.GetComponent<Collider>() is { } collider ? collider.WorldBounds.Center
            : new Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // Its pieces: Split along its longer side and as many across as keeps them about square (a plank in halves, a crate in
    // quarters), each a part of its shape and its sprite, moving and turning as it was, flying apart a little, and fading
    // away after Pieces Last seconds.
    private void MakePieces(Breakable breakable)
    {
        var entity = breakable.Entity!;
        var split = Math.Min(breakable.Split, 6);
        var (center, half, world) = Breakable.Shape(entity);
        if (!(half.X > 0) || !(half.Y > 0))
            return;
        var body = entity.GetComponent<Body>();
        var collider = entity.GetComponent<Collider>();
        var sprite = entity.GetComponent<Sprite>();
        var scale = new Vector2(new Vector2(world.M11, world.M12).Length(), new Vector2(world.M21, world.M22).Length());
        var longest = MathF.Max(half.X * scale.X, half.Y * scale.Y);
        var (across, down) = (Count(half.X * scale.X), Count(half.Y * scale.Y));
        int Count(float side) => Math.Clamp((int)MathF.Round(split * side / longest), 1, split);
        var turn = MathF.Atan2(world.M12, world.M11) * 180 / MathF.PI;
        var size = half * 2 / new Vector2(across, down);
        var middle = Vector2.Transform(center, world);
        var spin = body?.Spin ?? 0;
        var z = entity.Transform.WorldPosition.Z;

        for (var row = 0; row < down; row++)
        {
            for (var column = 0; column < across; column++)
            {
                var own = center - half + new Vector2((column + 0.5f) * size.X, (row + 0.5f) * size.Y);
                var at = Vector2.Transform(own, world);
                var piece = new Entity($"{entity.Name} Piece");
                piece.Transform.Position = new Vector3(at, z);
                piece.Transform.Rotation = new Vector3(0, 0, turn);
                piece.Transform.Scale = new Vector3(scale, 1);

                if (sprite is not null)
                {
                    var look = piece.AddComponent<Sprite>();
                    (look.Image, look.Tint, look.Order, look.FlipX, look.FlipY) = (sprite.Image, sprite.Tint, sprite.Order, sprite.FlipX, sprite.FlipY);
                    look.Size = sprite.Size / new Vector2(across, down);
                    if (!string.IsNullOrEmpty(sprite.Image) && sprite.Columns * sprite.Rows <= 1)
                    {
                        // Its part of the picture: frames count from the top left, rows from the bottom up here.
                        var x = sprite.FlipX ? across - 1 - column : column;
                        var y = sprite.FlipY ? row : down - 1 - row;
                        (look.Columns, look.Rows, look.Frame) = (across, down, y * across + x);
                    }
                }

                // Round things break into round pieces; the rest into boxes.
                Collider shape;
                if (collider is CircleCollider)
                    shape = new CircleCollider { Radius = MathF.Min(size.X, size.Y) * 0.48f };
                else
                    shape = new BoxCollider { Size = size * 0.96f };
                piece.AddComponent(shape);
                if (collider is not null)
                    (shape.Layer, shape.IgnoreLayers) = (collider.Layer, collider.IgnoreLayers);

                var moves = piece.AddComponent<Body>();
                moves.Rotates = true;
                var outward = at - middle;
                outward = outward.LengthSquared() > 1e-8f ? Vector2.Normalize(outward) : Vector2.UnitY;
                if (body is not null)
                {
                    (moves.Mass, moves.GravityScale, moves.Bounciness, moves.Grip, moves.Friction) =
                        (Math.Max(0.05f, body.Mass / (across * down)), body.GravityScale, body.Bounciness, body.Grip, body.Friction);

                    // Moving as that part of it moved: with it, and round its middle as it spun.
                    var arm = at - body.CenterOfMass;
                    var radians = spin * MathF.PI / 180;
                    moves.Velocity = body.Velocity + new Vector2(-radians * arm.Y, radians * arm.X) + outward * Burst;
                }
                else
                {
                    moves.Velocity = outward * Burst;
                }

                moves.Spin = spin + ((row + column) % 2 == 0 ? PieceSpin : -PieceSpin);

                if (breakable.PiecesLast > 0)
                {
                    var lifetime = piece.AddComponent<Lifetime>();
                    (lifetime.Seconds, lifetime.FadeOut) = (breakable.PiecesLast, Math.Min(0.6f, breakable.PiecesLast / 2));
                }

                Game.Spawn(piece);
            }
        }
    }

    // A puff of dust in its color, gone once it has settled.
    private void MakeDust(Breakable breakable, Vector2 at)
    {
        var entity = breakable.Entity!;
        var sprite = entity.GetComponent<Sprite>();
        var color = sprite?.Tint ?? Color.FromBytes(0xC8, 0xB8, 0xA0);
        var dust = new Entity($"{entity.Name} Dust");
        dust.Transform.Position = new Vector3(at, entity.Transform.WorldPosition.Z);
        var emitter = dust.AddComponent<ParticleEmitter>();
        (emitter.Emitting, emitter.BurstOnStart, emitter.DestroyWhenFinished) = (false, breakable.Dust, true);
        (emitter.Lifetime, emitter.Speed, emitter.SpeedVariation, emitter.Spread, emitter.GravityScale) = (0.7f, 3.5f, 0.7f, 360, 0.6f);
        (emitter.StartSize, emitter.EndSize, emitter.StartColor, emitter.EndColor) = (0.18f, 0.04f, color, color with { A = 0 });
        emitter.Order = (sprite?.Order ?? 0) + 1;
        Game.Spawn(dust);
    }

    // Its Effect prefab where it broke.
    private void SpawnEffect(Breakable breakable, Vector2 at)
    {
        Game.TrySpawn(breakable.Effect!, breakable, "its Effect", at);
    }
}
