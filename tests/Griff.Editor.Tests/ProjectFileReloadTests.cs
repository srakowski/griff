using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Controls;
using Griff.Editor.Features.Play;
using Griff.Input;
using Griff.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// project.griff changed on disk while the project is open (a text editor, a pull, an agent) is read again by itself: its
/// controls and step rate reach the Controls window, the names fields check against and a running game. The editor's own
/// saves, read back, change nothing, and a file that doesn't read leaves the settings as they were.
/// </summary>
public sealed class ProjectFileReloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private Workspace Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        return workspace;
    }

    // project.griff as a text editor would leave it: a Guard action on Q and E, and 30 steps a second.
    private static void EditOutside(Project project)
    {
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(project.ManifestPath))!.AsObject();
        manifest["controls"] = new System.Text.Json.Nodes.JsonObject { ["Guard"] = new System.Text.Json.Nodes.JsonArray("Q", "E") };
        manifest["stepRate"] = 30;
        File.WriteAllText(project.ManifestPath, manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void An_outside_change_is_noticed_and_reaches_the_running_game() => EditorHost.Run(services =>
    {
        var workspace = Open(services);
        var project = workspace.Project!;
        var play = services.GetRequiredService<PlayMode>();
        play.Play();
        Assert.ThrowsAny<ArgumentException>(() => play.Game!.HostInput.BindingsOf("Guard"));

        var changed = new TaskCompletionSource<ProjectManifest>();
        workspace.ProjectSettingsChanged += (_, before) => changed.TrySetResult(before);
        EditOutside(project);
        // The file is watched: nothing but the dispatcher going round reads it again.
        var before = EditorHost.Pump(changed.Task, TimeSpan.FromSeconds(30));

        Assert.Null(before.Controls);
        Assert.Equal(["Q", "E"], project.Controls["Guard"].Select(b => b.ToString()));
        Assert.Equal(30, project.StepRate);
        Assert.Equal(["Q", "E"], play.Game!.HostInput.BindingsOf("Guard").Select(b => b.ToString()));
        Assert.Equal(30, play.Game.Time.StepRate);
        Assert.Contains("Guard", workspace.Names!.Actions);
        play.Stop();
    });

    [Fact]
    public void The_Controls_window_shows_the_change_and_the_editors_own_saves_are_nothing_new() => EditorHost.Run(services =>
    {
        var workspace = Open(services);
        var project = workspace.Project!;
        var controls = new ControlsViewModel(workspace, services.GetRequiredService<PlayMode>());
        var told = 0;
        workspace.ProjectSettingsChanged += (_, _) => told++;

        // Saved by the editor (as the Controls window does), then read back: nothing changed.
        EditorHost.Pump(project.SetControlsAsync(new Dictionary<string, IReadOnlyList<InputBinding>> { ["Shield"] = [Key.Q] }));
        Assert.False(workspace.ReloadProjectSettings());
        Assert.Equal(0, told);

        EditOutside(project);
        Assert.True(workspace.ReloadProjectSettings());
        Assert.Equal(1, told);
        controls.Reload();
        Assert.Contains(controls.Actions, a => a.Name == "Guard");
        Assert.DoesNotContain(controls.Actions, a => a.Name == "Shield");

        // Half written, or broken: what was read stays.
        File.WriteAllText(project.ManifestPath, "{ \"name\": \"Game\", ");
        Assert.False(workspace.ReloadProjectSettings());
        Assert.Equal(1, told);
        Assert.Equal(["Q", "E"], project.Controls["Guard"].Select(b => b.ToString()));
    });
}
