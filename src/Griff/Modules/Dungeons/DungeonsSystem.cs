using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Dungeons;

/// <summary>
/// Runs dungeons with no code (<see cref="DungeonGenerator"/>): floors of rooms and corridors made from a seed, the players
/// put in the first room, the exit in the furthest, and spawns scattered through the rest; stepping onto the exit makes
/// the next floor. The host makes them.
/// </summary>
internal sealed class DungeonsSystem : GameSystem
{
    public override string Name => "Dungeons";

    // Before anything starts or moves, so scripts starting this frame find the floor made and the players stand in it.
    internal void Run()
    {
        foreach (var dungeon in Game.Active<DungeonGenerator>())
        {
            if (dungeon.Entity!.GetComponent<Tilemap>() is not { } map)
                continue;
            if (dungeon.Floor <= 0)
            {
                dungeon.Floor = 1;
                dungeon.DescendAsked = dungeon.RegenerateAsked = false;
                if (dungeon.GenerateOnPlay)
                    MakeFloor(dungeon, map, NewSeed(dungeon));
            }
            else if (dungeon.DescendAsked || (dungeon.DescendOnExit && SomeoneAtExit(dungeon, map)))
            {
                Descend(dungeon, map);
            }
            else if (dungeon.RegenerateAsked)
            {
                dungeon.RegenerateAsked = false;
                MakeFloor(dungeon, map, dungeon.Seed != 0 ? DungeonGen.FloorSeed(dungeon.Seed, dungeon.Floor) : NewSeed(dungeon));
            }
        }
    }

    // The seed a new game's dungeon starts from: its own, or one picked now from the game's Rand (so a check that seeds Rand
    // gets the same crypt on the desktop and in the browser).
    private static int NewSeed(DungeonGenerator dungeon) => dungeon.Seed != 0 ? dungeon.Seed : Rand.Range(1, int.MaxValue);

    // The next floor: made from the dungeon's seed and the floor's number (or a fresh seed once a rewind forgot the last).
    private void Descend(DungeonGenerator dungeon, Tilemap map)
    {
        dungeon.DescendAsked = dungeon.RegenerateAsked = false;
        dungeon.Floor++;
        var seed = dungeon.Seed != 0 ? DungeonGen.FloorSeed(dungeon.Seed, dungeon.Floor)
            : dungeon.Layout is { } last ? DungeonGen.FloorSeed(last.Seed, 2) : NewSeed(dungeon);
        MakeFloor(dungeon, map, seed);
        Game.PlayIfSet(dungeon.FloorSound);
        if (!string.IsNullOrWhiteSpace(dungeon.FloorMessage))
            Game.Send(dungeon.FloorMessage.Trim(), 1, from: dungeon.Entity);
    }

    // A player on the exit's cell: a grid mover once it has glided onto it, or as it steps on from it (walking on with a
    // way held, it never stands still there).
    private bool SomeoneAtExit(DungeonGenerator dungeon, Tilemap map)
    {
        if (string.IsNullOrWhiteSpace(dungeon.Exit) || Game.Find(dungeon.Exit.Trim()) is not { } exit)
            return false;
        var grid = GridCells.Of(map);
        var at = grid.CellAt(Game.Flat(exit.Transform.WorldPosition));
        return PlayersOf(dungeon).Any(p => p.GetComponent<GridMovement>() is { Started: true } m
            ? (m.Stepping ? grid.CellAt(m.From) : m.Cell) == at
            : grid.CellAt(Game.Flat(p.Transform.WorldPosition)) == at);
    }

    private IEnumerable<Entity> PlayersOf(DungeonGenerator dungeon) =>
        (dungeon.Players ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(Game.FindAll).Distinct();

    // Lays the floor's tiles, clears the last floor's spawns and spawns this one's, and puts the players and the exit in place.
    private void MakeFloor(DungeonGenerator dungeon, Tilemap map, int seed)
    {
        var layout = DungeonGen.Generate(dungeon, seed);
        dungeon.Layout = layout;
        map.Tiles = layout.Tiles;
        var grid = GridCells.Of(map);

        var holder = dungeon.Entity!.Children.FirstOrDefault(c => c.Name == DungeonGenerator.SpawnsName && c.GetComponent<Tilemap>() is null);
        if (holder is null)
            holder = Game.Spawn(new Entity(DungeonGenerator.SpawnsName), dungeon.Entity);
        foreach (var child in holder.Children.ToList())
            Game.Destroy(child);
        foreach (var (prefab, x, y) in layout.Spawns)
            Game.Spawn(prefab, map.CellCenter(x, y), holder);

        var players = PlayersOf(dungeon).ToList();
        for (var i = 0; i < players.Count; i++)
            PlaceOnCell(players[i], grid.Center(layout.Players[Math.Min(i, layout.Players.Count - 1)]));
        if (!string.IsNullOrWhiteSpace(dungeon.Exit) && Game.Find(dungeon.Exit.Trim()) is { } exit)
            PlaceOnCell(exit, grid.Center(layout.Exit));
        dungeon.RaiseGenerated(dungeon.Floor);
    }

    // Moves something straight to a spot, a grid mover dropping the step it was on and starting again from the new cell.
    private static void PlaceOnCell(Entity entity, Vector2 world)
    {
        Game.MoveTo(entity, world);
        if (entity.GetComponent<Body>() is { } body)
            body.Velocity = Vector2.Zero;
        if (entity.GetComponent<GridMovement>() is { } m)
            (m.Started, m.Stepping, m.Queued, m.Buffered) = (false, false, null, null);
    }
}
