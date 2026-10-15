using System.Globalization;
using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using static Griff.Rendering.OverlayShapes;

namespace Griff.Modules.Contraptions;

// The tools' extras over the world while the game runs: what the tools are doing, and the HUD: the tools, the spawn
// list and the ports to pick. Things pinned to the screen (menus, panels) still go on top. (Ropes, wires and gate labels
// draw themselves, in the editor too.)
internal static class ContraptionsDrawing
{
    private static readonly Color Accent = Color.FromBytes(0x4C, 0x9A, 0xFF);
    private static readonly Color SlotHover = Color.FromBytes(0x3E, 0x47, 0x58);

    /// <summary>Draws the tools' extras into <paramref name="frame"/>, over the world and under what's pinned to the screen.</summary>
    public static void Draw(ContraptionsSystem system, FrameDrawing frame)
    {
        var game = frame.Game;
        var state = system.Local;
        if (state.Hud is null && state.Held is null && state.First is null)
            return;
        var thickness = frame.LineWidth;
        var overlays = new List<SpriteDraw>();
        if (frame.SeenThrough is null)
            overlays.AddRange(ToolDraws(game, thickness));

        // The tool bar goes in the main view, not in every viewport.
        if (state.Hud is { } layout && system.Tools is { } tools && frame.Viewport is null)
        {
            var start = overlays.Count;
            overlays.AddRange(HudDraws(game, state, layout, tools, thickness));
            frame.PinToView(overlays, layout.View.Center, layout.View.Height / 2, start);
        }

        if (overlays.Count > 0)
            frame.AddOverWorld(overlays);
    }

    // ----- In the world -----

    // The thing being carried, a line from a first click to the mouse while a weld, hinge, rope or wire waits for its other
    // end, and a bolt on each hinge, so what's hinged where shows while building.
    private static IEnumerable<SpriteDraw> ToolDraws(Game game, float thickness)
    {
        var mouse = game.Input.MouseWorld;
        foreach (var hinge in game.Scene.Active<HingeJoint>())
        {
            var at = hinge.WorldAnchor;
            yield return Quad(at, new Vector2(thickness * 6), Neutral);
            yield return Quad(at, new Vector2(thickness * 3), HudBack);
        }

        if (game.Grabbed is { } held)
        {
            var center = Game.CenterOf(held);
            yield return LineDraw(center, mouse, thickness * 1.5f, Accent with { A = 0.8f });
            yield return Quad(mouse, new Vector2(thickness * 7), Accent);
            if (held.GetComponent<Collider>() is { } collider)
            {
                var bounds = Physics.BoundsOf(collider);
                foreach (var line in Outline(bounds.Center, bounds.HalfSize * 2, thickness * 1.5f, Accent))
                    yield return line;
            }
        }

        if (game.ToolFirst is { } first && first.Scene == game.Scene)
        {
            var color = game.Tools?.Current == SandboxTool.Wire ? Color.FromBytes(0xE3, 0x5B, 0x5B) : Neutral;
            yield return LineDraw(Game.CenterOf(first), mouse, thickness * 1.5f, color with { A = 0.85f });
            if (first.GetComponent<Collider>() is { } collider)
            {
                var bounds = Physics.BoundsOf(collider);
                foreach (var line in Outline(bounds.Center, bounds.HalfSize * 2, thickness * 1.5f, color))
                    yield return line;
            }
        }
    }

    // ----- The HUD -----

    private static IEnumerable<SpriteDraw> HudDraws(Game game, ContraptionsState state, ToolHud hud, SandboxTools tools, float thickness)
    {
        var u = hud.Unit;
        var mouse = game.Input.MouseWorld;
        var buttons = state.Buttons;

        // The tools along the top, the spawn list under them, and what to do next.
        foreach (var button in buttons.Where(b => b.Kind == ToolHud.Tool))
        {
            var active = button.Index == tools.Selected;
            foreach (var draw in HudButtonDraws(button, active, mouse, thickness, u, (button.Index + 1).ToString(CultureInfo.InvariantCulture)))
                yield return draw;
        }

        foreach (var button in buttons.Where(b => b.Kind == ToolHud.Prop))
        {
            foreach (var draw in HudButtonDraws(button, button.Index == tools.SelectedProp, mouse, thickness, u, null))
                yield return draw;
        }

        var ports = buttons.Where(b => b.Kind == ToolHud.Port).ToList();
        if (ports.Count > 0)
        {
            var first = ports[0].Rect;
            var title = ToolPickingOutputs(game) ? "Which output?" : "Which input?";
            yield return Label(title, new Vector2(first.X, first.Top + 1.8f * u), 2.1f * u, Neutral, TextAlign.Left);
        }

        foreach (var button in ports)
        {
            foreach (var draw in HudButtonDraws(button, false, mouse, thickness, u, null))
                yield return draw;
        }

        var y = hud.View.Top - 2 * u - hud.ToolHeight - (tools.Current == SandboxTool.Spawn ? 9 * u : 3 * u);
        var notice = state.NoticeLeft > 0 ? state.Notice : null;
        var hint = notice ?? ToolHint(game, tools);
        if (!string.IsNullOrEmpty(hint))
            yield return Label(hint, new Vector2(hud.View.Center.X, y), 2.2f * u, notice is null ? HudText : Neutral, TextAlign.Center);
    }

    private static bool ToolPickingOutputs(Game game) => game.ToolFirst is null;

    private static IEnumerable<SpriteDraw> HudButtonDraws(HudButton button, bool active, Vector2 mouse, float thickness, float u, string? key)
    {
        var rect = button.Rect;
        var hovered = rect.Contains(mouse);
        yield return Quad(rect.Center, new Vector2(rect.Width, rect.Height), active ? Accent : hovered ? HudDim : HudEdge);
        yield return Quad(rect.Center, new Vector2(rect.Width, rect.Height) - new Vector2(thickness * 3), hovered ? SlotHover : HudBack);
        var size = Math.Min(2.3f * u, rect.Width / Math.Max(5, button.Label.Length) * 1.7f);
        yield return Label(button.Label, rect.Center - new Vector2(0, 0.1f * u), size, active ? Color.White : HudText, TextAlign.Center);
        if (key is not null)
            yield return Label(key, new Vector2(rect.X + 0.9f * u, rect.Top - 1.1f * u), 1.5f * u, active ? Color.White : HudDim, TextAlign.Left);
    }

    private static string? ToolHint(Game game, SandboxTools tools) => tools.Current switch
    {
        SandboxTool.Grab when game.Grabbed is not null => "Let go to throw it. Right-click to freeze it in the air.",
        SandboxTool.Grab => "Drag something to carry it. Right-click a frozen thing to free it.",
        SandboxTool.Spawn => $"Click to put down a {tools.CurrentProp}. Z undoes.",
        SandboxTool.Weld when game.ToolFirst is { } first => $"Click what to weld {first.Name} to. Right-click to cancel.",
        SandboxTool.Weld => "Click something, then what to weld it to.",
        SandboxTool.Hinge when game.ToolFirst is { } first => $"Click what to hinge {first.Name} to, or somewhere empty to pin it there. Right-click to cancel.",
        SandboxTool.Hinge => "Click something where it should turn, then what to hinge it to.",
        SandboxTool.Rope when game.ToolFirst is { } first => $"Click what to tie {first.Name} to. Right-click to cancel.",
        SandboxTool.Rope => "Click something, then what to tie it to.",
        SandboxTool.Wire when game.ToolFirst is { } first => $"Click what {first.Name} should drive. Right-click to cancel.",
        SandboxTool.Wire => "Click a button, sensor or gate, then a lamp, thruster, gate or door.",
        SandboxTool.Remove => "Click something to remove it. Z undoes what you made.",
        _ => null,
    };
}
