using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Griff.Scenes;

namespace Griff.Runtime;

// Bodies that turn (Body.Rotates). Each is a rigid box or circle. Once everything else has moved, a step finds the points
// where they touch things (or will touch them before the step is out) and works them all out together, in four parts of
// the step: each part pushes them apart at every point, so they stop, bounce, grip and turn (a crate landing on its corner
// tips over and falls flat), starting from the pushes of the part before, and the first from the step before's (kept with
// the scene), so a tower's weight is carried all the way down it and it stands. Overlaps are eased apart rather than
// kicked. Bodies that have all stayed still together for a moment fall asleep and aren't worked out until something
// disturbs them. Whatever else moves meets them as the turned boxes they are. A circle is a ball: it rolls, held back from
// rolling at each point it touches by its rolling resistance (and, top-down, by the ground under the view), grips as hard
// as it hits so its spin goes into its bounces, and passes over the joins between things laid end to end.
public sealed partial class Physics
{
    // Shapes this close are touching: what a body rests on, is grounded on and reports bumping into.
    private const float TouchSkin = 0.01f;

    // Overlaps shallower than this are left for the next step's contacts, rather than pushed apart.
    private const float PushSlop = 0.002f;

    // A step is worked out in this many parts, each going round every contact this many times, then once more to take out
    // what easing overlaps apart added.
    private const int SolveParts = 4;
    private const int PushPasses = 2;

    // How stiffly an overlap is eased apart (hertz) and how much that's damped (more than enough not to bounce), and the
    // fastest it's pushed apart: overlapping things ease apart rather than fly.
    private const float ContactHertz = 60;
    private const float ContactDamping = 10;
    private const float PushApartSpeed = 3;

    // How many times a part of a fast body's move pushes it out of what it went into, the deepest first.
    private const int TurnPushPasses = 4;

    // In each part of a fast body's move, it (or the far end of it) moves at most this share of its own thickness.
    private const float TurnPartShare = 0.4f;

    private const int MaxTurnParts = 16;

    // Moving slower than this (world units a second, the far end of it when it turns) counts as still.
    private const float SleepSpeed = 0.05f;

    // Seconds a body and everything turning that it touches stay still before they fall asleep.
    private const float SleepAfter = 0.5f;

    // Something this far into a sleeping body wakes it.
    private const float WakeDepth = 0.03f;

    // The fastest anything turns: twenty times round a second.
    private const float MaxSpin = 40 * MathF.PI;

    // Walking into a turned box's slant no steeper than this (rise over run) walks up it.
    private const float TurnedStepSlope = 1.2f;

    // What one gathering of contacts found.
    private readonly List<Contact> _contacts = [];

    // A step's bodies that turn, in order, and those of them that sleep on; the bodies it works out (awake turning ones, and
    // bodies moved the old way they touch) and the contacts between them and everything else.
    private readonly List<Body> _turning = [];
    private readonly HashSet<Body> _sleepers = [];
    private readonly HashSet<Guid> _sleeping = [];
    private readonly List<Mover> _movers = [];
    private readonly Dictionary<Body, int> _moverOf = [];
    private readonly List<Contact> _solving = [];
    private readonly List<float> _islandStill = [];

    private float _widestMargin;

    // The step of every body that turns, together: asleep ones stay so unless disturbed; the rest (and the sleepers they
    // touch) are worked out together, move and turn, and fall asleep together once they've all been still a while.
    private void StepTurning(Scene scene, Solids solids, float dt, Action<Entity, Entity> onCollision)
    {
        var movers = _movers;
        movers.Clear();
        _moverOf.Clear();
        _sleepers.Clear();
        _solving.Clear();
        _widestMargin = 0;

        foreach (var body in _turning)
        {
            if (body.Asleep && (scene.Vacated.Count > 0 || scene.VacatedEverywhere) && NearVacated(scene, body))
                body.Asleep = false;
            if (body.Asleep && KeepsSleeping(body, solids, onCollision))
                _sleepers.Add(body);
            else
                AddTurning(body, solids, dt, reported: false);
        }

        // What each touches, or could reach this step; each pair once. A sleeper it touches wakes and joins in.
        for (var i = 0; i < movers.Count; i++)
        {
            if (movers[i].Turns)
                GatherFor(i, solids);
        }

        Solve(scene.Contacts, dt);
        Remember(scene.Contacts);

        for (var i = 0; i < movers.Count; i++)
        {
            if (movers[i].Turns)
                Move(i, solids);
        }

        foreach (ref var mover in CollectionsMarshal.AsSpan(movers))
        {
            var body = mover.Body;
            body.Velocity = mover.Velocity;
            if (!mover.Turns)
                continue;
            body.Spin = mover.Spin * 180 / MathF.PI;
            (body.Ground, body.WallSide, mover.GroundUp, mover.Touched) = (null, 0, 0.5f, null);
        }

        Touches(onCollision);
        foreach (ref var mover in CollectionsMarshal.AsSpan(movers))
        {
            if (mover.Turns)
                Settle(ref mover, dt);
        }

        FallAsleep();
    }

    // Whether a solid has gone (taken out, turned off, tiles cleared) from beside a sleeping body since the last step: what
    // held it up may have gone with it.
    private static bool NearVacated(Scene scene, Body body)
    {
        if (scene.VacatedEverywhere)
            return true;
        var near = Grow(body.Entity!.GetComponent<Collider>()!.WorldBounds, TouchSkin + Margin);
        foreach (var box in scene.Vacated)
        {
            if (box.Overlaps(near))
                return true;
        }

        return false;
    }

    // A sleeping body stays asleep while nothing has moved it, pushed into it or taken away what's under it (anything else
    // that held it, taken out or turned off, has woken it already); it still rests on what it rests on and says what it
    // touches. False (and nothing done) if it should wake.
    private bool KeepsSleeping(Body body, Solids solids, Action<Entity, Entity> onCollision)
    {
        var entity = body.Entity!;
        if (body.Velocity != Vector2.Zero || body.Spin != 0)
            return false;
        if (CarryOf(body) != Vector2.Zero)
            return false;

        var collider = entity.GetComponent<Collider>()!;
        Hull self = default;
        var half = Measure(collider, ref self);
        Gather(body, solids, solids.FilterOf(collider), ref self, self.IsCircle ? self.Radius : half.X, TouchSkin, HullBounds(ref self).Min.Y, turning: false);

        // Still something under it, if it falls, and nothing pushed into it. (Something moved the old way that runs into it
        // knocks it, which wakes it.) What's under it may be one thing below it, or things either side of it it's wedged
        // between: a ball in a heap against a wall rests on a ball down to one side and the wall.
        var pull = body.FloatThisStep ? Vector2.Zero : Gravity * body.GravityScale;
        var up = pull == Vector2.Zero ? Vector2.Zero : -Vector2.Normalize(pull);
        var held = up == Vector2.Zero;
        var (left, right) = (false, false);
        foreach (var contact in _contacts)
        {
            if (contact.Gap < -WakeDepth)
                return false;
            var lifts = Vector2.Dot(contact.Normal, up);
            held |= lifts > 0.5f;
            if (lifts > -0.01f)
            {
                var side = Cross(up, contact.Normal);
                (left, right) = (left || side > 0, right || side < 0);
            }
        }

        if (!held && !(left && right))
            return false;

        Touching(body, onCollision, report: true);
        return true;
    }

    // From the contacts just gathered: what it stands on, a wall beside it, and (when told to) what it bumped into.
    private void Touching(Body body, Action<Entity, Entity> onCollision, bool report)
    {
        var entity = body.Entity!;
        var up = 0.5f;
        Entity? last = null;
        body.Ground = null;
        body.WallSide = 0;
        foreach (var contact in _contacts)
        {
            if (contact.Normal.Y > up)
                (up, body.Ground) = (contact.Normal.Y, contact.Solid);
            if (MathF.Abs(contact.Normal.X) > 0.9f)
                body.WallSide = -MathF.Sign(contact.Normal.X);
            if (report && contact.Solid != last)
                onCollision(entity, last = contact.Solid);
        }

        body.IsGrounded = body.Ground is not null;
    }

    // How far what a turning body stood on last step has moved since, to carry it along (a platform moved by a script).
    // Another body that turns doesn't carry it: their contacts move them together.
    private Vector2 CarryOf(Body body)
    {
        if (!_grounds.TryGetValue(body, out var ground) || ground.Ground.Scene != body.Entity!.Scene || !ground.Ground.IsActive ||
            ground.Ground.GetComponent<Body>() is { Rotates: true, HeldStill: false } under && !_groupOf.ContainsKey(under) && ground.Ground.MovesHere)
            return Vector2.Zero;
        return PositionOf(ground.Ground) - ground.Position;
    }

    // Wakes a body that turns (if asleep) and adds it to the step's: it rides along with what it stood on, and is ready to
    // be pushed. `reported`: it has said what it touches this step already.
    private int AddTurning(Body body, Solids solids, float dt, bool reported)
    {
        var entity = body.Entity!;
        if (body.Asleep)
            (body.Asleep, body.StillFor) = (false, 0);
        if (!reported)
        {
            body.IsGrounded = false;
            body.Ground = null;
            body.WallSide = 0;
        }

        // Ride along with whatever it stood on, if that has moved since.
        var carry = CarryOf(body);
        var rode = carry != Vector2.Zero;
        if (rode)
            Translate(solids, entity, carry);

        var collider = entity.GetComponent<Collider>()!;
        Hull self = default;
        var half = Measure(collider, ref self);
        var reach = self.IsCircle ? self.Radius : (self.Points[0] - self.Center).Length();
        var thickness = MathF.Max(1e-3f, self.IsCircle ? self.Radius : MathF.Min(half.X, half.Y));
        var pull = body.FloatThisStep ? Vector2.Zero : Gravity * body.GravityScale;
        var velocity = body.Velocity;
        var spin = Math.Clamp(body.Spin * MathF.PI / 180, -MaxSpin, MaxSpin);
        // How far it could go this step (a ball turning doesn't bring its edge any nearer anything).
        var margin = ((velocity + pull * dt).Length() + (self.IsCircle ? 0 : MathF.Abs(spin) * reach)) * dt + TouchSkin;
        _widestMargin = MathF.Max(_widestMargin, margin);

        // A ball with nothing pulling it (top-down) rolls on the ground under the view, slowed as on level ground.
        var floor = self.IsCircle && pull == Vector2.Zero
            ? Math.Max(0, body.RollingResistance) * (Gravity != Vector2.Zero ? Gravity.Length() : DefaultGravity.Length())
            : 0;

        var index = _movers.Count;
        _movers.Add(new Mover
        {
            Body = body,
            Collider = collider,
            Turns = true,
            Round = self.IsCircle,
            Pull = pull,
            Floor = floor,
            Center = self.Center,
            InverseMass = 1 / Math.Max(0.01f, body.Mass),
            InverseInertia = 1 / (self.IsCircle ? body.CircleInertia(self.Radius) : body.BoxInertia(half)),
            Velocity = velocity,
            Spin = spin,
            TurnCos = 1,
            Reach = reach,
            Thickness = thickness,
            Footprint = self.IsCircle ? self.Radius : half.X,
            BottomBefore = HullBounds(ref self).Min.Y,
            Margin = margin,
            Filter = solids.FilterOf(collider),
            Island = index,
            Rode = rode,
            Reported = reported,
        });
        _moverOf[body] = index;
        return index;
    }

    // A body moved the old way that a turning one touches: it takes its share of the pushes, but doesn't turn or move here.
    private int AddPushed(Body body)
    {
        if (_moverOf.TryGetValue(body, out var index))
            return index;
        index = _movers.Count;
        _movers.Add(new Mover
        {
            Body = body,
            InverseMass = 1 / Math.Max(0.01f, body.Mass),
            Velocity = body.Velocity,
            TurnCos = 1,
            Island = index,
        });
        _moverOf[body] = index;
        return index;
    }

    // A collider's hull in the world, and (for a box) half its own width and height.
    private static Vector2 Measure(Collider collider, ref Hull hull)
    {
        if (collider is CircleCollider circle)
        {
            CircleHull(ref hull, circle.WorldCircle);
            return default;
        }

        var (center, half, axis) = ((BoxCollider)collider).WorldShape;
        BoxHull(ref hull, center, half, axis);
        return half;
    }

    // The contacts of turning body i, into the step's: each pair of turning bodies once, and a sleeper it touches woken.
    private void GatherFor(int i, Solids solids)
    {
        var movers = _movers;
        var mover = movers[i];
        Hull self = default;
        HullOf(mover.Collider!, ref self);
        Gather(mover.Body, solids, mover.Filter, ref self, mover.Footprint, mover.Margin, mover.BottomBefore, turning: true);
        CollectionsMarshal.AsSpan(movers)[i].Gathered = true;

        for (var c = 0; c < _contacts.Count; c++)
        {
            var contact = _contacts[c];
            var other = -1;
            if (contact.Other is { } body)
            {
                if (_moverOf.TryGetValue(body, out other))
                {
                    if (movers[other].Gathered)
                        continue;
                }
                else
                    other = _sleepers.Remove(body) ? AddTurning(body, solids, 0, reported: true) : AddPushed(body);
            }

            contact.A = i;
            contact.B = other;
            _solving.Add(contact);
            if (other >= 0 && movers[other].Turns)
                Join(i, other);
        }
    }

    // Puts two turning bodies in one island: they fall asleep together.
    private void Join(int a, int b)
    {
        (a, b) = (IslandOf(a), IslandOf(b));
        if (a != b)
            CollectionsMarshal.AsSpan(_movers)[Math.Max(a, b)].Island = Math.Min(a, b);
    }

    private int IslandOf(int i)
    {
        var movers = CollectionsMarshal.AsSpan(_movers);
        while (movers[i].Island != i)
        {
            movers[i].Island = movers[movers[i].Island].Island;
            i = movers[i].Island;
        }

        return i;
    }

    // Works out every contact of the step together, in parts: in each, gravity, then pushes at each contact (starting from
    // the part before's, and the first part from the step before's, which the scene remembers) that stop what's coming
    // together and ease what overlaps apart, then they move, then the pushes again without the easing apart, so it doesn't
    // fling them. Then the bounces.
    private void Solve(ContactMemory memory, float dt)
    {
        var movers = CollectionsMarshal.AsSpan(_movers);
        var contacts = CollectionsMarshal.AsSpan(_solving);
        var part = dt / SolveParts;
        var inversePart = 1 / part;

        // A soft spring for overlaps: how fast it eases them apart, and how much of each push (and of the push so far) counts.
        var omega = 2 * MathF.PI * ContactHertz;
        var a1 = 2 * ContactDamping + part * omega;
        var a2 = part * omega * a1;
        var a3 = 1 / (1 + a2);
        var softness = new Softness(omega / a1, a2 * a3, a3);

        foreach (ref var contact in contacts)
        {
            ref var a = ref movers[contact.A];
            var normal = contact.Normal;
            var tangent = new Vector2(-normal.Y, normal.X);
            // A ball touches where its edge meets the normal, however far off the point found is yet.
            contact.ArmA = contact.Underfoot ? Vector2.Zero : a.Round ? -normal * a.Reach : contact.Point - a.Center;
            var (bounce, grip) = (a.Body.Bounciness, Math.Clamp(a.Body.Grip, 0, 1));
            var rolling = a.Round && !contact.Underfoot ? Math.Max(0, a.Body.RollingResistance) * a.Reach : 0;
            var (otherMass, otherInertia) = (0f, 0f);
            if (contact.B >= 0)
            {
                ref var b = ref movers[contact.B];
                otherMass = b.InverseMass;
                if (b.Turns)
                {
                    contact.ArmB = b.Round ? normal * b.Reach : contact.Point - b.Center;
                    otherInertia = b.InverseInertia;
                    grip = MathF.Sqrt(grip * Math.Clamp(b.Body.Grip, 0, 1));
                    bounce = MathF.Max(bounce, b.Body.Bounciness);
                    if (b.Round)
                        rolling = MathF.Max(rolling, Math.Max(0, b.Body.RollingResistance) * b.Reach);
                }
            }

            // How hard it is to change how fast the two go apart (and slide) at the point, from their weights and how far
            // from their middles it is.
            var (turn, otherTurn) = (Cross(contact.ArmA, normal), Cross(contact.ArmB, normal));
            var (slide, otherSlide) = (Cross(contact.ArmA, tangent), Cross(contact.ArmB, tangent));
            var masses = a.InverseMass + otherMass;
            contact.NormalMass = 1 / (masses + a.InverseInertia * turn * turn + otherInertia * otherTurn * otherTurn);
            contact.TangentMass = 1 / (masses + a.InverseInertia * slide * slide + otherInertia * otherSlide * otherSlide);
            contact.Grip = grip;
            contact.Bounce = Math.Clamp(bounce, 0, 1);
            contact.Rolling = rolling;
            contact.RollMass = rolling > 0 ? 1 / (a.InverseInertia + otherInertia) : 0;
            contact.Approach = Vector2.Dot(Relative(movers, ref contact), normal);
            if (memory.TryGet(new ContactKey(a.Body.Entity!.Id, contact.Solid.Id, contact.Part, contact.Feature), out var pushed))
                (contact.Pushed, contact.Rubbed, contact.Rolled) = (pushed.X, pushed.Y, rolling > 0 ? pushed.Z : 0);
        }

        for (var step = 0; step < SolveParts; step++)
        {
            foreach (ref var mover in movers)
            {
                if (!mover.Turns)
                    continue;
                mover.Velocity += mover.Pull * part;
                if (mover.Floor > 0)
                    RollOnFloor(ref mover, part);
            }

            // The pushes of the part before (of the step before, to begin with), as a first guess at this part's.
            foreach (ref var contact in contacts)
            {
                Apply(movers, ref contact, contact.Normal * contact.Pushed + new Vector2(-contact.Normal.Y, contact.Normal.X) * contact.Rubbed);
                if (contact.Rolled != 0)
                    Twist(movers, ref contact, contact.Rolled);
            }

            for (var pass = 0; pass < PushPasses; pass++)
                PushAll(movers, contacts, softness, inversePart, ease: true);

            foreach (ref var mover in movers)
            {
                if (!mover.Turns)
                    continue;
                mover.Spin = Math.Clamp(mover.Spin, -MaxSpin, MaxSpin);
                mover.Moved += mover.Velocity * part;
                mover.Turned += mover.Spin * part;
                (mover.TurnSin, mover.TurnCos) = MathF.SinCos(mover.Turned);
            }

            PushAll(movers, contacts, softness, inversePart, ease: false);
        }

        // Bounces: what hit hard enough goes back apart at its share of how fast it came together. A ball that hit grips as
        // hard as it hit, so its spin goes into the bounce (topspin kicks it on, backspin back) and what it slid along
        // sets it spinning.
        foreach (ref var contact in contacts)
        {
            if (contact.Approach > -SettleSpeed || contact.MostPushed == 0)
                continue;
            var bounced = 0f;
            if (contact.Bounce > 0)
            {
                var approach = Vector2.Dot(Relative(movers, ref contact), contact.Normal);
                var push = -contact.NormalMass * (approach + contact.Bounce * contact.Approach);
                var pushed = Math.Max(contact.Pushed + push, 0);
                Apply(movers, ref contact, contact.Normal * (pushed - contact.Pushed));
                bounced = pushed - contact.Pushed;
                contact.Pushed = pushed;
            }

            if (contact.Grip > 0 && (movers[contact.A].Round || contact.B >= 0 && movers[contact.B].Round))
            {
                var tangent = new Vector2(-contact.Normal.Y, contact.Normal.X);
                var limit = contact.Grip * (contact.NormalMass * -contact.Approach + Math.Max(0, bounced));
                var rub = Math.Clamp(-Vector2.Dot(Relative(movers, ref contact), tangent) * contact.TangentMass, -limit, limit);
                Apply(movers, ref contact, tangent * rub);
            }
        }
    }

    // How much of what holds a ball back from rolling slows how fast it goes: its turning takes the rest.
    private const float RollingShare = 2 / 3f;

    // A ball rolling on the ground under a top-down view: slowed as rolling on level ground slows it, and its turning as
    // spinning on the spot would.
    private static void RollOnFloor(ref Mover mover, float part)
    {
        var speed = mover.Velocity.Length();
        if (speed > 0)
            mover.Velocity *= Math.Max(0, speed - mover.Floor * RollingShare * part) / speed;
        if (mover.Spin != 0)
            mover.Spin = MathF.Sign(mover.Spin) * Math.Max(0, MathF.Abs(mover.Spin) - 2 * mover.Floor / Math.Max(1e-3f, mover.Reach) * part);
    }

    // How hard each contact pushed, kept with the scene for the next step's first guess.
    private void Remember(ContactMemory memory)
    {
        // What sleeps on keeps what it remembers, for when it wakes. (With everything asleep, there's nothing new.)
        if (_movers.Count == 0)
            return;
        _sleeping.Clear();
        foreach (var body in _sleepers)
            _sleeping.Add(body.Entity!.Id);
        memory.KeepOnly(_sleeping);
        foreach (var contact in _solving)
            memory.Set(new ContactKey(_movers[contact.A].Body.Entity!.Id, contact.Solid.Id, contact.Part, contact.Feature), new Vector3(contact.Pushed, contact.Rubbed, contact.Rolled));
    }

    private readonly record struct Softness(float BiasRate, float MassScale, float ImpulseScale);

    // Once round every contact. The two points of a side lying along what it touches are pushed together, so they come out
    // even rather than one end favoured: a crate landing flat lands straight, and one resting flat neither rocks nor creeps.
    private static void PushAll(Span<Mover> movers, Span<Contact> contacts, Softness softness, float inversePart, bool ease)
    {
        for (var i = 0; i < contacts.Length; i++)
        {
            ref var contact = ref contacts[i];
            if (!contact.Paired)
            {
                Push(movers, ref contact, softness, inversePart, ease);
                continue;
            }

            ref var second = ref contacts[++i];
            Grip(movers, ref contact);
            Grip(movers, ref second);
            if (!PressBoth(movers, ref contact, ref second, softness, inversePart, ease))
            {
                Press(movers, ref contact, softness, inversePart, ease);
                Press(movers, ref second, softness, inversePart, ease);
            }
        }
    }

    // How fast a contact may come together this part (negative: how fast it's eased apart), and how soft the push is: a gap
    // may close only as far as it goes, and an overlap, with `ease`, is eased apart.
    private static (float Bias, float MassScale, float ImpulseScale) Aim(Span<Mover> movers, ref Contact contact, Softness softness,
        float inversePart, bool ease)
    {
        var gap = GapNow(movers, ref contact);
        if (gap > 0)
            return (gap * inversePart, 1, 0);
        return ease ? (MathF.Max(softness.BiasRate * gap, -PushApartSpeed), softness.MassScale, softness.ImpulseScale) : (0, 1, 0);
    }

    // Both points of a side at once, as one point is pushed but solving for the two together. False (nothing done) when one
    // of them would have to pull, the two are pushed differently (one closing a gap, one easing apart), or they're too
    // nearly the same point to tell apart: then one at a time.
    private static bool PressBoth(Span<Mover> movers, ref Contact a, ref Contact b, Softness softness, float inversePart, bool ease)
    {
        var (biasA, massScale, impulseScale) = Aim(movers, ref a, softness, inversePart, ease);
        var (biasB, massScaleB, _) = Aim(movers, ref b, softness, inversePart, ease);
        if (massScale != massScaleB)
            return false;

        ref var first = ref movers[a.A];
        var normal = a.Normal;
        var (armA, armB) = (Cross(a.ArmA, normal), Cross(b.ArmA, normal));
        var (otherA, otherB) = (Cross(a.ArmB, normal), Cross(b.ArmB, normal));
        var (otherMass, otherInertia) = (0f, 0f);
        if (a.B >= 0)
        {
            ref var other = ref movers[a.B];
            (otherMass, otherInertia) = (other.InverseMass, other.InverseInertia);
        }

        var masses = first.InverseMass + otherMass;
        var k11 = masses + first.InverseInertia * armA * armA + otherInertia * otherA * otherA;
        var k22 = masses + first.InverseInertia * armB * armB + otherInertia * otherB * otherB;
        var k12 = masses + first.InverseInertia * armA * armB + otherInertia * otherA * otherB;
        var determinant = k11 * k22 - k12 * k12;
        if (!(k11 * k11 < 1000 * determinant))
            return false;

        var errorA = Vector2.Dot(Relative(movers, ref a), normal) + biasA;
        var errorB = Vector2.Dot(Relative(movers, ref b), normal) + biasB;
        var pushedA = a.Pushed - massScale * (k22 * errorA - k12 * errorB) / determinant - impulseScale * a.Pushed;
        var pushedB = b.Pushed - massScale * (k11 * errorB - k12 * errorA) / determinant - impulseScale * b.Pushed;
        if (!(pushedA >= 0 && pushedB >= 0))
            return false;

        Apply(movers, ref a, normal * (pushedA - a.Pushed));
        Apply(movers, ref b, normal * (pushedB - b.Pushed));
        a.MostPushed = MathF.Max(a.MostPushed, pushedA - a.Pushed);
        b.MostPushed = MathF.Max(b.MostPushed, pushedB - b.Pushed);
        (a.Pushed, b.Pushed) = (pushedA, pushedB);
        return true;
    }

    // One contact's pushes: grip against sliding (up to the grip times how hard they press), then apart, never pulling them
    // together. A gap may close only as far as it goes this part; an overlap, with `ease`, is eased apart.
    private static void Push(Span<Mover> movers, ref Contact contact, Softness softness, float inversePart, bool ease)
    {
        Roll(movers, ref contact);
        Grip(movers, ref contact);
        Press(movers, ref contact, softness, inversePart, ease);
    }

    // Holding a ball back from rolling: as much against its turning (on what it touches) as stops it, up to its rolling
    // resistance times its radius times how hard they press. Its grip turns that into slowing it down.
    private static void Roll(Span<Mover> movers, ref Contact contact)
    {
        if (contact.Rolling == 0)
            return;
        var turning = movers[contact.A].Spin;
        if (contact.B >= 0)
            turning -= movers[contact.B].Spin;
        var limit = contact.Rolling * contact.Pushed;
        var rolled = Math.Clamp(contact.Rolled - turning * contact.RollMass, -limit, limit);
        Twist(movers, ref contact, rolled - contact.Rolled);
        contact.Rolled = rolled;
    }

    // A twist at a contact: one way on the turning body, the other way on what it touches.
    private static void Twist(Span<Mover> movers, ref Contact contact, float twist)
    {
        ref var a = ref movers[contact.A];
        a.Spin += a.InverseInertia * twist;
        if (contact.B >= 0)
        {
            ref var b = ref movers[contact.B];
            b.Spin -= b.InverseInertia * twist;
        }
    }

    // Grip: as much against the sliding as it takes to stop it, up to the grip times how hard they press.
    private static void Grip(Span<Mover> movers, ref Contact contact)
    {
        var tangent = new Vector2(-contact.Normal.Y, contact.Normal.X);
        var rub = -Vector2.Dot(Relative(movers, ref contact), tangent) * contact.TangentMass;
        var limit = contact.Grip * contact.Pushed;
        var rubbed = Math.Clamp(contact.Rubbed + rub, -limit, limit);
        Apply(movers, ref contact, tangent * (rubbed - contact.Rubbed));
        contact.Rubbed = rubbed;
    }

    // Apart, never pulling them together.
    private static void Press(Span<Mover> movers, ref Contact contact, Softness softness, float inversePart, bool ease)
    {
        var normal = contact.Normal;
        var (bias, massScale, impulseScale) = Aim(movers, ref contact, softness, inversePart, ease);

        var approach = Vector2.Dot(Relative(movers, ref contact), normal);
        var push = -contact.NormalMass * massScale * (approach + bias) - impulseScale * contact.Pushed;
        var pushed = Math.Max(contact.Pushed + push, 0);
        Apply(movers, ref contact, normal * (pushed - contact.Pushed));
        contact.MostPushed = MathF.Max(contact.MostPushed, pushed - contact.Pushed);
        contact.Pushed = pushed;
    }

    // How far apart the two are at the contact now, from how far each has moved and turned since the step began.
    private static float GapNow(Span<Mover> movers, ref Contact contact)
    {
        ref var a = ref movers[contact.A];
        var moved = a.Round ? a.Moved : a.Moved + Rotate(contact.ArmA, a.TurnCos, a.TurnSin) - contact.ArmA;
        if (contact.B >= 0)
        {
            ref var b = ref movers[contact.B];
            if (b.Turns)
                moved -= b.Round ? b.Moved : b.Moved + Rotate(contact.ArmB, b.TurnCos, b.TurnSin) - contact.ArmB;
        }

        return contact.Gap + Vector2.Dot(moved, contact.Normal);
    }

    private static Vector2 Rotate(Vector2 v, float cos, float sin) => new(cos * v.X - sin * v.Y, sin * v.X + cos * v.Y);

    // How fast the turning body goes away from the other at the contact.
    private static Vector2 Relative(Span<Mover> movers, ref Contact contact)
    {
        ref var a = ref movers[contact.A];
        var moving = a.Velocity + new Vector2(-a.Spin * contact.ArmA.Y, a.Spin * contact.ArmA.X);
        if (contact.B < 0)
            return moving;
        ref var b = ref movers[contact.B];
        return moving - b.Velocity - new Vector2(-b.Spin * contact.ArmB.Y, b.Spin * contact.ArmB.X);
    }

    private static void Apply(Span<Mover> movers, ref Contact contact, Vector2 impulse)
    {
        ref var a = ref movers[contact.A];
        a.Velocity += impulse * a.InverseMass;
        a.Spin += a.InverseInertia * Cross(contact.ArmA, impulse);
        if (contact.B < 0)
            return;
        ref var b = ref movers[contact.B];
        b.Velocity -= impulse * b.InverseMass;
        b.Spin -= b.InverseInertia * Cross(contact.ArmB, impulse);
    }

    // Moves and turns a body as the step worked out, round its collider's middle; a fast one in parts small enough not to go
    // through anything, pushed out of whatever it went into after each.
    private void Move(int i, Solids solids)
    {
        var mover = _movers[i];
        var entity = mover.Body.Entity!;
        var travel = mover.Moved.Length() + (mover.Round ? 0 : MathF.Abs(mover.Turned) * mover.Reach);
        var parts = Math.Clamp((int)MathF.Ceiling(travel / (TurnPartShare * mover.Thickness)), 1, MaxTurnParts);
        if (parts == 1)
        {
            Turn(solids, entity, mover.Collider, mover.Turned);
            if (mover.Moved != Vector2.Zero)
                Translate(solids, entity, mover.Moved);
            return;
        }

        for (var part = 0; part < parts; part++)
        {
            Translate(solids, entity, mover.Moved / parts);
            Turn(solids, entity, mover.Collider, mover.Turned / parts);
            PushOut(mover.Body, mover.Collider!, solids, mover.Filter, mover.Footprint, mover.InverseMass, mover.BottomBefore);
        }
    }

    // What each turning body touches after its move, from the step's contacts: what it stands on, a wall beside it, and what
    // it bumped into.
    private void Touches(Action<Entity, Entity> onCollision)
    {
        var movers = CollectionsMarshal.AsSpan(_movers);
        foreach (ref var contact in CollectionsMarshal.AsSpan(_solving))
        {
            if (GapNow(movers, ref contact) > TouchSkin)
                continue;
            ref var a = ref movers[contact.A];
            Touch(ref a, contact.Normal, contact.Solid, onCollision);
            if (contact.B < 0)
                continue;
            ref var b = ref movers[contact.B];
            if (b.Turns)
                Touch(ref b, -contact.Normal, a.Body.Entity!, onCollision);
        }
    }

    private static void Touch(ref Mover mover, Vector2 normal, Entity solid, Action<Entity, Entity> onCollision)
    {
        var body = mover.Body;
        if (normal.Y > mover.GroundUp)
            (mover.GroundUp, mover.GroundNormal, body.Ground) = (normal.Y, normal, solid);
        if (MathF.Abs(normal.X) > 0.9f)
            body.WallSide = -MathF.Sign(normal.X);
        if (!mover.Reported && solid != mover.Touched)
            onCollision(body.Entity!, mover.Touched = solid);
    }

    // After its move: its sliding along the ground slowed, and how long it has been still (hardly moving, and not carried
    // along).
    private static void Settle(ref Mover mover, float dt)
    {
        var body = mover.Body;
        body.IsGrounded = body.Ground is not null;
        if (body.IsGrounded && mover.Round)
            RubRolling(ref mover, body.Friction, dt);
        else if (body.IsGrounded)
            Rub(body, body.Friction, dt);
        var still = !mover.Rode && body.Velocity.LengthSquared() < SleepSpeed * SleepSpeed &&
                    MathF.Abs(mover.Spin) * mover.Reach < SleepSpeed;
        body.StillFor = still ? body.StillFor + dt : 0;
    }

    // A ball's friction slows only its sliding: where it touches the ground, how fast that goes along it, by `friction` units
    // a second every second, its turning taking its share. Rolling, it isn't sliding, so it rolls on.
    private static void RubRolling(ref Mover mover, float friction, float dt)
    {
        if (!(friction > 0))
            return;
        var body = mover.Body;
        var (normal, spin) = (mover.GroundNormal, body.Spin * MathF.PI / 180);
        var tangent = new Vector2(-normal.Y, normal.X);
        var arm = -normal * mover.Reach;
        var sliding = Vector2.Dot(body.Velocity + new Vector2(-spin * arm.Y, spin * arm.X), tangent);
        var slowed = MathF.Sign(sliding) * Math.Max(0, MathF.Abs(sliding) - friction * dt);
        var turn = Cross(arm, tangent);
        var push = (slowed - sliding) / (mover.InverseMass + mover.InverseInertia * turn * turn);
        body.Velocity += tangent * (push * mover.InverseMass);
        body.Spin += mover.InverseInertia * turn * push * 180 / MathF.PI;
    }

    // Each island of turning bodies that have all been still long enough falls asleep.
    private void FallAsleep()
    {
        var still = _islandStill;
        still.Clear();
        for (var i = 0; i < _movers.Count; i++)
            still.Add(float.PositiveInfinity);
        for (var i = 0; i < _movers.Count; i++)
        {
            if (_movers[i].Turns)
            {
                var island = IslandOf(i);
                still[island] = MathF.Min(still[island], _movers[i].Body.StillFor);
            }
        }

        for (var i = 0; i < _movers.Count; i++)
        {
            if (!_movers[i].Turns || still[IslandOf(i)] < SleepAfter)
                continue;
            var body = _movers[i].Body;
            (body.Velocity, body.Spin, body.Asleep) = (Vector2.Zero, 0, true);
        }
    }

    // Pushes a turning body out of the deepest thing it's in, again and again while it's in something: all the way out of
    // what holds still, part of the way out of another turning body (by their weights), which is pushed the rest (and woken).
    private void PushOut(Body body, Collider collider, Solids solids, LayerFilter filter, float footprint, float inverseMass, float bottomBefore)
    {
        Hull self = default;
        for (var pass = 0; pass < TurnPushPasses; pass++)
        {
            HullOf(collider, ref self);
            Gather(body, solids, filter, ref self, footprint, -PushSlop, bottomBefore, turning: false);
            if (_contacts.Count == 0)
                return;

            var deepest = _contacts[0];
            foreach (var contact in _contacts)
            {
                if (contact.Gap < deepest.Gap)
                    deepest = contact;
            }

            var depth = -deepest.Gap;
            if (deepest.Other is { Rotates: true, HeldStill: false } other && !_groupOf.ContainsKey(other))
            {
                var otherInverse = 1 / Math.Max(0.01f, other.Mass);
                var share = inverseMass / (inverseMass + otherInverse);
                Translate(solids, other.Entity!, -deepest.Normal * depth * (1 - share));
                if (other.Asleep)
                    (other.Asleep, other.StillFor) = (false, 0);
                depth *= share;
            }

            Translate(solids, body.Entity!, deepest.Normal * depth);
        }
    }

    // The points where a turning body touches (or is within `margin` of) everything solid, into _contacts. While `turning`
    // (gathering the step's contacts), another turning body's own reach this step counts too, and those already gathered
    // (whose contacts with this one are in) are left out.
    private void Gather(Body body, Solids solids, LayerFilter filter, ref Hull self, float footprint, float margin, float bottomBefore, bool turning)
    {
        var contacts = _contacts;
        contacts.Clear();
        var entity = body.Entity!;
        var region = Grow(HullBounds(ref self), Math.Max(0, margin) + (turning ? _widestMargin : 0) + Margin);
        Hull other = default;

        var near = solids.Near(region);
        foreach (var index in near)
        {
            var solid = solids.All[index];
            if (solid.Entity == entity || solid.Entity.IsSelfOrDescendantOf(entity) || filter.IgnoresEachOther(solid.Filter))
                continue;
            var area = solids.AreaOf(index);
            var bounds = area.Bounds;
            if (!bounds.Overlaps(region))
                continue;
            var moving = MovingBody(solid);
            var reach = margin;
            if (turning && moving is not null && _moverOf.TryGetValue(moving, out var mover))
            {
                if (_movers[mover].Gathered)
                    continue;
                reach += _movers[mover].Margin;
            }

            HullOf(area, ref other);
            if (!Collide(ref self, ref other, reach, out var manifold))
                continue;

            // One-way: only what comes down onto its top from above it.
            if (solid.OneWay && (manifold.Normal.Y < 0.7f || body.IsDroppingThrough || bottomBefore < bounds.Max.Y - 0.05f))
                continue;
            Add(ref manifold, solid.Entity, moving, solids.PartOf(index));
        }

        solids.Return(near);

        for (var s = 0; s < _slopes.Count; s++)
        {
            var slope = _slopes[s];
            if (filter.IgnoresEachOther(slope.Filter) || slope.Entity.IsSelfOrDescendantOf(entity) || !slope.Cell.Overlaps(region))
                continue;
            // Its flat bottom sits on the tile below (or is a ceiling only to the old way of moving): it's met from above
            // and the side.
            SlopeHull(ref other, slope);
            if (Collide(ref self, ref other, margin, out var manifold) && manifold.Normal.Y > -0.5f)
                Add(ref manifold, slope.Entity, null, 1 << 28 | s);
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
                            Add(ref manifold, map, null, 2 << 28 | (y & 0x3FFF) << 14 | (x & 0x3FFF), underfoot: true);
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
                        Add(ref manifold, map, null, 3 << 28 | (y & 0x3FFF) << 14 | (x & 0x3FFF));
                }
            }
        }

        if (self.IsCircle && contacts.Count > 1)
            LeaveOutFlushCorners();
    }

    // A ball meeting a corner (rather than a side), in a contact's Feature.
    private const int CornerFeature = 1 << 10;

    // Two circles meeting, in a contact's Feature.
    private const int RoundFeature = 1 << 11;

    // A corner this close to a side's line lies along it.
    private const float FlushSkin = 1e-3f;

    // Leaves out the corners a ball touches that lie flush along a side it touches of something else (where two crates,
    // platforms or tile runs meet end to end at the same height): rolling over the join, it would otherwise catch on the
    // next one's corner and hop.
    private void LeaveOutFlushCorners()
    {
        var contacts = CollectionsMarshal.AsSpan(_contacts);
        var count = contacts.Length;
        for (var i = count - 1; i >= 0; i--)
        {
            if (contacts[i].Feature != CornerFeature)
                continue;
            for (var j = 0; j < count; j++)
            {
                ref var side = ref contacts[j];
                if (j == i || side.Feature is CornerFeature or RoundFeature || side.Underfoot || Vector2.Dot(side.Normal, contacts[i].Normal) <= 0 ||
                    MathF.Abs(Vector2.Dot(side.Normal, contacts[i].Point - side.Point)) > FlushSkin)
                    continue;
                _contacts.RemoveAt(i);
                contacts = CollectionsMarshal.AsSpan(_contacts);
                count--;
                break;
            }
        }
    }

    private void Add(ref Manifold manifold, Entity solid, Body? other, int part, bool underfoot = false)
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
                Part = part,
                Feature = i == 0 ? manifold.Feature0 : manifold.Feature1,
                Paired = i == 0 && manifold.Count == 2,
                Underfoot = underfoot,
            });
        }
    }

    // The body that moves a solid, when it moves under its own steam here and so shares a turning body's pushes.
    private static Body? MovingBody(in Shape solid)
    {
        if (solid.Collider is null || solid.Entity.GetComponent<Body>() is not { HeldStill: false } body || !solid.Entity.MovesHere)
            return null;
        return body;
    }


    // A body moved the old way (one that doesn't turn) ran into one that turns: it stops as it would at a wall, and the one
    // it ran into takes the knock where they touch, shared by their weights, so walking into a crate pushes it along, running
    // into a plank's end tips it, and standing on one end of a seesaw lowers it.
    private static void Knock(Body mover, Area moving, in Shape solid, int axis, float sign)
    {
        if (solid.Collider is null || solid.Entity.GetComponent<Body>() is not { Rotates: true, HeldStill: false } struck ||
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

        // Which of the solid's shapes (a tile run, a cell) and which corners and sides make the point: the same point next step.
        public int Part;
        public int Feature;

        // The first of two points of one side lying along what it touches; the next contact is the other.
        public bool Paired;

        // Where the diamond under it on an isometric map meets a wall: that doesn't turn, so it pushes through its middle.
        public bool Underfoot;

        // The step's turning body (in _movers), and what it touches there (-1 for something that holds still).
        public int A;
        public int B;

        // From each one's middle to the point, as the step began.
        public Vector2 ArmA;
        public Vector2 ArmB;

        public float NormalMass;
        public float TangentMass;
        public float Grip;
        public float Bounce;

        // For a ball: its rolling resistance times its radius (the most it's held back from rolling, for how hard it's
        // pressed), and how hard it is to change how fast the two turn on each other.
        public float Rolling;
        public float RollMass;

        // How fast they came together as the step began.
        public float Approach;

        // The push apart and the grip so far in this part of the step, and the most a part pushed.
        public float Pushed;
        public float Rubbed;
        public float Rolled;
        public float MostPushed;
    }

    // A body a step works out: one that turns, or one moved the old way that a turning one touches (which takes its share of
    // the pushes, but doesn't turn or move here).
    private struct Mover
    {
        public Body Body;
        public Collider? Collider;
        public bool Turns;

        // A circle: turning it doesn't move where it touches anything.
        public bool Round;

        // Gravity on it this step.
        public Vector2 Pull;

        // A ball with nothing pulling it rolls on the ground under the view: its rolling resistance times gravity.
        public float Floor;

        public Vector2 Center;
        public float InverseMass;
        public float InverseInertia;
        public Vector2 Velocity;

        // Radians a second.
        public float Spin;

        // How far it has moved and turned so far this step.
        public Vector2 Moved;
        public float Turned;
        public float TurnCos;
        public float TurnSin;

        // How far its furthest point is from its middle, how thick it is, how wide its foot is (on an isometric map), where
        // its bottom was as the step began, and how far it could go this step.
        public float Reach;
        public float Thickness;
        public float Footprint;
        public float BottomBefore;
        public float Margin;
        public LayerFilter Filter;

        // Another body of its island (turning bodies touching each other), leading to the island's first.
        public int Island;

        // Its contacts are in; it rode along with what it stood on; it has said what it touches this step (a sleeper woken
        // by something touching it).
        public bool Gathered;
        public bool Rode;
        public bool Reported;

        // After its move: how much what it stands on faces up (and which way), and the last thing it said it touched.
        public float GroundUp;
        public Vector2 GroundNormal;
        public Entity? Touched;
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

        // Which corners and sides make each point, so a point is known again next step.
        public int Feature0;
        public int Feature1;

        public void Add(Vector2 point, float gap, int feature = 0)
        {
            if (Count == 0)
                (Point0, Gap0, Feature0) = (point, gap, feature);
            else
                (Point1, Gap1, Feature1) = (point, gap, feature);
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

    private static void HullOf(in Area area, ref Hull hull)
    {
        if (area.IsCircle)
            CircleHull(ref hull, area.Circle);
        else if (area.IsTurned)
            BoxHull(ref hull, area.Center, area.Half, area.Axis);
        else
            BoxHull(ref hull, area.Center, area.Bounds.HalfSize, Vector2.UnitX);
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
            manifold.Add(b.Center + manifold.Normal * b.Radius, gap, RoundFeature);
            return true;
        }

        if (a.IsCircle || b.IsCircle)
        {
            ref var polygon = ref a.IsCircle ? ref b : ref a;
            ref var circle = ref a.IsCircle ? ref a : ref b;
            if (!PolygonAndCircle(ref polygon, circle.Center, circle.Radius, margin, out var normal, out var point, out var gap, out var corner))
                return false;
            manifold.Normal = a.IsCircle ? normal : -normal;
            manifold.Add(point, gap, corner ? CornerFeature : 0);
            return true;
        }

        Polygons(ref a, ref b, margin, ref manifold);
        return manifold.Count > 0;
    }

    // A polygon and a circle: the normal out of the polygon towards the circle's middle, a point on the polygon where they
    // touch, how far apart they are, and whether that's one of its corners (rather than along a side).
    private static bool PolygonAndCircle(ref Hull polygon, Vector2 center, float radius, float margin, out Vector2 normal, out Vector2 point, out float gap,
        out bool corner)
    {
        (normal, point, gap, corner) = (default, default, 0, false);
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
            var end = Vector2.Dot(center - from, to - from) <= 0 ? from : Vector2.Dot(center - to, from - to) <= 0 ? to : (Vector2?)null;
            if (end is { } at)
            {
                var away = center - at;
                var distance = away.Length();
                if (distance - radius > margin)
                    return false;
                (normal, point, gap, corner) = (distance > 1e-6f ? away / distance : face, at, distance - radius, true);
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
        var feature = (onB ? 1 : 0) | (side << 1) | (facing << 5);
        var gapP = Vector2.Dot(normal, p) - front;
        if (gapP <= margin)
            manifold.Add(p, gapP, feature);
        var gapQ = Vector2.Dot(normal, q) - front;
        if (gapQ <= margin && Vector2.DistanceSquared(p, q) > 1e-12f)
            manifold.Add(q, gapQ, feature | 1 << 9);
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
