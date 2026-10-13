using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Rendering;

// A tactics game's extras: the cells the picked (or hovered) unit can walk to and hit, drawn on the ground just over its
// map; the way it would go, the cursor and health bars over the units; and units that have had their go, dimmed.
public static partial class SceneRenderer
{
    private static List<SpriteDraw> WithTactics(Game game, List<SpriteDraw> sprites, float cameraSize)
    {
        var view = game.Tactics;
        if (view.Controls is not { } controls)
            return sprites;
        var units = game.Scene.Active<TacticsUnit>();
        if (units.Count == 0)
            return sprites;

        var grid = view.Grid;
        var cell = Vector2.Distance(grid.Center((0, 0)), grid.Center((1, 0)));
        var done = units.Where(u => u.IsDone).Select(u => u.Entity!).ToList();
        var list = sprites.Select(d => done.Count > 0 && done.Any(d.Entity.IsSelfOrDescendantOf) ? d with { Tint = Times(d.Tint, controls.DoneTint) } : d).ToList();
        bool OfUnit(SpriteDraw d) => units.Any(u => d.Entity.IsSelfOrDescendantOf(u.Entity!));

        // On the ground: just over the map (its layers too), else under the units.
        var map = grid.Map?.Entity;
        var ground = map is null ? -1 : list.FindLastIndex(d => d.Entity.IsSelfOrDescendantOf(map));
        if (ground < 0)
            ground = Math.Max(-1, list.FindIndex(OfUnit) - 1);
        var faint = view.Faint ? 0.5f : 1;
        var lit = new List<SpriteDraw>();
        foreach (var c in view.Moves)
            lit.Add(Quad(grid.Center(c), new Vector2(cell * 0.94f), Faded(controls.MoveColor, faint)));
        foreach (var c in view.Attacks)
            lit.Add(Quad(grid.Center(c), new Vector2(cell * 0.94f), Faded(controls.AttackColor, faint)));
        list.InsertRange(ground + 1, lit);

        // Over the units: health bars, the way the picked unit would go, and the cursor.
        var over = new List<SpriteDraw>();
        if (controls.HealthBars)
        {
            foreach (var unit in units)
            {
                if (unit.Entity!.GetComponent<Health>() is not { } health || health.IsDead)
                    continue;
                var fraction = health.Max > 0 ? Math.Clamp(health.Current / health.Max, 0, 1) : 0;
                var at = Flat(unit.Entity.Transform.WorldPosition) - new Vector2(0, cell * 0.42f);
                over.AddRange(Meter(at, cell * 0.7f, cell * 0.08f, fraction, HealthColor(fraction)));
            }
        }

        var thickness = cameraSize / 180;
        if (view.Path.Count > 1)
        {
            for (var i = 1; i < view.Path.Count; i++)
                over.Add(LineDraw(grid.Center(view.Path[i - 1]), grid.Center(view.Path[i]), cell * 0.1f, controls.PathColor));
            over.Add(Quad(grid.Center(view.Path[^1]), new Vector2(cell * 0.24f), controls.PathColor));
        }

        if (view.Cursor is { } cursor && controls.CursorColor.A > 0)
            over.AddRange(Outline(grid.Center(cursor), new Vector2(cell * 0.96f), thickness * 1.6f, controls.CursorColor));

        var top = list.FindLastIndex(OfUnit);
        list.InsertRange(top < 0 ? ground + 1 + lit.Count : top + 1, over);
        return list;
    }

    private static Color Times(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);

    private static Color Faded(Color color, float amount) => color with { A = color.A * amount };
}
