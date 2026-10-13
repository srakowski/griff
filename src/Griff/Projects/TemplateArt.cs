namespace Griff.Projects;

/// <summary>
/// The pictures the Top-down, Shooter, Sandbox, Contraption, Multiplayer, Puzzle, Match-3, Falling Blocks, Roguelike, Tactics and BunnyMark templates come with, painted in code as small pixel art:
/// a guide's portrait, a tileset of walls, enemies, pickups, and the guns held in hand. Saved as PNGs in Textures/, so
/// they open (and can be repainted) in any editor.
/// </summary>
internal static partial class TemplateArt
{
    public const string Walls = "Textures/walls.png";
    public const string Grunt = "Textures/grunt.png";
    public const string Gunner = "Textures/gunner.png";
    public const string Fireball = "Textures/fireball.png";
    public const string Medkit = "Textures/medkit.png";
    public const string Bullets = "Textures/bullets.png";
    public const string Shells = "Textures/shells.png";
    public const string Shotgun = "Textures/shotgun.png";
    public const string Key = "Textures/key.png";
    public const string Barrel = "Textures/barrel.png";
    public const string Lamp = "Textures/lamp.png";
    public const string Door = "Textures/door.png";
    public const string Player = "Textures/player.png";
    public const string PistolView = "Textures/pistol-view.png";
    public const string PistolFire = "Textures/pistol-fire.png";
    public const string ShotgunView = "Textures/shotgun-view.png";
    public const string ShotgunFire = "Textures/shotgun-fire.png";
    public const string GuidePortrait = "Textures/guide.png";

    /// <summary>The tiles across and down <see cref="Walls"/>.</summary>
    public const int WallColumns = 4, WallRows = 2;

    // The tiles of the wall sheet, in order.
    public const int StoneTile = 0, PanelTile = 1, BrickTile = 2, MossTile = 3, ComputerTile = 4, ExitTile = 5, HazardTile = 6, WoodTile = 7;

    /// <summary>The Top-down template's pictures: the guide's portrait.</summary>
    internal static Dictionary<string, byte[]> TopDownFiles() => new() { [GuidePortrait] = GuideArt().Png() };

    /// <summary>The Shooter template's pictures: the wall tiles, enemies, pickups and the guns held in hand.</summary>
    internal static Dictionary<string, byte[]> ShooterFiles() => new()
    {
        [Walls] = WallSheet().Png(),
        [Grunt] = GruntArt().Png(),
        [Gunner] = GunnerArt().Png(),
        [Fireball] = FireballArt().Png(),
        [Medkit] = MedkitArt().Png(),
        [Bullets] = AmmoArt(Rgb(0x5B, 0x63, 0x3A), Rgb(0xD9, 0xB0, 0x4C)).Png(),
        [Shells] = AmmoArt(Rgb(0x8C, 0x2E, 0x2A), Rgb(0xC9, 0x3F, 0x35)).Png(),
        [Shotgun] = ShotgunPickupArt().Png(),
        [Key] = KeyArt().Png(),
        [Barrel] = BarrelArt().Png(),
        [Lamp] = LampArt().Png(),
        [Door] = DoorArt().Png(),
        [Player] = PlayerArt().Png(),
        [PistolView] = PistolArt(firing: false).Png(),
        [PistolFire] = PistolArt(firing: true).Png(),
        [ShotgunView] = ShotgunViewArt(firing: false).Png(),
        [ShotgunFire] = ShotgunViewArt(firing: true).Png(),
    };

    // ----- Walls: a 4 by 2 sheet of 32-pixel tiles -----

    private static Canvas WallSheet()
    {
        const int size = 32;
        var sheet = new Canvas(size * WallColumns, size * WallRows);
        Canvas[] tiles = [Stone(), Panel(), Brick(), Moss(), Computer(), Exit(), Hazard(), Wood()];
        for (var i = 0; i < tiles.Length; i++)
            sheet.Paste(tiles[i], i % WallColumns * size, i / WallColumns * size);
        return sheet;
    }

    // Big grey blocks, each its own shade, with dark mortar between.
    private static Canvas Stone()
    {
        var c = new Canvas(32, 32, seed: 1);
        c.Fill(Rgb(0x3A, 0x3A, 0x40));
        Courses(c, 16, 8, Rgb(0x76, 0x76, 0x80), 0.18f, 1);
        c.Noise(0.08f);
        return c;
    }

    // Red-brown bricks, smaller and redder.
    private static Canvas Brick()
    {
        var c = new Canvas(32, 32, seed: 2);
        c.Fill(Rgb(0x4A, 0x36, 0x30));
        Courses(c, 8, 4, Rgb(0x96, 0x4A, 0x38), 0.15f, 1);
        c.Noise(0.07f);
        return c;
    }

    // Old stone with moss creeping up from the bottom.
    private static Canvas Moss()
    {
        var c = Stone();
        var random = new Random(3);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                if (random.NextSingle() < (y / 32f) * (y / 32f) * 0.75f)
                    c.Blend(x, y, Rgb(0x4E, 0x7A, 0x3A), 0.75f);
            }
        }

        return c;
    }

    // Rows of blocks `height` tall and `width` long, every other row shifted half a block, each block a little lighter
    // or darker, with a highlight along its top and shadow along its bottom.
    private static void Courses(Canvas c, int width, int height, Pixel color, float vary, int mortar)
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

    // A bevelled steel panel with rivets and a seam across the middle.
    private static Canvas Panel()
    {
        var c = new Canvas(32, 32, seed: 4);
        var steel = Rgb(0x5A, 0x67, 0x78);
        c.Fill(steel);
        c.Bevel(0, 0, 32, 32, steel.Times(1.35f), steel.Times(0.55f));
        c.Rect(2, 15, 28, 2, steel.Times(0.6f));
        c.Rect(2, 17, 28, 1, steel.Times(1.2f));
        foreach (var (x, y) in new[] { (4, 4), (27, 4), (4, 27), (27, 27) })
        {
            c.Rect(x, y, 2, 2, steel.Times(0.5f));
            c.Set(x, y, steel.Times(1.5f));
        }

        c.Noise(0.05f);
        return c;
    }

    // A steel panel with a glowing green screen of readouts.
    private static Canvas Computer()
    {
        var c = Panel();
        c.Rect(6, 5, 20, 9, Rgb(0x0C, 0x1A, 0x12));
        var random = new Random(5);
        for (var row = 0; row < 4; row++)
            c.Rect(8, 6 + row * 2, 4 + random.Next(14), 1, Rgb(0x5C, 0xF0, 0x8A));
        foreach (var (x, color) in new[] { (7, Rgb(0xF0, 0x50, 0x40)), (12, Rgb(0xF0, 0xC8, 0x40)), (17, Rgb(0x50, 0xC8, 0xF0)) })
            c.Rect(x, 22, 3, 3, color);
        return c;
    }

    // A steel panel with a lit EXIT sign.
    private static Canvas Exit()
    {
        var c = Panel();
        c.Rect(3, 8, 26, 11, Rgb(0x0E, 0x2A, 0x16));
        c.Bevel(3, 8, 26, 11, Rgb(0x3B, 0x9C, 0x55), Rgb(0x08, 0x18, 0x0C));
        var green = Rgb(0x6D, 0xFF, 0x8E);
        // E X I T in a 5-pixel-high font.
        string[] glyphs = ["###/#../##./#../###", "#.#/#.#/.#./#.#/#.#", "###/.#./.#./.#./###", "###/.#./.#./.#./.#."];
        for (var g = 0; g < glyphs.Length; g++)
        {
            var rows = glyphs[g].Split('/');
            for (var y = 0; y < rows.Length; y++)
            {
                for (var x = 0; x < rows[y].Length; x++)
                {
                    if (rows[y][x] == '#')
                        c.Set(6 + g * 6 + x, 11 + y, green);
                }
            }
        }

        return c;
    }

    // Yellow and black warning stripes.
    private static Canvas Hazard()
    {
        var c = new Canvas(32, 32, seed: 6);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
                c.Set(x, y, (x + y) / 8 % 2 == 0 ? Rgb(0xE8, 0xB8, 0x2A) : Rgb(0x22, 0x20, 0x1E));
        }

        c.Bevel(0, 0, 32, 32, Rgb(0xF4, 0xD8, 0x70), Rgb(0x10, 0x10, 0x10));
        c.Noise(0.08f);
        return c;
    }

    // Upright planks of wood.
    private static Canvas Wood()
    {
        var c = new Canvas(32, 32, seed: 7);
        var random = new Random(7);
        for (var plank = 0; plank < 4; plank++)
        {
            var color = Rgb(0x7A, 0x52, 0x32).Times(0.85f + random.NextSingle() * 0.3f);
            c.Rect(plank * 8, 0, 8, 32, color);
            c.Rect(plank * 8, 0, 1, 32, color.Times(0.6f));
            for (var y = 0; y < 32; y++)
            {
                if (random.NextSingle() < 0.3f)
                    c.Set(plank * 8 + 2 + random.Next(5), y, color.Times(0.8f));
            }

            c.Set(plank * 8 + 4, 4, Rgb(0x30, 0x30, 0x30));
            c.Set(plank * 8 + 4, 27, Rgb(0x30, 0x30, 0x30));
        }

        c.Noise(0.05f);
        return c;
    }

    // A heavy sliding door: steel with a yellow-and-black band and a light.
    private static Canvas DoorArt()
    {
        var c = new Canvas(32, 32, seed: 8);
        var steel = Rgb(0x8A, 0x90, 0x9A);
        c.Fill(steel);
        c.Bevel(0, 0, 32, 32, steel.Times(1.3f), steel.Times(0.5f));
        for (var x = 1; x < 31; x++)
        {
            for (var y = 13; y < 19; y++)
                c.Set(x, y, (x + y) / 3 % 2 == 0 ? Rgb(0xE8, 0xB8, 0x2A) : Rgb(0x22, 0x20, 0x1E));
        }

        c.Rect(15, 1, 2, 12, steel.Times(0.55f));
        c.Rect(15, 19, 2, 12, steel.Times(0.55f));
        c.Rect(13, 4, 6, 4, Rgb(0xFF, 0xFF, 0xFF));
        c.Noise(0.05f);
        return c;
    }

    // ----- Things in the world -----

    // A hulking red brute with horns and glowing eyes.
    private static Canvas GruntArt()
    {
        var c = new Canvas(32, 40, seed: 9);
        var skin = Rgb(0xB0, 0x3A, 0x2E);
        var dark = skin.Times(0.6f);
        c.Ellipse(16, 26, 11, 9, dark); // body
        c.Ellipse(16, 24, 9, 8, skin);
        c.Rect(9, 32, 5, 8, dark); // legs
        c.Rect(18, 32, 5, 8, dark);
        c.Rect(2, 19, 5, 13, skin); // arms, claws at the ends
        c.Rect(25, 19, 5, 13, skin);
        c.Rect(2, 31, 5, 2, Rgb(0xEE, 0xE6, 0xD0));
        c.Rect(25, 31, 5, 2, Rgb(0xEE, 0xE6, 0xD0));
        c.Ellipse(16, 11, 7, 7, skin); // head
        c.Line(10, 7, 6, 1, Rgb(0xEE, 0xE6, 0xD0)); // horns
        c.Line(22, 7, 26, 1, Rgb(0xEE, 0xE6, 0xD0));
        c.Rect(12, 9, 3, 2, Rgb(0xFF, 0xE0, 0x40)); // eyes
        c.Rect(18, 9, 3, 2, Rgb(0xFF, 0xE0, 0x40));
        c.Rect(13, 14, 7, 2, Rgb(0x30, 0x0C, 0x0C)); // mouth
        c.Set(14, 14, Rgb(0xEE, 0xE6, 0xD0));
        c.Set(18, 14, Rgb(0xEE, 0xE6, 0xD0));
        c.Shade(0.12f);
        c.Outline(Rgb(0x1A, 0x08, 0x08));
        return c;
    }

    // A squat purple robot with a red visor and a cannon for an arm.
    private static Canvas GunnerArt()
    {
        var c = new Canvas(32, 40, seed: 10);
        var plate = Rgb(0x6A, 0x58, 0x9A);
        c.Rect(7, 16, 18, 16, plate); // body
        c.Bevel(7, 16, 18, 16, plate.Times(1.4f), plate.Times(0.55f));
        c.Rect(9, 32, 5, 8, plate.Times(0.6f)); // legs
        c.Rect(18, 32, 5, 8, plate.Times(0.6f));
        c.Rect(9, 4, 14, 11, plate); // head
        c.Bevel(9, 4, 14, 11, plate.Times(1.4f), plate.Times(0.55f));
        c.Rect(10, 8, 12, 3, Rgb(0xFF, 0x3A, 0x30)); // visor
        c.Rect(12, 9, 3, 1, Rgb(0xFF, 0xC0, 0xB0));
        c.Rect(1, 18, 6, 7, Rgb(0x50, 0x54, 0x5C)); // cannon arm
        c.Rect(0, 19, 3, 5, Rgb(0x2A, 0x2C, 0x30));
        c.Rect(25, 18, 4, 11, plate.Times(0.8f)); // other arm
        c.Rect(13, 20, 6, 6, Rgb(0x40, 0xD0, 0xF0)); // core
        c.Set(14, 21, Rgb(0xE0, 0xFF, 0xFF));
        c.Shade(0.1f);
        c.Outline(Rgb(0x14, 0x10, 0x20));
        return c;
    }

    private static Canvas FireballArt()
    {
        var c = new Canvas(16, 16, seed: 11);
        c.Glow(8, 8, 8, Rgb(0xFF, 0x50, 0x10), Rgb(0xFF, 0xF4, 0xB0));
        return c;
    }

    private static Canvas MedkitArt()
    {
        var c = new Canvas(16, 16, seed: 12);
        c.Rect(1, 4, 14, 11, Rgb(0xE8, 0xEA, 0xEE));
        c.Bevel(1, 4, 14, 11, Rgb(0xFF, 0xFF, 0xFF), Rgb(0x9A, 0x9E, 0xA8));
        c.Rect(5, 2, 6, 2, Rgb(0x9A, 0x9E, 0xA8));
        c.Rect(7, 6, 2, 7, Rgb(0xE0, 0x2A, 0x2A));
        c.Rect(4, 8, 8, 2, Rgb(0xE0, 0x2A, 0x2A));
        c.Outline(Rgb(0x20, 0x20, 0x24));
        return c;
    }

    // A box of ammunition with rounds showing on top.
    private static Canvas AmmoArt(Pixel box, Pixel rounds)
    {
        var c = new Canvas(16, 16, seed: 13);
        for (var i = 0; i < 4; i++)
        {
            c.Rect(2 + i * 3, 2, 2, 5, rounds);
            c.Set(2 + i * 3, 2, rounds.Times(1.4f));
        }

        c.Rect(1, 6, 14, 9, box);
        c.Bevel(1, 6, 14, 9, box.Times(1.4f), box.Times(0.55f));
        c.Rect(4, 9, 8, 3, box.Times(0.7f));
        c.Outline(Rgb(0x14, 0x14, 0x10));
        return c;
    }

    // A shotgun lying on its side.
    private static Canvas ShotgunPickupArt()
    {
        var c = new Canvas(32, 16, seed: 14);
        c.Rect(2, 6, 10, 5, Rgb(0x7A, 0x4A, 0x2A)); // stock
        c.Rect(2, 9, 4, 4, Rgb(0x7A, 0x4A, 0x2A));
        c.Rect(11, 5, 6, 5, Rgb(0x3A, 0x3C, 0x42)); // body
        c.Rect(16, 5, 15, 2, Rgb(0x5A, 0x5E, 0x66)); // barrels
        c.Rect(16, 7, 15, 2, Rgb(0x48, 0x4C, 0x54));
        c.Rect(17, 9, 9, 2, Rgb(0x7A, 0x4A, 0x2A)); // pump
        c.Outline(Rgb(0x10, 0x10, 0x10));
        return c;
    }

    private static Canvas KeyArt()
    {
        var c = new Canvas(16, 16, seed: 15);
        var blue = Rgb(0x4C, 0x9A, 0xFF);
        c.Ellipse(5, 8, 4, 4, blue);
        c.Ellipse(5, 8, 2, 2, Pixel.Clear);
        c.Rect(8, 7, 7, 2, blue);
        c.Rect(12, 9, 2, 3, blue);
        c.Rect(10, 9, 1, 2, blue);
        c.Shade(0.15f);
        c.Outline(Rgb(0x10, 0x1C, 0x30));
        return c;
    }

    // A green barrel of something nasty.
    private static Canvas BarrelArt()
    {
        var c = new Canvas(24, 32, seed: 16);
        var green = Rgb(0x3E, 0x7A, 0x3A);
        c.Rect(2, 2, 20, 30, green);
        for (var x = 2; x < 22; x++)
        {
            // Round: lighter in the middle, darker to the sides.
            var t = MathF.Abs(x - 11.5f) / 10;
            for (var y = 2; y < 32; y++)
                c.Set(x, y, green.Times(1.25f - t * 0.7f));
        }

        c.Rect(2, 8, 20, 2, Rgb(0x2A, 0x2C, 0x30));
        c.Rect(2, 24, 20, 2, Rgb(0x2A, 0x2C, 0x30));
        c.Rect(4, 0, 16, 3, Rgb(0x9C, 0xF0, 0x50)); // ooze on top
        c.Rect(8, 14, 8, 6, Rgb(0xE8, 0xB8, 0x2A));
        c.Rect(11, 15, 2, 3, Rgb(0x22, 0x20, 0x1E));
        c.Set(11, 19, Rgb(0x22, 0x20, 0x1E));
        c.Outline(Rgb(0x10, 0x18, 0x10));
        return c;
    }

    // A lamp hanging from the ceiling.
    private static Canvas LampArt()
    {
        var c = new Canvas(16, 16, seed: 17);
        c.Rect(7, 0, 2, 6, Rgb(0x30, 0x30, 0x34));
        c.Ellipse(8, 10, 6, 4, Rgb(0x50, 0x54, 0x5C));
        c.Glow(8, 13, 4, Rgb(0xFF, 0xC0, 0x60), Rgb(0xFF, 0xFF, 0xE0));
        return c;
    }

    // The player as seen on the map: a green arrow pointing the way they face (right).
    private static Canvas PlayerArt()
    {
        var c = new Canvas(16, 16, seed: 18);
        for (var x = 2; x < 15; x++)
        {
            var half = (int)MathF.Round((15 - x) * 0.55f);
            c.Rect(x, 8 - half, 1, half * 2, Rgb(0x57, 0xD3, 0x6B));
        }

        c.Outline(Rgb(0x10, 0x30, 0x18));
        return c;
    }

    // ----- Guns in hand, seen from behind, 64 pixels square -----

    private static Canvas PistolArt(bool firing)
    {
        var c = new Canvas(64, 64, seed: 19);
        var skin = Rgb(0xC8, 0x8A, 0x64);
        var metal = Rgb(0x5A, 0x60, 0x6A);
        if (firing)
            c.Glow(32, 14, 13, Rgb(0xFF, 0x90, 0x20), Rgb(0xFF, 0xFF, 0xD0));
        c.Rect(26, 18, 12, 30, metal); // slide
        c.Bevel(26, 18, 12, 30, metal.Times(1.5f), metal.Times(0.5f));
        c.Rect(30, 16, 4, 3, metal.Times(0.6f)); // sight
        c.Rect(31, 20, 2, 10, metal.Times(0.7f));
        c.Ellipse(32, 54, 14, 12, skin); // hand
        c.Rect(18, 48, 28, 16, skin);
        c.Rect(20, 50, 24, 2, skin.Times(0.8f)); // knuckles
        c.Rect(20, 55, 24, 1, skin.Times(0.8f));
        c.Rect(44, 42, 6, 12, skin.Times(0.9f)); // thumb
        c.Shade(0.08f);
        c.Outline(Rgb(0x14, 0x12, 0x10));
        return c;
    }

    private static Canvas ShotgunViewArt(bool firing)
    {
        var c = new Canvas(64, 64, seed: 20);
        var wood = Rgb(0x7A, 0x4A, 0x2A);
        var metal = Rgb(0x48, 0x4C, 0x54);
        if (firing)
        {
            c.Glow(25, 12, 12, Rgb(0xFF, 0x90, 0x20), Rgb(0xFF, 0xFF, 0xD0));
            c.Glow(39, 12, 12, Rgb(0xFF, 0x90, 0x20), Rgb(0xFF, 0xFF, 0xD0));
        }

        c.Rect(19, 18, 12, 30, metal); // two barrels
        c.Rect(33, 18, 12, 30, metal);
        c.Bevel(19, 18, 12, 30, metal.Times(1.6f), metal.Times(0.5f));
        c.Bevel(33, 18, 12, 30, metal.Times(1.6f), metal.Times(0.5f));
        c.Ellipse(25, 19, 3, 2, Rgb(0x10, 0x10, 0x10));
        c.Ellipse(39, 19, 3, 2, Rgb(0x10, 0x10, 0x10));
        c.Rect(15, 40, 34, 24, wood); // pump
        c.Bevel(15, 40, 34, 24, wood.Times(1.35f), wood.Times(0.55f));
        for (var y = 44; y < 62; y += 4)
            c.Rect(18, y, 28, 1, wood.Times(0.75f));
        c.Rect(10, 52, 8, 12, Rgb(0xC8, 0x8A, 0x64)); // hands
        c.Rect(46, 52, 8, 12, Rgb(0xC8, 0x8A, 0x64));
        c.Shade(0.08f);
        c.Outline(Rgb(0x14, 0x12, 0x10));
        return c;
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

    private static Pixel Rgb(byte r, byte g, byte b) => new(r, g, b, 255);

    /// <summary>One RGBA pixel.</summary>
    private readonly record struct Pixel(byte R, byte G, byte B, byte A)
    {
        public static readonly Pixel Clear = new(0, 0, 0, 0);

        public Pixel Times(float amount) => new(Clamp(R * amount), Clamp(G * amount), Clamp(B * amount), A);

        public static byte Clamp(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
    }

    /// <summary>A little picture to paint on, pixel by pixel, top-left first.</summary>
    private sealed class Canvas(int width, int height, int seed = 0)
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

        /// <summary>The picture as a PNG file.</summary>
        public byte[] Png()
        {
            var rgba = new byte[width * height * 4];
            for (var i = 0; i < _pixels.Length; i++)
                (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (_pixels[i].R, _pixels[i].G, _pixels[i].B, _pixels[i].A);
            return Art.Png.Encode(rgba, width, height);
        }
    }
}
