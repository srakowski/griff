using System.Collections.Concurrent;
using Griff.Editing;
using Griff.Hosting;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Runtime;

/// <summary>
/// A prefab is read once into a fixed template that every spawn copies (in exported games too), from any number of threads
/// at once; where its files come from says when it's read again. Each placed copy remembers the version of its prefab it
/// was made from, which scene, prefab and clipboard text keep, so its changes are kept however the prefab changed.
/// </summary>
public sealed class PrefabTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-prefab-templates-" + Guid.NewGuid().ToString("N"));
    private readonly SceneSerializer _serializer = new(ComponentRegistry.CreateDefault());

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A source of files whose changes the test says, as the editor's watched folder does once a file settles.
    private sealed class SaidAssets(string root) : AssetFolder(root)
    {
        private Action<string>? _changed;

        public override event Action<string>? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void Say(string path) => _changed?.Invoke(path);
    }

    private static Entity Coin(int amount = 1, string image = "Sprites/coin.png")
    {
        var coin = new Entity("Coin");
        coin.AddComponent<Sprite>().Image = image;
        coin.AddComponent<Pickup>().Amount = amount;
        var shine = new Entity("Shine");
        shine.AddComponent<Sprite>().Image = "Sprites/shine.png";
        var scratch = new Scene();
        scratch.Add(coin);
        scratch.Add(shine, coin);
        scratch.Remove(coin);
        return coin;
    }

    private async Task<(Project Project, string Path)> ProjectWithCoinAsync()
    {
        var project = await Project.CreateAsync(_root, "Game");
        var path = project.DefaultPrefabPath("Coin");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, _serializer.SavePrefabToString(Coin()));
        return (project, path);
    }

    [Fact]
    public async Task EveryTemplatesPrefabs_CopyAsReadingTheirFileWould()
    {
        foreach (var template in ProjectTemplates.All.Select(t => t.Name))
        {
            var root = Path.Combine(_root, template);
            var project = await Project.CreateAsync(root, "Game", ProjectTemplates.Get(template));
            var registry = ComponentRegistry.CreateDefault();
            new ScriptHost(registry).ReloadFolder(project.AssetsPath, []);
            var serializer = new SceneSerializer(registry);
            foreach (var file in project.FindPrefabs())
            {
                var read = serializer.LoadPrefabFromString(File.ReadAllText(file));
                var template1 = new EntityTemplate(serializer.LoadPrefabFromString(File.ReadAllText(file)), registry);
                var copy = template1.Instantiate();
                var again = template1.Instantiate();

                Assert.Equal(serializer.SavePrefabToString(read), serializer.SavePrefabToString(copy));
                Assert.Empty(copy.SelfAndDescendants().Select(e => e.Id).Intersect(again.SelfAndDescendants().Select(e => e.Id)));
                Assert.Empty(copy.SelfAndDescendants().SelectMany(e => e.Components)
                    .Intersect(again.SelfAndDescendants().SelectMany(e => e.Components)));
            }
        }
    }

    [Fact]
    public async Task AnExportedGamesPrefab_IsReadOnce()
    {
        var (project, path) = await ProjectWithCoinAsync();
        var library = new PrefabLibrary(project, ComponentRegistry.CreateDefault(), new AssetFolder(project.AssetsPath));
        Assert.Equal(1, library.Instantiate("Coin").GetComponent<Pickup>()!.Amount);
        Assert.Equal(1, library.Instantiate("Prefabs/Coin.prefab").GetComponent<Pickup>()!.Amount);

        // Its files never change: the template stands, the file isn't looked at again.
        await File.WriteAllTextAsync(path, _serializer.SavePrefabToString(Coin(amount: 5)));
        Assert.Equal(1, library.Instantiate("Coin").GetComponent<Pickup>()!.Amount);
        File.Delete(path);
        Assert.Equal(1, library.Instantiate("Prefabs/Coin.prefab").GetComponent<Pickup>()!.Amount);
    }

    [Fact]
    public async Task APrefabChanged_IsReadAgain_AndSaid_UnlessTheEditorWroteIt()
    {
        var (project, path) = await ProjectWithCoinAsync();
        var assets = new SaidAssets(project.AssetsPath);
        using var library = new PrefabLibrary(project, ComponentRegistry.CreateDefault(), assets);
        var changes = new List<PrefabChange>();
        library.Changed += changes.Add;
        var first = library.VersionOf(path)!;
        Assert.Equal(1, library.Instantiate("Coin").GetComponent<Pickup>()!.Amount);

        await File.WriteAllTextAsync(path, _serializer.SavePrefabToString(Coin(amount: 5)));
        assets.Say(path);

        Assert.Equal(5, library.Instantiate("Coin").GetComponent<Pickup>()!.Amount);
        var change = Assert.Single(changes);
        Assert.Equal(first, change.Before);
        Assert.Equal(library.VersionOf(path), change.Now);
        Assert.NotEqual(first, change.Now);

        // Written by the editor, which says so first: its settling isn't a change.
        var json = _serializer.SavePrefabToString(Coin(amount: 7));
        await File.WriteAllTextAsync(path, json);
        library.Remember(path, PrefabVersion.Of(json));
        assets.Say(path);
        Assert.Single(changes);
        Assert.Equal(7, library.Instantiate("Coin").GetComponent<Pickup>()!.Amount);

        // Deleted, it's said and gone.
        File.Delete(path);
        assets.Say(path);
        Assert.Null(changes[^1].Now);
        Assert.Throws<FileNotFoundException>(() => library.Instantiate("Coin"));
    }

    [Fact]
    public async Task OneLibrary_SpawnsOnManyThreadsAtOnce()
    {
        var (project, _) = await ProjectWithCoinAsync();
        var library = new PrefabLibrary(project, ComponentRegistry.CreateDefault());
        var made = new ConcurrentBag<Entity>();

        Parallel.For(0, 4000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ => made.Add(library.Instantiate("Coin")));

        Assert.Equal(4000, made.Select(e => e.Id).Distinct().Count());
        Assert.All(made, e => Assert.Equal(["Shine"], e.Children.Select(c => c.Name)));
    }

    [Fact]
    public async Task ScriptsReloaded_MakeTheTemplateAgain()
    {
        var (project, path) = await ProjectWithCoinAsync();
        var registry = ComponentRegistry.CreateDefault();
        registry.Unregister(typeof(Pickup));
        var library = new PrefabLibrary(project, registry);
        Assert.IsType<MissingComponent>(library.Instantiate("Coin").Components[2]);

        registry.Register<Pickup>();

        Assert.Equal(1, library.Instantiate("Coin").GetComponent<Pickup>()!.Amount);
    }

    [Fact]
    public void AVersion_IsTheSameText_HoweverItsSpacedOrItsLinesEnd()
    {
        var text = _serializer.SavePrefabToString(Coin());

        Assert.Equal(PrefabVersion.Of(text), PrefabVersion.Of(text.Replace("\r\n", "\n").Replace("\n", "\r\n")));
        Assert.Equal(PrefabVersion.Of(text).Id, PrefabVersion.Of(PrefabVersion.Of(text).Json).Id);
        Assert.NotEqual(PrefabVersion.Of(text), PrefabVersion.Of(_serializer.SavePrefabToString(Coin(amount: 2))));
        Assert.Throws<InvalidDataException>(() => PrefabVersion.Of("{ not json"));
    }

    [Fact]
    public void ACopysVersion_IsKeptByScenesPrefabsAndTheClipboard_OnceEach()
    {
        var version = PrefabVersion.Of(_serializer.SavePrefabToString(Coin()));
        var scene = new Scene();
        for (var i = 0; i < 3; i++)
        {
            var copy = _serializer.LoadPrefabFromString(version.Json);
            (copy.Prefab, copy.PrefabVersion) = ("Prefabs/Coin.prefab", version);
            scene.Add(copy);
        }

        var text = _serializer.SaveToString(scene);
        Assert.Equal(3, Count(text, "\"prefabVersion\":"));
        Assert.Equal(1, Count(text, $"\"{version.Id}\": {{"));
        Assert.All(_serializer.LoadFromString(text).Roots, copy => Assert.Equal(version, copy.PrefabVersion));
        Assert.Equal(version.Json, _serializer.LoadFromString(text).Roots[0].PrefabVersion!.Json);

        // A chest holding a coin keeps the coin's version in its own file.
        var chest = new Entity("Chest");
        var held = new Scene();
        held.Add(chest);
        held.Add(_serializer.Clone(scene.Roots[0]), chest);
        var chestText = _serializer.SavePrefabToString(chest);
        Assert.Equal(version, _serializer.LoadPrefabFromString(chestText).Children[0].PrefabVersion);

        // Copied and pasted, or duplicated, with the version.
        Assert.Equal(version, _serializer.DeserializeEntity(_serializer.SerializeEntity(chest)).Children[0].PrefabVersion);
        Assert.All(_serializer.DeserializeEntities(_serializer.SerializeEntities([.. scene.Roots])), e => Assert.Equal(version, e.PrefabVersion));

        // A scene with no copies is as it was.
        Assert.DoesNotContain("prefabVersions", _serializer.SaveToString(new Scene()), StringComparison.Ordinal);
    }

    [Fact]
    public void Updating_KeepsWhatEachCopyChanged_FromTheVersionItWasMadeFrom()
    {
        var v1 = PrefabVersion.Of(_serializer.SavePrefabToString(Coin(amount: 1)));
        var v2 = PrefabVersion.Of(_serializer.SavePrefabToString(Coin(amount: 2)));
        var v3 = PrefabVersion.Of(_serializer.SavePrefabToString(Coin(amount: 2, image: "Sprites/gold.png")));
        var scene = new Scene();
        Entity Place(PrefabVersion version)
        {
            var copy = _serializer.LoadPrefabFromString(version.Json);
            (copy.Prefab, copy.PrefabVersion) = ("Prefabs/Coin.prefab", version);
            scene.Add(copy);
            return copy;
        }

        // Made from the first version and given 9; made from the second, as it was; made from the third already.
        var old = Place(v1);
        old.GetComponent<Pickup>()!.Amount = 9;
        var plain = Place(v2);
        var current = Place(v3);
        current.GetComponent<Pickup>()!.Amount = 4;

        // Brought up to the third, the base each was made from tells its own changes apart: the 2 the second version had
        // isn't the first copy's change, the 9 is.
        var edit = PrefabCopies.Update(scene, "Prefabs/Coin.prefab", v3.Json, oldPrefabJson: null, _serializer)!;
        edit.Apply();

        Assert.Equal((9, "Sprites/gold.png"), (old.GetComponent<Pickup>()!.Amount, old.GetComponent<Sprite>()!.Image));
        Assert.Equal((2, "Sprites/gold.png"), (plain.GetComponent<Pickup>()!.Amount, plain.GetComponent<Sprite>()!.Image));
        Assert.Equal(4, current.GetComponent<Pickup>()!.Amount);
        Assert.All(scene.Roots, copy => Assert.Equal(v3, copy.PrefabVersion));

        edit.Revert();
        Assert.Equal([v1, v2, v3], scene.Roots.Select(c => c.PrefabVersion));
        Assert.Equal("Sprites/coin.png", old.GetComponent<Sprite>()!.Image);
    }

    [Fact]
    public void CatchingUp_UpdatesOnlyCopiesOfAnotherVersion()
    {
        var v1 = PrefabVersion.Of(_serializer.SavePrefabToString(Coin(amount: 1)));
        var v2 = PrefabVersion.Of(_serializer.SavePrefabToString(Coin(amount: 1, image: "Sprites/gold.png")));
        var scene = new Scene();
        var stale = _serializer.LoadPrefabFromString(v1.Json);
        (stale.Prefab, stale.PrefabVersion) = ("Prefabs/Coin.prefab", v1);
        stale.GetComponent<Pickup>()!.Amount = 3;
        scene.Add(stale);
        // Made before copies remembered their version: what it is now can't be told apart from a change, so it stays.
        var unknown = _serializer.LoadPrefabFromString(v1.Json);
        unknown.Prefab = "Prefabs/Coin.prefab";
        scene.Add(unknown);

        Assert.Equal(1, PrefabCopies.UpdateStale(scene, _ => v2, _serializer));
        Assert.Equal((3, "Sprites/gold.png"), (stale.GetComponent<Pickup>()!.Amount, stale.GetComponent<Sprite>()!.Image));
        Assert.Equal("Sprites/coin.png", unknown.GetComponent<Sprite>()!.Image);
        Assert.Equal(v2, unknown.PrefabVersion);
        Assert.Equal(0, PrefabCopies.UpdateStale(scene, _ => v2, _serializer));
    }

    private static int Count(string text, string part) =>
        (text.Length - text.Replace(part, "", StringComparison.Ordinal).Length) / part.Length;
}
