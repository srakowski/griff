using System.Globalization;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Modules.BlockWorlds;

/// <summary>
/// Fills its <see cref="Tilemap"/> with a world made from a <see cref="Seed"/>: rolling hills of grass over dirt, stone
/// below with caves and veins of ore, sand, trees, and a floor that can't be dug through. The same seed always makes the
/// same world; 0 makes a new one every time.
/// </summary>
/// <remarks>
/// With <see cref="GenerateOnPlay"/> the map's tiles are replaced by a fresh world each time the game starts (unless a
/// saved world comes back, see <see cref="Blocks.SaveAs"/>); untick it to keep the world as painted. Tile numbers are the
/// map's tileset's: -1 leaves a feature out.
/// </remarks>
[Category("Blocks")]
[Requires(typeof(Tilemap), Note = "the world it fills")]
[WorksWith(typeof(Blocks), Note = "so the world can be dug up and built on")]
[ComponentName("Griff.WorldGenerator")]
public sealed class WorldGenerator : Component
{
    /// <summary>Picks the world: the same number always makes the same one. 0 makes a new world each time.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Make the world when the game starts. Untick it to keep (and paint on) the map as it is.</summary>
    public bool GenerateOnPlay { get; set; } = true;

    /// <summary>Cells across.</summary>
    [Group("Size and Shape")]
    public int Width { get; set; } = 120;

    /// <summary>Cells down, sky included.</summary>
    public int Height { get; set; } = 60;

    /// <summary>Rows of sky above the ground, on average.</summary>
    public int Surface { get; set; } = 18;

    /// <summary>How many cells the ground rises and falls.</summary>
    public float Hills { get; set; } = 5;

    /// <summary>About how many cells from one hilltop to the next.</summary>
    public float HillWidth { get; set; } = 28;

    /// <summary>The tile on top of the ground.</summary>
    [Group("Ground")]
    public int GrassTile { get; set; } = 0;

    /// <summary>The tile under the grass, <see cref="DirtDepth"/> cells deep.</summary>
    public int DirtTile { get; set; } = 1;

    /// <summary>The tile under the dirt, all the way down.</summary>
    public int StoneTile { get; set; } = 2;

    /// <summary>How many cells of dirt lie under the grass before the stone starts.</summary>
    public int DirtDepth { get; set; } = 5;

    /// <summary>How much of the underground is caves, from 0 (solid) to 1 (mostly hollow). 0.3 is plenty.</summary>
    [Group("Underground")]
    public float Caves { get; set; } = 0.3f;

    /// <summary>
    /// Ores in the stone: a tile and how much of the stone it makes up, separated by commas, e.g. "3 6%, 4 3%". Each
    /// comes in little veins; the second and later ores lie deeper.
    /// </summary>
    public string? Ores { get; set; } = "3 6%, 4 3%";

    /// <summary>Patches of sand along the surface, from 0 (none) to 1 (everywhere).</summary>
    [Group("Sand and Trees")]
    public float Sand { get; set; } = 0.15f;

    /// <summary>The tile for patches of sand; -1 for none.</summary>
    public int SandTile { get; set; } = -1;

    /// <summary>How often a tree grows on the grass, from 0 (never) to 1 (wherever there's room).</summary>
    public float Trees { get; set; } = 0.15f;

    /// <summary>The tile for tree trunks; -1 for no trees.</summary>
    public int TrunkTile { get; set; } = -1;

    /// <summary>The tile for treetops; -1 for none.</summary>
    public int LeavesTile { get; set; } = -1;

    /// <summary>A tile for the bottom row, usually one that's unbreakable (see <see cref="Blocks.Unbreakable"/>).</summary>
    [Group("More")]
    public int BottomTile { get; set; } = -1;

    /// <summary>
    /// The name of another tilemap to fill with the walls of the underground, drawn behind the world: what caves and
    /// tunnels show instead of the sky. Give it the same position and tileset, no collisions (untick Solid), a darker
    /// tint, and a lower Order.
    /// </summary>
    [EntityName]
    public string? Walls { get; set; }

    /// <summary>
    /// Entities to stand on the ground in the middle of the world once it's made, by name, separated by commas, e.g.
    /// "Player".
    /// </summary>
    [EntityName(Many = true)]
    public string? Spawn { get; set; } = "Player";

    /// <summary>The seed the world was last made from: the one picked when <see cref="Seed"/> is 0.</summary>
    [DoNotSerialize]
    public int UsedSeed { get; internal set; }
}

/// <summary>
/// Makes a <see cref="Tilemap"/> a world of blocks to dig up and build with: what each tile gives when it's mined, what
/// each item puts down, how long blocks take to break, blocks that can't be broken, sand that falls, and torches that
/// glow. A <see cref="BlockBuilder"/> on the player does the digging and building.
/// </summary>
[Category("Blocks")]
[Requires(typeof(Tilemap), Note = "the blocks")]
[WorksWith(typeof(WorldGenerator), Note = "to make the world")]
[WorksWith(typeof(BlockBuilder), Note = "on the player, to dig and build")]
[ComponentName("Griff.Blocks")]
public sealed class Blocks : Component, ILightSource
{
    private IReadOnlyList<string> _items = [];

    // Its glowing blocks light up the dark around them.
    void ILightSource.AddLights(List<LightSpot> lights)
    {
        if (string.IsNullOrWhiteSpace(GlowingTiles) || Entity!.GetComponent<Tilemap>() is not { } map || !(GlowRadius > 0))
            return;
        var world = Entity.Transform.WorldMatrix;
        var scale = new System.Numerics.Vector2(world.M11, world.M12).Length();
        foreach (var (x, y, tile) in map.Cells())
        {
            if (Glows(tile))
                lights.Add(new LightSpot(System.Numerics.Vector2.Transform(map.CellCenter(x, y), world), GlowRadius * scale, GlowColor, 1));
        }
    }
    private Dictionary<int, string> _drops = [];
    private string? _dropsText;
    private string? _hardness;
    private List<(bool[] Tiles, float Seconds)> _hardnessRuns = [];
    private string? _unbreakable;
    private bool[] _unbreakableSet = [];
    private string? _falling;
    private bool[] _fallingSet = [];
    private string? _glowing;
    private bool[] _glowingSet = [];
    private Dictionary<string, int> _places = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, string> _names = [];

    /// <summary>
    /// The blocks the player can carry and put down, one per row: the item's name and its tile, e.g. "Dirt 1". Mining a
    /// tile gives the item named for it here, unless <see cref="Drops"/> says otherwise.
    /// </summary>
    public IReadOnlyList<string> Items
    {
        get => _items;
        set
        {
            _items = value?.ToArray() ?? [];
            _places = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _names = [];
            foreach (var row in _items)
            {
                var text = row.Trim();
                var space = text.LastIndexOf(' ');
                if (space <= 0 || !int.TryParse(text[(space + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tile) ||
                    tile is < 0 or >= Tilemap.MaxTiles)
                    continue;
                var name = text[..space].Trim();
                _places.TryAdd(name, tile);
                _names.TryAdd(tile, name);
            }
        }
    }

    /// <summary>
    /// Tiles that give something else when mined: a tile and an item, separated by commas, e.g. "0 Dirt, 6 Sapling".
    /// "6 nothing" gives nothing.
    /// </summary>
    public string? Drops
    {
        get => _dropsText;
        set
        {
            _dropsText = value;
            _drops = [];
            foreach (var part in (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var space = part.IndexOf(' ');
                if (space > 0 && int.TryParse(part[..space], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tile))
                    _drops[tile] = part[(space + 1)..].Trim();
            }
        }
    }

    /// <summary>Seconds it takes to break a block, unless <see cref="Hardness"/> says otherwise.</summary>
    public float MineTime { get; set; } = 0.35f;

    /// <summary>
    /// Blocks that take longer (or less long) to break: tiles and seconds, separated by commas, e.g. "2 0.8, 3-4 1.2".
    /// </summary>
    public string? Hardness
    {
        get => _hardness;
        set
        {
            _hardness = value;
            _hardnessRuns = [];
            foreach (var part in (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var space = part.LastIndexOf(' ');
                if (space > 0 && float.TryParse(part[(space + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    _hardnessRuns.Add((Tilemap.ParseTileSet(part[..space]), seconds));
            }
        }
    }

    /// <summary>Tiles that can't be broken, like the bottom of the world: tile numbers and ranges, e.g. "9".</summary>
    public string? Unbreakable
    {
        get => _unbreakable;
        set => (_unbreakable, _unbreakableSet) = (value, Tilemap.ParseTileSet(value));
    }

    /// <summary>Tiles that fall when there's nothing under them, like sand and gravel: tile numbers and ranges.</summary>
    public string? FallingTiles
    {
        get => _falling;
        set => (_falling, _fallingSet) = (value, Tilemap.ParseTileSet(value));
    }

    /// <summary>
    /// Tiles that give off light, like torches and lava: tile numbers and ranges. They light up a dark scene (a camera
    /// with an <see cref="Camera.Ambient"/> light) like a <see cref="Light"/> would.
    /// </summary>
    public string? GlowingTiles
    {
        get => _glowing;
        set => (_glowing, _glowingSet) = (value, Tilemap.ParseTileSet(value));
    }

    /// <summary>How far the light of a glowing tile reaches, in world units.</summary>
    [Group("Light")]
    public float GlowRadius { get; set; } = 5;

    /// <summary>The color of the light glowing tiles give off.</summary>
    public Color GlowColor { get; set; } = Color.FromBytes(0xFF, 0xC8, 0x7A);

    /// <summary>
    /// How dark it gets underground: as the camera goes below the ground (where it was when the game started), its light
    /// fades to this color, and torches and other glowing blocks light the way.
    /// Transparent keeps it bright everywhere.
    /// </summary>
    public Color Underground { get; set; } = Color.Transparent;

    /// <summary>How far below the surface it takes to get fully dark, in world units.</summary>
    public float DarkDepth { get; set; } = 8;

    /// <summary>
    /// A name to keep the world under in the game's save data, e.g. "World": what was dug and built comes back next time
    /// the game runs, instead of a new world. Empty starts over each time.
    /// </summary>
    [Group("Saving")]
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveAs { get; set; }

    /// <summary>A prefab spawned where a block breaks, e.g. a puff of dust.</summary>
    [Names(NameKind.Prefab)]
    [Group("Sounds and Effects")]
    public string? BreakEffect { get; set; }

    /// <summary>A sound played while digging, each time a block cracks a little more.</summary>
    public string? DigSound { get; set; }

    /// <summary>A sound played when a block breaks.</summary>
    public string? BreakSound { get; set; }

    /// <summary>A sound played when a block is put down.</summary>
    public string? PlaceSound { get; set; }

    /// <summary>The item mining <paramref name="tile"/> gives, or null for nothing.</summary>
    public string? DropOf(int tile)
    {
        if (_drops.TryGetValue(tile, out var drop))
            return drop.Equals("nothing", StringComparison.OrdinalIgnoreCase) || drop.Length == 0 ? null : drop;
        return _names.GetValueOrDefault(tile);
    }

    /// <summary>The tile <paramref name="item"/> puts down, or -1 if it isn't a block.</summary>
    public int TileOf(string? item) => item is not null && _places.TryGetValue(item.Trim(), out var tile) ? tile : -1;

    /// <summary>The name of the item that puts down <paramref name="tile"/>, or null.</summary>
    public string? NameOf(int tile) => _names.GetValueOrDefault(tile);

    /// <summary>Seconds it takes to break <paramref name="tile"/>: its <see cref="Hardness"/>, or <see cref="MineTime"/>.</summary>
    public float TimeToBreak(int tile)
    {
        foreach (var (tiles, seconds) in _hardnessRuns)
        {
            if (Tilemap.InSet(tiles, tile))
                return Math.Max(0, seconds);
        }

        return Math.Max(0, MineTime);
    }

    public bool IsUnbreakable(int tile) => Tilemap.InSet(_unbreakableSet, tile);

    public bool Falls(int tile) => Tilemap.InSet(_fallingSet, tile);

    public bool Glows(int tile) => Tilemap.InSet(_glowingSet, tile);

    // The game's bookkeeping: falling blocks to look at, when the world last changed, and when it was last saved.
    internal HashSet<(int X, int Y)> Unsettled { get; } = [];

    internal float FallClock { get; set; }

    // The tiles as last saved (or loaded): any change, by digging, building or a script, replaces the map's tiles.
    internal IReadOnlyList<string>? SavedTiles { get; set; }

    internal float SinceSaved { get; set; }

    internal bool Started { get; set; }

    // The first solid row of each column as the world began, for how dark it is underground.
    internal int[] Surface { get; set; } = [];
}

/// <summary>
/// Digs up and puts down the blocks of a <see cref="Blocks"/> world with the mouse: hold the left button on a block to
/// mine it (it goes in the <see cref="Inventory"/>), and click the right button to put down the <see cref="Item"/> (the
/// one picked on the <see cref="Hotbar"/>). In first person it works on the block straight ahead.
/// </summary>
/// <remarks>Blocks go down next to another block, never in mid-air or on top of someone.</remarks>
[Category("Blocks")]
[WorksWith(typeof(Hotbar), Note = "to pick which block to put down")]
[WorksWith(typeof(Inventory), Note = "holds what's mined (one is added if there's none)")]
[ComponentName("Griff.BlockBuilder")]
public sealed class BlockBuilder : Component
{
    /// <summary>How far away it can reach a block, in world units from the middle of the entity.</summary>
    [Distance]
    public float Reach { get; set; } = 5;

    /// <summary>How fast it digs: 2 breaks blocks in half the time.</summary>
    public float MineSpeed { get; set; } = 1;

    /// <summary>Whether it can dig blocks out (with Attack).</summary>
    public bool CanMine { get; set; } = true;

    /// <summary>Whether it can put blocks down.</summary>
    public bool CanBuild { get; set; } = true;

    /// <summary>
    /// Untick it to have it ignore the mouse (no digging or building, and its hotbar and crafting ignore their keys):
    /// another part of the scene has the player's input. Its Focus action ticks it and unticks it on everything else the
    /// player plays, and another's Focus takes it back.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>The item it puts down. The <see cref="Hotbar"/> sets it to whatever's picked there.</summary>
    public string? Item { get; set; } = "Dirt";

    /// <summary>The name of the world's entity. Empty uses the first <see cref="Blocks"/> map in the scene.</summary>
    [EntityName]
    public string? World { get; set; }

    /// <summary>The color of the outline around the block it's pointing at. Transparent hides it.</summary>
    public Color Highlight { get; set; } = new(1, 1, 1, 0.7f);

    /// <summary>The block it's pointing at, if any, as a cell of the world's map.</summary>
    [DoNotSerialize]
    public (int X, int Y)? Target { get; internal set; }

    /// <summary>The empty cell it would put a block in, if any.</summary>
    [DoNotSerialize]
    public (int X, int Y)? PlaceAt { get; internal set; }

    /// <summary>How far through breaking <see cref="Target"/> it is, from 0 to 1.</summary>
    [DoNotSerialize]
    [Sent]
    public float Progress
    {
        get => _progress.Value;
        internal set => _progress.Set(this, value);
    }

    // Kept as it was when the step began too, so digging is drawn between steps (and starting over as it is).
    private Smoothed<float> _progress;

    // How far through breaking it is in a frame drawn as `blend` says.
    internal float ProgressDrawn(StepBlend blend) => _progress.Value >= _progress.Was ? blend.Of(_progress) : _progress.Value;

    internal Tilemap? Map { get; set; }

    internal float PlaceCooldown { get; set; }

    [Sent]
    internal int Cracks { get; set; }
}

/// <summary>
/// A row of slots along the bottom of the screen showing what the <see cref="Inventory"/> holds, with how many. Pick a
/// slot with the number keys or the mouse wheel; a <see cref="BlockBuilder"/> puts down the picked block.
/// </summary>
[Category("Blocks")]
[Requires(typeof(Inventory), Note = "what it shows")]
[WorksWith(typeof(BlockBuilder), Note = "puts down what's picked")]
[ComponentName("Griff.Hotbar")]
public sealed class Hotbar : Component, IClaimsNumberKeys
{
    /// <summary>How many slots it has, up to 10.</summary>
    public int Slots { get; set; } = 9;

    /// <summary>The picked slot, counting from 0.</summary>
    public int Selected { get; set; }

    /// <summary>Item colors for the slots, one per row: a name and a color, e.g. "Dirt #8B5A2B". Others get a color from their name.</summary>
    public IReadOnlyList<string> Colors { get; set; } = [];

    /// <summary>
    /// What's in each slot and how many: each item keeps its slot until it runs out, and something new takes the first
    /// empty one. An empty slot's item is null.
    /// </summary>
    [DoNotSerialize]
    public IReadOnlyList<(string? Item, int Count)> Shown { get; internal set; } = [];

    /// <summary>The item in the picked slot, or null if it's empty.</summary>
    [Output]
    public string? SelectedItem => Selected >= 0 && Selected < Shown.Count ? Shown[Selected].Item : null;

    /// <summary>How many of the picked slot's item it holds: the blocks left to place.</summary>
    [DoNotSerialize]
    [Output]
    public int SelectedCount => Selected >= 0 && Selected < Shown.Count ? Shown[Selected].Count : 0;

    /// <summary>How many of <paramref name="item"/> (e.g. "Dirt") its slots hold. Read as "Blocks:Dirt".</summary>
    [Output("Blocks", Example = "Dirt")]
    public int CountOf(string item)
    {
        var name = item.AsSpan().Trim();
        var count = 0;
        var shown = Shown;
        for (var i = 0; i < shown.Count; i++)
        {
            if (shown[i].Item.AsSpan().Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                count += shown[i].Count;
        }

        return count;
    }

    /// <summary>The color of <paramref name="item"/>'s slot: from <see cref="Colors"/>, or picked from its name.</summary>
    public Color ColorOf(string item)
    {
        foreach (var row in Colors)
        {
            var text = row.Trim();
            var space = text.LastIndexOf(' ');
            if (space > 0 && text[..space].Trim().Equals(item, StringComparison.OrdinalIgnoreCase) && Color.TryParse(text[(space + 1)..], out var color))
                return color;
        }

        // A steady color from the name, so the same item always looks the same.
        var hash = 17;
        foreach (var c in item.ToLowerInvariant())
            hash = hash * 31 + c;
        var hue = (hash & 0xFFFF) / 65535f * 6;
        var x = 1 - MathF.Abs(hue % 2 - 1);
        var (r, g, b) = (int)hue switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
        // Muted: mostly gray, a little of the hue.
        return new Color(0.35f + 0.4f * r, 0.35f + 0.4f * g, 0.35f + 0.4f * b);
    }
}

/// <summary>
/// Makes things from what the <see cref="Inventory"/> holds. Press <see cref="Key"/> (C) to open the list of
/// <see cref="Recipes"/> and click one to make it; the ones there's enough for are lit up.
/// </summary>
/// <remarks>
/// A recipe is what it makes (and how many), "=", then what it takes joined with "+": "Torch 4 = Wood + Coal". Add
/// "@ Workbench" to need a workbench nearby: a placed block of that item, or an entity of that name.
/// </remarks>
[Category("Blocks")]
[Requires(typeof(Inventory), Note = "what it makes things from")]
[ComponentName("Griff.Crafting")]
public sealed class Crafting : Component
{
    private IReadOnlyList<string> _recipes = [];
    private IReadOnlyList<Recipe> _parsed = [];

    /// <summary>One recipe per row, e.g. "Planks 4 = Wood" or "Furnace = Stone 8 @ Workbench".</summary>
    public IReadOnlyList<string> Recipes
    {
        get => _recipes;
        set
        {
            _recipes = value?.ToArray() ?? [];
            _parsed = _recipes.Select(Recipe.Parse).OfType<Recipe>().ToList();
        }
    }

    /// <summary>The key that opens and closes the list.</summary>
    public Key Key { get; set; } = Key.C;

    /// <summary>How close a recipe's workbench has to be, in world units.</summary>
    [Distance]
    public float StationReach { get; set; } = 4;

    /// <summary>True while the list is open.</summary>
    [Group("State")]
    public bool IsOpen { get; set; }

    /// <summary>A sound played when something is made.</summary>
    public string? Sound { get; set; }

    /// <summary>The recipes, read from <see cref="Recipes"/>; rows that don't make sense are left out.</summary>
    public IReadOnlyList<Recipe> Parsed => _parsed;
}

/// <summary>One of a <see cref="Crafting"/>'s recipes: what it makes, how many, what it takes, and where.</summary>
public sealed record Recipe(string Item, int Count, IReadOnlyList<(string Item, int Count)> Needs, string? Station)
{
    /// <summary>Reads "Torch 4 = Wood + Coal @ Workbench"; null if it isn't a recipe.</summary>
    public static Recipe? Parse(string? row)
    {
        var text = row?.Trim() ?? "";
        string? station = null;
        var at = text.IndexOf('@');
        if (at >= 0)
        {
            station = text[(at + 1)..].Trim();
            text = text[..at].Trim();
            if (station.Length == 0)
                station = null;
        }

        var equals = text.IndexOf('=');
        if (equals <= 0)
            return null;
        var (item, count) = Amount(text[..equals]);
        if (item.Length == 0 || count <= 0)
            return null;
        var needs = text[(equals + 1)..].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Amount).Where(n => n.Item.Length > 0 && n.Count > 0).ToList();
        return needs.Count == 0 ? null : new Recipe(item, count, needs, station);
    }

    /// <summary>"Torch 4", "4 Torch" or "Torch": a name and how many.</summary>
    private static (string Item, int Count) Amount(string text)
    {
        text = text.Trim();
        var space = text.LastIndexOf(' ');
        if (space > 0 && int.TryParse(text[(space + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var after))
            return (text[..space].Trim(), after);
        space = text.IndexOf(' ');
        if (space > 0 && int.TryParse(text[..space], NumberStyles.Integer, CultureInfo.InvariantCulture, out var before))
            return (text[(space + 1)..].Trim(), before);
        return (text, 1);
    }

    /// <summary>"Wood 1 + Coal 1", for showing.</summary>
    public string NeedsText => string.Join(" + ", Needs.Select(n => $"{n.Item} {n.Count.ToString(CultureInfo.InvariantCulture)}"));
}
