using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Drag and drop with the mouse or a grabber's button, with no code: draggable things picked up and carried, drop targets
// that take them, and the slides into place. In a network game the host carries things for every player, by their own
// mouse or grabber.
public sealed partial class Game
{
    // What a player's mouse or a grabber holds: the thing, where it's held from (its middle less the mouse or hand), and
    // where it came from. A grabber's grip closes to nothing over the thing's slide time, so it slides into its hand.
    internal sealed record Holding(Draggable Thing, Vector2 Grip, Vector2 From, Entity? FromTarget)
    {
        public float Elapsed { get; set; }
    }

    // A thing sliding into place after it was let go.
    internal sealed record Slide(Vector2 From, Vector2 To, float Seconds)
    {
        public float Elapsed { get; set; }
    }

    private readonly DragSystem _drags = new();

    /// <summary>What this machine's mouse is holding (a <see cref="Draggable"/>'s entity), or null.</summary>
    public Entity? Held => _drags.Held.GetValueOrDefault(Net.LocalPlayer?.Id ?? 0)?.Thing.Entity;

    // After scripts and before physics, so a held thing with a body is carried this frame and nothing falls from the hand.
    private void RunDragging()
    {
        var draggables = Active<Draggable>();
        var targets = Active<DropTarget>();
        var grabbers = Active<Grabber>();

        // A grabber that goes away puts down what it held where it is.
        if (_drags.Held.Count > 0)
        {
            foreach (var (holder, held) in _drags.Held.ToList())
            {
                if (held.Thing.Entity?.Scene != Scene || !held.Thing.Entity.IsActive ||
                    (holder is Grabber grabber && !grabbers.Contains(grabber)))
                    LetGo(holder, held);
            }
        }

        if (draggables.Count == 0 && targets.Count == 0 && grabbers.Count == 0 && _drags.Held.Count == 0)
            return;

        if (!Net.IsClient && !PausedThisFrame)
        {
            if (TopPanel is null)
                Drag(Net.LocalPlayer?.Id ?? 0, _input, draggables, targets);
            if (Net.IsHost)
            {
                foreach (var player in Net.Players.Where(p => !p.IsLocal && p.Input is not null).ToList())
                    Drag(player.Id, player.Input!, draggables, targets);
            }

            foreach (var grabber in grabbers)
                Grab(grabber, draggables, targets);
        }

        ShowReach(draggables, grabbers);
        RunSlides();
        foreach (var held in _drags.Held.Values)
        {
            if (held.Thing.Entity!.GetComponent<Body>() is { } body)
                (body.Velocity, body.Spin, body.FloatThisStep) = (Vector2.Zero, 0, true);
        }

        UpdateDropTargets(draggables, targets);
    }

    private void Drag(int player, IInput input, IReadOnlyList<Draggable> draggables, IReadOnlyList<DropTarget> targets)
    {
        var mouse = input.MouseWorld;
        if (_drags.Held.TryGetValue(player, out var held))
        {
            if (!input.IsDown(MouseButton.Left))
                Drop(player, held, targets);
            else if (input.IsMouseInView)
                MoveTo(held.Thing.Entity!, mouse + held.Grip - CenterOf(held.Thing.Entity!) + Flat(held.Thing.Entity!.Transform.WorldPosition));
            return;
        }

        if (!input.WasPressed(MouseButton.Left) || !input.IsMouseInView)
            return;

        // The one drawn on top, if several are under the mouse.
        var free = draggables.Where(d => IsFree(d) && Covers(d.Entity!, mouse)).ToHashSet();
        if (free.Count == 0)
            return;
        PickUp(player, TopOf(free), mouse);
    }

    // Picks up or puts down on a press of its action (or, carrying only while that's held, when it's let go).
    private void Grab(Grabber grabber, IReadOnlyList<Draggable> draggables, IReadOnlyList<DropTarget> targets)
    {
        var input = InputOf(grabber.Entity);
        var action = grabber.Action?.Trim();
        var pressed = grabber.Pressed || (!string.IsNullOrEmpty(action) && input.WasPressed(action));
        grabber.Pressed = false;
        var hand = HandOf(grabber);
        if (_drags.Held.TryGetValue(grabber, out var held))
        {
            // Carried to the hand first, so it's put down where the hand is now.
            grabber.InReach = null;
            held.Elapsed += Time.Delta;
            Carry(grabber, held);
            if (pressed || (grabber.HoldToCarry && !string.IsNullOrEmpty(action) && !input.IsDown(action)))
                Put(grabber, held, targets, hand);
            return;
        }

        var reachable = ReachableBy(grabber, hand, draggables);
        grabber.InReach = reachable?.Entity;
        if (!pressed)
            return;
        if (reachable is null)
            PlayIfSet(grabber.RefuseSound);
        else
            PickUp(grabber, reachable, hand);
    }

    // What's under a grabber's hand (the one drawn on top), else the nearest thing whose middle is within its reach.
    private Draggable? ReachableBy(Grabber grabber, Vector2 hand, IReadOnlyList<Draggable> draggables)
    {
        var self = grabber.Entity!;
        var free = draggables.Where(d => IsFree(d) && !d.Entity!.IsSelfOrDescendantOf(self) && !self.IsSelfOrDescendantOf(d.Entity!)).ToList();
        var under = free.Where(d => Covers(d.Entity!, hand)).ToHashSet();
        if (under.Count > 0)
            return TopOf(under);
        return grabber.Reach <= 0
            ? null
            : free.Select(d => (Thing: d, Distance: Vector2.Distance(CenterOf(d.Entity!), hand)))
                .Where(p => p.Distance <= grabber.Reach)
                .OrderBy(p => p.Distance)
                .Select(p => p.Thing)
                .FirstOrDefault();
    }

    private static bool IsFree(Draggable thing) => !thing.IsHeld && !(thing.StaysOnTarget && thing.Target is not null);

    // The one drawn on top.
    private Draggable TopOf(HashSet<Draggable> things) =>
        SceneRenderer.DrawOrder(Scene).Reverse().Select(e => e.GetComponent<Draggable>()).FirstOrDefault(d => d is not null && things.Contains(d))
        ?? things.First();

    // Where a grabber holds things: its middle, its hold offset's X in front of it (the way it faces) and its Y up.
    private static Vector2 HandOf(Grabber grabber)
    {
        var entity = grabber.Entity!;
        var offset = grabber.HoldOffset;
        var ahead = offset.X == 0 ? Vector2.Zero : FacingOf(entity) * MathF.Abs(offset.X);
        return CenterOf(entity) + ahead + new Vector2(0, offset.Y);
    }

    // Moves what a grabber holds to its hand, sliding in from where it was picked up.
    private static void Carry(Grabber grabber, Holding held)
    {
        var entity = held.Thing.Entity!;
        var seconds = held.Thing.SlideTime;
        var t = seconds > 0 ? Math.Clamp(held.Elapsed / seconds, 0, 1) : 1;
        var grip = held.Grip * (1 - t) * (1 - t);
        MoveTo(entity, HandOf(grabber) + grip - CenterOf(entity) + Flat(entity.Transform.WorldPosition));
    }

    // After physics too, so what's carried keeps up with a grabber that moved this frame.
    private void CarryHeld()
    {
        foreach (var (holder, held) in _drags.Held)
        {
            if (holder is Grabber grabber && grabber.Entity?.Scene == Scene && held.Thing.Entity?.Scene == Scene)
                Carry(grabber, held);
        }
    }

    private void PickUp(object holder, Draggable thing, Vector2 point)
    {
        var entity = thing.Entity!;
        var at = CenterOf(entity);
        // A thing still sliding home is picked up from where it was headed.
        var from = _drags.Slides.Remove(thing, out var slide) ? slide.To : at;
        _drags.Held[holder] = new Holding(thing, at - point, from, thing.Target);
        thing.IsHeld = true;
        PlayIfSet(thing.PickUpSound);
        if (!string.IsNullOrWhiteSpace(thing.PickUpMessage))
            Send(thing.PickUpMessage.Trim(), entity, from: entity);
        thing.RaisePickedUp();
        if (holder is Grabber grabber)
        {
            (grabber.Holding, grabber.InReach) = (entity, null);
            if (!string.IsNullOrWhiteSpace(grabber.PickUpMessage))
                Send(grabber.PickUpMessage.Trim(), entity, from: grabber.Entity);
            grabber.RaisePickedUp(entity);
        }
    }

    // Where a dropped thing goes: back where it came from, or onto a target (swapping out what was there), a grid cell or
    // right where it is. What's swapped out goes into a grabber's hand, or else where the dropped thing came from.
    private sealed record Landing(bool Back, DropTarget? Target = null, Draggable? Swapped = null, Vector2? To = null);

    private static Landing LandingOf(Holding held, Vector2 at, IReadOnlyList<DropTarget> targets, bool intoHand)
    {
        var thing = held.Thing;
        var entity = thing.Entity!;
        var target = TargetAt(at, targets, entity);
        if (target is not null)
        {
            var others = target.Contents.Where(e => e != entity).ToList();
            var room = target.Capacity <= 0 || others.Count < target.Capacity;
            var swap = !room && target.Swap && target.Capacity == 1 && others.Count == 1 &&
                       others[0].GetComponent<Draggable>() is { IsHeld: false, StaysOnTarget: false } &&
                       (intoHand || held.FromTarget?.GetComponent<DropTarget>() is not { } home ||
                        home.AcceptsKind(others[0].GetComponent<Draggable>()!.Kind));
            if (!target.AcceptsKind(thing.Kind) || !(room || swap))
                return new Landing(true);
            return new Landing(false, target, swap ? others[0].GetComponent<Draggable>() : null,
                target.Snap ? DragAreaOf(target.Entity!).Center : null);
        }

        if (thing.OnlyOnTargets)
            return new Landing(true);
        if (thing.Grid <= 0)
            return new Landing(false);
        var cell = thing.Grid;
        return new Landing(false, To: new Vector2((MathF.Floor(at.X / cell) + 0.5f) * cell, (MathF.Floor(at.Y / cell) + 0.5f) * cell));
    }

    private void Drop(int player, Holding held, IReadOnlyList<DropTarget> targets)
    {
        var thing = held.Thing;
        LetGo(player, held);
        var landing = LandingOf(held, CenterOf(thing.Entity!), targets, intoHand: false);
        if (landing.Back)
        {
            SlideTo(thing, held.From);
        }
        else
        {
            if (landing.Swapped is { } swapped)
                SlideTo(swapped, held.From);
            if (landing.To is { } to)
                SlideTo(thing, to);
        }

        Dropped(thing, landing.Target?.Entity);
    }

    // A drop the mouse would send back does nothing here: the grabber keeps holding it.
    private void Put(Grabber grabber, Holding held, IReadOnlyList<DropTarget> targets, Vector2 hand)
    {
        var thing = held.Thing;
        var entity = thing.Entity!;
        var landing = LandingOf(held, CenterOf(entity), targets, intoHand: true);
        if (landing.Back)
        {
            PlayIfSet(grabber.RefuseSound);
            return;
        }

        LetGo(grabber, held);
        if (landing.To is { } to)
            SlideTo(thing, to);
        Dropped(thing, landing.Target?.Entity);
        if (!string.IsNullOrWhiteSpace(grabber.DropMessage))
            Send(grabber.DropMessage.Trim(), entity, from: grabber.Entity);
        grabber.RaiseDropped(entity, landing.Target?.Entity);
        if (landing.Swapped is { } swapped)
            PickUp(grabber, swapped, hand);
    }

    private void Dropped(Draggable thing, Entity? landed)
    {
        PlayIfSet(thing.DropSound);
        if (!string.IsNullOrWhiteSpace(thing.DropMessage))
            Send(thing.DropMessage.Trim(), thing.Entity, from: thing.Entity);
        thing.RaiseDropped(landed);
    }

    internal void LetGo(object holder, Holding held)
    {
        _drags.Held.Remove(holder);
        held.Thing.IsHeld = false;
        if (holder is Grabber grabber)
            grabber.Holding = null;
    }

    // What a grabber would pick up is lit with its highlight; the rest go back to how they were.
    private static void ShowReach(IReadOnlyList<Draggable> draggables, IReadOnlyList<Grabber> grabbers)
    {
        foreach (var thing in draggables)
        {
            if (thing.Entity!.GetComponent<Sprite>() is not { } sprite)
                continue;
            if (grabbers.FirstOrDefault(g => g.InReach == thing.Entity && g.Highlight.A > 0) is { } grabber)
            {
                thing.RestingTint ??= sprite.Tint;
                if (sprite.Tint != grabber.Highlight)
                    sprite.Tint = grabber.Highlight;
            }
            else if (thing.RestingTint is { } resting)
            {
                sprite.Tint = resting;
                thing.RestingTint = null;
            }
        }
    }

    // The drop target on top under a point, leaving out any inside the thing being dropped.
    private static DropTarget? TargetAt(Vector2 point, IReadOnlyList<DropTarget> targets, Entity? dropping = null)
    {
        var under = targets.Where(t => (dropping is null || !t.Entity!.IsSelfOrDescendantOf(dropping)) && Covers(t.Entity!, point)).ToList();
        return under.OrderBy(t => SceneRenderer.OrderOf(t.Entity!) ?? int.MinValue).ThenBy(t => t.Entity!.Transform.WorldPosition.Z).LastOrDefault();
    }

    private void SlideTo(Draggable thing, Vector2 to)
    {
        var from = CenterOf(thing.Entity!);
        if (thing.SlideTime > 0 && Vector2.DistanceSquared(from, to) > 1e-8f)
            _drags.Slides[thing] = new Slide(from, to, thing.SlideTime);
        else
            MoveTo(thing.Entity!, to - from + Flat(thing.Entity!.Transform.WorldPosition));
    }

    // Eased out, so it settles into place.
    private void RunSlides()
    {
        foreach (var (thing, slide) in _drags.Slides.ToList())
        {
            var entity = thing.Entity;
            if (entity?.Scene != Scene || thing.IsHeld)
            {
                _drags.Slides.Remove(thing);
                continue;
            }

            slide.Elapsed += Time.Delta;
            var t = Math.Clamp(slide.Elapsed / slide.Seconds, 0, 1);
            var eased = 1 - (1 - t) * (1 - t);
            var to = Vector2.Lerp(slide.From, slide.To, eased);
            MoveTo(entity, to - CenterOf(entity) + Flat(entity.Transform.WorldPosition));
            if (entity.GetComponent<Body>() is { } body)
                (body.Velocity, body.Spin, body.FloatThisStep) = (Vector2.Zero, 0, true);
            if (t >= 1)
                _drags.Slides.Remove(thing);
        }
    }

    // What rests on each target (not held, not sliding), what arrived and left, and how they look.
    private void UpdateDropTargets(IReadOnlyList<Draggable> draggables, IReadOnlyList<DropTarget> targets)
    {
        var resting = draggables.Where(d => !d.IsHeld && !_drags.Slides.ContainsKey(d)).ToList();
        foreach (var thing in draggables)
        {
            if (!thing.IsHeld && !_drags.Slides.ContainsKey(thing))
                thing.Target = TargetAt(CenterOf(thing.Entity!), targets, thing.Entity)?.Entity;
            else if (thing.IsHeld)
                thing.Target = null;
        }

        foreach (var target in targets)
        {
            var on = resting.Where(d => d.Target == target.Entity).Select(d => d.Entity!).ToList();
            var kept = target.Contents.Where(on.Contains).Concat(on.Where(e => !target.Contents.Contains(e))).ToList();
            target.SetContents(kept);

            // What it starts with isn't an arrival.
            if (target.Seen is { } seen)
            {
                foreach (var gone in seen.Where(e => !kept.Contains(e)).ToList())
                {
                    if (!string.IsNullOrWhiteSpace(target.TakeMessage))
                        Send(target.TakeMessage.Trim(), target.Entity, from: target.Entity);
                    target.RaiseTaken(gone);
                }

                foreach (var arrived in kept.Where(e => !seen.Contains(e)).ToList())
                {
                    if (!string.IsNullOrWhiteSpace(target.DropMessage))
                        Send(target.DropMessage.Trim(), target.Entity, from: target.Entity);
                    target.RaiseReceived(arrived);
                }
            }

            target.Seen = kept;
            ShowHover(target);
        }

        foreach (var group in targets.Where(t => !string.IsNullOrWhiteSpace(t.AllFullMessage)).GroupBy(t => t.AllFullMessage!.Trim()))
        {
            var full = group.All(t => t.IsFull);
            if (_drags.AllFull.TryGetValue(group.Key, out var was) && full && !was)
                Send(group.Key, group.Last().Entity, from: group.Last().Entity);
            _drags.AllFull[group.Key] = full;
        }
    }

    // Lit up while a held thing it would take is over it.
    private void ShowHover(DropTarget target)
    {
        if (target.Entity!.GetComponent<Sprite>() is not { } sprite || target.HoverTint.A <= 0)
            return;
        target.RestingTint ??= sprite.Tint;
        var hovered = _drags.Held.Values.Any(h =>
        {
            var at = CenterOf(h.Thing.Entity!);
            return h.Thing.Entity!.Scene == Scene && Covers(target.Entity!, at) && target.AcceptsKind(h.Thing.Kind) &&
                   (!target.IsFull || target.Contents.Contains(h.Thing.Entity!) || (target.Swap && target.Capacity == 1)) &&
                   TargetAt(at, [target], h.Thing.Entity) is not null;
        });
        var tint = hovered ? target.HoverTint : target.RestingTint.Value;
        if (sprite.Tint != tint)
            sprite.Tint = tint;
    }

    // A draggable's or drop target's shape: its collider's, else its sprite's, else a square a unit across.
    internal static bool Covers(Entity entity, Vector2 point) => entity.GetComponent<Collider>() switch
    {
        { } collider => collider.Contains(point),
        _ => DragAreaOf(entity).Contains(point),
    };

    private static Box DragAreaOf(Entity entity)
    {
        if (entity.GetComponent<Collider>() is { } collider)
            return Physics.BoundsOf(collider);
        var world = entity.Transform.WorldMatrix;
        var scale = new Vector2(new Vector2(world.M11, world.M12).Length(), new Vector2(world.M21, world.M22).Length());
        var size = entity.GetComponent<Sprite>()?.Size ?? Vector2.One;
        return new Box(new Vector2(world.M41, world.M42), Vector2.Abs(size) * scale / 2);
    }
}

/// <summary>Drag and drop: what each mouse and grabber holds, what's sliding into place, and the slots last all full.</summary>
internal sealed class DragSystem : GameSystem
{
    public override string Name => "Dragging";

    // Keyed by the player whose mouse holds it (0 offline), or the grabber holding it.
    public Dictionary<object, Game.Holding> Held { get; } = [];

    public Dictionary<Draggable, Game.Slide> Slides { get; } = [];

    // The All Full Messages that were last seen full, so each is sent as they fill up rather than every frame.
    public Dictionary<string, bool> AllFull { get; } = new(StringComparer.Ordinal);

    protected override void SceneChanged()
    {
        foreach (var (holder, held) in Held.ToList())
            Game.LetGo(holder, held);
        Slides.Clear();
        AllFull.Clear();
    }
}
