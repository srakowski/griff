using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Opening a project whose start scene has unsaved changes kept from when the editor closed without saving: the question
/// goes through the dialogs (an agent answers it with "answers"), and is asked before anything changes, so the editor is
/// never left with the project open and no scene; nor when the start scene can't be read. A question showing the user
/// is answered by an agent's call that comes in meanwhile.
/// </summary>
public sealed class RecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Wait(Task task) => EditorHost.Pump(task);

    private static JsonElement Answers(string json) => JsonDocument.Parse(json).RootElement;

    // Two projects, First and Second, with First open; Second's start scene has unsaved changes (an entity called Unsaved)
    // in a recovery copy newer than it.
    private (IWorkspace Workspace, Project First, string Second) TwoProjects(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Second"));
        var second = workspace.Project!;
        KeepUnsaved(workspace, second);
        Wait(workspace.CreateProjectAsync(_root, "First"));
        return (workspace, workspace.Project!, second.RootPath);
    }

    private static void KeepUnsaved(IWorkspace workspace, Project project)
    {
        var path = project.StartScenePath!;
        var scene = workspace.Serializer.LoadFromString(File.ReadAllText(path));
        scene.Add(new Entity("Unsaved"));
        RecoveryFiles.Write(project, path, workspace.Serializer.SaveToString(scene));
        File.SetLastWriteTimeUtc(RecoveryFiles.PathFor(project, path), DateTime.UtcNow.AddMinutes(1));
    }

    private static bool Shows(IWorkspace workspace, string name) => workspace.Scene!.Scene.Entities.Any(e => e.Name == name);

    [Fact]
    public void Reopening_a_project_with_unsaved_changes_asks_before_anything_changes() => EditorHost.Run(services =>
    {
        var (workspace, first, second) = TwoProjects(services);
        var firstScene = workspace.Scene;

        // Not answered: the call comes back asking, and the editor is as it was.
        using (AgentDialogs.Answering(null))
        {
            var asked = Assert.Throws<AgentInputNeededException>(() => Wait(workspace.OpenProjectAsync(second)));
            Assert.Equal(Workspace.RecoverTitle, asked.Key);
        }

        Assert.Same(first, workspace.Project);
        Assert.Same(firstScene, workspace.Scene);

        // Recovered: the scene opens with the unsaved changes, still unsaved.
        using (AgentDialogs.Answering(Answers("""{"Recover unsaved changes?": true}""")))
            Wait(workspace.OpenProjectAsync(second));
        Assert.Equal(second, workspace.Project!.RootPath);
        Assert.True(Shows(workspace, "Unsaved"));
        Assert.True(workspace.Scene!.IsDirty);

        // Not recovered: the scene as it was saved, and the copy goes.
        var secondProject = workspace.Project;
        using (AgentDialogs.Answering(Answers("""{"Save changes": "Discard"}""")))
            Wait(workspace.OpenProjectAsync(first.RootPath));
        KeepUnsaved(workspace, secondProject);
        using (AgentDialogs.Answering(Answers("""{"Recover unsaved changes?": false}""")))
            Wait(workspace.OpenProjectAsync(second));
        Assert.NotNull(workspace.Scene);
        Assert.False(Shows(workspace, "Unsaved"));
        Assert.False(workspace.Scene!.IsDirty);
        Assert.False(File.Exists(RecoveryFiles.PathFor(secondProject, secondProject.StartScenePath!)));
    });

    [Fact]
    public void A_project_whose_start_scene_cant_be_read_opens_with_another_scene() => EditorHost.Run(services =>
    {
        var (workspace, first, second) = TwoProjects(services);
        var level = first.CreateScene("Level 2");
        File.WriteAllText(first.StartScenePath!, "{ not a scene");

        using (AgentDialogs.Answering(null))
        {
            Wait(workspace.OpenProjectAsync(first.RootPath));
            Assert.Contains(AgentDialogs.Reported, error => error.StartsWith("Couldn't open the start scene", StringComparison.Ordinal));
        }

        Assert.Equal(first.RootPath, workspace.Project!.RootPath);
        Assert.Equal(Path.GetFullPath(level), Path.GetFullPath(workspace.Scene!.FilePath));

        // With no scene that can be read, a new one beside them (the unreadable one is left as it is).
        File.WriteAllText(level, "{ not a scene either");
        using (AgentDialogs.Answering(null))
            Wait(workspace.OpenProjectAsync(first.RootPath));
        Assert.NotNull(workspace.Scene);
        Assert.Empty(workspace.Scene!.Scene.Entities);
        Assert.Equal("{ not a scene", File.ReadAllText(first.StartScenePath!));
    });

    [Fact]
    public void A_question_showing_the_user_is_answered_by_an_agents_call_that_comes_in_meanwhile()
    {
        var shown = new ShowingDialogs();
        EditorHost.Run(services => services.AddSingleton<IDialogService>(_ => new AgentDialogs(shown)), services =>
        {
            var dialogs = (AgentDialogs)services.GetRequiredService<IDialogService>();
            Assert.Null(dialogs.AnswerShowing(null));

            // Asked outside any agent's call (as when the editor reopens the last project as it starts), two calls come in
            // while it shows: one not answering it is told what it asks; the next answers it.
            (bool Failed, string Text)? told = null, answered = null;
            shown.WhileShowing = () =>
            {
                Assert.Equal($"{Workspace.RecoverTitle}: Level has unsaved changes.", dialogs.Showing);
                var first = dialogs.AnswerShowing(null)!;
                told = (first.IsError, first.Text);
                var second = dialogs.AnswerShowing(Answers("""{"Recover unsaved changes?": true}"""))!;
                answered = (second.IsError, second.Text);
            };

            Assert.True(dialogs.Confirm(Workspace.RecoverTitle, "Level has unsaved changes."));
            Assert.True(told!.Value.Failed);
            Assert.Contains("\"answers\": {\"Recover unsaved changes?\": <true or false>}", told.Value.Text);
            Assert.False(answered!.Value.Failed);
            Assert.Null(dialogs.Showing);

            // A question an agent can't answer for the user (a file to pick) says so.
            shown.WhileShowing = () => Assert.Contains("answered in the editor", dialogs.AnswerShowing(Answers("""{"Open": "x"}"""))!.Text);
            Assert.Null(dialogs.ShowOpenFileDialog("Text|*.txt"));
        });
    }

    // Dialogs that wait for an answer in a loop of their own, as the real ones do, running WhileShowing in it.
    private sealed class ShowingDialogs : IDialogService, IAnswerableDialogs
    {
        private DispatcherFrame? _frame;
        private MessageBoxResult _answer;

        public Action? WhileShowing { get; set; }

        private MessageBoxResult Show()
        {
            (_frame, _answer) = (new DispatcherFrame(), MessageBoxResult.None);
            Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                WhileShowing?.Invoke();
                if (_frame is { } open)
                    open.Continue = false;
            });
            Dispatcher.PushFrame(_frame);
            _frame = null;
            return _answer;
        }

        public bool AnswerShowing(MessageBoxResult answer)
        {
            if (_frame is not { } frame)
                return false;
            (_answer, frame.Continue) = (answer, false);
            return true;
        }

        public bool Confirm(string title, string message) => Show() == MessageBoxResult.Yes;

        public SaveChangesChoice AskSaveChanges(string documentName) => Show() switch
        {
            MessageBoxResult.Yes => SaveChangesChoice.Save,
            MessageBoxResult.No => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };

        public string? ShowOpenFileDialog(string filter)
        {
            Show();
            return null;
        }

        public string? ShowSaveFileDialog(string filter, string defaultFileName) => ShowOpenFileDialog(filter);

        public string? ShowFolderDialog(string? initialDirectory) => ShowOpenFileDialog("");

        public string? Prompt(string title, string label, string initial, Func<string, string?> validate) => ShowOpenFileDialog("");

        public void ShowError(string title, string message)
        {
        }
    }
}
