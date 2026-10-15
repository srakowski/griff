using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Build;
using Griff.Projects;
using Griff.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Build &gt; Build for Web: the open project exported with the web player the editor ships, into the folder picked, with
/// a zip for itch.io beside it, network play warned of, and served on this computer to play in the browser when asked.
/// The Build menu holds every export.
/// </summary>
public sealed class BuildWebTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-build-web-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void BuildForWeb_ExportsWhereItsPicked_WithAZip_AndPlaysItServedHere() => EditorHost.Run(
        s => s.AddSingleton<IExternalPrograms, RecordedPrograms>(),
        services =>
        {
            var project = Open(services, ProjectTemplates.Get("Platformer"));
            var programs = (RecordedPrograms)services.GetRequiredService<IExternalPrograms>();
            var preview = services.GetRequiredService<WebPreview>();
            // Any free port, not the one a person's editor serves on.
            preview.Port = 0;
            var picked = Path.Combine(_root, "Web");

            using (AgentDialogs.Answering(Answers(new { Folder = picked, PlayInBrowser = true })))
                Wait(services.GetRequiredService<BuildWebCommand>().ExecuteAsync());

            Assert.Empty(AgentDialogs.Reported);
            Assert.Contains($"<title>{project.Name}</title>", File.ReadAllText(Path.Combine(picked, "index.html")), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(picked, "_framework", "dotnet.js")));
            Assert.Contains("Assets/Music/theme.wav", File.ReadAllLines(Path.Combine(picked, ExportLayout.GameFolder, ExportLayout.WebFileList)));
            using (var zip = ZipFile.OpenRead(picked + ".zip"))
                Assert.Contains(zip.Entries, e => e.FullName == "index.html");

            // Served on this computer, and opened in the browser.
            Assert.Equal(picked, preview.Folder);
            Assert.Equal(["browse " + preview.Url], programs.Calls);
            using var http = new HttpClient();
            Assert.Contains($"<title>{project.Name}</title>", http.GetStringAsync(preview.Url).GetAwaiter().GetResult(), StringComparison.Ordinal);
            preview.Stop();
            Assert.Null(preview.Url);
        });

    [Fact]
    public void BuildForWeb_IntoAFolderInUse_MakesOneForTheGame_AndShowsTheZipWhenNotPlayed() => EditorHost.Run(
        s => s.AddSingleton<IExternalPrograms, RecordedPrograms>(),
        services =>
        {
            var project = Open(services, null);
            var programs = (RecordedPrograms)services.GetRequiredService<IExternalPrograms>();
            var picked = Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName;
            File.WriteAllText(Path.Combine(picked, "notes.txt"), "mine");

            using (AgentDialogs.Answering(Answers(new { Folder = picked, PlayInBrowser = false })))
                Wait(services.GetRequiredService<BuildWebCommand>().ExecuteAsync());

            var folder = Path.Combine(picked, project.Name + " (Web)");
            Assert.True(File.Exists(Path.Combine(folder, "index.html")));
            Assert.Equal("mine", File.ReadAllText(Path.Combine(picked, "notes.txt")));
            Assert.Equal(["show " + folder + ".zip"], programs.Calls);
            Assert.Null(services.GetRequiredService<WebPreview>().Url);
        });

    [Fact]
    public void BuildForWeb_SaysNetworkPlayIsntThere_ForAMultiplayerGame() => EditorHost.Run(
        s => s.AddSingleton<IExternalPrograms, RecordedPrograms>(),
        services =>
        {
            Open(services, ProjectTemplates.Get("Multiplayer"));

            // Asked whether to play it, it says first that network play won't be there.
            var asked = Assert.Throws<AgentInputNeededException>(() =>
            {
                using (AgentDialogs.Answering(Answers(new { Folder = Path.Combine(_root, "Arena") })))
                    Wait(services.GetRequiredService<BuildWebCommand>().ExecuteAsync());
            });

            Assert.Equal(BuildWebCommand.PlayQuestion, asked.Key);
            Assert.Contains("Network play isn't available in a browser", asked.Message, StringComparison.Ordinal);
        });

    [Fact]
    public void TheBuildMenu_HoldsEveryExport() => EditorHost.Run(services =>
    {
        var builds = services.GetRequiredService<CommandRegistry>().All.Where(c => c.Placement?.Menu == Menus.Build)
            .OrderBy(c => c.Placement!.Group).ThenBy(c => c.Placement!.Order).Select(c => c.DisplayText);

        Assert.Equal(["Build Game", "Build Small Game (Needs .NET)", "Build for Web..."], builds);
        Assert.Contains(services.GetServices<MenuDefinition>(), m => m.Id == Menus.Build && m.Text == "_Build");
    });

    private Project Open(IServiceProvider services, ProjectTemplate? template)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Web Game", template));
        return workspace.Project!;
    }

    private static JsonElement Answers(object answers) =>
        JsonDocument.Parse(JsonSerializer.Serialize(answers)).RootElement;

    private static T Wait<T>(Task<T> task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        return task.GetAwaiter().GetResult();
    }

    private static void Wait(Task task) => Wait(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default));

    private sealed class RecordedPrograms : IExternalPrograms
    {
        public List<string> Calls { get; } = [];

        public void Open(string path) => Calls.Add("open " + path);

        public void ShowInExplorer(string path) => Calls.Add("show " + path);

        public void OpenInBrowser(string url) => Calls.Add("browse " + url);
    }
}
