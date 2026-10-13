using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scripting;

namespace Griff.Tests.Scripting;

public sealed class GameExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-export-" + Guid.NewGuid().ToString("N"));
    private readonly string _player;
    private readonly string _output;
    private readonly string _cache;

    public GameExporterTests()
    {
        _player = Path.Combine(_root, "player", "Griff.Player.exe");
        _output = Path.Combine(_root, "out");
        _cache = Path.Combine(_root, "cache");
        Directory.CreateDirectory(Path.GetDirectoryName(_player)!);
        File.WriteAllText(_player, "player exe");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Export_PacksContentAndScriptsIntoOneExe()
    {
        var project = await Project.CreateAsync(_root, "Space Hop");
        Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Scripts"));
        File.WriteAllText(Path.Combine(project.AssetsPath, "Scripts", "Jumper.cs"), "public class Jumper : Behavior { public float Height { get; set; } = 2; }");
        File.WriteAllText(Path.Combine(project.AssetsPath, "hero.png"), "png");

        var result = GameExporter.Export(project, _player, _output);

        Assert.True(result.Success, result.Error);
        Assert.Equal(Path.Combine(_output, "Space Hop.exe"), result.Executable);
        Assert.Equal(["Space Hop.exe"], Directory.GetFileSystemEntries(_output).Select(Path.GetFileName));
        Assert.StartsWith("player exe", File.ReadAllText(result.Executable!));
        Assert.True(GamePackage.IsPackaged(result.Executable!));

        var game = GamePackage.Extract(result.Executable!, _cache, "Space Hop")!;
        Assert.True(File.Exists(Path.Combine(game, "project.griff")));
        Assert.True(File.Exists(Path.Combine(game, "Assets", "Main.scene")));
        Assert.True(File.Exists(Path.Combine(game, "Assets", "hero.png")));
        Assert.False(File.Exists(Path.Combine(game, "Assets", "Scripts", "Jumper.cs")));
        Assert.True(File.Exists(Path.Combine(game, ExportLayout.ScriptsAssembly)));
    }

    [Fact]
    public async Task ExportedGame_LoadsWithItsScripts()
    {
        var project = await Project.CreateAsync(_root, "Loader Test");
        File.WriteAllText(Path.Combine(project.AssetsPath, "Bouncer.cs"), "public class Bouncer : Behavior { public float Height { get; set; } = 2; }");

        var result = GameExporter.Export(project, _player, _output);
        var loaded = await GameLoader.LoadAsync(GamePackage.Extract(result.Executable!, _cache, "Loader Test")!);

        Assert.Equal("Loader Test", loaded.Project.Name);
        Assert.NotNull(loaded.Components.Find("Bouncer"));
        Assert.Equal("Main Camera", Assert.Single(loaded.Scene.Roots).Name);
    }

    [Fact]
    public async Task ExportedScripts_AreOptimized_WhileTheEditorsAreNot()
    {
        var project = await Project.CreateAsync(_root, "Fast Scripts");
        File.WriteAllText(Path.Combine(project.AssetsPath, "Spinner.cs"), "public class Spinner : Behavior { public float Speed { get; set; } = 2; }");

        var result = GameExporter.Export(project, _player, _output);
        var game = GamePackage.Extract(result.Executable!, _cache, "Fast Scripts")!;
        var exported = File.ReadAllBytes(Path.Combine(game, ExportLayout.ScriptsAssembly));
        var editor = ScriptCompiler.CompileFolder(project.AssetsPath).Assembly!;

        Assert.False(JitOptimizerDisabled(exported));
        Assert.True(JitOptimizerDisabled(editor));
    }

    // A Debug build asks the JIT not to optimize (DebuggableAttribute); a Release build doesn't.
    private static bool JitOptimizerDisabled(byte[] assembly)
    {
        var context = new System.Runtime.Loader.AssemblyLoadContext("optimized?", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(assembly);
            return System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Diagnostics.DebuggableAttribute>(context.LoadFromStream(stream))?.IsJITOptimizerDisabled ?? false;
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public async Task ExportedArcade_KnowsItsStartScene_AndPlaysIntoItsGameScene()
    {
        var project = await Project.CreateAsync(_root, "Arcade Export", ProjectTemplate.Arcade);

        var result = GameExporter.Export(project, _player, _output);
        Assert.True(result.Success, result.Error);
        var loaded = await GameLoader.LoadAsync(GamePackage.Extract(result.Executable!, _cache, "Arcade Export")!);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(Project.DefaultSceneName, loaded.SceneName);

        // As the player sets it up: Enter on the menu clicks Play, which loads the game scene.
        var input = new InputState();
        var game = new Game(loaded.Scene, input) { SceneLoader = loaded.LoadScene, PrefabLoader = loaded.LoadPrefab, SceneName = loaded.SceneName };
        game.Tick(1 / 60.0);
        input.SetKey(Key.Enter, true);
        game.Tick(1 / 60.0);
        input.EndFrame();
        for (var i = 0; i < 30; i++) // through the button's fade
            game.Tick(1 / 60.0);

        Assert.Equal("Game", game.SceneName);
        Assert.NotNull(game.Find("Player"));
    }

    [Fact]
    public async Task Export_WithScriptErrors_Fails()
    {
        var project = await Project.CreateAsync(_root, "Broken");
        File.WriteAllText(Path.Combine(project.AssetsPath, "Bad.cs"), "public class Bad : Behavior {");

        var result = GameExporter.Export(project, _player, _output);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.False(File.Exists(Path.Combine(_output, "Broken.exe")));
    }

    [Fact]
    public async Task Export_ReplacesItsOwnOutput_ButNotOtherFiles()
    {
        var project = await Project.CreateAsync(_root, "Again");
        Assert.True(GameExporter.Export(project, _player, _output).Success);
        Assert.True(GameExporter.Export(project, _player, _output).Success);

        var foreign = await Project.CreateAsync(_root, "Foreign");
        File.WriteAllText(Path.Combine(_output, "Foreign.exe"), "mine");

        var result = GameExporter.Export(foreign, _player, _output);

        Assert.False(result.Success);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_output, "Foreign.exe")));
    }

    [Fact]
    public async Task Export_WithSuffix_SitsBesideTheOtherKind()
    {
        var project = await Project.CreateAsync(_root, "Both");

        Assert.True(GameExporter.Export(project, _player, _output).Success);
        var small = GameExporter.Export(project, _player, _output, " (needs .NET)");

        Assert.True(small.Success, small.Error);
        Assert.True(File.Exists(Path.Combine(_output, "Both.exe")));
        Assert.Equal(Path.Combine(_output, "Both (needs .NET).exe"), small.Executable);
    }

    [Fact]
    public async Task Extract_UnpacksOncePerVersion_AndClearsOldVersions()
    {
        var project = await Project.CreateAsync(_root, "Versions");
        var exe = GameExporter.Export(project, _player, _output).Executable!;
        var first = GamePackage.Extract(exe, _cache, "Versions")!;
        File.WriteAllText(Path.Combine(first, "touched.txt"), "kept while the game is unchanged");
        Assert.Equal(first, GamePackage.Extract(exe, _cache, "Versions"));
        Assert.True(File.Exists(Path.Combine(first, "touched.txt")));

        File.WriteAllText(Path.Combine(project.AssetsPath, "new.txt"), "a change");
        GameExporter.Export(project, _player, _output);
        var second = GamePackage.Extract(exe, _cache, "Versions")!;

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(Path.Combine(second, "Assets", "new.txt")));
        Assert.False(Directory.Exists(first));
    }

    [Fact]
    public void Extract_OfAPlainExe_IsNull()
    {
        Assert.False(GamePackage.IsPackaged(_player));
        Assert.Null(GamePackage.Extract(_player, _cache, "Plain"));
    }
}
