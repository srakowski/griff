using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Players with no code (platformer and top-down movement, ladders; grid movers step through GridSystem) and the level
/// pieces they play with: bouncers and crumbling platforms. Pushable crates are physics.
/// </summary>
internal sealed class MovementSystem : GameSystem
{
    public override string Name => "Movement";

    // Seconds after leaving a wall that a wall jump still works.
    private const float WallCoyoteTime = 0.12f;

    // Seconds after a wall jump before the stick steers again, so it kicks clear of the wall.
    private const float WallJumpLockTime = 0.15f;

    // Seconds after jumping off a ladder before it grabs one again, so holding up while jumping off doesn't catch it at once.
    private const float RegrabTime = 0.3f;

    // Falling faster than this (units per second) when it lands counts as a landing, with its sound and dust.
    private const float LandingSpeed = 2;

    // Like enemies, players move before scripts run, so a script can still take over the body this frame.
    internal void RunMovement()
    {
        if (Game.Time.Delta <= 0 || Game.PausedThisFrame)
            return;

        // Ropes laid out (or brought back from a rewind) before anyone grabs one.
        if (Game.Scene.ActiveNow<Rope>().Count > 0)
            Game.Physics.ReadyRopes(Game.Scene);
        foreach (var movement in Game.Moving<PlatformerMovement>())
            RunPlatformer(movement);
        foreach (var movement in Game.Moving<TopDownMovement>())
            RunTopDown(movement);
        foreach (var movement in Game.Moving<GridMovement>())
            Game.GridSystem.RunGrid(movement);
    }

    private void RunPlatformer(PlatformerMovement m)
    {
        var entity = m.Entity!;
        var keys = Game.InputOf(entity);
        if (entity.GetComponent<Body>() is not { } body)
            return;

        var dt = Game.Time.Delta;
        var control = m.Controllable;
        var x = control ? keys.Horizontal : 0;
        var y = control ? keys.Vertical : 0;
        var v = body.Velocity;
        var grounded = body.IsGrounded;
        var gravity = -Game.Physics.Gravity.Y * body.GravityScale;
        var jumpSpeed = m.JumpSpeedFor(gravity);
        var jumped = false;

        // Its air jumps from the start, even if it starts in the air.
        if (!m.Started)
        {
            m.Started = true;
            m.JumpsLeft = m.AirJumps;
        }

        if (grounded)
        {
            if (!m.WasGrounded && m.FallSpeed > LandingSpeed)
            {
                Game.PlayIfSet(m.LandSound);
                SpawnDust(m, entity);
                m.RaiseLanded(m.FallSpeed);
            }

            m.JumpsLeft = m.AirJumps;
            m.AirDashUsed = false;
            m.IsJumping = false;
        }

        m.WasGrounded = grounded;
        m.IsWallSliding = false;
        m.DashCooldownLeft = Math.Max(0, m.DashCooldownLeft - dt);
        m.WallJumpLock = Math.Max(0, m.WallJumpLock - dt);
        m.RegrabLeft = Math.Max(0, m.RegrabLeft - dt);
        if (body.WallSide != 0)
            (m.LastWallSide, m.WallAge) = (body.WallSide, 0);
        else
            m.WallAge += dt;

        // Ropes: up grabs one at its hands (in the air too); on one, it climbs up and down it, swings on it left and right,
        // and jumps off it (or, holding down, lets go). The rope moves it.
        var bounds = entity.GetComponent<Collider>() is { } collider ? Physics.BoundsOf(collider) : new Box(Game.Flat(entity.Transform.WorldPosition), Vector2.Zero);
        if (body.Climbing is { } rope && (rope.Entity is not { IsActive: true } ropeEntity || ropeEntity.Scene != Game.Scene || !rope.Climbable))
            Physics.Release(body);
        if (body.Climbing is null && m.IsOnRope)
            (m.IsOnRope, m.IsClimbing) = (false, false);
        if (body.Climbing is null && y > 0.5f && !m.IsDashing && m.RegrabLeft <= 0 && bounds.HalfSize != Vector2.Zero && !body.Rotates)
        {
            var hands = bounds.Center + new Vector2(0, bounds.HalfSize.Y * 0.6f);
            if (Physics.RopeNear(Game.Scene, hands, bounds.HalfSize.X + 0.25f, body) is { } grab)
            {
                Physics.Hold(grab.Rope, body, grab.At);
                Shift(entity, grab.Point - hands);
            }
        }

        if (body.Climbing is not null)
        {
            (m.IsOnRope, m.IsClimbing, m.IsJumping) = (true, true, false);
            m.JumpsLeft = m.AirJumps;
            m.AirDashUsed = false;
            Physics.Steer(body, MathF.Abs(y) > 0.1f ? y * m.ClimbSpeed : 0, x);
            v = body.Velocity;
            if (control && keys.WasPressed("Jump", Math.Max(0, m.JumpBuffer)))
            {
                // Jump off it, keeping its swing; or just let go, holding down.
                Physics.Release(body);
                (m.IsOnRope, m.IsClimbing, m.RegrabLeft) = (false, false, RegrabTime);
                if (y >= -0.5f)
                {
                    v = new Vector2(v.X + x * m.Speed * 0.5f, MathF.Max(v.Y + jumpSpeed * 0.5f, jumpSpeed));
                    Jumped(m, ref jumped, sound: true);
                }
            }

            if (MathF.Abs(x) > 0.1f)
                m.Facing = MathF.Sign(x);
            Finish();
            return;
        }

        // Ladders: up grabs one it's in front of, down one under its feet (below the one-way floor it stands on, if that's
        // what it's on). It stays on while any of it is on the ladder, or it's still climbing up through the floor at the top.
        var oneWayFloor = grounded ? Physics.OneWayFloorBottom(Game.Scene, body) : null;
        var reach = bounds.HalfSize.X / 2;
        var front = LadderAt(bounds.Center, reach);
        var under = LadderAt(new Vector2(bounds.Center.X, (oneWayFloor ?? bounds.Min.Y) - 0.05f), reach);
        if (m.IsClimbing && front is null && under is null && !Physics.FeetInOneWay(Game.Scene, body))
            m.IsClimbing = false;
        if (m.IsClimbing && grounded && y < -0.5f && under is null)
            m.IsClimbing = false;
        if (!m.IsClimbing && !m.IsDashing && m.RegrabLeft <= 0 && ((y > 0.5f && front is not null) || (y < -0.5f && under is not null && (!grounded || oneWayFloor is not null))))
        {
            m.IsClimbing = true;
            m.IsJumping = false;
            if ((y > 0 ? front : under)?.SnapX is { } snap)
                Shift(entity, new Vector2(snap - bounds.Center.X, 0));
        }

        if (m.IsClimbing)
        {
            body.FloatThisStep = true;
            body.PassOneWayThisStep = true;
            v = new Vector2(x * m.ClimbSpeed, y * m.ClimbSpeed);
            m.JumpsLeft = m.AirJumps;
            m.AirDashUsed = false;
            if (control && keys.WasPressed("Jump", Math.Max(0, m.JumpBuffer)))
            {
                // Jump off it, or just let go while holding down.
                m.IsClimbing = false;
                m.RegrabLeft = RegrabTime;
                v.Y = y < -0.5f ? 0 : jumpSpeed;
                if (v.Y > 0)
                    Jumped(m, ref jumped, sound: true);
            }

            if (MathF.Abs(x) > 0.1f)
                m.Facing = MathF.Sign(x);
            Finish();
            return;
        }

        // A dash goes straight, ignoring gravity, then carries on at running speed.
        if (m.Dash && control && m.DashLeft <= 0 && m.DashCooldownLeft <= 0 && (grounded || !m.AirDashUsed) && keys.WasPressed("Dash"))
        {
            m.DashDirection = x > 0.1f ? 1 : x < -0.1f ? -1 : m.Facing;
            m.DashLeft = Math.Max(0, m.DashTime);
            m.DashCooldownLeft = m.DashLeft + Math.Max(0, m.DashCooldown);
            m.AirDashUsed |= !grounded;
            m.IsJumping = false;
            Game.PlayIfSet(m.DashSound);
            m.RaiseDashed();
        }

        if (m.DashLeft > 0)
        {
            m.DashLeft -= dt;
            body.FloatThisStep = true;
            m.Facing = m.DashDirection;
            v = new Vector2(m.DashDirection * m.DashSpeed, 0);
            if (m.DashLeft <= 0)
                v.X = m.DashDirection * m.Speed;
            Finish();
            return;
        }

        // Running: up to speed and back down again at its rates, less of them in the air.
        if (m.WallJumpLock <= 0)
        {
            var target = x * m.Speed;
            var rate = MathF.Abs(target) > 1e-3f ? m.Acceleration : m.Deceleration;
            var air = Math.Clamp(m.AirControl, 0, 1);
            if (grounded || air > 0)
                v.X = rate > 0 ? Game.MoveTowards(v.X, target, rate * (grounded ? 1 : air) * dt) : target;
        }

        // Jumping: from the ground (a moment after leaving it, too), off a wall, or in the air; or down through the floor.
        var canGround = body.WasGrounded(Math.Max(0, m.CoyoteTime));
        var canWall = m.WallJump && !grounded && m.WallAge <= WallCoyoteTime && m.LastWallSide != 0;
        var canAir = m.JumpsLeft > 0;
        var drop = m.DropThrough && y < -0.5f && oneWayFloor is not null;
        if (control && (canGround || canWall || canAir || drop) && keys.WasPressed("Jump", Math.Max(0, m.JumpBuffer)))
        {
            if (drop)
            {
                body.DropThrough();
            }
            else if (canGround)
            {
                v.Y = jumpSpeed;
                SpawnDust(m, entity);
                Jumped(m, ref jumped, sound: true);
            }
            else if (canWall)
            {
                v = new Vector2(-m.LastWallSide * m.Speed, jumpSpeed);
                m.WallJumpLock = WallJumpLockTime;
                m.Facing = -m.LastWallSide;
                m.WallAge = float.PositiveInfinity;
                Jumped(m, ref jumped, sound: true);
            }
            else
            {
                m.JumpsLeft--;
                v.Y = jumpSpeed;
                Jumped(m, ref jumped, sound: true);
            }
        }

        if (v.Y <= 0)
            m.IsJumping = false;
        else if (m.ShortHop && m.IsJumping && control && !keys.IsDown("Jump"))
        {
            v.Y *= 0.5f;
            m.IsJumping = false;
        }

        if (v.Y < 0 && m.FallMultiplier > 1)
            v.Y -= gravity * (m.FallMultiplier - 1) * dt;
        m.IsWallSliding = m.WallSlide && !grounded && v.Y < 0 && body.WallSide != 0 && MathF.Abs(x) > 0.1f && MathF.Sign(x) == body.WallSide;
        if (m.IsWallSliding)
            v.Y = Math.Max(v.Y, -Math.Max(0, m.WallSlideSpeed));
        if (m.MaxFallSpeed > 0)
            v.Y = Math.Max(v.Y, -m.MaxFallSpeed);

        if (m.WallJumpLock <= 0 && MathF.Abs(x) > 0.1f)
            m.Facing = MathF.Sign(x);
        Finish();

        void Finish()
        {
            body.Velocity = v;
            m.FallSpeed = grounded && !jumped ? 0 : Math.Max(0, -v.Y);
            if (m.FlipSprite)
                Game.Face(entity, m.Facing);

            if (entity.GetComponent<Animation>() is not { } animation || CombatSystem.IsPlayingAttack(entity, animation))
                return;
            var onGround = grounded && !jumped;
            if (m.IsDashing)
                PlayFirst(animation, "Dash", "Run");
            else if (m.IsClimbing)
                PlayFirst(animation, "Climb");
            else if (m.IsWallSliding)
                PlayFirst(animation, "WallSlide", "Fall", "Jump");
            else if (onGround)
                PlayFirst(animation, MathF.Abs(x) > 0.1f && MathF.Abs(v.X) > 0.1f ? "Run" : "Idle", "Idle");
            else
                PlayFirst(animation, v.Y > 0 ? "Jump" : "Fall", "Jump");

            // Hanging still on a ladder holds the climbing frame.
            if (m.IsClimbing && animation.HasClip("Climb"))
                animation.Playing = v != Vector2.Zero;
        }
    }

    private void Jumped(PlatformerMovement m, ref bool jumped, bool sound)
    {
        jumped = true;
        m.IsJumping = true;
        if (sound)
            Game.PlayIfSet(m.JumpSound);
        m.RaiseJumped();
    }

    // A puff at its feet.
    private void SpawnDust(PlatformerMovement m, Entity entity)
    {
        if (string.IsNullOrWhiteSpace(m.DustEffect))
            return;
        var bounds = entity.GetComponent<Collider>() is { } collider ? Physics.BoundsOf(collider) : new Box(Game.Flat(entity.Transform.WorldPosition), Vector2.Zero);
        Game.TrySpawn(m.DustEffect, m, "its Dust Effect", new Vector2(bounds.Center.X, bounds.Min.Y));
    }

    private void RunTopDown(TopDownMovement m)
    {
        var entity = m.Entity!;
        var keys = Game.InputOf(entity);
        if (entity.GetComponent<Body>() is not { } body)
            return;

        var dt = Game.Time.Delta;
        var control = m.Controllable;
        var raw = control ? keys.Move : Vector2.Zero;
        var input = raw;
        if (m.FourWay && m.Isometric)
        {
            // Along the diamonds' edges: one way held is turned an eighth to the right, two held go between them.
            var (x, y) = (MathF.Abs(raw.X) > 0.1f ? MathF.Sign(raw.X) : 0, MathF.Abs(raw.Y) > 0.1f ? MathF.Sign(raw.Y) : 0);
            var edge = x != 0 && y != 0 ? new Vector2(x, y) : new Vector2(x + y, y - x);
            input = edge == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(edge) * Math.Min(1, raw.Length());
        }
        else if (m.FourWay)
        {
            // The axis pressed most recently wins; holding just one goes that way.
            if (MathF.Abs(raw.X) > 0.1f && MathF.Abs(m.LastInput.X) <= 0.1f)
                m.PreferVertical = false;
            if (MathF.Abs(raw.Y) > 0.1f && MathF.Abs(m.LastInput.Y) <= 0.1f)
                m.PreferVertical = true;
            var vertical = MathF.Abs(raw.Y) > 0.1f && (m.PreferVertical || MathF.Abs(raw.X) <= 0.1f);
            // As far as the stick is pushed, but straight along the one axis.
            var push = Math.Min(1, raw.Length());
            input = vertical ? new Vector2(0, MathF.Sign(raw.Y) * push) : MathF.Abs(raw.X) > 0.1f ? new Vector2(MathF.Sign(raw.X) * push, 0) : Vector2.Zero;
        }

        m.LastInput = raw;
        if (input.LengthSquared() > 0.01f)
            m.Facing = Vector2.Normalize(input);

        m.DashCooldownLeft = Math.Max(0, m.DashCooldownLeft - dt);
        if (m.Dash && control && m.DashLeft <= 0 && m.DashCooldownLeft <= 0 && keys.WasPressed("Dash"))
        {
            m.DashDirection = m.Facing;
            m.DashLeft = Math.Max(0, m.DashTime);
            m.DashCooldownLeft = m.DashLeft + Math.Max(0, m.DashCooldown);
            Game.PlayIfSet(m.DashSound);
            m.RaiseDashed();
        }

        var v = body.Velocity;
        if (m.DashLeft > 0)
        {
            m.DashLeft -= dt;
            v = OnGround(m, m.DashDirection) * (m.DashLeft > 0 ? m.DashSpeed : m.Speed);
        }
        else
        {
            var target = OnGround(m, input) * m.Speed;
            var rate = target != Vector2.Zero ? m.Acceleration : m.Deceleration;
            v = rate > 0 ? Game.MoveTowards(v, target, rate * dt) : target;
        }

        body.Velocity = v;
        body.FloatThisStep = true;
        if (m.FlipSprite && MathF.Abs(m.Facing.X) > 0.1f)
            Game.Face(entity, MathF.Sign(m.Facing.X));

        if (entity.GetComponent<Animation>() is not { } animation || CombatSystem.IsPlayingAttack(entity, animation))
            return;
        var moving = m.IsDashing || (input != Vector2.Zero && v.LengthSquared() > 0.01f);
        // On an isometric map, going up the screen at all is seen from behind, and down it from the front.
        var facingUpOrDown = m.Isometric ? MathF.Abs(m.Facing.Y) > 0.1f : MathF.Abs(m.Facing.Y) > MathF.Abs(m.Facing.X);
        var way = m.Facing.Y > 0 ? "Up" : "Down";
        if (m.IsDashing)
            PlayFirst(animation, "Dash", facingUpOrDown ? "Run" + way : "Run", "Run");
        else if (moving)
            PlayFirst(animation, facingUpOrDown ? "Run" + way : "Run", "Run");
        else
            PlayFirst(animation, facingUpOrDown ? "Idle" + way : "Idle", "Idle");
    }

    // A way over the ground as it moves on the screen: on an isometric map's ground, seen at a slant, up and down are
    // halved.
    private static Vector2 OnGround(TopDownMovement m, Vector2 way) => m.Isometric ? way with { Y = way.Y / 2 } : way;

    // The first of the clips the animation has, if any.
    internal static void PlayFirst(Animation animation, params string[] clips)
    {
        foreach (var clip in clips)
        {
            if (animation.HasClip(clip))
            {
                animation.Play(clip);
                return;
            }
        }
    }

    /// <summary>
    /// A ladder within <paramref name="reach"/> to the side of <paramref name="point"/> (a <see cref="Ladder"/>'s collider or
    /// a tilemap's ladder tile), with the X of its middle to line a climber up with (null for a ladder that doesn't
    /// center); null if there's none.
    /// </summary>
    private LadderSpot? LadderAt(Vector2 point, float reach)
    {
        var span = new Box(point, new Vector2(reach, 0));
        foreach (var ladder in Game.Showing<Ladder>())
        {
            foreach (var collider in ladder.Entity!.GetComponents<Collider>())
            {
                var box = Physics.BoundsOf(collider);
                if (box.Min.X <= span.Max.X && box.Max.X >= span.Min.X && point.Y >= box.Min.Y && point.Y <= box.Max.Y)
                    return new LadderSpot(ladder.Center ? box.Center.X : null);
            }
        }

        foreach (var tilemap in Game.Showing<Tilemap>())
        {
            if ((tilemap.LadderAt(point) ?? tilemap.LadderAt(point - new Vector2(reach, 0)) ?? tilemap.LadderAt(point + new Vector2(reach, 0))) is { } x)
                return new LadderSpot(x);
        }

        return null;
    }

    // Moves by a world-space offset, whatever its parent.
    internal static void Shift(Entity entity, Vector2 world)
    {
        var delta = new Vector3(world, 0);
        if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            delta = Vector3.TransformNormal(delta, inverse);
        entity.Transform.Position += delta;
    }

    // Touches from this frame's physics: whatever touches a bouncer from its launching side is sent off that way. In a
    // network game, each machine launches what it moves, so a player's own character springs off at once.
    internal void RunBouncers()
    {
        foreach (var bouncer in Game.Showing<Bouncer>())
        {
            // A paused bouncer launches nothing, and nothing paused is launched (see Entity.IsPaused).
            if (bouncer.Entity!.IsPaused)
                continue;
            foreach (var key in bouncer.Recent.Keys.ToList())
            {
                bouncer.Recent[key] += Game.Time.Delta;
                if (bouncer.Recent[key] > 0.15f)
                    bouncer.Recent.Remove(key);
            }

            var entity = bouncer.Entity!;
            var way = bouncer.Way;
            var collider = entity.GetComponent<Collider>();
            var box = collider is null ? new Box(Game.Flat(entity.Transform.WorldPosition), Vector2.Zero) : Physics.BoundsOf(collider);
            foreach (var (by, layer) in Game.TouchersOf(entity).ToList())
            {
                if (!bouncer.AcceptsLayer(layer) || by.GetComponent<Body>() is not { } body || bouncer.Recent.ContainsKey(by) || !Game.Moves(by) || by.IsPaused)
                    continue;
                if (!OnLaunchingSide(by, box, way, collider is { IsTrigger: false }))
                    continue;

                body.Velocity = body.Velocity - Vector2.Dot(body.Velocity, way) * way + way * bouncer.Strength;
                by.GetComponent<PlatformerMovement>()?.Launch();
                bouncer.Recent[by] = 0;
                Game.PlayIfSet(bouncer.Sound);
                if (bouncer.Squash)
                {
                    var transform = entity.Transform;
                    var rest = bouncer.RestingScale ??= transform.Scale;
                    var along = new Vector2(MathF.Abs(way.X), MathF.Abs(way.Y));
                    var squash = Vector2.One + (Vector2.One - along) * 0.3f - along * 0.3f;
                    transform.Scale = new Vector3(rest.X * squash.X, rest.Y * squash.Y, rest.Z);
                    Game.ScaleTo(entity, new Vector2(rest.X, rest.Y), 0.3f, Ease.OutElastic);
                }

                bouncer.RaiseBounced(by);
            }
        }
    }

    // Something passing through a trigger bouncer is launched once its middle is past the bouncer's; on a solid one it has to
    // be against the launching face (standing on a spring, not walking into its side).
    private static bool OnLaunchingSide(Entity by, Box box, Vector2 way, bool solid)
    {
        var them = by.GetComponent<Collider>() is { } collider ? Physics.BoundsOf(collider) : new Box(Game.Flat(by.Transform.WorldPosition), Vector2.Zero);
        if (!solid)
            return Vector2.Dot(them.Center - box.Center, way) > 0;
        var face = Vector2.Dot(box.Center, way) + Extent(box, way);
        var back = Vector2.Dot(them.Center, way) - Extent(them, way);
        return back >= face - 0.05f;

        static float Extent(Box b, Vector2 d) => MathF.Abs(b.HalfSize.X * d.X) + MathF.Abs(b.HalfSize.Y * d.Y);
    }

    // Crumbling platforms shake once stood on, fall away, and come back once nothing's in the way. Gone ones are hidden, so
    // all of them are looked at, not just the active ones.
    internal void RunCrumbles()
    {
        foreach (var crumble in Game.RunHere<Crumble>())
        {
            var entity = crumble.Entity!;
            if (entity.IsPaused)
                continue;
            if (crumble.Crumbled)
            {
                crumble.ShakeOffset = Vector2.Zero;
                if (entity.IsEnabled)
                    entity.IsEnabled = false;
                if (!(crumble.Respawn > 0))
                    continue;
                if (float.IsNaN(crumble.Left))
                    crumble.Left = crumble.Respawn;
                crumble.Left -= Game.Time.Delta;
                if (crumble.Left > 0 || SomethingInside(entity))
                    continue;
                crumble.Crumbled = false;
                crumble.Left = float.NaN;
                entity.IsEnabled = true;
                crumble.RaiseReturned();
                continue;
            }

            if (!entity.IsActive)
            {
                crumble.ShakeOffset = Vector2.Zero;
                continue;
            }

            if (float.IsNaN(crumble.Left))
            {
                if (!StoodOn(crumble))
                    continue;
                crumble.Left = Math.Max(0, crumble.Delay);
                crumble.Age = 0;
            }

            crumble.Left -= Game.Time.Delta;
            crumble.Age += Game.Time.Delta;
            crumble.ShakeOffset = new Vector2(MathF.Sin(crumble.Age * 70) * 0.06f, MathF.Cos(crumble.Age * 55) * 0.02f);
            if (crumble.Left > 0)
                continue;

            crumble.Crumbled = true;
            crumble.Left = float.NaN;
            crumble.ShakeOffset = Vector2.Zero;
            entity.IsEnabled = false;
            Game.PlayIfSet(crumble.Sound);
            if (!string.IsNullOrWhiteSpace(crumble.Effect))
                Game.TrySpawn(crumble.Effect, crumble, "its Effect", Game.Flat(entity.Transform.WorldPosition));
            crumble.RaiseFell();
        }
    }

    // A body it accepts is standing on it (or on something inside it).
    private bool StoodOn(Crumble crumble) =>
        Game.Active<Body>().Any(b => b.Ground is { } ground && ground.IsSelfOrDescendantOf(crumble.Entity!) &&
            crumble.AcceptsLayer(b.Entity!.GetComponent<Collider>()?.Layer));

    // A body overlaps where its solid colliders would be, so it can't come back yet.
    private bool SomethingInside(Entity entity) =>
        entity.SelfAndDescendants().SelectMany(e => e.GetComponents<Collider>()).Where(c => !c.IsTrigger).Any(c =>
        {
            var box = Physics.BoundsOf(c);
            return Game.Physics.OverlapBox(Game.Scene, box.Center, box.HalfSize * 2).Any(e => Game.BodyOwner(e) is { } owner && !owner.IsSelfOrDescendantOf(entity));
        });

    private readonly record struct LadderSpot(float? SnapX);
}
