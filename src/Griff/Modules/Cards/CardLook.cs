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

        var title = card.ShownTitle;
        if (title.Length > 0)
        {
            var titleSize = Math.Min(0.2f * unit, (w - 0.9f * unit) / Math.Max(1, title.Length * GlyphWidth));
            Text(drawing, card, world, title, new Vector2(card.Value != 0 ? 0.12f * unit : 0, top - 0.15f * unit), titleSize, ink, TextAlign.Center, font);
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
        else if (string.IsNullOrWhiteSpace(card.Text) && card.Value != 0)
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
