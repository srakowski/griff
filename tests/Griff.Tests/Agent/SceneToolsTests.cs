using System.Numerics;
using Griff.Agent;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Agent;

public class SceneToolsTests
{
    private sealed class FakeHost : IAgentScene
    {
        public Scene? Scene { get; } = new();

        public string? ScenePath => "Scenes/Main.scene";

        public bool IsPlaying => false;

        public ComponentRegistry Components { get; } = ComponentRegistry.CreateDefault();

        public EditHistory History { get; } = new();

        public IReadOnlyList<Entity> Selection { get; set; } = [];

        public void Execute(IEdit edit) => History.Execute(edit);

        public IDisposable BeginGroup(string description) => History.BeginGroup(description);

        public Entity? InstantiatePrefab(string name)
        {
            if (name != "Coin")
                return null;
            var coin = new Entity("Coin");
            coin.AddComponent<Sprite>();
            return coin;
        }
    }

    private readonly FakeHost _host = new();
    private readonly Dictionary<string, AgentTool> _tools;

    public SceneToolsTests() => _tools = new SceneTools(_host).GetTools().ToDictionary(t => t.Name);

    // As the server runs them: a mistake comes back as an error result.
    private ToolResult Run(string tool, string json)
    {
        try
        {
            return _tools[tool].Invoke(ToolArgs.Parse(json)).GetAwaiter().GetResult();
        }
        catch (AgentException ex)
        {
            return ToolResult.Error(ex.Message);
        }
    }

    private Entity Named(string name) => _host.Scene!.Entities.Single(e => e.Name == name);

    [Fact]
    public void Create_BuildsComponentsChildrenAndPlacement_AsOneUndoStep()
    {
        var result = Run("entity_create", """
            {"entity": {"name": "Hero", "position": [2, 3], "scale": 2,
              "components": {"Sprite": {"size": [1, 2], "tint": "#FF0000"}, "PlatformerMovement": {"speed": 9}},
              "children": [{"name": "Lantern", "position": {"x": 0.5, "y": 1}, "components": {"Light": {"radius": 6}}}]}}
            """);

        Assert.False(result.IsError, result.Text);
        var hero = Named("Hero");
        Assert.Equal(new Vector3(2, 3, 0), hero.Transform.Position);
        Assert.Equal(new Vector3(2, 2, 1), hero.Transform.Scale);
        Assert.Equal(new Vector2(1, 2), hero.GetComponent<Sprite>()!.Size);
        Assert.Equal(Color.Red, hero.GetComponent<Sprite>()!.Tint);
        Assert.Equal(9, hero.GetComponent<PlatformerMovement>()!.Speed);
        // What it needs came along with it.
        Assert.NotNull(hero.GetComponent<Body>());
        Assert.NotNull(hero.GetComponent<Collider>());
        Assert.Contains("because PlatformerMovement needs it", result.Text);
        var lantern = Assert.Single(hero.Children);
        Assert.Equal(6, lantern.GetComponent<Light>()!.Radius);
        Assert.Equal(new Vector3(0.5f, 1, 0), lantern.Transform.Position);
        Assert.Same(hero, Assert.Single(_host.Selection));

        _host.History.Undo();
        Assert.Equal(0, _host.Scene!.Count);
    }

    [Fact]
    public void Create_ManyAtOnce_UnderAParent()
    {
        Run("entity_create", """{"entity": {"name": "Coins"}}""");

        var result = Run("entity_create", """{"parent": "Coins", "entities": [{"name": "A"}, {"name": "B", "components": [{"type": "Pickup", "amount": 5}]}]}""");

        Assert.False(result.IsError, result.Text);
        Assert.Equal(["A", "B"], Named("Coins").Children.Select(c => c.Name));
        Assert.Equal(5, Named("B").GetComponent<Pickup>()!.Amount);
    }

    [Fact]
    public void Create_FromAPrefab()
    {
        var result = Run("entity_create", """{"entity": {"prefab": "Coin", "name": "Gold Coin", "position": [4, 0]}}""");

        Assert.False(result.IsError, result.Text);
        Assert.NotNull(Named("Gold Coin").GetComponent<Sprite>());
    }

    [Fact]
    public void Create_ExplainsMistakes()
    {
        Assert.Contains("no component \"Sprit\"", Run("entity_create", """{"entity": {"components": {"Sprit": {}}}}""").Text);
        Assert.Contains("has no property \"colour\"", Run("entity_create", """{"entity": {"components": {"Sprite": {"colour": 1}}}}""").Text);
        Assert.Contains("Expected", Run("entity_create", """{"entity": {"components": {"Sprite": {"size": "big"}}}}""").Text);
        Assert.Equal(0, _host.Scene!.Count);
    }

    [Fact]
    public void Update_SetsPropertiesAddsAndRemovesComponents_AsOneUndoStep()
    {
        Run("entity_create", """{"entity": {"name": "Torch", "components": {"Sprite": {}, "Light": {"radius": 3}}}}""");
        Run("entity_create", """{"entity": {"name": "Wall"}}""");
        var torch = Named("Torch");
        var undoBefore = _host.History.UndoDescription;

        var result = Run("entity_update", """
            {"entity": "Torch", "changes": {"name": "Big Torch", "parent": "Wall", "position": [1, 1],
              "components": {"Light": {"radius": 8, "flicker": 0.2}, "ParticleEmitter": {}}, "remove": ["Sprite"]}}
            """);

        Assert.False(result.IsError, result.Text);
        Assert.Equal("Big Torch", torch.Name);
        Assert.Same(Named("Wall"), torch.Parent);
        Assert.Equal(8, torch.GetComponent<Light>()!.Radius);
        Assert.Equal(0.2f, torch.GetComponent<Light>()!.Flicker);
        Assert.NotNull(torch.GetComponent<ParticleEmitter>());
        Assert.Null(torch.GetComponent<Sprite>());

        _host.History.Undo();
        Assert.Equal(undoBefore, _host.History.UndoDescription);
        Assert.Equal("Torch", torch.Name);
        Assert.Null(torch.Parent);
        Assert.Equal(3, torch.GetComponent<Light>()!.Radius);
        Assert.NotNull(torch.GetComponent<Sprite>());
        Assert.Null(torch.GetComponent<ParticleEmitter>());
    }

    [Fact]
    public void Update_TheSecondOfAKind()
    {
        Run("entity_create", """{"entity": {"name": "Lamp", "components": [{"type": "Light", "radius": 1}, {"type": "Light", "radius": 2}]}}""");

        Run("entity_update", """{"entity": "Lamp", "changes": {"components": {"Light#2": {"radius": 9}}}}""");

        Assert.Equal([1f, 9f], Named("Lamp").GetComponents<Light>().Select(l => l.Radius));
    }

    [Fact]
    public void Get_ShowsChangedValuesInTheFormCreateTakes()
    {
        Run("entity_create", """{"entity": {"name": "Sign", "position": [1, 2], "components": {"Dialogue": {"lines": ["Hello", "there"]}}}}""");

        var json = Run("entity_get", """{"entity": "Sign"}""").Text;

        Assert.Contains("\"type\": \"Dialogue\"", json);
        Assert.Contains("\"Hello\"", json);
        Assert.Contains("\"position\": [", json);
        Assert.DoesNotContain("\"scale\"", json); // unchanged from the default
    }

    [Fact]
    public void Select_PicksSeveral_AddsToThem_AndClears()
    {
        Run("entity_create", """{"entities": [{"name": "A"}, {"name": "B"}, {"name": "C"}]}""");

        var result = Run("select", """{"entities": ["C", "A"]}""");
        Assert.Equal(["C", "A"], _host.Selection.Select(e => e.Name));
        Assert.Contains("Selected 2 entities", result.Text);
        Assert.Contains("selected: C #", Run("scene_tree", "{}").Text);

        Run("select", """{"entity": "B", "add": true}""");
        Assert.Equal(["C", "A", "B"], _host.Selection.Select(e => e.Name));
        // Adding one that's already selected makes it the primary one.
        Run("select", """{"entity": "C", "add": true}""");
        Assert.Equal(["A", "B", "C"], _host.Selection.Select(e => e.Name));

        Assert.Contains("Nothing is selected", Run("select", "{}").Text);
        Assert.Empty(_host.Selection);
        Assert.True(Run("select", """{"entities": ["Nope"]}""").IsError);
    }

    [Fact]
    public void Tree_FindDeleteAndClone()
    {
        Run("entity_create", """{"entity": {"name": "Level", "children": [{"name": "Spike", "position": [3, 0], "components": {"DamageOnTouch": {}}}]}}""");

        Assert.Contains("  - Spike #", Run("scene_tree", "{}").Text);
        Assert.Contains("Level/Spike", Run("entity_find", """{"component": "DamageOnTouch"}""").Text);
        Assert.Contains("Nothing matches", Run("entity_find", """{"near": [50, 50]}""").Text);

        Run("entity_clone", """{"entity": "Level/Spike", "positions": [[4, 0], [5, 0]]}""");
        Assert.Equal(3, Named("Level").Children.Count);
        Assert.Equal(new Vector3(5, 0, 0), Named("Level").Children[2].Transform.Position);

        Assert.Contains("3 entities are called \"Spike\"", Run("entity_delete", """{"entities": ["Spike"]}""").Text);
        Run("entity_delete", """{"entities": ["Level"]}""");
        Assert.Equal(0, _host.Scene!.Count);
    }

    [Fact]
    public void Entities_CanBeNamedByIdPrefixOrPath()
    {
        Run("entity_create", """{"entity": {"name": "A", "children": [{"name": "B"}]}}""");
        var b = Named("B");

        Assert.Same(b, SceneTools.Resolve(_host.Scene!, "A/B"));
        Assert.Same(b, SceneTools.Resolve(_host.Scene!, "#" + b.Id.ToString("N")[..6]));
        Assert.Same(b, SceneTools.Resolve(_host.Scene!, b.Id.ToString()));
        Assert.Throws<AgentException>(() => SceneTools.Resolve(_host.Scene!, "C"));
    }

    [Fact]
    public void Describe_ListsEveryPropertyWithItsKindAndDefault()
    {
        var text = Run("component_describe", """{"component": "PlatformerMovement"}""").Text;

        Assert.Contains("# PlatformerMovement (Movement)", text);
        Assert.Contains("Requires: Body", text);
        Assert.Contains("- jumpHeight: a number = ", text);
        Assert.Contains("- airJumps: a whole number = 0", text);
    }

    [Fact]
    public void Describe_SaysWhichPropertiesAreDistances()
    {
        var text = Run("component_describe", """{"component": "Unit"}""").Text.Split('\n');

        Assert.Contains("(A distance:", text.Single(l => l.StartsWith("- sight:", StringComparison.Ordinal)));
        Assert.DoesNotContain("(A distance:", text.Single(l => l.StartsWith("- speed:", StringComparison.Ordinal)));
    }

    [Fact]
    public void ListComponents_ByCategoryAndSearch()
    {
        Assert.Contains("## Movement", Run("components_list", "{}").Text);
        var lights = Run("components_list", """{"search": "torch"}""").Text;
        Assert.Contains("Light", lights);
        Assert.DoesNotContain("## Strategy", lights);
    }

    /// <summary>A component that didn't exist when the tools were written: a stand-in for any future feature or script.</summary>
    [Category("Gameplay")]
    public sealed class Jetpack : Component
    {
        public float Fuel { get; set; } = 10;

        public Vector2 Thrust { get; set; } = new(0, 12);
    }

    [Fact]
    public void ANewComponent_IsUsableAtOnce_WithNoToolWrittenForIt()
    {
        _host.Components.Register<Jetpack>();

        var created = Run("entity_create", """{"entity": {"name": "Pilot", "components": {"Jetpack": {"fuel": 3, "thrust": [0, 20]}}}}""");
        var described = Run("component_describe", """{"component": "Jetpack"}""").Text;
        Run("entity_update", """{"entity": "Pilot", "changes": {"components": {"Jetpack": {"fuel": 1.5}}}}""");

        Assert.False(created.IsError, created.Text);
        Assert.Equal(1.5f, Named("Pilot").GetComponent<Jetpack>()!.Fuel);
        Assert.Equal(new Vector2(0, 20), Named("Pilot").GetComponent<Jetpack>()!.Thrust);
        Assert.Contains("- thrust: [x, y] = [0,12]", described);
        Assert.Contains("Jetpack", Run("components_list", """{"category": "Gameplay"}""").Text);
    }

    [Fact]
    public void Locked_IsCreatedShownAndUpdated()
    {
        Run("entity_create", """{"entity": {"name": "Backdrop", "locked": true, "components": {"Sprite": {}}}}""");
        Run("entity_create", """{"entity": {"name": "Ground"}}""");
        var backdrop = Named("Backdrop");

        Assert.True(backdrop.IsLocked);
        Assert.Contains("Backdrop", Run("scene_tree", "{}").Text.Split('\n').Single(l => l.Contains("locked")));
        Assert.Contains("\"locked\": true", Run("entity_get", """{"entity": "Backdrop"}""").Text);

        var result = Run("entity_update", """{"updates": [{"entity": "Backdrop", "changes": {"locked": false}}, {"entity": "Ground", "changes": {"locked": true}}]}""");
        Assert.False(result.IsError, result.Text);
        Assert.False(backdrop.IsLocked);
        Assert.True(Named("Ground").IsLocked);

        _host.History.Undo();
        Assert.True(backdrop.IsLocked);
        Assert.False(Named("Ground").IsLocked);
    }
}
