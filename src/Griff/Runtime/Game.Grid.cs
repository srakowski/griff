using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Players that step tile by tile on a tilemap's grid, blocked by walls.
public sealed partial class Game
{
    // How far the stick has to lean before it counts as a way.
    private const float GridDeadZone = 0.4f;

    // The cells a grid mover steps between: a tilemap's (its transform and tile size), or with none, world units centered
    // on whole numbers.
    private readonly record struct Grid(Tilemap? Map, Matrix4x4 World, Matrix4x4 ToLocal)
    {
        public (int X, int Y) CellAt(Vector2 world)
        {
            if (Map is null)
                return ((int)MathF.Round(world.X), -(int)MathF.Round(world.Y));
            return Map.CellAt(Vector2.Transform(world, ToLocal));
        }

        public Vector2 Center((int X, int Y) cell) =>
            Map is null ? new Vector2(cell.X, -cell.Y) : Vector2.Transform(Map.CellCenter(cell.X, cell.Y), World);

        // On no map, a step up is a row up; on a map, row numbers grow downward.
        public static (int X, int Y) Offset((int X, int Y) way) => (way.X, -way.Y);
    }

    private void RunGrid(GridMovement m)
    {
        var entity = m.Entity!;
        if (entity.GetComponent<Body>() is { } body)
        {
            // It moves itself: physics only reports what it touches.
            body.Velocity = Vector2.Zero;
            body.FloatThisStep = true;
        }

        var grid = GridOf(m);
        var here = Flat(entity.Transform.WorldPosition);
        if (!m.Started)
        {
            // Onto the middle of the cell it's placed in (or that a rewind left it partway into).
            m.Started = true;
            m.Stepping = false;
            m.Cell = grid.CellAt(here);
            MoveTo(entity, grid.Center(m.Cell));
        }

        var way = m.Controllable ? GridWay(m, InputOf(entity).Move) : (X: 0, Y: 0);
        var pressed = way != (0, 0) && way != m.Held;
        m.HeldFor = way == m.Held ? m.HeldFor + Time.Delta : 0;
        m.Held = way;
        if (way != m.BumpedWay)
            m.BumpedWay = (0, 0);
        if (way != (0, 0))
            m.Facing = new Vector2(way.X, way.Y);
        if (pressed && m.Stepping)
            m.Buffered = way;

        var stepTime = Math.Max(0.02f, m.StepTime);
        var spare = Time.Delta;
        if (m.Stepping)
        {
            m.Progress += Time.Delta / stepTime;
            spare = 0;
            if (m.Progress >= 1)
            {
                spare = (m.Progress - 1) * stepTime;
                m.Stepping = false;
                MoveTo(entity, m.To);
            }
            else
            {
                MoveTo(entity, Vector2.Lerp(m.From, m.To, m.Progress));
            }
        }

        // Still, it's in the cell it stands in, wherever a script may have put it.
        if (!m.Stepping)
            m.Cell = grid.CellAt(Flat(entity.Transform.WorldPosition));

        if (!m.Stepping)
        {
            // A script's step first, then a way pressed during the last step, then the way held.
            var next = m.Queued ?? m.Buffered;
            if (next is null && way != (0, 0) && (pressed || (m.Repeat && m.HeldFor >= m.RepeatDelay)))
                next = way;
            m.Queued = null;
            m.Buffered = null;
            if (next is { } step && step != (0, 0))
                TryGridStep(m, grid, step, Math.Min(spare, stepTime) / stepTime);
        }

        if (m.FlipSprite && MathF.Abs(m.Facing.X) > 0.1f)
            Face(entity, MathF.Sign(m.Facing.X));

        if (entity.GetComponent<Animation>() is not { } animation || IsPlayingAttack(entity, animation))
            return;
        var facingUpOrDown = MathF.Abs(m.Facing.Y) > MathF.Abs(m.Facing.X);
        var suffix = facingUpOrDown ? (m.Facing.Y > 0 ? "Up" : "Down") : "";
        if (m.Stepping)
            PlayFirst(animation, "Walk" + suffix, "Run" + suffix, "Walk", "Run");
        else
            PlayFirst(animation, "Idle" + suffix, "Idle");
    }

    // Starts a step if the cell that way is free; otherwise turns to face it and bumps, once a press.
    private void TryGridStep(GridMovement m, Grid grid, (int X, int Y) way, float progress)
    {
        var entity = m.Entity!;
        var direction = new Vector2(way.X, way.Y);
        m.Facing = direction;
        var offset = Grid.Offset(way);
        var target = (m.Cell.X + offset.X, m.Cell.Y + offset.Y);
        var free = CanEnter(m, grid, target);
        if (free && offset.X != 0 && offset.Y != 0)
            free = CanEnter(m, grid, (m.Cell.X + offset.X, m.Cell.Y)) && CanEnter(m, grid, (m.Cell.X, m.Cell.Y + offset.Y));
        if (!free)
        {
            if (m.BumpedWay != way)
            {
                m.BumpedWay = way;
                PlayIfSet(m.BumpSound);
                m.RaiseBumped(direction);
            }

            return;
        }

        m.From = grid.Center(m.Cell);
        m.To = grid.Center(target);
        m.Cell = target;
        m.Progress = progress;
        m.Stepping = true;
        MoveTo(entity, Vector2.Lerp(m.From, m.To, m.Progress));
        PlayIfSet(m.StepSound);
        m.RaiseStepped(direction);
    }

    // The way pressed as a step: four-way, the axis pressed most recently winning, unless it steps diagonally.
    private static (int X, int Y) GridWay(GridMovement m, Vector2 move)
    {
        var x = MathF.Abs(move.X) > GridDeadZone ? Math.Sign(move.X) : 0;
        var y = MathF.Abs(move.Y) > GridDeadZone ? Math.Sign(move.Y) : 0;
        if (x != 0 && m.LastRaw.X == 0)
            m.PreferVertical = false;
        if (y != 0 && m.LastRaw.Y == 0)
            m.PreferVertical = true;
        m.LastRaw = (x, y);
        if (m.Diagonal || x == 0 || y == 0)
            return (x, y);
        return m.PreferVertical ? (0, y) : (x, 0);
    }

    private Grid GridOf(GridMovement m)
    {
        var map = string.IsNullOrWhiteSpace(m.Map)
            ? Showing<Tilemap>().FirstOrDefault(t => t.LayerOf is null)
            : Scene.Entities.FirstOrDefault(e => e.Name == m.Map.Trim())?.GetComponent<Tilemap>()?.BaseMap;
        if (map?.Entity is null)
            return new Grid(null, Matrix4x4.Identity, Matrix4x4.Identity);
        var world = map.Entity.Transform.WorldMatrix;
        return Matrix4x4.Invert(world, out var toLocal) ? new Grid(map, world, toLocal) : new Grid(null, Matrix4x4.Identity, Matrix4x4.Identity);
    }

    // Free if it's on the map (when it has to stay on), no solid tile is there on any tilemap, no solid collider covers its
    // middle and no other grid mover is in it or heading into it. Layers either side passes through don't count.
    private bool CanEnter(GridMovement m, Grid grid, (int X, int Y) cell)
    {
        var entity = m.Entity!;
        if (m.StayOnMap && grid.Map is { } map)
        {
            var layers = map.Layers();
            if (cell.X < 0 || cell.Y < 0 || cell.X >= layers.Max(l => l.Width) || cell.Y >= layers.Max(l => l.Height))
                return false;
        }

        var collider = entity.GetComponent<Collider>();
        var layer = collider?.Layer;
        var ignores = CollisionLayers.Parse(collider?.IgnoreLayers);
        bool PassesThrough(string? otherLayer, string? otherIgnores)
        {
            var theirs = CollisionLayers.Parse(otherIgnores);
            return (ignores.Length > 0 && CollisionLayers.Contains(ignores, otherLayer)) ||
                   (theirs.Length > 0 && CollisionLayers.Contains(theirs, layer));
        }

        var point = grid.Center(cell);
        foreach (var other in Scene.Entities.Where(SceneRenderer.IsActive))
        {
            if (other.IsSelfOrDescendantOf(entity))
                continue;

            // Another grid mover holds the cell it's in, or the one it's heading into.
            if (other.GetComponent<GridMovement>() is { } mover)
            {
                if (other.GetComponent<Collider>() is { IsTrigger: false } theirs && !PassesThrough(theirs.Layer, theirs.IgnoreLayers) &&
                    (mover.Started ? mover.Cell : grid.CellAt(Flat(other.Transform.WorldPosition))) == cell)
                    return false;
                continue;
            }

            foreach (var component in other.Components)
            {
                switch (component)
                {
                    case Tilemap { Solid: true } tilemap when !PassesThrough(tilemap.Layer, tilemap.IgnoreLayers):
                        if (Matrix4x4.Invert(other.Transform.WorldMatrix, out var toLocal))
                        {
                            var (x, y) = tilemap.CellAt(Vector2.Transform(point, toLocal));
                            if (tilemap.IsBlocked(x, y))
                                return false;
                        }

                        break;
                    case BoxCollider { IsTrigger: false, OneWay: false } box when !PassesThrough(box.Layer, box.IgnoreLayers):
                        if (Physics.BoundsOf(box).Contains(point))
                            return false;
                        break;
                    case CircleCollider { IsTrigger: false } circle when !PassesThrough(circle.Layer, circle.IgnoreLayers):
                        if (Physics.BoundsOf(circle).Contains(point))
                            return false;
                        break;
                }
            }
        }

        return true;
    }

    // Puts it at a world position, whatever its parent.
    private static void MoveTo(Entity entity, Vector2 world) => Shift(entity, world - Flat(entity.Transform.WorldPosition));
}
