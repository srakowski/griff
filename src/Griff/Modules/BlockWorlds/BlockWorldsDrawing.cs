using System.Globalization;
using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using static Griff.Rendering.OverlayShapes;

namespace Griff.Modules.BlockWorlds;

// A block world's extras over the world while the game runs: the block being dug or built on, and the HUD: the hotbar
// and the crafting list. Things pinned to the screen (menus, panels) still go on top.
internal static class BlockWorldsDrawing
{
    private static readonly Color Accent = Color.FromBytes(0x4C, 0x9A, 0xFF);
    private static readonly Color SlotFace = Color.FromBytes(0x2B, 0x32, 0x40);
    private static readonly Color SlotHover = Color.FromBytes(0x3E, 0x47, 0x58);

    /// <summary>Draws a block world's extras into <paramref name="frame"/>, over the world and under what's pinned to the screen.</summary>
    public static void Draw(BlockWorldsSystem system, FrameDrawing frame)
    {
        var game = frame.Game;
        var state = system.Local;
        if (state.Hud is null && game.Scene.Active<BlockBuilder>().Count == 0)
            return;
        var thickness = frame.LineWidth;
        var firstPerson = frame.SeenThrough is not null;
        var overlays = new List<SpriteDraw>();
        if (!firstPerson)
            overlays.AddRange(BuilderDraws(game, thickness, frame.BlendOf));

        // The hotbar and crafting list are the whole screen's, in the main view.
        var hud = state.Hud is { } layout && frame.Viewport is null ? HudDraws(game, state, layout, thickness).ToList() : [];
        if (state.Hud is { } laidOut && hud.Count > 0)
            frame.PinToView(hud, laidOut.View.Center, laidOut.View.Height / 2);
        if (firstPerson)
            hud.InsertRange(0, FirstPersonDigDraws(game, frame.View, frame.BlendOf));
        if (overlays.Count == 0 && hud.Count == 0)
            return;
        overlays.AddRange(hud);
        frame.AddOverWorld(overlays);
    }

    // ----- In the world -----

    // The block a builder points at, cracking as it's dug, or the outline of where a block would go.
    private static IEnumerable<SpriteDraw> BuilderDraws(Game game, float thickness, Func<Entity, StepBlend> blendOf)
    {
        foreach (var builder in game.Scene.Active<BlockBuilder>())
        {
            if (builder.Map is not { Entity: not null } map || builder.Highlight.A <= 0)
                continue;
            var cell = map.TileSize * new Vector2(map.Entity.Transform.WorldMatrix.M11, map.Entity.Transform.WorldMatrix.M22);
            if (builder.Target is { } target)
            {
                var center = BlockWorldsSystem.CellWorld(map, target.X, target.Y);
                if (builder.Progress > 0)
                {
                    var amount = Math.Clamp(builder.ProgressDrawn(blendOf(builder.Entity!)), 0, 1);
                    yield return Quad(center, cell, new Color(0, 0, 0, 0.15f + 0.45f * amount));
                    foreach (var crack in Cracks(center, cell, amount, thickness))
                        yield return crack;
                }

                foreach (var line in Outline(center, cell, thickness * 1.5f, builder.Highlight))
                    yield return line;
            }
            else if (builder.PlaceAt is { } at && !string.IsNullOrWhiteSpace(builder.Item))
            {
                var center = BlockWorldsSystem.CellWorld(map, at.X, at.Y);
                var blocks = map.Entity.GetComponent<Blocks>();
                var tile = blocks?.TileOf(builder.Item) ?? -1;
                if (tile >= 0 && map.ImageOf(tile) is { } tileImage)
                    yield return new SpriteDraw(Matrix4x4.CreateScale(cell.X, cell.Y, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0), tileImage,
                        new Color(1, 1, 1, 0.4f), SceneRenderer.ScreenEntity) { Source = map.TileRect(tile) };
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
    private static IEnumerable<SpriteDraw> FirstPersonDigDraws(Game game, Box view, Func<Entity, StepBlend> blendOf)
    {
        if (game.Eyes?.GetComponent<BlockBuilder>() is not { Progress: > 0 } builder)
            yield break;
        var u = view.HalfSize.Y * 2 / 100;
        foreach (var draw in Meter(view.Center - new Vector2(0, 5 * u), 10 * u, 0.9f * u, builder.ProgressDrawn(blendOf(builder.Entity!)), Color.White))
            yield return draw;
    }

    // ----- The HUD -----

    private static IEnumerable<SpriteDraw> HudDraws(Game game, BlockWorldsState state, BlockHud hud, float thickness)
    {
        var u = hud.Unit;
        var mouse = game.Input.MouseWorld;
        var buttons = state.Buttons;
        var hotbar = game.Scene.Active<Hotbar>().FirstOrDefault();
        var crafting = game.Scene.Active<Crafting>().FirstOrDefault();
        var world = WorldOf(game);

        // The hotbar: each slot's block (or a swatch), how many, and the picked one's name over it.
        if (hotbar is not null)
        {
            var slots = buttons.Where(b => b.Kind == BlockHud.Slot).ToList();
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
            var rows = buttons.Where(b => b.Kind == BlockHud.Recipe).ToList();
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

        // What was made.
        if (state.NoticeLeft > 0 && state.Notice is { } notice)
        {
            var y = hud.View.Y + (hotbar is null ? 5 : 15) * u;
            yield return Label(notice, new Vector2(hud.View.Center.X, y + (crafting?.IsOpen == true ? 0 : 3 * u)), 2.4f * u, Neutral, TextAlign.Center);
        }
    }


    // The block's own picture from the world's tileset, or a swatch of the hotbar's color for the item.
    private static IEnumerable<SpriteDraw> ItemIcon(Tilemap? world, Hotbar? hotbar, string item, Vector2 center, float size)
    {
        var tile = world?.Entity?.GetComponent<Blocks>()?.TileOf(item) ?? -1;
        if (tile >= 0 && world!.ImageOf(tile) is { } image)
        {
            yield return new SpriteDraw(Matrix4x4.CreateScale(size, size, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0), image,
                world.Tint, SceneRenderer.ScreenEntity) { Source = world.TileRect(tile) };
            yield break;
        }

        var color = hotbar?.ColorOf(item) ?? (tile >= 0 ? world!.Tint : Color.Gray);
        yield return Quad(center, new Vector2(size * 0.8f), color);
        yield return Label(item.Length > 0 ? item[..1].ToUpperInvariant() : "?", center, size * 0.5f, Color.White, TextAlign.Center);
    }

    private static Tilemap? WorldOf(Game game) =>
        game.Scene.Entities.Where(e => e.IsActive).Select(e => e.GetComponent<Tilemap>()).FirstOrDefault(t => t?.Entity?.GetComponent<Blocks>() is not null);
}
