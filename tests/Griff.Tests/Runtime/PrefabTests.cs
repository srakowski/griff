using System.Numerics;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

public sealed class PrefabTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-prefabs-" + Guid.NewGuid().ToString("N"));
    private readonly SceneSerializer _serializer = new(ComponentRegistry.CreateDefault());

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Entity Bullet()
    {
        var bullet = new Entity("Bullet");
        bullet.Transform.Position = new Vector3(3, 4, 2);
        bullet.AddComponent<Sprite>().Tint = Color.FromBytes(0xFF, 0x00, 0x00);
        bullet.AddComponent<BoxCollider>().IsTrigger = true;
        var trail = new Entity("Trail");
        trail.AddComponent<Sprite>();

        // Removing from a scene keeps the hierarchy, which gives a detached bullet with a child.
        var scratch = new Scene();
        scratch.Add(bullet);
        scratch.Add(trail, bullet);
        scratch.Remove(bullet);
        return bullet;
    }

    private async Task<Project> ProjectWithBulletAsync()
    {
        var project = await Project.CreateAsync(_root, "Game");
        var path = project.DefaultPrefabPath("Bullet");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, _serializer.SavePrefabToString(Bullet()));
        return project;
    }

    [Fact]
    public void PrefabFile_RoundTripsWithoutIds()
    {
        var json = _serializer.SavePrefabToString(Bullet());

        Assert.DoesNotContain("\"id\"", json);
        var copy = _serializer.LoadPrefabFromString(json);
        Assert.Equal("Bullet", copy.Name);
        Assert.Equal(new Vector3(3, 4, 2), copy.Transform.Position);
        Assert.True(copy.GetComponent<BoxCollider>()!.IsTrigger);
        Assert.Equal("Trail", Assert.Single(copy.Children).Name);
        Assert.Same(copy, copy.Children[0].Parent);
    }

    [Fact]
    public void PrefabFile_IsStable()
    {
        // Same entity, same bytes: re-saving an instance doesn't churn the file in git.
        Assert.Equal(_serializer.SavePrefabToString(Bullet()), _serializer.SavePrefabToString(Bullet()));
    }

    [Fact]
    public void LoadingTwice_GivesDistinctEntities()
    {
        var json = _serializer.SavePrefabToString(Bullet());

        var a = _serializer.LoadPrefabFromString(json);
        var b = _serializer.LoadPrefabFromString(json);

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.Children[0].Id, b.Children[0].Id);
        var scene = new Scene();
        scene.Add(a);
        scene.Add(b);
        Assert.Equal(4, scene.Count);
    }

    [Theory]
    [InlineData("{ \"entities\": [] }")]
    [InlineData("not json")]
    [InlineData("{ \"formatVersion\": 99, \"prefab\": { \"components\": [] } }")]
    public void LoadPrefab_RejectsOtherFiles(string json)
    {
        Assert.Throws<InvalidDataException>(() => _serializer.LoadPrefabFromString(json));
    }

    [Fact]
    public async Task Project_FindsPrefabsByNameOrPath()
    {
        var project = await ProjectWithBulletAsync();
        var path = project.DefaultPrefabPath("Bullet");

        Assert.Equal(path, project.FindPrefab("Bullet"));
        Assert.Equal(path, project.FindPrefab("Prefabs/Bullet"));
        Assert.Equal(path, project.FindPrefab("Prefabs/Bullet.prefab"));
        Assert.Null(project.FindPrefab("Missile"));
        Assert.Equal([path], project.FindPrefabs());
    }

    [Fact]
    public async Task Library_FindsPrefabsByName_AndSaysWhenThereIsNone()
    {
        var project = await ProjectWithBulletAsync();
        var library = new PrefabLibrary(project, ComponentRegistry.CreateDefault());

        Assert.Equal("Bullet", library.Instantiate("Bullet").Name);
        Assert.Equal("Bullet", library.Instantiate("Prefabs/Bullet.prefab").Name);
        Assert.Throws<FileNotFoundException>(() => library.Instantiate("Missile"));
    }

    [Fact]
    public async Task Spawn_ByName_AddsACopyAtThePosition()
    {
        var project = await ProjectWithBulletAsync();
        var scene = new Scene();
        var game = new Game(scene, new InputState())
        {
            PrefabLoader = new PrefabLibrary(project, ComponentRegistry.CreateDefault()).Instantiate,
        };

        var first = game.Spawn("Bullet", new Vector2(-1, 5));
        var second = game.Spawn("Bullet");

        Assert.Equal(new Vector3(-1, 5, 2), first.Transform.Position);
        Assert.Equal(new Vector3(3, 4, 2), second.Transform.Position);
        Assert.Same(scene, first.Scene);
        Assert.Equal(4, scene.Count);
    }

    [Fact]
    public void Spawn_ByName_WithoutLoader_Throws()
    {
        var game = new Game(new Scene(), new InputState());

        Assert.Throws<InvalidOperationException>(() => game.Spawn("Bullet"));
    }

    [Fact]
    public async Task LoadedGame_SpawnsPrefabs()
    {
        var project = await ProjectWithBulletAsync();

        var loaded = await GameLoader.LoadAsync(project.RootPath);

        Assert.Equal("Bullet", loaded.LoadPrefab("Prefabs/Bullet").Name);
    }
}
