using System.Numerics;
using System.Runtime.InteropServices;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// One hard knock in the last physics step: <see cref="Struck"/> was hit by <see cref="By"/> at <see cref="Point"/>, as hard as
/// <see cref="Strength"/> says. Strength is the push it took to stop the two coming together where they met (an impulse:
/// mass times speed), so a crate of Mass 1 landing on the floor at 5 units a second hits 5, and a ball of Mass 2 thrown at a
/// crate of Mass 1 at 9 units a second hits about 6, shared by their weights. Both sides of a knock are told, once a step for
/// each pair, the harder of the points where they met; gentle touches (closing at under a unit a second: resting, sliding,
/// settling) aren't knocks.
/// </summary>
public readonly record struct Impact(Entity Struck, Entity By, float Strength, Vector2 Point);

// Knocks: as each step works out what's run into what, it notes how hard, for what breaks on hard knocks (Breakable) and
// for scripts. Bodies that turn note theirs from the points the step worked out together, how fast each pair came together
// there as the step began and how hard that was to stop; bodies moved the old way, from what they ran into and how fast.
public sealed partial class Physics
{
    // Coming together slower than this (world units a second) isn't a knock: resting, sliding, settling.
    private const float ImpactSpeed = SettleSpeed;

    private readonly List<Impact> _impacts = [];

    /// <summary>
    /// The hard knocks of the last step, in the order it found them (the same every time): each pair of things that ran into
    /// each other hard enough, both ways round (see <see cref="Impact"/>).
    /// </summary>
    public IReadOnlyList<Impact> Impacts => _impacts;

    /// <summary>
    /// The same, as a list to go through without an enumerator.
    /// </summary>
    public List<Impact> ImpactList => _impacts;

    // A knock between two things, noted both ways round.
    private void Knocked(Entity struck, Entity by, float strength, Vector2 point)
    {
        if (!(strength > 0) || !float.IsFinite(strength))
            return;
        _impacts.Add(new Impact(struck, by, strength, point));
        _impacts.Add(new Impact(by, struck, strength, point));
    }

    // What the step's points between bodies that turn and what they touch found: for each pair, the hardest of their points
    // that stopped them coming together (each pair's points come one after another), as hard as the speed they came
    // together at times their weights shared.
    private void NoteImpacts()
    {
        var contacts = CollectionsMarshal.AsSpan(_solving);
        var movers = CollectionsMarshal.AsSpan(_movers);
        var (lastOwn, lastSolid, lastAt) = ((Entity?)null, (Entity?)null, -1);
        foreach (ref var contact in contacts)
        {
            if (contact.MostPushed <= 0 || contact.Approach > -ImpactSpeed)
                continue;

            // How fast they came together there, times their weights shared (what stops them dead, head on): a crate
            // landing flat hits its mass times its speed, however its two corners share it.
            var masses = movers[contact.A].InverseMass + (contact.B >= 0 ? movers[contact.B].InverseMass : 0);
            var strength = masses > 0 ? -contact.Approach / masses : 0;
            if (!(strength > 0) || !float.IsFinite(strength))
                continue;
            var own = contact.Own.Entity!;
            if (lastAt >= 0 && own == lastOwn && contact.Solid == lastSolid && lastAt == _impacts.Count - 2)
            {
                if (strength > _impacts[lastAt].Strength)
                {
                    _impacts[lastAt] = new Impact(own, contact.Solid, strength, contact.Point);
                    _impacts[lastAt + 1] = new Impact(contact.Solid, own, strength, contact.Point);
                }

                continue;
            }

            (lastOwn, lastSolid, lastAt) = (own, contact.Solid, _impacts.Count);
            Knocked(own, contact.Solid, strength, contact.Point);
        }
    }

    // A body moved the old way stopped by what it ran into (along one axis): as hard as its speed that way, times its mass,
    // shared with a body it ran into by their weights.
    private void NoteImpact(Body body, Collider collider, Entity struck, int axis, float speed)
    {
        if (!(MathF.Abs(speed) >= ImpactSpeed))
            return;
        var mass = Math.Max(0.01f, body.Mass);
        if (struck.GetComponent<Body>() is { HeldStill: false } other && other != body)
            mass = 1 / (1 / mass + 1 / Math.Max(0.01f, other.Mass));
        var bounds = collider.WorldBounds;
        var point = bounds.Center + (axis == 0 ? new Vector2(MathF.Sign(speed) * bounds.HalfSize.X, 0) : new Vector2(0, MathF.Sign(speed) * bounds.HalfSize.Y));
        Knocked(body.Entity!, struck, mass * MathF.Abs(speed), point);
    }
}
