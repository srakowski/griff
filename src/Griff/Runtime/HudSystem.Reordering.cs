using System.Numerics;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

// Reorderable layouts: a row pressed on and dragged along its list to a new place (a press that doesn't go far is a
// click, left to its Button), or lifted with the grab action and moved with the arrows; the order sent on as a message
// and kept in the save data.
internal sealed partial class HudSystem : IClaimsBack
{
    // How far, in world units, the mouse goes from where it was pressed before a press on a row drags it.
    private const float DragStart = 0.2f;

    // A dragged row near the end of a clipping layout scrolls it, within this many rows of the end, up to this many rows a
    // second.
    private const float EdgeRows = 0.75f;

    private const float EdgeRowsPerSecond = 8;

    // A press on a row: its layout, the row, where the mouse went down, and once it's being dragged, where the row's middle
    // is held from the mouse (both from the layout's middle).
    private sealed record RowPress(Layout Layout, Entity Row, Vector2 At)
    {
        public Vector2? Grip { get; set; }
    }

    [NotState("the player's hand, let go of on a rewind")]
    private RowPress? _rowPress;

    // The layout whose row is lifted with keys.
    [NotState("the player's hand, let go of on a rewind")]
    private Layout? _lifted;

    // Esc puts a held row back before it opens a pause menu.
    public bool ClaimsBack => _rowPress?.Grip is not null || _lifted is not null;

    // Whether the mouse is on a reorderable layout, or dragging a row: a press there isn't meant for the world.
    private bool MouseOnRows()
    {
        if (_rowPress is not null)
            return true;
        if (!Game.Input.IsMouseInView)
            return false;
        var top = Game.TopPanel;
        foreach (var layout in Game.Scene.ActiveNow<Layout>())
        {
            if (Works(layout, top) && layout.Reaches(Game.Input.MouseWorld))
                return true;
        }

        return false;
    }

    // Whether the player can work a layout's rows: it reorders, and is on the panel on top if one's open.
    private static bool Works(Layout layout, Panel? top) => layout.Reorderable && (top is null || PanelSystem.Inside(layout.Entity!, top));

    // Presses, drags and lets go of rows with the mouse (a finger too). Run with the menus, before buttons take their
    // clicks, so a row dragged away from a press doesn't click its Button, and Esc puts the row back rather than closing
    // its panel.
    internal void DragRows(Panel? top)
    {
        var input = Game.Input;
        var mouse = input.MouseWorld;
        if (_rowPress is { } press)
        {
            var (layout, row) = (press.Layout, press.Row);
            if (layout.Entity is not { IsActive: true } entity || entity.Scene != Game.Scene || !layout.Reorderable || row.Parent != entity || !row.IsEnabled)
            {
                layout.LetGo();
                _rowPress = null;
            }
            else if (!input.IsDown(MouseButton.Left))
            {
                _rowPress = null;
                if (press.Grip is not null)
                    PutDown(layout, row, input.IsMouseInView && layout.Reaches(mouse));
            }
            else if (press.Grip is not null && (input.WasPressed(Key.Escape) || input.WasPressed(GamepadButton.B)))
            {
                PanelSystem.UseBack(Game.MachineInput);
                _rowPress = null;
                PutDown(layout, row, false);
            }
            else
            {
                Drag(press, layout, row, mouse);
            }

            return;
        }

        if (!input.WasPressed(MouseButton.Left) || !input.IsMouseInView || _lifted is not null)
            return;

        // The innermost reorderable layout under the mouse with a row there.
        Layout? found = null;
        Entity? under = null;
        foreach (var layout in Game.Scene.ActiveNow<Layout>())
        {
            if (!Works(layout, top) || layout.RowAt(mouse) is not { } row || (found is not null && !layout.Entity!.IsSelfOrDescendantOf(found.Entity!)))
                continue;
            (found, under) = (layout, row);
        }

        if (found is not null)
            _rowPress = new RowPress(found, under!, found.Local(mouse));
    }

    private void Drag(RowPress press, Layout layout, Entity row, Vector2 mouse)
    {
        var at = layout.Local(mouse);
        if (press.Grip is null)
        {
            if (Vector2.Distance(at, press.At) < DragStart)
                return;

            // Picked up: the press no longer clicks a button on it.
            press.Grip = layout.CenterOf(row) - press.At;
            layout.HeldRow = row;
            layout.HeldFrom = layout.HeldTo = Layout.IndexIn(layout.Rows, row);
            Unpress(row);
        }

        layout.HeldCenter = at + press.Grip;
        layout.HeldOutside = !Game.Input.IsMouseInView || !layout.Reaches(mouse);
        ScrollAtEdge(layout, at);
    }

    // Near the start or the end of a clipping layout, it scrolls that way, faster the nearer.
    private void ScrollAtEdge(Layout layout, Vector2 at)
    {
        if (!layout.Clip || layout.MostScroll <= 0)
            return;
        var area = Vector2.Abs(layout.Size);
        var edge = Math.Max(0.05f, layout.ScrollStep * EdgeRows);
        var (toStart, toEnd) = layout.Direction == LayoutDirection.Horizontal
            ? (at.X + area.X / 2, area.X / 2 - at.X)
            : (area.Y / 2 - at.Y, at.Y + area.Y / 2);
        var step = layout.ScrollStep * EdgeRowsPerSecond * Game.Time.UnscaledDelta;
        if (toStart < edge)
            layout.Scroll -= step * Math.Min(1, 1 - toStart / edge);
        else if (toEnd < edge)
            layout.Scroll += step * Math.Min(1, 1 - toEnd / edge);
        layout.Scroll = Math.Clamp(layout.Scroll, 0, layout.MostScroll);
    }

    // The buttons on a row forget the press that picked it up, so letting go doesn't click them.
    private static void Unpress(Entity row)
    {
        if (row.GetComponent<Button>() is { } button)
            button.IsPressed = false;
        foreach (var child in row.Children)
            Unpress(child);
    }

    // Lets go of the held row: where it would land if `there`, else back where it was.
    private static void PutDown(Layout layout, Entity row, bool there)
    {
        var (from, to) = (layout.HeldFrom, layout.HeldTo);
        layout.LetGo();
        if (there && to != from)
            layout.Move(row, to);
    }

    // With keys: the grab action lifts the picked button's row and puts it down again (as do Enter and A), the arrows move
    // it, Esc and B put it back. Run with the menus before they move the pick; true if it used the direction.
    internal bool MoveRowsByKeys(Panel? top, Component? focus, (int X, int Y) direction)
    {
        var input = Game.Input;
        var submit = input.WasPressed(Key.Enter) || input.WasPressed(Key.Space) || input.WasPressed(GamepadButton.A);
        if (_lifted is { } layout)
        {
            var row = layout.HeldRow;
            if (row is null || layout.Entity is not { IsActive: true } entity || entity.Scene != Game.Scene || !layout.Reorderable ||
                focus is not Button { Entity: { } picked } || !picked.IsSelfOrDescendantOf(row))
            {
                layout.LetGo();
                _lifted = null;
                return false;
            }

            var grab = layout.GrabAction?.Trim();
            if (input.WasPressed(Key.Escape) || input.WasPressed(GamepadButton.B))
            {
                PanelSystem.UseBack(Game.MachineInput);
                _lifted = null;
                PutDown(layout, row, false);
                return true;
            }

            if (submit || (!string.IsNullOrEmpty(grab) && input.WasPressed(grab)))
            {
                Game.MachineInput.Use(Key.Enter);
                Game.MachineInput.Use(Key.Space);
                Game.MachineInput.Use(GamepadButton.A);
                if (!string.IsNullOrEmpty(grab))
                    Game.MachineInput.Use(grab);
                _lifted = null;
                PutDown(layout, row, true);
                return true;
            }

            if (direction == default)
                return false;

            // Up and left go towards the start; a grid's rows are its columns apart.
            var step = layout.Direction switch
            {
                LayoutDirection.Vertical => -direction.Y,
                LayoutDirection.Horizontal => direction.X,
                _ => direction.X - direction.Y * Math.Max(1, layout.Columns),
            };
            layout.HeldTo = Math.Clamp(layout.HeldTo + step, 0, Math.Max(0, layout.Rows.Count - 1));
            return true;
        }

        if (focus is not Button { Entity: { } focused } || submit || _rowPress is not null)
            return false;
        foreach (var candidate in Game.Scene.ActiveNow<Layout>())
        {
            var grab = candidate.GrabAction?.Trim();
            if (!Works(candidate, top) || string.IsNullOrEmpty(grab) || !input.WasPressed(grab) || RowOf(candidate, focused) is not { } row)
                continue;
            Game.MachineInput.Use(grab);
            candidate.HeldRow = row;
            candidate.HeldFrom = candidate.HeldTo = Layout.IndexIn(candidate.Rows, row);
            _lifted = candidate;
            return false;
        }

        return false;
    }

    // The row of the layout that `entity` is (or is inside), or null.
    private static Entity? RowOf(Layout layout, Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.Parent == layout.Entity)
                return e.IsEnabled ? e : null;
        }

        return null;
    }

    // Before the layouts line up their rows: rows put back in their saved order, and those moved since last time sent on
    // as messages and their new order saved.
    private void KeepOrders()
    {
        foreach (var layout in Game.Scene.ActiveNow<Layout>())
        {
            if (!layout.Reorderable)
                continue;
            var key = layout.SaveOrderAs?.Trim();
            if (layout.Moved.Count > 0)
            {
                foreach (var row in layout.Moved)
                {
                    if (!string.IsNullOrWhiteSpace(layout.ReorderMessage))
                        Game.Send(layout.ReorderMessage.Trim(), row, from: layout.Entity);
                }

                layout.Moved.Clear();
                if (!string.IsNullOrEmpty(key))
                    Game.SaveData.Set(key, layout.OrderText());
            }

            if (string.IsNullOrEmpty(key) || layout.HeldRow is not null)
                continue;
            var saved = Game.SaveData.GetString(key);
            var stamp = (saved, layout.ChildrenStamp());
            if (layout.OrderedFor == stamp)
                continue;
            layout.PutInOrder(saved);
            layout.OrderedFor = (saved, layout.ChildrenStamp());
        }
    }
}
