using Griff.Scenes;

namespace Griff.Tests.Scenes;

public class EntityTests
{
    [Fact]
    public void NewEntity_HasTransformFirst()
    {
        var entity = new Entity();

        Assert.Same(entity.Transform, Assert.Single(entity.Components));
        Assert.Same(entity, entity.Transform.Entity);
    }

    [Fact]
    public void Transform_CannotBeRemovedOrAdded()
    {
        var entity = new Entity();

        Assert.Throws<InvalidOperationException>(() => entity.RemoveComponent(entity.Transform));
        Assert.Throws<InvalidOperationException>(() => entity.AddComponent(new Transform()));
    }

    [Fact]
    public void AddComponent_AtIndex_NeverDisplacesTransform()
    {
        var entity = new Entity();
        var a = entity.AddComponent<TestComponent>();
        var b = (TestComponent)entity.AddComponent(new TestComponent(), index: 0);

        Assert.Equal<Component>([entity.Transform, b, a], entity.Components);
    }

    [Fact]
    public void RemoveComponent_Detaches()
    {
        var entity = new Entity();
        var component = entity.AddComponent<TestComponent>();

        Assert.True(entity.RemoveComponent(component));
        Assert.Null(component.Entity);
        Assert.False(entity.RemoveComponent(component));
    }

    [Fact]
    public void AddComponent_AttachedElsewhere_Throws()
    {
        var component = new Entity().AddComponent<TestComponent>();

        Assert.Throws<InvalidOperationException>(() => new Entity().AddComponent(component));
    }

    [Fact]
    public void GetComponent_FindsByType()
    {
        var entity = new Entity();
        var component = entity.AddComponent<TestComponent>();

        Assert.Same(component, entity.GetComponent<TestComponent>());
        Assert.Null(entity.GetComponent<RenamedComponent>());
    }
}
