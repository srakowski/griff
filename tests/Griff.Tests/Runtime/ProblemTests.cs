using System.Numerics;
using System.Text.RegularExpressions;
using Griff.Input;
using Griff.Modules.PhysicsGames;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Tests.Architecture;

namespace Griff.Tests.Runtime;

/// <summary>
/// A mistake in a game's data (a prefab that won't load, a name that finds nothing, a bad row) goes to one place, the
/// game's <see cref="ProblemLog"/>, once, naming the entity and component, and the game goes on; a mistake in code throws.
/// <see cref="Game.TrySpawn(string, Component?, string, Entity?)"/> is how a system or script spawns what data names, so a
/// missing prefab no longer throws in one module and goes unsaid in another.
/// </summary>
public sealed partial class ProblemTests
{
    private readonly Scene _scene = new();
    private readonly Game _game;

    public ProblemTests() => _game = new Game(_scene, new InputState());

    private static Entity Prefab(string name) => throw new FileNotFoundException($"No prefab named \"{name}\".");

    private Slingshot AddSlingshot(string shot)
    {
        var entity = new Entity("Sling");
        var slingshot = entity.AddComponent<Slingshot>();
        (slingshot.Shot, slingshot.Shots) = (shot, 2);
        _scene.Add(entity);
        return slingshot;
    }

    [Fact]
    public void AMissingPrefab_WarnsOnce_NamingTheEntityAndComponent_AndTheGameGoesOn()
    {
        _game.PrefabLoader = Prefab;
        var slingshot = AddSlingshot("Nowhere");

        Assert.Null(_game.TrySpawn("Nowhere", slingshot, "its Shot"));
        Assert.Null(_game.TrySpawn("Nowhere", slingshot, "its Shot", new Vector2(1, 2)));

        var problem = Assert.Single(_game.Problems.Problems);
        Assert.Equal(("Sling", "Slingshot"), (problem.EntityName, problem.ComponentType));
        Assert.Contains("Slingshot Sling: its Shot \"Nowhere\" won't load", problem.Message, StringComparison.Ordinal);
        Assert.Equal([problem], _game.Problems.On(slingshot));
    }

    [Fact]
    public void ASystemSpawningWhatDataNames_WarnsInsteadOfThrowing()
    {
        _game.PrefabLoader = Prefab;
        var slingshot = AddSlingshot("Nowhere");

        for (var i = 0; i < 3; i++)
            _game.Tick(1 / 60f);

        Assert.Contains(_game.Problems.Lines, l => l.Contains("its Shot \"Nowhere\" won't load", StringComparison.Ordinal));
        Assert.Single(_game.Problems.On(slingshot));
    }

    [Fact]
    public void AGameWithNoPrefabs_SaysSoToo_AndAFilePrefabThatDoesNotReadIsDataToo()
    {
        var slingshot = AddSlingshot("Anything");
        Assert.Null(_game.TryLoadPrefab("Anything", slingshot, "its Shot"));
        _game.PrefabLoader = _ => throw new System.Text.Json.JsonException("bad file");
        Assert.Null(_game.TryLoadPrefab("Broken", slingshot, "its Shot"));

        Assert.Equal(2, _game.Problems.Count);
        Assert.Contains(_game.Problems.Lines, l => l.Contains("no prefab loader", StringComparison.Ordinal) || l.Contains("can't load prefabs", StringComparison.Ordinal));
        Assert.Contains(_game.Problems.Lines, l => l.Contains("bad file", StringComparison.Ordinal));
    }

    [Fact]
    public void AMistakeInCode_StillThrows()
    {
        _game.PrefabLoader = _ => throw new DivideByZeroException("a bug in the loader");
        Assert.Throws<DivideByZeroException>(() => _game.TrySpawn("Any", null, "its Shot"));
        Assert.Throws<FileNotFoundException>(() => { _game.PrefabLoader = Prefab; _game.Spawn("Nowhere"); });
        Assert.Empty(_game.Problems.Problems);
    }

    [Fact]
    public void ThePrefabThatLoads_SpawnsAsSpawnDoes()
    {
        _game.PrefabLoader = name => new Entity(name);
        var spawned = _game.TrySpawn("Coin", null, "the coin", new Vector2(3, 4));
        Assert.NotNull(spawned);
        Assert.Same(_scene, spawned.Scene);
        Assert.Equal((3f, 4f), (spawned.Transform.Position.X, spawned.Transform.Position.Y));
        Assert.Empty(_game.Problems.Problems);
    }

    [Fact]
    public void TheLog_SaysEachProblemOnce_ToEveryoneListening_AndHandsLoadersALineSink()
    {
        var log = new ProblemLog();
        var heard = new List<string>();
        log.Reported += p => heard.Add(p.Message);
        log.Report("Same");
        log.Report("Same");
        ((ICollection<string>)log).Add("Other");
        log.MarkSaid(["Quiet"]);
        log.Report("Quiet");

        Assert.Equal(["Same", "Other"], heard);
        Assert.Equal(["Same", "Other"], log);
        log.Clear();
        log.Report("Same");
        Assert.Equal(["Same", "Other", "Same"], heard);
    }

    [Fact]
    public void ARewind_DoesNotMakeAGameSayAProblemTwice()
    {
        _game.PrefabLoader = Prefab;
        var slingshot = AddSlingshot("Nowhere");
        _game.TrySpawn("Nowhere", slingshot, "its Shot");
        var said = _game.Problems.Count;
        var timeline = new Timeline(new Griff.Scenes.Serialization.SceneSerializer(Griff.Scenes.Serialization.ComponentRegistry.CreateDefault()));
        Assert.True(timeline.Record(_game, force: true));
        timeline.Restore(0, _game);
        _game.TrySpawn("Nowhere", slingshot, "its Shot");
        Assert.Equal(said, _game.Problems.Count);
    }

    [Fact]
    public void ADataMistakeInASceneFile_WarnsThroughTheSameLog_NamingItsEntityAndComponent()
    {
        var json = $$"""
            { "formatVersion": 1, "entities": [ { "id": "{{Guid.NewGuid()}}", "name": "Thing", "components": [
                { "type": "Transform" }, { "type": "Health", "max": "lots" }, { "type": "NoSuchComponent" } ] } ] }
            """;
        var log = new ProblemLog();

        var scene = new Griff.Scenes.Serialization.SceneSerializer(Griff.Scenes.Serialization.ComponentRegistry.CreateDefault()).LoadFromString(json, log);

        Assert.Single(scene.Roots);
        Assert.Contains(log.Problems, p => p is { EntityName: "Thing", ComponentType: "Health" });
        Assert.Contains(log.Problems, p => p is { EntityName: "Thing", ComponentType: "NoSuchComponent" });
        Assert.All(log.Problems, p => Assert.Equal("Thing", p.EntityName));
    }

    // The rule is kept: nothing in a module or the running game's systems reaches for the prefab loader or swallows a
    // mistake; they ask Game.TrySpawn / TryLoadPrefab, which say it.
    [Fact]
    public void NoSystem_LoadsPrefabsOutsideTheGame_OrSwallowsAMistakeSilently()
    {
        var root = Path.Combine(LayerTests.RepoRoot(), "src");
        string[] hosts = ["Game.cs", "GameLoader.cs", "PrefabLibrary.cs"];
        string[] folders = ["Modules", Path.Combine("Griff", "Runtime")];
        var files = folders
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        var found = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var name = Path.GetRelativePath(root, file);
            if (!hosts.Contains(Path.GetFileName(file)) && text.Contains("PrefabLoader", StringComparison.Ordinal))
                found.Add($"{name} uses PrefabLoader; ask Game.TrySpawn or Game.TryLoadPrefab");
            if (EmptyCatch().IsMatch(text))
                found.Add($"{name} has a catch that does nothing; say it with Game.Warn (a mistake in data) or let it throw (a mistake in code)");
        }

        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [GeneratedRegex(@"catch\s*(\([^)]*\))?\s*(when\s*\([^{]*\))?\s*\{\s*\}")]
    private static partial Regex EmptyCatch();
}
