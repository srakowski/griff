using System.Globalization;
using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// A point on the primary selection's path that the Scene view puts a handle on: one of its <see cref="Mover"/>'s or
/// <see cref="GridPatrol"/>'s Points (<see cref="Index"/> 1 is the first after the start), where its <see cref="Patrol"/>
/// turns (<see cref="Index"/> -1 for its Left, 1 for its Right), one of its <see cref="PolygonCollider"/>'s Points
/// (<see cref="Index"/> 1 is the first), its <see cref="HingeJoint"/>'s anchor (<see cref="Index"/> 0) and the ends of
/// its limits' arc (-1 for its Lower Angle, 1 for its Upper), or its <see cref="Rope"/>'s two ends (0 where it's tied on the
/// entity, 1 its other end) and middle (2, its length). <see cref="World"/> is where it is in the world (a grid
/// patrol's, the middle of its cell). A patrol's side with no turn set walks on until a wall or an edge: it's
/// <see cref="Open"/>, and its handle sits at the end of a short stub, to drag out.
/// </summary>
public sealed record ScenePathPoint(Component Component, int Index, Vector2 World, bool Open = false)
{
    /// <summary>
    /// "Mover.1" (its first point), "GridPatrol.1", "Patrol.Left", "Patrol.Right", "PolygonCollider.1", "HingeJoint.Anchor",
    /// "HingeJoint.Lower", "HingeJoint.Upper", "Rope.Start", "Rope.End" or "Rope.Length", as
    /// <see cref="SceneViewModel.DragPathPoint"/> takes it.
    /// </summary>
    public string Key => Component switch
    {
        Patrol => Index < 0 ? "Patrol.Left" : "Patrol.Right",
        HingeJoint => Index < 0 ? "HingeJoint.Lower" : Index > 0 ? "HingeJoint.Upper" : "HingeJoint.Anchor",
        Rope => Index == 0 ? "Rope.Start" : Index == 1 ? "Rope.End" : "Rope.Length",
        _ => $"{Component.GetType().Name}.{Index}",
    };

    /// <summary>What agents see: "Mover.1 at 4, 0", or "Patrol.Left not set (walks on until a wall or an edge)".</summary>
    public string Label => Open
        ? $"{Key} not set (walks on until a wall or an edge)"
        : FormattableString.Invariant($"{Key} at {World.X:0.##}, {World.Y:0.##}");
}

/// <summary>
/// Where a hinge swings in the world (see <see cref="SceneViewModel.ArcOf"/>): its <see cref="Anchor"/>, the way it pointed
/// as it was joined (<see cref="Way"/>, a unit vector), and how far out the arc is drawn.
/// </summary>
public readonly record struct HingeArc(Vector2 Anchor, Vector2 Way, float Radius)
{
    /// <summary>The point on the arc <paramref name="degrees"/> round from <see cref="Way"/>, anticlockwise.</summary>
    public Vector2 At(float degrees)
    {
        var (sin, cos) = MathF.SinCos(degrees * MathF.PI / 180);
        return Anchor + new Vector2(cos * Way.X - sin * Way.Y, sin * Way.X + cos * Way.Y) * Radius;
    }
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

    /// <summary>
    /// The middles of the cells a grid patrol's points are on, in the world, starting with where it was placed. It finds
    /// its way around walls between them. Empty without a <see cref="GridMovement"/> to step it.
    /// </summary>
    public static IReadOnlyList<Vector2> RouteOf(GridPatrol patrol) =>
        patrol.Path.Select(p => CellOf(patrol, p.X, p.Y)).OfType<Vector2>().ToList();

    /// <summary>
    /// The middle of the cell <paramref name="x"/>, <paramref name="y"/> cells from where a grid patrol was placed (x right,
    /// y up), in the world: where its point "x y" is. Null without a <see cref="GridMovement"/> to step it.
    /// </summary>
    public static Vector2? CellOf(GridPatrol patrol, int x, int y)
    {
        if (patrol.Entity?.GetComponent<GridMovement>() is not { } movement)
            return null;
        var home = HomeCell(patrol, movement);
        return movement.CellCenter((home.X + x, home.Y - y));
    }

    // The cell a grid patrol was placed on, which its points are counted from: it's Away from it now.
    private static (int X, int Y) HomeCell(GridPatrol patrol, GridMovement movement)
    {
        var here = movement.CellAt(Flat(patrol.Entity!.Transform.WorldPosition));
        return (here.X - (int)MathF.Round(patrol.Away.X), here.Y + (int)MathF.Round(patrol.Away.Y));
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
    /// The points on the primary selection's path the Scene view puts handles on: its mover's and grid patrol's points
    /// after the start (the start is where the entity is placed: move the entity to move it), its patrol's Left and
    /// Right turns, and its polygon collider's points.
    /// </summary>
    public IReadOnlyList<ScenePathPoint> PathPoints()
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } primary || primary.Scene != document.Scene)
            return [];
        var points = new List<ScenePathPoint>();
        if (primary.GetComponent<Mover>() is { } mover)
            points.AddRange(PathOf(mover).Select((world, i) => new ScenePathPoint(mover, i, world)).Skip(1));
        if (primary.GetComponent<GridPatrol>() is { } gridPatrol)
            points.AddRange(RouteOf(gridPatrol).Select((world, i) => new ScenePathPoint(gridPatrol, i, world)).Skip(1));
        if (primary.GetComponent<Patrol>() is { } patrol)
        {
            var route = RouteOf(patrol);
            points.Add(new ScenePathPoint(patrol, -1, route.Left, route.LeftOpen));
            points.Add(new ScenePathPoint(patrol, 1, route.Right, route.RightOpen));
        }

        if (primary.GetComponent<PolygonCollider>() is { } polygon)
            points.AddRange(polygon.GivenPoints.Select((point, i) => new ScenePathPoint(polygon, i + 1, polygon.ToWorld(point))));

        // A hinge's anchor, and with limits, the two ends of the arc it swings through.
        if (primary.GetComponent<HingeJoint>() is { } hinge)
        {
            var arc = ArcOf(hinge);
            points.Add(new ScenePathPoint(hinge, 0, arc.Anchor));
            if (hinge.Limit)
            {
                points.Add(new ScenePathPoint(hinge, -1, arc.At(hinge.LowerAngle)));
                points.Add(new ScenePathPoint(hinge, 1, arc.At(hinge.UpperAngle)));
            }
        }

        // A rope's two ends, and its middle as it hangs (its length): the length knob first, taken where it sits on its loose end.
        if (primary.GetComponent<Rope>() is { } rope)
        {
            var (start, end, middle) = KnobsOf(rope);
            points.Add(new ScenePathPoint(rope, 0, start));
            points.Add(new ScenePathPoint(rope, 2, middle));
            points.Add(new ScenePathPoint(rope, 1, end));
        }

        return points;
    }

    /// <summary>
    /// Where a rope's knobs go in the world: where it's tied on its entity, its other end (on what it's tied to, or where its
    /// loose end starts), and the knob for its length: the middle of the rope as it hangs, or (loose) the tip of it.
    /// </summary>
    public static (Vector2 Start, Vector2 End, Vector2 Length) KnobsOf(Rope rope)
    {
        var (start, end) = (rope.WorldAnchor, rope.WorldOtherAnchor);
        if (rope.EndIsLoose)
            return (start, end, LooseTip(rope, rope.PlacedLength));
        Span<Vector2> shape = stackalloc Vector2[Rope.MaxPoints];
        var count = rope.Shape(shape, StepBlend.AsIs);
        var middle = count % 2 == 1 ? shape[count / 2] : (shape[count / 2 - 1] + shape[count / 2]) / 2;
        return (start, end, middle);
    }

    /// <summary>
    /// Where a rope's length knob goes for it to be <paramref name="length"/> long: below the middle between its ends (or, loose,
    /// that far along it), for a drag of the knob to set it.
    /// </summary>
    public static Vector2 RopeLengthPoint(Rope rope, float length)
    {
        if (rope.EndIsLoose)
            return LooseTip(rope, length);
        var (start, end) = (rope.WorldAnchor, rope.WorldOtherAnchor);
        var half = Vector2.Distance(start, end) / 2;
        var across = end - start;
        var down = across.LengthSquared() > 1e-8f ? Vector2.Normalize(new Vector2(across.Y, -across.X)) : -Vector2.UnitY;
        if (down.Y > 0)
            down = -down;
        return (start + end) / 2 + down * MathF.Sqrt(MathF.Max(0, length * length / 4 - half * half));
    }

    // The tip of a loose rope `length` long: laid out towards where its end starts, and on down from there.
    private static Vector2 LooseTip(Rope rope, float length)
    {
        var (start, end) = (rope.WorldAnchor, rope.WorldOtherAnchor);
        var reach = Vector2.Distance(start, end);
        return length <= reach && reach > 1e-5f ? start + (end - start) / reach * length : end - new Vector2(0, length - reach);
    }

    // How long a rope is with its length knob dragged to `world`: the way from its start through there to its other end (or,
    // loose, to where its end starts and on down to there), never shorter than its ends are apart.
    private static float RopeLengthAt(Rope rope, Vector2 world)
    {
        var (start, end) = (rope.WorldAnchor, rope.WorldOtherAnchor);
        var reach = Vector2.Distance(start, end);
        if (rope.EndIsLoose)
            return MathF.Max(0.1f, world.Y < end.Y ? reach + (end.Y - world.Y) : Vector2.Distance(start, world));
        return MathF.Max(reach, Vector2.Distance(start, world) + Vector2.Distance(world, end));
    }

    /// <summary>
    /// Where a hinge swings in the world: round its anchor, from the way it points as the two are joined (from the anchor
    /// to its body's middle, or along its own X with the anchor at its middle) at <see cref="HingeArc.Radius"/>, angles
    /// counted anticlockwise from that.
    /// </summary>
    public static HingeArc ArcOf(HingeJoint hinge)
    {
        var entity = hinge.Entity!;
        var anchor = hinge.WorldAnchor;
        var middle = entity.GetComponent<Collider>() is { } collider ? collider is PolygonCollider polygon ? polygon.WorldCentroid : collider.WorldBounds.Center : Flat(entity.Transform.WorldPosition);
        var arm = middle - anchor;
        var world = entity.Transform.WorldMatrix;
        var way = arm.Length() > 0.05f ? Vector2.Normalize(arm) : new Vector2(world.M11, world.M12) is var x && x != Vector2.Zero ? Vector2.Normalize(x) : Vector2.UnitX;
        // Back to how it pointed as the two were joined.
        var back = -hinge.Angle * MathF.PI / 180;
        var (sin, cos) = MathF.SinCos(back);
        way = new Vector2(cos * way.X - sin * way.Y, sin * way.X + cos * way.Y);
        return new HingeArc(anchor, way, Math.Clamp(arm.Length(), 0.75f, 3));
    }

    /// <summary>
    /// Drags a point of the primary selection's path (see <see cref="PathPoints"/>), named like "Mover.2", "GridPatrol.1",
    /// "Patrol.Left", "PolygonCollider.3", "HingeJoint.Anchor", "HingeJoint.Lower", "HingeJoint.Upper", "Rope.Start", "Rope.End"
    /// or "Rope.Length", to a point in the
    /// world, as one undo step. A mover's point lands there, its offset on hundredths (<paramref name="snap"/>: half units); a
    /// grid patrol's lands on the cell there; a patrol's turn goes level with it on its side, and dragging it back past where
    /// the patrol was placed clears it, so it walks on until a wall or an edge; a polygon's point lands there, in its own
    /// units on hundredths (<paramref name="snap"/>: half units); a hinge's anchor too (joining the two again there if the
    /// game had joined them), and a hinge's limit goes to the angle round its arc the point is at, in whole degrees
    /// (<paramref name="snap"/>: fifteens). A rope's start lands there in its entity's own units, its end in the own units of what it's
    /// tied to (or in the world), and its length is the way from its start through the point to its end (loose: on down to the
    /// point), on hundredths (<paramref name="snap"/>: half units).
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
    /// Adds a point to the primary selection's mover (or with none, its grid patrol, or with neither, its polygon
    /// collider; <paramref name="component"/> names which, e.g. "PolygonCollider") at a point in the world, as one undo
    /// step: it becomes point <paramref name="index"/> (on a path 1 is right after the start, on a polygon its first point;
    /// one past the last adds it at the end, a polygon's between its last and first), a mover's offset or a polygon's point
    /// on hundredths (<paramref name="snap"/>: half units), a grid patrol's on the cell there.
    /// </summary>
    public void InsertPathPoint(int index, Vector2 at, bool snap = false, string? component = null)
    {
        var entity = selection.Entity;
        var path = component is { Length: > 0 }
            ? entity?.Components.FirstOrDefault(c => c is Mover or GridPatrol or PolygonCollider && c.GetType().Name.Equals(component.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            : (Component?)entity?.GetComponent<Mover>() ?? (Component?)entity?.GetComponent<GridPatrol>() ?? entity?.GetComponent<PolygonCollider>();
        if (path is null)
            throw new ArgumentException(component is { Length: > 0 }
                ? $"The selection has no {component} to add a point to."
                : "The selection has no Mover, Grid Patrol or Polygon Collider to add a point to.");
        if (path is GridPatrol && path.Entity!.GetComponent<GridMovement>() is null)
            throw new ArgumentException("The selection's Grid Patrol has no Grid Movement to step it: add one first.");
        var count = PointCount(path);
        if (index < 1 || index > count + 1)
            throw new ArgumentOutOfRangeException(nameof(index), path is PolygonCollider
                ? $"A point goes from 1 (before the first) to {count + 1} (after the last)."
                : $"A point goes from 1 (right after the start) to {count + 1} (after the last).");
        if (BeginInsertPathPoint(path, index) is not { } drag)
            return;
        workspace.Scene!.History.Seal();
        drag.To(at, snap);
        drag.End();
    }

    /// <summary>
    /// Takes a point off the primary selection's path, as one undo step: a mover's or grid patrol's point (it keeps at
    /// least one, to go to), a patrol's turn (it then walks on until a wall or an edge on that side), a polygon's point
    /// (it keeps at least three), a hinge's limit (it then swings freely; its anchor stays), or a rope's length (it's then as long as
    /// its ends are apart as the game starts).
    /// </summary>
    public void RemovePathPoint(string point)
    {
        var found = FindPathPoint(point);
        if (found.Open || workspace.Scene is not { } document)
            return;
        var (property, value) = found.Component switch
        {
            HingeJoint when found.Index == 0 =>
                throw new InvalidOperationException("A hinge needs its anchor: drag it instead, or remove the Hinge Joint."),
            HingeJoint hinge => (Property(hinge, nameof(HingeJoint.Limit)), (object?)false),
            Rope when found.Index < 2 =>
                throw new InvalidOperationException("A rope needs both its ends: drag this one instead, or remove the Rope."),
            Rope rope => (Property(rope, nameof(Rope.Length)), (object?)0f),
            Mover or GridPatrol when PointCount(found.Component) <= 1 =>
                throw new InvalidOperationException("It needs somewhere to go: that's its only point. Drag it instead."),
            Mover or GridPatrol => (Property(found.Component, nameof(Mover.Points)),
                (object?)FormatPoints(PointsOf(found.Component).Where((_, i) => i != found.Index - 1))),
            PolygonCollider when PointCount(found.Component) <= 3 =>
                throw new InvalidOperationException("A polygon needs at least three points: drag this one instead."),
            PolygonCollider => (Property(found.Component, nameof(PolygonCollider.Points)),
                (object?)FormatPoints(PointsOf(found.Component).Where((_, i) => i != found.Index - 1))),
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
            Mover or GridPatrol => new PathPointDrag(document, point.Component, Property(point.Component, nameof(Mover.Points)), point.Index, insert: false),
            Patrol patrol => new PathPointDrag(document, patrol, Property(patrol, point.Index < 0 ? nameof(Patrol.Left) : nameof(Patrol.Right)), point.Index, insert: false),
            PolygonCollider polygon => new PathPointDrag(document, polygon, Property(polygon, nameof(PolygonCollider.Points)), point.Index, insert: false),
            HingeJoint hinge => new PathPointDrag(document, hinge, Property(hinge, point.Index < 0 ? nameof(HingeJoint.LowerAngle) : point.Index > 0 ? nameof(HingeJoint.UpperAngle) : nameof(HingeJoint.Anchor)),
                point.Index, insert: false, joined: point.Index == 0 && hinge.Joined ? Property(hinge, nameof(HingeJoint.Joined)) : null),
            Rope rope => new PathPointDrag(document, rope, Property(rope, point.Index == 0 ? nameof(Rope.Anchor) : point.Index == 1 ? nameof(Rope.OtherAnchor) : nameof(Rope.Length)),
                point.Index, insert: false),
            _ => null,
        };
    }

    /// <summary>
    /// Starts dragging a new point into the primary selection's <see cref="Mover"/>, <see cref="GridPatrol"/> or
    /// <see cref="PolygonCollider"/>, for a drag of the handle between two of its points (or past its last): the first
    /// <see cref="PathPointDrag.To"/> adds it as point <paramref name="index"/>, and the whole drag is one undo step. Null
    /// when it isn't the primary selection's, or a grid patrol has no <see cref="GridMovement"/> to step it.
    /// </summary>
    public PathPointDrag? BeginInsertPathPoint(Component path, int index)
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } primary || path.Entity != primary ||
            path is not (Mover or GridPatrol or PolygonCollider) || path is GridPatrol && primary.GetComponent<GridMovement>() is null)
            return null;
        // Mover, GridPatrol and PolygonCollider all call theirs Points.
        return new PathPointDrag(document, path, Property(path, nameof(Mover.Points)), Math.Clamp(index, 1, PointCount(path) + 1), insert: true);
    }

    private ScenePathPoint FindPathPoint(string point)
    {
        var points = PathPoints();
        return points.FirstOrDefault(p => string.Equals(p.Key, point.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(points.Count == 0
                ? $"The selection has no path point \"{point}\": select an entity with a Mover, a Grid Patrol, a Patrol, a Polygon Collider, a Hinge Joint or a Rope."
                : $"The selection has no path point \"{point}\"; it has {string.Join(", ", points.Select(p => p.Key))}.");
    }

    private PropertyDescriptor Property(Component component, string name) =>
        workspace.Components.Get(component.GetType()).Properties.Single(p => p.Name == name);

    // A mover's or grid patrol's points after the start, as offsets from it; a polygon's points.
    private static List<Vector2> PointsOf(Component path) => path switch
    {
        Mover mover => mover.Path.Skip(1).ToList(),
        GridPatrol patrol => patrol.Path.Skip(1).Select(p => new Vector2(p.X, p.Y)).ToList(),
        PolygonCollider polygon => polygon.GivenPoints.ToList(),
        _ => [],
    };

    private static int PointCount(Component path) => PointsOf(path).Count;

    // "4 0; 4 3", as Mover.Points, GridPatrol.Points and PolygonCollider.Points read it.
    private static string FormatPoints(IEnumerable<Vector2> points) =>
        string.Join("; ", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:0.##} {p.Y:0.##}")));

    /// <summary>
    /// One drag of a path point's handle (or a polygon's point's), from <see cref="BeginPathPoint"/> or
    /// <see cref="BeginInsertPathPoint"/>.
    /// </summary>
    public sealed class PathPointDrag
    {
        /// <summary>
        /// The step Ctrl snaps points and turns to: half a unit (a polygon's points in its own units). A grid patrol's points
        /// are always on cells.
        /// </summary>
        public const float SnapStep = 0.5f;

        private const float RoundStep = 0.01f;

        private readonly SceneDocument _document;
        private readonly PropertyDescriptor _property;
        private readonly bool _insert;
        private readonly Matrix4x4 _toParent;
        private readonly Vector2 _start;
        private readonly List<Vector2> _points;
        private readonly GridMovement? _grid;
        private readonly (int X, int Y) _home;

        // A hinge's arc as the drag began, and the whole drag as one undo step when it unjoins the hinge too.
        private readonly HingeArc _arc;
        private readonly IDisposable? _group;

        internal PathPointDrag(SceneDocument document, Component component, PropertyDescriptor property, int index, bool insert,
            PropertyDescriptor? joined = null)
        {
            _document = document;
            _property = property;
            _insert = insert;
            Component = component;
            Index = index;
            if (component is HingeJoint hinge)
                _arc = ArcOf(hinge);
            var entity = component.Entity!;
            _toParent = entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse) ? inverse : Matrix4x4.Identity;
            // Where the path is measured from, in the parent's space: a mover's start, a patrol's home. A grid patrol's
            // points are counted in cells from the one it was placed on.
            (_start, _points) = component switch
            {
                Mover mover => (Flat(entity.Transform.Position) - mover.OffsetAt(mover.Time), PointsOf(mover)),
                Patrol patrol => (new Vector2(entity.Transform.Position.X - patrol.Walked, entity.Transform.Position.Y), []),
                _ => (Vector2.Zero, PointsOf(component)),
            };
            if (component is GridPatrol gridPatrol && entity.GetComponent<GridMovement>() is { } movement)
                (_grid, _home) = (movement, HomeCell(gridPatrol, movement));
            document.History.Seal();

            // Moving a hinge the game has joined joins it again where it's moved to.
            if (joined is not null)
            {
                _group = document.History.BeginGroup("Move Hinge");
                document.Execute(new SetPropertyEdit(component, joined, false));
            }
        }

        /// <summary>
        /// The mover, grid patrol or patrol whose path it is, the polygon collider whose point it is, or the hinge whose anchor
        /// or limit it is.
        /// </summary>
        public Component Component { get; }

        /// <summary>
        /// The point being dragged: a mover's, grid patrol's or polygon's point from 1, or -1 and 1 for a patrol's Left and
        /// Right.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Puts the point at <paramref name="world"/>, on hundredths, or with <paramref name="snap"/> on half units, from
        /// the start (a patrol's turn only goes along its side, and clears when dragged back past home; a grid patrol's
        /// point goes on the cell there; a polygon's point is in its own units).
        /// </summary>
        public void To(Vector2 world, bool snap)
        {
            var perUnit = MathF.Round(1 / (snap ? SnapStep : RoundStep));
            float Fit(float value) => MathF.Round(value * perUnit) / perUnit + 0f; // + 0 turns -0 into 0
            var offset = Vector2.Transform(world, _toParent) - _start;

            object? value;
            if (Component is HingeJoint hinge)
                value = Index == 0 ? HingeAnchor(hinge, world, Fit) : HingeLimit(hinge, world, snap);
            else if (Component is Rope rope)
                value = Index == 2 ? Fit(RopeLengthAt(rope, world)) : Index == 0 ? OwnPoint(rope.Entity!, world, Fit) : RopeEnd(rope, world, Fit);
            else if (Component is Patrol)
                value = MathF.Max(0, Fit(offset.X * MathF.Sign(Index)));
            else
            {
                var points = _points.ToList();
                var own = Component is PolygonCollider polygon ? polygon.FromWorld(world) : offset;
                var point = _grid?.CellAt(world) is (int x, int y)
                    ? new Vector2(x - _home.X, _home.Y - y)
                    : new Vector2(Fit(own.X), Fit(own.Y));
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

        // A hinge's anchor at a point in the world, in its entity's own units.
        private static Vector2 HingeAnchor(HingeJoint hinge, Vector2 world, Func<float, float> fit) => OwnPoint(hinge.Entity!, world, fit);

        // A point in the world in an entity's own units.
        private static Vector2 OwnPoint(Entity entity, Vector2 world, Func<float, float> fit)
        {
            var own = Matrix4x4.Invert(entity.Transform.WorldMatrix, out var toOwn) ? Vector2.Transform(world, toOwn) : world;
            return new Vector2(fit(own.X), fit(own.Y));
        }

        // A rope's other end at a point in the world: in the own units of what it's tied to, or in the world.
        private static Vector2 RopeEnd(Rope rope, Vector2 world, Func<float, float> fit) =>
            !string.IsNullOrWhiteSpace(rope.To) && rope.OtherEntity is { } other ? OwnPoint(other, world, fit) : new Vector2(fit(world.X), fit(world.Y));

        // A hinge's limit at the angle round its arc a point in the world is at, in whole degrees (with snap, fifteens),
        // kept on its own side of the other limit.
        private float HingeLimit(HingeJoint hinge, Vector2 world, bool snap)
        {
            var toward = world - _arc.Anchor;
            var degrees = MathF.Atan2(_arc.Way.X * toward.Y - _arc.Way.Y * toward.X, Vector2.Dot(_arc.Way, toward)) * 180 / MathF.PI;
            var step = snap ? 15 : 1;
            degrees = MathF.Round(degrees / step) * step + 0f;
            return Index < 0 ? Math.Clamp(degrees, -180, Math.Min(180, hinge.UpperAngle)) : Math.Clamp(degrees, Math.Max(-180, hinge.LowerAngle), 180);
        }

        /// <summary>Ends the drag, so the next change is its own undo step.</summary>
        public void End()
        {
            _group?.Dispose();
            _document.History.Seal();
        }
    }
}
