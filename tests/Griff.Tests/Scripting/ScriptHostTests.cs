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
