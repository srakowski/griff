using System.IO.Compression;
using System.Net.Http;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Scripting;

public sealed class WebExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-web-export-" + Guid.NewGuid().ToString("N"));
    private readonly string _player;
    private readonly string _output;

    public WebExporterTests()
    {
        // Stands in for the published web player (Griff.Web's wwwroot).
        _player = Path.Combine(_root, "player");
        Directory.CreateDirectory(Path.Combine(_player, "_framework"));
        File.WriteAllText(Path.Combine(_player, WebExporter.Page), Page);
        File.WriteAllText(Path.Combine(_player, "main.js"), "// main");
        File.WriteAllText(Path.Combine(_player, "_framework", "dotnet.js"), "// .NET");
        // The copies the publish compresses for servers set up to send them.
        File.WriteAllText(Path.Combine(_player, "_framework", "dotnet.js.br"), "brotli");
        File.WriteAllText(Path.Combine(_player, "_framework", "dotnet.js.gz"), "gzip");
        _output = Path.Combine(_root, "Web Build");
    }

    private const string Page = "<title>Griff</title><div id=\"title\">Griff</div>";

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Export_PutsThePlayerBesideTheGame_AndListsTheGamesFiles()
    {
        var project = await Project.CreateAsync(_root, "Space Hop");
        Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Scripts"));
        File.WriteAllText(Path.Combine(project.AssetsPath, "Scripts", "Jumper.cs"), "public class Jumper : Behavior { public float Height { get; set; } = 2; }");
        File.WriteAllText(Path.Combine(project.AssetsPath, "hero.png"), "png");

        var result = WebExporter.Export(project, _player, _output);

        Assert.True(result.Success, result.Error);
        Assert.Equal(Path.Combine(_output, WebExporter.Page), result.Executable);
        Assert.Equal("<title>Space Hop</title><div id=\"title\">Space Hop</div>", File.ReadAllText(Path.Combine(_output, WebExporter.Page)));
        Assert.True(File.Exists(Path.Combine(_output, "_framework", "dotnet.js")));
        Assert.Empty(Directory.GetFiles(_output, "*.br", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_output, "*.gz", SearchOption.AllDirectories));
        Assert.Empty(result.Warnings);
        var game = Path.Combine(_output, ExportLayout.GameFolder);
        Assert.False(File.Exists(Path.Combine(game, "Assets", "Scripts", "Jumper.cs")));
        Assert.Equal(
            ["Assets/Main.scene", "Assets/hero.png", ExportLayout.ScriptsAssembly, "project.griff"],
            File.ReadAllLines(Path.Combine(game, ExportLayout.WebFileList)));
    }

    [Fact]
    public async Task ExportedGame_LoadsWithItsScripts_FromWhatItsListNames()
    {
        var project = await Project.CreateAsync(_root, "Loader Test");
        File.WriteAllText(Path.Combine(project.AssetsPath, "Bouncer.cs"), "public class Bouncer : Behavior { public float Height { get; set; } = 2; }");
        Assert.True(WebExporter.Export(project, _player, _output).Success);

        // As the web player does: only the listed files, copied to a folder of their own.
        var game = Path.Combine(_output, ExportLayout.GameFolder);
        var fetched = Path.Combine(_root, "fetched");
        foreach (var file in File.ReadAllLines(Path.Combine(game, ExportLayout.WebFileList)))
        {
            var target = Path.Combine(fetched, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(game, file), target);
        }

        var loaded = await GameLoader.LoadAsync(fetched);

        Assert.Equal("Loader Test", loaded.Project?.Name);
        Assert.NotNull(loaded.Components.Find("Bouncer"));
        Assert.Equal("Main Camera", Assert.Single(loaded.Scene.Roots).Name);
    }

    [Fact]
    public async Task Export_ReplacesItsOwnOutput_ButNotAnotherFolder()
    {
        var project = await Project.CreateAsync(_root, "Again");
        Assert.True(WebExporter.Export(project, _player, _output).Success);
        File.WriteAllText(Path.Combine(project.AssetsPath, "new.txt"), "a change");
        Assert.True(WebExporter.Export(project, _player, _output).Success);
        Assert.Contains("Assets/new.txt", File.ReadAllLines(Path.Combine(_output, ExportLayout.GameFolder, ExportLayout.WebFileList)));

        var foreign = Path.Combine(_root, "Mine");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "notes.txt"), "mine");

        var result = WebExporter.Export(project, _player, foreign);

        Assert.False(result.Success);
        Assert.Equal(["notes.txt"], Directory.GetFileSystemEntries(foreign).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Export_WithoutThePlayer_OrWithScriptErrors_Fails()
    {
        var project = await Project.CreateAsync(_root, "Broken");

        Assert.False(WebExporter.Export(project, Path.Combine(_root, "nowhere"), _output).Success);

        File.WriteAllText(Path.Combine(project.AssetsPath, "Bad.cs"), "public class Bad : Behavior {");
        var result = WebExporter.Export(project, _player, _output);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.False(Directory.Exists(_output));
    }

    [Fact]
    public void ThePagesTitle_IsTheGamesName_Escaped() =>
        Assert.Equal("<title>Tom &amp; Jerry &lt;3</title><div id=\"title\">Tom &amp; Jerry &lt;3</div>", WebExporter.Titled(Page, "Tom & Jerry <3"));

    [Fact]
    public async Task Zip_HasThePageAtTheTop_ForItchIo()
    {
        var project = await Project.CreateAsync(_root, "Zipped");
        var result = WebExporter.Export(project, _player, _output);

        var zip = WebExporter.Zip(result.OutputFolder!);

        Assert.Equal(_output + ".zip", zip);
        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains(WebExporter.Page, names);
        Assert.Contains("_framework/dotnet.js", names);
        Assert.Contains("game/files.txt", names);
        Assert.Contains(ThirdPartyNotices.FileName, names);
        Assert.DoesNotContain(ExportLayout.Marker, names);
        Assert.Throws<ArgumentException>(() => WebExporter.Zip(_root));
    }

    [Fact]
    public async Task OutputIn_TheFolderPicked_OrAFolderForTheGameInIt()
    {
        var project = await Project.CreateAsync(Path.Combine(_root, "project"), "Space Hop");
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        var busy = Directory.CreateDirectory(Path.Combine(_root, "busy")).FullName;
        File.WriteAllText(Path.Combine(busy, "notes.txt"), "mine");

        Assert.Equal(empty, WebExporter.OutputIn(empty, project));
        Assert.Equal(Path.Combine(_root, "new"), WebExporter.OutputIn(Path.Combine(_root, "new"), project));
        Assert.Equal(Path.Combine(busy, "Space Hop (Web)"), WebExporter.OutputIn(busy, project));

        // The project's Build folder, where Build Game puts its .exe, always gets a folder of its own.
        var builds = Directory.CreateDirectory(Path.Combine(project.RootPath, "Build")).FullName;
        Assert.Equal(Path.Combine(builds, "Space Hop (Web)"), WebExporter.OutputIn(builds, project));

        // A folder an earlier web export wrote is exported into again.
        Assert.True(WebExporter.Export(project, _player, empty).Success);
        Assert.Equal(empty, WebExporter.OutputIn(empty, project));
    }

    [Fact]
    public async Task NetworkPlay_IsWarnedOf_ScenesPrefabsAndScripts()
    {
        var project = await Project.CreateAsync(_root, "Arena");
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        var settings = new Entity("Multiplayer");
        settings.AddComponent<Multiplayer>();
        scene.Add(settings);
        await using (var file = File.Create(Path.Combine(project.AssetsPath, "Lobby.scene")))
            serializer.Save(scene, file);
        var join = new Entity("Join");
        join.AddComponent<Button>().Action = ButtonAction.JoinGame;
        File.WriteAllText(Path.Combine(project.AssetsPath, "Join.prefab"), serializer.SavePrefabToString(join));
        File.WriteAllText(Path.Combine(project.AssetsPath, "Starter.cs"),
            "public class Starter : Behavior { protected override void Start() => Game.Network.Host(); }");

        var result = WebExporter.Export(project, _player, _output);

        Assert.True(result.Success, result.Error);
        Assert.Equal(3, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.StartsWith("Lobby.scene is set up for network play", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.StartsWith("Join.prefab is set up for network play", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.StartsWith("Starter.cs hosts or joins", StringComparison.Ordinal));
        Assert.All(result.Warnings, w => Assert.Contains("isn't available in a browser", w, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheServer_ServesTheExport_ToThisComputerOnly()
    {
        var project = await Project.CreateAsync(_root, "Served");
        Assert.True(WebExporter.Export(project, _player, _output).Success);
        await using var server = new WebServer(_output);
        server.Start();
        using var http = new HttpClient();

        Assert.StartsWith("http://127.0.0.1:", server.Url, StringComparison.Ordinal);
        var page = await http.GetAsync(server.Url);
        Assert.Equal("text/html; charset=utf-8", page.Content.Headers.ContentType!.ToString());
        Assert.Contains("<title>Served</title>", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("project.griff", await http.GetStringAsync(server.Url + "game/files.txt"), StringComparison.Ordinal);
        Assert.Equal("text/javascript; charset=utf-8", (await http.GetAsync(server.Url + "main.js")).Content.Headers.ContentType!.ToString());
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync(server.Url + "nothing.js")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync(server.Url + "..%2F..%2Fproject.griff")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, (await http.PostAsync(server.Url, new StringContent(""))).StatusCode);
    }
}
