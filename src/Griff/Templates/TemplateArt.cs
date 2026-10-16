namespace Griff.Templates;

/// <summary>
/// The pictures Griff's core templates come with (Top-down, Multiplayer, BunnyMark) and those several templates share (a
/// crate, the crypt's hero, stone walls), painted in code as small pixel art, with the canvas they're painted on. Saved as PNGs in
/// Textures/, so they open (and can be repainted) in any editor. A module's template paints its own (Match3Art,
/// FirstPersonArt) with the same canvas.
/// </summary>
internal static partial class TemplateArt
{
    public const string GuidePortrait = "Textures/guide.png";

    /// <summary>The Top-down template's pictures: the guide's portrait.</summary>
    internal static Dictionary<string, byte[]> TopDownFiles() => new() { [GuidePortrait] = GuideArt().Png() };

    // Big grey blocks, each its own shade, with dark mortar between.
    internal static Canvas Stone()
    {
        var c = new Canvas(32, 32, seed: 1);
        c.Fill(Rgb(0x3A, 0x3A, 0x40));
        Courses(c, 16, 8, Rgb(0x76, 0x76, 0x80), 0.18f, 1);
        c.Noise(0.08f);
        return c;
    }

    // Rows of blocks `height` tall and `width` long, every other row shifted half a block, each block a little lighter
    // or darker, with a highlight along its top and shadow along its bottom.
    internal static void Courses(Canvas c, int width, int height, Pixel color, float vary, int mortar)
    {
        var random = new Random(c.Seed * 31);
        for (var row = 0; row * height < c.Height; row++)
        {
            var shift = row % 2 == 0 ? 0 : width / 2;
            for (var start = -width; start < c.Width; start += width)
            {
                var shade = 1 + (random.NextSingle() * 2 - 1) * vary;
                var block = color.Times(shade);
                var x0 = start + shift + mortar;
                var y0 = row * height + mortar;
                c.Rect(x0, y0, width - mortar, height - mortar, block);
                c.Rect(x0, y0, width - mortar, 1, block.Times(1.2f));
                c.Rect(x0, y0 + height - mortar - 1, width - mortar, 1, block.Times(0.75f));
            }
        }
    }

    // The Top-down template's guide, framed for the dialogue box: a kindly face under a gold hood.
    private static Canvas GuideArt()
    {
        var c = new Canvas(24, 24, seed: 30);
        var frame = Rgb(0x2A, 0x2F, 0x3A);
        c.Fill(frame);
        c.Rect(2, 2, 20, 20, Rgb(0x3E, 0x48, 0x63));
        var gold = Rgb(0xE3, 0xB3, 0x41);
        c.Ellipse(12, 13, 8, 9, gold); // hood
        c.Rect(4, 18, 16, 4, gold.Times(0.8f)); // shoulders
        var skin = Rgb(0xE8, 0xB8, 0x8E);
        c.Ellipse(12, 13, 5, 5, skin);
        c.Rect(9, 12, 2, 2, Rgb(0x22, 0x1C, 0x1A)); // eyes
        c.Rect(14, 12, 2, 2, Rgb(0x22, 0x1C, 0x1A));
        c.Rect(10, 16, 5, 1, Rgb(0xA0, 0x52, 0x48)); // smile
        c.Set(9, 15, Rgb(0xA0, 0x52, 0x48));
        c.Set(15, 15, Rgb(0xA0, 0x52, 0x48));
        c.Rect(8, 7, 9, 2, gold.Times(1.2f)); // light on the hood
        c.Shade(0.08f);
        c.Bevel(0, 0, 24, 24, frame.Times(1.6f), frame.Times(0.6f));
        return c;
    }

    // A regular polygon's corners around (cx, cy), the first at the given angle (0 is straight right).
    internal static (float X, float Y)[] Ring(int corners, float cx, float cy, float radius, float start) =>
        Enumerable.Range(0, corners).Select(i => start + i * MathF.Tau / corners)
            .Select(a => (cx + radius * MathF.Cos(a), cy - radius * MathF.Sin(a))).ToArray();

    internal static (float X, float Y)[] Star(float cx, float cy, float outer, float inner) =>
        Enumerable.Range(0, 10).Select(i => (Angle: MathF.PI / 2 + i * MathF.PI / 5, Radius: i % 2 == 0 ? outer : inner))
            .Select(p => (cx + p.Radius * MathF.Cos(p.Angle), cy - p.Radius * MathF.Sin(p.Angle))).ToArray();

    // Fills the pixels whose middles are inside the polygon.
    internal static void Polygon(Canvas c, (float X, float Y)[] points, Pixel color)
    {
        for (var y = 0; y < c.Height; y++)
        {
            for (var x = 0; x < c.Width; x++)
            {
                var (px, py) = (x + 0.5f, y + 0.5f);
                var inside = false;
                for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                {
                    var (a, b) = (points[i], points[j]);
                    if ((a.Y > py) != (b.Y > py) && px < (b.X - a.X) * (py - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }

                if (inside)
                    c.Set(x, y, color);
            }
        }
    }

    internal static Pixel Rgb(byte r, byte g, byte b) => new(r, g, b, 255);

    /// <summary>One RGBA pixel.</summary>
    internal readonly record struct Pixel(byte R, byte G, byte B, byte A)
    {
        public static readonly Pixel Clear = new(0, 0, 0, 0);

        public Pixel Times(float amount) => new(Clamp(R * amount), Clamp(G * amount), Clamp(B * amount), A);

        public static byte Clamp(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
    }

    /// <summary>A little picture to paint on, pixel by pixel, top-left first.</summary>
    internal sealed class Canvas(int width, int height, int seed = 0)
    {
        private readonly Pixel[] _pixels = new Pixel[width * height];

        public int Width => width;

        public int Height => height;

        public int Seed => seed;

        public Pixel Get(int x, int y) => x >= 0 && y >= 0 && x < width && y < height ? _pixels[y * width + x] : Pixel.Clear;

        public void Set(int x, int y, Pixel color)
        {
            if (x >= 0 && y >= 0 && x < width && y < height)
                _pixels[y * width + x] = color;
        }

        public void Fill(Pixel color) => Array.Fill(_pixels, color);

        public void Rect(int x, int y, int w, int h, Pixel color)
        {
            for (var j = y; j < y + h; j++)
            {
                for (var i = x; i < x + w; i++)
                    Set(i, j, color);
            }
        }

        // A raised edge: light along the top and left, dark along the bottom and right.
        public void Bevel(int x, int y, int w, int h, Pixel light, Pixel dark)
        {
            Rect(x, y, w, 1, light);
            Rect(x, y, 1, h, light);
            Rect(x, y + h - 1, w, 1, dark);
            Rect(x + w - 1, y, 1, h, dark);
        }

        public void Ellipse(int cx, int cy, int rx, int ry, Pixel color)
        {
            for (var y = cy - ry; y <= cy + ry; y++)
            {
                for (var x = cx - rx; x <= cx + rx; x++)
                {
                    var (dx, dy) = ((x - cx) / (rx + 0.5f), (y - cy) / (ry + 0.5f));
                    if (dx * dx + dy * dy <= 1)
                        Set(x, y, color);
                }
            }
        }

        public void Line(int x0, int y0, int x1, int y1, Pixel color)
        {
            var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (var i = 0; i <= steps; i++)
            {
                var t = steps == 0 ? 0 : i / (float)steps;
                var (x, y) = ((int)MathF.Round(x0 + (x1 - x0) * t), (int)MathF.Round(y0 + (y1 - y0) * t));
                Set(x, y, color);
                Set(x + 1, y, color);
            }
        }

        // A soft ball of light: the edge color at the rim, the core color in the middle, fading out past the rim.
        public void Glow(int cx, int cy, int radius, Pixel edge, Pixel core)
        {
            for (var y = cy - radius; y <= cy + radius; y++)
            {
                for (var x = cx - radius; x <= cx + radius; x++)
                {
                    var t = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                    if (t > 1)
                        continue;
                    var mix = Math.Clamp(1 - t * 1.6f, 0, 1);
                    var color = new Pixel(Pixel.Clamp(edge.R + (core.R - edge.R) * mix), Pixel.Clamp(edge.G + (core.G - edge.G) * mix),
                        Pixel.Clamp(edge.B + (core.B - edge.B) * mix), Pixel.Clamp(255 * Math.Clamp((1 - t) * 2.2f, 0, 1)));
                    var under = Get(x, y);
                    Set(x, y, under.A == 0 || color.A == 255 ? color : Over(under, color));
                }
            }
        }

        public void Blend(int x, int y, Pixel color, float amount)
        {
            var under = Get(x, y);
            Set(x, y, new Pixel(Pixel.Clamp(under.R + (color.R - under.R) * amount), Pixel.Clamp(under.G + (color.G - under.G) * amount),
                Pixel.Clamp(under.B + (color.B - under.B) * amount), under.A));
        }

        // Every painted pixel a touch lighter or darker, so flat colors look like a surface.
        public void Noise(float amount)
        {
            var random = new Random(seed * 7 + 1);
            for (var i = 0; i < _pixels.Length; i++)
            {
                if (_pixels[i].A > 0)
                    _pixels[i] = _pixels[i].Times(1 + (random.NextSingle() * 2 - 1) * amount);
            }
        }

        // Light from the top left: painted pixels get lighter towards the top and darker towards the bottom.
        public void Shade(float amount)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var p = Get(x, y);
                    if (p.A > 0)
                        Set(x, y, p.Times(1 + amount - 2 * amount * y / height));
                }
            }
        }

        // A one-pixel dark edge around everything painted, so it stands out against any wall.
        public void Outline(Pixel color)
        {
            var edge = new List<(int, int)>();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (Get(x, y).A == 0 && (Get(x - 1, y).A > 128 || Get(x + 1, y).A > 128 || Get(x, y - 1).A > 128 || Get(x, y + 1).A > 128))
                        edge.Add((x, y));
                }
            }

            foreach (var (x, y) in edge)
                Set(x, y, color);
        }

        public void Paste(Canvas other, int left, int top)
        {
            for (var y = 0; y < other.Height; y++)
            {
                for (var x = 0; x < other.Width; x++)
                    Set(left + x, top + y, other.Get(x, y));
            }
        }

        private static Pixel Over(Pixel under, Pixel over)
        {
            var a = over.A / 255f;
            return new Pixel(Pixel.Clamp(under.R + (over.R - under.R) * a), Pixel.Clamp(under.G + (over.G - under.G) * a),
                Pixel.Clamp(under.B + (over.B - under.B) * a), Math.Max(under.A, over.A));
        }

        /// <summary>The picture as a PNG file; with a <paramref name="layout"/>, a sprite sheet the Sprite Painter opens cut into its frames.</summary>
        public byte[] Png(Art.SheetLayout? layout = null)
        {
            var rgba = new byte[width * height * 4];
            for (var i = 0; i < _pixels.Length; i++)
                (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (_pixels[i].R, _pixels[i].G, _pixels[i].B, _pixels[i].A);
            return Art.Png.Encode(rgba, width, height, layout?.ToText());
        }
    }
}
