using System.Numerics;
using System.Runtime.InteropServices;
using Griff.Scenes;

namespace Griff.Runtime;

// Hinges (HingeJoint): two bodies, or a body and the world, pinned together at a point, worked out with the bodies that
// turn in the same parts of the step, before their contacts: the point held together, the angle kept within its limits and
// turned by its motor. Each hinge's pushes are kept with the scene's contact memory, as a first guess for the next step's,
// so a rewind replays them to the bit. The two a hinge joins don't bump into each other unless it says they should.
public sealed partial class Physics
{
    // How stiffly a hinge is pulled back together (hertz), and how much that's damped: stiffer than contacts, as a hinge
    // pulls as well as pushes.
    private const float JointHertz = 60;
    private const float JointDamping = 2;

    // How far from the hinge its friction holds against turning, for how hard it's pulled on (as if it turned on an axle
    // this thick, at Friction 1).
    private const float AxleReach = 0.5f;

    // In the contact memory, a hinge's pushes are kept under its own entity, the part saying it's a hinge (beyond the kinds of
    // tile part), the feature which of its two halves.
    private const int JointPart = 5 << 28;

    // The step's hinges, as found before anything moves: which bodies they join, and what they're hinged to.
    [NotState("gathered again each step")]
    private readonly List<JointLink> _joints = [];

    // Bodies a hinge joins (and so are worked out with those that turn), the pairs that don't bump into each other, and
    // bodies a running motor keeps awake.
    [NotState("gathered again each step")]
    private readonly HashSet<Body> _jointed = [];
    [NotState("gathered again each step")]
    private readonly HashSet<(Entity, Entity)> _unbumped = [];
    [NotState("gathered again each step")]
    private readonly HashSet<Body> _motorized = [];

    // The step's welded groups that move, worked out with the bodies that turn.
    [NotState("gathered again each step")]
    private readonly List<BodyGroup> _turningGroups = [];

    // The hinges worked out this step (with at least one side moving here).
    [NotState("gathered again each step")]
    private readonly List<JointRow> _rows = [];

    private readonly record struct JointLink(HingeJoint Hinge, Body Host, Entity? Other, Body? OtherBody);

    // The step's hinges: joined as they stand the first time they're seen (see HingeJoint.Joined).
    private void CollectJoints(Scene scene)
    {
        _joints.Clear();
        _jointed.Clear();
        _unbumped.Clear();
        _motorized.Clear();
        var hinges = scene.ActiveNow<HingeJoint>();
        if (hinges.Count == 0)
            return;

        foreach (var hinge in hinges)
        {
            var entity = hinge.Entity!;
            if (entity.GetComponent<Body>() is not { } host)
                continue;
            var other = hinge.OtherEntity;
            if (other is not null && (!other.IsActive || other == entity))
                continue;
            if (!hinge.Joined)
                hinge.Join();
            _jointed.Add(host);
            _joints.Add(new JointLink(hinge, host, other, null));
        }

        // What it's hinged to is pulled about by it if that moves under physics with the bodies that turn: one that rotates,
        // a welded group, or one hung on a hinge itself. Anything else just carries the hinge along.
        var joints = CollectionsMarshal.AsSpan(_joints);
        foreach (ref var joint in joints)
        {
            var (hinge, host, other) = (joint.Hinge, joint.Host, joint.Other);
            Body? otherBody = null;
            if (other?.GetComponent<Body>() is { } body && body != host && (body.Rotates || _jointed.Contains(body) || _groupOf.TryGetValue(body, out var group) && !group.Still))
                otherBody = body;
            joint = joint with { OtherBody = otherBody };
            if (otherBody is not null)
                _jointed.Add(otherBody);
            if (!hinge.Collide && other is not null)
            {
                _unbumped.Add((host.Entity!, other));
                _unbumped.Add((other, host.Entity!));
            }

            if (hinge.Motor && hinge.MotorStrength > 0)
            {
                _motorized.Add(host);
                if (otherBody is not null)
                    _motorized.Add(otherBody);
            }
        }
    }

    // Whether a body is worked out with those that turn: it rotates, it's welded into a group that moves, or it's hinged.
    private bool Solves(Body body) =>
        body.Rotates || (_groupOf.Count > 0 && _groupOf.TryGetValue(body, out var group) && !group.Still) || (_jointed.Count > 0 && _jointed.Contains(body));

    // Whether a body turns here: it rotates, and isn't welded to one that doesn't.
    private bool TurnsHere(Body body) => body.Rotates && (_groupOf.Count == 0 || !_groupOf.TryGetValue(body, out var group) || group.Turns);

    // Whether a body of a welded group that holds still.
    private bool HeldInGroup(Body body) => _groupOf.Count > 0 && _groupOf.TryGetValue(body, out var group) && group.Still;

    // Two entities a hinge joins that don't bump into each other.
    private bool Unbumped(Entity a, Entity b) => _unbumped.Count > 0 && _unbumped.Contains((a, b));

    // Wakes a sleeper hinged to something awake (with everything welded to it); true if it woke any.
    private bool WakeJoined(Solids solids)
    {
        var woke = false;
        foreach (var joint in _joints)
        {
            if (joint.OtherBody is not { } other)
                continue;
            var (hostAwake, otherAwake) = (_moverOf.ContainsKey(joint.Host), _moverOf.ContainsKey(other));
            if (hostAwake && !otherAwake && _sleepers.Contains(other))
                (_, woke) = (Wake(other, solids), true);
            else if (otherAwake && !hostAwake && _sleepers.Contains(joint.Host))
                (_, woke) = (Wake(joint.Host, solids), true);
        }

        return woke;
    }

    // Whether a body hangs on a hinge, so is held up by it while it sleeps.
    private bool Hinged(Body body) => _jointed.Count > 0 && _jointed.Contains(body);

    // The step's hinges that have a side moving here: where each is on the two as the step begins, and the pushes it
    // remembers from the step before.
    private void SetUpJoints(ContactMemory memory, Span<Mover> movers, float dt)
    {
        _rows.Clear();
        foreach (var joint in _joints)
        {
            var (hinge, host) = (joint.Hinge, joint.Host);
            var a = _moverOf.TryGetValue(host, out var hostMover) && movers[hostMover].Turns ? hostMover : -1;
            var b = joint.OtherBody is { } otherBody && _moverOf.TryGetValue(otherBody, out var otherMover) && movers[otherMover].Turns ? otherMover : -1;
            if (a < 0 && b < 0 || a == b)
                continue;

            var other = joint.Other;
            var hostAnchor = hinge.WorldAnchor;
            var otherAnchor = hinge.OtherWorldAnchor(other);
            var angle = HingeJoint.Wrap(HingeJoint.TurnOf(host.Entity!) - (other is null ? 0 : HingeJoint.TurnOf(other)) - hinge.StartAngle) * MathF.PI / 180;

            // The side that moves here goes first; the angle and its limits are turned round to suit.
            var flipped = a < 0;
            var row = new JointRow { Hinge = hinge, Flipped = flipped };
            if (!flipped)
            {
                (row.A, row.B, row.Angle) = (a, b, angle);
                row.ArmA = hostAnchor - movers[a].Center;
                if (b >= 0)
                    row.ArmB = otherAnchor - movers[b].Center;
                else
                    (row.Fixed, row.FixedVelocity) = (otherAnchor, CarriedVelocity(other));
            }
            else
            {
                (row.A, row.B, row.Angle) = (b, -1, -angle);
                row.ArmA = otherAnchor - movers[b].Center;
                (row.Fixed, row.FixedVelocity) = (hostAnchor, CarriedVelocity(host.Entity));
            }

            ref var mover = ref movers[row.A];
            row.InverseMassB = row.B >= 0 ? movers[row.B].InverseMass : 0;
            row.InverseInertiaB = row.B >= 0 ? movers[row.B].InverseInertia : 0;
            var axial = mover.InverseInertia + row.InverseInertiaB;
            row.AxialMass = axial > 0 ? 1 / axial : 0;

            var (lower, upper) = (Math.Clamp(hinge.LowerAngle, -180, 180) * MathF.PI / 180, Math.Clamp(hinge.UpperAngle, -180, 180) * MathF.PI / 180);
            if (lower > upper)
                (lower, upper) = (upper, lower);
            (row.Lower, row.Upper) = flipped ? (-upper, -lower) : (lower, upper);
            // A motor turns it at its speed as hard as it can; without one, its friction holds it still as hard as it's pulled on.
            var motor = hinge.Motor && hinge.MotorStrength > 0;
            row.Speed = motor ? (flipped ? -1 : 1) * hinge.MotorSpeed * MathF.PI / 180 : 0;
            row.MostTwist = motor ? hinge.MotorStrength * dt / SolveParts : 0;
            row.Stiff = motor ? 0 : Math.Clamp(hinge.Friction, 0, 1) * AxleReach;

            var id = host.Entity!.Id;
            var otherId = other?.Id ?? Guid.Empty;
            if (memory.TryGet(new ContactKey(id, otherId, JointPart, 0), out var pulled))
                (row.Pulled, row.Turned) = (new Vector2(pulled.X, pulled.Y) * (flipped ? -1 : 1), pulled.Z * (flipped ? -1 : 1));
            if (memory.TryGet(new ContactKey(id, otherId, JointPart, 1), out var held))
                (row.HeldLow, row.HeldHigh) = flipped ? (held.Y, held.X) : (held.X, held.Y);
            if (!hinge.Limit)
                (row.HeldLow, row.HeldHigh) = (0, 0);
            if (row.MostTwist == 0 && row.Stiff == 0)
                row.Turned = 0;

            if (row.B >= 0)
                Join(row.A, row.B);
            _rows.Add(row);
        }
    }

    // How fast something that carries a hinge along (moved before the bodies that turn, or by a script) goes.
    private static Vector2 CarriedVelocity(Entity? entity) =>
        entity?.GetComponent<Body>() is { HeldStill: false } body && entity.MovesHere ? body.Velocity : Vector2.Zero;

    // The pushes of the part before (of the step before, to begin with), as a first guess at this part's.
    private void WarmJoints(Span<Mover> movers)
    {
        foreach (ref var row in CollectionsMarshal.AsSpan(_rows))
        {
            ref var a = ref movers[row.A];
            var (rA, rB) = Arms(movers, ref row);
            var twist = row.Turned + row.HeldLow - row.HeldHigh;
            a.Velocity -= row.Pulled * a.InverseMass;
            a.Spin += a.InverseInertia * (twist - Cross(rA, row.Pulled));
            if (row.B < 0)
                continue;
            ref var b = ref movers[row.B];
            b.Velocity += row.Pulled * b.InverseMass;
            b.Spin += b.InverseInertia * (Cross(rB, row.Pulled) - twist);
        }
    }

    // From each side's middle to the hinge now, as far as they've turned this step.
    private static (Vector2 A, Vector2 B) Arms(Span<Mover> movers, ref JointRow row)
    {
        ref var a = ref movers[row.A];
        var rA = Rotate(row.ArmA, a.TurnCos, a.TurnSin);
        if (row.B < 0)
            return (rA, default);
        ref var b = ref movers[row.B];
        return (rA, Rotate(row.ArmB, b.TurnCos, b.TurnSin));
    }

    // Once round every hinge: its motor, its limits, then the point held together (pulled back together where it's come
    // apart, with `ease`). `elapsed`: how far into the step the movers have got, for where something carrying a hinge is.
    private void SolveJoints(Span<Mover> movers, Softness softness, float inversePart, bool ease, float dt, float elapsed)
    {
        foreach (ref var row in CollectionsMarshal.AsSpan(_rows))
        {
            ref var a = ref movers[row.A];
            var (rA, rB) = Arms(movers, ref row);
            var (otherVelocity, otherSpin, otherTurned) = (row.FixedVelocity, 0f, 0f);
            Vector2 at;
            if (row.B >= 0)
            {
                ref var b = ref movers[row.B];
                (otherVelocity, otherSpin, otherTurned) = (b.Velocity, b.Spin, b.Turned);
                at = b.Center + b.Moved + rB;
            }
            else
                at = row.Fixed - row.FixedVelocity * (dt - elapsed);

            if (row.AxialMass > 0)
            {
                // The motor: turning at its speed against the other side, as hard as its strength lets it; or the hinge's
                // friction, holding it still as hard as the two are pulled together.
                var most = row.MostTwist > 0 ? row.MostTwist : row.Stiff * row.Pulled.Length();
                if (most > 0)
                {
                    var twist = -row.AxialMass * (a.Spin - otherSpin - row.Speed);
                    var turned = Math.Clamp(row.Turned + twist, -most, most);
                    TwistJoint(movers, ref row, turned - row.Turned);
                    row.Turned = turned;
                    otherSpin = row.B >= 0 ? movers[row.B].Spin : 0;
                }

                // Its limits, each held like a contact: never pulling, letting it close up to the limit, easing it back
                // within it.
                if (row.Hinge.Limit)
                {
                    var angle = row.Angle + a.Turned - otherTurned;
                    var twist = Limit(angle - row.Lower, a.Spin - otherSpin, row.HeldLow, row.AxialMass, softness, inversePart, ease);
                    TwistJoint(movers, ref row, twist - row.HeldLow);
                    row.HeldLow = twist;
                    otherSpin = row.B >= 0 ? movers[row.B].Spin : 0;

                    twist = Limit(row.Upper - angle, otherSpin - a.Spin, row.HeldHigh, row.AxialMass, softness, inversePart, ease);
                    TwistJoint(movers, ref row, row.HeldHigh - twist);
                    row.HeldHigh = twist;
                    otherSpin = row.B >= 0 ? movers[row.B].Spin : 0;
                }
            }

            // The point: the two sides there going the same way, pulled back together where they've come apart.
            var apart = at - (a.Center + a.Moved + rA);
            var (bias, massScale, impulseScale) = ease ? (apart * softness.BiasRate, softness.MassScale, softness.ImpulseScale) : (Vector2.Zero, 1f, 0f);
            var closing = otherVelocity + new Vector2(-otherSpin * rB.Y, otherSpin * rB.X) - a.Velocity - new Vector2(-a.Spin * rA.Y, a.Spin * rA.X);
            var (mA, iA, mB, iB) = (a.InverseMass, a.InverseInertia, row.InverseMassB, row.InverseInertiaB);
            var k11 = mA + mB + iA * rA.Y * rA.Y + iB * rB.Y * rB.Y;
            var k12 = -iA * rA.X * rA.Y - iB * rB.X * rB.Y;
            var k22 = mA + mB + iA * rA.X * rA.X + iB * rB.X * rB.X;
            var determinant = k11 * k22 - k12 * k12;
            if (!(MathF.Abs(determinant) > 1e-12f))
                continue;
            var error = closing + bias;
            var solved = new Vector2(k22 * error.X - k12 * error.Y, k11 * error.Y - k12 * error.X) / determinant;
            var pull = -massScale * solved - impulseScale * row.Pulled;
            row.Pulled += pull;
            a.Velocity -= pull * mA;
            a.Spin -= iA * Cross(rA, pull);
            if (row.B < 0)
                continue;
            ref var other = ref movers[row.B];
            other.Velocity += pull * mB;
            other.Spin += iB * Cross(rB, pull);
        }
    }

    // A limit's hold: how hard it twists back, from how far inside the limit it is (`room`) and how fast that's closing.
    private static float Limit(float room, float closing, float held, float axialMass, Softness softness, float inversePart, bool ease)
    {
        var (bias, massScale, impulseScale) = room > 0 ? (room * inversePart, 1f, 0f)
            : ease ? (softness.BiasRate * room, softness.MassScale, softness.ImpulseScale) : (0f, 1f, 0f);
        var twist = -axialMass * massScale * (closing + bias) - impulseScale * held;
        return Math.Max(held + twist, 0);
    }

    // A twist on a hinge: one way on its first side, the other way on the second.
    private static void TwistJoint(Span<Mover> movers, ref JointRow row, float twist)
    {
        ref var a = ref movers[row.A];
        a.Spin += a.InverseInertia * twist;
        if (row.B < 0)
            return;
        ref var b = ref movers[row.B];
        b.Spin -= b.InverseInertia * twist;
    }

    // Each hinge's pushes, kept with the scene for the next step's first guess.
    private void RememberJoints(ContactMemory memory)
    {
        foreach (var row in _rows)
        {
            var sign = row.Flipped ? -1 : 1;
            var (id, otherId) = (row.Hinge.Entity!.Id, row.Hinge.Other?.Id ?? Guid.Empty);
            memory.Set(new ContactKey(id, otherId, JointPart, 0), new Vector3(row.Pulled * sign, row.Turned * sign));
            memory.Set(new ContactKey(id, otherId, JointPart, 1), row.Flipped ? new Vector3(row.HeldHigh, row.HeldLow, 0) : new Vector3(row.HeldLow, row.HeldHigh, 0));
        }
    }

    // A hinge as a step works it out.
    private struct JointRow
    {
        public HingeJoint Hinge;

        // Its first side worked out here is what's hinged to: the angle, its limits and the motor are turned round.
        public bool Flipped;

        // The two sides in _movers; B is -1 for something that holds still or carries the hinge along (at Fixed, at the
        // end of the step, going at FixedVelocity).
        public int A;
        public int B;
        public Vector2 ArmA;
        public Vector2 ArmB;
        public Vector2 Fixed;
        public Vector2 FixedVelocity;
        public float InverseMassB;
        public float InverseInertiaB;
        public float AxialMass;

        // Radians: how far it had turned on the hinge as the step began, its limits, and its motor's speed (a second).
        public float Angle;
        public float Lower;
        public float Upper;
        public float Speed;

        // The most the motor twists in a part of the step; without a motor, how far out its friction holds (the most it
        // twists, for how hard it's pulled on).
        public float MostTwist;
        public float Stiff;

        // This part's pushes so far: pulling the point together, the motor's twist, and each limit's.
        public Vector2 Pulled;
        public float Turned;
        public float HeldLow;
        public float HeldHigh;
    }
}
