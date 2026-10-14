using System.Globalization;
using Griff.Scenes;

namespace Griff.Modules.BlockWorlds;

/// <summary>Makes the worlds of <see cref="WorldGenerator"/>s: the same settings and seed always give the same rows of tiles.</summary>
public static class WorldGen
{
    /// <summary>The world's rows of tiles, top row first, in <see cref="Tilemap.Tiles"/>' format.</summary>
    public static IReadOnlyList<string> Generate(WorldGenerator g, int seed)
    {
        var width = Math.Clamp(g.Width, 1, 2000);
        var height = Math.Clamp(g.Height, 1, 1000);
        var grid = new int[width, height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
                grid[x, y] = -1;
        }

        var random = new Random(seed);
        var dirtDepth = Math.Max(0, g.DirtDepth);
        var surface = Surface(g, seed, width, height);

        // Sand along the surface in patches.
        var sandy = new bool[width];
        if (g.SandTile >= 0 && g.Sand > 0)
        {
            for (var x = 0; x < width; x++)
                sandy[x] = Noise(seed + 7, x / 9f) > 1 - Math.Clamp(g.Sand, 0, 1) * 0.75f;
        }

        for (var x = 0; x < width; x++)
        {
            var dirtBottom = DirtBottom(seed, surface[x], dirtDepth, x);
            for (var y = surface[x]; y < height; y++)
            {
                var tile = y == surface[x] ? g.GrassTile : y < dirtBottom ? g.DirtTile : g.StoneTile;
                if (sandy[x] && y < surface[x] + 3)
                    tile = g.SandTile;
                grid[x, y] = tile;
            }
        }

        // Caves: long tunnels where a ridge of noise passes through, and wider caverns, more of both deeper down.
        var caves = Math.Clamp(g.Caves, 0, 1);
        if (caves > 0)
        {
            for (var x = 0; x < width; x++)
            {
                for (var y = surface[x] + 3; y < height - 1; y++)
                {
                    var depth = Math.Clamp((y - surface[x]) / (float)Math.Max(1, height - surface[x]), 0, 1);
                    var amount = caves * (0.55f + 0.9f * depth);
                    var ridge = MathF.Abs(Noise(seed + 11, x / 13f, y / 8f) - 0.5f);
                    var blob = Noise(seed + 17, x / 9f, y / 6f);
                    if (ridge < amount * 0.07f || blob > 1 - amount * 0.32f)
                        grid[x, y] = -1;
                }
            }
        }

        // Ores in veins through the stone; each ore after the first lies deeper.
        var ores = ParseOres(g.Ores);
        var stone = 0;
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
                stone += grid[x, y] == g.StoneTile ? 1 : 0;
        }

        for (var k = 0; k < ores.Count; k++)
        {
            var (tile, share) = ores[k];
            var veins = (int)MathF.Round(stone * share / 4.5f);
            for (var v = 0; v < veins; v++)
            {
                var x = random.Next(width);
                var top = surface[x] + dirtDepth + (int)((height - surface[x] - dirtDepth) * Math.Min(0.6f, 0.25f * k));
                if (top >= height - 1)
                    continue;
                var y = random.Next(top, height - 1);
                for (var step = random.Next(3, 7); step > 0; step--)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height && grid[x, y] == g.StoneTile)
                        grid[x, y] = tile;
                    x += random.Next(-1, 2);
                    y += random.Next(-1, 2);
                }
            }
        }

        // Trees on the grass, a few cells apart, kept clear of the middle where the player starts.
        if (g.TrunkTile >= 0 && g.Trees > 0)
        {
            var last = -10;
            for (var x = 2; x < width - 2; x++)
            {
                var ground = surface[x];
                if (grid[x, ground] != g.GrassTile || x - last < 4 || Math.Abs(x - width / 2) < 3 || random.NextSingle() >= g.Trees)
                    continue;
                var tall = random.Next(3, 6);
                if (ground - tall - 2 < 0)
                    continue;
                last = x;
                for (var i = 1; i <= tall; i++)
                    grid[x, ground - i] = g.TrunkTile;
                if (g.LeavesTile < 0)
                    continue;
                var crown = ground - tall;
                for (var dy = -2; dy <= 1; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        var (cx, cy) = (x + dx, crown + dy);
                        if (Math.Abs(dx) + Math.Abs(dy) > 3 || cx < 0 || cx >= width || cy < 0 || grid[cx, cy] >= 0)
                            continue;
                        grid[cx, cy] = g.LeavesTile;
                    }
                }
            }
        }

        if (g.BottomTile >= 0)
        {
            for (var x = 0; x < width; x++)
                grid[x, height - 1] = g.BottomTile;
        }

        return Rows(grid, width, height);
    }

    /// <summary>
    /// The walls behind the world's underground (see <see cref="WorldGenerator.Walls"/>): dirt under the dirt and stone
    /// under the stone, everywhere below the surface, caves included.
    /// </summary>
    public static IReadOnlyList<string> GenerateWalls(WorldGenerator g, int seed)
    {
        var width = Math.Clamp(g.Width, 1, 2000);
        var height = Math.Clamp(g.Height, 1, 1000);
        var surface = Surface(g, seed, width, height);
        var dirtDepth = Math.Max(0, g.DirtDepth);
        var grid = new int[width, height];
        for (var x = 0; x < width; x++)
        {
            var dirtBottom = DirtBottom(seed, surface[x], dirtDepth, x);
            for (var y = 0; y < height; y++)
                grid[x, y] = y <= surface[x] ? -1 : y < dirtBottom ? g.DirtTile : g.StoneTile;
        }

        return Rows(grid, width, height);
    }

    // Where the ground is in each column: two waves of smooth noise, a broad one and a smaller one on top.
    private static int[] Surface(WorldGenerator g, int seed, int width, int height)
    {
        var hillWidth = Math.Max(2, g.HillWidth);
        var surface = new int[width];
        for (var x = 0; x < width; x++)
        {
            var wave = 0.65f * Noise(seed, x / hillWidth) + 0.35f * Noise(seed + 1, x * 2.3f / hillWidth);
            var top = (int)MathF.Round(g.Surface + (wave - 0.5f) * 2 * g.Hills);
            surface[x] = Math.Clamp(top, 1, Math.Max(1, height - 2));
        }

        return surface;
    }

    private static int DirtBottom(int seed, int surface, int dirtDepth, int x) => surface + dirtDepth + (Hash(seed + 3, x, 0) < 0.5f ? 0 : 1);

    private static string[] Rows(int[,] grid, int width, int height)
    {
        var rows = new string[height];
        var row = new char[width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                row[x] = Tilemap.CharOf(grid[x, y]);
            rows[y] = new string(row).TrimEnd(Tilemap.Empty);
        }

        var used = rows.Length;
        while (used > 0 && rows[used - 1].Length == 0)
            used--;
        return rows[..used];
    }

    /// <summary>Reads <see cref="WorldGenerator.Ores"/>: "3 6%, 4 3%" (or "3 0.06"). Anything else is skipped.</summary>
    public static IReadOnlyList<(int Tile, float Share)> ParseOres(string? text)
    {
        var ores = new List<(int, float)>();
        foreach (var part in (text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length != 2 || !int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tile) ||
                tile is < 0 or >= Tilemap.MaxTiles)
                continue;
            var amount = bits[1];
            var percent = amount.EndsWith('%');
            if (!float.TryParse(percent ? amount[..^1] : amount, NumberStyles.Float, CultureInfo.InvariantCulture, out var share) || share <= 0)
                continue;
            ores.Add((tile, Math.Min(1, percent ? share / 100 : share)));
        }

        return ores;
    }

    // A steady random number from 0 to 1 for each seed and cell.
    private static float Hash(int seed, int x, int y)
    {
        unchecked
        {
            var h = (uint)seed * 374761393u + (uint)x * 668265263u + (uint)y * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    // Smooth noise along a line, 0 to 1.
    private static float Noise(int seed, float x)
    {
        var i = (int)MathF.Floor(x);
        var t = Smooth(x - i);
        return Hash(seed, i, 0) * (1 - t) + Hash(seed, i + 1, 0) * t;
    }

    // Smooth noise over a plane, 0 to 1.
    private static float Noise(int seed, float x, float y)
    {
        var (i, j) = ((int)MathF.Floor(x), (int)MathF.Floor(y));
        var (tx, ty) = (Smooth(x - i), Smooth(y - j));
        var top = Hash(seed, i, j) * (1 - tx) + Hash(seed, i + 1, j) * tx;
        var bottom = Hash(seed, i, j + 1) * (1 - tx) + Hash(seed, i + 1, j + 1) * tx;
        return top * (1 - ty) + bottom * ty;
    }
}
