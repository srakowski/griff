using System.Numerics;
using System.Windows;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Point = System.Windows.Point;
using Vector = System.Windows.Vector;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// A handle on the primary selection's path: on one of its points, or (with no Point) a plus that adds point Index to
/// its Mover or GridPatrol (its Path).
/// </summary>
internal sealed record PathHandle(string Key, Point Screen, ScenePathPoint? Point, Component? Path, int Index);

/// <summary>
/// Where the Scene view's handles on the primary selection are on screen, and which one is under a point: the rotate
/// knob, the scale and resize squares, the knobs on its distances and its path, and the arrows of its links. What a
/// drag on one does is <see cref="SceneViewModel"/>'s; this only says where they are, for drawing and for the mouse.
/// </summary>
internal sealed class SceneHandleGeometry(SceneViewport viewport, SceneViewModel model, ISelection selection, IWorkspace workspace,
    Func<string, Size> measure)
{
    public const double MarkerRadius = 7;
    public const double HandleRadius = 5;
    public const double HandleSquare = 3.5;
    private const double HandleStem = 24;
    private const double MarkerFrame = MarkerRadius + 8;
    private const double EdgeHandleRoom = 28;
    private const double ScaleHandleRoom = 16;
    private const double PathGap = 28;

    // Where the scale handles sit on the primary selection's frame, in its own axes: corners and edge middles.
    private static readonly Vector2[] ScaleSpots =
        [new(-1, -1), new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0)];

    /// <summary>True while a path point is being dragged: the pluses that add points hide meanwhile.</summary>
    public bool DraggingPathPoint { get; set; }

    private SceneDocument? Document => workspace.Scene;

    /// <summary>Topmost unlocked entity under the point (see <see cref="SceneViewModel.EntityAt"/>); pivots are hit within a few pixels.</summary>
    public Entity? HitTest(Point screen) =>
        Document is null ? null : model.EntityAt(viewport.ToWorld(screen), (float)((MarkerRadius + 3) / viewport.Zoom));

    /// <summary>
    /// The frame the primary selection's handles sit on, in screen space: its outline's corners, or a box around its
    /// marker, as (-1, -1), (1, -1), (1, 1) and (-1, 1) in its own axes. Null when there's nothing to handle, or while
    /// painting.
    /// </summary>
    public Point[]? HandleFrame()
    {
        if (Document is null || selection.Entity is not { } entity || entity.Scene != Document.Scene || model.PaintTarget is not null)
            return null;
        if (SceneViewModel.Outline(entity) is { } corners)
            return corners.Select(c => viewport.ToScreen(c)).ToArray();

        var world = entity.Transform.WorldMatrix;
        var right = ScreenDirection(Vector3.UnitX, world, new Vector(1, 0)) * MarkerFrame;
        var up = ScreenDirection(Vector3.UnitY, world, new Vector(0, -1)) * MarkerFrame;
        var pivot = viewport.ToScreen(Flat(entity.Transform.WorldPosition));
        return [pivot - right - up, pivot + right - up, pivot + right + up, pivot - right + up];
    }

    /// <summary>A point on the frame from its spot in the entity's own axes, each from -1 to 1.</summary>
    public static Point FramePoint(Point[] frame, Vector2 spot)
    {
        var (u, v) = ((spot.X + 1) / 2, (spot.Y + 1) / 2);
        var bottom = frame[0] + (frame[1] - frame[0]) * u;
        var top = frame[3] + (frame[2] - frame[3]) * u;
        return bottom + (top - bottom) * v;
    }

    // Which way one of an entity's own axes points on screen (Y down), or the fallback when it's squashed flat.
    private static Vector ScreenDirection(Vector3 axis, Matrix4x4 world, Vector fallback)
    {
        var v = Vector3.TransformNormal(axis, world);
        var direction = new Vector(v.X, -v.Y);
        if (direction.Length < 1e-6)
            return fallback;
        direction.Normalize();
        return direction;
    }

    /// <summary>
    /// Where the rotate handle is, in screen space: a stem from the top edge of the primary selection's frame out along
    /// its up direction, ending in the knob. Null when there's nothing to turn, or while painting.
    /// </summary>
    public (Point Root, Point Knob)? RotateHandle()
    {
        if (HandleFrame() is not { } frame)
            return null;
        var root = FramePoint(frame, new Vector2(0, 1));
        return (root, root + ScreenDirection(Vector3.UnitY, selection.Entity!.Transform.WorldMatrix, new Vector(0, -1)) * HandleStem);
    }

    public bool IsOverRotateHandle(Point screen) =>
        RotateHandle() is var (_, knob) && (screen - knob).Length <= HandleRadius + 4;

    /// <summary>
    /// The scale handles, in screen space, with their spots on the frame (see <see cref="ScaleSpots"/>). Edges too short
    /// to hold a handle in their middle go without, and something drawn too small for handles to stay clear of the
    /// middle, where it's grabbed to move it, gets none until the view zooms in.
    /// </summary>
    public List<(Vector2 Spot, Point Screen)> ScaleHandles()
    {
        if (model.Handles != SceneHandles.Scale || HandleFrame() is not { } frame)
            return [];
        var (width, height) = ((frame[1] - frame[0]).Length, (frame[3] - frame[0]).Length);
        if (Math.Min(width, height) < ScaleHandleRoom)
            return [];
        return ScaleSpots
            .Where(spot => (spot.X == 0 ? width : spot.Y == 0 ? height : double.MaxValue) >= EdgeHandleRoom)
            .Select(spot => (spot, FramePoint(frame, spot)))
            .ToList();
    }

    /// <summary>The scale handle under a point (the nearest, where they crowd), if any.</summary>
    public Vector2? ScaleHandleAt(Point screen) =>
        ScaleHandles().Where(h => (screen - h.Screen).Length <= HandleSquare + 4)
            .OrderBy(h => (screen - h.Screen).Length)
            .Select(h => (Vector2?)h.Spot)
            .FirstOrDefault();

    /// <summary>
    /// The resize handles on the primary selection's sprite or colliders (see <see cref="SceneViewModel.ResizableShapes"/>),
    /// in screen space, with their spots on each shape. As with the scale handles, a shape drawn too small to hold them
    /// clear of its middle gets none until the view zooms in, and edges too short go without a middle one.
    /// </summary>
    public List<(Component Shape, Vector2 Spot, Point Screen)> ResizeHandles()
    {
        if (model.Handles == SceneHandles.Scale || HandleFrame() is null)
            return [];
        var handles = new List<(Component, Vector2, Point)>();
        foreach (var shape in model.ResizableShapes())
        {
            if (SceneViewModel.ShapeFrame.Of(shape) is not { } frame)
                continue;
            Point At(Vector2 spot) => viewport.ToScreen(frame.ToWorld(frame.HandleAt(spot)));
            var (width, height) = ((At(new Vector2(1, 0)) - At(new Vector2(-1, 0))).Length, (At(new Vector2(0, 1)) - At(new Vector2(0, -1))).Length);
            if (Math.Min(width, height) < ScaleHandleRoom)
                continue;
            handles.AddRange(frame.Spots
                .Where(spot => frame.IsCircle || (spot.X == 0 ? width : spot.Y == 0 ? height : double.MaxValue) >= EdgeHandleRoom)
                .Select(spot => (shape, spot, At(spot))));
        }

        return handles;
    }

    /// <summary>The resize handle under a point (the nearest, where they crowd), if any.</summary>
    public (Component Shape, Vector2 Spot)? ResizeHandleAt(Point screen) =>
        ResizeHandles().Where(h => (screen - h.Screen).Length <= HandleSquare + 4)
            .OrderBy(h => (screen - h.Screen).Length)
            .Select(h => ((Component, Vector2)?)(h.Shape, h.Spot))
            .FirstOrDefault();

    /// <summary>
    /// The knobs on the primary selection's distances (see <see cref="SceneViewModel.Distances"/>), in Distance mode, in
    /// screen space, with the way out from their circle's middle (Y down). Each sits at its own angle, the first on the
    /// right and the next ones an eighth further round clockwise, so circles of the same size keep their knobs (and
    /// names) apart.
    /// </summary>
    public List<(SceneDistance Distance, Point Screen, Vector Outward)> DistanceHandles()
    {
        if (model.Handles != SceneHandles.Distance || HandleFrame() is null)
            return [];
        return model.Distances()
            .Select((distance, i) =>
            {
                var angle = -i * MathF.PI / 4;
                var way = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                return (distance, viewport.ToScreen(distance.Center + way * distance.Value), new Vector(way.X, -way.Y));
            })
            .ToList();
    }

    /// <summary>The distance knob under a point (the nearest, where they crowd), if any.</summary>
    public SceneDistance? DistanceHandleAt(Point screen) =>
        DistanceHandles().Where(h => (screen - h.Screen).Length <= HandleRadius + 4)
            .OrderBy(h => (screen - h.Screen).Length)
            .Select(h => h.Distance)
            .FirstOrDefault();

    /// <summary>
    /// The handles on the primary selection's path, in screen space: one on each of its points (see
    /// <see cref="SceneViewModel.PathPoints"/>), and for a mover or grid patrol a plus between each two points far enough
    /// apart (and on the way back to the start of a loop) and one past its last point, to drag out a new point. A grid
    /// patrol's pluses sit in cells: the one halfway between, and the next along its last stretch. The pluses hide while a
    /// point is dragged, and everything while painting.
    /// </summary>
    public List<PathHandle> PathHandles()
    {
        if (Document is not { } document || model.PaintTarget is not null)
            return [];
        var handles = model.PathPoints().Select(p => new PathHandle(p.Key, viewport.ToScreen(p.World), p, null, p.Index)).ToList();
        if (DraggingPathPoint || selection.Entity is not { } primary || primary.Scene != document.Scene)
            return handles;
        if (primary.GetComponent<Mover>() is { } mover)
            AddMoverPluses(handles, mover);
        if (primary.GetComponent<GridPatrol>() is { } patrol && primary.GetComponent<GridMovement>() is not null)
            AddGridPatrolPluses(handles, patrol);
        return handles;
    }

    private void AddMoverPluses(List<PathHandle> handles, Mover mover)
    {
        var path = SceneViewModel.PathOf(mover).Select(viewport.ToScreen).ToList();
        for (var i = 1; i < path.Count; i++)
        {
            if ((path[i] - path[i - 1]).Length >= PathGap)
                handles.Add(new PathHandle($"Mover.+{i}", path[i - 1] + (path[i] - path[i - 1]) / 2, null, mover, i));
        }

        // A loop adds on the way back to its start; anything else adds past its end, on along its last stretch.
        if (mover.Mode == MoverMode.Loop && path.Count > 2)
        {
            if ((path[0] - path[^1]).Length >= PathGap)
                handles.Add(new PathHandle($"Mover.+{path.Count}", path[^1] + (path[0] - path[^1]) / 2, null, mover, path.Count));
        }
        else
        {
            var way = path.Count > 1 ? path[^1] - path[^2] : new Vector(1, 0);
            way = way.Length < 1 ? new Vector(1, 0) : way / way.Length;
            handles.Add(new PathHandle($"Mover.+{path.Count}", path[^1] + way * PathGap, null, mover, path.Count));
        }
    }

    private void AddGridPatrolPluses(List<PathHandle> handles, GridPatrol patrol)
    {
        var cells = patrol.Path;
        Point At((int X, int Y) cell) => viewport.ToScreen(SceneViewModel.CellOf(patrol, cell.X, cell.Y)!.Value);

        // Between two points, in the cell halfway (when there's one between them and they're far enough apart on screen).
        void Between(int index, (int X, int Y) a, (int X, int Y) b)
        {
            var middle = ((a.X + b.X) >> 1, (a.Y + b.Y) >> 1);
            if (middle != a && middle != b && (At(b) - At(a)).Length >= PathGap)
                handles.Add(new PathHandle($"GridPatrol.+{index}", At(middle), null, patrol, index));
        }

        for (var i = 1; i < cells.Count; i++)
            Between(i, cells[i - 1], cells[i]);

        // A loop adds on the way back to its start; anything else adds past its end, a cell or more on along its last
        // stretch (straight along the way it mostly goes), enough to clear the last point's knob.
        if (patrol.Mode == MoverMode.Loop && cells.Count > 2)
        {
            Between(cells.Count, cells[^1], cells[0]);
            return;
        }

        var (last, before) = (cells[^1], cells.Count > 1 ? cells[^2] : (X: cells[^1].X - 1, Y: cells[^1].Y));
        var (dx, dy) = (last.X - before.X, last.Y - before.Y);
        var step = Math.Abs(dx) >= Math.Abs(dy) ? (X: dx >= 0 ? 1 : -1, Y: 0) : (X: 0, Y: Math.Sign(dy));
        var cellSize = (At((last.X + step.X, last.Y + step.Y)) - At(last)).Length;
        var cellsOut = cellSize >= PathGap || cellSize <= 0 ? 1 : (int)Math.Ceiling(PathGap / cellSize);
        handles.Add(new PathHandle($"GridPatrol.+{cells.Count}", At((last.X + step.X * cellsOut, last.Y + step.Y * cellsOut)), null, patrol, cells.Count));
    }

    /// <summary>The path handle under a point (the nearest, where they crowd), if any.</summary>
    public PathHandle? PathHandleAt(Point screen) =>
        PathHandles().Where(h => (screen - h.Screen).Length <= HandleRadius + 4)
            .OrderBy(h => (screen - h.Screen).Length)
            .FirstOrDefault();

    /// <summary>
    /// The arrows for the selection's links, in screen space: one from each entity to each it names (several fields
    /// naming the same one share it, their names joined, as "Switch Targets"), bowed to one side so links both ways
    /// between two entities stay apart, its tip a little short of the entity so its marker stays clear, and
    /// <c>Middle</c> where the fields are named: halfway along, or past the tip when the arrow is too short.
    /// </summary>
    public List<(Entity From, Entity To, string Fields, Point Start, Point Bend, Point Tip, Point Middle)> LinkArrows()
    {
        var arrows = new List<(Entity, Entity, string, Point, Point, Point, Point)>();
        foreach (var pair in model.Links().GroupBy(l => (l.From, l.To)))
        {
            var from = viewport.ToScreen(Flat(pair.Key.From.Transform.WorldPosition));
            var to = viewport.ToScreen(Flat(pair.Key.To.Transform.WorldPosition));
            var along = to - from;
            if (along.Length < 1)
                continue;
            var side = new Vector(-along.Y, along.X) / along.Length;
            var bend = from + along / 2 + side * Math.Min(along.Length * 0.15, 40);
            var into = to - bend;
            into.Normalize();
            var fields = string.Join(", ", pair.Select(l => $"{l.Reference.Component.GetType().Name} {l.Reference.Property.Name}"));

            // Too short to hold its label (a lever's own hint, a fighter's hitbox), the label goes past the tip instead,
            // off the selection.
            var label = measure(fields);
            var middle = from + along / 4 + (bend - from) / 2;
            if (along.Length < label.Width + 40)
            {
                var way = along / along.Length;
                middle = to + way * (Math.Abs(way.X) * (label.Width / 2 + 4) + Math.Abs(way.Y) * (label.Height / 2 + 1) + 8);
            }

            arrows.Add((pair.Key.From, pair.Key.To, fields, from, bend, to - into * 4, middle));
        }

        return arrows;
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
