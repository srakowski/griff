using System.Globalization;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>Makes the dungeons of <see cref="DungeonGenerator"/>s: the same settings and seed always give the same one.</summary>
public static class DungeonGen
{
    // Tries at placing each room before settling for fewer.
    private const int TriesPerRoom = 30;

    /// <summary>The seed floor <paramref name="floor"/> (from 1) of the dungeon started from <paramref name="seed"/> is made from.</summary>
    public static int FloorSeed(int seed, int floor) => floor <= 1 ? seed : unchecked(seed + (floor - 1) * 1_000_003);

    /// <summary>Makes a dungeon from <paramref name="seed"/>.</summary>
    public static DungeonLayout Generate(DungeonGenerator g, int seed)
    {
        var width = Math.Clamp(g.Width, 5, 1000);
        var height = Math.Clamp(g.Height, 5, 1000);
        var random = new Random(seed);
        var floor = new bool[width, height];
        var rooms = PlaceRooms(g, random, width, height);
        foreach (var room in rooms)
            Carve(floor, room.X, room.Y, room.X + room.Width - 1, room.Y + room.Height - 1);

        // Every room joined to the tree of rooms by its nearest (so the corridors are short), then a few more for loops.
        var joined = new HashSet<(int, int)>();
        var corridor = Math.Clamp(g.CorridorWidth, 1, Math.Max(1, Math.Min(width, height) - 2));
        var inTree = new List<int> { 0 };
        while (inTree.Count < rooms.Count)
        {
            var (from, to) = inTree
                .SelectMany(a => Enumerable.Range(0, rooms.Count).Where(b => !inTree.Contains(b)).Select(b => (a, b)))
                .MinBy(p => Distance(rooms[p.a], rooms[p.b]));
            Join(floor, rooms[from], rooms[to], corridor, random, width, height);
            joined.Add((Math.Min(from, to), Math.Max(from, to)));
            inTree.Add(to);
        }

        for (var i = 0; i < Math.Max(0, g.Loops) && rooms.Count > 2; i++)
        {
            var from = random.Next(rooms.Count);
            var to = Enumerable.Range(0, rooms.Count)
                .Where(b => b != from && !joined.Contains((Math.Min(from, b), Math.Max(from, b))))
                .OrderBy(b => Distance(rooms[from], rooms[b])).Cast<int?>().FirstOrDefault();
            if (to is not { } other)
                continue;
            Join(floor, rooms[from], rooms[other], corridor, random, width, height);
            joined.Add((Math.Min(from, other), Math.Max(from, other)));
        }

        var start = rooms[0];
        var players = start.Cells().OrderBy(c => Math.Abs(c.X - start.Center.X) + Math.Abs(c.Y - start.Center.Y))
            .ThenBy(c => c.Y).ThenBy(c => c.X).ToList();

        // The exit goes in the middle of the room the longest walk from the first.
        var steps = Walk(floor, start.Center, width, height);
        var exitRoom = rooms.Count > 1 ? rooms.Skip(1).MaxBy(r => steps[r.Center.X, r.Center.Y]) : start;
        var exit = exitRoom.Center;
        if (rooms.Count == 1)
            exit = players[^1];

        // Spawns go on room floors away from the first room (unless it's the only one), the exit and each other.
        var free = rooms.Skip(rooms.Count > 1 ? 1 : 0).SelectMany(r => r.Cells())
            .Where(c => c != exit && (rooms.Count > 1 || c != players[0])).ToList();
        Shuffle(free, random);
        var spawns = new List<(string Prefab, int X, int Y)>();
        foreach (var (prefab, count) in ParseSpawns(g.Spawns, random))
        {
            for (var i = 0; i < count && free.Count > 0; i++)
            {
                var (x, y) = free[^1];
                free.RemoveAt(free.Count - 1);
                spawns.Add((prefab, x, y));
            }
        }

        return new DungeonLayout(seed, Tiles(g, floor, width, height), rooms, players, exit, spawns);
    }

    /// <summary>
    /// Reads <see cref="DungeonGenerator.Spawns"/>: each line a prefab's name and how many ("Enemy 3"), a range picked
    /// from ("Potion 1-2"), or just the name for one. Blank lines are skipped.
    /// </summary>
    public static IReadOnlyList<(string Prefab, int Count)> ParseSpawns(IReadOnlyList<string>? lines, Random random)
    {
        var spawns = new List<(string, int)>();
        foreach (var line in lines ?? [])
        {
            var text = line.Trim();
            if (text.Length == 0)
                continue;
            var space = text.LastIndexOf(' ');
            var (name, count) = space > 0 && Count(text[(space + 1)..], random) is { } n ? (text[..space].Trim(), n) : (text, 1);
            if (name.Length > 0 && count > 0)
                spawns.Add((name, count));
        }

        return spawns;
    }

    // "3" or "1-4" (a number picked between them), or null if it's neither.
    private static int? Count(string text, Random random)
    {
        var dash = text.IndexOf('-', 1);
        if (dash < 0)
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(0, n) : null;
        if (!int.TryParse(text[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var low) ||
            !int.TryParse(text[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var high))
            return null;
        (low, high) = (Math.Max(0, Math.Min(low, high)), Math.Max(0, Math.Max(low, high)));
        return random.Next(low, high + 1);
    }

    // Rooms of random sizes at random places, each with a wall's width between it and the others and the map's edge.
    private static List<DungeonRoom> PlaceRooms(DungeonGenerator g, Random random, int width, int height)
    {
        var most = Math.Max(1, Math.Min(g.MaxRoomSize, Math.Min(width, height) - 2));
        var least = Math.Clamp(g.MinRoomSize, 1, most);
        var rooms = new List<DungeonRoom>();
        var wanted = Math.Max(1, g.Rooms);
        for (var tries = 0; rooms.Count < wanted && tries < wanted * TriesPerRoom; tries++)
        {
            var w = random.Next(least, most + 1);
            var h = random.Next(least, most + 1);
            if (w > width - 2 || h > height - 2)
                continue;
            var room = new DungeonRoom(random.Next(1, width - w), random.Next(1, height - h), w, h);
            if (!rooms.Any(r => r.Overlaps(room, margin: 1)))
                rooms.Add(room);
        }

        if (rooms.Count == 0)
            rooms.Add(new DungeonRoom(1, 1, Math.Min(least, width - 2), Math.Min(least, height - 2)));
        return rooms;
    }

    private static int Distance(DungeonRoom a, DungeonRoom b) =>
        Math.Abs(a.Center.X - b.Center.X) + Math.Abs(a.Center.Y - b.Center.Y);

    // An L-shaped corridor from one room's middle to the other's, across first or down first at random.
    private static void Join(bool[,] floor, DungeonRoom a, DungeonRoom b, int corridor, Random random, int width, int height)
    {
        var (from, to) = (a.Center, b.Center);
        var bend = random.Next(2) == 0 ? (to.X, from.Y) : (from.X, to.Y);
        Line(floor, from, bend, corridor, width, height);
        Line(floor, bend, to, corridor, width, height);
    }

    // A straight run of corridor, so many cells wide (spreading right and down), kept off the map's edge.
    private static void Line(bool[,] floor, (int X, int Y) from, (int X, int Y) to, int corridor, int width, int height)
    {
        var (x0, x1) = (Math.Min(from.X, to.X), Math.Max(from.X, to.X) + corridor - 1);
        var (y0, y1) = (Math.Min(from.Y, to.Y), Math.Max(from.Y, to.Y) + corridor - 1);
        Carve(floor, Math.Max(1, x0), Math.Max(1, y0), Math.Min(width - 2, x1), Math.Min(height - 2, y1));
    }

    private static void Carve(bool[,] floor, int x0, int y0, int x1, int y1)
    {
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
                floor[x, y] = true;
        }
    }

    // Steps from a cell to every floor cell, four ways; -1 where it can't be reached.
    private static int[,] Walk(bool[,] floor, (int X, int Y) from, int width, int height)
    {
        var steps = new int[width, height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
                steps[x, y] = -1;
        }

        var queue = new Queue<(int X, int Y)>();
        steps[from.X, from.Y] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (nx < 0 || ny < 0 || nx >= width || ny >= height || !floor[nx, ny] || steps[nx, ny] >= 0)
                    continue;
                steps[nx, ny] = steps[x, y] + 1;
                queue.Enqueue((nx, ny));
            }
        }

        return steps;
    }

    private static void Shuffle<T>(List<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // Floors as the floor tile (or empty), walls round them (every cell touching a floor, corners too) or everywhere else.
    private static IReadOnlyList<string> Tiles(DungeonGenerator g, bool[,] floor, int width, int height)
    {
        char Char(int tile) => tile >= 0 && tile < Tilemap.MaxTiles ? Tilemap.CharOf(tile) : Tilemap.Empty;
        var (wall, ground) = (Char(g.WallTile), Char(g.FloorTile));
        var rows = new List<string>(height);
        var row = new char[width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                row[x] = floor[x, y] ? ground : g.FillWalls || NextToFloor(floor, x, y, width, height) ? wall : Tilemap.Empty;
            rows.Add(new string(row));
        }

        return rows;
    }

    private static bool NextToFloor(bool[,] floor, int x, int y, int width, int height)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var (nx, ny) = (x + dx, y + dy);
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && floor[nx, ny])
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<(int X, int Y)> Cells(this DungeonRoom room)
    {
        for (var y = room.Y; y < room.Y + room.Height; y++)
        {
            for (var x = room.X; x < room.X + room.Width; x++)
                yield return (x, y);
        }
    }
}
