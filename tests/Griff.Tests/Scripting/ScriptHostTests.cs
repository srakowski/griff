using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Scripting;

public class ScriptHostTests
{
    private const string MoverV1 = """
        public class Mover : Behavior
        {
            public float Speed { get; set; } = 1;

            protected override void Update() => Transform.Position += new Vector3(Speed, 0, 0);
        }
        """;

    // Same property, new behavior: moves up instead of right.
    private const string MoverV2 = """
        public class Mover : Behavior
        {
            public float Speed { get; set; } = 1;

            protected override void Update() => Transform.Position += new Vector3(0, Speed, 0);
        }
        """;

    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();
    private readonly ScriptHost _host;

    public ScriptHostTests()
    {
        _host = new ScriptHost(_registry);
    }

    [Fact]
    public void Scripts_CanUseClipsMessagesPathsFollowAndDebugDrawing()
    {
        const string source = """
            public class Guard : Behavior
            {
                public int Alarms { get; set; }

                protected override void Start()
                {
                    On("Alarm", () => Alarms++);
                    On<int>("Noise", volume => Alarms += volume);
                    GetComponent<Animation>()?.Play("Walk");
                    if (Game.Find("Main Camera")?.GetComponent<Follow>() is { } follow)
                        follow.TargetEntity = Entity;
                }

                protected override void Update()
                {
                    var here = Transform.WorldPosition;
                    var level = Game.Find<Tilemap>();
                    var path = level?.FindPath(here, Vector3.Zero) ?? [];
                    Debug.Path(path, Color.Yellow);
                    Debug.Circle(here, 3, Color.Red);
                    Debug.Text(here + new Vector3(0, 1, 0), Alarms);
                    if (Input.WasPressed(Key.Space))
                        Game.Send("Alarm");
                }
            }
            """;

        var result = _host.Reload([new ScriptSource("Guard.cs", source)], []);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void Scripts_CanUseSwitchesInventoriesAndTrails()
    {
        // The README's puzzle examples, as a script would write them.
        const string source = """
            public class Vault : Behavior
            {
                protected override void Start()
                {
                    GetComponent<Switch>()!.Changed += on => Game.ShakeCamera(on ? 0.2f : 0.1f);
                }

                protected override void Update()
                {
                    var player = Game.Find("Player")!;
                    if (player.GetComponent<Inventory>()?.Has("Key") == true)
                    {
                        Game.Find("Gate Lever")!.GetComponent<Switch>()!.IsOn = true;
                        Debug.Text(Transform.Position, "I have the key");
                    }

                    Game.Power(Game.Find("Fan")!, on: false);
                    if (GetComponent<Trail>() is { } trail)
                        trail.Emitting = Input.IsDown("Dash");
                    var tile = Game.Find<Tilemap>()?.ShownTile(16);
                }
            }
            """;

        var result = _host.Reload([new ScriptSource("Vault.cs", source)], []);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void Compile_ReportsErrorsWithFileAndLine()
    {
        var result = ScriptCompiler.Compile([new ScriptSource("Broken.cs", "public class Broken : Behavior\n{\n    int x = ;\n}")]);

        Assert.False(result.Success);
        var error = result.Errors.First();
        Assert.Equal("Broken.cs", error.File);
        Assert.Equal(3, error.Line);
    }

    [Fact]
    public void Reload_RegistersScriptComponents()
    {
        var result = _host.Reload([new ScriptSource("Mover.cs", MoverV1)], []);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var type = Assert.Single(result.ComponentTypes);
        Assert.Equal("Mover", type.Name);
        Assert.NotNull(_registry.Find("Mover"));
    }

    // Renaming a script and a property, then changing what a value means, mid-game: nothing set in the scene is lost.
    [Fact]
    public void Reload_OfARenamedScript_KeepsItsValues_ThroughFormerNamesAndUpgrades()
    {
        _host.Reload([new ScriptSource("Mover.cs", MoverV1)], []);
        var scene = new Scene();
        var entity = new Entity("Ship");
        scene.Add(entity);
        var mover = _registry.Find("Mover")!.CreateInstance();
        _registry.Find("Mover")!.Properties.Single(p => p.Name == "Speed").SetValue(mover, 2f);
        entity.AddComponent(mover);

        // Mover becomes Glider, Speed becomes Pace and counts tenths, and Pace's old meaning is upgraded from.
        const string glider = """
            [FormerName("Mover")]
            [ComponentVersion(2)]
            public class Glider : Behavior
            {
                [FormerName("Speed")]
                public float Pace { get; set; } = 10;

                [UpgradeFrom(1)]
                static void ToTenths(SavedData data) => data.Set("Pace", data.Get<float>("Pace") * 10);
            }
            """;
        var result = _host.Reload([new ScriptSource("Mover.cs", glider)], [scene]);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Empty(result.Warnings);
        var replacement = Assert.Single(result.Replaced).Value;
        var descriptor = _registry.Find("Glider")!;
        Assert.IsType(descriptor.Type, replacement);
        Assert.Equal(20f, descriptor.Properties.Single(p => p.Name == "Pace").GetValue(replacement));
        Assert.Same(descriptor, _registry.Find("Mover"));
    }

    [Fact]
    public void Reload_SwapsRunningScripts_AndKeepsTheirProperties()
    {
        _host.Reload([new ScriptSource("Mover.cs", MoverV1)], []);
        var scene = new Scene();
        var entity = new Entity("Ship");
        scene.Add(entity);
        var mover = (Behavior)_registry.Find("Mover")!.CreateInstance();
        _registry.Find("Mover")!.Properties.Single(p => p.Name == "Speed").SetValue(mover, 2f);
        entity.AddComponent(mover);
        var game = new Game(scene, new InputState());

        game.Tick(0.016);
        Assert.Equal(new Vector3(2, 0, 0), entity.Transform.Position);

        // Hot reload mid-game.
        var result = _host.Reload([new ScriptSource("Mover.cs", MoverV2)], [scene]);
        game.Tick(0.016);

        Assert.True(result.Success);
        var replacement = Assert.Single(result.Replaced).Value;
        Assert.Same(entity, replacement.Entity);
        Assert.NotSame(mover, entity.Components[1]);
        Assert.Equal(2f, _registry.Find("Mover")!.Properties.Single(p => p.Name == "Speed").GetValue(replacement));
        Assert.Equal(new Vector3(2, 2, 0), entity.Transform.Position);
    }

    [Fact]
    public void ScriptsSystems_RunEveryFrame_AndAReloadReplacesThem()
    {
        static string Source(string step) => $$"""
            public class Push : GameSystem
            {
                public override FramePhase Phase => FramePhase.Simulate;

                protected override void Update()
                {
                    foreach (var ship in Game.FindAll("Ship"))
                        ship.Transform.Position += new Vector3({{step}}, 0);
                }
            }

            public class Level : Behavior
            {
                public float CameraY { get; set; }

                protected override void Start() => AddSystem(new Push());

                protected override void LateUpdate() => CameraY = Game.Find("Ship")!.Transform.Position.Y;
            }
            """;

        _host.Reload([new ScriptSource("Level.cs", Source("1, 0"))], []);
        var scene = new Scene();
        var ship = new Entity("Ship");
        scene.Add(ship);
        var level = new Entity("Level");
        level.AddComponent(_registry.Find("Level")!.CreateInstance());
        scene.Add(level);
        var game = new Game(scene, new InputState());

        game.Tick(0.016);
        Assert.Equal(new Vector3(1, 0, 0), ship.Transform.Position);

        // The new Level starts again and adds the new Push, which takes the old one's place.
        var result = _host.Reload([new ScriptSource("Level.cs", Source("0, 1"))], [scene]);
        game.Tick(0.016);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Equal(new Vector3(1, 1, 0), ship.Transform.Position);
        Assert.Single(game.Systems, s => s.Name == "Push");
        Assert.Equal(1f, _registry.Find("Level")!.Properties.Single(p => p.Name == "CameraY").GetValue(level.Components[1]));
    }

    [Fact]
    public void FailedReload_LeavesEverythingRunning()
    {
        _host.Reload([new ScriptSource("Mover.cs", MoverV1)], []);
        var scene = new Scene();
        var entity = new Entity();
        scene.Add(entity);
        var mover = entity.AddComponent(_registry.Find("Mover")!.CreateInstance());

        var result = _host.Reload([new ScriptSource("Mover.cs", "public class Mover : Behavior {")], [scene]);

        Assert.False(result.Success);
        Assert.Same(mover, entity.Components[1]);
        Assert.NotNull(_registry.Find("Mover"));
    }

    [Fact]
    public void DeletedScript_BecomesMissing_AndComesBackWhenRestored()
    {
        _host.Reload([new ScriptSource("Mover.cs", MoverV1)], []);
        var scene = new Scene();
        var entity = new Entity();
        scene.Add(entity);
        var mover = _registry.Find("Mover")!.CreateInstance();
        _registry.Find("Mover")!.Properties.Single().SetValue(mover, 7f);
        entity.AddComponent(mover);

        _host.Reload([], [scene]);
        Assert.IsType<MissingComponent>(entity.Components[1]);

        _host.Reload([new ScriptSource("Mover.cs", MoverV1)], [scene]);
        var restored = entity.Components[1];
        Assert.Equal("Mover", restored.GetType().Name);
        Assert.Equal(7f, _registry.Find("Mover")!.Properties.Single().GetValue(restored));
    }
}

public class HistoryRemapTests
{
    [Fact]
    public void UndoAfterScriptReload_TargetsTheRebuiltComponent()
    {
        var registry = ComponentRegistry.CreateDefault();
        var host = new ScriptHost(registry);
        const string source = "public class Health : Behavior { public int Value { get; set; } = 10; }";
        host.Reload([new ScriptSource("Health.cs", source)], []);

        var scene = new Scene();
        var entity = new Entity();
        scene.Add(entity);
        var history = new Griff.Editing.EditHistory();
        history.Execute(new Griff.Editing.AddComponentEdit(entity, registry.Find("Health")!.CreateInstance()));
        history.Execute(new Griff.Editing.SetPropertyEdit(entity.Components[1], registry.Find("Health")!.Properties.Single(), 99));

        var result = host.Reload([new ScriptSource("Health.cs", source + "\n// edited")], [scene]);
        history.RemapComponents(result.Replaced, registry);

        var rebuilt = entity.Components[1];
        var value = registry.Find("Health")!.Properties.Single();
        Assert.Equal(99, value.GetValue(rebuilt));

        history.Undo();
        Assert.Equal(10, value.GetValue(rebuilt));

        history.Undo();
        Assert.Single(entity.Components);

        history.Redo();
        Assert.Same(rebuilt, entity.Components[1]);
    }
}

public class UnchangedReloadTests
{
    [Fact]
    public void ReloadingIdenticalSources_KeepsRunningInstances()
    {
        var registry = ComponentRegistry.CreateDefault();
        var host = new ScriptHost(registry);
        var sources = new[] { new ScriptSource("A.cs", "public class A : Behavior { public int N { get; set; } }") };
        host.Reload(sources, []);
        var scene = new Scene();
        var entity = new Entity();
        scene.Add(entity);
        var instance = entity.AddComponent(registry.Find("A")!.CreateInstance());

        var result = host.Reload(sources, [scene]);

        Assert.True(result.Unchanged);
        Assert.Same(instance, entity.Components[1]);
        Assert.False(host.Reload([new ScriptSource("A.cs", sources[0].Text + " ")], [scene]).Unchanged);
    }
}
