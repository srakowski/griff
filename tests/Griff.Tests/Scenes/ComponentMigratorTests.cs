using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

public class ComponentMigratorTests
{
    [Fact]
    public void TypeRemovedThenRestored_RoundTripsThroughMissingComponent()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<RenamedComponent>();
        var serializer = new SceneSerializer(registry);
        var scene = new Scene();
        var entity = new Entity("Hero");
        scene.Add(entity);
        entity.AddComponent<RenamedComponent>().Value = 42;
        var trailing = entity.AddComponent<TestComponent>();

        // The type goes away (e.g. a script fails to load): data is kept as a MissingComponent.
        var snapshot = ComponentMigrator.Capture([scene], serializer, c => c is RenamedComponent);
        registry.Unregister(typeof(RenamedComponent));
        ComponentMigrator.Restore(snapshot, serializer);

        var missing = Assert.IsType<MissingComponent>(entity.Components[1]);
        Assert.Equal("Health", missing.TypeName);
        Assert.Same(trailing, entity.Components[2]);

        // The type comes back: the component is rebuilt with its value, in the same slot.
        snapshot = ComponentMigrator.Capture([scene], serializer, _ => false);
        registry.Register<RenamedComponent>();
        var replaced = ComponentMigrator.Restore(snapshot, serializer);

        var restored = Assert.IsType<RenamedComponent>(entity.Components[1]);
        Assert.Equal(42, restored.Value);
        Assert.Same(restored, replaced[missing]);
        Assert.Same(entity, restored.Entity);
        Assert.Null(missing.Entity);
    }

    [Fact]
    public void UnaffectedComponents_AreLeftAlone()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<RenamedComponent>();
        var serializer = new SceneSerializer(registry);
        var scene = new Scene();
        var entity = new Entity();
        scene.Add(entity);
        var component = entity.AddComponent<RenamedComponent>();

        var snapshot = ComponentMigrator.Capture([scene], serializer, _ => false);
        var replaced = ComponentMigrator.Restore(snapshot, serializer);

        Assert.Empty(replaced);
        Assert.Same(component, entity.Components[1]);
    }
}
