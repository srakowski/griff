using System.Globalization;
using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Rendering;

// Sandbox extras over the world: ropes, wires and gate labels (in the editor too), and while the game runs, the block
// being dug or built on, what the tools are doing, and the HUD: the hotbar, the crafting list, the tools and the spawn
// list. Things pinned to the screen (menus, panels) still go on top.
public static partial class SceneRenderer
{
    private static readonly Color Accent = Color.FromBytes(0x4C, 0x9A, 0xFF);
    private static readonly Color SlotFace = Color.FromBytes(0x2B, 0x32, 0x40);
    private static readonly Color SlotHover = Color.FromBytes(0x3E, 0x47, 0x58);

    private static List<SpriteDraw> WithSandbox(Scene scene, Game? game, List<SpriteDraw> sprites, Entity? camera, View view, float cameraSize)
    {
        var thickness = cameraSize / 180;
        var firstPerson = game?.FirstPersonView is not null;
        var overlays = new List<SpriteDraw>();
        if (!firstPerson)
        {
            overlays.AddRange(RopeDraws(scene));
            overlays.AddRange(WireDraws(scene, game, thickness));
            if (game is not null)
            {
                overlays.AddRange(BuilderDraws(game, thickness));
                overlays.AddRange(ToolDraws(game, thickness));
            }
        }

        var hud = game?.Sandbox.Hud is { } layout ? SandboxHudDraws(game, layout, thickness).ToList() : [];
        if (firstPerson && game is not null)
            hud.InsertRange(0, FirstPersonDigDraws(game, view));
        if (overlays.Count == 0 && hud.Count == 0)
            return sprites;

        var world = sprites.Where(d => !IsOnScreen(d.Entity, camera)).ToList();
        world.AddRange(overlays);
        world.AddRange(hud);
        world.AddRange(sprites.Where(d => IsOnScreen(d.Entity, camera)));
        return world;
    }

    // ----- In the world -----

    private static IEnumerable<SpriteDraw> RopeDraws(Scene scene)
    {
        foreach (var rope in scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<Rope>()))
        {
            var other = rope.Other?.Scene == scene ? rope.Other : WirePorts.Find(rope.Entity!, rope.To);
            if (other is null || !other.IsActive || !(rope.Width > 0))
                continue;
            var a = Game.CenterOf(rope.Entity!) + rope.Offset;
            var b = Game.CenterOf(other) + rope.OtherOffset;
            yield return LineDraw(a, b, rope.Width, rope.Color);
        }
    }

    // Wires run from the output's entity to the input's, brighter while they carry a signal; gates say what they are.
    private static IEnumerable<SpriteDraw> WireDraws(Scene scene, Game? game, float thickness)
    {
        foreach (var wire in scene.Entities.SelectMany(e => e.GetComponents<Wire>()))
        {
            if (!wire.Visible || !wire.Entity!.IsActive)
                continue;
            var source = wire.Source?.Scene == scene ? wire.Source : WirePorts.Find(wire.Entity!, wire.From);
            if (source is null || !source.IsActive)
                continue;
            var (a, b) = (Game.CenterOf(source), Game.CenterOf(wire.Entity!));
            var live = game is null || wire.Signal > 0;
            var color = live ? wire.Color : wire.Color with { R = wire.Color.R * 0.45f, G = wire.Color.G * 0.45f, B = wire.Color.B * 0.45f };
            yield return LineDraw(a, b, thickness * 1.6f, color);
            yield return Quad(a, new Vector2(thickness * 4), color);
            yield return Quad(b, new Vector2(thickness * 4), color);
        }

        foreach (var gate in scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<WireGate>()))
            yield return Label(gate.Label, Game.CenterOf(gate.Entity!), LabelSize(gate.Entity!), Color.White, TextAlign.Center);
        foreach (var display in scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<WireDisplay>()))
        {
            if (display.Entity!.GetComponent<Text>() is null)
                yield return Label(display.Shown, Game.CenterOf(display.Entity), LabelSize(display.Entity), Color.White, TextAlign.Center);
        }
    }

    // Big enough to read on the part, small enough to fit.
    private static float LabelSize(Entity entity)
    {
        if (entity.GetComponent<Collider>() is { } collider)
            return Math.Clamp(Physics.BoundsOf(collider).HalfSize.Y * 0.8f, 0.15f, 1f);
        return 0.35f;
    }

    // The block a builder points at, cracking as it's dug, or the outline of where a block would go.
    private static IEnumerable<SpriteDraw> BuilderDraws(Game game, float thickness)
    {
        foreach (var builder in game.Scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<BlockBuilder>()))
        {
            if (builder.Map is not { Entity: not null } map || builder.Highlight.A <= 0)
                continue;
            var cell = map.TileSize * new Vector2(map.Entity.Transform.WorldMatrix.M11, map.Entity.Transform.WorldMatrix.M22);
            if (builder.Target is { } target)
            {
                var center = Game.CellWorld(map, target.X, target.Y);
                if (builder.Progress > 0)
                {
                    var amount = Math.Clamp(builder.Progress, 0, 1);
                    yield return Quad(center, cell, new Color(0, 0, 0, 0.15f + 0.45f * amount));
                    foreach (var crack in Cracks(center, cell, amount, thickness))
                        yield return crack;
                }

                foreach (var line in Outline(center, cell, thickness * 1.5f, builder.Highlight))
                    yield return line;
            }
            else if (builder.PlaceAt is { } at && !string.IsNullOrWhiteSpace(builder.Item))
            {
                var center = Game.CellWorld(map, at.X, at.Y);
                var blocks = map.Entity.GetComponent<Blocks>();
                var tile = blocks?.TileOf(builder.Item) ?? -1;
                if (tile >= 0 && map.ImageOf(tile) is { } tileImage)
                    yield return new SpriteDraw(Matrix4x4.CreateScale(cell.X, cell.Y, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0), tileImage,
                        new Color(1, 1, 1, 0.4f), ScreenEntity) { Source = map.TileRect(tile) };
                foreach (var line in Outline(center, cell, thickness, builder.Highlight with { A = builder.Highlight.A * 0.6f }))
                    yield return line;
            }
        }
    }

    // Jagged lines across a block, more of them the further it's dug.
    private static IEnumerable<SpriteDraw> Cracks(Vector2 center, Vector2 cell, float amount, float thickness)
    {
        var half = cell / 2;
        var color = new Color(0, 0, 0, 0.75f);
        Vector2[][] cracks =
        [
            [new(-0.1f, 0.05f), new(-0.45f, 0.3f)],
            [new(-0.1f, 0.05f), new(0.25f, 0.4f)],
            [new(-0.1f, 0.05f), new(0.05f, -0.4f)],
            [new(0.25f, 0.4f), new(0.45f, 0.1f)],
            [new(0.05f, -0.4f), new(-0.35f, -0.3f)],
            [new(0.05f, -0.4f), new(0.4f, -0.2f)],
        ];
        var shown = (int)MathF.Ceiling(amount * cracks.Length);
        for (var i = 0; i < shown; i++)
            yield return LineDraw(center + cracks[i][0] * half * 2, center + cracks[i][1] * half * 2, thickness * 1.4f, color);
    }

    // A first-person dig shows as a bar under the crosshair.
    private static IEnumerable<SpriteDraw> FirstPersonDigDraws(Game game, View view)
    {
        if (game.FirstPersonViewer?.GetComponent<BlockBuilder>() is not { Progress: > 0 } builder)
            yield break;
        var u = view.Half.Y * 2 / 100;
        foreach (var draw in Meter(view.Center - new Vector2(0, 5 * u), 10 * u, 0.9f * u, builder.Progress, Color.White))
            yield return draw;
    }

    // The thing being carried, and a line from a first click to the mouse while a weld, rope or wire waits for its other end.
    private static IEnumerable<SpriteDraw> ToolDraws(Game game, float thickness)
    {
        var mouse = game.Input.MouseWorld;
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

    private static IEnumerable<SpriteDraw> SandboxHudDraws(Game game, SandboxHud hud, float thickness)
    {
        var u = hud.Unit;
        var mouse = game.Input.MouseWorld;
        var buttons = game.SandboxButtons;
        var hotbar = game.Scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<Hotbar>()).FirstOrDefault();
        var crafting = game.Scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<Crafting>()).FirstOrDefault();
        var tools = game.Tools;
        var world = WorldOf(game);

        // The hotbar: each slot's block (or a swatch), how many, and the picked one's name over it.
        if (hotbar is not null)
        {
            var slots = buttons.Where(b => b.Kind == HudButtonKind.Slot).ToList();
            if (slots.Count > 0)
            {
                var strip = hud.Hotbar(slots.Count);
                yield return Quad(strip.Center, new Vector2(strip.Width, strip.Height), HudBack);
            }

            foreach (var slot in slots)
            {
                var picked = slot.Index == hotbar.Selected;
                var rect = slot.Rect;
                yield return Quad(rect.Center, new Vector2(rect.Width, rect.Height), picked ? Color.White : rect.Contains(mouse) ? HudDim : HudEdge);
                yield return Quad(rect.Center, new Vector2(rect.Width, rect.Height) - new Vector2(thickness * (picked ? 5 : 3)), picked ? SlotHover : SlotFace);
                var (item, count) = slot.Index < hotbar.Shown.Count ? hotbar.Shown[slot.Index] : (null, 0);
                if (item is not null)
                {
                    foreach (var draw in ItemIcon(world, hotbar, item, rect.Center + new Vector2(0, 0.4f * u), rect.Width * 0.55f))
                        yield return draw;
                    yield return Label(count.ToString(CultureInfo.InvariantCulture), new Vector2(rect.Right - 0.9f * u, rect.Y + 1.5f * u), 2.2f * u, HudText, TextAlign.Right);
                }

                yield return Label((slot.Index + 1) % 10 + "", new Vector2(rect.X + 1.1f * u, rect.Top - 1.3f * u), 1.6f * u, HudDim, TextAlign.Left);
            }

            if (hotbar.SelectedItem is { } selected && slots.Count > 0)
                yield return Label(selected, new Vector2(hud.View.Center.X, hud.Hotbar(slots.Count).Top + 1.8f * u), 2.4f * u, HudText, TextAlign.Center);
        }

        // The crafting list: what each recipe makes and takes, lit up when there's enough.
        if (crafting is { IsOpen: true })
        {
            var rows = buttons.Where(b => b.Kind == HudButtonKind.Recipe).ToList();
            if (rows.Count > 0)
            {
                var panel = hud.Recipes(rows.Count, hotbar is not null);
                yield return Quad(panel.Center, new Vector2(panel.Width, panel.Height), HudBack);
                yield return Label($"Crafting  ({crafting.Key})", new Vector2(panel.X + 1.5f * u, panel.Top - 2.6f * u), 2.4f * u, HudText, TextAlign.Left);
                yield return Label("Click to make", new Vector2(panel.Right - 1.5f * u, panel.Top - 2.6f * u), 1.9f * u, HudDim, TextAlign.Right);
            }

            foreach (var row in rows)
            {
                var recipe = crafting.Parsed[row.Index];
                var can = game.CanCraft(crafting.Entity!, recipe);
                var rect = row.Rect;
                var hovered = rect.Contains(mouse);
                yield return Quad(rect.Center, new Vector2(rect.Width, rect.Height), hovered && can ? SlotHover : SlotFace);
                if (hovered && can)
                {
                    foreach (var line in Outline(rect.Center, new Vector2(rect.Width, rect.Height), thickness * 1.5f, Accent))
                        yield return line;
                }

                foreach (var draw in ItemIcon(world, hotbar, recipe.Item, new Vector2(rect.X + rect.Height / 2 + 0.5f * u, rect.Center.Y), rect.Height * 0.7f))
                    yield return draw;
                var made = recipe.Count > 1 ? $"{recipe.Item} x{recipe.Count}" : recipe.Item;
                yield return Label(made, new Vector2(rect.X + rect.Height + 1.5f * u, rect.Center.Y), 2.3f * u, can ? HudText : HudDim, TextAlign.Left);
                var needs = recipe.NeedsText + (recipe.Station is { } station ? $"  @ {station}" : "");
                yield return Label(needs, new Vector2(rect.Right - 1.2f * u, rect.Center.Y), 1.9f * u, can ? Neutral : HudDim, TextAlign.Right);
            }
        }

        // The tools along the top, the spawn list under them, and what to do next.
        if (tools is not null)
        {
            foreach (var button in buttons.Where(b => b.Kind == HudButtonKind.Tool))
            {
                var active = button.Index == tools.Selected;
                foreach (var draw in HudButtonDraws(button, active, mouse, thickness, u, (button.Index + 1).ToString(CultureInfo.InvariantCulture)))
                    yield return draw;
            }

            foreach (var button in buttons.Where(b => b.Kind == HudButtonKind.Prop))
            {
                foreach (var draw in HudButtonDraws(button, button.Index == tools.SelectedProp, mouse, thickness, u, null))
                    yield return draw;
            }

            var ports = buttons.Where(b => b.Kind == HudButtonKind.Port).ToList();
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
            var hint = game.SandboxNotice ?? ToolHint(game, tools);
            if (!string.IsNullOrEmpty(hint))
                yield return Label(hint, new Vector2(hud.View.Center.X, y), 2.2f * u, game.SandboxNotice is null ? HudText : Neutral, TextAlign.Center);
        }
        else if (game.SandboxNotice is { } notice)
        {
            var y = hud.View.Y + (hotbar is null ? 5 : 15) * u;
            yield return Label(notice, new Vector2(hud.View.Center.X, y + (crafting?.IsOpen == true ? 0 : 3 * u)), 2.4f * u, Neutral, TextAlign.Center);
        }
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
        SandboxTool.Rope when game.ToolFirst is { } first => $"Click what to tie {first.Name} to. Right-click to cancel.",
        SandboxTool.Rope => "Click something, then what to tie it to.",
        SandboxTool.Wire when game.ToolFirst is { } first => $"Click what {first.Name} should drive. Right-click to cancel.",
        SandboxTool.Wire => "Click a button, sensor or gate, then a lamp, thruster, gate or door.",
        SandboxTool.Remove => "Click something to remove it. Z undoes what you made.",
        _ => null,
    };

    // The block's own picture from the world's tileset, or a swatch of the hotbar's color for the item.
    private static IEnumerable<SpriteDraw> ItemIcon(Tilemap? world, Hotbar? hotbar, string item, Vector2 center, float size)
    {
        var tile = world?.Entity?.GetComponent<Blocks>()?.TileOf(item) ?? -1;
        if (tile >= 0 && world!.ImageOf(tile) is { } image)
        {
            yield return new SpriteDraw(Matrix4x4.CreateScale(size, size, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0), image,
                world.Tint, ScreenEntity) { Source = world.TileRect(tile) };
            yield break;
        }

        var color = hotbar?.ColorOf(item) ?? (tile >= 0 ? world!.Tint : Color.Gray);
        yield return Quad(center, new Vector2(size * 0.8f), color);
        yield return Label(item.Length > 0 ? item[..1].ToUpperInvariant() : "?", center, size * 0.5f, Color.White, TextAlign.Center);
    }

    private static Tilemap? WorldOf(Game game) =>
        game.Scene.Entities.Where(e => e.IsActive).Select(e => e.GetComponent<Tilemap>()).FirstOrDefault(t => t?.Entity?.GetComponent<Blocks>() is not null);

    // ----- Glowing blocks -----

    private static IEnumerable<LightDraw> GlowDraws(Scene scene)
    {
        foreach (var blocks in scene.Entities.Where(e => e.IsActive).SelectMany(e => e.GetComponents<Blocks>()))
        {
            if (string.IsNullOrWhiteSpace(blocks.GlowingTiles) || blocks.Entity!.GetComponent<Tilemap>() is not { } map || !(blocks.GlowRadius > 0))
                continue;
            var scale = new Vector2(map.Entity!.Transform.WorldMatrix.M11, map.Entity.Transform.WorldMatrix.M12).Length();
            foreach (var (x, y, tile) in map.Cells())
            {
                if (blocks.Glows(tile))
                    yield return new LightDraw(Game.CellWorld(map, x, y), blocks.GlowRadius * scale, blocks.GlowColor, 1);
            }
        }
    }
}
