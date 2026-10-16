using Griff.Exporting;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scripting;
using Griff.Templates;

namespace Griff.Tests.Runtime;

/// <summary>
/// A game uses the modules its project does (ProjectModules): those its scenes and prefabs have a component of and those its
/// scripts were built against. It builds the systems of those alone, its scripts get a using for each, and its export
/// carries their assemblies in its Modules folder and no other module's.
/// </summary>
public sealed class ProjectModulesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-modules-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static GameModule Module(string name) => GameModule.Named(name) ?? throw new InvalidOperationException($"No {name} module.");

    [Theory]
    [InlineData("Match3", new[] { "Match-3" })]
    [InlineData("Fighter", new[] { "Fighting" })]
    [InlineData("Pinball", new[] { "Physics Games" })]
    [InlineData("Arcade", new string[0])]
    [InlineData("Empty", new string[0])]
    public async Task ATemplatesProject_UsesTheModulesItsScenesAndPrefabsHave(string template, string[] modules)
    {
        var project = await Project.CreateAsync(Path.Combine(_root, template), template, ProjectTemplates.Get(template));

        Assert.Equal(modules, ProjectModules.Of(project).Select(m => m.Name));
        var loaded = await GameLoader.LoadAsync(project.RootPath);
        Assert.Equal(modules, loaded.Modules!.Select(m => m.Name));
    }

    // A game builds the systems of its modules and no other's; one made without a project runs every module's.
    [Fact]
    public async Task AGame_BuildsSystemsOnlyForItsModules()
    {
        var project = await Project.CreateAsync(_root, "Gems", ProjectTemplates.Get("Match3"));
        var loaded = await GameLoader.LoadAsync(project.RootPath);

        var game = loaded.NewGame();
        Assert.Equal(["Match-3"], game.Modules.Select(m => m.Name));
        var moduleSystems = game.Systems.Where(s => ModuleAssemblies.IsModule(s.GetType().Assembly)).Select(s => GameModule.Of(s.GetType())!.Name).Distinct();
        Assert.Equal(["Match-3"], moduleSystems);

        var everything = new Game(new Scene(), new InputState());
        Assert.Equal(GameModule.All, everything.Modules);
        Assert.Equal(GameModule.All.Count, everything.Systems.Where(s => ModuleAssemblies.IsModule(s.GetType().Assembly)).Select(s => GameModule.Of(s.GetType())).Distinct().Count());
    }

    // What a script names counts: a project whose scenes have no module, with a script that names a Match Board.
    [Fact]
    public async Task AScriptNamingAModulesType_BringsItsModule()
    {
        var project = await Project.CreateAsync(_root, "Scripted");
        File.WriteAllText(Path.Combine(project.AssetsPath, "Boards.cs"),
            "using Griff.Modules.Match3;\npublic class Boards : Behavior { public int Count => Entity!.Scene!.All<MatchBoard>().Count; }");
        var compiled = ScriptCompiler.CompileFolder(project.AssetsPath);
        Assert.True(compiled.Success, string.Join("\n", compiled.Errors));

        var scripts = System.Reflection.Assembly.Load(compiled.Assembly!);
        Assert.Empty(ProjectModules.Of(project));
        Assert.Equal(["Match-3"], ProjectModules.Of(project, scripts).Select(m => m.Name));
        Assert.Equal(["Match-3"], ProjectModules.BuiltAgainst(scripts).Select(m => m.Name));
        Assert.Equal(["Match-3"], GameExporter.ModulesOf(project, compiled.Assembly).Select(m => m.Name));
    }

    // Scripts get a using for each module their project uses, and none for the rest (a using away).
    [Fact]
    public async Task Scripts_GetUsingsForTheModulesTheirProjectUses()
    {
        var project = await Project.CreateAsync(_root, "Gems", ProjectTemplates.Get("Match3"));
        var usings = ScriptCompiler.ImplicitUsingsFor(ProjectModules.Of(project));
        Assert.Contains("global using Griff.Modules.Match3;", usings, StringComparison.Ordinal);
        Assert.DoesNotContain("Griff.Modules.Strategy", usings, StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(project.AssetsPath, "Count.cs"), "public class Count : Behavior { public int Boards => Entity!.Scene!.All<MatchBoard>().Count; }");
        Assert.True(ScriptCompiler.CompileFolder(project.AssetsPath).Success);

        File.WriteAllText(Path.Combine(project.AssetsPath, "Army.cs"), "public class Army : Behavior { public int Units => Entity!.Scene!.All<Commander>().Count; }");
        Assert.False(ScriptCompiler.CompileFolder(project.AssetsPath).Success);
        File.WriteAllText(Path.Combine(project.AssetsPath, "Army.cs"), "using Griff.Modules.Strategy;\npublic class Army : Behavior { public int Units => Entity!.Scene!.All<Commander>().Count; }");
        Assert.True(ScriptCompiler.CompileFolder(project.AssetsPath).Success);
    }

    // An open scene counts as it is, unsaved changes and all (the editor plays and builds scripts with them).
    [Fact]
    public void AnOpenScene_CountsAsItIs()
    {
        var scene = new Scene();
        var board = new Entity("Board");
        board.AddComponent<Griff.Modules.Match3.MatchBoard>();
        scene.Add(board);

        Assert.Equal(["Match-3"], ProjectModules.Of(Path.Combine(_root, "Nowhere"), null, [scene]).Select(m => m.Name));
        Assert.Equal(["Match-3"], ProjectModules.InScene(scene).Select(m => m.Name));
    }

    // An export carries the running parts of the modules its project uses, and nothing of the editor's; its host loads them.
    [Fact]
    public async Task AnExport_CarriesOnlyItsModules_WhichItsHostLoads()
    {
        var project = await Project.CreateAsync(_root, "Gems", ProjectTemplates.Get("Match3"));
        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");

        var export = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(export.Success, export.Error);
        var game = GamePackage.Extract(export.Executable!, Path.Combine(_root, "cache"), "Gems")!;

        Assert.Equal(["Griff.Modules.Match3.dll"], Directory.GetFiles(Path.Combine(game, ExportLayout.ModulesFolder)).Select(Path.GetFileName));
        Assert.DoesNotContain(Directory.EnumerateFiles(game, "*.dll", SearchOption.AllDirectories), f => Path.GetFileName(f).Contains("Edit", StringComparison.Ordinal) ||
                                                                                                         Path.GetFileName(f).Contains("Agent", StringComparison.Ordinal));
        // Loaded already here (the tests have every module), so the host's load hands back the one this process has.
        Assert.Equal([typeof(Griff.Modules.Match3.MatchBoard).Assembly], ModuleAssemblies.Load(Path.Combine(game, ExportLayout.ModulesFolder)));
        var loaded = await GameLoader.LoadAsync(game);
        Assert.Equal(["Match-3"], loaded.Modules!.Select(m => m.Name));
    }

    [Fact]
    public async Task AProjectWithNoModule_ExportsNoModulesFolder()
    {
        var project = await Project.CreateAsync(_root, "Plain");
        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");

        var export = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        var game = GamePackage.Extract(export.Executable!, Path.Combine(_root, "cache"), "Plain")!;

        Assert.False(Directory.Exists(Path.Combine(game, ExportLayout.ModulesFolder)));
    }

    // The module assemblies are found by name: a module's running parts, not its editor parts.
    [Fact]
    public void ModuleAssemblies_AreTheModulesRunningParts()
    {
        Assert.True(ModuleAssemblies.IsModuleName("Griff.Modules.Match3"));
        Assert.False(ModuleAssemblies.IsModuleName("Griff.Modules.Match3.Editor"));
        Assert.False(ModuleAssemblies.IsModuleName("Griff.Modules"));
        Assert.False(ModuleAssemblies.IsModuleName("Griff.Editing"));
        Assert.All(ModuleAssemblies.All, a => Assert.True(ModuleAssemblies.IsModule(a)));
        Assert.True(ModuleAssemblies.IsGriffs(typeof(Griff.Modules.Match3.MatchBoard)));
        Assert.True(ModuleAssemblies.IsGriffs(typeof(Sprite)));
        Assert.False(ModuleAssemblies.IsGriffs(typeof(ProjectModulesTests)));
        Assert.Throws<BadImageFormatException>(() => ModuleAssemblies.Load("Griff.Editing", [0]));
    }
}
