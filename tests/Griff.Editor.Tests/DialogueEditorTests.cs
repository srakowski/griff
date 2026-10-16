using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Story;
using Griff.Modules.VisualNovels;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Dialogue Editor and Create Stage: a stage made with its pictures in Assets, its dialogue's lines listed, a line's
/// speaker, expression, words and stage directions picked from lists, each an undo step, and the stage previewed at a line.
/// </summary>
public sealed class DialogueEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A new project with a stage made by Create Stage, selected.
    private (Workspace Workspace, DialogueEditor Editor, Entity Stage) Stage(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        EditorHost.Pump(services.GetRequiredService<CommandRegistry>().Get("Entity.CreateStage").ExecuteAsync());
        var stage = workspace.Scene!.Scene.Entities.Single(e => e.GetComponent<Stage>() is not null);
        return (workspace, services.GetRequiredService<DialogueEditor>(), stage);
    }

    [Fact]
    public void CreateStage_PutsItsPicturesInAssets_AndMakesAStageWithABoxAndCharacters_InOneUndoStep() => EditorHost.Run(services =>
    {
        var (workspace, _, stage) = Stage(services);

        Assert.True(File.Exists(Path.Combine(workspace.Project!.AssetsPath, "Stage", "park.png")));
        Assert.True(File.Exists(Path.Combine(workspace.Project.AssetsPath, "Stage", "ava-happy.png")));
        Assert.Equal(["Ava", "Ben"], stage.Children.Select(c => c.Name));
        Assert.NotNull(workspace.Scene!.Scene.FindNamed("Dialogue Box")?.GetComponent<DialogueBox>());
        Assert.Equal(DialogueStart.SceneStart, stage.GetComponent<Dialogue>()!.Start);

        workspace.Scene.History.Undo();
        Assert.DoesNotContain(workspace.Scene.Scene.Entities, e => e.GetComponent<Stage>() is not null || e.Name == "Dialogue Box");
    });

    [Fact]
    public void ListsTheLines_FollowingTheStageOrACharacterOnIt() => EditorHost.Run(services =>
    {
        var (_, editor, stage) = Stage(services);
        var selection = services.GetRequiredService<ISelection>();
        selection.Select(stage.Children[0]);

        Assert.Equal("Stage", editor.EntityName);
        Assert.Null(editor.Notice);
        Assert.Equal(StagePreset.Script.Count, editor.Lines.Count);
        Assert.Equal("Ava", editor.Lines[2].Who);
        Assert.Equal("Ava (happy)", editor.Lines[4].Who);
        Assert.Equal("Show Ava at Left, Happy", editor.Lines[2].Directions);
        Assert.Equal("choice", editor.Lines[7].Kind);
        Assert.Equal(0, editor.SelectedIndex);

        selection.Select(null);
        Assert.NotNull(editor.Notice);
    });

    [Fact]
    public void PickingTheSpeakerExpressionAndWords_RewritesTheLine_EachAnUndoStep() => EditorHost.Run(services =>
    {
        var (workspace, editor, stage) = Stage(services);
        services.GetRequiredService<ISelection>().Select(stage);
        var dialogue = stage.GetComponent<Dialogue>()!;
        editor.SelectLine(1);

        Assert.Equal(DialogueEditor.Nobody, editor.Speaker);
        Assert.Contains("Ava", editor.Speakers);
        editor.Speaker = "Ben";
        Assert.Equal(["(as is)", "Neutral", "Happy", "Surprised"], editor.Expressions);
        editor.Expression = "Surprised";
        editor.Said = "Is that... the park?";
        Assert.Equal("Ben (Surprised): Is that... the park?", dialogue.Lines[1]);

        workspace.Scene!.History.Undo();
        Assert.Equal("Ben (Surprised): It's a bright morning in the park.", dialogue.Lines[1]);
        workspace.Scene.History.Undo();
        workspace.Scene.History.Undo();
        Assert.Equal("It's a bright morning in the park.", dialogue.Lines[1]);
    });

    [Fact]
    public void DirectionsArePickedFromWhatTheStageHas_AndThePreviewShowsTheStageAtTheLine() => EditorHost.Run(services =>
    {
        var (workspace, editor, stage) = Stage(services);
        services.GetRequiredService<ISelection>().Select(stage);
        var dialogue = stage.GetComponent<Dialogue>()!;
        editor.SelectLine(1);

        Assert.Equal(["Ava", "Ben", "all"], editor.Characters);
        Assert.Equal(["", "Left", "Center", "Right"], editor.Positions);
        Assert.Equal(["Park", "Room", "Night"], editor.Backgrounds);
        Assert.Equal("Park. Nobody on stage. Says: It's a bright morning in the park.", editor.PreviewText);
        Assert.Empty(editor.PreviewFigures);
        Assert.NotNull(editor.PreviewBackground);

        editor.NewKind = "Show";
        editor.NewWho = "Ben";
        Assert.Equal(["", "Neutral", "Happy", "Surprised"], editor.NewExpressions);
        (editor.NewPosition, editor.NewExpression, editor.NewTransition) = ("Center", "Happy", "slide");
        editor.AddDirectionCommand.Execute(null);
        Assert.Equal("[show Ben Center Happy slide] It's a bright morning in the park.", dialogue.Lines[1]);
        Assert.Equal("Show Ben at Center, Happy (slide)", Assert.Single(editor.Directions).Description);
        var ben = Assert.Single(editor.PreviewFigures);
        Assert.Equal("Ben", ben.Name);
        Assert.NotNull(ben.Image);
        Assert.Equal(0, ben.Dim);

        Assert.Equal("[background Night fade]", editor.AddDirection("background", background: "Night", transition: "fade"));
        Assert.StartsWith("Night.", editor.PreviewText);

        editor.SelectLine(4);
        Assert.Equal("Night. Ava at Left, Happy, speaking; Ben at Right, Surprised, dimmed. Says: Ava: Ben! Come and sit with me.", editor.PreviewText);
        Assert.True(editor.PreviewFigures.Single(f => f.Name == "Ben").Dim > 0);

        editor.SelectLine(1);
        editor.RemoveDirection(0);
        Assert.Equal("[background Night fade] It's a bright morning in the park.", dialogue.Lines[1]);
        workspace.Scene!.History.Undo();
        Assert.Equal(2, editor.Directions.Count);
        Assert.Throws<ArgumentException>(() => editor.AddDirection("move", who: "Ava"));
    });

    [Fact]
    public void LinesAreAddedMovedAndRemoved() => EditorHost.Run(services =>
    {
        var (_, editor, stage) = Stage(services);
        services.GetRequiredService<ISelection>().Select(stage);
        var dialogue = stage.GetComponent<Dialogue>()!;
        editor.SelectLine(2);

        var added = editor.AddLine();
        Assert.Equal(3, added);
        Assert.Equal("Ava: ...", dialogue.Lines[3]);
        editor.MoveUpCommand.Execute(null);
        Assert.Equal("Ava: ...", dialogue.Lines[2]);
        Assert.Equal(2, editor.SelectedIndex);
        editor.RemoveLineCommand.Execute(null);
        Assert.Equal(StagePreset.Script, dialogue.Lines);
    });

    [Fact]
    public void AgentsReachTheEditor_AndTheEntityMenuOpensIt() => EditorHost.Run(services =>
    {
        var (_, editor, stage) = Stage(services);
        services.GetRequiredService<ISelection>().Select(stage);
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        Assert.Contains(surface.All(), r => r.Name == "DialogueEditor" && r.Target == editor);

        var command = services.GetRequiredService<CommandRegistry>().Get("Story.EditDialogue");
        Assert.True(command.CanExecute());
        Assert.Equal(new MenuPlacement(Menus.Entity, 1, 4), command.Placement);
    });

    [Fact]
    public void Close_IsReachableByAgentsThroughUiInvoke_AndDoesNothingWithoutAWindow() => EditorHost.Run(services =>
    {
        var editor = services.GetRequiredService<DialogueEditor>();
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        Assert.Contains(surface.All(), r => r.Name == "DialogueEditor" && r.Target == editor);
        Assert.Contains(typeof(DialogueEditor).GetMethods(), m => m.Name == nameof(DialogueEditor.Close) && m.GetParameters().Length == 0);

        Assert.Null(editor.Window);
        editor.Close();
    });
}
