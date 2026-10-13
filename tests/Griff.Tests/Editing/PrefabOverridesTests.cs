using System.Numerics;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Editing;

/// <summary>What placed copies change from their prefab, and how saving the prefab keeps those changes.</summary>
public sealed class PrefabOverridesTests
{
    private const string GemPath = "Prefabs/Gem.prefab";

    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();
    private readonly SceneSerializer _serializer;
    private readonly EditHistory _history = new();

    public PrefabOverridesTests() => _serializer = new SceneSerializer(_registry);

    // A gem: a blue sprite and a Pickup worth Amount, with a Shine and two Sparks inside.
    private string GemPrefab(int amount = 1, string tint = "#29ADFF", string image = "Sprites/gem.png", float shineRadius = 2)
    {
        var scene = new Scene();
        var gem = new Entity("Gem");
        var sprite = gem.AddComponent<Sprite>();
        sprite.Image = image;
        sprite.Tint = Hex(tint);
        gem.AddComponent<Pickup>().Amount = amount;
        gem.Transform.Scale = new Vector3(0.5f, 0.5f, 1);
        scene.Add(gem);
        var shine = new Entity("Shine");
        shine.AddComponent<Light>().Radius = shineRadius;
        scene.Add(shine, gem);
        scene.Add(new Entity("Spark"), gem);
        scene.Add(new Entity("Spark") { IsEnabled = false }, gem);
        scene.Remove(gem);
        return _serializer.SavePrefabToString(gem);
    }

    private Entity Place(Scene scene, string json, string name, Vector3 position)
    {
        var copy = _serializer.LoadPrefabFromString(json);
        copy.Prefab = GemPath;
        copy.Name = name;
        copy.Transform.Position = position;
        scene.Add(copy);
        return copy;
    }

    private static Color Hex(string hex) => Color.TryParse(hex, out var color) ? color : throw new FormatException(hex);

    private List<string> Overrides(Entity copy, string prefabJson) =>
        PrefabOverrides.Find(copy, _serializer.LoadPrefabFromString(prefabJson), _registry).Select(o => o.ToString()).ToList();

    [Fact]
    public void AFreshCopy_ChangesNothing_EvenPlacedNamedAndTurnedItsOwnWay()
    {
        var json = GemPrefab();
        var copy = Place(new Scene(), json, "Gem 7", new Vector3(4, 2, 0));
        copy.Transform.Rotation = new Vector3(0, 0, 30);
        copy.IsLocked = true;
        copy.IsHidden = true;

        Assert.Empty(Overrides(copy, json));
    }

    [Fact]
    public void Find_ListsChangedFields_AddedAndRemovedComponentsAndChildren_InsideToo()
    {
        var scene = new Scene();
        var json = GemPrefab();
        var copy = Place(scene, json, "Gem", Vector3.Zero);
        copy.GetComponent<Sprite>()!.Tint = Hex("#FF004D");
        copy.Transform.Scale = Vector3.One;
        copy.RemoveComponent(copy.GetComponent<Pickup>()!);
        copy.AddComponent<Health>();
        copy.Children[0].GetComponent<Light>()!.Radius = 6;
        copy.Children[2].IsEnabled = true;
        scene.Add(new Entity("Halo"), copy);

        Assert.Equal(
        [
            "Transform.Scale", "Sprite.Tint", "added Health", "removed Pickup",
            "Shine: Light.Radius", "Spark: enabled", "added child Halo",
        ], Overrides(copy, json));

        // A child taken away from the middle: children match by name in order, so the spark left over stands for the
        // first one, which was on.
        scene.Remove(copy.Children[1]);
        Assert.Equal(["Shine: Light.Radius", "added child Halo", "removed child Spark"], Overrides(copy, json).Skip(4));
    }

    [Fact]
    public void Counterpart_MatchesChildrenByNameAndOrder_AndComponentsByTypeAndOrder()
    {
        var scene = new Scene();
        var json = GemPrefab();
        var copy = Place(scene, json, "Gem", Vector3.Zero);
        var prefab = _serializer.LoadPrefabFromString(json);
        // A child added in front doesn't throw the others off.
        scene.Add(new Entity("Halo"), copy, 0);

        var secondSpark = copy.Children[3];
        Assert.False(PrefabOverrides.Counterpart(prefab, copy, secondSpark)!.IsEnabled);
        Assert.Null(PrefabOverrides.Counterpart(prefab, copy, copy.Children[0]));
        Assert.Equal(GemPath, PrefabOverrides.CopyFor(secondSpark)!.Prefab);
        Assert.Null(PrefabOverrides.CopyFor(new Entity("Loose")));

        var shine = copy.Children[1];
        shine.AddComponent<Light>().Radius = 9;
        var shineInPrefab = PrefabOverrides.Counterpart(prefab, copy, shine)!;
        Assert.Same(shineInPrefab.GetComponent<Light>(), PrefabOverrides.Counterpart(shineInPrefab, shine.GetComponent<Light>()!));
        Assert.Null(PrefabOverrides.Counterpart(shineInPrefab, shine.Components[2]));
    }

    [Fact]
    public void Update_KeepsEachCopysChanges_AndTakesEverythingElseFromTheNewPrefab()
    {
        var scene = new Scene();
        var old = GemPrefab();
        var red = Place(scene, old, "Red Gem", new Vector3(1, 0, 0));
        red.GetComponent<Sprite>()!.Tint = Hex("#FF004D");
        red.AddComponent<Health>().Max = 7;
        red.Children[0].GetComponent<Light>()!.Radius = 6;
        scene.Remove(red.Children[1]);
        var plain = Place(scene, old, "Plain Gem", new Vector3(2, 0, 0));

        var fresh = GemPrefab(amount: 5, tint: "#00E436", image: "Sprites/big-gem.png", shineRadius: 3);
        _history.Execute(PrefabCopies.Update(scene, GemPath, fresh, old, _serializer)!);

        // The red gem keeps its colour, its health, its brighter shine and its missing spark...
        Assert.Equal(Hex("#FF004D"), red.GetComponent<Sprite>()!.Tint);
        Assert.Equal(7, red.GetComponent<Health>()!.Max);
        Assert.Equal(6, red.Children[0].GetComponent<Light>()!.Radius);
        Assert.Equal(["Shine", "Spark"], red.Children.Select(c => c.Name));
        Assert.False(red.Children[1].IsEnabled);
        // ...and takes the rest of the new prefab.
        Assert.Equal(5, red.GetComponent<Pickup>()!.Amount);
        Assert.Equal("Sprites/big-gem.png", red.GetComponent<Sprite>()!.Image);
        Assert.Equal(new Vector3(1, 0, 0), red.Transform.Position);

        // The plain one changed nothing, so it takes it all.
        Assert.Equal(Hex("#00E436"), plain.GetComponent<Sprite>()!.Tint);
        Assert.Equal(3, plain.Children[0].GetComponent<Light>()!.Radius);
        Assert.Equal(3, plain.Children.Count);

        // Its changes still show as changes from the new prefab.
        Assert.Equal(["Sprite.Tint", "added Health", "Shine: Light.Radius", "Spark: disabled", "removed child Spark"], Overrides(red, fresh));
        Assert.Empty(Overrides(plain, fresh));

        _history.Undo();
        Assert.Equal(1, red.GetComponent<Pickup>()!.Amount);
        Assert.Equal(Hex("#29ADFF"), plain.GetComponent<Sprite>()!.Tint);
    }

    [Fact]
    public void Update_KeepsAChangedScale_AndAddedChildrenComeAlongAsCopies()
    {
        var scene = new Scene();
        var old = GemPrefab();
        var copy = Place(scene, old, "Gem", Vector3.Zero);
        copy.Transform.Scale = new Vector3(2, 2, 1);
        var halo = new Entity("Halo");
        halo.AddComponent<Light>().Radius = 8;
        scene.Add(halo, copy);

        _history.Execute(PrefabCopies.Update(scene, GemPath, GemPrefab(amount: 2), old, _serializer)!);

        Assert.Equal(new Vector3(2, 2, 1), copy.Transform.Scale);
        var newHalo = copy.Children.Single(c => c.Name == "Halo");
        Assert.Equal(8, newHalo.GetComponent<Light>()!.Radius);
        Assert.Same(scene, newHalo.Scene);
        Assert.Null(halo.Scene);
    }

    [Fact]
    public void AFieldSetBackToThePrefabsValue_IsNoLongerAChange_AndFollowsThePrefab()
    {
        var scene = new Scene();
        var old = GemPrefab(amount: 1);
        var copy = Place(scene, old, "Gem", Vector3.Zero);
        copy.GetComponent<Pickup>()!.Amount = 4;
        copy.GetComponent<Pickup>()!.Amount = 1;
        Assert.Empty(Overrides(copy, old));

        _history.Execute(PrefabCopies.Update(scene, GemPath, GemPrefab(amount: 9), old, _serializer)!);
        Assert.Equal(9, copy.GetComponent<Pickup>()!.Amount);
    }

    [Fact]
    public void UpdateFile_KeepsTheChangesOfCopiesInOtherScenes()
    {
        var root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var old = GemPrefab();
            var level = new Scene();
            Place(level, old, "Gem", Vector3.Zero).GetComponent<Sprite>()!.Tint = Hex("#FFA300");
            var path = Path.Combine(root, "Level.scene");
            File.WriteAllText(path, _serializer.SaveToString(level));

            Assert.Equal(1, PrefabCopies.UpdateFile(path, GemPath, GemPrefab(amount: 3), old, _serializer));

            var gem = _serializer.LoadFromString(File.ReadAllText(path)).Roots[0];
            Assert.Equal(Hex("#FFA300"), gem.GetComponent<Sprite>()!.Tint);
            Assert.Equal(3, gem.GetComponent<Pickup>()!.Amount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Lists_AreCopiedBetweenComponents_NotShared()
    {
        string[] lines = ["a", "b"];
        var copied = (string[])PrefabOverrides.CopyValue(lines)!;
        Assert.Equal(lines, copied);
        Assert.NotSame(lines, copied);
    }
}
