#if DEBUG
using System.Windows;
using Griff.Editor.Diagnostics.Demo;
using Griff.Modules.Fighting;

namespace Griff.Editor.Genres.Fighting;

/// <summary>
/// The Fighting genre's demo commands: the Move List (moves, movepick, moveset, expectmove, and moves: parts) and the Clip
/// Editor's boxes (clipbox, expectboxes, and its clips: parts). See demos/README.md.
/// </summary>
internal sealed class FightingDemo : DemoGenre
{
    public override async Task<bool> Play(DemoFile demo, string command, string a)
    {
        var ui = demo.Ui;
        switch (command)
        {
            case "moves":
                await DemoUi.WaitForMoveList();
                return true;
            case "movepick":
                // "movepick: Palm" clicks the Move List's move called Palm.
                await ui.MovesPick(a);
                return true;
            case "moveset":
                // "moveset: motion = 236" picks from a list; "moveset: startup = 8" types into a box.
                var (moveField, moveValue) = DemoFile.Split2(a, '=');
                await ui.MovesSet(moveField, moveValue);
                return true;
            case "expectmove":
                // What the Move List says about the move picked (its arrows, what the motion's called, its frame data, what it
                // leaves on hit and block, its problems) has this in it.
                await DemoUi.WaitForMoveList();
                string MoveSays() => DemoUi.MoveList?.Model is { } moves
                    ? string.Join(" | ", moves.InputArrows, moves.InputDescription, moves.FrameSummary, moves.Advantage, moves.Problems)
                    : "";
                if (!await demo.Until(() => MoveSays().Contains(a, StringComparison.OrdinalIgnoreCase), 3000))
                    throw new InvalidOperationException($"The Move List says \"{MoveSays()}\", not \"{a}\".");
                return true;
            case "clipbox":
                // "clipbox: 0.3, 0.1 > 0.7, 0.4" drags over the Clip Editor's frame between two points in world units.
                var (clipBoxFrom, clipBoxTo) = DemoFile.Split2(a, '>');
                var (boxFrom, boxTo) = (DemoFile.Numbers(clipBoxFrom, 2), DemoFile.Numbers(clipBoxTo, 2));
                await DragClipBox(ui, new System.Numerics.Vector2((float)boxFrom[0], (float)boxFrom[1]), new System.Numerics.Vector2((float)boxTo[0], (float)boxTo[1]));
                return true;
            case "expectboxes":
                // The Clip Editor's line on the frame's boxes, or one of their labels, has this in it.
                var boxes = DemoUi.ClipBoxes(await DemoUi.WaitForClipEditor());
                if (!await demo.Until(() => boxes.BoxesSummary.Contains(a, StringComparison.OrdinalIgnoreCase) ||
                        boxes.Boxes.Any(b => b.Label.Contains(a, StringComparison.OrdinalIgnoreCase)), 3000))
                    throw new InvalidOperationException($"The Clip Editor's boxes are \"{boxes.BoxesSummary}\" ({string.Join("; ", boxes.Boxes.Select(b => b.Label))}), not \"{a}\".");
                return true;
            default:
                return false;
        }
    }

    public override FrameworkElement? Part(string kind, string name) => kind switch
    {
        "moves" => DemoUi.MovesPart(name),
        "clips" => DemoUi.ClipBoxesPart(name),
        _ => null,
    };

    /// <summary>
    /// Drags over the Clip Editor's frame with the real mouse, between two points in world units (as boxes are placed, from
    /// the entity's middle): with the Hitbox or Hurtbox tool it draws one from corner to corner, with Select it moves the
    /// box under the first point (or resizes the picked one by its corner there).
    /// </summary>
    private static async Task DragClipBox(DemoUi ui, System.Numerics.Vector2 from, System.Numerics.Vector2 to)
    {
        var window = await DemoUi.WaitForClipEditor();
        var boxes = DemoUi.ClipBoxes(window);
        if (window.ScreenPointOf(from) is not { } start || window.ScreenPointOf(to) is not { } end)
            throw new InvalidOperationException("The Clip Editor shows no frame to draw boxes on.");
        var before = boxes.Hitboxes?.Boxes.ToList() ?? [];
        await ui.Input.Drag(start, [end], 500);
        await DemoUi.Settle(120);

        // A drag that missed (the window lost the mouse): do it the same way through the model.
        if ((boxes.Hitboxes?.Boxes ?? []).SequenceEqual(before))
        {
            var (min, max) = (System.Numerics.Vector2.Min(from, to), System.Numerics.Vector2.Max(from, to));
            if (boxes.Tool != BoxTool.Select)
            {
                var kind = boxes.Tool == BoxTool.Hitbox ? FrameBoxKind.Hit : FrameBoxKind.Hurt;
                if (boxes.DrawCircles)
                    boxes.AddCircle(kind, (min.X + max.X) / 2, (min.Y + max.Y) / 2, MathF.Max(max.X - min.X, max.Y - min.Y) / 2);
                else
                    boxes.AddBox(kind, (min.X + max.X) / 2, (min.Y + max.Y) / 2, max.X - min.X, max.Y - min.Y);
            }
            else if (boxes.Boxes.LastOrDefault(b => Contains(b.Box, from)) is { } row)
                boxes.MoveBox(row.Index, row.Box.Center.X + to.X - from.X, row.Box.Center.Y + to.Y - from.Y);
            ui.Log?.Invoke($"FORCED clipbox {from.X},{from.Y} > {to.X},{to.Y}");
            await DemoUi.Settle();
        }

        static bool Contains(FrameBox box, System.Numerics.Vector2 at) => box.IsCircle
            ? System.Numerics.Vector2.Distance(at, box.Center) <= box.Radius
            : MathF.Abs(at.X - box.Center.X) <= box.Size.X / 2 && MathF.Abs(at.Y - box.Center.Y) <= box.Size.Y / 2;
    }
}
#endif
