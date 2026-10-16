using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Cards;

/// <summary>How cards and the places they go are drawn: faces, backs, piles, play zones. Everything in the card's own space.</summary>
internal static class CardLook
{
    /// <summary>The color of a card's back when its deck doesn't say.</summary>
    public static readonly Color DefaultBack = Color.FromBytes(0x2D, 0x4F, 0x8A);

    /// <summary>How many cards of a pile are drawn under its top one at most: a thicker deck looks no thicker.</summary>
    public const int PileLayers = 12;

    /// <summary>Seconds a deck shakes for as it's shuffled.</summary>
    public const float JiggleTime = 0.4f;

    // An average glyph of the built-in font is about this much of its height across, for wrapping text.
    private const float GlyphWidth = 0.52f;

    /// <summary>A card's face: its border and color, value, title, picture, text and kind.</summary>
    public static void Face(Drawing drawing, Card card, Matrix4x4 world)
    {
        var size = card.Size;
        var (w, h) = (Math.Abs(size.X), Math.Abs(size.Y));
        if (w <= 0 || h <= 0)
            return;
        var face = drawing.Tint(card, card.Color);
        var ink = drawing.Tint(card, card.TextColor);
        var font = string.IsNullOrWhiteSpace(card.Font) ? null : card.Font;
        var border = Scale(face, 0.7f);
        Rect(drawing, world, Vector2.Zero, new Vector2(w, h), border);
        Rect(drawing, world, Vector2.Zero, new Vector2(w - 0.08f, h - 0.08f), face);

        var unit = Math.Min(w / 1.6f, h / 2.24f);
        var top = h / 2 - 0.1f * unit;
        if (card.Value != 0)
        {
            var badge = new Vector2(-w / 2 + 0.24f * unit, top - 0.15f * unit);
            Rect(drawing, world, badge, new Vector2(0.36f, 0.36f) * unit, border);
            Text(drawing, card, world, card.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), badge, 0.24f * unit, face, TextAlign.Center, font);
        }

        if (card.Cost != 0)
        {
            // Its cost in a gem in the top-right corner, dimmed while its hand hasn't the mana.
            var gem = new Vector2(w / 2 - 0.24f * unit, top - 0.15f * unit);
            var color = card.Unaffordable ? Color.FromBytes(0x6A, 0x6E, 0x7A) : ManaColor;
            Diamond(drawing, card, world, gem, 0.42f * unit, drawing.Tint(card, Scale(color, 0.6f)));
            Diamond(drawing, card, world, gem, 0.34f * unit, drawing.Tint(card, color));
            Text(drawing, card, world, card.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture), gem, 0.24f * unit,
                drawing.Tint(card, Color.White), TextAlign.Center, font);
        }

        var title = card.ShownTitle;
        if (title.Length > 0)
        {
            var room = w - (card.Value != 0 ? 0.45f : 0.1f) * unit - (card.Cost != 0 ? 0.45f : 0.1f) * unit;
            var titleSize = Math.Min(0.2f * unit, room / Math.Max(1, title.Length * GlyphWidth));
            var shift = ((card.Value != 0 ? 0.35f : 0) - (card.Cost != 0 ? 0.35f : 0)) * unit / 2;
            Text(drawing, card, world, title, new Vector2(shift, top - 0.15f * unit), titleSize, ink, TextAlign.Center, font);
        }

        var y = top - 0.42f * unit;
        if (!string.IsNullOrWhiteSpace(card.Image))
        {
            var art = new Vector2(w - 0.24f * unit, h * 0.38f);
            var middle = new Vector2(0, y - art.Y / 2);
            Rect(drawing, world, middle, art + new Vector2(0.04f), border);
            drawing.Image(Matrix4x4.CreateScale(art.X, art.Y, 1) * Matrix4x4.CreateTranslation(middle.X, middle.Y, 0) * world, card.Image,
                drawing.Tint(card, Color.White));
            y -= art.Y + 0.12f * unit;
        }
        else if (card.ShownText.Length == 0 && card.Value != 0)
        {
            Text(drawing, card, world, card.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2(0, -0.05f * unit),
                0.8f * unit, Scale(face, 0.55f), TextAlign.Center, font);
        }

        var textSize = 0.15f * unit;
        foreach (var line in card.Lines(textSize, w - 0.24f * unit))
        {
            if (y < -h / 2 + 0.35f * unit)
                break;
            Text(drawing, card, world, line, new Vector2(0, y - textSize / 2), textSize, ink, TextAlign.Center, font);
            y -= textSize * 1.25f;
        }

        if (!string.IsNullOrWhiteSpace(card.Kind))
            Text(drawing, card, world, card.Kind.Trim(), new Vector2(0, -h / 2 + 0.17f * unit), 0.13f * unit, ink with { A = ink.A * 0.6f }, TextAlign.Center, font);

        if (card.Entity?.GetComponent<Health>() is { } health)
        {
            // A creature's health in a red gem in the bottom-right corner, its number pale while it's hurt.
            var gem = new Vector2(w / 2 - 0.24f * unit, -h / 2 + 0.24f * unit);
            Diamond(drawing, card, world, gem, 0.46f * unit, drawing.Tint(card, Scale(LifeColor, 0.6f)));
            Diamond(drawing, card, world, gem, 0.38f * unit, drawing.Tint(card, LifeColor));
            Text(drawing, card, world, card.LifeText(health.Current), gem, 0.24f * unit,
                drawing.Tint(card, health.Current < health.Max ? Color.FromBytes(0xFF, 0xD8, 0x9A) : Color.White), TextAlign.Center, font);
        }
    }

    /// <summary>The color of a creature card's health.</summary>
    public static readonly Color LifeColor = Color.FromBytes(0xC8, 0x34, 0x3C);

    /// <summary>A card lit all over in <paramref name="color"/>: hurt, it flashes.</summary>
    public static void Flash(Drawing drawing, Card card, Matrix4x4 world, Color color)
    {
        var size = card.Size;
        Rect(drawing, world, Vector2.Zero, new Vector2(Math.Abs(size.X), Math.Abs(size.Y)), drawing.Tint(card, color));
    }

    /// <summary>A card's back: <paramref name="image"/> over it, or a plain back of <paramref name="color"/> with a lighter frame.</summary>
    public static void Back(Drawing drawing, Component owner, Matrix4x4 world, Vector2 size, string? image, Color color)
    {
        var (w, h) = (Math.Abs(size.X), Math.Abs(size.Y));
        if (w <= 0 || h <= 0)
            return;
        var tint = TintOf(drawing, owner, color);
        if (!string.IsNullOrWhiteSpace(image))
        {
            Rect(drawing, world, Vector2.Zero, new Vector2(w, h), TintOf(drawing, owner, Scale(color, 0.6f)));
            drawing.Image(Matrix4x4.CreateScale(w - 0.06f, h - 0.06f, 1) * world, image, TintOf(drawing, owner, Color.White));
            return;
        }

        Rect(drawing, world, Vector2.Zero, new Vector2(w, h), TintOf(drawing, owner, Color.FromBytes(0xF4, 0xEE, 0xE0)));
        Rect(drawing, world, Vector2.Zero, new Vector2(w - 0.12f, h - 0.12f), tint);
        var frame = TintOf(drawing, owner, Color.Lerp(color, Color.White, 0.35f));
        OutlineIn(drawing, world, new Vector2(w - 0.36f, h - 0.36f), 0.04f, frame);
        // A diamond in the middle.
        var d = Math.Min(w, h) * 0.22f;
        drawing.Add(new SpriteDraw(Matrix4x4.CreateScale(d, d, 1) * Matrix4x4.CreateRotationZ(MathF.PI / 4) * world, null, frame, owner.Entity!));
    }

    /// <summary>The edge of a card in a pile under the top one: a plain card.</summary>
    public static void Edge(Drawing drawing, Component owner, Matrix4x4 world, Vector2 size)
    {
        var (w, h) = (Math.Abs(size.X), Math.Abs(size.Y));
        Rect(drawing, world, Vector2.Zero, new Vector2(w, h), TintOf(drawing, owner, Color.FromBytes(0xB8, 0xB0, 0xA0)));
        Rect(drawing, world, Vector2.Zero, new Vector2(w - 0.06f, h - 0.06f), TintOf(drawing, owner, Color.FromBytes(0xEC, 0xE6, 0xD8)));
    }

    /// <summary>Where a pile goes while it's empty: a faint outline, with what it's made from in the editor.</summary>
    public static void EmptySlot(Drawing drawing, Component owner, Matrix4x4 world, Vector2 size, string? label)
    {
        var (w, h) = (Math.Abs(size.X), Math.Abs(size.Y));
        Rect(drawing, world, Vector2.Zero, new Vector2(w, h), TintOf(drawing, owner, Color.FromBytes(0xFF, 0xFF, 0xFF, 0x12)));
        OutlineIn(drawing, world, new Vector2(w, h), 0.04f, TintOf(drawing, owner, Color.FromBytes(0xFF, 0xFF, 0xFF, 0x50)));
        if (label is not null)
            Text(drawing, owner, world, label, Vector2.Zero, 0.2f, TintOf(drawing, owner, Color.FromBytes(0xFF, 0xFF, 0xFF, 0xA0)), TextAlign.Center, null);
    }

    /// <summary>A play zone: a translucent area with an outline, and its label while it's empty.</summary>
    public static void Zone(Drawing drawing, Component owner, Matrix4x4 world, Vector2 size, Color color, string? label)
    {
        var (w, h) = (Math.Abs(size.X), Math.Abs(size.Y));
        if (color.A <= 0 || w <= 0 || h <= 0)
            return;
        Rect(drawing, world, Vector2.Zero, new Vector2(w, h), color);
        OutlineIn(drawing, world, new Vector2(w, h), 0.04f, color with { A = Math.Min(1, color.A * 2.5f) });
        if (!string.IsNullOrWhiteSpace(label))
            Text(drawing, owner, world, label.Trim(), Vector2.Zero, 0.3f, color with { A = Math.Min(1, color.A * 4) }, TextAlign.Center, null);
    }

    /// <summary>Where a hand fans out, in the editor: a faint outline.</summary>
    public static void HandArea(Drawing drawing, Component owner, Matrix4x4 world, Vector2 size)
    {
        var tint = TintOf(drawing, owner, Color.FromBytes(0xFF, 0xD8, 0x4A, 0x60));
        OutlineIn(drawing, world, size, 0.04f, tint);
        Text(drawing, owner, world, owner.Entity!.Name, Vector2.Zero, 0.3f, tint, TextAlign.Center, null);
    }

    /// <summary>The color of mana: a card's cost, a hand's mana.</summary>
    public static readonly Color ManaColor = Color.FromBytes(0x2F, 0x6F, 0xD8);

    /// <summary>A hand's mana beside it: a gem with what it has and the most it fills to, "3/5".</summary>
    public static void Mana(Drawing drawing, Component owner, Matrix4x4 world, Vector2 at, bool any, string text)
    {
        Diamond(drawing, owner, world, at, 0.95f, TintOf(drawing, owner, Scale(ManaColor, 0.55f)));
        Diamond(drawing, owner, world, at, 0.8f, TintOf(drawing, owner, any ? ManaColor : Color.FromBytes(0x55, 0x5A, 0x66)));
        Text(drawing, owner, world, text, at, text.Length > 3 ? 0.2f : 0.26f, TintOf(drawing, owner, Color.White), TextAlign.Center, null);
    }

    /// <summary>
    /// A card being aimed: a dotted line from it to where it's aimed (in the world), and a box round each thing it can be
    /// aimed at, the one it's on brighter.
    /// </summary>
    public static void Aim(Drawing drawing, Component owner, Vector2 from, Vector2 to, List<(Vector2 Center, Vector2 Size, bool On)> marks, Color color)
    {
        foreach (var (center, size, on) in marks)
        {
            var at = Matrix4x4.CreateTranslation(center.X, center.Y, 0);
            OutlineIn(drawing, at, size + new Vector2(0.2f), on ? 0.1f : 0.05f, color with { A = color.A * (on ? 1 : 0.45f) });
        }

        var line = to - from;
        var length = line.Length();
        if (length < 0.05f)
            return;
        var angle = MathF.Atan2(line.Y, line.X);
        var dots = Math.Max(1, (int)(length / 0.3f));
        for (var i = 0; i <= dots; i++)
        {
            var p = from + line * (i / (float)dots);
            var place = Matrix4x4.CreateRotationZ(angle) * Matrix4x4.CreateTranslation(p.X, p.Y, 0);
            Rect(drawing, place, Vector2.Zero, new Vector2(0.16f, 0.08f), color);
        }

        // A diamond where it lands.
        Diamond(drawing, owner, Matrix4x4.CreateTranslation(to.X, to.Y, 0), Vector2.Zero, 0.3f, color);
    }

    /// <summary>The edges of a rectangle of <paramref name="size"/> centered in <paramref name="world"/>'s space.</summary>
    public static void Outline(Drawing drawing, Component owner, Matrix4x4 world, Vector2 size, float thickness, Color color) =>
        OutlineIn(drawing, world, size, thickness, color);

    /// <summary>
    /// <paramref name="text"/> in lines that fit across <paramref name="width"/> at <paramref name="size"/>, broken between
    /// words (and on the line breaks it has).
    /// </summary>
    public static string[] Wrap(string? text, float size, float width)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var fits = Math.Max(4, (int)(width / Math.Max(0.01f, size * GlyphWidth)));
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (next.Length <= fits || line.Length == 0)
                {
                    line = next;
                    continue;
                }

                lines.Add(line);
                line = word;
            }

            lines.Add(line);
        }

        return [.. lines];
    }

    private static Color TintOf(Drawing drawing, Component owner, Color color) => owner is IDrawable drawn ? drawing.Tint(drawn, color) : color;

    private static Color Scale(Color color, float by) => new(color.R * by, color.G * by, color.B * by, color.A);

    private static void Rect(Drawing drawing, Matrix4x4 world, Vector2 center, Vector2 size, Color color)
    {
        if (color.A > 0)
            drawing.Add(Quads.Rect(drawing.Entity, center, size, color, world));
    }

    // A square turned on its corner, `size` from corner to corner.
    private static void Diamond(Drawing drawing, Component owner, Matrix4x4 world, Vector2 center, float size, Color color)
    {
        if (color.A <= 0)
            return;
        var side = size / MathF.Sqrt(2);
        drawing.Add(new SpriteDraw(Matrix4x4.CreateScale(side, side, 1) * Matrix4x4.CreateRotationZ(MathF.PI / 4) *
                                   Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world, null, color, owner.Entity!));
    }

    private static void OutlineIn(Drawing drawing, Matrix4x4 world, Vector2 size, float thickness, Color color)
    {
        if (color.A <= 0)
            return;
        var half = size / 2;
        Rect(drawing, world, new Vector2(0, half.Y), new Vector2(size.X + thickness, thickness), color);
        Rect(drawing, world, new Vector2(0, -half.Y), new Vector2(size.X + thickness, thickness), color);
        Rect(drawing, world, new Vector2(-half.X, 0), new Vector2(thickness, size.Y - thickness), color);
        Rect(drawing, world, new Vector2(half.X, 0), new Vector2(thickness, size.Y - thickness), color);
    }

    private static void Text(Drawing drawing, Component owner, Matrix4x4 world, string text, Vector2 at, float size, Color color, TextAlign align, string? font)
    {
        if (color.A <= 0 || size <= 0)
            return;
        drawing.Add(new SpriteDraw(Matrix4x4.CreateScale(size, size, 1) * Matrix4x4.CreateTranslation(at.X, at.Y, 0) * world, null, color, owner.Entity!, text)
        {
            Align = align,
            Font = font,
        });
    }
}
