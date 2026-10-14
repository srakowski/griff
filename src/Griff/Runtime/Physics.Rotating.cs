using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Griff.Scenes;

namespace Griff.Runtime;

// Bodies that turn (Body.Rotates). Each is a rigid box or circle. A step finds the points where it touches things, or will
// touch them before the step is out, and pushes it and them apart at those points so it stops, bounces, grips and turns (a
// crate landing on its corner tips over and falls flat); then it moves and turns, in parts when it's fast, and is pushed
// back out of anything it went into. Whatever else moves meets it as the turned box it is.
public sealed partial class Physics
{
    // Shapes this close are touching: what a body rests on, is grounded on and reports bumping into.
    private const float TouchSkin = 0.01f;

    // Overlaps shallower than this are left for the next step's contacts, rather than pushed apart.
    private const float PushSlop = 0.002f;

    // How many times a step goes round a body's contacts, each time evening out their pushes a little more.
    private const int ContactPasses = 8;

    // How many times a part of a step pushes a body out of what it went into, the deepest first.
    private const int TurnPushPasses = 4;

    // In each part of a step, a turning body (or the far end of it) moves at most this share of its own thickness.
    private const float TurnPartShare = 0.4f;

    private const int MaxTurnParts = 16;

    // Turning slower than this (radians a second) while resting on something stops, so a crate set down stays still.
    private const float SettleSpin = 0.05f;

    // Moving slower than this (world units a second) while resting on something and not turning stops.
    private const float SettleDrift = 0.05f;

    // The fastest anything turns: twenty times round a second.
    private const float MaxSpin = 40 * MathF.PI;

    // Walking into a turned box's slant no steeper than this (rise over run) walks up it.
    private const float TurnedStepSlope = 1.2f;

    private readonly List<Contact> _contacts = [];

    // A turning body's step: what it touches decides how it moves and turns, then it moves and turns.
    private void StepTurning(Body body, Collider collider, Solids solids, float dt, Action<Entity, Entity> onCollision)
    {
        var entity = body.Entity!;
        if (!body.FloatThisStep)
            body.Velocity += Gravity * body.GravityScale * dt;
        body.IsGrounded = false;
        body.Ground = null;
        body.WallSide = 0;

        // Ride along with whatever it stood on, if that has moved since.
        if (_grounds.TryGetValue(body, out var ground) && ground.Ground.Scene == entity.Scene && ground.Ground.IsActive)
            Translate(solids, entity, PositionOf(ground.Ground) - ground.Position);

        var filter = solids.FilterOf(collider);
        var footprint = collider is BoxCollider box ? box.WorldShape.HalfSize.X : ((CircleCollider)collider).WorldCircle.Radius;
        Hull self = default;
        HullOf(collider, ref self);
        var bottomBefore = HullBounds(ref self).Min.Y;
        var reach = self.IsCircle ? self.Radius : (self.Points[0] - self.Center).Length();
        var thickness = collider is BoxCollider shaped
            ? MathF.Max(1e-3f, MathF.Min(shaped.WorldShape.HalfSize.X, shaped.WorldShape.HalfSize.Y))
            : MathF.Max(1e-3f, self.Radius);

        var inverseMass = 1 / Math.Max(0.01f, body.Mass);
        var inverseInertia = 1 / body.Inertia;
        var velocity = body.Velocity;
        var spin = Math.Clamp(body.Spin * MathF.PI / 180, -MaxSpin, MaxSpin);

        // Everything it touches or could reach this step, pushed against at once.
        var margin = (velocity.Length() + MathF.Abs(spin) * reach) * dt + TouchSkin;
        Gather(body, solids, filter, ref self, footprint, margin, bottomBefore);
        SolveContacts(body, self.Center, ref velocity, ref spin, inverseMass, inverseInertia, dt);
        spin = Math.Clamp(spin, -MaxSpin, MaxSpin);

        // Moved and turned in parts small enough not to go through anything, out of whatever it went into after each.
        var travel = (velocity.Length() + MathF.Abs(spin) * reach) * dt;
        var parts = Math.Clamp((int)MathF.Ceiling(travel / (TurnPartShare * thickness)), 1, MaxTurnParts);
        for (var part = 0; part < parts; part++)
        {
            Translate(solids, entity, velocity * (dt / parts));
            Turn(solids, entity, collider, spin * (dt / parts));
            PushOut(body, collider, solids, filter, footprint, inverseMass, bottomBefore);
        }

        // What it's touching now: what it stands on, a wall beside it, and what it bumped into.
        HullOf(collider, ref self);
        Gather(body, solids, filter, ref self, footprint, TouchSkin, bottomBefore);
        var up = 0.5f;
        Entity? last = null;
        foreach (var contact in _contacts)
        {
            if (contact.Normal.Y > up)
                (up, body.Ground) = (contact.Normal.Y, contact.Solid);
            if (MathF.Abs(contact.Normal.X) > 0.9f)
                body.WallSide = -MathF.Sign(contact.Normal.X);
            if (contact.Solid != last)
                onCollision(entity, last = contact.Solid);
        }

        // Resting on something and hardly moving, it stops rather than creeping.
        body.IsGrounded = body.Ground is not null;
        if (body.IsGrounded && MathF.Abs(spin) < SettleSpin)
        {
            spin = 0;
            if (velocity.LengthSquared() < SettleDrift * SettleDrift)
                velocity = Vector2.Zero;
        }
        body.Velocity = velocity;
        body.Spin = spin * 180 / MathF.PI;
        if (body.IsGrounded)
            Rub(body, body.Friction, dt);
    }

    // Pushes a turning body out of the deepest thing it's in, again and again while it's in something: all the way out of
    // what holds still, part of the way out of another turning body (by their weights), which is pushed the rest.
    private void PushOut(Body body, Collider collider, Solids solids, LayerFilter filter, float footprint, float inverseMass, float bottomBefore)
    {
        Hull self = default;
        for (var pass = 0; pass < TurnPushPasses; pass++)
        {
            HullOf(collider, ref self);
            Gather(body, solids, filter, ref self, footprint, -PushSlop, bottomBefore);
            if (_contacts.Count == 0)
                return;

            var deepest = _contacts[0];
            foreach (var contact in _contacts)
            {
                if (contact.Gap < deepest.Gap)
                    deepest = contact;
            }

            var depth = -deepest.Gap;
            if (deepest.Other is { Rotates: true } other && !_groupOf.ContainsKey(other))
            {
                var otherInverse = 1 / Math.Max(0.01f, other.Mass);
                var share = inverseMass / (inverseMass + otherInverse);
                Translate(solids, other.Entity!, -deepest.Normal * depth * (1 - share));
                depth *= share;
            }

            Translate(solids, body.Entity!, deepest.Normal * depth);
        }
    }

    // The points where a turning body touches (or is within `margin` of) everything solid, into _contacts.
    private void Gather(Body body, Solids solids, LayerFilter filter, ref Hull self, float footprint, float margin, float bottomBefore)
    {
        var contacts = _contacts;
        contacts.Clear();
        var entity = body.Entity!;
        var region = Grow(HullBounds(ref self), Math.Max(0, margin) + Margin);
        Hull other = default;

        var near = solids.Near(region);
        foreach (var index in near)
        {
            var solid = solids.All[index];
            if (solid.Entity == entity || solid.Entity.IsSelfOrDescendantOf(entity) || filter.IgnoresEachOther(solid.Filter))
                continue;
            var bounds = solid.Area.Bounds;
            if (!bounds.Overlaps(region))
                continue;
            HullOf(solid, ref other);
            if (!Collide(ref self, ref other, margin, out var manifold))
                continue;

            // One-way: only what comes down onto its top from above it.
            if (solid.OneWay && (manifold.Normal.Y < 0.7f || body.IsDroppingThrough || bottomBefore < bounds.Max.Y - 0.05f))
                continue;
            Add(ref manifold, solid.Entity, MovingBody(solid));
        }

        solids.Return(near);

        foreach (var slope in _slopes)
        {
            if (filter.IgnoresEachOther(slope.Filter) || slope.Entity.IsSelfOrDescendantOf(entity) || !slope.Cell.Overlaps(region))
                continue;
            // Its flat bottom sits on the tile below (or is a ceiling only to the old way of moving): it's met from above
            // and the side.
            SlopeHull(ref other, slope);
            if (Collide(ref self, ref other, margin, out var manifold) && manifold.Normal.Y > -0.5f)
                Add(ref manifold, slope.Entity, null);
        }

        // On an isometric map, the diamond under it meets the walls, as everything's does there.
        if (_isometric.Count > 0)
        {
            Hull foot = default;
            foreach (var walls in _isometric)
            {
                var map = walls.Map.Entity!;
                if (filter.IgnoresEachOther(walls.Filter) || map.IsSelfOrDescendantOf(entity))
                    continue;
                walls.FootprintHull(ref foot, self.Center, footprint);
                var (x0, y0, x1, y1) = walls.CellRange(Grow(HullBounds(ref foot), Math.Max(0, margin) + Margin));
                for (var y = y0; y <= y1; y++)
                {
                    for (var x = x0; x <= x1; x++)
                    {
                        if (!walls.Blocked(x, y))
                            continue;
                        walls.DiamondHull(ref other, x, y);
                        if (Collide(ref foot, ref other, margin, out var manifold))
                            Add(ref manifold, map, null);
                    }
                }
            }
        }

        foreach (var walls in _hex)
        {
            var map = walls.Map.Entity!;
            if (filter.IgnoresEachOther(walls.Filter) || map.IsSelfOrDescendantOf(entity) || !walls.Bounds.Overlaps(region))
                continue;
            var (x0, y0, x1, y1) = walls.CellsNear(region);
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    if (!walls.Blocked(x, y))
                        continue;
                    walls.HexagonHull(ref other, x, y);
                    // A side shared with another solid hexagon is inside the wall, not its surface.
                    if (Collide(ref self, ref other, margin, out var manifold) && !walls.SharedSide(x, y, manifold.Normal))
                        Add(ref manifold, map, null);
                }
            }
        }
    }

    private void Add(ref Manifold manifold, Entity solid, Body? other)
    {
        for (var i = 0; i < manifold.Count; i++)
        {
            _contacts.Add(new Contact
            {
                Point = i == 0 ? manifold.Point0 : manifold.Point1,
                Gap = i == 0 ? manifold.Gap0 : manifold.Gap1,
                Normal = manifold.Normal,
                Solid = solid,
                Other = other,
                Paired = i == 0 && manifold.Count == 2,
            });
        }
    }

    // The body that moves a solid, when it moves under its own steam here and so shares a turning body's pushes.
    private static Body? MovingBody(in Shape solid)
    {
        if (solid.Collider is null || solid.Entity.GetComponent<Body>() is not { Frozen: false } body || !solid.Entity.MovesHere)
            return null;
        return body;
    }

    // Pushes at each contact, a few times round, until together they stop the body going into anything (or going in no
    // further than it can before the step is out), bounce it off what it hits hard, and grip what it slides along. Bodies it
    // touches that move take their share.
    private void SolveContacts(Body body, Vector2 center, ref Vector2 velocity, ref float spin, float inverseMass, float inverseInertia, float dt)
    {
        var contacts = CollectionsMarshal.AsSpan(_contacts);
        if (contacts.Length == 0)
            return;
        var bounce = Math.Clamp(body.Bounciness, 0, 1);
        var grip = Math.Clamp(body.Grip, 0, 1);
        for (var i = 0; i < contacts.Length; i++)
        {
            ref var contact = ref contacts[i];
            if (contact.Other is { } other)
            {
                contact.OtherInverseMass = 1 / Math.Max(0.01f, other.Mass);
                contact.OtherTurns = other.Rotates && !_groupOf.ContainsKey(other);
                if (contact.OtherTurns)
                {
                    contact.OtherCenter = other.CenterOfMass;
                    contact.OtherInverseInertia = 1 / other.Inertia;
                }
            }

            // How hard it is to change how fast the two go apart (and slide) at the point, from their weights and how far
            // from their middles it is.
            var normal = contact.Normal;
            var tangent = new Vector2(-normal.Y, normal.X);
            var arm = contact.Point - center;
            var otherArm = contact.Point - contact.OtherCenter;
            var (turn, otherTurn) = (Cross(arm, normal), Cross(otherArm, normal));
            var (slide, otherSlide) = (Cross(arm, tangent), Cross(otherArm, tangent));
            var masses = inverseMass + contact.OtherInverseMass;
            contact.NormalMass = 1 / (masses + inverseInertia * turn * turn + contact.OtherInverseInertia * otherTurn * otherTurn);
            contact.TangentMass = 1 / (masses + inverseInertia * slide * slide + contact.OtherInverseInertia * otherSlide * otherSlide);
            contact.Grip = contact.OtherTurns ? MathF.Sqrt(grip * Math.Clamp(contact.Other!.Grip, 0, 1)) : grip;

            var approach = Vector2.Dot(Relative(ref contact, velocity, spin), normal);
            var reached = contact.Gap <= 0 || contact.Gap + approach * dt <= 0;
            var bounces = reached && bounce > 0 && approach < -SettleSpeed;
            contact.Target = bounces ? -bounce * approach : Math.Min(0, -contact.Gap / dt);
        }

        for (var pass = 0; pass < ContactPasses; pass++)
        {
            for (var i = 0; i < contacts.Length; i++)
            {
                ref var contact = ref contacts[i];
                Grip(ref contact, ref velocity, ref spin);
                if (!contact.Paired)
                {
                    Press(ref contact, ref velocity, ref spin);
                    continue;
                }

                // Two points where a side lies along what it touches (a crate resting flat): both pushes at once, so they
                // come out even rather than one end favoured and the crate rocking or creeping.
                ref var second = ref contacts[++i];
                Grip(ref second, ref velocity, ref spin);
                if (!PressBoth(ref contact, ref second, ref velocity, ref spin))
                {
                    Press(ref contact, ref velocity, ref spin);
                    Press(ref second, ref velocity, ref spin);
                }
            }
        }

        // Grip: as much against the sliding as it takes to stop it, up to the grip times how hard they press.
        void Grip(ref Contact contact, ref Vector2 moving, ref float turning)
        {
            var tangent = new Vector2(-contact.Normal.Y, contact.Normal.X);
            var rub = -Vector2.Dot(Relative(ref contact, moving, turning), tangent) * contact.TangentMass;
            var limit = contact.Grip * contact.Pushed;
            var rubbed = Math.Clamp(contact.Rubbed + rub, -limit, limit);
            Apply(ref contact, tangent * (rubbed - contact.Rubbed), ref moving, ref turning);
            contact.Rubbed = rubbed;
        }

        // Apart: never pulling them together.
        void Press(ref Contact contact, ref Vector2 moving, ref float turning)
        {
            var push = (contact.Target - Vector2.Dot(Relative(ref contact, moving, turning), contact.Normal)) * contact.NormalMass;
            var pushed = Math.Max(contact.Pushed + push, 0);
            Apply(ref contact, contact.Normal * (pushed - contact.Pushed), ref moving, ref turning);
            contact.Pushed = pushed;
        }

        // Both points' pushes together: the pair of pushes (neither pulling) that leaves each point no faster together than
        // its target, or, where one of them has to let go, the other alone. False when the two are too nearly the same
        // point to tell apart, for one at a time instead.
        bool PressBoth(ref Contact a, ref Contact b, ref Vector2 moving, ref float turning)
        {
            var normal = a.Normal;
            var (armA, armB) = (Cross(a.Point - center, normal), Cross(b.Point - center, normal));
            var (otherA, otherB) = (Cross(a.Point - a.OtherCenter, normal), Cross(b.Point - b.OtherCenter, normal));
            var masses = inverseMass + a.OtherInverseMass;
            var k11 = masses + inverseInertia * armA * armA + a.OtherInverseInertia * otherA * otherA;
            var k22 = masses + inverseInertia * armB * armB + a.OtherInverseInertia * otherB * otherB;
            var k12 = masses + inverseInertia * armA * armB + a.OtherInverseInertia * otherA * otherB;
            var determinant = k11 * k22 - k12 * k12;
            if (!(k11 * k11 < 1000 * determinant))
                return false;

            var (pushedA, pushedB) = (a.Pushed, b.Pushed);
            var errorA = Vector2.Dot(Relative(ref a, moving, turning), normal) - a.Target - (k11 * pushedA + k12 * pushedB);
            var errorB = Vector2.Dot(Relative(ref b, moving, turning), normal) - b.Target - (k12 * pushedA + k22 * pushedB);

            // Both pressing; else only the first; else only the second; else neither.
            var x = (-(k22 * errorA - k12 * errorB) / determinant, -(k11 * errorB - k12 * errorA) / determinant);
            if (!(x.Item1 >= 0 && x.Item2 >= 0))
            {
                x = (-errorA / k11, 0);
                if (!(x.Item1 >= 0 && k12 * x.Item1 + errorB >= 0))
                {
                    x = (0, -errorB / k22);
                    if (!(x.Item2 >= 0 && k12 * x.Item2 + errorA >= 0))
                    {
                        x = (0, 0);
                        if (!(errorA >= 0 && errorB >= 0))
                            return false;
                    }
                }
            }

            Apply(ref a, normal * (x.Item1 - pushedA), ref moving, ref turning);
            Apply(ref b, normal * (x.Item2 - pushedB), ref moving, ref turning);
            (a.Pushed, b.Pushed) = x;
            return true;
        }

        void Apply(ref Contact contact, Vector2 impulse, ref Vector2 moving, ref float turning)
        {
            moving += impulse * inverseMass;
            turning += inverseInertia * Cross(contact.Point - center, impulse);
            if (contact.Other is not { } other)
                return;
            other.Velocity -= impulse * contact.OtherInverseMass;
            if (contact.OtherTurns)
                other.Spin -= contact.OtherInverseInertia * Cross(contact.Point - contact.OtherCenter, impulse) * 180 / MathF.PI;
        }

        Vector2 Relative(ref Contact contact, Vector2 moving, float turning)
        {
            var arm = contact.Point - center;
            moving += new Vector2(-turning * arm.Y, turning * arm.X);
            if (contact.Other is not { } other)
                return moving;
            var otherMoving = other.Velocity;
            if (contact.OtherTurns)
            {
                var otherArm = contact.Point - contact.OtherCenter;
                var otherSpin = other.Spin * MathF.PI / 180;
                otherMoving += new Vector2(-otherSpin * otherArm.Y, otherSpin * otherArm.X);
            }

            return moving - otherMoving;
        }
    }

    // A body moved the old way (one that doesn't turn) ran into one that turns: it stops as it would at a wall, and the one
    // it ran into takes the knock where they touch, shared by their weights, so walking into a crate pushes it along, running
    // into a plank's end tips it, and standing on one end of a seesaw lowers it.
    private static void Knock(Body mover, Area moving, in Shape solid, int axis, float sign)
    {
        if (solid.Collider is null || solid.Entity.GetComponent<Body>() is not { Rotates: true, Frozen: false } struck ||
            struck == mover || !solid.Entity.MovesHere)
            return;
        var into = axis == 0 ? new Vector2(sign, 0) : new Vector2(0, sign);
        var other = solid.Area;

        // Where they touch: on the mover's leading side, in the middle of where the two overlap across it.
        var across = 1 - axis;
        var low = MathF.Max(Component(moving.Bounds.Min, across), Component(other.Bounds.Min, across));
        var high = MathF.Min(Component(moving.Bounds.Max, across), Component(other.Bounds.Max, across));
        var side = Component(moving.Center, axis) + sign * Component(moving.Bounds.HalfSize, axis);
        var point = axis == 0 ? new Vector2(side, (low + high) / 2) : new Vector2((low + high) / 2, side);

        var arm = point - struck.CenterOfMass;
        var spin = struck.Spin * MathF.PI / 180;
        var closing = Vector2.Dot(mover.Velocity - struck.Velocity - new Vector2(-spin * arm.Y, spin * arm.X), into);
        if (closing <= 0)
            return;
        var turn = Cross(arm, into);
        var inertia = struck.Inertia;
        var struckMass = Math.Max(0.01f, struck.Mass);
        var impulse = closing / (1 / Math.Max(0.01f, mover.Mass) + 1 / struckMass + turn * turn / inertia);
        struck.Velocity += into * (impulse / struckMass);
        struck.Spin += turn * impulse / inertia * 180 / MathF.PI;
    }

    // Turns an entity by `radians` (anticlockwise) round its collider's middle, so it spins in place.
    private static void Turn(Solids solids, Entity entity, Collider? collider, float radians)
    {
        if (radians == 0)
            return;
        var before = collider is null ? default : Area.Of(collider).Center;
        var degrees = radians * 180 / MathF.PI;
        if (entity.Parent is { } parent && parent.Transform.WorldMatrix is var world && world.M11 * world.M22 - world.M12 * world.M21 < 0)
            degrees = -degrees;
        var rotation = entity.Transform.Rotation;
        var z = rotation.Z + degrees;
        entity.Transform.Rotation = rotation with { Z = z - 360 * MathF.Floor((z + 180) / 360) };
        if (collider is null)
            solids.Moved(entity);
        else
            Translate(solids, entity, before - Area.Of(collider).Center);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    // A point where a turning body touches something, and what a step works out for it.
    private struct Contact
    {
        public Vector2 Point;

        // Out of what it touches, into the turning body.
        public Vector2 Normal;

        // How far apart they are there: negative where they overlap.
        public float Gap;

        public Entity Solid;

        // What it touches, when that's a body that moves and so takes its share of the pushes.
        public Body? Other;
        public bool OtherTurns;
        public Vector2 OtherCenter;
        public float OtherInverseMass;
        public float OtherInverseInertia;

        // The first of two points of one side lying along what it touches; the next contact is the other.
        public bool Paired;

        public float NormalMass;
        public float TangentMass;
        public float Grip;
        public float Target;

        // The push apart and the grip so far this step.
        public float Pushed;
        public float Rubbed;
    }

    // Up to two points where two shapes touch, sharing one normal.
    private struct Manifold
    {
        // Out of the second shape into the first.
        public Vector2 Normal;
        public int Count;
        public Vector2 Point0;
        public Vector2 Point1;
        public float Gap0;
        public float Gap1;

        public void Add(Vector2 point, float gap)
        {
            if (Count == 0)
                (Point0, Gap0) = (point, gap);
            else
                (Point1, Gap1) = (point, gap);
            Count++;
        }
    }

    [InlineArray(8)]
    private struct HullCorners
    {
        private Vector2 _first;
    }

    // A convex shape in the world: its corners going round it anticlockwise, or a circle (no corners).
    private struct Hull
    {
        public HullCorners Points;
        public int Count;
        public Vector2 Center;
        public float Radius;

        public readonly bool IsCircle => Count == 0;
    }

    private static void HullOf(Collider collider, ref Hull hull)
    {
        if (collider is CircleCollider circle)
        {
            CircleHull(ref hull, circle.WorldCircle);
            return;
        }

        var (center, half, axis) = ((BoxCollider)collider).WorldShape;
        BoxHull(ref hull, center, half, axis);
    }

    private static void HullOf(in Shape solid, ref Hull hull)
    {
        if (solid.Collider is { } collider)
            HullOf(collider, ref hull);
        else
            BoxHull(ref hull, solid.TileBox.Center, solid.TileBox.HalfSize, Vector2.UnitX);
    }

    private static void CircleHull(ref Hull hull, Circle circle)
    {
        hull.Count = 0;
        hull.Center = circle.Center;
        hull.Radius = MathF.Max(0, circle.Radius);
    }

    private static void BoxHull(ref Hull hull, Vector2 center, Vector2 half, Vector2 axis)
    {
        half = Vector2.Abs(half);
        var (x, y) = (axis * half.X, new Vector2(-axis.Y, axis.X) * half.Y);
        hull.Count = 4;
        hull.Center = center;
        hull.Radius = 0;
        hull.Points[0] = center - x - y;
        hull.Points[1] = center + x - y;
        hull.Points[2] = center + x + y;
        hull.Points[3] = center - x + y;
    }

    // A slope tile's solid triangle.
    private static void SlopeHull(ref Hull hull, in Slope slope)
    {
        var (min, max) = (slope.Cell.Min, slope.Cell.Max);
        hull.Count = 3;
        hull.Radius = 0;
        hull.Points[0] = min;
        hull.Points[1] = new Vector2(max.X, min.Y);
        hull.Points[2] = slope.Rising ? max : new Vector2(min.X, max.Y);
        hull.Center = (hull.Points[0] + hull.Points[1] + hull.Points[2]) / 3;
    }

    // Once a hull's corners are in, in order one way or the other round it: its middle, and anticlockwise.
    private static void FinishPolygon(ref Hull hull)
    {
        hull.Radius = 0;
        var (sum, twiceArea) = (Vector2.Zero, 0f);
        for (var i = 0; i < hull.Count; i++)
        {
            sum += hull.Points[i];
            twiceArea += Cross(hull.Points[i], hull.Points[(i + 1) % hull.Count]);
        }

        hull.Center = sum / hull.Count;
        if (twiceArea >= 0)
            return;
        for (var (i, j) = (0, hull.Count - 1); i < j; i++, j--)
            (hull.Points[i], hull.Points[j]) = (hull.Points[j], hull.Points[i]);
    }

    private static Box HullBounds(ref Hull hull)
    {
        if (hull.IsCircle)
            return new Box(hull.Center, new Vector2(hull.Radius));
        var (min, max) = (hull.Points[0], hull.Points[0]);
        for (var i = 1; i < hull.Count; i++)
            (min, max) = (Vector2.Min(min, hull.Points[i]), Vector2.Max(max, hull.Points[i]));
        return new Box((min + max) / 2, (max - min) / 2);
    }

    // The outward normal of the side from corner i of an anticlockwise hull.
    private static Vector2 SideNormal(ref Hull hull, int i)
    {
        var side = hull.Points[(i + 1) % hull.Count] - hull.Points[i];
        var length = side.Length();
        return length > 1e-12f ? new Vector2(side.Y, -side.X) / length : Vector2.UnitY;
    }

    /// <summary>
    /// Where two shapes touch, or come within <paramref name="margin"/> of it (a negative margin: overlap at least that
    /// much), with the normal out of <paramref name="b"/> into <paramref name="a"/>.
    /// </summary>
    private static bool Collide(ref Hull a, ref Hull b, float margin, out Manifold manifold)
    {
        manifold = default;
        if (a.IsCircle && b.IsCircle)
        {
            var between = a.Center - b.Center;
            var distance = between.Length();
            var gap = distance - a.Radius - b.Radius;
            if (gap > margin)
                return false;
            manifold.Normal = distance > 1e-6f ? between / distance : Vector2.UnitY;
            manifold.Add(b.Center + manifold.Normal * b.Radius, gap);
            return true;
        }

        if (a.IsCircle || b.IsCircle)
        {
            ref var polygon = ref a.IsCircle ? ref b : ref a;
            ref var circle = ref a.IsCircle ? ref a : ref b;
            if (!PolygonAndCircle(ref polygon, circle.Center, circle.Radius, margin, out var normal, out var point, out var gap))
                return false;
            manifold.Normal = a.IsCircle ? normal : -normal;
            manifold.Add(point, gap);
            return true;
        }

        Polygons(ref a, ref b, margin, ref manifold);
        return manifold.Count > 0;
    }

    // A polygon and a circle: the normal out of the polygon towards the circle's middle, a point on the polygon where they
    // touch, and how far apart they are.
    private static bool PolygonAndCircle(ref Hull polygon, Vector2 center, float radius, float margin, out Vector2 normal, out Vector2 point, out float gap)
    {
        (normal, point, gap) = (default, default, 0);
        var (best, side) = (float.NegativeInfinity, 0);
        for (var i = 0; i < polygon.Count; i++)
        {
            var separation = Vector2.Dot(SideNormal(ref polygon, i), center - polygon.Points[i]);
            if (separation > radius + margin)
                return false;
            if (separation > best)
                (best, side) = (separation, i);
        }

        var (from, to) = (polygon.Points[side], polygon.Points[(side + 1) % polygon.Count]);
        var face = SideNormal(ref polygon, side);
        if (best > 1e-6f)
        {
            // Off past an end of its nearest side, it meets that corner.
            var corner = Vector2.Dot(center - from, to - from) <= 0 ? from : Vector2.Dot(center - to, from - to) <= 0 ? to : (Vector2?)null;
            if (corner is { } at)
            {
                var away = center - at;
                var distance = away.Length();
                if (distance - radius > margin)
                    return false;
                (normal, point, gap) = (distance > 1e-6f ? away / distance : face, at, distance - radius);
                return true;
            }
        }

        (normal, point, gap) = (face, center - face * best, best - radius);
        return true;
    }

    // Two polygons, by their sides: the side of one they're least far into (the touched shape's, when it's about as good),
    // and the other's side most facing it, cut to its length: where that's within the margin of it, they touch.
    private static void Polygons(ref Hull a, ref Hull b, float margin, ref Manifold manifold)
    {
        var separationA = MostApart(ref a, ref b, out var sideA);
        if (separationA > margin)
            return;
        var separationB = MostApart(ref b, ref a, out var sideB);
        if (separationB > margin)
            return;

        var onB = separationB + 0.1f * TouchSkin >= separationA;
        ref var reference = ref onB ? ref b : ref a;
        ref var incident = ref onB ? ref a : ref b;
        var side = onB ? sideB : sideA;
        var (from, to) = (reference.Points[side], reference.Points[(side + 1) % reference.Count]);
        var normal = SideNormal(ref reference, side);

        var (least, facing) = (float.PositiveInfinity, 0);
        for (var i = 0; i < incident.Count; i++)
        {
            var dot = Vector2.Dot(SideNormal(ref incident, i), normal);
            if (dot < least)
                (least, facing) = (dot, i);
        }

        var (p, q) = (incident.Points[facing], incident.Points[(facing + 1) % incident.Count]);
        var along = to - from;
        var length = along.Length();
        if (length < 1e-9f)
            return;
        along /= length;
        if (!Clip(ref p, ref q, -along, -Vector2.Dot(along, from)) || !Clip(ref p, ref q, along, Vector2.Dot(along, to)))
            return;

        manifold.Normal = onB ? normal : -normal;
        var front = Vector2.Dot(normal, from);
        var gapP = Vector2.Dot(normal, p) - front;
        if (gapP <= margin)
            manifold.Add(p, gapP);
        var gapQ = Vector2.Dot(normal, q) - front;
        if (gapQ <= margin && Vector2.DistanceSquared(p, q) > 1e-12f)
            manifold.Add(q, gapQ);
    }

    // Of a's sides, the one b is furthest out beyond (by b's nearest corner), and how far.
    private static float MostApart(ref Hull a, ref Hull b, out int side)
    {
        var best = float.NegativeInfinity;
        side = 0;
        for (var i = 0; i < a.Count; i++)
        {
            var normal = SideNormal(ref a, i);
            var corner = a.Points[i];
            var least = float.PositiveInfinity;
            for (var j = 0; j < b.Count; j++)
                least = MathF.Min(least, Vector2.Dot(normal, b.Points[j] - corner));
            if (least > best)
                (best, side) = (least, i);
        }

        return best;
    }

    // Cuts the segment p to q down to where normal . point <= offset; false if none of it is.
    private static bool Clip(ref Vector2 p, ref Vector2 q, Vector2 normal, float offset)
    {
        var (dp, dq) = (Vector2.Dot(normal, p) - offset, Vector2.Dot(normal, q) - offset);
        if (dp > 0 && dq > 0)
            return false;
        if (dp > 0)
            p += (q - p) * (dp / (dp - dq));
        else if (dq > 0)
            q = p + (q - p) * (dp / (dp - dq));
        return true;
    }
}
