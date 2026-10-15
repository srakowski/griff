using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using static Griff.Rendering.OverlayShapes;

namespace Griff.Modules.Tactics;

// A tactics game's extras: the cells the picked (or hovered) unit can walk to and hit, drawn on the ground just over its
// map; the way it would go, the cursor and health bars over the units; and units that have had their go, dimmed.
internal static class TacticsDrawing
{
    /// <summary>Draws a tactics game's extras into <paramref name="frame"/>, among the world's draws (not in first person).</summary>
    public static void Draw(TacticsView view, FrameDrawing frame)
    {
        var game = frame.Game;
        if (frame.SeenThrough is not null)
            return;
        if (view.Controls is not { } controls)
            return;
        var units = game.Scene.ActiveNow<TacticsUnit>();
        if (units.Count > 0)
            Draw(view, frame, controls, units);
    }

    // Apart from the checks above, so a game with no tactics in it doesn't pay for what this captures (its lambdas'
    // closure is made as the method starts, before any return).
    private static void Draw(TacticsView view, FrameDrawing frame, TacticsControls controls, ComponentList<TacticsUnit> units)
    {
        var grid = view.Grid;
        var cell = Vector2.Distance(grid.Center((0, 0)), grid.Center((1, 0)));
        var done = units.Where(u => u.IsDone).Select(u => u.Entity!).ToList();
        var list = frame.Sprites;
        if (done.Count > 0)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (done.Any(list[i].Entity.IsSelfOrDescendantOf))
                    list[i] = list[i] with { Tint = Times(list[i].Tint, controls.DoneTint) };
            }
        }

        bool OfUnit(SpriteDraw d) => units.Any(u => d.Entity.IsSelfOrDescendantOf(u.Entity!));

        // On the ground: just over the map (its layers too), else under the units.
        var map = grid.Map?.Entity;
        var ground = map is null ? -1 : list.FindLastIndex(d => d.Entity.IsSelfOrDescendantOf(map));
        if (ground < 0)
            ground = Math.Max(-1, list.FindIndex(OfUnit) - 1);
        var faint = view.Faint ? 0.5f : 1;
        // Each cell lit in its own shape: a square, or an isometric map's diamond or a hex map's hexagon.
        var shaped = grid.Map is { Shape: not TileShape.Square };
        var lit = new List<SpriteDraw>();
        void Light((int X, int Y) c, Color color)
        {
            if (!shaped)
                lit.Add(Quad(grid.Center(c), new Vector2(cell * 0.94f), color));
            else
                lit.AddRange(grid.Shape(c, 0.94f).Select(m => new SpriteDraw(m, null, color, SceneRenderer.ScreenEntity)));
        }

        foreach (var c in view.Moves)
            Light(c, Faded(controls.MoveColor, faint));
        foreach (var c in view.Attacks)
            Light(c, Faded(controls.AttackColor, faint));
        list.InsertRange(ground + 1, lit);

        // Over the units: health bars, the way the picked unit would go, and the cursor.
        var over = new List<SpriteDraw>();
        if (controls.HealthBars)
        {
            foreach (var unit in units)
            {
                if (unit.Entity!.GetComponent<Health>() is not { } health || health.IsDead)
                    continue;
                // Filling and emptying between steps.
                var fraction = health.Max > 0 ? Math.Clamp(health.CurrentDrawn(frame.BlendOf(unit.Entity)) / health.Max, 0, 1) : 0;
                var at = Flat(unit.Entity.Transform.WorldPosition) - new Vector2(0, cell * 0.42f);
                over.AddRange(Meter(at, cell * 0.7f, cell * 0.08f, fraction, HealthColor(fraction)));
            }
        }

        var thickness = frame.LineWidth;
        if (view.Path.Count > 1)
        {
            for (var i = 1; i < view.Path.Count; i++)
                over.Add(LineDraw(grid.Center(view.Path[i - 1]), grid.Center(view.Path[i]), cell * 0.1f, controls.PathColor));
            over.Add(Quad(grid.Center(view.Path[^1]), new Vector2(cell * 0.24f), controls.PathColor));
        }

        if (view.Cursor is { } cursor && controls.CursorColor.A > 0)
        {
            if (!shaped)
            {
                over.AddRange(Outline(grid.Center(cursor), new Vector2(cell * 0.96f), thickness * 1.6f, controls.CursorColor));
            }
            else
            {
                // Round a diamond's or a hexagon's edge.
                var corners = grid.Corners(cursor, 0.96f);
                for (var i = 0; i < corners.Length; i++)
                    over.Add(LineDraw(corners[i], corners[(i + 1) % corners.Length], thickness * 1.6f, controls.CursorColor));
            }
        }

        var top = list.FindLastIndex(OfUnit);
        list.InsertRange(top < 0 ? ground + 1 + lit.Count : top + 1, over);
    }

    private static Color Times(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);

    private static Color Faded(Color color, float amount) => color with { A = color.A * amount };
}
