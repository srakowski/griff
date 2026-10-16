using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Assets;
using Griff.Editor.Features.Output;
using Griff.Editor.Features.Scenes;
using Griff.Editor.Features.Scripts;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Tests;

/// <summary>
/// What used to live only in click handlers is a command agents can run (Add to Scene, Open in Another Program, Show in
/// Explorer, Set as Start Scene for any scene, going to a script error), ui_set and ui_invoke leave scene data to the
/// edits undo knows about, destructive commands say so, and a script reload reaches every live game.
/// </summary>
public sealed class AgentGapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static T Wait<T>(Task<T> task)
    {
        return EditorHost.Pump(task);
    }

    private static void Wait(Task task) => Wait(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default));

    // A new project with a Coin prefab, a 32x16 picture and a second scene, Level 2.
    private (Workspace Workspace, Project Project) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;

        var coin = new Entity("Coin");
        coin.AddComponent<Pickup>().Amount = 1;
        coin.Transform.Position = new System.Numerics.Vector3(4, 2, 0);
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Coin"))!);
        File.WriteAllText(project.DefaultPrefabPath("Coin"), workspace.Serializer.SavePrefabToString(coin));

        Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Sprites"));
        var pixels = BitmapSource.Create(32, 16, 96, 96, PixelFormats.Bgra32, null, new byte[32 * 16 * 4], 32 * 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(pixels));
        using (var file = File.Create(Path.Combine(project.AssetsPath, "Sprites", "Banner.png")))
            png.Save(file);

        File.WriteAllText(project.CreateScene("Level 2"), workspace.Serializer.SaveToString(new Scene()));
        return (workspace, project);
    }

    [Fact]
    public void AddToScene_PlacesAPrefabOrAnImage_AsOneUndoStep_AndSelectsIt() => EditorHost.Run(services =>
    {
        var (workspace, _) = Open(services);
        var command = services.GetRequiredService<AddAssetToSceneCommand>();
        var scene = workspace.Scene!;
        var selection = services.GetRequiredService<ISelection>();

        Assert.Contains("Prefabs/Coin.prefab", command.Choices!);
        Assert.Contains("Sprites/Banner.png", command.Choices!);

        Wait(command.ChooseAsync("Prefabs/Coin.prefab"));
        var copy = scene.Scene.Entities.Single(e => e.Name == "Coin");
        Assert.Equal("Prefabs/Coin.prefab", copy.Prefab);
        Assert.Equal(4, copy.Transform.Position.X);
        Assert.Same(copy, selection.Entity);

        Wait(command.ChooseAsync("Sprites/Banner.png"));
        var banner = scene.Scene.Entities.Single(e => e.Name == "Banner");
        Assert.Equal("Sprites/Banner.png", banner.GetComponent<Sprite>()!.Image);
        Assert.Equal(new System.Numerics.Vector2(2, 1), banner.GetComponent<Sprite>()!.Size);

        scene.History.Undo();
        Assert.DoesNotContain(scene.Scene.Entities, e => e.Name == "Banner");
        Assert.Contains(scene.Scene.Entities, e => e.Name == "Coin");

        Assert.Throws<ArgumentException>(() => Wait(command.ChooseAsync("Prefabs/Nothing.prefab")));
    });

    [Fact]
    public void AddToScene_AtAPlace_IsWhereTheSceneViewDropsIt() => EditorHost.Run(services =>
    {
        var (workspace, _) = Open(services);
        var placement = services.GetRequiredService<AssetPlacement>();

        var coin = placement.AddToScene("Prefabs/Coin.prefab", new System.Numerics.Vector2(-3, 5));

        Assert.Equal(new System.Numerics.Vector3(-3, 5, 0), coin.Transform.Position);
        Assert.NotNull(workspace.Scene!.History.UndoDescription);
    });

    [Fact]
    public void SetStartScene_PicksAnyScene_NotJustTheOpenOne() => EditorHost.Run(services =>
    {
        var (workspace, project) = Open(services);
        var command = services.GetRequiredService<SetStartSceneCommand>();

        Assert.Equal(["Level 2", "Main"], command.Choices!.Order());
        Wait(command.ChooseAsync("Level 2"));

        Assert.Equal(project.FindScene("Level 2"), project.StartScenePath);
        Assert.Equal("Main", workspace.Scene!.Name);

        // The Assets panel picks by path.
        Wait(command.ChooseAsync(PrefabFiles.AssetPathOf(project, project.FindScene("Main")!)));
        Assert.Equal(project.FindScene("Main"), project.StartScenePath);
    });

    [Fact]
    public void OpenInAnotherProgram_AndShowInExplorer_HandFilesInAssetsToWindows() => EditorHost.Run(
        s => s.AddSingleton<IExternalPrograms, RecordedPrograms>(),
        services =>
        {
            var (_, project) = Open(services);
            var programs = (RecordedPrograms)services.GetRequiredService<IExternalPrograms>();

            Wait(services.GetRequiredService<OpenAssetExternallyCommand>().ChooseAsync("Sprites/Banner.png"));
            Wait(services.GetRequiredService<ShowAssetInExplorerCommand>().ChooseAsync("Sprites"));
            Wait(services.GetRequiredService<ShowAssetInExplorerCommand>().ExecuteAsync());

            Assert.Equal([
                "open " + Path.Combine(project.AssetsPath, "Sprites", "Banner.png"),
                "show " + Path.Combine(project.AssetsPath, "Sprites"),
                "show " + Path.GetFullPath(project.AssetsPath),
            ], programs.Calls);

            // Nothing outside Assets, and nothing that isn't there.
            Assert.Throws<ArgumentException>(() => Wait(services.GetRequiredService<OpenAssetExternallyCommand>().ChooseAsync("../project.griff")));
            Assert.Throws<ArgumentException>(() => Wait(services.GetRequiredService<ShowAssetInExplorerCommand>().ChooseAsync("Nowhere")));
        });

    [Fact]
    public void OpeningAProgram_IsRefused() => Assert.Throws<InvalidOperationException>(() => new ExternalPrograms().Open(Path.Combine(Path.GetTempPath(), "setup.bat")));

    [Fact]
    public void GoToScriptError_OpensTheScriptAtTheLine_OutputNamed() => EditorHost.Run(services =>
    {
        var (_, project) = Open(services);
        var scripts = Path.Combine(project.AssetsPath, "Scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "Ship.cs"), "public class Ship : Behavior\n{\n    int x = ;\n}\n");
        var log = services.GetRequiredService<OutputLog>();
        log.Entries.Add(new LogEntry(DateTime.Now, LogLevel.Information, "Test", "Scripts compiled"));
        log.Entries.Add(new LogEntry(DateTime.Now, LogLevel.Error, "Test", "Ship.cs(3,13): error CS1525: Invalid expression term ';'"));
        var command = services.GetRequiredService<GoToScriptErrorCommand>();

        Assert.Equal(["Ship.cs(3,13)"], command.Choices);
        Wait(command.ExecuteAsync());

        var editors = services.GetRequiredService<ScriptEditors>();
        var document = Assert.Single(editors.Documents);
        Assert.Equal("Ship.cs", document.FileName);
    });

    [Fact]
    public void DestructiveCommands_AreMarked_NotGuessedFromTheirIds() => EditorHost.Run(services =>
    {
        var tools = services.GetRequiredService<AgentServer>().Tools;

        Assert.True(tools["Entity_Delete"].Destructive);
        Assert.True(tools["Game_ClearSaveData"].Destructive);
        Assert.False(tools["Scene_Save"].Destructive);
        Assert.False(tools["Asset_AddToScene"].Destructive);
        foreach (var command in services.GetRequiredService<CommandRegistry>().All)
            Assert.Equal(command.GetType().IsDefined(typeof(DestructiveAttribute), false), tools[CommandTools.ToolName(command.Id)].Destructive);

        // The commands that pick an asset or a scene take it as their choice.
        Assert.Contains("choice", tools["Asset_AddToScene"].InputSchema.ToJsonString());
        Assert.Contains("choice", tools["Scene_SetStart"].InputSchema.ToJsonString());
    });

    [Fact]
    public void UiSetAndInvoke_LeaveEntitiesAndComponents_ToEditsThatUndo() => EditorHost.Run(services =>
    {
        var (workspace, _) = Open(services);
        var tools = services.GetRequiredService<AgentServer>().Tools;
        var camera = workspace.Scene!.Scene.Entities[0];
        var name = camera.Name;

        var renamed = Assert.ThrowsAny<AgentException>(() => tools["ui_set"].Invoke(ToolArgs.Parse("""{"target": "Workspace", "path": "Scene.Scene.Roots[0].Name", "value": "Renamed"}""")).GetAwaiter().GetResult());
        Assert.Contains("entity_update", renamed.Message);
        Assert.Throws<AgentException>(() => tools["ui_set"].Invoke(ToolArgs.Parse("""{"target": "Workspace", "path": "Scene.Scene.Roots[0].Transform.Position", "value": [1, 2, 3]}""")).GetAwaiter().GetResult());
        Assert.Throws<AgentException>(() => tools["ui_invoke"].Invoke(ToolArgs.Parse("""{"target": "Workspace", "path": "Scene.Scene.Clear"}""")).GetAwaiter().GetResult());
        Assert.Equal(name, camera.Name);

        // Everything else can still be set.
        tools["ui_set"].Invoke(ToolArgs.Parse("""{"target": "Output", "path": "ShowMessages", "value": false}""")).GetAwaiter().GetResult();
        Assert.False(services.GetRequiredService<OutputPanel>().ShowMessages);
    });

    [Fact]
    public void ReloadingScripts_ReachesLiveScenes_OutsideTheWorkspace() => EditorHost.Run(services =>
    {
        var (workspace, project) = Open(services);
        var script = Path.Combine(project.AssetsPath, "Scripts", "Spin.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, "public class Spin : Behavior { public float Speed { get; set; } = 1; }");
        Assert.True(workspace.ReloadScripts()!.Success);

        // Another player's game, as Play with Players runs it, with a Spin of its own.
        var other = new Scene();
        var spinner = new Entity("Spinner");
        var old = (Component)Activator.CreateInstance(workspace.Components.Find("Spin")!.Type)!;
        old.GetType().GetProperty("Speed")!.SetValue(old, 3f);
        spinner.AddComponent(old);
        other.Add(spinner);
        workspace.AddLiveScenes(() => [other]);

        File.WriteAllText(script, "public class Spin : Behavior { public float Speed { get; set; } = 1; public float Turns { get; set; } }");
        Assert.True(workspace.ReloadScripts()!.Success);

        var now = spinner.Components.Single(c => c.GetType().Name == "Spin");
        Assert.NotSame(old.GetType(), now.GetType());
        Assert.Same(workspace.Components.Find("Spin")!.Type, now.GetType());
        Assert.Equal(3f, now.GetType().GetProperty("Speed")!.GetValue(now));
    });

    private sealed class RecordedPrograms : IExternalPrograms
    {
        public List<string> Calls { get; } = [];

        public void Open(string path) => Calls.Add("open " + path);

        public void ShowInExplorer(string path) => Calls.Add("show " + path);

        public void OpenInBrowser(string url) => Calls.Add("browse " + url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Games")]
    [InlineData(@"..\Games")]
    public void CreatingAProject_WithAnEmptyOrRelativeLocation_IsRejected_NotMadeInTheWorkingDirectory(string location) => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        var before = Directory.GetDirectories(Directory.GetCurrentDirectory()).Length;

        var error = Assert.Throws<ArgumentException>(() => Wait(workspace.CreateProjectAsync(location, "Game")));
        Assert.Contains("full folder path", error.Message);
        Assert.Null(workspace.Project);
        Assert.Equal(before, Directory.GetDirectories(Directory.GetCurrentDirectory()).Length);
    });
}
