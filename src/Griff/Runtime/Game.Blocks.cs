using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Block worlds with no code: worlds made from a seed, digging and building with the mouse (or straight ahead in first
// person), a hotbar, crafting, sand that falls, and worlds kept in the save data. Drawn by SceneRenderer.Sandbox.cs.
public sealed partial class Game
{
    // How often falling blocks drop a cell, in seconds; how long between saves of a changed world.
    private const float FallStep = 0.06f;
    private const float SaveEvery = 2f;

    // The world's, or while the host runs another player's tools for them, that player's own HUD and tool state.
    internal SandboxState Sandbox => _acting?.Sandbox ?? _sandboxes.State;

    private readonly SandboxSystem _sandboxes = new();

    /// <summary>The sandbox HUD's layout for the view as it is now, if anything in the scene shows one.</summary>
    public SandboxHud? SandboxHud => Sandbox.Hud;

    /// <summary>The buttons on the sandbox HUD right now: hotbar slots, recipes, tools, props and ports.</summary>
    public IReadOnlyList<HudButton> SandboxButtons => Sandbox.Buttons;

    /// <summary>A short message the sandbox HUD is showing (something made, a tool's next step), or null.</summary>
    public string? SandboxNotice => Sandbox.NoticeLeft > 0 ? Sandbox.Notice : null;

    // Worlds are made before anything moves, so the player is stood on the new ground in the first frame.
    private void StartWorlds()
    {
        foreach (var generator in Active<WorldGenerator>())
        {
            if (!Sandbox.Started.Add(generator) || generator.Entity!.GetComponent<Tilemap>() is not { } map)
                continue;
            var blocks = map.Entity!.GetComponent<Blocks>();
            if (LoadWorld(blocks, map))
            {
                PlaceOnSurface(generator, map);
                continue;
            }

            if (!generator.GenerateOnPlay)
                continue;
            generator.UsedSeed = generator.Seed != 0 ? generator.Seed : Random.Shared.Next(1, int.MaxValue);
            map.Tiles = WorldGen.Generate(generator, generator.UsedSeed);
            if (!string.IsNullOrWhiteSpace(generator.Walls) && Find(generator.Walls.Trim())?.GetComponent<Tilemap>() is { } walls)
                walls.Tiles = WorldGen.GenerateWalls(generator, generator.UsedSeed);
            PlaceOnSurface(generator, map);
        }

        foreach (var blocks in Active<Blocks>())
        {
            if (blocks.Started || blocks.Entity!.GetComponent<Tilemap>() is not { } map)
                continue;
            blocks.Started = true;
            blocks.SavedTiles = map.Tiles;
            blocks.Surface = [];
            if (blocks.Entity.GetComponent<WorldGenerator>() is null)
                LoadWorld(blocks, map);
            foreach (var (x, y, tile) in map.Cells())
            {
                if (blocks.Falls(tile))
                    blocks.Unsettled.Add((x, y));
            }
        }
    }

    // A world kept under the Blocks' SaveAs comes back in place of the map's own tiles.
    private bool LoadWorld(Blocks? blocks, Tilemap map)
    {
        var key = blocks?.SaveAs?.Trim();
        if (string.IsNullOrEmpty(key) || SaveData.GetString(key) is not { } saved)
            return false;
        map.Tiles = saved.Split('\n');
        return true;
    }

    private void SaveWorld(Blocks blocks)
    {
        var key = blocks.SaveAs?.Trim();
        if (string.IsNullOrEmpty(key) || blocks.Entity?.GetComponent<Tilemap>() is not { } map)
            return;
        SaveData.Set(key, string.Join('\n', map.Tiles));
        blocks.SavedTiles = map.Tiles;
        blocks.SinceSaved = 0;
    }

    // A world with somewhere to save it, changed since it was saved or loaded.
    private static bool Changed(Blocks blocks) =>
        !string.IsNullOrWhiteSpace(blocks.SaveAs) && blocks.Entity?.GetComponent<Tilemap>() is { } map && !ReferenceEquals(map.Tiles, blocks.SavedTiles);

    private void SaveWorlds()
    {
        foreach (var blocks in Scene.Entities.SelectMany(e => e.GetComponents<Blocks>()))
        {
            if (Changed(blocks))
                SaveWorld(blocks);
        }
    }

    // Stands the generator's Spawn entities on the ground in the middle of the world.
    private void PlaceOnSurface(WorldGenerator generator, Tilemap map)
    {
        var names = (generator.Spawn ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
            return;
        var column = Math.Max(0, map.Width / 2);
        var row = Enumerable.Range(0, map.Height).FirstOrDefault(y => map.IsBlocked(column, y), map.Height);
        var world = map.Entity!.Transform.WorldMatrix;
        var top = Vector2.Transform(map.CellCenter(column, row) + new Vector2(0, map.TileSize / 2), world);
        foreach (var entity in Scene.Entities.Where(e => names.Contains(e.Name)).ToList())
        {
            var position = entity.Transform.WorldPosition;
            var lift = 0.5f;
            if (entity.GetComponent<Collider>() is { } collider)
            {
                var bounds = Physics.BoundsOf(collider);
                lift = bounds.HalfSize.Y - (bounds.Center.Y - position.Y);
            }

            var target = new Vector3(top.X, top.Y + lift + 0.02f, position.Z);
            if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
                target = Vector3.Transform(target, inverse);
            entity.Transform.Position = target;
            if (entity.GetComponent<Body>() is { } body)
                body.Velocity = Vector2.Zero;
        }
    }

    private void RunBlocks()
    {
        Sandbox.NoticeLeft = Math.Max(0, Sandbox.NoticeLeft - Time.UnscaledDelta);
        if (Time.Delta <= 0 || PausedThisFrame)
            return;

        RunBlockPlayers();
        DarkenUnderground();
        foreach (var blocks in Active<Blocks>())
        {
            RunFalling(blocks);
            blocks.SinceSaved += Time.Delta;
            if (blocks.SinceSaved >= SaveEvery && Changed(blocks))
                SaveWorld(blocks);
        }
    }

    // A player's hotbar, crafting and digging: on the host (for each player in turn, in a network game). A player's own
    // machine works out just what its HUD shows.
    private void RunBlockPlayers()
    {
        if (Time.Delta <= 0 || PausedThisFrame)
            return;
        var acts = !Net.IsClient;
        foreach (var hotbar in Mine<Hotbar>())
            RunHotbar(hotbar, acts);
        if (!acts)
        {
            foreach (var builder in Mine<BlockBuilder>())
                AimOnly(builder);
            return;
        }

        foreach (var crafting in Mine<Crafting>())
        {
            if (!PanelOpenHere && Input.WasPressed(crafting.Key))
                crafting.IsOpen = !crafting.IsOpen;
        }

        foreach (var builder in Mine<BlockBuilder>())
            RunBuilder(builder);
    }

    // What a builder points at, for its outline on this player's screen; the host does the digging.
    private void AimOnly(BlockBuilder builder)
    {
        if (builder.Map is null || builder.Map.Entity?.Scene != Scene)
            builder.Map = WorldFor(builder);
        (builder.Target, builder.PlaceAt) = builder.Map is null || PanelOpenHere ? (null, null) : Aim(builder, builder.Map);
    }

    // Below the surface the camera's light fades towards the world's Underground color.
    private void DarkenUnderground()
    {
        if (SceneRenderer.FindCamera(Scene) is not { } camera || camera.GetComponent<Camera>() is not { } view)
            return;
        var blocks = Active<Blocks>().FirstOrDefault(b => b.Underground.A > 0);
        if (blocks?.Entity?.GetComponent<Tilemap>() is not { } map)
            return;
        if (!Matrix4x4.Invert(map.Entity!.Transform.WorldMatrix, out var toLocal))
            return;

        // The ground as it was when the world began, averaged over a few columns either side of the camera, so digging
        // doesn't move it and walking past a cliff doesn't flicker the light.
        if (blocks.Surface.Length != map.Width)
            blocks.Surface = Enumerable.Range(0, map.Width).Select(x => Enumerable.Range(0, map.Height).FirstOrDefault(y => map.IsBlocked(x, y), map.Height)).ToArray();
        var eye = new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        var column = map.CellAt(Vector2.Transform(eye, toLocal)).X;
        var near = Enumerable.Range(column - 4, 9).Where(x => x >= 0 && x < blocks.Surface.Length).Select(x => blocks.Surface[x]).DefaultIfEmpty(0);
        var surface = Vector2.Transform(map.CellCenter(0, (int)MathF.Round((float)near.Average())), map.Entity.Transform.WorldMatrix).Y;
        var t = Math.Clamp((surface - eye.Y - 1) / Math.Max(0.1f, blocks.DarkDepth), 0, 1);
        var light = blocks.Underground with { A = 1 };
        view.FadeAmbient(t <= 0 ? null : Color.Lerp(view.Ambient, light, t), 0);
    }

    // Each item keeps its slot until it runs out; new ones take the first empty slot. Number keys and the wheel pick.
    private void RunHotbar(Hotbar hotbar, bool acts)
    {
        var slots = Math.Clamp(hotbar.Slots, 1, 10);
        var contents = hotbar.Entity!.GetComponent<Inventory>()?.Contents ?? [];
        var items = hotbar.Shown.Select(s => s.Item).ToList();
        while (items.Count < slots)
            items.Add(null);
        if (items.Count > slots)
            items.RemoveRange(slots, items.Count - slots);
        for (var i = 0; i < slots; i++)
        {
            if (items[i] is { } item && !contents.Any(c => c.Name.Equals(item, StringComparison.OrdinalIgnoreCase)))
                items[i] = null;
        }

        foreach (var (name, _) in contents)
        {
            if (items.Any(i => name.Equals(i, StringComparison.OrdinalIgnoreCase)) || items.IndexOf(null) is not (>= 0 and var free))
                continue;
            items[free] = name;
        }

        hotbar.Shown = items.Select(i => (i, i is null ? 0 : contents.First(c => c.Name.Equals(i, StringComparison.OrdinalIgnoreCase)).Count)).ToList();

        if (acts && !PanelOpenHere)
        {
            for (var i = 0; i < slots; i++)
            {
                if (Input.WasPressed(i == 9 ? Key.D0 : Key.D1 + i))
                    hotbar.Selected = i;
            }

            if (Input.Scroll != 0)
                hotbar.Selected -= Math.Sign(Input.Scroll);
        }

        hotbar.Selected = ((hotbar.Selected % slots) + slots) % slots;
        if (hotbar.Entity.GetComponent<BlockBuilder>() is { } builder)
            builder.Item = hotbar.SelectedItem;
    }

    private void RunBuilder(BlockBuilder builder)
    {
        var entity = builder.Entity!;
        builder.PlaceCooldown -= Time.Delta;
        if (builder.Map is null || builder.Map.Entity?.Scene != Scene)
            builder.Map = WorldFor(builder);
        var map = builder.Map;
        var before = builder.Target;
        (builder.Target, builder.PlaceAt) = map is null || PanelOpenHere ? (null, null) : Aim(builder, map);
        if (builder.Target != before)
        {
            builder.Progress = 0;
            builder.Cracks = 0;
        }

        if (map is null)
            return;
        var blocks = map.Entity!.GetComponent<Blocks>();
        var onHud = Sandbox.Hud is not null && Sandbox.MouseOnHud && !LooksThrough(entity);

        // Digging: hold the button on a block until it breaks.
        if (builder.CanMine && !onHud && builder.Target is { } target && Input.IsDown(MouseButton.Left) &&
            blocks?.IsUnbreakable(map.GetTile(target.X, target.Y)) != true)
        {
            var tile = map.GetTile(target.X, target.Y);
            var seconds = (blocks?.TimeToBreak(tile) ?? 0.35f) / Math.Max(0.01f, builder.MineSpeed);
            builder.Progress = seconds <= 0 ? 1 : builder.Progress + Time.Delta / seconds;
            var cracks = (int)(Math.Min(builder.Progress, 0.999f) * 4);
            if (cracks > builder.Cracks && !string.IsNullOrWhiteSpace(blocks?.DigSound))
                PlaySoundAt(blocks.DigSound.Trim(), CellWorld(map, target.X, target.Y));
            builder.Cracks = cracks;
            if (builder.Progress >= 1)
            {
                Break(map, blocks, target, entity);
                builder.Progress = 0;
                builder.Cracks = 0;
                builder.Target = null;
            }
        }
        else
        {
            builder.Progress = 0;
            builder.Cracks = 0;
        }

        // Building: the right button puts down the item in hand, again every so often while it's held.
        if (builder.CanBuild && !onHud && builder.PlaceAt is { } at && Input.IsDown(MouseButton.Right) && builder.PlaceCooldown <= 0 &&
            !string.IsNullOrWhiteSpace(builder.Item))
        {
            var tile = blocks?.TileOf(builder.Item) ?? -1;
            var inventory = entity.GetComponent<Inventory>();
            if (tile >= 0 && inventory?.Take(builder.Item.Trim()) == true)
            {
                map.SetTile(at.X, at.Y, tile);
                builder.PlaceCooldown = 0.18f;
                if (blocks is not null)
                {
                    blocks.Unsettled.Add(at);
                    if (!string.IsNullOrWhiteSpace(blocks.PlaceSound))
                        PlaySoundAt(blocks.PlaceSound.Trim(), CellWorld(map, at.X, at.Y));
                }
            }
        }
    }

    // The world a builder works on: the named one, or the first map with Blocks (or failing that, any solid map).
    private Tilemap? WorldFor(BlockBuilder builder)
    {
        var name = builder.World?.Trim();
        var maps = Active<Tilemap>().ToList();
        if (!string.IsNullOrEmpty(name))
            return maps.FirstOrDefault(m => m.Entity!.Name == name);
        return maps.FirstOrDefault(m => m.Entity!.GetComponent<Blocks>() is not null) ?? maps.FirstOrDefault(m => m.Solid);
    }

    // The block pointed at and the empty cell a block would go in: under the mouse, or straight ahead in first person.
    private ((int X, int Y)? Target, (int X, int Y)? PlaceAt) Aim(BlockBuilder builder, Tilemap map)
    {
        var entity = builder.Entity!;
        if (!Matrix4x4.Invert(map.Entity!.Transform.WorldMatrix, out var toLocal))
            return (null, null);
        var origin = CenterOf(entity);
        var reach = Math.Max(0, builder.Reach);

        if (LooksThrough(entity) && entity.GetComponent<FirstPersonMovement>() is { } movement)
        {
            // Through the cells along the way the player faces, a little at a time.
            var facing = movement.Facing;
            (int X, int Y)? empty = null;
            for (var d = 0f; d <= reach; d += 0.05f)
            {
                var cell = map.CellAt(Vector2.Transform(origin + facing * d, toLocal));
                if (map.GetTile(cell.X, cell.Y) >= 0)
                    return (cell, empty);
                if (cell != map.CellAt(Vector2.Transform(origin, toLocal)) && CanPlace(map, cell))
                    empty = cell;
            }

            return (null, null);
        }

        if (!Input.IsMouseInView)
            return (null, null);
        var mouse = Input.MouseWorld;
        var aimed = map.CellAt(Vector2.Transform(mouse, toLocal));
        if (Vector2.Distance(CellWorld(map, aimed.X, aimed.Y), origin) > reach)
            return (null, null);
        if (map.GetTile(aimed.X, aimed.Y) >= 0)
            return (aimed, null);
        var touching = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) }.Any(d => map.GetTile(aimed.X + d.Item1, aimed.Y + d.Item2) >= 0);
        return (null, touching && CanPlace(map, aimed) ? aimed : null);
    }

    // An empty cell inside the grid (it grows right and down, not left or up) with no one standing in it.
    private bool CanPlace(Tilemap map, (int X, int Y) cell)
    {
        if (cell.X < 0 || cell.Y < 0 || map.GetTile(cell.X, cell.Y) >= 0)
            return false;
        var size = map.TileSize * ScaleOf(map.Entity!) * 0.96f;
        return !Physics.OverlapBox(Scene, CellWorld(map, cell.X, cell.Y), new Vector2(size))
            .Any(e => e.GetComponents<Collider>().Any(c => !c.IsTrigger) && e.GetComponent<Tilemap>() is null);
    }

    private void Break(Tilemap map, Blocks? blocks, (int X, int Y) cell, Entity by)
    {
        var tile = map.GetTile(cell.X, cell.Y);
        map.SetTile(cell.X, cell.Y, -1);
        var at = CellWorld(map, cell.X, cell.Y);
        if (blocks is null)
            return;
        blocks.Unsettled.Add((cell.X, cell.Y - 1));
        if (blocks.DropOf(tile) is { } item)
        {
            var inventory = by.GetComponent<Inventory>() ?? by.AddComponent<Inventory>();
            inventory.Add(item);
        }

        if (!string.IsNullOrWhiteSpace(blocks.BreakSound))
            PlaySoundAt(blocks.BreakSound.Trim(), at);
        if (!string.IsNullOrWhiteSpace(blocks.BreakEffect) && PrefabLoader is not null)
            Spawn(blocks.BreakEffect.Trim(), at);
    }

    // Sand and the like drop a cell at a time while there's nothing under them, settling one another as they go.
    private void RunFalling(Blocks blocks)
    {
        if (blocks.Unsettled.Count == 0 || blocks.Entity!.GetComponent<Tilemap>() is not { } map)
            return;
        blocks.FallClock += Time.Delta;
        if (blocks.FallClock < FallStep)
            return;
        blocks.FallClock = 0;

        // Lowest first, so a column of sand falls together.
        var cells = blocks.Unsettled.OrderByDescending(c => c.Y).ToList();
        blocks.Unsettled.Clear();
        foreach (var (x, y) in cells)
        {
            var tile = map.GetTile(x, y);
            if (!blocks.Falls(tile) || y + 1 >= map.Height || map.GetTile(x, y + 1) >= 0)
                continue;
            map.SetTile(x, y, -1);
            map.SetTile(x, y + 1, tile);
            blocks.Unsettled.Add((x, y + 1));
            blocks.Unsettled.Add((x, y - 1));
        }
    }

    /// <summary>
    /// Makes <paramref name="recipe"/> for <paramref name="crafter"/> if they have what it takes (and are near its
    /// workbench); returns whether it did.
    /// </summary>
    public bool Craft(Entity crafter, Recipe recipe)
    {
        if (!CanCraft(crafter, recipe) || crafter.GetComponent<Inventory>() is not { } inventory)
            return false;
        foreach (var (item, count) in recipe.Needs)
            inventory.Take(item, count);
        inventory.Add(recipe.Item, recipe.Count);
        SandboxNote(recipe.Count == 1 ? $"Made a {recipe.Item}" : $"Made {recipe.Count} {recipe.Item}");
        if (crafter.GetComponent<Crafting>()?.Sound is { } sound && !string.IsNullOrWhiteSpace(sound))
            PlayShared(sound.Trim());
        return true;
    }

    /// <summary>True if <paramref name="crafter"/> has everything <paramref name="recipe"/> takes, and is near its workbench.</summary>
    public bool CanCraft(Entity crafter, Recipe recipe)
    {
        if (crafter.GetComponent<Inventory>() is not { } inventory || !recipe.Needs.All(n => inventory.Has(n.Item, n.Count)))
            return false;
        return recipe.Station is null || IsNear(crafter, recipe.Station, crafter.GetComponent<Crafting>()?.StationReach ?? 4);
    }

    // A station: an entity with that name, or a block of that item placed in a world, within reach.
    private bool IsNear(Entity crafter, string station, float reach)
    {
        var here = CenterOf(crafter);
        if (Scene.Entities.Any(e => e.Name.Equals(station, StringComparison.OrdinalIgnoreCase) && e != crafter && e.IsActive &&
                                    Vector2.Distance(CenterOf(e), here) <= reach))
            return true;
        foreach (var blocks in Active<Blocks>())
        {
            var tile = blocks.TileOf(station);
            if (tile < 0 || blocks.Entity!.GetComponent<Tilemap>() is not { } map || !Matrix4x4.Invert(map.Entity!.Transform.WorldMatrix, out var toLocal))
                continue;
            var (cx, cy) = map.CellAt(Vector2.Transform(here, toLocal));
            var cells = (int)MathF.Ceiling(reach / Math.Max(0.01f, map.TileSize * ScaleOf(map.Entity!))) + 1;
            for (var y = cy - cells; y <= cy + cells; y++)
            {
                for (var x = cx - cells; x <= cx + cells; x++)
                {
                    if (map.GetTile(x, y) == tile && Vector2.Distance(CellWorld(map, x, y), here) <= reach)
                        return true;
                }
            }
        }

        return false;
    }

    private void SandboxNote(string text) => (Sandbox.Notice, Sandbox.NoticeLeft) = (text, 2.5f);

    /// <summary>The world position of the middle of a map's cell.</summary>
    internal static Vector2 CellWorld(Tilemap map, int x, int y) => Vector2.Transform(map.CellCenter(x, y), map.Entity!.Transform.WorldMatrix);

    private static float ScaleOf(Entity entity) => new Vector2(entity.Transform.WorldMatrix.M11, entity.Transform.WorldMatrix.M12).Length();

    // The middle of an entity: its collider's, or its position.
    internal static Vector2 CenterOf(Entity entity)
    {
        if (entity.GetComponent<Collider>() is { } collider)
            return Physics.BoundsOf(collider).Center;
        var position = entity.Transform.WorldPosition;
        return new Vector2(position.X, position.Y);
    }
}

/// <summary>What the sandbox features keep track of while a scene runs.</summary>
internal sealed class SandboxState
{
    public SandboxState()
    {
    }

    /// <summary>One player's own HUD and tools in a network game, sharing the world's welds, wiring and worlds.</summary>
    public SandboxState(SandboxState world)
    {
        World = world;
        Started = world.Started;
        Groups = world.Groups;
        Applied = world.Applied;
    }

    /// <summary>For one player's state, the world's it shares.</summary>
    public SandboxState? World { get; }

    public HashSet<WorldGenerator> Started { get; } = [];

    public SandboxHud? Hud { get; set; }

    public List<HudButton> Buttons { get; } = [];

    /// <summary>True when the mouse was over the HUD as this frame began, so clicks there don't reach the world.</summary>
    public bool MouseOnHud { get; set; }

    public string? Notice { get; set; }

    public float NoticeLeft { get; set; }

    // The Grab tool: what's held, and where on it.
    public Body? Held { get; set; }

    public Vector2 HeldAt { get; set; }

    // The Weld, Rope and Wire tools' first click: what, where on it, and (wiring) which output.
    public Entity? First { get; set; }

    public Vector2 FirstAt { get; set; }

    public string? FirstPort { get; set; }

    // A list of an entity's inputs or outputs to pick from, popped up by the Wire tool.
    public (Entity Entity, IReadOnlyList<string> Ports, bool Outputs)? Picking { get; set; }

    // What the tools made, newest last, for undo: spawned entities, and welds, ropes and wires.
    public List<object> Made { get; } = [];

    // Each welded body's group, as of this frame.
    public Dictionary<Body, BodyGroup> Groups { get; } = [];

    // What wires last set each generic input to, so a wire only takes over when its signal changes.
    public Dictionary<(Entity Entity, string Port), float> Applied { get; } = [];
}

/// <summary>Block worlds and contraptions: the HUD, tools, worlds started and the rest of <see cref="SandboxState"/>.</summary>
internal sealed class SandboxSystem : GameSystem
{
    public override string Name => "Sandbox";

    public SandboxState State { get; private set; } = new();

    protected override void SceneChanged() => State = new SandboxState();
}
