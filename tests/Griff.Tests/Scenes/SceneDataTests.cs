using System.Text.Json;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

// Components whose saved data changed shape over time, the way a game's own would.

/// <summary>Was Lamp (saved as "Griff.Tests.Scenes.Lamp"), and before that "Torch" in another game.</summary>
[FormerName("Lamp")]
[FormerName("OldGame.Torch")]
public class Lantern : Component
{
    [FormerName("Range")]
    [FormerName("Reach")]
    public float Radius { get; set; } = 3;

    public string? Color { get; set; }
}

/// <summary>
/// Version 1 saved a Delay in seconds; version 2 counts frames (at 60 a second); version 3 splits a "Name" holding
/// "first last" into First and Last.
/// </summary>
[ComponentVersion(3)]
public class Timer : Component
{
    public float Delay { get; set; }

    public string? First { get; set; }

    public string? Last { get; set; }

    [UpgradeFrom(1)]
    private static void SecondsToFrames(SavedData data) => data.Set("Delay", data.Get<float>("Delay") * 60);

    [UpgradeFrom(2)]
    private static void SplitName(SavedData data)
    {
        if (data.Get<string>("Name") is not { } name)
            return;
        var parts = name.Split(' ', 2);
        data.Set("First", parts[0]);
        data.Set("Last", parts.Length > 1 ? parts[1] : "");
        data.Remove("Name");
    }
}

[ComponentVersion(2)]
public class MissingStep : Component
{
    public int Value { get; set; }
}

[ComponentVersion(2)]
public class WrongStep : Component
{
    [UpgradeFrom(1)]
    private static int Step(SavedData data) => 0;
}

[FormerName("Lamp")]
public class AlsoWasLamp : Component;

public class SceneDataTests
{
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();
    private readonly SceneSerializer _serializer;

    public SceneDataTests()
    {
        _registry.Register<TestComponent>();
        _registry.Register<Lantern>();
        _registry.Register<Timer>();
        _serializer = new SceneSerializer(_registry);
    }

    [Fact]
    public void UnknownValues_AreKept_AndWrittenBackAfterTheComponentsOwn()
    {
        var scene = Load("""{ "type": "Griff.Tests.Scenes.TestComponent", "count": 4, "glow": [1, 2], "note": "from a newer Griff" }""", out var warnings);

        var component = scene.Roots[0].GetComponent<TestComponent>()!;
        Assert.Equal(4, component.Count);
        Assert.Empty(warnings);

        var saved = SavedComponent(_serializer.SaveToString(scene));
        var keys = saved.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["glow", "note"], keys[^2..]);
        Assert.Equal([1, 2], saved.GetProperty("glow").EnumerateArray().Select(e => e.GetInt32()).ToList());
        Assert.Contains("\"glow\": [1, 2]", _serializer.SaveToString(scene), StringComparison.Ordinal);
        Assert.Equal("from a newer Griff", saved.GetProperty("note").GetString());
    }

    [Fact]
    public void KeptValues_SurviveCopyingAnEntity()
    {
        var scene = Load("""{ "type": "Griff.Tests.Scenes.TestComponent", "note": "keep me" }""", out _);

        var copy = _serializer.Clone(scene.Roots[0]);

        Assert.Equal("keep me", SavedComponent(_serializer.SerializeEntity(copy)).GetProperty("note").GetString());
    }

    [Fact]
    public void RenamedProperty_LoadsFromItsFormerName_AndSavesUnderTheNewOne()
    {
        var scene = Load("""{ "type": "Griff.Tests.Scenes.Lantern", "range": 7.5, "color": "red" }""", out var warnings);

        Assert.Equal(7.5f, scene.Roots[0].GetComponent<Lantern>()!.Radius);
        Assert.Empty(warnings);
        var saved = SavedComponent(_serializer.SaveToString(scene));
        Assert.Equal(7.5f, saved.GetProperty("radius").GetSingle());
        Assert.False(saved.TryGetProperty("range", out _));
    }

    [Fact]
    public void RenamedProperty_PrefersItsCurrentName_WhenBothAreSaved()
    {
        var scene = Load("""{ "type": "Griff.Tests.Scenes.Lantern", "reach": 1, "radius": 2 }""", out _);

        Assert.Equal(2f, scene.Roots[0].GetComponent<Lantern>()!.Radius);
        Assert.False(SavedComponent(_serializer.SaveToString(scene)).TryGetProperty("reach", out _));
    }

    [Theory]
    [InlineData("Griff.Tests.Scenes.Lamp")]
    [InlineData("Lamp")]
    [InlineData("OldGame.Torch")]
    public void RenamedComponent_LoadsFromAFormerName_AndSavesUnderTheNewOne(string formerName)
    {
        var scene = Load($$"""{ "type": "{{formerName}}", "radius": 5 }""", out var warnings);

        Assert.Equal(5f, scene.Roots[0].GetComponent<Lantern>()!.Radius);
        Assert.Empty(warnings);
        Assert.Equal("Griff.Tests.Scenes.Lantern", SavedComponent(_serializer.SaveToString(scene)).GetProperty("type").GetString());
        Assert.Same(_registry.Get(typeof(Lantern)), _registry.Find(formerName));
    }

    [Fact]
    public void TwoComponents_CantClaimTheSameFormerName()
    {
        var registry = new ComponentRegistry();
        registry.Register<Lantern>();

        var error = Assert.Throws<InvalidOperationException>(() => registry.Register<AlsoWasLamp>());
        Assert.Contains("Lamp", error.Message);
    }

    [Fact]
    public void OldData_IsUpgradedStepByStep_AsItLoads()
    {
        // No "version" is version 1: seconds, and a name in one piece.
        var scene = Load("""{ "type": "Griff.Tests.Scenes.Timer", "delay": 2, "name": "Ada Lovelace" }""", out var warnings);

        var timer = scene.Roots[0].GetComponent<Timer>()!;
        Assert.Equal(120f, timer.Delay);
        Assert.Equal("Ada", timer.First);
        Assert.Equal("Lovelace", timer.Last);
        Assert.Empty(warnings);

        var saved = SavedComponent(_serializer.SaveToString(scene));
        Assert.Equal(3, saved.GetProperty("version").GetInt32());
        Assert.False(saved.TryGetProperty("name", out _));
    }

    [Fact]
    public void Data_IsOnlyUpgradedFromTheVersionItWasSavedAt()
    {
        var scene = Load("""{ "type": "Griff.Tests.Scenes.Timer", "version": 2, "delay": 30, "name": "Grace Hopper" }""", out _);

        var timer = scene.Roots[0].GetComponent<Timer>()!;
        Assert.Equal(30f, timer.Delay);
        Assert.Equal("Hopper", timer.Last);
    }

    [Fact]
    public void CurrentData_LoadsAsSaved_AndVersionOneIsntWritten()
    {
        var scene = Load("""{ "type": "Griff.Tests.Scenes.Timer", "version": 3, "delay": 30 }""", out _);
        Assert.Equal(30f, scene.Roots[0].GetComponent<Timer>()!.Delay);

        var plain = new Scene();
        plain.Add(new Entity("Thing"));
        plain.Roots[0].AddComponent<TestComponent>();
        Assert.False(SavedComponent(_serializer.SaveToString(plain)).TryGetProperty("version", out _));
    }

    [Fact]
    public void DataFromANewerVersion_LoadsWhatItCan_WithAWarning()
    {
        var scene = Load("""{ "type": "Griff.Tests.Scenes.Timer", "version": 9, "delay": 30, "ticks": 4 }""", out var warnings);

        Assert.Equal(30f, scene.Roots[0].GetComponent<Timer>()!.Delay);
        Assert.Contains(warnings, w => w.Contains("newer version", StringComparison.Ordinal));
        Assert.Equal(4, SavedComponent(_serializer.SaveToString(scene)).GetProperty("ticks").GetInt32());
    }

    [Fact]
    public void AVersion_WithoutAStepFromEachEarlierOne_IsRejected()
    {
        var registry = new ComponentRegistry();

        Assert.Contains("[UpgradeFrom(1)]", Assert.Throws<NotSupportedException>(() => registry.Register<MissingStep>()).Message);
        Assert.Contains("static void Step(SavedData data)", Assert.Throws<NotSupportedException>(() => registry.Register<WrongStep>()).Message);
    }

    [Fact]
    public void Descriptors_SayTheirVersionAndFormerNames()
    {
        var lantern = _registry.Get(typeof(Lantern));
        Assert.Equal(1, lantern.Version);
        Assert.Equal(["Griff.Tests.Scenes.Lamp", "Lamp", "OldGame.Torch"], lantern.FormerNames.Order(StringComparer.Ordinal));
        Assert.Equal(["Range", "Reach"], lantern.Properties.Single(p => p.Name == "Radius").FormerNames.Order(StringComparer.Ordinal));
        Assert.Equal(3, _registry.Get(typeof(Timer)).Version);
    }

    [Fact]
    public void ARestoredMissingComponent_IsUpgradedAndRenamedToo()
    {
        var registry = new ComponentRegistry();
        registry.Register<Transform>();
        var serializer = new SceneSerializer(registry);
        var scene = serializer.LoadFromString(Scene("""{ "type": "Lamp", "range": 4, "extra": true }"""));
        var missing = Assert.IsType<MissingComponent>(scene.Roots[0].Components[1]);

        var snapshot = ComponentSwap.Capture([scene], serializer, _ => false);
        registry.Register<Lantern>();
        ComponentSwap.Restore(snapshot, serializer);

        var lantern = scene.Roots[0].GetComponent<Lantern>()!;
        Assert.Equal(4f, lantern.Radius);
        Assert.NotSame(missing, scene.Roots[0].Components[1]);
        Assert.True(SavedComponent(serializer.SaveToString(scene)).GetProperty("extra").GetBoolean());
    }

    [Fact]
    public void SavedData_ReadsAndWritesValuesByTheirNameInCode()
    {
        var data = new SavedData(new System.Text.Json.Nodes.JsonObject());

        data.Set("MoveSpeed", 2.5f);
        data.Set("Offset", new System.Numerics.Vector2(1, 2));
        data.Rename("MoveSpeed", "Speed");

        Assert.False(data.Has("MoveSpeed"));
        Assert.Equal(2.5f, data.Get<float>("Speed"));
        Assert.Equal(new System.Numerics.Vector2(1, 2), data.Get<System.Numerics.Vector2>("Offset"));
        Assert.Equal(7, data.Get("Missing", 7));
        Assert.Equal(-1, data.Get("Offset", -1));
        Assert.True(data.Json.ContainsKey("speed"));
    }

    [Fact]
    public void BuiltInComponents_AreFound_NotListed()
    {
        var found = ComponentRegistry.CreateDefault().All.Select(d => d.Type).ToHashSet();
        var engine = ModuleAssemblies.All.Prepend(typeof(Component).Assembly).SelectMany(a => a.GetTypes())
            .Where(t => t.IsSubclassOf(typeof(Component)) && !t.IsAbstract && t != typeof(MissingComponent) && t.GetConstructor(Type.EmptyTypes) is not null);

        Assert.Equal(engine.ToHashSet(), found);
        Assert.Equal(typeof(Transform), ComponentRegistry.CreateDefault().All.First().Type);
    }

    [Fact]
    public void ComponentTypesIn_FindsAnAssemblysConcreteComponents()
    {
        var types = ComponentRegistry.ComponentTypesIn(typeof(SceneDataTests).Assembly);

        Assert.Contains(typeof(Lantern), types);
        Assert.Contains(typeof(TestComponent), types);
        Assert.DoesNotContain(types, t => t.IsAbstract);
    }

    private Scene Load(string component, out List<string> warnings)
    {
        warnings = [];
        return _serializer.LoadFromString(Scene(component), warnings);
    }

    private static string Scene(string component) => $$"""
        { "formatVersion": 1, "entities": [ { "id": "{{Guid.NewGuid()}}", "name": "Thing", "components": [ { "type": "Transform" }, {{component}} ] } ] }
        """;

    // The first component after the Transform, as saved.
    private static JsonElement SavedComponent(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var entity = root.TryGetProperty("entities", out var entities) ? entities[0] : root;
        return entity.GetProperty("components")[1].Clone();
    }
}
