using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Drag and drop with the mouse, with no code: draggable things picked up and carried, drop targets that take them, and
// the slides into place. In a network game the host carries things for every player, by their own mouse.
public sealed partial class Game
{
    // What a player's mouse holds: the thing, where it's held from (its middle less the mouse), and where it came from.
    private sealed record Holding(Draggable Thing, Vector2 Grip, Vector2 From, Entity? FromTarget);

    // A thing sliding into place after it was let go.
    private sealed record Slide(Vector2 From, Vector2 To, float Seconds)
    {
        public float Elapsed { get; set; }
    }

    // Keyed by the player holding it (0 offline).
    private readonly Dictionary<int, Holding> _held = [];
    private readonly Dictionary<Draggable, Slide> _slides = [];

    // The All Full Messages that were last seen full, so each is sent as they fill up rather than every frame.
    private readonly Dictionary<string, bool> _allFull = new(StringComparer.Ordinal);

    /// <summary>What this machine's mouse is holding (a <see cref="Draggable"/>'s entity), or null.</summary>
    public Entity? Held => _held.GetValueOrDefault(Net.LocalPlayer?.Id ?? 0)?.Thing.Entity;

    // After scripts and before physics, so a held thing with a body is carried this frame and nothing falls from the hand.
    private void RunDragging()
    {
        foreach (var (player, held) in _held.ToList())
        {
            if (held.Thing.Entity?.Scene != Scene || !SceneRenderer.IsActive(held.Thing.Entity))
                LetGo(player, held);
        }

        var draggables = Active<Draggable>().ToList();
        var targets = Active<DropTarget>().ToList();
        if (draggables.Count == 0 && targets.Count == 0 && _held.Count == 0)
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
        }

        RunSlides();
        foreach (var held in _held.Values)
        {
            if (held.Thing.Entity!.GetComponent<Body>() is { } body)
                (body.Velocity, body.FloatThisStep) = (Vector2.Zero, true);
        }

        UpdateDropTargets(draggables, targets);
    }

    private void Drag(int player, IInput input, List<Draggable> draggables, List<DropTarget> targets)
    {
        var mouse = input.MouseWorld;
        if (_held.TryGetValue(player, out var held))
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
        var free = draggables.Where(d => !d.IsHeld && !(d.StaysOnTarget && d.Target is not null) && Covers(d.Entity!, mouse)).ToHashSet();
        if (free.Count == 0)
            return;
        var top = SceneRenderer.DrawOrder(Scene).Reverse().Select(e => e.GetComponent<Draggable>()).FirstOrDefault(d => d is not null && free.Contains(d))
                  ?? free.First();
        PickUp(player, top, mouse);
    }

    private void PickUp(int player, Draggable thing, Vector2 mouse)
    {
        var entity = thing.Entity!;
        var at = CenterOf(entity);
        // A thing still sliding home is picked up from where it was headed.
        var from = _slides.Remove(thing, out var slide) ? slide.To : at;
        _held[player] = new Holding(thing, at - mouse, from, thing.Target);
        thing.IsHeld = true;
        PlayIfSet(thing.PickUpSound);
        if (!string.IsNullOrWhiteSpace(thing.PickUpMessage))
            Send(thing.PickUpMessage.Trim(), entity);
        thing.RaisePickedUp();
    }

    private void Drop(int player, Holding held, List<DropTarget> targets)
    {
        var thing = held.Thing;
        var entity = thing.Entity!;
        var at = CenterOf(entity);
        LetGo(player, held);

        var target = TargetAt(at, targets, entity);
        Entity? landed = null;
        if (target is not null)
        {
            var others = target.Contents.Where(e => e != entity).ToList();
            var room = target.Capacity <= 0 || others.Count < target.Capacity;
            var swap = !room && target.Swap && target.Capacity == 1 && others.Count == 1 &&
                       others[0].GetComponent<Draggable>() is { IsHeld: false, StaysOnTarget: false } &&
                       (held.FromTarget?.GetComponent<DropTarget>() is not { } home || home.AcceptsKind(others[0].GetComponent<Draggable>()!.Kind));
            if (target.AcceptsKind(thing.Kind) && (room || swap))
            {
                if (swap)
                    SlideTo(others[0].GetComponent<Draggable>()!, held.From);
                if (target.Snap)
                    SlideTo(thing, DragAreaOf(target.Entity!).Center);
                landed = target.Entity;
            }
            else
            {
                SlideTo(thing, held.From);
            }
        }
        else if (thing.OnlyOnTargets)
        {
            SlideTo(thing, held.From);
        }
        else if (thing.Grid > 0)
        {
            var cell = thing.Grid;
            SlideTo(thing, new Vector2((MathF.Floor(at.X / cell) + 0.5f) * cell, (MathF.Floor(at.Y / cell) + 0.5f) * cell));
        }

        PlayIfSet(thing.DropSound);
        if (!string.IsNullOrWhiteSpace(thing.DropMessage))
            Send(thing.DropMessage.Trim(), entity);
        thing.RaiseDropped(landed);
    }

    private void LetGo(int player, Holding held)
    {
        _held.Remove(player);
        held.Thing.IsHeld = false;
    }

    // The drop target on top under a point, leaving out any inside the thing being dropped.
    private static DropTarget? TargetAt(Vector2 point, List<DropTarget> targets, Entity? dropping = null)
    {
        var under = targets.Where(t => (dropping is null || !t.Entity!.IsSelfOrDescendantOf(dropping)) && Covers(t.Entity!, point)).ToList();
        return under.OrderBy(t => SceneRenderer.OrderOf(t.Entity!) ?? int.MinValue).ThenBy(t => t.Entity!.Transform.WorldPosition.Z).LastOrDefault();
    }

    private void SlideTo(Draggable thing, Vector2 to)
    {
        var from = CenterOf(thing.Entity!);
        if (thing.SlideTime > 0 && Vector2.DistanceSquared(from, to) > 1e-8f)
            _slides[thing] = new Slide(from, to, thing.SlideTime);
        else
            MoveTo(thing.Entity!, to - from + Flat(thing.Entity!.Transform.WorldPosition));
    }

    // Eased out, so it settles into place.
    private void RunSlides()
    {
        foreach (var (thing, slide) in _slides.ToList())
        {
            var entity = thing.Entity;
            if (entity?.Scene != Scene || thing.IsHeld)
            {
                _slides.Remove(thing);
                continue;
            }

            slide.Elapsed += Time.Delta;
            var t = Math.Clamp(slide.Elapsed / slide.Seconds, 0, 1);
            var eased = 1 - (1 - t) * (1 - t);
            var to = Vector2.Lerp(slide.From, slide.To, eased);
            MoveTo(entity, to - CenterOf(entity) + Flat(entity.Transform.WorldPosition));
            if (entity.GetComponent<Body>() is { } body)
                (body.Velocity, body.FloatThisStep) = (Vector2.Zero, true);
            if (t >= 1)
                _slides.Remove(thing);
        }
    }

    // What rests on each target (not held, not sliding), what arrived and left, and how they look.
    private void UpdateDropTargets(List<Draggable> draggables, List<DropTarget> targets)
    {
        var resting = draggables.Where(d => !d.IsHeld && !_slides.ContainsKey(d)).ToList();
        foreach (var thing in draggables)
        {
            if (!thing.IsHeld && !_slides.ContainsKey(thing))
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
                        Send(target.TakeMessage.Trim(), target.Entity);
                    target.RaiseTaken(gone);
                }

                foreach (var arrived in kept.Where(e => !seen.Contains(e)).ToList())
                {
                    if (!string.IsNullOrWhiteSpace(target.DropMessage))
                        Send(target.DropMessage.Trim(), target.Entity);
                    target.RaiseReceived(arrived);
                }
            }

            target.Seen = kept;
            ShowHover(target);
        }

        foreach (var group in targets.Where(t => !string.IsNullOrWhiteSpace(t.AllFullMessage)).GroupBy(t => t.AllFullMessage!.Trim()))
        {
            var full = group.All(t => t.IsFull);
            if (_allFull.TryGetValue(group.Key, out var was) && full && !was)
                Send(group.Key, group.Last().Entity);
            _allFull[group.Key] = full;
        }
    }

    // Lit up while a held thing it would take is over it.
    private void ShowHover(DropTarget target)
    {
        if (target.Entity!.GetComponent<Sprite>() is not { } sprite || target.HoverTint.A <= 0)
            return;
        target.RestingTint ??= sprite.Tint;
        var hovered = _held.Values.Any(h =>
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
        CircleCollider circle => Physics.BoundsOf(circle).Contains(point),
        { } collider => Physics.BoundsOf(collider).Contains(point),
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

    private void ForgetDragState()
    {
        foreach (var held in _held.Values)
            held.Thing.IsHeld = false;
        _held.Clear();
        _slides.Clear();
        _allFull.Clear();
    }
}
