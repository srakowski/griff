#if DEBUG
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// The demo game's art pack, drawn pixel by pixel: a 16-pixel tileset with autotiles, a hero sprite sheet, a slime,
/// pickups, a door, a torch and parallax hills. Written as PNGs, the way an artist's files would arrive.
/// </summary>
internal static class DemoArt
{
    // Palette (RGB).
    private const uint Clear = 0x00000000;
    private const uint Outline = 0xFF1A1220;
    private const uint Dirt = 0xFF4A3036, DirtDark = 0xFF3A242C, DirtLight = 0xFF5E3E40, Pebble = 0xFF6E5450;
    private const uint Grass = 0xFF5BB04A, GrassDark = 0xFF3B8A3E, GrassLight = 0xFF8FD65C;
    private const uint Wood = 0xFF9C6438, WoodDark = 0xFF6E4028, WoodLight = 0xFFC48A4E, Nail = 0xFFD9C7A0;
    private const uint Stone = 0xFF5A5F70, StoneDark = 0xFF40434F, StoneLight = 0xFF7A8094;
    private const uint Water = 0xFF2E6FB8, WaterDark = 0xFF1F4F8C, Foam = 0xFFBFE6FF, WaterLight = 0xFF5A9BE0;
    private const uint Gold = 0xFFF2C14E, GoldDark = 0xFFB57F24, GoldLight = 0xFFFFF0A8;
    private const uint Cloak = 0xFFE8743B, CloakDark = 0xFFB04E2A, Skin = 0xFFF4CDA5, Eye = 0xFF1A1220, Boot = 0xFF3A2A3A, Scarf = 0xFFFFD25E;
    private const uint Slime = 0xFF7BD35A, SlimeDark = 0xFF4E9A3C, SlimeLight = 0xFFC8F59A;
    private const uint Flame = 0xFFFFB347, FlameHot = 0xFFFFF1A6, FlameDeep = 0xFFE5532F;

    public static void WriteAll(string spritesFolder)
    {
        Directory.CreateDirectory(spritesFolder);
        Save(Tileset(), Path.Combine(spritesFolder, "tiles.png"));
        Save(Hero(), Path.Combine(spritesFolder, "hero.png"));
        Save(SlimeSheet(), Path.Combine(spritesFolder, "slime.png"));
        Save(Coin(), Path.Combine(spritesFolder, "coin.png"));
        Save(Key(), Path.Combine(spritesFolder, "key.png"));
        Save(Door(), Path.Combine(spritesFolder, "door.png"));
        Save(Flag(), Path.Combine(spritesFolder, "flag.png"));
        Save(Torch(), Path.Combine(spritesFolder, "torch.png"));
        Save(Hills(), Path.Combine(spritesFolder, "hills.png"));
        Save(Slash(), Path.Combine(spritesFolder, "slash.png"));
    }

    // ----- Tileset: 8 × 3 tiles of 16 px -----

    private static Pixels Tileset()
    {
        var p = new Pixels(128, 48);
        for (var mask = 0; mask < 16; mask++)
            GroundTile(p, (mask % 8) * 16, (mask / 8) * 16, mask);
        Plank(p, 0, 32);           // 16: one-way plank
        StoneTile(p, 16, 32);      // 17: stone block
        Tuft(p, 32, 32);           // 18: grass tuft (background)
        Flower(p, 48, 32);         // 19: flower (background)
        for (var f = 0; f < 4; f++)
            WaterTile(p, 64 + f * 16, 32, f); // 20-23: water
        return p;
    }

    // Joins: 1 up, 2 right, 4 down, 8 left.
    private static void GroundTile(Pixels p, int ox, int oy, int mask)
    {
        bool up = (mask & 1) != 0, right = (mask & 2) != 0, down = (mask & 4) != 0, left = (mask & 8) != 0;
        var rng = new Random(mask * 31 + 7);
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
        {
            var c = (x + y * 3 + rng.Next(5)) % 11 == 0 ? DirtDark : Dirt;
            if (rng.Next(23) == 0) c = DirtLight;
            p[ox + x, oy + y] = c;
        }

        // Pebbles.
        for (var i = 0; i < 3; i++)
        {
            int px = 2 + rng.Next(11), py = 5 + rng.Next(9);
            p[ox + px, oy + py] = Pebble;
            p[ox + px + 1, oy + py] = DirtLight;
        }

        if (!up)
        {
            // A grassy top with blades hanging over the dirt.
            for (var x = 0; x < 16; x++)
            {
                p[ox + x, oy] = GrassLight;
                p[ox + x, oy + 1] = Grass;
                p[ox + x, oy + 2] = Grass;
                p[ox + x, oy + 3] = (x % 3 == 0) ? Grass : GrassDark;
                if (x % 4 == 1) p[ox + x, oy + 4] = GrassDark;
            }
        }

        if (!left)
            for (var y = 0; y < 16; y++) p[ox, oy + y] = Outline;
        if (!right)
            for (var y = 0; y < 16; y++) p[ox + 15, oy + y] = Outline;
        if (!down)
            for (var x = 0; x < 16; x++) p[ox + x, oy + 15] = Outline;
        if (!up)
        {
            for (var x = 0; x < 16; x++) p[ox + x, oy] = Outline;
            for (var x = 1; x < 15; x++) p[ox + x, oy + 1] = GrassLight;
        }

        // Round the outer corners.
        if (!up && !left) { p[ox, oy] = Clear; p[ox + 1, oy] = Clear; p[ox, oy + 1] = Clear; p[ox + 1, oy + 1] = Outline; }
        if (!up && !right) { p[ox + 15, oy] = Clear; p[ox + 14, oy] = Clear; p[ox + 15, oy + 1] = Clear; p[ox + 14, oy + 1] = Outline; }
        if (!down && !left) { p[ox, oy + 15] = Clear; p[ox + 1, oy + 15] = Clear; p[ox, oy + 14] = Clear; p[ox + 1, oy + 14] = Outline; }
        if (!down && !right) { p[ox + 15, oy + 15] = Clear; p[ox + 14, oy + 15] = Clear; p[ox + 15, oy + 14] = Clear; p[ox + 14, oy + 14] = Outline; }
    }

    private static void Plank(Pixels p, int ox, int oy)
    {
        for (var x = 0; x < 16; x++)
        {
            p[ox + x, oy] = Outline;
            p[ox + x, oy + 1] = WoodLight;
            p[ox + x, oy + 2] = Wood;
            p[ox + x, oy + 3] = Wood;
            p[ox + x, oy + 4] = WoodDark;
            p[ox + x, oy + 5] = Outline;
        }

        p[ox + 2, oy + 2] = Nail;
        p[ox + 13, oy + 2] = Nail;
        // Little supports under the plank.
        for (var y = 6; y < 9; y++)
        {
            p[ox + 3, oy + y] = WoodDark;
            p[ox + 12, oy + y] = WoodDark;
        }
    }

    private static void StoneTile(Pixels p, int ox, int oy)
    {
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
        {
            var mortar = y % 8 == 7 || (x + (y / 8) * 8) % 16 == 15;
            p[ox + x, oy + y] = mortar ? StoneDark : (y % 8 == 0 ? StoneLight : Stone);
        }
    }

    private static void Tuft(Pixels p, int ox, int oy)
    {
        int[] heights = [3, 5, 4, 6, 3, 5, 2];
        for (var i = 0; i < heights.Length; i++)
        {
            var x = 2 + i * 2;
            for (var h = 0; h < heights[i]; h++)
                p[ox + x, oy + 15 - h] = h == heights[i] - 1 ? GrassLight : (i % 2 == 0 ? Grass : GrassDark);
        }
    }

    private static void Flower(Pixels p, int ox, int oy)
    {
        for (var h = 0; h < 6; h++) p[ox + 7, oy + 15 - h] = GrassDark;
        p[ox + 6, oy + 12] = Grass;
        p[ox + 8, oy + 13] = Grass;
        const uint petal = 0xFFFF7EB6, middle = 0xFFFFE07A;
        p[ox + 7, oy + 8] = middle;
        p[ox + 6, oy + 8] = petal; p[ox + 8, oy + 8] = petal; p[ox + 7, oy + 7] = petal; p[ox + 7, oy + 9] = petal;
    }

    private static void WaterTile(Pixels p, int ox, int oy, int frame)
    {
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
        {
            var wave = (x + frame * 4) % 16;
            uint c = y < 3 ? (y == 0 && (wave < 3 || wave is > 8 and < 11) ? Foam : WaterLight) : (y > 9 ? WaterDark : Water);
            if (y is > 4 and < 9 && (x * 3 + y * 5 + frame * 4) % 17 == 0) c = WaterLight;
            p[ox + x, oy + y] = c;
        }
    }

    // ----- Hero: 8 frames of 16 × 16: idle ×2, run ×4, jump, fall -----

    private static Pixels Hero()
    {
        var p = new Pixels(128, 16);
        for (var f = 0; f < 8; f++)
            HeroFrame(p, f * 16, f);
        return p;
    }

    private static void HeroFrame(Pixels p, int ox, int frame)
    {
        // Body bob: idle breathes, run bounces.
        var bob = frame switch { 1 => 1, 3 or 5 => -1, _ => 0 };
        var y0 = 2 + bob;

        // Hood.
        Fill(p, ox + 4, y0, 8, 7, Cloak);
        Fill(p, ox + 5, y0 - 1, 6, 1, Cloak);
        Fill(p, ox + 6, y0 + 2, 5, 4, Skin);              // face
        p[ox + 9, y0 + 3] = Eye; p[ox + 7, y0 + 3] = Eye;  // eyes, looking right
        p[ox + 11, y0 + 1] = CloakDark; p[ox + 12, y0 + 2] = CloakDark; // hood tip
        // Scarf.
        Fill(p, ox + 4, y0 + 7, 8, 1, Scarf);
        p[ox + 3, y0 + 7] = Scarf; p[ox + 2, y0 + 8 + (frame % 2)] = Scarf;
        // Cloak body.
        Fill(p, ox + 4, y0 + 8, 8, 4, Cloak);
        Fill(p, ox + 4, y0 + 11, 8, 1, CloakDark);

        // Legs.
        var legY = y0 + 12;
        switch (frame)
        {
            case 2: Leg(p, ox + 4, legY, 0); Leg(p, ox + 10, legY, -1); break;
            case 3: Leg(p, ox + 6, legY, 0); Leg(p, ox + 9, legY, 0); break;
            case 4: Leg(p, ox + 10, legY, 0); Leg(p, ox + 5, legY, -1); break;
            case 5: Leg(p, ox + 7, legY, 0); Leg(p, ox + 8, legY, 0); break;
            case 6: Leg(p, ox + 5, legY - 1, -1); Leg(p, ox + 9, legY - 2, -1); break; // tucked for the jump
            case 7: Leg(p, ox + 5, legY, 0); Leg(p, ox + 10, legY, 0); Fill(p, ox + 2, y0 + 6, 2, 2, Cloak); Fill(p, ox + 12, y0 + 6, 2, 2, Cloak); break; // arms out
            default: Leg(p, ox + 5, legY, 0); Leg(p, ox + 9, legY, 0); break;
        }

        OutlineSprite(p, ox, 0, 16, 16);
    }

    private static void Leg(Pixels p, int x, int y, int shorter)
    {
        for (var i = 0; i < 2 + shorter; i++)
            p[x, y + i] = Boot;
        p[x + 1, y + 1 + shorter] = Boot;
    }

    // ----- Slime: 2 frames -----

    private static Pixels SlimeSheet()
    {
        var p = new Pixels(32, 16);
        SlimeFrame(p, 0, squash: 0);
        SlimeFrame(p, 16, squash: 1);
        return p;
    }

    private static void SlimeFrame(Pixels p, int ox, int squash)
    {
        var top = 6 + squash;
        for (var y = top; y < 15; y++)
        {
            var t = (y - top) / (double)(15 - top);
            var half = (int)Math.Round(4 + 3.5 * Math.Sqrt(Math.Min(1, t * 1.6)) + squash);
            for (var x = 8 - half; x < 8 + half; x++)
                p[ox + x, y] = y > 12 ? SlimeDark : Slime;
        }

        p[ox + 5, top + 2] = SlimeLight; p[ox + 6, top + 1] = SlimeLight; p[ox + 5, top + 1] = SlimeLight;
        p[ox + 6, top + 4] = Eye; p[ox + 6, top + 5] = Eye; p[ox + 10, top + 4] = Eye; p[ox + 10, top + 5] = Eye;
        OutlineSprite(p, ox, 0, 16, 16);
    }

    // ----- Pickups and props -----

    private static Pixels Coin()
    {
        var p = new Pixels(16, 16);
        Disc(p, 7.5, 7.5, 6.2, GoldDark);
        Disc(p, 7.5, 7.5, 5.0, Gold);
        Fill(p, 7, 4, 2, 8, GoldDark);
        p[5, 4] = GoldLight; p[4, 5] = GoldLight; p[4, 6] = GoldLight;
        OutlineSprite(p, 0, 0, 16, 16);
        return p;
    }

    private static Pixels Key()
    {
        var p = new Pixels(16, 16);
        Disc(p, 4.5, 7.5, 3.6, Gold);
        Disc(p, 4.5, 7.5, 1.5, Clear);
        Fill(p, 8, 7, 7, 2, Gold);
        Fill(p, 12, 9, 1, 2, Gold);
        Fill(p, 14, 9, 1, 3, Gold);
        p[3, 5] = GoldLight; p[2, 6] = GoldLight;
        Fill(p, 8, 8, 7, 1, GoldDark);
        OutlineSprite(p, 0, 0, 16, 16);
        return p;
    }

    private static Pixels Door()
    {
        var p = new Pixels(16, 32);
        // Stone arch around a wooden door.
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 16; x++)
        {
            var dx = x - 7.5;
            var inArch = y >= 6 || dx * dx + (y - 7) * (y - 7) < 64;
            if (!inArch) continue;
            p[x, y] = (x + y) % 5 == 0 ? StoneDark : Stone;
        }

        for (var y = 4; y < 32; y++)
        for (var x = 3; x < 13; x++)
        {
            var dx = x - 7.5;
            if (y < 9 && dx * dx + (y - 9) * (y - 9) > 22) continue;
            p[x, y] = x % 3 == 0 ? WoodDark : Wood;
        }

        Fill(p, 3, 14, 10, 1, WoodDark);
        Fill(p, 3, 24, 10, 1, WoodDark);
        // Lock.
        Fill(p, 10, 17, 2, 3, Gold);
        p[10, 18] = Outline;
        OutlineSprite(p, 0, 0, 16, 32);
        return p;
    }

    private static Pixels Flag()
    {
        var p = new Pixels(16, 32);
        Fill(p, 3, 2, 2, 30, StoneLight);
        for (var y = 3; y < 13; y++)
        {
            var width = 9 - Math.Abs(y - 8) / 2;
            for (var x = 5; x < 5 + width; x++)
                p[x, y] = (x + y) % 4 == 0 ? 0xFFD8DCE6 : 0xFFF4F6FA;
        }

        OutlineSprite(p, 0, 0, 16, 32);
        return p;
    }

    private static Pixels Torch()
    {
        var p = new Pixels(32, 16);
        for (var f = 0; f < 2; f++)
        {
            var ox = f * 16;
            Fill(p, ox + 7, 9, 2, 7, WoodDark);
            Fill(p, ox + 6, 8, 4, 2, StoneDark);
            Disc(p, ox + 7.5, 5.5 - f * 0.5, 3.2, FlameDeep);
            Disc(p, ox + 7.5 + (f == 0 ? -0.5 : 0.5), 5.0 - f * 0.5, 2.3, Flame);
            Disc(p, ox + 7.5, 5.5, 1.1, FlameHot);
            p[ox + 7 + f, 1] = Flame;
        }

        return p;
    }

    private static Pixels Hills()
    {
        var p = new Pixels(128, 48);
        const uint far = 0xFF232C4A, near = 0xFF1B2238, rim = 0xFF34406A;
        for (var x = 0; x < 128; x++)
        {
            var h1 = 22 + 8 * Math.Sin(x / 128.0 * Math.PI * 2) + 4 * Math.Sin(x / 128.0 * Math.PI * 6);
            var h2 = 32 + 5 * Math.Sin(x / 128.0 * Math.PI * 4 + 1.3) + 2 * Math.Sin(x / 128.0 * Math.PI * 10);
            for (var y = (int)h1; y < 48; y++) p[x, y] = y == (int)h1 ? rim : far;
            for (var y = (int)h2; y < 48; y++) p[x, y] = near;
            // Pines on the near hills.
            if (x % 13 == 4)
                for (var t = 0; t < 7; t++)
                    for (var w = -t / 2; w <= t / 2; w++)
                        p[Math.Clamp(x + w, 0, 127), Math.Clamp((int)h2 - 7 + t, 0, 47)] = near;
        }

        return p;
    }

    private static Pixels Slash()
    {
        var p = new Pixels(32, 16);
        for (var a = -60; a <= 60; a += 2)
        {
            var r = a * Math.PI / 180;
            for (var d = 9.0; d < 14; d += 0.5)
            {
                var x = (int)Math.Round(10 + Math.Cos(r) * d);
                var y = (int)Math.Round(8 + Math.Sin(r) * d * 0.55);
                p[Math.Clamp(x, 0, 31), Math.Clamp(y, 0, 15)] = d > 12.5 ? 0xFFFFFFFF : 0xB0E6F2FF;
            }
        }

        return p;
    }

    // ----- Drawing helpers -----

    private static void Fill(Pixels p, int x, int y, int w, int h, uint c)
    {
        for (var j = y; j < y + h; j++)
        for (var i = x; i < x + w; i++)
            p[i, j] = c;
    }

    private static void Disc(Pixels p, double cx, double cy, double r, uint c)
    {
        for (var y = (int)(cy - r - 1); y <= cy + r + 1; y++)
        for (var x = (int)(cx - r - 1); x <= cx + r + 1; x++)
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                p[x, y] = c;
    }

    // A one-pixel dark outline around everything opaque in the box.
    private static void OutlineSprite(Pixels p, int ox, int oy, int w, int h)
    {
        var edge = new List<(int, int)>();
        for (var y = oy; y < oy + h; y++)
        for (var x = ox; x < ox + w; x++)
        {
            if (p[x, y] != Clear)
                continue;
            if (Opaque(x - 1, y) || Opaque(x + 1, y) || Opaque(x, y - 1) || Opaque(x, y + 1))
                edge.Add((x, y));
        }

        foreach (var (x, y) in edge)
            p[x, y] = Outline;

        bool Opaque(int x, int y) => x >= ox && x < ox + w && y >= oy && y < oy + h && p[x, y] != Clear && p[x, y] != Outline;
    }

    private static void Save(Pixels p, string path)
    {
        var bitmap = BitmapSource.Create(p.Width, p.Height, 96, 96, PixelFormats.Bgra32, null, p.Data, p.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private sealed class Pixels(int width, int height)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public uint[] Data { get; } = new uint[width * height];

        // ARGB as written above; Bgra32 in memory is the same uint on little-endian.
        public uint this[int x, int y]
        {
            get => x >= 0 && y >= 0 && x < Width && y < Height ? Data[y * Width + x] : Clear;
            set
            {
                if (x >= 0 && y >= 0 && x < Width && y < Height)
                    Data[y * Width + x] = value;
            }
        }
    }
}
#endif
