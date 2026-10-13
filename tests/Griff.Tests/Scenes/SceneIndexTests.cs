using System.Numerics;
using Griff.Scenes;

namespace Griff.Tests.Scenes;

/// <summary>The scene's components by type (All, Active), and entities' cached IsActive, kept up to date as it changes.</summary>
public class SceneIndexTests
{
    private interface IMarked;

    private sealed class Marked : Component, IMarked;

    private sealed class Other : Component;

    [Fact]
    public void All_finds_components_in_hierarchy_order_by_base_type_and_interface()
    {
        var scene = new Scene();
        var a = new Entity("A");
        var box = a.AddComponent<BoxCollider>();
        var b = new Entity("B");
        var circle = b.AddComponent<CircleCollider>();
        var marked = b.AddComponent<Marked>();
        var c = new Entity("C");
        var inner = c.AddComponent<BoxCollider>();
        scene.Add(a);
        scene.Add(c);
        scene.Add(b, a);

        Assert.Equal<Collider>([box, circle, inner], scene.All<Collider>());
        Assert.Equal([box, inner], scene.All<BoxCollider>());
        Assert.Equal<IMarked>([marked], scene.All<IMarked>());
        Assert.Empty(scene.All<Other>());
        Assert.Equal(3, scene.All<Transform>().Count);
    }

    [Fact]
    public void Spawning_removing_and_moving_keep_the_lists_in_order()
    {
        var scene = new Scene();
        var first = Spawn(scene, "First");
        var second = Spawn(scene, "Second");
        Assert.Equal(["First", "Second"], Names(scene.All<Marked>()));

        // Spawned at the end, put among the others, under another, moved.
        var third = Spawn(scene, "Third");
        var middle = new Entity("Middle");
        middle.AddComponent<Marked>();
        scene.Add(middle, index: 1);
        var child = new Entity("Child");
        child.AddComponent<Marked>();
        scene.Add(child, first);
        Assert.Equal(["First", "Child", "Middle", "Second", "Third"], Names(scene.All<Marked>()));

        scene.Move(first, third);
        Assert.Equal(["Middle", "Second", "Third", "First", "Child"], Names(scene.All<Marked>()));

        scene.Remove(second);
        Assert.Equal(["Middle", "Third", "First", "Child"], Names(scene.All<Marked>()));

        // Back again, and only once.
        scene.Add(second);
        Assert.Equal(["Middle", "Third", "First", "Child", "Second"], Names(scene.All<Marked>()));
    }

    [Fact]
    public void Adding_removing_moving_and_replacing_components_keep_the_lists_in_order()
    {
        var scene = new Scene();
        var entity = new Entity("Thing");
        scene.Add(entity);
        var later = Spawn(scene, "Later");
        Assert.Equal(["Later"], Names(scene.All<Marked>()));

        var first = entity.AddComponent<Marked>();
        var second = entity.AddComponent<Marked>();
        Assert.Equal([first, second, later.GetComponent<Marked>()!], scene.All<Marked>());

        entity.MoveComponent(second, 1);
        Assert.Equal([second, first, later.GetComponent<Marked>()!], scene.All<Marked>());

        entity.RemoveComponent(second);
        Assert.Equal([first, later.GetComponent<Marked>()!], scene.All<Marked>());

        // Taken off one entity and put on another: listed once, where it is now.
        entity.RemoveComponent(first);
        later.AddComponent(first);
        Assert.Equal([later.GetComponent<Marked>()!, first], scene.All<Marked>());

        var other = new Other();
        later.ReplaceComponent(first, other);
        Assert.Equal([later.GetComponent<Marked>()!], scene.All<Marked>());
        Assert.Equal([other], scene.All<Other>());
    }

    [Fact]
    public void Active_leaves_out_disabled_entities_and_what_is_inside_them()
    {
        var scene = new Scene();
        var parent = Spawn(scene, "Parent");
        var child = new Entity("Child");
        child.AddComponent<Marked>();
        scene.Add(child, parent);
        var other = Spawn(scene, "Other");
        Assert.Equal(["Parent", "Child", "Other"], Names(scene.Active<Marked>()));

        parent.IsEnabled = false;
        Assert.False(child.IsActive);
        Assert.Equal(["Other"], Names(scene.Active<Marked>()));
        Assert.Equal(3, scene.All<Marked>().Count);

        // Moved out from under it, it's active again.
        scene.Move(child, null);
        Assert.True(child.IsActive);
        Assert.Equal(["Other", "Child"], Names(scene.Active<Marked>()));

        scene.Move(other, parent);
        Assert.False(other.IsActive);
        parent.IsEnabled = true;
        Assert.True(other.IsActive);
        Assert.Equal(["Parent", "Other", "Child"], Names(scene.Active<Marked>()));
    }

    [Fact]
    public void An_entity_taken_to_another_scene_is_active_as_it_is_there()
    {
        var first = new Scene();
        var open = new Entity("Open");
        first.Add(open);
        var entity = new Entity("Entity");
        first.Add(entity, open);
        Assert.True(entity.IsActive);

        first.Remove(entity);
        var second = new Scene();
        var closed = new Entity("Closed") { IsEnabled = false };
        second.Add(closed);
        second.Add(entity, closed);
        Assert.False(entity.IsActive);

        second.Remove(entity);
        Assert.True(entity.IsActive);
    }

    [Fact]
    public void A_list_is_as_it_was_when_asked_for_while_the_scene_changes()
    {
        var scene = new Scene();
        Spawn(scene, "A");
        Spawn(scene, "B");

        var seen = new List<string>();
        foreach (var marked in scene.All<Marked>())
        {
            seen.Add(marked.Entity!.Name);
            Spawn(scene, marked.Entity.Name + "'s child");
            scene.Remove(marked.Entity);
        }

        Assert.Equal(["A", "B"], seen);
        Assert.Equal(["A's child", "B's child"], Names(scene.All<Marked>()));
    }

    [Fact]
    public void Asking_again_without_changes_gives_the_same_list_and_allocates_nothing()
    {
        var scene = new Scene();
        for (var i = 0; i < 100; i++)
            Spawn(scene, $"E{i}");
        _ = scene.Active<Marked>();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        for (var i = 0; i < 10; i++)
        {
            foreach (var marked in scene.Active<Marked>())
                count += marked.Entity!.IsActive ? 1 : 0;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1000, count);
    }

    // Many random changes, checking after each that the lists and IsActive say what going through the hierarchy would.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_lists_always_match_the_hierarchy(int seed)
    {
        var random = new Random(seed);
        var scene = new Scene();
        var outside = new List<Entity>();
        for (var step = 0; step < 600; step++)
        {
            var entities = scene.Entities;
            var any = entities.Count > 0 ? entities[random.Next(entities.Count)] : null;
            switch (random.Next(9))
            {
                case 0 or 1:
                    var spawned = new Entity($"E{step}");
                    if (random.Next(2) == 0)
                        spawned.AddComponent<Marked>();
                    if (random.Next(3) == 0)
                        spawned.AddComponent<BoxCollider>();
                    var parent = random.Next(3) == 0 ? any : null;
                    scene.Add(spawned, parent, random.Next(4) == 0 ? random.Next(3) : null);
                    break;
                case 2 when any is not null:
                    scene.Remove(any);
                    outside.Add(any);
                    break;
                case 3 when outside.Count > 0:
                    var back = outside[random.Next(outside.Count)];
                    outside.Remove(back);
                    if (back.SelfAndDescendants().All(e => scene.Find(e.Id) is null))
                        scene.Add(back);
                    break;
                case 4 when any is not null:
                    var to = entities[random.Next(entities.Count)];
                    if (!to.IsSelfOrDescendantOf(any))
                        scene.Move(any, random.Next(3) == 0 ? null : to, random.Next(3));
                    break;
                case 5 when any is not null:
                    any.IsEnabled = !any.IsEnabled;
                    break;
                case 6 when any is not null:
                    any.AddComponent(new Marked(), random.Next(3));
                    break;
                case 7 when any?.GetComponent<Marked>() is { } marked:
                    any.RemoveComponent(marked);
                    break;
                case 8 when any is not null && any.Components.Count > 2:
                    any.MoveComponent(any.Components[^1], 1);
                    break;
            }

            if (random.Next(3) == 0)
                Check(scene);
        }

        Check(scene);
    }

    private static void Check(Scene scene)
    {
        Assert.Equal(scene.Entities.SelectMany(e => e.GetComponents<Marked>()), scene.All<Marked>());
        Assert.Equal(scene.Entities.SelectMany(e => e.GetComponents<Collider>()), scene.All<Collider>());
        Assert.Equal(scene.Entities.Where(IsActive).SelectMany(e => e.GetComponents<Marked>()), scene.Active<Marked>());
        foreach (var entity in scene.Entities)
            Assert.Equal(IsActive(entity), entity.IsActive);

        // Worked out from scratch, going up through the parents.
        static bool IsActive(Entity entity)
        {
            for (var e = entity; e is not null; e = e.Parent)
            {
                if (!e.IsEnabled)
                    return false;
            }

            return true;
        }
    }

    [Fact]
    public void World_matrices_follow_their_own_and_their_parents_moves()
    {
        var scene = new Scene();
        var grandparent = new Entity("Grandparent");
        var parent = new Entity("Parent");
        var child = new Entity("Child");
        scene.Add(grandparent);
        scene.Add(parent, grandparent);
        scene.Add(child, parent);
        child.Transform.Position = new Vector3(1, 0, 0);
        Assert.Equal(new Vector3(1, 0, 0), child.Transform.WorldPosition);

        grandparent.Transform.Position = new Vector3(10, 0, 0);
        Assert.Equal(new Vector3(11, 0, 0), child.Transform.WorldPosition);

        parent.Transform.Scale = new Vector3(2, 2, 1);
        Assert.Equal(new Vector3(12, 0, 0), child.Transform.WorldPosition);

        child.Transform.Position = new Vector3(0, 3, 0);
        Assert.Equal(new Vector3(10, 6, 0), child.Transform.WorldPosition);

        // Moved out from under them, and back under another.
        scene.Move(child, null);
        Assert.Equal(new Vector3(0, 3, 0), child.Transform.WorldPosition);
        var other = new Entity("Other");
        other.Transform.Position = new Vector3(0, -3, 5);
        scene.Add(other);
        scene.Move(child, other);
        Assert.Equal(new Vector3(0, 0, 5), child.Transform.WorldPosition);
    }

    [Fact]
    public void World_matrices_match_working_them_out_afresh_through_random_moves()
    {
        var random = new Random(7);
        var scene = new Scene();
        var entities = new List<Entity>();
        for (var i = 0; i < 40; i++)
        {
            var entity = new Entity($"E{i}");
            scene.Add(entity, entities.Count > 0 && random.Next(3) > 0 ? entities[random.Next(entities.Count)] : null);
            entities.Add(entity);
        }

        for (var step = 0; step < 500; step++)
        {
            var entity = entities[random.Next(entities.Count)];
            var transform = entity.Transform;
            switch (random.Next(4))
            {
                case 0:
                    transform.Position = new Vector3(random.Next(-5, 6), random.Next(-5, 6), random.Next(-2, 3));
                    break;
                case 1:
                    transform.Rotation = new Vector3(0, 0, random.Next(0, 360));
                    break;
                case 2:
                    transform.Scale = new Vector3(random.Next(1, 4), random.Next(1, 4), 1);
                    break;
                case 3:
                    var to = entities[random.Next(entities.Count)];
                    if (!to.IsSelfOrDescendantOf(entity))
                        scene.Move(entity, random.Next(4) == 0 ? null : to);
                    break;
            }

            var probe = entities[random.Next(entities.Count)];
            Assert.Equal(Afresh(probe), probe.Transform.WorldMatrix);
        }

        static Matrix4x4 Afresh(Entity entity) =>
            entity.Parent is { } parent ? entity.Transform.LocalMatrix * Afresh(parent) : entity.Transform.LocalMatrix;
    }

    private static Entity Spawn(Scene scene, string name)
    {
        var entity = new Entity(name);
        entity.AddComponent<Marked>();
        scene.Add(entity);
        return entity;
    }

    private static List<string> Names<T>(ComponentList<T> components) where T : Component =>
        components.Select(c => c.Entity!.Name).ToList();
}
