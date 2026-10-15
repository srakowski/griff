using Griff.Projects;
using Griff.Runtime;
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
        File.WriteAllText(Path.Combine(_player, WebExporter.Page), "<html>");
        File.WriteAllText(Path.Combine(_player, "main.js"), "// main");
        File.WriteAllText(Path.Combine(_player, "_framework", "dotnet.js"), "// .NET");
        _output = Path.Combine(_root, "Web Build");
    }

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
        Assert.Equal("<html>", File.ReadAllText(Path.Combine(_output, WebExporter.Page)));
        Assert.True(File.Exists(Path.Combine(_output, "_framework", "dotnet.js")));
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

        Assert.Equal("Loader Test", loaded.Project.Name);
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
}
