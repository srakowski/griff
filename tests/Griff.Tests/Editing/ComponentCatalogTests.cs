using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Editing;

public class ComponentCatalogTests
{
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    private CatalogEntry Entry<T>() => ComponentCatalog.Build(_registry).Single(e => e.Descriptor.Type == typeof(T));

    [Fact]
    public void EveryBuiltInComponent_HasACategoryAndADescription()
    {
        var entries = ComponentCatalog.Build(_registry);

        Assert.DoesNotContain(entries, e => e.Descriptor.Type == typeof(Transform));
        Assert.All(entries, e =>
        {
            Assert.NotNull(e.Descriptor.Category);
            Assert.False(e.Summary.IsEmpty, $"{e.Name} has no summary");
        });
        // Listed by category in the catalog's order: rendering first.
        Assert.Equal("Rendering", entries[0].Category);
    }

    [Fact]
    public void Summary_TurnsReferencesIntoLinksToTheirTypes()
    {
        var summary = Entry<Body>().Summary;

        Assert.Equal("Makes an entity with a Collider move under velocity and gravity and collide with solids.", summary.PlainText);
        Assert.Contains(summary.Paragraphs[0], r => r is { Kind: DocRunKind.Reference, Text: "Collider" } && r.Target == typeof(Collider));
    }

    [Fact]
    public void PropertyDocs_ComeFromTheBaseWhenAnOverrideInheritsThem()
    {
        var isTrigger = Entry<BoxCollider>().Properties.Single(p => p.Name == nameof(BoxCollider.IsTrigger));

        Assert.StartsWith("Triggers don't block movement", isTrigger.Doc.PlainText);
        Assert.Equal("On/off", isTrigger.Kind);
        Assert.Equal("Off", isTrigger.Default);
    }

    [Fact]
    public void Properties_ShowTheirKindAndStartingValue()
    {
        var jump = Entry<PlatformerMovement>().Properties.Single(p => p.Name == nameof(PlatformerMovement.JumpHeight));

        Assert.Equal("Number", jump.Kind);
        Assert.Equal("3.4", jump.Default);
        Assert.Equal("How high a full jump goes, in world units.", jump.Doc.PlainText);
    }

    [Fact]
    public void Requirements_AreListedBothWays()
    {
        var movement = Entry<PlatformerMovement>();

        Assert.Equal([typeof(Body), typeof(Collider)], movement.Requires.Select(r => r.Type));
        Assert.Contains(Entry<Body>().RequiredBy, d => d.Type == typeof(PlatformerMovement));
        // A requirement on Collider counts for each kind of collider.
        Assert.Contains(Entry<CircleCollider>().RequiredBy, d => d.Type == typeof(Body));
    }

    [Fact]
    public void PlanAdd_AddsMissingRequirementsFirst()
    {
        var player = new Entity("Player");

        var plan = ComponentCatalog.PlanAdd(_registry, _registry.Get(typeof(PlatformerMovement)), player);

        Assert.Equal([typeof(BoxCollider), typeof(Body), typeof(PlatformerMovement)], plan.Select(d => d.Type));
    }

    [Fact]
    public void PlanAdd_SkipsWhatTheEntityAlreadyHas()
    {
        var player = new Entity("Player");
        player.AddComponent(new CircleCollider());

        var plan = ComponentCatalog.PlanAdd(_registry, _registry.Get(typeof(PlatformerMovement)), player);

        Assert.Equal([typeof(Body), typeof(PlatformerMovement)], plan.Select(d => d.Type));
    }

    [Fact]
    public void AddWithRequirements_IsOneUndoStep()
    {
        var player = new Entity("Player");
        var edit = ComponentCatalog.AddWithRequirements(_registry, _registry.Get(typeof(Patrol)), player);

        edit.Apply();
        Assert.Equal([typeof(Transform), typeof(BoxCollider), typeof(Body), typeof(Patrol)], player.Components.Select(c => c.GetType()));

        edit.Revert();
        Assert.Equal([typeof(Transform)], player.Components.Select(c => c.GetType()));
    }

    [Fact]
    public void Scripts_AreDocumentedFromTheirComments()
    {
        const string source = """
            /// <summary>Guards a spot and chases off anyone with <see cref="Health"/>. Uses <c>Range</c>.</summary>
            [Category("Enemies")]
            [Requires(typeof(Body), Note = "to move")]
            [WorksWith(typeof(Health))]
            public class Guard : Behavior
            {
                /// <summary>How far it sees, in world units.</summary>
                public float Range { get; set; } = 4;

                public int Undocumented { get; set; }
            }
            """;
        var result = new ScriptHost(_registry).Reload([new ScriptSource("Guard.cs", source)], []);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Empty(result.Diagnostics);

        var guard = ComponentCatalog.Build(_registry).Single(e => e.Name == "Guard");

        Assert.True(guard.IsScript);
        Assert.Equal("Enemies", guard.Category);
        Assert.Equal("Guards a spot and chases off anyone with Health. Uses Range.", guard.Summary.PlainText);
        Assert.Contains(guard.Summary.Paragraphs[0], r => r.Kind == DocRunKind.Reference && r.Target == typeof(Health));
        Assert.Equal("to move", Assert.Single(guard.Requires).Note);
        Assert.Equal(typeof(Health), Assert.Single(guard.WorksWith).Type);
        Assert.Equal("How far it sees, in world units.", guard.Properties.Single(p => p.Name == "Range").Doc.PlainText);
        Assert.Equal("4", guard.Properties.Single(p => p.Name == "Range").Default);
        Assert.True(guard.Properties.Single(p => p.Name == "Undocumented").Doc.IsEmpty);
    }

    [Fact]
    public void Scripts_WithoutACategory_AreListedUnderScripts()
    {
        var result = new ScriptHost(_registry).Reload([new ScriptSource("Spinner.cs", "public class Spinner : Behavior { }")], []);
        Assert.True(result.Success);

        var spinner = ComponentCatalog.Build(_registry).Single(e => e.Name == "Spinner");

        Assert.Equal(ComponentCatalog.ScriptsCategory, spinner.Category);
        Assert.True(spinner.Summary.IsEmpty);
        Assert.Equal(ComponentCatalog.ScriptsCategory, ComponentCatalog.Build(_registry)[^1].Category);
    }

    [Fact]
    public void FirstSentence_StopsAtTheFirstFullStop()
    {
        Assert.Equal("Hit points.", Entry<Health>().Summary.FirstSentence);
    }
}
