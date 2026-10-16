using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Griff.Agent;
using Griff.Data;
using Griff.Modules.Cards;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Scenes;

// What a component exposes is worked out once per type, in its ComponentModel, and everything that reads a component asks
// that: saving, triggers and wires, focus, areas, the network, tables, cards and agents. Every cache keyed by a type holds
// it weakly (WeakTypeCacheTests, in the editor's tests, keeps that for every assembly), so a script compiled again leaves the
// old one to unload.
public class ComponentModelTests
{
    public sealed class Stats : Component
    {
        public float Power { get; set; } = 1;

        public int Level { get; set; } = 1;

        [DoNotSerialize]
        public float Hidden { get; set; } = 5;

        public float Spent { get; private set; } = 2;

        [NotSent]
        public float Mine { get; set; }

        [Sent]
        internal int Charge { get; set; }
    }

    [Fact]
    public void EveryConsumer_AsksTheModel_ForEveryComponent()
    {
        var registry = ComponentRegistry.CreateDefault();
        foreach (var descriptor in registry.All.Where(d => d.Type != typeof(Transform)))
        {
            var model = ComponentModel.Of(descriptor.Type);
            var name = descriptor.Type.Name;
            Assert.Same(model, descriptor.Model);
            Assert.Same(model.Saved, descriptor.Properties);
            Assert.Same(model.Actions, ComponentActions.Of(descriptor.Type));
            Assert.Same(model.Outputs, ComponentOutputs.Of(descriptor.Type));
            Assert.Equal(model.Focus, ComponentFocus.PropertiesOf(descriptor.Type));
            Assert.Equal(model.HasArea, ComponentAreas.Has(descriptor.Type));

            // Tables set what the model says they may, and that's what's saved and takes a table's value.
            var entity = new Entity(name);
            var component = descriptor.CreateInstance();
            entity.AddComponent(component);
            var values = new TableValues();
            entity.AddComponent(values);
            if (component is not TableValues)
                Assert.Equal(model.Settable.Select(p => $"{name}.{p.Name}"), values.Settable());
            Assert.All(model.Settable, p => Assert.Contains(p, model.Saved));

            // The network sends what the model says it does, for a component that doesn't say more for itself.
            if (component is not INetState)
            {
                var sent = NetSchema.Build(entity, registry, ownerMoved: false).Where(s => s.Component == component).Select(s => s.Property?.Name);
                Assert.Equal(model.Sent.Select(p => p.Property.Name), sent);
            }

            // Agents read what's saved and what it says while it runs, and nothing else.
            Assert.DoesNotContain(model.Live, p => model.SavedProperty(p.Name) is not null);
        }
    }

    [Fact]
    public void ACard_Tables_TheNetworkAndAgents_SetOnlyWhatTheModelSays()
    {
        var entity = new Entity("Hero");
        var stats = new Stats();
        entity.AddComponent(stats);
        var model = ComponentModel.Of(typeof(Stats));

        Assert.Equal(["Power", "Level", "Mine"], model.Saved.Select(p => p.Name));
        Assert.Equal(["Power", "Level", "Mine"], model.Settable.Select(p => p.Name));

        // A card raises a number the model says it may set, and never one that isn't saved.
        Assert.True(CardsSystem.Raise(entity, "power", 2));
        Assert.True(CardsSystem.Raise(entity, "Stats.Level", 1.6f));
        Assert.False(CardsSystem.Raise(entity, "Hidden", 2));
        Assert.False(CardsSystem.Raise(entity, "Spent", 2));
        Assert.Equal((3f, 3, 5f, 2f), (stats.Power, stats.Level, stats.Hidden, stats.Spent));

        // A table's row the same.
        var values = new TableValues();
        entity.AddComponent(values);
        Assert.Equal(["Stats.Power", "Stats.Level", "Stats.Mine"], values.Settable());
        values.Table = "Heroes";
        values.Sets = ["Hidden"];
        Assert.NotEmpty(values.LineProblems());

        // The network sends what's saved but what it keeps to itself, and what's marked [Sent].
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Stats>();
        var sent = NetSchema.Build(entity, registry, ownerMoved: false).Where(s => s.Component == stats).Select(s => s.Property!.Name);
        Assert.Equal(["Power", "Level", "Charge"], sent);

        // An agent reaching into the component sets what's saved, and reads that and what it says while running.
        ObjectSurface.Set(stats, "Power", JsonDocument.Parse("4").RootElement);
        Assert.Equal(4, stats.Power);
        Assert.Throws<AgentException>(() => ObjectSurface.Set(stats, "Hidden", JsonDocument.Parse("1").RootElement));
        var described = ObjectSurface.Describe(stats, null)["properties"]!.AsObject();
        Assert.Equal(["Power", "Level", "Mine", "Hidden", "Spent"], described.Select(p => p.Key));
        Assert.Equal(["Power", "Level", "Mine"], described.Where(p => p.Value is System.Text.Json.Nodes.JsonObject).Select(p => p.Key));
    }

    // A script's component that every part of Griff has asked about, in a scene that's played, goes when the script is
    // compiled again, and again: each old assembly unloads.
    [Fact]
    public void AScriptReload_UnloadsTheScriptsItReplaced()
    {
        var registry = ComponentRegistry.CreateDefault();
        var host = new ScriptHost(registry);
        var scene = new Scene();
        var (old, game) = PlayAScriptThenReloadIt(host, registry, scene);
        for (var i = 0; i < 20 && old.Any(o => o.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(old[0].IsAlive, "The first scripts' assembly stayed loaded after two reloads.");
        Assert.False(old[1].IsAlive, "The second scripts' assembly stayed loaded after a reload.");
        game.Tick(1 / 60.0);
        GC.KeepAlive(scene);
    }

    private static string Gadget(int version) => $$"""
        public class Gadget : Behavior
        {
            public float Power { get; set; } = {{version}};

            [RollsFor(nameof(Power))]
            public string PowerRoll { get; set; } = "";

            [Focus]
            public bool Listening { get; set; } = true;

            [Powered]
            public bool On { get; set; } = true;

            [Area]
            public Box Covers => new(Vector2.Zero, Vector2.One);

            [Sent]
            public int Charge { get; set; }

            public int Ticks { get; private set; }

            [Output]
            public float Level => Power;

            [Action]
            public void Boost(float by) => Power += by;

            protected override void Update()
            {
                Ticks += Game.FindAll<Gadget>().Count;
                Charge = Game.FindAll<Behavior>().Count;
            }
        }

        public class GadgetRow
        {
            public string Name { get; set; } = "";

            public float Power { get; set; }
        }
        """;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference[] Old, Game Game) PlayAScriptThenReloadIt(ScriptHost host, ComponentRegistry registry, Scene scene)
    {
        Assert.True(host.Reload([new ScriptSource("Gadget.cs", Gadget(1))], [scene]).Success);
        var old = new WeakReference(AssemblyLoadContext.GetLoadContext(host.Assembly!));
        Touch(registry, scene, 1);

        var game = new Game(scene, new InputState());
        for (var i = 0; i < 3; i++)
            game.Tick(1 / 60.0);

        var reloaded = host.Reload([new ScriptSource("Gadget.cs", Gadget(2))], [scene]);
        Assert.True(reloaded.Success, string.Join("\n", reloaded.Diagnostics));
        Assert.NotEmpty(reloaded.Replaced);
        game.Tick(1 / 60.0);
        var second = new WeakReference(AssemblyLoadContext.GetLoadContext(host.Assembly!));
        Touch(registry, scene, 2);
        game.Tick(1 / 60.0);

        Assert.True(host.Reload([new ScriptSource("Gadget.cs", Gadget(3))], [scene]).Success);
        game.Tick(1 / 60.0);
        return ([old, second], game);
    }

    // Everything that reads a component reads the script's.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Touch(ComponentRegistry registry, Scene scene, int version)
    {
        var descriptor = registry.Find("Gadget")!;
        var entity = new Entity("Thing");
        scene.Add(entity);
        var gadget = descriptor.CreateInstance();
        entity.AddComponent(gadget);
        var values = new TableValues { Table = "Gadgets", Sets = ["Gadget.Power"] };
        entity.AddComponent(values);
        _ = scene.All<Behavior>();

        Assert.NotNull(ComponentActions.Find(gadget, "Boost"));
        Assert.Equal(version, ComponentOutputs.Read(entity, "Level"));
        Assert.True(ComponentFocus.Takes(gadget));
        Assert.NotNull(ComponentAreas.Of(entity));
        Assert.NotEmpty(NetSchema.Build(entity, registry, ownerMoved: true));
        Assert.True(NetLocalAttribute.IsOn(entity) is false && PlayerMovementAttribute.IsOn(entity) is false);
        Assert.Contains("Gadget.Power", values.Settable());
        var tables = new TableSet([new Table("Gadgets", [new TableColumn("Power", ColumnType.Number)], [("Thing", [TableValue.Of(3.0)])])]);
        Assert.Empty(values.Apply(tables, "test"));
        var row = typeof(TableRow).GetMethod(nameof(TableRow.To))!.MakeGenericMethod(gadget.GetType().Assembly.GetType("GadgetRow")!)
            .Invoke(tables["Gadgets"].Rows[0], null)!;
        Assert.Equal(3f, row.GetType().GetProperty("Power")!.GetValue(row));
        Assert.True(CardsSystem.Raise(entity, "Power", 1));
        Assert.NotNull(ObjectSurface.Describe(gadget, null)["properties"]);
        ObjectSurface.Set(gadget, "Power", JsonDocument.Parse("2").RootElement);
        Assert.NotEmpty(ComponentDescription.Of(registry, descriptor));
    }
}
