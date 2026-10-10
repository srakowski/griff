using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public sealed class ContraptionTests
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

    private static Entity Floor(Scene scene)
    {
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -0.5f, 0);
        floor.AddComponent<BoxCollider>().Size = new Vector2(40, 1);
        scene.Add(floor);
        return floor;
    }

    private static Entity Crate(Scene scene, string name, Vector2 at, float gravity = 1)
    {
        var crate = new Entity(name);
        crate.Transform.Position = new Vector3(at, 0);
        crate.AddComponent<Sprite>();
        crate.AddComponent<BoxCollider>();
        crate.AddComponent<Body>().GravityScale = gravity;
        scene.Add(crate);
        return crate;
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    private static Game Start(Scene scene, out InputState input)
    {
        input = new InputState();
        return new Game(scene, input);
    }

    private static void Click(Game game, InputState input, Vector2 at, MouseButton button = MouseButton.Left)
    {
        input.MouseWorld = at;
        input.SetMouseButton(button, true);
        Run(game, input, Frame);
        input.SetMouseButton(button, false);
        Run(game, input, Frame);
    }

    // ----- Physics -----

    [Fact]
    public void AFrozenBody_HoldsStill_AndOthersLandOnIt()
    {
        var scene = new Scene();
        Floor(scene);
        var frozen = Crate(scene, "Frozen", new Vector2(0, 5));
        frozen.GetComponent<Body>()!.Frozen = true;
        var falling = Crate(scene, "Falling", new Vector2(0, 8));
        var game = Start(scene, out var input);

        Run(game, input, 2);

        Assert.Equal(new Vector2(0, 5), At(frozen));
        Assert.Equal(6, At(falling).Y, 2);
        Assert.True(falling.GetComponent<Body>()!.IsGrounded);
    }

    [Fact]
    public void WeldedBodies_MoveAsOne_AndDontBumpEachOther()
    {
        var scene = new Scene();
        Floor(scene);
        var plank = Crate(scene, "Plank", new Vector2(0, 4));
        plank.GetComponent<BoxCollider>()!.Size = new Vector2(3, 0.5f);
        var box = Crate(scene, "Box", new Vector2(1, 4.6f));
        box.AddComponent<Weld>().To = "Plank";
        var game = Start(scene, out var input);
        plank.GetComponent<Body>()!.Velocity = new Vector2(3, 0);

        Run(game, input, 2);

        // Shoved sideways together, they slid along and came to rest with the gap between them kept.
        Assert.Equal(At(plank) + new Vector2(1, 0.6f), At(box), new Vector2Comparer(1e-3f));
        Assert.True(At(plank).X > 0.5f);
        Assert.Equal(0.25f, At(plank).Y, 2);
    }

    [Fact]
    public void ABalloon_WeldedToACrate_LiftsIt()
    {
        var scene = new Scene();
        Floor(scene);
        var crate = Crate(scene, "Crate", new Vector2(0, 0.5f));
        var balloon = Crate(scene, "Balloon", new Vector2(0, 2), gravity: -3);
        balloon.AddComponent<Weld>().To = "Crate";
        var game = Start(scene, out var input);

        Run(game, input, 1);

        Assert.True(At(crate).Y > 3);
        Assert.Equal(1.5f, At(balloon).Y - At(crate).Y, 3);
    }

    [Fact]
    public void WeldedToTheWorld_ABodyHoldsStill()
    {
        var scene = new Scene();
        Floor(scene);
        var wall = new Entity("Wall");
        wall.Transform.Position = new Vector3(-3, 5, 0);
        wall.AddComponent<BoxCollider>();
        scene.Add(wall);
        var crate = Crate(scene, "Crate", new Vector2(-2, 5));
        crate.AddComponent<Weld>().To = "Wall";
        var game = Start(scene, out var input);

        Run(game, input, 1);

        Assert.Equal(new Vector2(-2, 5), At(crate));
    }

    [Fact]
    public void ARope_KeepsAHangingCrateWithinItsLength()
    {
        var scene = new Scene();
        var hook = new Entity("Hook");
        hook.Transform.Position = new Vector3(0, 10, 0);
        scene.Add(hook);
        var crate = Crate(scene, "Crate", new Vector2(2, 10));
        var rope = crate.AddComponent<Rope>();
        rope.To = "Hook";
        var game = Start(scene, out var input);

        Run(game, input, 3);

        Assert.Equal(2, rope.Length, 3);
        Assert.InRange(Vector2.Distance(At(crate), new Vector2(0, 10)), 1.9f, 2.05f);
        Assert.True(At(crate).Y < 10);
    }

    [Fact]
    public void ARopedBalloon_FloatsUpToTheEndOfItsRope_AndPullsAlongWhatItsTiedTo()
    {
        var scene = new Scene();
        Floor(scene);
        var crate = Crate(scene, "Crate", new Vector2(0, 0.5f));
        var balloon = Crate(scene, "Balloon", new Vector2(0, 2), gravity: -0.5f);
        balloon.AddComponent<Rope>().To = "Crate";
        var game = Start(scene, out var input);

        Run(game, input, 2);

        Assert.InRange(At(balloon).Y, 1.9f, 2.1f);
        Assert.InRange(At(crate).Y, 0.49f, 0.55f);
        crate.GetComponent<Body>()!.Velocity = new Vector2(4, 0);
        Run(game, input, 0.5);
        Assert.True(At(balloon).X > 0.5f);
    }

    [Fact]
    public void AThruster_PushesItsBody_TurnedWithIt()
    {
        var scene = new Scene();
        var rocket = Crate(scene, "Rocket", Vector2.Zero, gravity: 0);
        var thruster = rocket.AddComponent<Thruster>();
        thruster.Power = 1;
        thruster.Force = 10;
        var game = Start(scene, out var input);

        Run(game, input, 1);
        Assert.Equal(10, rocket.GetComponent<Body>()!.Velocity.Y, 0);

        rocket.GetComponent<Body>()!.Velocity = Vector2.Zero;
        rocket.Transform.Rotation = new Vector3(0, 0, -90);
        Run(game, input, 1);
        Assert.Equal(10, rocket.GetComponent<Body>()!.Velocity.X, 0);
    }

    // ----- Wiring -----

    private static WireGate Gate(Scene scene, string name, GateKind kind, float a = 0, float b = 0)
    {
        var entity = new Entity(name);
        var gate = entity.AddComponent<WireGate>();
        (gate.Kind, gate.A, gate.B) = (kind, a, b);
        scene.Add(entity);
        return gate;
    }

    private static Wire WireUp(Entity to, string input, string from, string output = "Out")
    {
        var wire = to.AddComponent<Wire>();
        (wire.Input, wire.From, wire.Output) = (input, from, output);
        return wire;
    }

    [Theory]
    [InlineData(GateKind.And, 1, 1, 1)]
    [InlineData(GateKind.And, 1, 0, 0)]
    [InlineData(GateKind.Or, 0, 1, 1)]
    [InlineData(GateKind.Or, 0, 0, 0)]
    [InlineData(GateKind.Not, 0, 0, 1)]
    [InlineData(GateKind.Not, 1, 0, 0)]
    [InlineData(GateKind.Xor, 1, 1, 0)]
    [InlineData(GateKind.Xor, 1, 0, 1)]
    [InlineData(GateKind.Nand, 1, 1, 0)]
    [InlineData(GateKind.Nor, 0, 0, 1)]
    [InlineData(GateKind.Add, 2, 3, 5)]
    [InlineData(GateKind.Subtract, 2, 3, -1)]
    [InlineData(GateKind.Multiply, 2, 3, 6)]
    [InlineData(GateKind.Divide, 3, 0, 0)]
    [InlineData(GateKind.Greater, 3, 2, 1)]
    [InlineData(GateKind.Less, 3, 2, 0)]
    [InlineData(GateKind.Equal, 2, 2, 1)]
    public void Gates_WorkOutTheirOutput(GateKind kind, float a, float b, float expected)
    {
        var scene = new Scene();
        var gate = Gate(scene, "Gate", kind, a, b);
        var game = Start(scene, out var input);

        Run(game, input, Frame);

        Assert.Equal(expected, gate.Out);
    }

    [Fact]
    public void AChainOfGates_AnswersInTheSameFrame()
    {
        var scene = new Scene();
        Gate(scene, "One", GateKind.Constant).Value = 1;
        var not = Gate(scene, "Not", GateKind.Not);
        WireUp(not.Entity!, "A", "One");
        var notNot = Gate(scene, "Not Not", GateKind.Not);
        WireUp(notNot.Entity!, "A", "Not");
        var sum = Gate(scene, "Sum", GateKind.Add);
        WireUp(sum.Entity!, "A", "Not Not");
        WireUp(sum.Entity!, "B", "One");
        var game = Start(scene, out var input);

        Run(game, input, Frame);

        Assert.Equal(0, not.Out);
        Assert.Equal(1, notNot.Out);
        Assert.Equal(2, sum.Out);
    }

    [Fact]
    public void Toggles_Latches_Counters_Delays_AndClocks_RememberWhatHappened()
    {
        var scene = new Scene();
        var toggle = Gate(scene, "Toggle", GateKind.Toggle);
        var latch = Gate(scene, "Latch", GateKind.Latch);
        var counter = Gate(scene, "Counter", GateKind.Counter);
        var delay = Gate(scene, "Delay", GateKind.Delay);
        delay.Value = 0.5f;
        var clock = Gate(scene, "Clock", GateKind.Clock);
        clock.Value = 0.25f;
        var game = Start(scene, out var input);

        void Pulse(params WireGate[] gates)
        {
            foreach (var g in gates)
                g.A = 1;
            Run(game, input, Frame * 2);
            foreach (var g in gates)
                g.A = 0;
            Run(game, input, Frame * 2);
        }

        Pulse(toggle, latch, counter);
        Assert.Equal((1, 1, 1), (toggle.Out, latch.Out, counter.Out));
        Pulse(toggle, counter);
        Assert.Equal((0, 1, 2), (toggle.Out, latch.Out, counter.Out));
        latch.B = 1;
        counter.B = 1;
        Run(game, input, Frame);
        Assert.Equal((0, 0), (latch.Out, counter.Out));

        delay.A = 1;
        Run(game, input, 0.3);
        Assert.Equal(0, delay.Out);
        Run(game, input, 0.3);
        Assert.Equal(1, delay.Out);

        // A clock ticks on and off every quarter second.
        var flips = 0;
        var last = clock.Out;
        for (var i = 0; i < 60; i++)
        {
            Run(game, input, Frame);
            if (clock.Out != last)
                (flips, last) = (flips + 1, clock.Out);
        }

        Assert.InRange(flips, 3, 5);
    }

    [Fact]
    public void PressingE_WithTheMouseOverAButton_LightsTheLampItsWiredTo()
    {
        var scene = new Scene();
        var button = new Entity("Button");
        button.Transform.Position = new Vector3(2, 0, 0);
        button.AddComponent<Sprite>();
        button.AddComponent<BoxCollider>().IsTrigger = true;
        var press = button.AddComponent<WireButton>();
        scene.Add(button);
        var lamp = new Entity("Lamp");
        lamp.AddComponent<Sprite>();
        var light = lamp.AddComponent<Light>();
        light.Intensity = 2;
        var bulb = lamp.AddComponent<WireLamp>();
        WireUp(lamp, "On", "Button", "On");
        scene.Add(lamp);
        var game = Start(scene, out var input);
        input.MouseWorld = new Vector2(2, 0);
        Run(game, input, Frame);
        Assert.Equal(0, light.Intensity);
        Assert.Equal(bulb.OffColor.ToHex(), lamp.GetComponent<Sprite>()!.Tint.ToHex());

        input.SetKey(Key.E, true);
        Run(game, input, Frame);
        input.SetKey(Key.E, false);
        Run(game, input, Frame);

        Assert.True(press.IsOn);
        Assert.Equal(1, bulb.Brightness);
        Assert.Equal(2, light.Intensity);
        Assert.Equal(bulb.OnColor.ToHex(), lamp.GetComponent<Sprite>()!.Tint.ToHex());
        Assert.Equal(1, lamp.GetComponent<Wire>()!.Signal);
    }

    [Fact]
    public void AWireIntoShowOrPower_SwitchesThingsOnAndOff()
    {
        var scene = new Scene();
        Gate(scene, "Switch", GateKind.Constant).Value = 0;
        var bridge = new Entity("Bridge");
        bridge.AddComponent<BoxCollider>();
        WireUp(bridge, "Show", "Switch");
        scene.Add(bridge);
        var lift = new Entity("Lift");
        var mover = lift.AddComponent<Mover>();
        mover.Points = "0 4";
        WireUp(lift, "Power", "Switch");
        scene.Add(lift);
        var game = Start(scene, out var input);

        Run(game, input, Frame * 2);
        Assert.False(bridge.IsEnabled);
        Assert.False(mover.Running);

        game.Find<WireGate>()!.Value = 1;
        Run(game, input, Frame * 2);
        Assert.True(bridge.IsEnabled);
        Assert.True(mover.Running);
    }

    [Fact]
    public void ASensor_CountsWhatsInside_AndDrivesAThruster()
    {
        var scene = new Scene();
        Floor(scene);
        var plate = new Entity("Plate");
        plate.Transform.Position = new Vector3(0, 0.5f, 0);
        plate.AddComponent<BoxCollider>().IsTrigger = true;
        var sensor = plate.AddComponent<WireSensor>();
        scene.Add(plate);
        var rocket = Crate(scene, "Rocket", new Vector2(6, 0.5f));
        rocket.AddComponent<Thruster>().Force = 80;
        WireUp(rocket, "Power", "Plate");
        var game = Start(scene, out var input);
        Run(game, input, 0.5);
        Assert.Equal(0, sensor.Count);
        Assert.Equal(0.5f, At(rocket).Y, 2);

        Crate(scene, "Crate", new Vector2(0, 0.6f));
        Run(game, input, 0.5);

        Assert.Equal(1, sensor.Count);
        Assert.True(At(rocket).Y > 2);
    }

    // ----- Tools -----

    private static (Game Game, InputState Input, Entity Player, SandboxTools Tools) Sandbox(Scene scene)
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 8;
        scene.Add(camera);
        Floor(scene);
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(-6, 1, 0);
        player.AddComponent<BoxCollider>().Layer = "Player";
        player.AddComponent<Body>();
        var tools = player.AddComponent<SandboxTools>();
        tools.Props = "Crate, Lamp";
        scene.Add(player);
        var game = Start(scene, out var input);
        game.PrefabLoader = name => name switch
        {
            "Crate" => Loose(Crate(new Scene(), "Crate", Vector2.Zero)),
            "Lamp" => Lamp(),
            _ => throw new FileNotFoundException(name),
        };
        Run(game, input, Frame);
        return (game, input, player, tools);

        static Entity Loose(Entity entity)
        {
            entity.Scene!.Remove(entity);
            return entity;
        }

        static Entity Lamp()
        {
            var lamp = new Entity("Lamp");
            lamp.AddComponent<Sprite>();
            lamp.AddComponent<BoxCollider>();
            lamp.AddComponent<Body>();
            lamp.AddComponent<WireLamp>();
            return lamp;
        }
    }

    private static void Pick(Game game, InputState input, HudButtonKind kind, string label) =>
        Click(game, input, game.SandboxButtons.Single(b => b.Kind == kind && b.Label == label).Rect.Center);

    [Fact]
    public void TheSpawnTool_PutsDownThePickedProp_WithANameOfItsOwn()
    {
        var scene = new Scene();
        var (game, input, _, _) = Sandbox(scene);

        Pick(game, input, HudButtonKind.Tool, "Spawn");
        Click(game, input, new Vector2(0, 3));
        Click(game, input, new Vector2(2, 3));
        Pick(game, input, HudButtonKind.Prop, "Lamp");
        Click(game, input, new Vector2(4, 3));

        Assert.Equal(["Crate", "Crate 2"], scene.Entities.Where(e => e.Name.StartsWith("Crate")).Select(e => e.Name));
        Assert.NotNull(game.Find("Lamp"));
        Assert.Equal(new Vector2(0, 3), At(game.Find("Crate")!), new Vector2Comparer(0.3f));
    }

    [Fact]
    public void TheGrabTool_CarriesAThing_ThrowsItOnRelease_AndFreezesItWithRightClick()
    {
        var scene = new Scene();
        var crate = Crate(scene, "Crate", new Vector2(0, 0.5f));
        var (game, input, _, _) = Sandbox(scene);

        input.MouseWorld = new Vector2(0, 0.5f);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, Frame);
        Assert.Equal(crate, game.Grabbed);
        input.MouseWorld = new Vector2(3, 4);
        Run(game, input, 0.5);
        Assert.Equal(new Vector2(3, 4), At(crate), new Vector2Comparer(0.2f));

        input.SetMouseButton(MouseButton.Right, true);
        Run(game, input, Frame);
        input.SetMouseButton(MouseButton.Right, false);
        input.SetMouseButton(MouseButton.Left, false);
        Run(game, input, 1);
        Assert.True(crate.GetComponent<Body>()!.Frozen);
        Assert.Equal(new Vector2(3, 4), At(crate), new Vector2Comparer(0.2f));

        // Right-click frees it, and it falls.
        Click(game, input, At(crate), MouseButton.Right);
        Run(game, input, 1);
        Assert.Equal(0.5f, At(crate).Y, 1);
    }

    [Fact]
    public void TheWeldAndRopeTools_JoinTwoThings()
    {
        var scene = new Scene();
        var a = Crate(scene, "A", new Vector2(0, 0.5f));
        var b = Crate(scene, "B", new Vector2(1, 0.5f));
        var c = Crate(scene, "C", new Vector2(4, 0.5f));
        var (game, input, _, _) = Sandbox(scene);

        Pick(game, input, HudButtonKind.Tool, "Weld");
        Click(game, input, At(a));
        Assert.Equal(a, game.ToolFirst);
        Click(game, input, At(b));
        var weld = Assert.Single(a.GetComponents<Weld>());
        Assert.Equal("B", weld.To);

        Pick(game, input, HudButtonKind.Tool, "Rope");
        Click(game, input, At(b));
        Click(game, input, At(c));
        var rope = Assert.Single(b.GetComponents<Rope>());
        Assert.Equal(("C", 3f), (rope.To, rope.Length));
    }

    [Fact]
    public void TheWireTool_AsksWhichInput_WhenThereAreSeveral_AndUndoTakesTheWireAway()
    {
        var scene = new Scene();
        var button = new Entity("Button");
        button.Transform.Position = new Vector3(0, 2, 0);
        button.AddComponent<BoxCollider>();
        button.AddComponent<WireButton>().IsOn = true;
        scene.Add(button);
        var gate = Gate(scene, "Gate", GateKind.And, b: 1);
        gate.Entity!.Transform.Position = new Vector3(3, 2, 0);
        gate.Entity.AddComponent<BoxCollider>();
        var (game, input, _, _) = Sandbox(scene);

        Pick(game, input, HudButtonKind.Tool, "Wire");
        Click(game, input, new Vector2(0, 2));
        Assert.Equal(button, game.ToolFirst);
        Click(game, input, new Vector2(3, 2));
        Assert.Equal(["A", "B", "Power", "Show"], game.SandboxButtons.Where(x => x.Kind == HudButtonKind.Port).Select(x => x.Label));
        Pick(game, input, HudButtonKind.Port, "A");

        var wire = Assert.Single(gate.Entity.GetComponents<Wire>());
        Assert.Equal(("A", "Button", "On"), (wire.Input, wire.From, wire.Output));
        Assert.Equal(1, gate.Out);

        input.SetKey(Key.Z, true);
        Run(game, input, Frame);
        input.SetKey(Key.Z, false);
        Assert.Empty(gate.Entity.GetComponents<Wire>());
    }

    [Fact]
    public void TheRemoveTool_TakesAThingAway_WithTheWiresAndWeldsToIt()
    {
        var scene = new Scene();
        var a = Crate(scene, "A", new Vector2(0, 0.5f));
        var b = Crate(scene, "B", new Vector2(1, 0.5f));
        b.AddComponent<Weld>().To = "A";
        WireUp(b, "Power", "A", "Speed");
        var (game, input, player, _) = Sandbox(scene);

        Pick(game, input, HudButtonKind.Tool, "Remove");
        Click(game, input, At(a));
        Click(game, input, At(player));

        Assert.Null(a.Scene);
        Assert.Empty(b.GetComponents<Weld>());
        Assert.Empty(b.GetComponents<Wire>());
        Assert.NotNull(player.Scene);
    }

    [Fact]
    public void NumberKeys_PickTools_AndTheHudCatchesClicksOnIt()
    {
        var scene = new Scene();
        var (game, input, _, tools) = Sandbox(scene);

        input.SetKey(Key.D2, true);
        Run(game, input, Frame);
        input.SetKey(Key.D2, false);
        Run(game, input, Frame);
        Assert.Equal(SandboxTool.Spawn, tools.Current);

        // A click on the spawn list picks a prop rather than spawning one under it.
        Pick(game, input, HudButtonKind.Prop, "Crate");
        Assert.Null(game.Find("Crate"));
    }

    private sealed class Vector2Comparer(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector2 v) => 0;
    }
}
