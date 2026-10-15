using Griff.Scenes;

namespace Griff.Modules.Dungeons;

/// <summary>
/// Fills its <see cref="Tilemap"/> with a dungeon made from a <see cref="Seed"/>, with no code: rooms of different sizes
/// joined by corridors, walled in. The players start in the first room, the <see cref="Exit"/> waits in the room
/// furthest from it, and the <see cref="Spawns"/> (monsters, potions, gold) are scattered through the other rooms.
/// Reaching the exit makes the next floor. The same seed always makes the same dungeon; 0 makes a new one every game.
/// </summary>
/// <remarks>
/// With <see cref="GenerateOnPlay"/> the map's tiles are replaced by the first floor as the game starts; untick it to
/// play the map as painted, with the floors after it generated. Corridors and rooms are <see cref="FloorTile"/> (or left
/// empty) and the cells around them <see cref="WallTile"/>, so a grid player steps through the rooms and corridors and
/// is stopped by the walls. Spawns go inside a "Spawns" entity in the dungeon's, which the next floor clears out. The
/// tiles and <see cref="Floor"/> are saved with the scene, so a rewind goes back to the floor it was on. In a network
/// game the host makes the floors.
/// </remarks>
[Category("Level")]
[Requires(typeof(Tilemap), Note = "the dungeon it fills")]
[WorksWith(typeof(GridMovement), Note = "on the players, to step through it tile by tile")]
[WorksWith(typeof(Counter), Note = "shows the floor (Count Floor)")]
[ComponentName("Griff.DungeonGenerator")]
public sealed class DungeonGenerator : Component, ICounterSource, IHearsMessages
{
    (int Value, int Total)? ICounterSource.CountFor(Counter counter) => counter.CountFloor ? (Floor, 0) : null;

    // The next floor is made at the start of the next frame.
    bool IHearsMessages.Hear(string message, object? data)
    {
        if (NextFloorMessage?.Trim() != message)
            return false;
        NextFloor();
        return true;
    }

    /// <summary>The name of the entity inside the dungeon's that its spawns go in.</summary>
    public const string SpawnsName = "Spawns";

    /// <summary>Picks the dungeon: the same number always makes the same one (each floor after the first its own). 0 makes a new one each game.</summary>
    public int Seed { get; set; }

    /// <summary>Make the first floor when the game starts. Untick it to play the map as painted; the floors after it are still made.</summary>
    public bool GenerateOnPlay { get; set; } = true;

    /// <summary>Cells across.</summary>
    [Group("Size and Rooms")]
    public int Width { get; set; } = 48;

    /// <summary>Cells down.</summary>
    public int Height { get; set; } = 32;

    /// <summary>How many rooms to make. Fewer fit in a small map: as many as there's room for.</summary>
    public int Rooms { get; set; } = 9;

    /// <summary>The fewest cells across or down a room's floor can be.</summary>
    public int MinRoomSize { get; set; } = 4;

    /// <summary>The most cells across or down a room's floor can be.</summary>
    public int MaxRoomSize { get; set; } = 9;

    /// <summary>Cells across a corridor.</summary>
    public int CorridorWidth { get; set; } = 1;

    /// <summary>Corridors added between rooms that are already joined, so there's more than one way round. 0 makes every way the only one.</summary>
    public int Loops { get; set; } = 2;

    /// <summary>The tile the walls are made of.</summary>
    [Group("Tiles")]
    [TileNumbers]
    public int WallTile { get; set; }

    /// <summary>
    /// The tile for the floors of rooms and corridors; -1 leaves them empty. List it in the tilemap's Background Tiles
    /// so nothing collides with it.
    /// </summary>
    [TileNumbers]
    public int FloorTile { get; set; } = -1;

    /// <summary>Fill everything outside the rooms and corridors with wall. Untick it to wall them in with a single row and leave the rest empty.</summary>
    public bool FillWalls { get; set; }

    /// <summary>Entities put in the middle of the first room as each floor is made, by name, separated by commas, e.g. "Player".</summary>
    [Group("Placing")]
    [EntityName(Many = true)]
    public string? Players { get; set; } = "Player";

    /// <summary>
    /// The entity put in the middle of the room furthest from the first, e.g. "Stairs": a player stepping onto its cell
    /// goes down to the next floor (see <see cref="DescendOnExit"/>).
    /// </summary>
    [EntityName]
    public string? Exit { get; set; }

    /// <summary>
    /// Prefabs scattered through the rooms (never the first) as each floor is made, one per line: its name and how many,
    /// e.g. "Enemy 4", "Potion 1-2" (a number picked between them), "Gold" (one).
    /// </summary>
    [PrefabCounts]
    public IReadOnlyList<string> Spawns { get; set; } = [];

    /// <summary>The floor the players are on, from 1 once the game has started; 0 before. Saved with the scene, so a rewind takes it back.</summary>
    [Group("Floors")]
    [Output]
    public int Floor { get; set; }

    /// <summary>A player stepping onto the <see cref="Exit"/>'s cell goes down to the next floor. Untick it to go down only when told to.</summary>
    public bool DescendOnExit { get; set; } = true;

    /// <summary>A message that makes the next floor when it's sent, e.g. "Next Floor" from a <see cref="Button"/> or a script. Empty for none.</summary>
    [Names(NameKind.Message, Hears = true)]
    public string? NextFloorMessage { get; set; }

    /// <summary>
    /// A message sent with 1 as its data as each floor after the first is made, e.g. for a sound player. (A
    /// <see cref="Counter"/> with Count Floor shows the floor.)
    /// </summary>
    [Names(NameKind.Message)]
    public string? FloorMessage { get; set; } = "Floor";

    /// <summary>A sound played as the players go down to the next floor, e.g. "Sounds/stairs.wav".</summary>
    public string? FloorSound { get; set; }

    /// <summary>The seed the floor was last made from: the one picked when <see cref="Seed"/> is 0, then one of its own for each floor.</summary>
    [DoNotSerialize]
    public int UsedSeed => Layout?.Seed ?? 0;

    /// <summary>The rooms of the floor last made here, the first first (where the players start). Empty before one is made.</summary>
    [DoNotSerialize]
    public IReadOnlyList<DungeonRoom> RoomList => Layout?.Rooms ?? [];

    /// <summary>The cell the <see cref="Exit"/> was put in, or null before a floor is made.</summary>
    [DoNotSerialize]
    public (int X, int Y)? ExitCell => Layout?.Exit;

    /// <summary>Raised as each floor is made, with its number.</summary>
    public event Action<int>? Generated;

    /// <summary>The room cell (<paramref name="x"/>, <paramref name="y"/>) is in, or null in a corridor, a wall or outside.</summary>
    public DungeonRoom? RoomAt(int x, int y) => RoomList.Cast<DungeonRoom?>().FirstOrDefault(r => r!.Value.Contains(x, y));

    /// <summary>Goes down to the next floor, at the start of the next frame: a new dungeon, the players in its first room.</summary>
    [Action]
    public void NextFloor() => DescendAsked = true;

    /// <summary>Makes this floor again, at the start of the next frame: a different one when <see cref="Seed"/> is 0.</summary>
    public void Generate() => RegenerateAsked = true;

    internal void RaiseGenerated(int floor) => Generated?.Invoke(floor);

    // ----- What the game keeps while it runs (rewinds start these afresh) -----

    internal DungeonLayout? Layout { get; set; }

    internal bool DescendAsked { get; set; }

    internal bool RegenerateAsked { get; set; }
}

/// <summary>A room of a <see cref="DungeonGenerator"/>'s dungeon: the cells of its floor, from its top-left cell.</summary>
public readonly record struct DungeonRoom(int X, int Y, int Width, int Height)
{
    /// <summary>The cell in its middle (the nearer the top left when it's an even number of cells).</summary>
    public (int X, int Y) Center => (X + (Width - 1) / 2, Y + (Height - 1) / 2);

    /// <summary>True if cell (<paramref name="x"/>, <paramref name="y"/>) is on its floor.</summary>
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    /// <summary>True if it and <paramref name="other"/> share a cell, or would with <paramref name="margin"/> more cells round it.</summary>
    public bool Overlaps(DungeonRoom other, int margin = 0) =>
        X - margin < other.X + other.Width && other.X < X + Width + margin &&
        Y - margin < other.Y + other.Height && other.Y < Y + Height + margin;
}

/// <summary>A dungeon made by <see cref="DungeonGen"/>: its tiles, rooms, and where everything goes.</summary>
/// <param name="Seed">The seed it was made from.</param>
/// <param name="Tiles">Its rows of tiles, top row first, in <see cref="Tilemap.Tiles"/>' format.</param>
/// <param name="Rooms">Its rooms, the first (where the players start) first.</param>
/// <param name="Players">Cells in the first room for the players, its middle first.</param>
/// <param name="Exit">The cell for the exit: the middle of the room furthest (by walking) from the first.</param>
/// <param name="Spawns">Each prefab to spawn and its cell.</param>
public sealed record DungeonLayout(
    int Seed,
    IReadOnlyList<string> Tiles,
    IReadOnlyList<DungeonRoom> Rooms,
    IReadOnlyList<(int X, int Y)> Players,
    (int X, int Y) Exit,
    IReadOnlyList<(string Prefab, int X, int Y)> Spawns);
