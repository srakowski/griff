namespace Griff.Art;

/// <summary>What the <see cref="SpriteGenerator"/> makes.</summary>
public enum SpriteIdea
{
    /// <summary>A little monster with eyes, two frames of it breathing.</summary>
    Creature,

    /// <summary>A spaceship pointing up.</summary>
    Ship,

    /// <summary>A gem or potion to pick up.</summary>
    Item,

    /// <summary>A tile of earth with a grassy top.</summary>
    Ground,

    /// <summary>A tile of bricks.</summary>
    Bricks,
}

/// <summary>
/// Makes pixel art from a seed, the way the Sound Maker makes sounds from a preset: a starting point to paint over.
/// Shapes are mirrored left to right (as most sprites are), shaded light at the top and dark at the bottom, and
/// outlined. The same idea, seed and size always make the same picture.
/// </summary>
public static class SpriteGenerator
{
    private const uint Outline = 0xFF1D1A24;
    private const uint White = 0xFFFFF1E8;
    private const uint Black = 0xFF000000;

    // Dark, middle and light shades of the colors things come in.
    private static readonly (uint Dark, uint Mid, uint Light)[] Ramps =
    [
        (0xFF008751, 0xFF00E436, 0xFFB8F27A), // green
        (0xFF1D2B53, 0xFF29ADFF, 0xFFA8E4FF), // blue
        (0xFF7E2553, 0xFFFF004D, 0xFFFF77A8), // red
        (0xFFAB5236, 0xFFFFA300, 0xFFFFEC27), // orange
        (0xFF5F3A7A, 0xFF83769C, 0xFFD6C7F0), // purple
        (0xFF5F574F, 0xFFC2C3C7, 0xFFFFF1E8), // gray
    ];

    /// <summary>The frames of a picture of <paramref name="idea"/>, each <paramref name="width"/> by <paramref name="height"/> pixels.</summary>
    public static IReadOnlyList<uint[]> Generate(SpriteIdea idea, int seed, int width = 16, int height = 16)
    {
        width = Math.Clamp(width, 4, SheetLayout.MaxCellSize);
        height = Math.Clamp(height, 4, SheetLayout.MaxCellSize);
        var random = new Random(HashCode.Combine(idea, seed));
        var ramp = Ramps[random.Next(Ramps.Length)];
        var sheet = new PixelSheet(new SheetLayout(SheetKind.Sprite, width, height, 2, 1, 2, 4));
        switch (idea)
        {
            case SpriteIdea.Creature:
                Creature(sheet, random, ramp);
                return [sheet.Cell(0), sheet.Cell(1)];
            case SpriteIdea.Ship:
                Blob(sheet, random, ramp, ship: true);
                break;
            case SpriteIdea.Item:
                Item(sheet, random, ramp);
                break;
            case SpriteIdea.Ground:
                Ground(sheet, random);
                break;
            default:
                Bricks(sheet, random, ramp);
                break;
        }

        return [sheet.Cell(0)];
    }

    private static void Creature(PixelSheet sheet, Random random, (uint Dark, uint Mid, uint Light) ramp)
    {
        var (w, h) = (sheet.Layout.CellWidth, sheet.Layout.CellHeight);
        var feet = Math.Max(1, h / 8);
        Blob(sheet, random, ramp, ship: false, bottom: h - 1 - feet);

        // Feet under the body, then eyes a third of the way down.
        var legX = Math.Max(1, w / 2 - 1 - random.Next(1, Math.Max(2, w / 4)));
        for (var y = h - 1 - feet; y < h - 1; y++)
        {
            sheet.Set(0, legX, y, ramp.Dark);
            sheet.Set(0, w - 1 - legX, y, ramp.Dark);
        }

        var top = Enumerable.Range(0, h).FirstOrDefault(y => sheet.Get(0, w / 2, y) != 0);
        var bodyBottom = h - 1 - feet;
        var eyeY = top + Math.Max(1, (bodyBottom - top) / 3);
        var eyeX = w / 2 - 1 - random.Next(1, Math.Max(2, w / 6));
        foreach (var x in new[] { eyeX, w - 1 - eyeX })
        {
            if (sheet.Get(0, x, eyeY) == 0)
                continue;
            sheet.Set(0, x, eyeY, White);
            sheet.Set(0, x, eyeY + 1, Black);
        }

        sheet.Outline(0, Outline);

        // The second frame breathes: everything above the feet sinks a pixel.
        sheet.SetCell(1, sheet.Cell(0));
        for (var y = bodyBottom; y >= 1; y--)
            for (var x = 0; x < w; x++)
                sheet.Set(1, x, y, sheet.Get(0, x, y - 1) is var above && above != 0 ? above : y >= bodyBottom ? sheet.Get(0, x, y) : 0);
        for (var x = 0; x < w; x++)
            sheet.Set(1, x, 0, 0);
    }

    // A random, mirrored, roughly round (or, for a ship, pointed) shape, shaded and outlined.
    private static void Blob(PixelSheet sheet, Random random, (uint Dark, uint Mid, uint Light) ramp, bool ship, int? bottom = null)
    {
        var (w, h) = (sheet.Layout.CellWidth, sheet.Layout.CellHeight);
        var last = bottom ?? h - 2;
        double cx = (w - 1) / 2.0, top = 1.0, cy = (top + last) / 2.0;
        double rx = w / 2.0 - 1.5, ry = (last - top) / 2.0 + 0.5;
        for (var y = 1; y <= last; y++)
        {
            for (var x = 0; x < (w + 1) / 2; x++)
            {
                var dx = (x - cx) / rx;
                var dy = (y - cy) / ry;
                // A ship narrows to a point at the top and flares at the back.
                if (ship)
                    dx /= Math.Clamp((y - top + 1) / (last - top + 1.0) * 1.4, 0.25, 1);
                var inside = 1 - (dx * dx + dy * dy);
                if (inside + (random.NextDouble() - 0.5) * 0.6 < 0.15 && Math.Abs(x - cx) > 0.6)
                    continue;
                if (inside < -0.05)
                    continue;
                var shade = y < top + (last - top) * 0.35 ? ramp.Light : y < top + (last - top) * 0.75 ? ramp.Mid : ramp.Dark;
                if (random.NextDouble() < 0.12)
                    shade = shade == ramp.Mid ? ramp.Dark : ramp.Mid;
                sheet.Set(0, x, y, shade);
                sheet.Set(0, w - 1 - x, y, shade);
            }
        }

        if (ship)
        {
            // A cockpit down the middle.
            var (cockpitTop, cockpitBottom) = ((int)(top + (last - top) * 0.3), (int)(top + (last - top) * 0.55));
            for (var y = cockpitTop; y <= cockpitBottom; y++)
            {
                sheet.Set(0, w / 2, y, 0xFF29ADFF);
                sheet.Set(0, (w - 1) / 2, y, 0xFF29ADFF);
            }

            sheet.Outline(0, Outline);
        }
    }

    private static void Item(PixelSheet sheet, Random random, (uint Dark, uint Mid, uint Light) ramp)
    {
        var (w, h) = (sheet.Layout.CellWidth, sheet.Layout.CellHeight);
        var potion = random.Next(2) == 0;
        if (potion)
        {
            // A round flask with a neck and a cork.
            sheet.Ellipse(0, 2, h / 3, w - 3, h - 2, ramp.Mid, filled: true);
            sheet.Rectangle(0, w / 2 - 2, h / 3 - 2, (w - 1) / 2 + 2, h / 3, 0xFFC2C3C7, filled: true);
            sheet.Rectangle(0, w / 2 - 1, 1, (w - 1) / 2 + 1, h / 3 - 3, 0xFFAB5236, filled: true);
            for (var y = (h / 3 + h - 2) / 2 + 1; y <= h - 2; y++)
                for (var x = 0; x < w; x++)
                    if (sheet.Get(0, x, y) == ramp.Mid)
                        sheet.Set(0, x, y, ramp.Dark);
        }
        else
        {
            // A gem: a diamond, light on its upper left.
            var (cx, cy) = (w / 2.0 - 0.5, h / 2.0 - 0.5);
            var r = Math.Min(w, h) / 2.0 - 1.5;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var d = Math.Abs(x - cx) / r + Math.Abs(y - cy) / (r * 1.2);
                    if (d <= 1)
                        sheet.Set(0, x, y, y < cy ? (x < cx ? ramp.Light : ramp.Mid) : (x < cx ? ramp.Mid : ramp.Dark));
                }
        }

        // A glint.
        var glintX = Enumerable.Range(0, w).FirstOrDefault(x => sheet.Get(0, x, h / 2) != 0) + 1;
        sheet.Set(0, glintX, h / 2 - 1, White);
        sheet.Set(0, glintX, h / 2, White);
        sheet.Outline(0, Outline);
    }

    private static void Ground(PixelSheet sheet, Random random)
    {
        var (w, h) = (sheet.Layout.CellWidth, sheet.Layout.CellHeight);
        (uint Dark, uint Mid, uint Light) dirt = (0xFF5A3426, 0xFF8A5236, 0xFFAB7A4C);
        var grass = (Dark: 0xFF008751, Mid: 0xFF00E436);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var roll = random.NextDouble();
                sheet.Set(0, x, y, roll < 0.15 ? dirt.Dark : roll < 0.25 ? dirt.Light : dirt.Mid);
            }

        // Grass along the top, hanging down a pixel or two in places.
        var depth = Math.Max(2, h / 5);
        for (var x = 0; x < w; x++)
        {
            var hang = depth + random.Next(0, 2) + (random.NextDouble() < 0.25 ? 1 : 0);
            for (var y = 0; y < hang; y++)
                sheet.Set(0, x, y, y == 0 ? 0xFFB8F27A : y == hang - 1 ? grass.Dark : grass.Mid);
        }
    }

    private static void Bricks(PixelSheet sheet, Random random, (uint Dark, uint Mid, uint Light) ramp)
    {
        var (w, h) = (sheet.Layout.CellWidth, sheet.Layout.CellHeight);
        var mortar = ramp.Dark;
        var brickHeight = Math.Max(2, h / 4);
        for (var y = 0; y < h; y++)
        {
            var row = y / brickHeight;
            var offset = row % 2 == 0 ? 0 : w / 4;
            for (var x = 0; x < w; x++)
            {
                var edge = y % brickHeight == brickHeight - 1 || (x + offset) % (w / 2) == 0;
                var color = edge ? mortar : y % brickHeight == 0 ? ramp.Light : ramp.Mid;
                if (!edge && random.NextDouble() < 0.06)
                    color = ramp.Light;
                sheet.Set(0, x, y, color);
            }
        }
    }
}
