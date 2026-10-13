using System.Numerics;
using Griff.Audio;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Projects;

// The Sandbox template, a world of blocks to dig and build in (Terraria, Minecraft), and the Contraption template, a room
// of props to build machines from with tools and wires (Garry's Mod and Wiremod). All no-code components.
public static partial class ProjectTemplates
{
    private sealed class SandboxTemplate : ProjectTemplate
    {
        public override string Description => "Dig and build in a world of blocks made from a seed: grassy hills, caves, coal, iron and gold, trees and sand that falls. Mine with the left mouse button, build with the right, pick blocks on the hotbar, and craft planks, torches, workbenches, furnaces and glass. A/D to walk, Space to jump, C to craft.";

        public override int Order => 60;

        protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Chill, seed: 6) with { Volume = 0.55f };

        protected override Scene BuildStartScene() => Sandbox();

        protected override Dictionary<string, Entity> BuildPrefabs() => SandboxPrefabs();

        public override IReadOnlyDictionary<string, byte[]> Images() => TemplateArt.SandboxFiles();
    }

    private sealed class ContraptionTemplate : ProjectTemplate
    {
        public override string Description => "Build machines in a room full of props: grab and throw things, freeze them in the air, spawn crates, balloons, thrusters, lamps, buttons and logic gates, weld and rope them together, and wire buttons to thrusters to make a lift. A/D to walk, Space to jump, 1-6 for tools, E to press buttons, Z to undo.";

        public override int Order => 70;

        protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Bright, seed: 7) with { Volume = 0.5f };

        protected override Scene BuildStartScene() => Contraption();

        protected override Dictionary<string, Entity> BuildPrefabs() => ContraptionPrefabs();

        public override IReadOnlyDictionary<string, byte[]> Images() => TemplateArt.ContraptionFiles();
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
        map.Image = TemplateArt.BlockSheet;
        (map.Columns, map.Rows) = (TemplateArt.BlockColumns, TemplateArt.BlockRows);
        map.Order = -5;
        // Trees, torches and workbenches are walked through.
        map.BackgroundTiles = $"{TemplateArt.LogBlock}, {TemplateArt.LeavesBlock}, {TemplateArt.TorchBlock}, {TemplateArt.WorkbenchBlock}";
        var generator = world.AddComponent<WorldGenerator>();
        (generator.Seed, generator.Width, generator.Height, generator.Surface) = (2026, WorldWidth, WorldHeight, 24);
        (generator.Hills, generator.HillWidth, generator.DirtDepth, generator.Caves) = (6, 26, 5, 0.32f);
        (generator.GrassTile, generator.DirtTile, generator.StoneTile) = (TemplateArt.GrassBlock, TemplateArt.DirtBlock, TemplateArt.StoneBlock);
        generator.Ores = $"{TemplateArt.CoalBlock} 7%, {TemplateArt.IronBlock} 4%, {TemplateArt.GoldBlock} 2%";
        (generator.SandTile, generator.Sand) = (TemplateArt.SandBlock, 0.15f);
        (generator.TrunkTile, generator.LeavesTile, generator.Trees) = (TemplateArt.LogBlock, TemplateArt.LeavesBlock, 0.18f);
        generator.BottomTile = TemplateArt.BedrockBlock;
        generator.Walls = "Cave Walls";
        map.Tiles = WorldGen.Generate(generator, generator.Seed);

        var blocks = world.AddComponent<Blocks>();
        blocks.Items =
        [
            $"Dirt {TemplateArt.DirtBlock}", $"Stone {TemplateArt.StoneBlock}", $"Wood {TemplateArt.LogBlock}", $"Leaves {TemplateArt.LeavesBlock}",
            $"Sand {TemplateArt.SandBlock}", $"Planks {TemplateArt.PlanksBlock}", $"Torch {TemplateArt.TorchBlock}", $"Workbench {TemplateArt.WorkbenchBlock}",
            $"Bricks {TemplateArt.BricksBlock}", $"Glass {TemplateArt.GlassBlock}", $"Furnace {TemplateArt.FurnaceBlock}",
            $"Iron Ore {TemplateArt.IronBlock}", $"Gold Ore {TemplateArt.GoldBlock}",
        ];
        blocks.Drops = $"{TemplateArt.GrassBlock} Dirt, {TemplateArt.CoalBlock} Coal, {TemplateArt.LeavesBlock} nothing";
        blocks.Hardness = $"{TemplateArt.StoneBlock} 0.8, {TemplateArt.CoalBlock}-{TemplateArt.IronBlock} 1, {TemplateArt.GoldBlock} 1.2, " +
                          $"{TemplateArt.BricksBlock} 1, {TemplateArt.FurnaceBlock} 1, {TemplateArt.LogBlock} 0.6, {TemplateArt.PlanksBlock} 0.5, " +
                          $"{TemplateArt.LeavesBlock} 0.1, {TemplateArt.TorchBlock} 0.05, {TemplateArt.GlassBlock} 0.3";
        blocks.Unbreakable = $"{TemplateArt.BedrockBlock}";
        blocks.FallingTiles = $"{TemplateArt.SandBlock}";
        blocks.GlowingTiles = $"{TemplateArt.TorchBlock}";
        (blocks.Underground, blocks.DarkDepth) = (Color.FromBytes(0x16, 0x18, 0x24), 6);
        blocks.BreakEffect = "Crumbs";
        blocks.SaveAs = null;
        scene.Add(world);

        // Behind the blocks, darker: what caves and tunnels show instead of the sky.
        var caveWalls = new Entity("Cave Walls");
        caveWalls.Transform.Position = world.Transform.Position;
        var wallMap = caveWalls.AddComponent<Tilemap>();
        wallMap.Image = TemplateArt.BlockSheet;
        (wallMap.Columns, wallMap.Rows, wallMap.Solid, wallMap.Order) = (TemplateArt.BlockColumns, TemplateArt.BlockRows, false, -6);
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

    // A line of help in a corner of the screen.
    private static Entity Help(string text, Anchor corner)
    {
        var help = Label("Help", Vector2.Zero, text, 0.32f, Color.FromBytes(0xE6, 0xE8, 0xEC));
        help.GetComponent<Text>()!.Align = TextAlign.Left;
        help.GetComponent<Text>()!.Order = 200;
        var anchor = help.AddComponent<ScreenAnchor>();
        anchor.Anchor = corner;
        anchor.Offset = new Vector2(0.4f, corner == Anchor.TopLeft ? -0.45f : 0.45f);
        return help;
    }

    // ----- Contraption -----

    private static readonly Color WallGray = Color.FromBytes(0x3A, 0x3F, 0x4B);

    private static Scene Contraption()
    {
        var scene = new Scene();

        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 8.5f;
        view.Background = Color.FromBytes(0x1E, 0x22, 0x2B);
        // A little dim, so the lamps show.
        view.Ambient = Color.FromBytes(0xA8, 0xAE, 0xC0);
        var follow = camera.AddComponent<Follow>();
        (follow.Target, follow.StayInside) = ("Player", "Backdrop");
        camera.Transform.Position = new Vector3(-3, 0, 0);
        scene.Add(camera);

        var backdrop = new Entity("Backdrop");
        var wall = backdrop.AddComponent<Sprite>();
        wall.Size = new Vector2(40, 17);
        wall.Tint = Color.FromBytes(0x2A, 0x2F, 0x3A);
        wall.Order = -10;
        scene.Add(backdrop);

        var room = new Entity("Room");
        scene.Add(room);
        scene.Add(Box("Floor", new Vector2(0, -8), new Vector2(40, 1), WallGray), room);
        scene.Add(Box("Ceiling", new Vector2(0, 8), new Vector2(40, 1), WallGray), room);
        scene.Add(Box("Wall", new Vector2(-19.5f, 0), new Vector2(1, 17), WallGray), room);
        scene.Add(Box("Wall", new Vector2(19.5f, 0), new Vector2(1, 17), WallGray), room);

        var player = Box("Player", new Vector2(-3, -6.6f), new Vector2(0.8f, 1.4f), Color.FromBytes(0xF0, 0x8C, 0x2E));
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Body>();
        player.AddComponent<PlatformerMovement>();
        var tools = player.AddComponent<SandboxTools>();
        tools.Props = "Crate, Ball, Plank, Balloon, Thruster, Button, Lamp, Toggle, And Gate, Not Gate, Clock, Display, Sensor";
        scene.Add(player);

        // A lamp hanging from the ceiling on a rope, and a button on the floor wired to it.
        var hook = new Entity("Hook");
        hook.Transform.Position = new Vector3(-16, 7.5f, 0);
        scene.Add(hook);
        var lamp = Placed(LampProp(), new Vector2(-16, 3));
        var rope = lamp.AddComponent<Rope>();
        rope.To = "Hook";
        scene.Add(lamp);
        var button = Placed(ButtonProp(), new Vector2(-17.5f, -7.1f));
        button.GetComponent<Body>()!.Frozen = true;
        scene.Add(button);
        Wire(lamp, "On", "Button", "On");

        // A lift: a plank on two thrusters, welded together, that rise while the lift button is on.
        var lift = new Entity("Lift");
        scene.Add(lift);
        var plank = Placed(PlankProp(), new Vector2(-9, -6.5f));
        plank.Name = "Lift Plank";
        scene.Add(plank, lift);
        foreach (var (x, name) in new[] { (-10.5f, "Left Thruster"), (-7.5f, "Right Thruster") })
        {
            var thruster = Placed(ThrusterProp(), new Vector2(x, -7.1f));
            thruster.Name = name;
            thruster.AddComponent<Weld>().To = "Lift Plank";
            Wire(thruster, "Power", "Lift Button", "On");
            scene.Add(thruster, lift);
        }

        var liftButton = Placed(ButtonProp(), new Vector2(-13, -7.1f));
        liftButton.Name = "Lift Button";
        liftButton.GetComponent<Body>()!.Frozen = true;
        scene.Add(liftButton, lift);

        // A door held shut by logic: the wall shows while the plate is empty (NOT the sensor). Drop a crate on it.
        var door = new Entity("Door");
        scene.Add(door);
        var plate = Placed(SensorProp(), new Vector2(3, -7.35f));
        plate.Name = "Plate";
        scene.Add(plate, door);
        var not = Placed(GateProp("Not Gate", GateKind.Not), new Vector2(5, -2));
        not.GetComponent<Body>()!.Frozen = true;
        Wire(not, "A", "Plate", "Out");
        scene.Add(not, door);
        var gateWall = Box("Gate Wall", new Vector2(8, 0), new Vector2(0.6f, 15), Color.FromBytes(0xC9, 0x5B, 0x3C));
        Wire(gateWall, "Show", "Not Gate", "Out");
        scene.Add(gateWall, door);
        scene.Add(Placed(CrateProp(), new Vector2(0, -7)), door);

        // Past the door: a clock blinking a lamp and counting on a display, and a balloon tied to a heavy crate.
        var machine = new Entity("Machine");
        scene.Add(machine);
        var clock = Placed(GateProp("Clock", GateKind.Clock), new Vector2(11, 0));
        clock.GetComponent<WireGate>()!.Value = 0.5f;
        clock.GetComponent<Body>()!.Frozen = true;
        scene.Add(clock, machine);
        var counter = Placed(GateProp("Counter", GateKind.Counter), new Vector2(13.5f, 0));
        counter.GetComponent<Body>()!.Frozen = true;
        Wire(counter, "A", "Clock", "Out");
        scene.Add(counter, machine);
        var display = Placed(DisplayProp(), new Vector2(16.5f, 0));
        display.GetComponent<Body>()!.Frozen = true;
        Wire(display, "Value", "Counter", "Out");
        scene.Add(display, machine);
        var blinker = Placed(LampProp(), new Vector2(11, 3));
        blinker.Name = "Blinker";
        blinker.GetComponent<Body>()!.Frozen = true;
        Wire(blinker, "On", "Clock", "Out");
        scene.Add(blinker, machine);
        var heavy = Placed(CrateProp(), new Vector2(15, -7));
        heavy.Name = "Heavy Crate";
        heavy.GetComponent<Body>()!.Mass = 2;
        scene.Add(heavy, machine);
        var balloon = Placed(BalloonProp(), new Vector2(15, -4));
        balloon.AddComponent<Rope>().To = "Heavy Crate";
        scene.Add(balloon, machine);

        scene.Add(Help("1-6 or the wheel: pick a tool.   Drag things to carry them.   E presses buttons.   Z undoes.", Anchor.BottomLeft));
        AddPauseMenu(scene, menuScene: null);
        return scene;
    }

    private static Dictionary<string, Entity> ContraptionPrefabs() => new Dictionary<string, Entity>
    {
        ["Prefabs/Crate.prefab"] = CrateProp(),
        ["Prefabs/Ball.prefab"] = BallProp(),
        ["Prefabs/Plank.prefab"] = PlankProp(),
        ["Prefabs/Balloon.prefab"] = BalloonProp(),
        ["Prefabs/Thruster.prefab"] = ThrusterProp(),
        ["Prefabs/Button.prefab"] = ButtonProp(),
        ["Prefabs/Lamp.prefab"] = LampProp(),
        ["Prefabs/Toggle.prefab"] = GateProp("Toggle", GateKind.Toggle),
        ["Prefabs/And Gate.prefab"] = GateProp("And Gate", GateKind.And),
        ["Prefabs/Not Gate.prefab"] = GateProp("Not Gate", GateKind.Not),
        ["Prefabs/Clock.prefab"] = ClockProp(),
        ["Prefabs/Display.prefab"] = DisplayProp(),
        ["Prefabs/Sensor.prefab"] = SensorProp(),
    };

    private static Entity Placed(Entity entity, Vector2 position)
    {
        entity.Transform.Position = new Vector3(position, entity.Transform.Position.Z);
        return entity;
    }

    private static void Wire(Entity to, string input, string from, string output)
    {
        var wire = to.AddComponent<Scenes.Wire>();
        (wire.Input, wire.From, wire.Output) = (input, from, output);
    }

    // A prop: a picture (or a colored box), a collider that fits it, and a body.
    private static Entity Prop(string name, string? image, Vector2 size, Color tint, float mass, bool round = false)
    {
        var entity = new Entity(name);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Tint) = (image, size, tint);
        if (round)
            entity.AddComponent<CircleCollider>().Radius = size.X / 2;
        else
            entity.AddComponent<BoxCollider>().Size = size;
        var body = entity.AddComponent<Body>();
        (body.Mass, body.Friction) = (mass, 12);
        return entity;
    }

    private static Entity CrateProp() => Prop("Crate", TemplateArt.CrateArt, Vector2.One, Color.White, 1);

    private static Entity BallProp()
    {
        var ball = Prop("Ball", TemplateArt.BallArt, new Vector2(0.8f), Color.White, 0.6f, round: true);
        ball.GetComponent<Body>()!.Bounciness = 0.7f;
        return ball;
    }

    private static Entity PlankProp() => Prop("Plank", null, new Vector2(4, 0.4f), Color.FromBytes(0xB0, 0x7F, 0x4A), 1.5f);

    // As light as it is lifting: one holds up a crate, two carry it off.
    private static Entity BalloonProp()
    {
        var balloon = Prop("Balloon", TemplateArt.BalloonArt, new Vector2(0.9f), Color.White, 0.8f, round: true);
        balloon.GetComponent<Body>()!.GravityScale = -1.25f;
        return balloon;
    }

    private static Entity ThrusterProp()
    {
        var entity = Prop("Thruster", TemplateArt.ThrusterArt, new Vector2(0.8f), Color.White, 0.5f);
        entity.AddComponent<Thruster>().Force = 45;
        var flame = entity.AddComponent<ParticleEmitter>();
        flame.Emitting = false;
        (flame.Rate, flame.Lifetime, flame.Speed, flame.Direction, flame.Spread) = (60, 0.25f, 6, 270, 20);
        (flame.StartSize, flame.EndSize) = (0.35f, 0.05f);
        (flame.StartColor, flame.EndColor) = (Color.FromBytes(0xFF, 0xE0, 0x6A), Color.FromBytes(0xE5, 0x53, 0x4B) with { A = 0 });
        flame.Order = -1;
        return entity;
    }

    private static Entity ButtonProp()
    {
        var entity = Prop("Button", TemplateArt.ButtonArt, new Vector2(0.8f), Color.FromBytes(0xE5, 0x53, 0x4B), 0.3f);
        entity.AddComponent<WireButton>();
        return entity;
    }

    private static Entity LampProp()
    {
        var entity = Prop("Lamp", TemplateArt.LampArt2, new Vector2(0.8f), Color.White, 0.4f);
        entity.AddComponent<WireLamp>();
        var light = entity.AddComponent<Light>();
        (light.Radius, light.Intensity, light.Color) = (6, 1.2f, Color.FromBytes(0xFF, 0xE8, 0xA8));
        return entity;
    }

    private static Entity GateProp(string name, GateKind kind)
    {
        var entity = Prop(name, TemplateArt.ChipArt, new Vector2(1.2f, 0.9f), Color.White, 0.3f);
        entity.AddComponent<WireGate>().Kind = kind;
        return entity;
    }

    private static Entity ClockProp()
    {
        var clock = GateProp("Clock", GateKind.Clock);
        clock.GetComponent<WireGate>()!.Value = 0.5f;
        return clock;
    }

    private static Entity DisplayProp()
    {
        var entity = Prop("Display", TemplateArt.ChipArt, new Vector2(1.6f, 0.9f), Color.FromBytes(0x5A, 0x60, 0x70), 0.3f);
        entity.AddComponent<WireDisplay>().Format = "0";
        return entity;
    }

    // A plate that notices whatever's on it; frozen, so it stays where it's put.
    private static Entity SensorProp()
    {
        var entity = new Entity("Sensor");
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint) = (new Vector2(2, 0.3f), Color.FromBytes(0x57, 0xD3, 0x6B));
        var collider = entity.AddComponent<BoxCollider>();
        (collider.Size, collider.Offset, collider.IsTrigger) = (new Vector2(2, 0.8f), new Vector2(0, 0.3f), true);
        entity.AddComponent<Body>().Frozen = true;
        entity.AddComponent<WireSensor>();
        return entity;
    }
}
