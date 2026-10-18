using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Line Tools: a line dragged on the grid from the cell pressed on to the one under the mouse, its ghost drawn over the
// world, laid on its conveyor and tiles when it's let go. They have the mouse before Selection Controls, which leave it to
// one in hand.
internal sealed partial class SelectionSystem
{
    internal void RunLineTools()
    {
        var all = Game.Scene.ActiveNow<LineTool>();
        if (all.Count == 0)
            return;
        foreach (var tool in all)
        {
            if (TakesInput(tool))
                Run(tool);
            else
                (tool.Drag, tool.Hover, tool.Cells) = (null, null, []);
        }
    }

    // A line tool in hand has the mouse: Selection Controls neither select nor command.
    private bool LineToolInHand()
    {
        foreach (var tool in Game.Scene.ActiveNow<LineTool>())
        {
            if (tool.Controllable)
                return true;
        }

        return false;
    }

    private bool TakesInput(LineTool tool) =>
        tool.Controllable && !Game.PausedThisFrame && !Game.LocalInputBlocked && !tool.Entity!.IsPaused;

    private void Run(LineTool tool)
    {
        var input = Game.Input;
        var camera = CameraOf(tool.Entity!);
        var inView = camera is null ? input.IsMouseInView : Game.MouseIn(camera, out _);
        var mouse = camera is null ? input.MouseWorld : MouseIn(camera);
        var grid = tool.Grid;
        tool.Hover = inView && (tool.Drag is not null || !OnUi(camera, mouse, forCommand: false)) ? grid.CellAt(mouse) : null;

        if (Named(tool.RotateAction) is { } rotate && input.WasPressed(rotate))
            tool.Rotate();
        if (tool.Drag is not null && Named(tool.CancelAction) is { } cancel && input.WasPressed(cancel))
        {
            tool.Cancel();
            Game.MachineInput.Use(cancel);
        }

        var action = Named(tool.Action);
        if (tool.Drag is null && action is not null && input.WasPressed(action) && tool.Hover is { } start)
            tool.Drag = new LineDrag(start, tool.FirstLeg, tool.Corners);

        var before = tool.Cells;
        if (tool.Drag is { } drag)
        {
            if (tool.Hover is { } cell)
                drag.MoveTo(cell);
            tool.Cells = tool.Check(drag.Cells(LineDrag.StepOf(tool.Way)));
            if (!before.SequenceEqual(tool.Cells))
                tool.RaiseDragging();

            // Let go: the cells that fit are laid, and the game told.
            if (action is null || !input.IsDown(action))
            {
                tool.Drag = null;
                var laid = tool.Cells.Where(c => c.Fits).ToList();
                if (laid.Count > 0)
                {
                    tool.Lay(laid);
                    tool.RaisePlaced(laid);
                    if (!string.IsNullOrWhiteSpace(tool.Message))
                        Game.Send(tool.Message.Trim(), laid.Count, from: tool.Entity);
                }

                tool.Cells = tool.Hover is { } here ? tool.Line(here, here) : [];
            }
        }
        else
            tool.Cells = tool.Hover is { } here ? tool.Line(here, here) : [];
    }

    private static string? Named(string? action) => string.IsNullOrWhiteSpace(action) ? null : action.Trim();

    // The camera a tool works through: its own entity if it's one, else the scene's.
    private Entity? CameraOf(Entity entity) => entity.GetComponent<Camera>() is not null ? entity : Views.FindCamera(Game.Scene);

    // Each cell of the line a tint, where it fits or where it can't, with an arrow the way the line runs.
    private void DrawLineTools(FrameDrawing frame)
    {
        foreach (var tool in Game.Scene.Active<LineTool>())
        {
            var camera = CameraOf(tool.Entity!);
            if (tool.Cells.Count == 0 || (frame.Camera != camera && !(camera is null && frame.Viewport is null)))
                continue;
            var grid = tool.Grid;
            var (across, down) = (grid.InWorld(Vector2.UnitX), grid.InWorld(Vector2.UnitY));
            var size = new Vector2(across.Length(), down.Length());
            var side = MathF.Min(size.X, size.Y);
            var line = MathF.Max(frame.LineWidth * 1.5f, side * 0.06f);
            var draws = new List<SpriteDraw>();
            foreach (var cell in tool.Cells)
            {
                var color = cell.Fits ? tool.GhostColor : tool.BlockedColor;
                var center = grid.Center(cell.Cell);
                draws.Add(FrameDrawing.Quad(center, size * 0.92f, color));
                var way = grid.InWorld(new Vector2(cell.WayX, cell.WayY));
                if (way.LengthSquared() < 1e-8f)
                    continue;
                way = Vector2.Normalize(way);
                var side90 = new Vector2(-way.Y, way.X);
                var tip = center + way * side * 0.25f;
                var arrow = color with { A = MathF.Min(1, color.A * 2) };
                draws.Add(FrameDrawing.Line(tip, tip - (way + side90) * side * 0.2f, line, arrow));
                draws.Add(FrameDrawing.Line(tip, tip - (way - side90) * side * 0.2f, line, arrow));
            }

            frame.AddOverWorld(draws);
        }
    }
}
