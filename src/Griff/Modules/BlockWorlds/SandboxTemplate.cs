using System.Numerics;
using Griff.Audio;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using static Griff.Projects.ProjectTemplates;

namespace Griff.Modules.BlockWorlds;

// The Sandbox template: a world of blocks to dig and build in (Terraria, Minecraft). All no-code components.
internal sealed class SandboxTemplate : ProjectTemplate
{
    public override string Description => "Dig and build in a world of blocks made from a seed: grassy hills, caves, coal, iron and gold, trees and sand that falls. Mine with the left mouse button, build with the right, pick blocks on the hotbar, and craft planks, torches, workbenches, furnaces and glass. A/D to walk, Space to jump, C to craft.";

    public override int Order => 60;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Chill, seed: 6) with { Volume = 0.55f };

    protected override Scene BuildStartScene() => Sandbox();

    protected override Dictionary<string, Entity> BuildPrefabs() => SandboxPrefabs();

    public override IReadOnlyDictionary<string, byte[]> Images() => BlockWorldsArt.Files();

    protected internal override void WireSounds(Entity entity)
    {
        base.WireSounds(entity);
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case Blocks b:
                    TemplateSounds.Set(b.DigSound, s => b.DigSound = s, TemplateSounds.Dig);
                    TemplateSounds.Set(b.BreakSound, s => b.BreakSound = s, TemplateSounds.Hit);
                    TemplateSounds.Set(b.PlaceSound, s => b.PlaceSound = s, TemplateSounds.Place);
                    break;
                case Crafting c:
                    TemplateSounds.Set(c.Sound, s => c.Sound = s, TemplateSounds.PowerUp);
                    break;
            }
        }
    }

    private const int WorldWidth = 160, WorldHeight = 70;

    private static Scene Sandbox()
    {
        var scene = new Scene();

        // The world is a tilemap the generator fills from its seed each time the game starts; it's made here too, so
        // the editor shows the world you'll get. Change the Seed for another one.
        var world = new Entity("World");
        world.Transform.Position = new Vector3(-WorldWidth / 2f, WorldHeight / 2f, 0);
        var map = world.AddComponent<Tilemap>();
        map.Image = BlockWorldsArt.BlockSheet;
        (map.Columns, map.Rows) = (BlockWorldsArt.BlockColumns, BlockWorldsArt.BlockRows);
        map.Order = -5;
        // Trees, torches and workbenches are walked through.
        map.BackgroundTiles = $"{BlockWorldsArt.LogBlock}, {BlockWorldsArt.LeavesBlock}, {BlockWorldsArt.TorchBlock}, {BlockWorldsArt.WorkbenchBlock}";
        var generator = world.AddComponent<WorldGenerator>();
        (generator.Seed, generator.Width, generator.Height, generator.Surface) = (2026, WorldWidth, WorldHeight, 24);
        (generator.Hills, generator.HillWidth, generator.DirtDepth, generator.Caves) = (6, 26, 5, 0.32f);
        (generator.GrassTile, generator.DirtTile, generator.StoneTile) = (BlockWorldsArt.GrassBlock, BlockWorldsArt.DirtBlock, BlockWorldsArt.StoneBlock);
        generator.Ores = $"{BlockWorldsArt.CoalBlock} 7%, {BlockWorldsArt.IronBlock} 4%, {BlockWorldsArt.GoldBlock} 2%";
        (generator.SandTile, generator.Sand) = (BlockWorldsArt.SandBlock, 0.15f);
        (generator.TrunkTile, generator.LeavesTile, generator.Trees) = (BlockWorldsArt.LogBlock, BlockWorldsArt.LeavesBlock, 0.18f);
        generator.BottomTile = BlockWorldsArt.BedrockBlock;
        generator.Walls = "Cave Walls";
        map.Tiles = WorldGen.Generate(generator, generator.Seed);

        var blocks = world.AddComponent<Blocks>();
        blocks.Items =
        [
            $"Dirt {BlockWorldsArt.DirtBlock}", $"Stone {BlockWorldsArt.StoneBlock}", $"Wood {BlockWorldsArt.LogBlock}", $"Leaves {BlockWorldsArt.LeavesBlock}",
            $"Sand {BlockWorldsArt.SandBlock}", $"Planks {BlockWorldsArt.PlanksBlock}", $"Torch {BlockWorldsArt.TorchBlock}", $"Workbench {BlockWorldsArt.WorkbenchBlock}",
            $"Bricks {BlockWorldsArt.BricksBlock}", $"Glass {BlockWorldsArt.GlassBlock}", $"Furnace {BlockWorldsArt.FurnaceBlock}",
            $"Iron Ore {BlockWorldsArt.IronBlock}", $"Gold Ore {BlockWorldsArt.GoldBlock}",
        ];
        blocks.Drops = $"{BlockWorldsArt.GrassBlock} Dirt, {BlockWorldsArt.CoalBlock} Coal, {BlockWorldsArt.LeavesBlock} nothing";
        blocks.Hardness = $"{BlockWorldsArt.StoneBlock} 0.8, {BlockWorldsArt.CoalBlock}-{BlockWorldsArt.IronBlock} 1, {BlockWorldsArt.GoldBlock} 1.2, " +
                          $"{BlockWorldsArt.BricksBlock} 1, {BlockWorldsArt.FurnaceBlock} 1, {BlockWorldsArt.LogBlock} 0.6, {BlockWorldsArt.PlanksBlock} 0.5, " +
                          $"{BlockWorldsArt.LeavesBlock} 0.1, {BlockWorldsArt.TorchBlock} 0.05, {BlockWorldsArt.GlassBlock} 0.3";
        blocks.Unbreakable = $"{BlockWorldsArt.BedrockBlock}";
        blocks.FallingTiles = $"{BlockWorldsArt.SandBlock}";
        blocks.GlowingTiles = $"{BlockWorldsArt.TorchBlock}";
        (blocks.Underground, blocks.DarkDepth) = (Color.FromBytes(0x16, 0x18, 0x24), 6);
        blocks.BreakEffect = "Crumbs";
        blocks.SaveAs = null;
        scene.Add(world);

        // Behind the blocks, darker: what caves and tunnels show instead of the sky.
        var caveWalls = new Entity("Cave Walls");
        caveWalls.Transform.Position = world.Transform.Position;
        var wallMap = caveWalls.AddComponent<Tilemap>();
        wallMap.Image = BlockWorldsArt.BlockSheet;
        (wallMap.Columns, wallMap.Rows, wallMap.Solid, wallMap.Order) = (BlockWorldsArt.BlockColumns, BlockWorldsArt.BlockRows, false, -6);
        wallMap.Tint = Color.FromBytes(0x5A, 0x58, 0x60);
        wallMap.Tiles = WorldGen.GenerateWalls(generator, generator.Seed);
        scene.Add(caveWalls);

        // The sky, behind everything, also keeps the camera from looking past the world's edges.
        var sky = new Entity("Sky");
        var skySprite = sky.AddComponent<Sprite>();
        skySprite.Size = new Vector2(WorldWidth, WorldHeight);
        skySprite.Tint = Color.FromBytes(0x7C, 0xB8, 0xE8);
        skySprite.Order = -20;
        scene.Add(sky);

        // The player starts on the ground in the middle, where the generator puts them each time.
        var column = WorldWidth / 2;
        var ground = Enumerable.Range(0, WorldHeight).First(y => map.IsBlocked(column, y));
        var player = Box("Player", new Vector2(0.5f, WorldHeight / 2f - ground + 0.92f), new Vector2(0.8f, 1.8f), Color.FromBytes(0xF0, 0x8C, 0x2E));
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Body>();
        var movement = player.AddComponent<PlatformerMovement>();
        movement.JumpHeight = 2.6f;
        player.AddComponent<Inventory>().Items = ["Torch 8", "Wood 3"];
        player.AddComponent<BlockBuilder>().Reach = 5;
        var hotbar = player.AddComponent<Hotbar>();
        hotbar.Colors = ["Coal #2E2E33", "Iron Bar #C9CCD6", "Gold Bar #F2C93A"];
        var crafting = player.AddComponent<Crafting>();
        crafting.Recipes =
        [
            "Planks 4 = Wood",
            "Torch 4 = Wood + Coal",
            "Workbench = Planks 6",
            "Bricks 2 = Stone 2 @ Workbench",
            "Furnace = Stone 10 @ Workbench",
            "Glass 2 = Sand 2 @ Furnace",
            "Iron Bar = Iron Ore 2 + Coal @ Furnace",
            "Gold Bar = Gold Ore 2 + Coal @ Furnace",
        ];
        scene.Add(player);

        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 8;
        view.Background = Color.FromBytes(0x7C, 0xB8, 0xE8);
        var follow = camera.AddComponent<Follow>();
        (follow.Target, follow.StayInside, follow.Smoothing) = ("Player", "Sky", 8);
        camera.Transform.Position = player.Transform.Position with { Z = 0 };
        scene.Add(camera);

        scene.Add(Help("Hold the left mouse button to dig, right-click to build.   1-9 or the wheel: pick a block.   C: crafting", Anchor.TopLeft));
        AddPauseMenu(scene, menuScene: null);
        return scene;
    }

    private static Dictionary<string, Entity> SandboxPrefabs() => new Dictionary<string, Entity>
    {
        ["Prefabs/Crumbs.prefab"] = Crumbs(),
    };

    // A puff of dust where a block breaks.
    private static Entity Crumbs()
    {
        var entity = new Entity("Crumbs");
        entity.Transform.Position = new Vector3(0, 0, 0.5f);
        var dust = entity.AddComponent<ParticleEmitter>();
        dust.Emitting = false;
        dust.BurstOnStart = 10;
        dust.Lifetime = 0.4f;
        dust.Speed = 3;
        dust.Spread = 360;
        dust.GravityScale = 1;
        dust.StartSize = 0.18f;
        dust.EndSize = 0.05f;
        dust.StartColor = Color.FromBytes(0x9A, 0x86, 0x6E);
        dust.EndColor = Color.FromBytes(0x6E, 0x5A, 0x44) with { A = 0 };
        dust.Order = 10;
        dust.DestroyWhenFinished = true;
        return entity;
    }

}
