using System.Globalization;
using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// A point on the primary selection's path that the Scene view puts a handle on: one of its <see cref="Mover"/>'s
/// <see cref="Mover.Points"/> (<see cref="Index"/> 1 is the first after the start), or where its <see cref="Patrol"/>
/// turns (<see cref="Index"/> -1 for its Left, 1 for its Right). <see cref="World"/> is where it is in the world. A
/// patrol's side with no turn set walks on until a wall or an edge: it's <see cref="Open"/>, and its handle sits at the end
/// of a short stub, to drag out.
/// </summary>
public sealed record ScenePathPoint(Component Component, int Index, Vector2 World, bool Open = false)
{
    /// <summary>"Mover.1" (its first point), "Patrol.Left" or "Patrol.Right", as <see cref="SceneViewModel.DragPathPoint"/> takes it.</summary>
    public string Key => Component is Patrol ? Index < 0 ? "Patrol.Left" : "Patrol.Right" : $"{Component.GetType().Name}.{Index}";

    /// <summary>What agents see: "Mover.1 at 4, 0", or "Patrol.Left not set (walks on until a wall or an edge)".</summary>
    public string Label => Open
        ? $"{Key} not set (walks on until a wall or an edge)"
        : FormattableString.Invariant($"{Key} at {World.X:0.##}, {World.Y:0.##}");
}

/// <summary>
/// A patrol's route in the world: where it was placed (<see cref="Home"/>), and where it turns on each side. A side with no
/// turn set is open (it walks on until a wall or an edge), its end a short stub out from home.
/// </summary>
public sealed record PatrolRoute(Vector2 Home, Vector2 Left, Vector2 Right, bool LeftOpen, bool RightOpen);

public sealed partial class SceneViewModel
{
    /// <summary>How far out from home an open side of a patrol's route reaches, in its parent's units.</summary>
    public const float OpenRouteStub = 1.5f;

    /// <summary>The points a mover's path goes through in the world, starting where its trip starts.</summary>
    public static IReadOnlyList<Vector2> PathOf(Mover mover)
    {
        var entity = mover.Entity!;
        var parent = entity.Parent?.Transform.WorldMatrix ?? Matrix4x4.Identity;
        var start = Flat(entity.Transform.Position) - mover.OffsetAt(mover.Time);
        return mover.Path.Select(p => Vector2.Transform(start + p, parent)).ToList();
    }

    /// <summary>A patrol's route in the world (see <see cref="PatrolRoute"/>).</summary>
    public static PatrolRoute RouteOf(Patrol patrol)
    {
        var entity = patrol.Entity!;
        var parent = entity.Parent?.Transform.WorldMatrix ?? Matrix4x4.Identity;
        var home = new Vector2(entity.Transform.Position.X - patrol.Walked, entity.Transform.Position.Y);
        Vector2 At(float x) => Vector2.Transform(home with { X = home.X + x }, parent);
        var (left, right) = (patrol.Left > 0, patrol.Right > 0);
        return new PatrolRoute(At(0), At(left ? -patrol.Left : -OpenRouteStub), At(right ? patrol.Right : OpenRouteStub), !left, !right);
    }

    /// <summary>
    /// The points on the primary selection's path the Scene view puts handles on: its mover's points after the start
    /// (the start is where the entity is placed: move the entity to move it), and its patrol's Left and Right turns.
    /// </summary>
    public IReadOnlyList<ScenePathPoint> PathPoints()
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } primary || primary.Scene != document.Scene)
            return [];
        var points = new List<ScenePathPoint>();
        if (primary.GetComponent<Mover>() is { } mover)
            points.AddRange(PathOf(mover).Select((world, i) => new ScenePathPoint(mover, i, world)).Skip(1));
        if (primary.GetComponent<Patrol>() is { } patrol)
        {
            var route = RouteOf(patrol);
            points.Add(new ScenePathPoint(patrol, -1, route.Left, route.LeftOpen));
            points.Add(new ScenePathPoint(patrol, 1, route.Right, route.RightOpen));
        }

        return points;
    }

    /// <summary>
    /// Drags a point of the primary selection's path (see <see cref="PathPoints"/>), named like "Mover.2" or
    /// "Patrol.Left", to a point in the world, as one undo step. A mover's point lands there, its offset on hundredths
    /// (<paramref name="snap"/>: half units); a patrol's turn goes level with it on its side, and dragging it back past
    /// where the patrol was placed clears it, so it walks on until a wall or an edge.
    /// </summary>
    public void DragPathPoint(string point, Vector2 to, bool snap = false)
    {
        if (BeginPathPoint(FindPathPoint(point)) is not { } drag)
            return;
        workspace.Scene!.History.Seal();
        drag.To(to, snap);
        drag.End();
    }

    /// <summary>
    /// Adds a point to the primary selection's mover at a point in the world, as one undo step: it becomes point
    /// <paramref name="index"/> (1 is right after the start; one past the last adds it at the end), its offset on
    /// hundredths (<paramref name="snap"/>: half units).
    /// </summary>
    public void InsertPathPoint(int index, Vector2 at, bool snap = false)
    {
        if (selection.Entity?.GetComponent<Mover>() is not { } mover)
            throw new ArgumentException("The selection has no Mover to add a point to.");
        var count = mover.Path.Count - 1;
        if (index < 1 || index > count + 1)
            throw new ArgumentOutOfRangeException(nameof(index), $"A point goes from 1 (right after the start) to {count + 1} (after the last).");
        if (BeginInsertPathPoint(mover, index) is not { } drag)
            return;
        workspace.Scene!.History.Seal();
        drag.To(at, snap);
        drag.End();
    }

    /// <summary>
    /// Takes a point off the primary selection's path, as one undo step: a mover's point (it keeps at least one, to go
    /// to), or a patrol's turn (it then walks on until a wall or an edge on that side).
    /// </summary>
    public void RemovePathPoint(string point)
    {
        var found = FindPathPoint(point);
        if (found.Open || workspace.Scene is not { } document)
            return;
        var (property, value) = found.Component switch
        {
            Mover mover when mover.Path.Count <= 2 =>
                throw new InvalidOperationException("A mover needs somewhere to go: that's its only point. Drag it instead."),
            Mover mover => (Property(mover, nameof(Mover.Points)), (object?)FormatPoints(mover.Path.Skip(1).Where((_, i) => i != found.Index - 1))),
            _ => (Property(found.Component, found.Index < 0 ? nameof(Patrol.Left) : nameof(Patrol.Right)), 0f),
        };
        document.History.Seal();
        document.Execute(new SetPropertyEdit(found.Component, property, value));
        document.History.Seal();
    }

    /// <summary>
    /// Starts dragging a point of the primary selection's path, for a drag of its handle: <see cref="PathPointDrag.To"/>
    /// moves it, and the whole drag is one undo step. Null when it isn't the primary selection's.
    /// </summary>
    public PathPointDrag? BeginPathPoint(ScenePathPoint point)
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } primary || point.Component.Entity != primary)
            return null;
        return point.Component switch
        {
            Mover mover => new PathPointDrag(document, mover, Property(mover, nameof(Mover.Points)), point.Index, insert: false),
            Patrol patrol => new PathPointDrag(document, patrol, Property(patrol, point.Index < 0 ? nameof(Patrol.Left) : nameof(Patrol.Right)), point.Index, insert: false),
            _ => null,
        };
    }

    /// <summary>
    /// Starts dragging a new point into the primary selection's mover, for a drag of the handle between two of its points
    /// (or past its last): the first <see cref="PathPointDrag.To"/> adds it as point <paramref name="index"/>, and the
    /// whole drag is one undo step. Null when it isn't the primary selection's.
    /// </summary>
    public PathPointDrag? BeginInsertPathPoint(Mover mover, int index)
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } primary || mover.Entity != primary)
            return null;
        return new PathPointDrag(document, mover, Property(mover, nameof(Mover.Points)), Math.Clamp(index, 1, mover.Path.Count), insert: true);
    }

    private ScenePathPoint FindPathPoint(string point)
    {
        var points = PathPoints();
        return points.FirstOrDefault(p => string.Equals(p.Key, point.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(points.Count == 0
                ? $"The selection has no path point \"{point}\": select an entity with a Mover or a Patrol."
                : $"The selection has no path point \"{point}\"; it has {string.Join(", ", points.Select(p => p.Key))}.");
    }

    private PropertyDescriptor Property(Component component, string name) =>
        workspace.Components.Get(component.GetType()).Properties.Single(p => p.Name == name);

    // "4 0; 4 3", as Mover.Points reads it.
    private static string FormatPoints(IEnumerable<Vector2> points) =>
        string.Join("; ", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:0.##} {p.Y:0.##}")));

    /// <summary>One drag of a path point's handle, from <see cref="BeginPathPoint"/> or <see cref="BeginInsertPathPoint"/>.</summary>
    public sealed class PathPointDrag
    {
        /// <summary>The step Ctrl snaps points and turns to: half a unit.</summary>
        public const float SnapStep = 0.5f;

        private const float RoundStep = 0.01f;

        private readonly SceneDocument _document;
        private readonly PropertyDescriptor _property;
        private readonly bool _insert;
        private readonly Matrix4x4 _toParent;
        private readonly Vector2 _start;
        private readonly List<Vector2> _points;

        internal PathPointDrag(SceneDocument document, Component component, PropertyDescriptor property, int index, bool insert)
        {
            _document = document;
            _property = property;
            _insert = insert;
            Component = component;
            Index = index;
            var entity = component.Entity!;
            _toParent = entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse) ? inverse : Matrix4x4.Identity;
            // Where the path is measured from, in the parent's space: a mover's start, a patrol's home.
            (_start, _points) = component switch
            {
                Mover mover => (Flat(entity.Transform.Position) - mover.OffsetAt(mover.Time), mover.Path.Skip(1).ToList()),
                Patrol patrol => (new Vector2(entity.Transform.Position.X - patrol.Walked, entity.Transform.Position.Y), []),
                _ => (Vector2.Zero, []),
            };
            document.History.Seal();
        }

        /// <summary>The mover or patrol whose path it is.</summary>
        public Component Component { get; }

        /// <summary>The point being dragged: a mover's point from 1, or -1 and 1 for a patrol's Left and Right.</summary>
        public int Index { get; }

        /// <summary>
        /// Puts the point at <paramref name="world"/>, on hundredths, or with <paramref name="snap"/> on half units, from
        /// the start (a patrol's turn only goes along its side, and clears when dragged back past home).
        /// </summary>
        public void To(Vector2 world, bool snap)
        {
            var perUnit = MathF.Round(1 / (snap ? SnapStep : RoundStep));
            float Fit(float value) => MathF.Round(value * perUnit) / perUnit + 0f; // + 0 turns -0 into 0
            var offset = Vector2.Transform(world, _toParent) - _start;

            object? value;
            if (Component is Patrol)
                value = MathF.Max(0, Fit(offset.X * MathF.Sign(Index)));
            else
            {
                var points = _points.ToList();
                var point = new Vector2(Fit(offset.X), Fit(offset.Y));
                if (_insert)
                    points.Insert(Index - 1, point);
                else
                    points[Index - 1] = point;
                value = FormatPoints(points);
            }

            if (Equals(_property.GetValue(Component), value))
                return;
            _document.Execute(new SetPropertyEdit(Component, _property, value));
        }

        /// <summary>Ends the drag, so the next change is its own undo step.</summary>
        public void End() => _document.History.Seal();
    }
}
