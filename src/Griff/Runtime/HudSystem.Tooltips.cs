using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Tooltips: which one the mouse rests on, when it shows, what it says, and its box by the cursor.
internal sealed partial class HudSystem
{
    // An average letter is about this much of its height across (as TextWrap reckons), for the box around the text.
    private const float LetterWidth = 0.5f;

    // The room around a box's text, in lines of it.
    private const float BoxPadding = 0.4f;

    // The Text a tooltip is writing into now, and what it said before, put back once the tooltip stops showing.
    private Text? _writtenInto;
    private string? _before;

    protected override void SceneChanged() => (_writtenInto, _before) = (null, null);

    // The topmost tooltip under the mouse counts the time it's rested there, each fills in the output it shows, and the one
    // showing with a Text to write into writes into it.
    internal void RunTooltips()
    {
        var tips = Game.Showing<Tooltip>();
        if (tips.Count == 0 && _writtenInto is null)
            return;

        var hovered = HoveredTooltip(tips);
        // Moving straight from one showing to the next shows the next at once.
        var warm = false;
        foreach (var tip in tips)
        {
            ReadOutput(tip);
            if (tip == hovered)
                continue;
            warm |= tip.IsShowing;
            (tip.IsHovered, tip.HoveredFor) = (false, 0);
        }

        if (hovered is not null)
        {
            hovered.HoveredFor = !hovered.IsHovered ? (warm ? Math.Max(hovered.Delay, 0) : 0) : hovered.HoveredFor + Game.Time.UnscaledDelta;
            hovered.IsHovered = true;
        }

        WriteInto(hovered is { IsShowing: true } && !string.IsNullOrWhiteSpace(hovered.Into) ? hovered : null);
    }

    // Only the topmost under the mouse: what's on the screen (or a button the mouse is on) before the world, then the
    // highest draw order, then the latest in the hierarchy. While a panel is open, only those on it; one in the world
    // doesn't show under a button or a system's HUD.
    private Tooltip? HoveredTooltip(ComponentList<Tooltip> tips)
    {
        if (tips.Count == 0 || !Game.Input.IsMouseInView)
            return null;
        var mouse = Game.Input.MouseWorld;
        var camera = Views.FindCamera(Game.Scene);
        var top = Game.TopPanel is { Entity: { } open } panel && !Game.MinigameSystem.IsBehindMinigame(open) ? panel : null;
        var shown = Game.Minigame;
        Tooltip? best = null;
        var (bestOnUi, bestOrder) = (false, 0);
        foreach (var tip in tips)
        {
            var entity = tip.Entity!;
            if ((top is not null && !PanelSystem.Inside(entity, top)) || MinigameRoot.IsBehind(entity, shown) || !Covers(tip, mouse))
                continue;
            var onUi = Views.IsOnScreen(entity, camera) || entity.GetComponent<Button>() is { IsHovered: true };
            var order = Views.OrderOf(entity) ?? 0;
            if (best is not null && (bestOnUi && !onUi || (bestOnUi == onUi && order < bestOrder)))
                continue;
            (best, bestOnUi, bestOrder) = (tip, onUi, order);
        }

        return best is null || bestOnUi || !Game.MouseOnUi ? best : null;
    }

    private static bool Covers(Tooltip tip, Vector2 point)
    {
        var entity = tip.Entity!;
        if (tip.Area != Vector2.Zero)
            return MenuSystem.AreaOf(entity, tip.Area).Contains(point);
        if (entity.GetComponent<Button>() is { } button)
            return MenuSystem.AreaOf(button).Contains(point);
        return DragSystem.Covers(entity, point);
    }

    // As a Text showing an output does: made again only when the output (or what it's out of) changes.
    private static void ReadOutput(Tooltip tip)
    {
        if (string.IsNullOrWhiteSpace(tip.Output))
        {
            if (tip.ReadFrom is not null)
            {
                tip.ReadFrom = null;
                tip.SetReadout(null);
            }

            return;
        }

        var reader = tip.Reader;
        var found = reader.Find(tip.Entity!, tip.Track, tip.Output);
        var (number, name, outOf) = found ? (reader.Number, reader.Text, reader.OutOf) : (0f, "", 0f);
        if (tip.ReadFrom is { } from && ReferenceEquals(from.Template, tip.Value) && from.Number.Equals(number) && from.Name == name &&
            from.OutOf.Equals(outOf))
            return;
        tip.ReadFrom = (tip.Value, number, name, outOf);
        tip.SetReadout(Readout.Fill(tip.Value, number, name, outOf, found ? reader.Output!.ValueType : typeof(string)));
    }

    // The Text the showing tooltip names says what it says; the one written into before says what it said, once nothing
    // (or a tooltip writing elsewhere) shows.
    private void WriteInto(Tooltip? writing)
    {
        var into = writing is null ? null : EntityNames.Find(Game.Scene, writing.Into, new NameLookup(writing.Entity!))?.GetComponent<Text>();
        if (_writtenInto is not null && _writtenInto != into)
        {
            if (_writtenInto.Entity?.Scene == Game.Scene)
                _writtenInto.Value = _before;
            (_writtenInto, _before) = (null, null);
        }

        if (into is null)
            return;
        if (_writtenInto is null)
            (_writtenInto, _before) = (into, into.Value);
        if (into.Value != writing!.Shown)
            into.Value = writing.Shown;
    }

    // The box of the tooltip showing by the cursor, in the main view, over everything. It's pinned to the screen: drawn as
    // the screen's (as a system's HUD is) at the cursor, sized in world units of the view as a Text anchored to the screen.
    protected override void Draw(FrameDrawing frame)
    {
        if (frame.Viewport is not null || !Game.Input.IsMouseInView)
            return;
        foreach (var tip in Game.Showing<Tooltip>())
        {
            if (tip.IsShowing && string.IsNullOrWhiteSpace(tip.Into) && !string.IsNullOrEmpty(tip.Shown) && tip.Size > 0)
            {
                DrawBox(frame, tip, Game.Input.MouseWorld);
                return;
            }
        }
    }

    private static void DrawBox(FrameDrawing frame, Tooltip tip, Vector2 cursor)
    {
        var (lines, columns, rows) = LinesOf(tip);
        // Drawn on the screen as the HUD is, so with the camera's Hud Size it keeps its size as the camera zooms.
        var hud = frame.Camera?.GetComponent<Camera>()?.HudScale ?? 1;
        var size = tip.Size * hud;
        var offset = tip.Offset * hud;
        var pad = size * BoxPadding;
        var box = new Vector2(columns * size * LetterWidth + pad * 2, rows * size + pad * 2);
        var view = frame.View;

        // Its top-left corner by the cursor, on the other side of it at the screen's right or bottom edge, kept on the screen.
        var left = cursor.X + offset.X;
        if (left + box.X > view.Max.X)
            left = cursor.X - offset.X - box.X;
        var top = cursor.Y + offset.Y;
        if (top - box.Y < view.Min.Y)
            top = cursor.Y - offset.Y + box.Y;
        left = Math.Clamp(left, view.Min.X, Math.Max(view.Min.X, view.Max.X - box.X));
        top = Math.Clamp(top, Math.Min(view.Max.Y, view.Min.Y + box.Y), view.Max.Y);

        var middle = top - box.Y / 2;
        if (tip.Background.A > 0)
            frame.Sprites.Add(FrameDrawing.Quad(new Vector2(left + box.X / 2, middle), box, tip.Background));
        frame.Sprites.Add(FrameDrawing.Label(lines, new Vector2(left + pad, middle), size, tip.Color, TextAlign.Left) with
        {
            Font = string.IsNullOrWhiteSpace(tip.Font) ? null : tip.Font,
        });
    }

    // What the box says broken onto lines to its width, with its widest line's letters and its lines; worked out again only
    // when that changes.
    private static (string Lines, int Columns, int Rows) LinesOf(Tooltip tip)
    {
        var shown = tip.Shown ?? "";
        if (tip.Wrapped is { Lines: { } kept } wrapped && ReferenceEquals(wrapped.From, shown) && wrapped.Width == tip.Width && wrapped.Size == tip.Size)
            return (kept, wrapped.Columns, wrapped.Rows);

        var lines = TextWrap.Wrap(shown, tip.Width, tip.Size);
        var (columns, rows, run) = (0, 1, 0);
        foreach (var c in lines)
        {
            if (c == '\n')
                (rows, run) = (rows + 1, 0);
            else
                columns = Math.Max(columns, ++run);
        }

        tip.Wrapped = (shown, tip.Width, tip.Size, lines, columns, rows);
        return (lines, columns, rows);
    }
}
