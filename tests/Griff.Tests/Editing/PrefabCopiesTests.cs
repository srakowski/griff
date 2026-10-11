using System.Numerics;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Editing;

/// <summary>Placed copies of prefabs: linked in scenes, and brought up to date when the prefab is saved.</summary>
public sealed class PrefabCopiesTests : IDisposable
{
    private const string CoinPath = "Prefabs/Coin.prefab";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SceneSerializer _serializer = new(ComponentRegistry.CreateDefault());
    private readonly EditHistory _history = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A coin prefab: a sprite, a Pickup worth Amount, and a Sparkle child.
    private string CoinPrefab(int amount, string image = "Sprites/coin.png", bool sparkle = true)
    {
        var coin = new Entity("Coin");
        coin.AddComponent<Sprite>().Image = image;
        coin.AddComponent<Pickup>().Amount = amount;
        coin.Transform.Scale = new Vector3(0.5f, 0.5f, 1);
        if (sparkle)
        {
            var scene = new Scene();
            scene.Add(coin);
            scene.Add(new Entity("Sparkle"), coin);
            scene.Remove(coin);
        }

        return _serializer.SavePrefabToString(coin);
    }

    private Entity Place(Scene scene, string json, string name, Vector3 position, Entity? parent = null)
    {
        var copy = _serializer.LoadPrefabFromString(json);
        copy.Prefab = CoinPath;
        copy.Name = name;
        copy.Transform.Position = position;
        scene.Add(copy, parent);
        return copy;
    }

    [Fact]
    public void Link_IsSavedInScenes_ButNotOnAPrefabsOwnRoot()
    {
        var scene = new Scene();
        var copy = Place(scene, CoinPrefab(1), "Coin", Vector3.Zero);

        var loaded = _serializer.LoadFromString(_serializer.SaveToString(scene));
        Assert.Equal(CoinPath, loaded.Roots[0].Prefab);
        Assert.Equal(CoinPath, _serializer.Clone(copy).Prefab);

        // Saving the copy as a prefab again doesn't make the prefab a copy of itself.
        Assert.DoesNotContain("\"prefab\": \"Prefabs", _serializer.SavePrefabToString(copy));
        Assert.Null(_serializer.LoadPrefabFromString(_serializer.SavePrefabToString(copy)).Prefab);
    }

    [Fact]
    public void In_FindsCopiesAnywhere_ButNotInsideAnotherCopy_AndIgnoresHowThePathIsWritten()
    {
        var scene = new Scene();
        var json = CoinPrefab(1);
        var a = Place(scene, json, "A", Vector3.Zero);
        var shelf = new Entity("Shelf");
        scene.Add(shelf);
        var b = Place(scene, json, "B", Vector3.Zero, shelf);
        Place(scene, json, "Inside A", Vector3.Zero, a);
        b.Prefab = "prefabs\\coin.prefab";
        scene.Add(new Entity("Other") { Prefab = "Prefabs/Gem.prefab" });

        Assert.Equal([a, b], PrefabCopies.In(scene, CoinPath));
        Assert.Equal("Coin", PrefabCopies.NameOf("Prefabs\\Coin.prefab"));
    }

    [Fact]
    public void Update_GivesEveryCopyThePrefabsContents_KeepingItsOwnNamePlaceAndId_AsOneUndoStep()
    {
        var scene = new Scene();
        var old = CoinPrefab(1);
        var gold = Place(scene, old, "Gold", new Vector3(3, 1, 0));
        gold.Transform.Rotation = new Vector3(0, 0, 45);
        gold.IsLocked = true;
        var silver = Place(scene, old, "Silver", new Vector3(-2, 0, 0));
        var source = Place(scene, old, "Source", Vector3.Zero);
        var (goldId, oldSparkle) = (gold.Id, gold.Children[0]);

        var fresh = CoinPrefab(5, "Sprites/ruby.png", sparkle: false);
        var edit = PrefabCopies.Update(scene, CoinPath, fresh, old, _serializer, except: source);
        Assert.NotNull(edit);
        _history.Execute(edit);
        Assert.Equal("Update 2 copies of Coin", _history.UndoDescription);

        foreach (var copy in new[] { gold, silver })
        {
            Assert.Equal(5, copy.GetComponent<Pickup>()!.Amount);
            Assert.Equal("Sprites/ruby.png", copy.GetComponent<Sprite>()!.Image);
            Assert.Empty(copy.Children);
            Assert.Equal(CoinPath, copy.Prefab);
        }

        Assert.Equal(("Gold", goldId, new Vector3(3, 1, 0), 45f, true), (gold.Name, gold.Id, gold.Transform.Position, gold.Transform.Rotation.Z, gold.IsLocked));
        Assert.Same(gold, scene.Find(goldId));
        Assert.Null(scene.Find(oldSparkle.Id));
        Assert.Equal(1, source.GetComponent<Pickup>()!.Amount); // The entity the prefab was saved from is left alone.

        _history.Undo();
        Assert.Equal(1, gold.GetComponent<Pickup>()!.Amount);
        Assert.Same(oldSparkle, Assert.Single(gold.Children));
        Assert.Same(oldSparkle, scene.Find(oldSparkle.Id));

        _history.Redo();
        Assert.Empty(gold.Children);
        Assert.Equal(5, silver.GetComponent<Pickup>()!.Amount);
    }

    [Fact]
    public void Update_WithoutTheOldPrefab_TakesTheNewOneWhole_ScaleAndNewChildren()
    {
        var scene = new Scene();
        var copy = Place(scene, CoinPrefab(1, sparkle: false), "Coin", Vector3.Zero);
        copy.Transform.Scale = new Vector3(3, 3, 1);

        var edit = PrefabCopies.Update(scene, CoinPath, CoinPrefab(2), oldPrefabJson: null, _serializer)!;
        _history.Execute(edit);
        Assert.Equal(new Vector3(0.5f, 0.5f, 1), copy.Transform.Scale);
        var sparkle = Assert.Single(copy.Children);
        Assert.Equal("Sparkle", sparkle.Name);
        Assert.Same(scene, sparkle.Scene);

        _history.Undo();
        Assert.Equal(new Vector3(3, 3, 1), copy.Transform.Scale);
        Assert.Empty(copy.Children);
        Assert.Null(sparkle.Scene);
    }

    [Fact]
    public void Update_WithNoCopies_IsNothing()
    {
        var scene = new Scene();
        scene.Add(new Entity("Coin"));
        Assert.Null(PrefabCopies.Update(scene, CoinPath, "not a prefab", null, _serializer));
    }

    [Fact]
    public void UpdateFile_RewritesScenesWithCopies_AndLeavesOthersUntouched()
    {
        Directory.CreateDirectory(_root);
        var level = new Scene();
        Place(level, CoinPrefab(1), "Coin", new Vector3(4, 0, 0));
        level.Add(new Entity("Player"));
        var levelPath = Path.Combine(_root, "Level.scene");
        File.WriteAllText(levelPath, _serializer.SaveToString(level));
        var emptyPath = Path.Combine(_root, "Menu.scene");
        var menu = new Scene();
        menu.Add(new Entity("Title"));
        File.WriteAllText(emptyPath, _serializer.SaveToString(menu));
        var menuWritten = File.GetLastWriteTimeUtc(emptyPath);

        Assert.Equal(1, PrefabCopies.UpdateFile(levelPath, CoinPath, CoinPrefab(9), CoinPrefab(1), _serializer));
        Assert.Equal(0, PrefabCopies.UpdateFile(emptyPath, CoinPath, CoinPrefab(9), CoinPrefab(1), _serializer));

        var updated = _serializer.LoadFromString(File.ReadAllText(levelPath));
        var coin = updated.Roots.Single(e => e.Name == "Coin");
        Assert.Equal(9, coin.GetComponent<Pickup>()!.Amount);
        Assert.Equal(new Vector3(4, 0, 0), coin.Transform.Position);
        Assert.Equal(CoinPath, coin.Prefab);
        Assert.Equal(menuWritten, File.GetLastWriteTimeUtc(emptyPath));
        Assert.False(File.Exists(levelPath + ".saving"));
    }

    [Fact]
    public void SetPrefab_LinksAndUnlinks_AndUndoesEachToItsOwnLink()
    {
        var (a, b) = (new Entity("A"), new Entity("B") { Prefab = "Prefabs/Gem.prefab" });

        _history.Execute(new SetEntitiesPrefabEdit([a, b], null));
        Assert.Equal("Unlink 2 entities", _history.UndoDescription);
        Assert.Null(b.Prefab);

        _history.Undo();
        Assert.Equal("Prefabs/Gem.prefab", b.Prefab);

        _history.Execute(new SetEntitiesPrefabEdit([a], CoinPath));
        Assert.Equal("Link A", _history.UndoDescription);
        Assert.Equal(CoinPath, a.Prefab);
    }
}
