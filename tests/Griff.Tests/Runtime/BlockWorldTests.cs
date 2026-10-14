using System.Numerics;
using Griff.Input;
using Griff.Modules.BlockWorlds;
using Griff.Modules.FirstPerson;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public sealed class BlockWorldTests
{
    private const double Frame = 1 / 60.0;

    private static void Run(Game game, InputState input, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            game.Tick(Frame);
            input.EndFrame();
        }
    }

    // A flat world: ten columns of dirt (tile 1) four deep, with its top-left corner at (0, 0), so the surface is y = -2.
    private static (Entity World, Tilemap Map, Blocks Blocks) FlatWorld(Scene scene)
    {
        var world = new Entity("World");
        var map = world.AddComponent<Tilemap>();
        map.Tiles = ["", "", "1111111111", "1111111111", "1111111111", "9999999999"];
        var blocks = world.AddComponent<Blocks>();
        blocks.Items = ["Dirt 1", "Sand 7", "Torch 8"];
        blocks.Unbreakable = "9";
        blocks.MineTime = 0.3f;
        blocks.FallingTiles = "7";
        scene.Add(world);
        return (world, map, blocks);
    }

    private static Entity Digger(Scene scene, Vector2 at)
    {
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(at, 0);
        player.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 1.6f);
        player.AddComponent<Body>();
        player.AddComponent<Inventory>();
        player.AddComponent<BlockBuilder>();
        scene.Add(player);
        return player;
    }

    [Fact]
    public void TheSameSeed_MakesTheSameWorld_AndAnotherSeedADifferentOne()
    {
        var generator = new WorldGenerator { Width = 80, Height = 40, Surface = 12, SandTile = 7, TrunkTile = 5, LeavesTile = 6, BottomTile = 9 };

        var first = WorldGen.Generate(generator, 42);
        var again = WorldGen.Generate(generator, 42);
        var other = WorldGen.Generate(generator, 43);

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.Equal(40, first.Count);
        Assert.Equal(new string('9', 80), first[^1]);
    }

    [Fact]
    public void AWorld_HasGrassOnDirtOnStone_WithCavesAndOre()
    {
        var generator = new WorldGenerator { Width = 120, Height = 60, Surface = 15, Sand = 0, Trees = 0, Caves = 0.4f, Ores = "3 8%" };
        var rows = WorldGen.Generate(generator, 7);
        var map = new Tilemap { Tiles = rows };

        // Each column's first tile from the top is grass, with dirt under it.
        for (var x = 0; x < 120; x++)
        {
            var top = Enumerable.Range(0, 60).First(y => map.GetTile(x, y) >= 0);
            Assert.Equal(0, map.GetTile(x, top));
            Assert.Equal(1, map.GetTile(x, top + 1));
            Assert.InRange(top, 15 - 6, 15 + 6);
        }

        var cells = map.Cells().ToList();
        Assert.Contains(cells, c => c.Tile == 2);
        Assert.Contains(cells, c => c.Tile == 3);
        // Caves: empty cells deep underground.
        Assert.True(Enumerable.Range(0, 120).Count(x => map.GetTile(x, 45) < 0) > 5);
    }

    [Fact]
    public void GenerateOnPlay_FillsTheMap_AndStandsThePlayerOnTheGround()
    {
        var scene = new Scene();
        var world = new Entity("World");
        world.Transform.Position = new Vector3(-30, 15, 0);
        var map = world.AddComponent<Tilemap>();
        var generator = world.AddComponent<WorldGenerator>();
        (generator.Width, generator.Height, generator.Surface, generator.Seed) = (60, 30, 10, 5);
        scene.Add(world);
        var player = Digger(scene, new Vector2(100, 100));
        var input = new InputState();
        var game = new Game(scene, input);

        Run(game, input, 1);

        Assert.Equal(30, map.Height);
        Assert.Equal(5, generator.UsedSeed);
        Assert.True(player.GetComponent<Body>()!.IsGrounded);
        Assert.InRange(player.Transform.Position.X, -1, 1);
    }

    [Fact]
    public void Holding_TheLeftButtonOnABlock_DigsItOut_IntoTheInventory()
    {
        var scene = new Scene();
        var (_, map, _) = FlatWorld(scene);
        var player = Digger(scene, new Vector2(5, -1.2f));
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, 0.3);

        // The block under the player's right foot: column 6, row 2.
        input.MouseWorld = new Vector2(6.5f, -2.5f);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, 0.15);
        Assert.Equal(1, map.GetTile(6, 2));
        Assert.InRange(player.GetComponent<BlockBuilder>()!.Progress, 0.3f, 0.7f);
        Run(game, input, 0.25);

        Assert.Equal(-1, map.GetTile(6, 2));
        Assert.Equal(1, player.GetComponent<Inventory>()!.Count("Dirt"));
    }

    [Fact]
    public void Unbreakable_BlocksAndBlocksOutOfReach_StayPut()
    {
        var scene = new Scene();
        var (_, map, blocks) = FlatWorld(scene);
        map.Tiles = ["", "", "9111111111", "1111111111"];
        var player = Digger(scene, new Vector2(1.5f, -1.2f));
        player.GetComponent<BlockBuilder>()!.Reach = 3;
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, 0.2);

        input.MouseWorld = new Vector2(0.5f, -2.5f);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, 1);
        input.MouseWorld = new Vector2(9.5f, -2.5f);
        Run(game, input, 1);

        Assert.Equal(9, map.GetTile(0, 2));
        Assert.Equal(1, map.GetTile(9, 2));
        Assert.True(blocks.IsUnbreakable(9));
    }

    [Fact]
    public void RightClick_PutsDownTheItemInHand_NextToAnotherBlock_AndUsesItUp()
    {
        var scene = new Scene();
        var (_, map, _) = FlatWorld(scene);
        var player = Digger(scene, new Vector2(2, -1.2f));
        player.GetComponent<Inventory>()!.Add("Dirt", 1);
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, 0.2);

        // In mid-air nothing happens; on the ground it goes down.
        input.MouseWorld = new Vector2(5.5f, 0.5f);
        input.SetMouseButton(MouseButton.Right, true);
        Run(game, input, 0.1);
        Assert.Null(player.GetComponent<BlockBuilder>()!.PlaceAt);
        input.MouseWorld = new Vector2(5.5f, -1.5f);
        Run(game, input, 0.5);

        Assert.Equal(1, map.GetTile(5, 1));
        Assert.Equal(0, player.GetComponent<Inventory>()!.Count("Dirt"));
        Assert.Equal(-1, map.GetTile(6, 1));
    }

    [Fact]
    public void ABlock_NeverGoesDown_WhereSomeoneIsStanding()
    {
        var scene = new Scene();
        var (_, map, _) = FlatWorld(scene);
        var player = Digger(scene, new Vector2(2.5f, -1.2f));
        player.GetComponent<Inventory>()!.Add("Dirt", 5);
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, 0.3);

        input.MouseWorld = new Vector2(2.5f, -1.5f);
        input.SetMouseButton(MouseButton.Right, true);
        Run(game, input, 0.3);

        Assert.Equal(-1, map.GetTile(2, 1));
        Assert.Equal(5, player.GetComponent<Inventory>()!.Count("Dirt"));
    }

    [Fact]
    public void TheHotbar_KeepsEachItemInItsSlot_AndPicksWithNumberKeysAndTheWheel()
    {
        var scene = new Scene();
        FlatWorld(scene);
        var player = Digger(scene, new Vector2(2, -1.2f));
        var inventory = player.GetComponent<Inventory>()!;
        inventory.Add("Dirt", 3);
        inventory.Add("Sand", 2);
        inventory.Add("Torch", 4);
        var hotbar = player.AddComponent<Hotbar>();
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, Frame);

        Assert.Equal(["Dirt", "Sand", "Torch"], hotbar.Shown.Take(3).Select(s => s.Item));
        Assert.Equal("Dirt", player.GetComponent<BlockBuilder>()!.Item);

        input.SetKey(Key.D3, true);
        Run(game, input, Frame);
        input.SetKey(Key.D3, false);
        Assert.Equal("Torch", player.GetComponent<BlockBuilder>()!.Item);

        input.AddScroll(1);
        Run(game, input, Frame);
        Assert.Equal("Sand", hotbar.SelectedItem);

        // Sand runs out: Torch stays in the third slot, and new things fill the gap.
        inventory.Take("Sand", 2);
        Run(game, input, Frame);
        Assert.Null(hotbar.Shown[1].Item);
        Assert.Equal("Torch", hotbar.Shown[2].Item);
        inventory.Add("Stone", 1);
        Run(game, input, Frame);
        Assert.Equal("Stone", hotbar.Shown[1].Item);
    }

    [Fact]
    public void Recipes_ReadWhatTheyMakeAndTake()
    {
        var recipe = Recipe.Parse("Torch 4 = Wood + Coal 2 @ Workbench")!;

        Assert.Equal("Torch", recipe.Item);
        Assert.Equal(4, recipe.Count);
        Assert.Equal([("Wood", 1), ("Coal", 2)], recipe.Needs);
        Assert.Equal("Workbench", recipe.Station);
        Assert.Null(Recipe.Parse("Nothing here"));
        Assert.Equal(("Planks", 4), (Recipe.Parse("4 Planks = Wood")!.Item, Recipe.Parse("4 Planks = Wood")!.Count));
    }

    [Fact]
    public void Crafting_TakesTheIngredients_AndGivesTheItem_OnlyNearItsStation()
    {
        var scene = new Scene();
        var (_, map, blocks) = FlatWorld(scene);
        blocks.Items = ["Dirt 1", "Workbench 4"];
        var player = Digger(scene, new Vector2(2, -1.2f));
        var inventory = player.GetComponent<Inventory>()!;
        inventory.Add("Wood", 3);
        inventory.Add("Coal", 1);
        var crafting = player.AddComponent<Crafting>();
        crafting.Recipes = ["Torch 4 = Wood + Coal", "Chair = Wood 2 @ Workbench"];
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, Frame);

        Assert.True(game.Craft(player, crafting.Parsed[0]));
        Assert.Equal(4, inventory.Count("Torch"));
        Assert.Equal(2, inventory.Count("Wood"));
        Assert.False(game.Craft(player, crafting.Parsed[0]));

        Assert.False(game.CanCraft(player, crafting.Parsed[1]));
        map.SetTile(4, 1, 4);
        Assert.True(game.Craft(player, crafting.Parsed[1]));
        Assert.Equal(1, inventory.Count("Chair"));
        Assert.Equal(0, inventory.Count("Wood"));
    }

    [Fact]
    public void TheCraftingKey_OpensTheList_AndClickingARecipeMakesIt()
    {
        var scene = new Scene();
        FlatWorld(scene);
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        scene.Add(camera);
        var player = Digger(scene, new Vector2(2, -1.2f));
        player.GetComponent<Inventory>()!.Add("Wood", 1);
        var crafting = player.AddComponent<Crafting>();
        crafting.Recipes = ["Planks 4 = Wood"];
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, Frame);

        input.SetKey(Key.C, true);
        Run(game, input, Frame);
        input.SetKey(Key.C, false);
        Run(game, input, Frame);
        Assert.True(crafting.IsOpen);
        var row = Assert.Single(game.HudButtons, b => b.Kind == BlockHud.Recipe);

        input.MouseWorld = row.Rect.Center;
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, Frame);
        input.SetMouseButton(MouseButton.Left, false);

        Assert.Equal(4, player.GetComponent<Inventory>()!.Count("Planks"));
        Assert.Equal(1, map(scene).GetTile(0, 2)); // The click on the HUD didn't dig anything.

        static Tilemap map(Scene s) => s.Entities.First(e => e.Name == "World").GetComponent<Tilemap>()!;
    }

    [Fact]
    public void Sand_FallsWhenTheBlockUnderItIsDug_AndSettles()
    {
        var scene = new Scene();
        var (_, map, _) = FlatWorld(scene);
        map.Tiles = ["", "", "7777711111", "1111111111", "1111111111"];
        map.SetTile(2, 1, 7);
        var player = Digger(scene, new Vector2(7, -1.2f));
        player.GetComponent<BlockBuilder>()!.Reach = 20;
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, 0.2);

        input.MouseWorld = new Vector2(2.5f, -3.5f);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, 0.35);
        input.SetMouseButton(MouseButton.Left, false);
        Run(game, input, 0.5);

        // The dirt at (2, 3) is gone; the two sands above it have dropped a cell.
        Assert.Equal(7, map.GetTile(2, 3));
        Assert.Equal(7, map.GetTile(2, 2));
        Assert.Equal(-1, map.GetTile(2, 1));
    }

    [Fact]
    public void AWorldSavedAs_ComesBackNextTime_InsteadOfANewOne()
    {
        var save = new SaveData();
        var scene = new Scene();
        var (world, map, blocks) = FlatWorld(scene);
        var generator = world.AddComponent<WorldGenerator>();
        (generator.Width, generator.Height, generator.Seed) = (20, 10, 3);
        blocks.SaveAs = "World";
        var input = new InputState();
        var game = new Game(scene, input) { SaveData = save };
        Run(game, input, Frame);
        map.SetTile(0, 0, 8);
        game.Stop();

        var again = new Scene();
        var (_, map2, blocks2) = FlatWorld(again);
        var generator2 = map2.Entity!.AddComponent<WorldGenerator>();
        (generator2.Width, generator2.Height, generator2.Seed) = (20, 10, 99);
        blocks2.SaveAs = "World";
        var input2 = new InputState();
        var game2 = new Game(again, input2) { SaveData = save };
        Run(game2, input2, Frame);

        Assert.Equal(8, map2.GetTile(0, 0));
        Assert.Equal(map.Tiles, map2.Tiles);
    }

    [Fact]
    public void GlowingTiles_LightUpTheDark()
    {
        var scene = new Scene();
        var (_, map, blocks) = FlatWorld(scene);
        blocks.GlowingTiles = "8";
        blocks.GlowRadius = 3;
        map.SetTile(4, 1, 8);

        var glow = Assert.Single(SceneRenderer.LightDraws(scene));
        Assert.Equal(new Vector2(4.5f, -1.5f), glow.Center);
        Assert.Equal(3, glow.Radius);
    }

    [Fact]
    public void ABigTilemap_DrawsOnlyTheTilesInView()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        scene.Add(camera);
        var world = new Entity("World");
        world.Transform.Position = new Vector3(-100, 50, 0);
        world.AddComponent<Tilemap>().Tiles = Enumerable.Repeat(new string('1', 200), 100).ToList();
        scene.Add(world);

        var frame = SceneRenderer.Build(scene, 16f / 9);

        var tiles = frame.Sprites.Count(s => s.Entity == world);
        Assert.InRange(tiles, 18 * 10, 22 * 14);
    }

    [Fact]
    public void InFirstPerson_TheBuilderDigsTheBlockAhead()
    {
        var scene = new Scene();
        var world = new Entity("World");
        var map = world.AddComponent<Tilemap>();
        map.Tiles = ["11111", "1...1", "1...1", "1...1", "11111"];
        world.AddComponent<Blocks>().Items = ["Stone 1"];
        scene.Add(world);
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        camera.AddComponent<FirstPersonView>().Viewer = "Player";
        scene.Add(camera);
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(2.5f, -2.5f, 0);
        player.AddComponent<CircleCollider>().Radius = 0.3f;
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<FirstPersonMovement>();
        player.AddComponent<BlockBuilder>().Reach = 3;
        scene.Add(player);
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, Frame);

        // Facing right (rotation 0) the wall at column 4 is ahead.
        Assert.Equal((4, 2), player.GetComponent<BlockBuilder>()!.Target);
        Assert.Equal((3, 2), player.GetComponent<BlockBuilder>()!.PlaceAt);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, 0.5);

        Assert.Equal(-1, map.GetTile(4, 2));
        Assert.Equal(1, player.GetComponent<Inventory>()!.Count("Stone"));
    }
}
