using Griff.Scenes;

namespace Griff.Tests.Scenes;

public class SceneTests
{
    [Fact]
    public void Add_IndexesEntityAndDescendants()
    {
        var scene = new Scene();
        var player = new Entity("Player");
        var weapon = new Entity("Weapon");
        scene.Add(player);
        scene.Add(weapon, player);

        Assert.Equal(2, scene.Count);
        Assert.Same(weapon, scene.Find(weapon.Id));
        Assert.Same(player, weapon.Parent);
        Assert.Same(scene, weapon.Scene);
        Assert.Equal([player, weapon], scene.Entities);
    }

    [Fact]
    public void Add_AtIndex_InsertsAmongSiblings()
    {
        var scene = new Scene();
        var a = new Entity("A");
        var b = new Entity("B");
        var c = new Entity("C");
        scene.Add(a);
        scene.Add(c);
        scene.Add(b, index: 1);

        Assert.Equal([a, b, c], scene.Roots);
        Assert.Equal(1, scene.IndexOf(b));
    }

    [Fact]
    public void Add_EntityAlreadyInAScene_Throws()
    {
        var scene = new Scene();
        var entity = new Entity();
        scene.Add(entity);

        Assert.Throws<InvalidOperationException>(() => new Scene().Add(entity));
    }

    [Fact]
    public void Remove_DropsDescendantsFromIndex_AndKeepsTheirHierarchy()
    {
        var scene = new Scene();
        var parent = new Entity("Parent");
        var child = new Entity("Child");
        scene.Add(parent);
        scene.Add(child, parent);

        scene.Remove(parent);

        Assert.Equal(0, scene.Count);
        Assert.Null(scene.Find(child.Id));
        Assert.Null(child.Scene);
        Assert.Same(parent, child.Parent);
        Assert.Null(parent.Parent);
    }

    [Fact]
    public void Remove_ThenAdd_RestoresEntity()
    {
        var scene = new Scene();
        var parent = new Entity("Parent");
        var child = new Entity("Child");
        scene.Add(parent);
        scene.Add(child, parent);

        scene.Remove(parent);
        scene.Add(parent);

        Assert.Equal(2, scene.Count);
        Assert.Same(child, scene.Find(child.Id));
    }

    [Fact]
    public void Move_ReparentsAndReorders()
    {
        var scene = new Scene();
        var a = new Entity("A");
        var b = new Entity("B");
        var c = new Entity("C");
        scene.Add(a);
        scene.Add(b);
        scene.Add(c);

        scene.Move(c, a);
        scene.Move(b, null, 0);

        Assert.Equal([b, a], scene.Roots);
        Assert.Equal([c], a.Children);
        Assert.Same(a, c.Parent);
    }

    [Fact]
    public void Move_UnderOwnDescendant_Throws()
    {
        var scene = new Scene();
        var parent = new Entity("Parent");
        var child = new Entity("Child");
        scene.Add(parent);
        scene.Add(child, parent);

        Assert.Throws<InvalidOperationException>(() => scene.Move(parent, child));
        Assert.Throws<InvalidOperationException>(() => scene.Move(parent, parent));
        Assert.Same(parent, child.Parent);
    }

    [Fact]
    public void Move_ToEntityInAnotherScene_Throws()
    {
        var scene = new Scene();
        var other = new Scene();
        var entity = new Entity();
        var foreign = new Entity();
        scene.Add(entity);
        other.Add(foreign);

        Assert.Throws<InvalidOperationException>(() => scene.Move(entity, foreign));
    }
}
