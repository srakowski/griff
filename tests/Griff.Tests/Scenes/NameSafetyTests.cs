using System.Collections;
using System.Numerics;
using Griff.Editing;
using Griff.Input;
using Griff.Modules.VisualNovels;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Templates;

namespace Griff.Tests.Scenes;

/// <summary>
/// What scenes name by name stays safe: a renamed action or output keeps what names it working, a name that resolves to
/// nothing is said (loader, Inspector, scene_tree, Output), a dialogue tag word or a saved name belongs to one thing, and
/// the types a script sees by name are unique.
/// </summary>
public sealed class NameSafetyTests
{
    public static TheoryData<string> Templates => [.. ProjectTemplates.All.Select(t => t.Name)];

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task EveryTemplateScene_NamesOnlyActionsAndOutputsThatResolve(string template)
    {
        var root = Path.Combine(Path.GetTempPath(), "griff-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = await Project.CreateAsync(root, "Names", ProjectTemplates.Get(template));
            var registry = ComponentRegistry.CreateDefault();
            Assert.True(new Griff.Scripting.ScriptHost(registry).ReloadFolder(project.AssetsPath, []).Success);
            foreach (var path in project.FindScenes())
            {
                var scene = GameLoader.LoadScene(project, registry, Path.GetFileNameWithoutExtension(path));
                Assert.Empty(NameChecks.Lines(scene, registry));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    public sealed class Door : Component
    {
        public bool Opened { get; private set; }

        public int Coins { get; set; }

        /// <summary>Opens it.</summary>
        [Action]
        [FormerName("Unlock")]
        public void Open() => Opened = true;

        /// <summary>How many coins it holds.</summary>
        [Output]
        [FormerName("Gold")]
        public int Money => Coins;
    }

    private static (Scene Scene, Door Door) Build(string action, string output)
    {
        var scene = new Scene();
        var entity = new Entity("Door");
        var door = entity.AddComponent<Door>();
        door.Coins = 7;
        var trigger = entity.AddComponent<Trigger>();
        (trigger.Message, trigger.Do) = ("Go", action);
        var counter = entity.AddComponent<Counter>();
        (counter.Track, counter.Output) = ("Door", output);
        scene.Add(entity);
        return (scene, door);
    }

    private static ComponentRegistry Registry()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Door>();
        return registry;
    }

    [Fact]
    public void ARenamedAction_KeepsASavedTriggerWorking()
    {
        var (scene, door) = Build("Unlock", "Money");
        var registry = Registry();
        var game = new Game(scene, new InputState()) { Components = registry };

        game.Send("Go");
        game.Advance(1 / 60.0);

        Assert.True(door.Opened);
        Assert.Empty(NameChecks.Lines(scene, registry));
        Assert.NotNull(ComponentActions.Find(door, "unlock"));
        Assert.NotNull(ComponentActions.Find(door, "Door.Unlock"));
    }

    [Fact]
    public void ARenamedOutput_KeepsACounterReadingIt()
    {
        var (scene, door) = Build("Open", "Gold");

        Assert.Empty(NameChecks.Lines(scene, Registry()));
        Assert.Equal(7, ComponentOutputs.Read(door.Entity!, "Gold"));
        Assert.Equal(7, ComponentOutputs.Read(door.Entity!, "Money"));
    }

    [Fact]
    public void ANameThatResolvesToNothing_IsSaidByTheCheckAndByAGameAsItStarts()
    {
        var (scene, _) = Build("Explode", "Silver");
        var registry = Registry();
        var lines = NameChecks.Lines(scene, registry).ToList();

        Assert.Contains(lines, l => l.Contains("Trigger.Do", StringComparison.Ordinal) && l.Contains("Explode", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Counter.Output", StringComparison.Ordinal) && l.Contains("Silver", StringComparison.Ordinal));

        var game = new Game(scene, new InputState()) { Components = registry };
        var said = new List<string>();
        game.Problems.Reported += p => said.Add(p.Message);
        game.Advance(1 / 60.0);
        game.Advance(1 / 60.0);

        Assert.Equal(lines.Order(), said.Order());
    }

    [Fact]
    public void WhatTheLoaderReported_TheGameDoesNotSayAgain()
    {
        var (scene, _) = Build("Explode", "Money");
        var registry = Registry();
        var warnings = NameChecks.Lines(scene, registry).ToList();
        var game = new Game(scene, new InputState()) { Components = registry };
        game.AlreadySaid(warnings);
        var said = new List<string>();
        game.Problems.Reported += p => said.Add(p.Message);
        game.Advance(1 / 60.0);

        Assert.Single(warnings);
        Assert.Empty(said);
    }

    [Fact]
    public void TwoComponentsClaimingOneDialogueTagWord_Fails()
    {
        Assert.Throws<InvalidOperationException>(() => Dialogue.ClaimTags([typeof(Mine), typeof(AlsoMine)]));
        Assert.Throws<InvalidOperationException>(() => Dialogue.ClaimTags([typeof(Greedy)]));
        Assert.Equal(typeof(Mine), Dialogue.ClaimTags([typeof(Mine)])["Wave"]);
        Assert.True(Dialogue.IsTagWord("show"));
    }

    [DialogueTags("wave", "bow")]
    private sealed class Mine : Component;

    [DialogueTags("WAVE")]
    private sealed class AlsoMine : Component;

    [DialogueTags("set")]
    private sealed class Greedy : Component;

    [Fact]
    public void ADialogueTagWord_IsDoneByOneRegistration()
    {
        var game = new Game(new Scene(), new InputState());
        // The Visual Novels module has already done its Stage's words as the game was set up.
        Assert.Throws<InvalidOperationException>(() => game.LevelSystem.OnDialogueTags(typeof(Stage), (_, _, _) => { }));
        Assert.Throws<InvalidOperationException>(() => game.LevelSystem.OnDialogueTags(typeof(Door), (_, _, _) => { }));
    }

    [Fact]
    public void ComponentsAreSavedUnderOneNameEach_ThoseTheyHadBeforeIncluded()
    {
        var registry = ComponentRegistry.CreateDefault();
        var names = registry.All.Select(d => d.Name).ToList();
        var formers = registry.All.SelectMany(d => d.FormerNames).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(formers.Count, formers.Distinct().Count());
        Assert.Empty(names.Intersect(formers));
        Assert.Throws<InvalidOperationException>(() => registry.Register<Renamed>());
    }

    [FormerName("Griff.Trigger")]
    private sealed class Renamed : Component;

    [Fact]
    public void ATypeNameAScriptSees_BelongsToOneNamespace()
    {
        // What a script can get without writing it (ScriptCompiler.ImplicitUsingsFor): System's common namespaces, Griff's and each module's its project uses.
        var visible = new HashSet<string>(["System", "System.Collections", "System.Collections.Generic", "System.Linq", "System.Numerics",
            "Griff", "Griff.Data", "Griff.Input", "Griff.Runtime", "Griff.Scenes", .. GameModule.All.Select(m => m.Namespace)]);
        System.Reflection.Assembly[] assemblies = [typeof(object).Assembly, typeof(IEnumerable).Assembly, typeof(List<>).Assembly, typeof(Enumerable).Assembly,
            typeof(Vector2).Assembly, typeof(System.Numerics.BigInteger).Assembly, typeof(Dialogue).Assembly, .. ModuleAssemblies.All];
        var clashes = assemblies.SelectMany(a => a.GetExportedTypes())
            .Where(t => !t.IsNested && t.Namespace is { } ns && visible.Contains(ns))
            .GroupBy(t => (t.Name, t.IsGenericType ? t.GetGenericArguments().Length : 0))
            .Where(g => g.Select(t => t.Namespace).Distinct().Count() > 1)
            .Select(g => $"{g.Key.Name}: {string.Join(", ", g.Select(t => t.FullName))}")
            .Order()
            .ToList();

        Assert.True(clashes.Count == 0, "A script naming one of these gets an ambiguity error:\n" + string.Join("\n", clashes));
    }

    [Fact]
    public void AnEntityCalledTeamColor_InAnOldScene_StillTakesItsTeamsColor()
    {
        var registry = ComponentRegistry.CreateDefault();
        var serializer = new SceneSerializer(registry);
        var scene = new Scene();
        var unit = new Entity("Unit");
        unit.AddComponent<Sprite>();
        scene.Add(unit);
        var band = new Entity("Team Color");
        band.AddComponent<Sprite>();
        scene.Add(band, unit);

        Assert.Empty(TeamColor.SpritesIn(unit));
        var loaded = serializer.LoadFromString(serializer.SaveToString(scene));

        Assert.Single(TeamColor.SpritesIn(loaded.Entities.Single(e => e.Name == "Unit")));
    }
}
