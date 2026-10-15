using System.Numerics;
using Griff.Audio;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using static Griff.Projects.ProjectTemplates;

namespace Griff.Modules.Contraptions;

// The Contraption template: a room of props to build machines from with tools and wires (Garry's Mod and Wiremod). All
// no-code components.
internal sealed class ContraptionTemplate : ProjectTemplate
{
    public override string Description => "Build machines in a room full of props: grab and throw things, freeze them in the air, spawn crates, wheels, balloons, thrusters, lamps, buttons and logic gates, weld, hinge and rope them together (a cart on wheels, a windmill), and wire buttons to thrusters to make a lift. A/D to walk, Space to jump, 1-7 for tools, E to press buttons, Z to undo.";

    public override int Order => 70;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Bright, seed: 7) with { Volume = 0.5f };

    protected override Scene BuildStartScene() => Contraption();

    protected override Dictionary<string, Entity> BuildPrefabs() => ContraptionPrefabs();

    public override IReadOnlyDictionary<string, byte[]> Images() => ContraptionsArt.Files();

    protected internal override void WireSounds(Entity entity)
    {
        base.WireSounds(entity);
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case SandboxTools t:
                    TemplateSounds.Set(t.Sound, s => t.Sound = s, TemplateSounds.Place);
                    break;
                case WireButton w:
                    TemplateSounds.Set(w.Sound, s => w.Sound = s, TemplateSounds.Switch);
                    break;
            }
        }
    }

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
        tools.Props = "Crate, Ball, Wheel, Plank, Balloon, Thruster, Button, Lamp, Toggle, And Gate, Not Gate, Clock, Display, Sensor";
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

        // A windmill: a plank pinned to the wall at its middle, turned by its hinge's motor.
        var windmill = Placed(PlankProp(), new Vector2(13.5f, 4.5f));
        windmill.Name = "Windmill";
        var hinge = windmill.AddComponent<HingeJoint>();
        (hinge.Motor, hinge.MotorSpeed, hinge.MotorStrength) = (true, 60, 400);
        scene.Add(windmill, machine);

        scene.Add(Help("1-7 or the wheel: pick a tool.   Drag things to carry them.   E presses buttons.   Z undoes.", Anchor.BottomLeft));
        AddPauseMenu(scene, menuScene: null);
        return scene;
    }

    private static Dictionary<string, Entity> ContraptionPrefabs() => new Dictionary<string, Entity>
    {
        ["Prefabs/Crate.prefab"] = CrateProp(),
        ["Prefabs/Ball.prefab"] = BallProp(),
        ["Prefabs/Wheel.prefab"] = WheelProp(),
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
        var wire = to.AddComponent<global::Griff.Modules.Contraptions.Wire>();
        (wire.Input, wire.From, wire.Output) = (input, from, output);
    }

    // A prop: a picture (or a colored box), a collider that fits it, and a body that turns, so what's built with them tips,
    // tumbles and swings.
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
        (body.Mass, body.Friction, body.Rotates) = (mass, 12, true);
        return entity;
    }

    private static Entity CrateProp() => Prop("Crate", TemplateArt.CrateArt, Vector2.One, Color.White, 1);

    // A bouncy ball that rolls: along the floor, down planks and off whatever it's kicked into.
    private static Entity BallProp()
    {
        var ball = Prop("Ball", ContraptionsArt.BallArt, new Vector2(0.8f), Color.White, 0.6f, round: true);
        ball.GetComponent<Body>()!.Bounciness = 0.7f;
        return ball;
    }

    // A wheel to hinge to a plank or a crate (the Hinge tool): a cart. Wire a button to its Power to run its motor.
    private static Entity WheelProp()
    {
        var wheel = Prop("Wheel", ContraptionsArt.WheelArt, new Vector2(0.9f), Color.White, 0.5f, round: true);
        var body = wheel.GetComponent<Body>()!;
        (body.Grip, body.RollingResistance) = (1, 0.02f);
        return wheel;
    }

    private static Entity PlankProp() => Prop("Plank", null, new Vector2(4, 0.4f), Color.FromBytes(0xB0, 0x7F, 0x4A), 1.5f);

    // As light as it is lifting: one holds up a crate, two carry it off.
    private static Entity BalloonProp()
    {
        var balloon = Prop("Balloon", ContraptionsArt.BalloonArt, new Vector2(0.9f), Color.White, 0.8f, round: true);
        balloon.GetComponent<Body>()!.GravityScale = -1.25f;
        return balloon;
    }

    private static Entity ThrusterProp()
    {
        var entity = Prop("Thruster", ContraptionsArt.ThrusterArt, new Vector2(0.8f), Color.White, 0.5f);
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
        var entity = Prop("Button", ContraptionsArt.ButtonArt, new Vector2(0.8f), Color.FromBytes(0xE5, 0x53, 0x4B), 0.3f);
        entity.AddComponent<WireButton>();
        return entity;
    }

    private static Entity LampProp()
    {
        var entity = Prop("Lamp", ContraptionsArt.LampArt2, new Vector2(0.8f), Color.White, 0.4f);
        entity.AddComponent<WireLamp>();
        var light = entity.AddComponent<Light>();
        (light.Radius, light.Intensity, light.Color) = (6, 1.2f, Color.FromBytes(0xFF, 0xE8, 0xA8));
        return entity;
    }

    private static Entity GateProp(string name, GateKind kind)
    {
        var entity = Prop(name, ContraptionsArt.ChipArt, new Vector2(1.2f, 0.9f), Color.White, 0.3f);
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
        var entity = Prop("Display", ContraptionsArt.ChipArt, new Vector2(1.6f, 0.9f), Color.FromBytes(0x5A, 0x60, 0x70), 0.3f);
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
