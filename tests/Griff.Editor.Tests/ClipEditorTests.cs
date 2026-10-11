using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Griff.Art;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Art;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>The Clip Editor: an animation's clips listed and previewed playing, and added, renamed and removed with undo.</summary>
public sealed class ClipEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A hero on a 12-frame sheet (4 across, 3 down) with three clips, starting with Idle, and a crate with just a sprite.
    private static (SceneDocument Document, ISelection Selection, ClipEditor Clips) Open(IServiceProvider services, string? start = "Idle")
    {
        var scene = new Scene();
        var hero = new Entity("Hero");
        hero.AddComponent(new Sprite { Columns = 4, Rows = 3 });
        hero.AddComponent(new Animation { FramesPerSecond = 10, Clips = "Idle 0-1, Run 2-5 @12, Die 6-7 once", Clip = start });
        scene.Add(hero);
        var crate = new Entity("Crate");
        crate.AddComponent(new Sprite { Columns = 2 });
        scene.Add(crate);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        var selection = services.GetRequiredService<ISelection>();
        selection.Select(hero);
        return (document, selection, services.GetRequiredService<ClipEditor>());
    }

    private static Animation AnimationOf(SceneDocument document) => document.Scene.Entities.Single(e => e.Name == "Hero").GetComponent<Animation>()!;

    [Fact]
    public void ListsTheSelectedEntitysClips_PreviewingTheOneTheGameStartsWith() => EditorHost.Run(services =>
    {
        var (_, _, clips) = Open(services);

        Assert.Equal("Hero", clips.EntityName);
        Assert.Equal(["Idle", "Run", "Die"], clips.Clips.Select(c => c.Name));
        Assert.Equal("Idle", clips.Selected!.Name);
        Assert.True(clips.Selected.IsStart);
        Assert.Equal("frames 2–5 · 12 fps · loops", clips.Clips[1].Summary);
        Assert.Equal("frames 6–7 · 10 fps · once", clips.Clips[2].Summary);
        Assert.Equal([0, 1], clips.Frames.Select(f => f.SheetFrame));
        Assert.True(clips.IsPlaying);
    });

    [Fact]
    public void WithoutAStartingClip_ListsTheAnimationsOwnFramesFirst() => EditorHost.Run(services =>
    {
        var (_, _, clips) = Open(services, start: null);

        Assert.Equal([ClipEditor.OwnFrames, "Idle", "Run", "Die"], clips.Clips.Select(c => c.Name));
        Assert.True(clips.Selected!.IsOwnFrames);
        Assert.Equal("frames 0–3 · 10 fps · loops", clips.Selected.Summary);
        Assert.False(clips.RenameClipCommand.CanExecute(null));
        Assert.False(clips.RemoveClipCommand.CanExecute(null));
    });

    [Fact]
    public void ThePreviewPlaysTheClipAsTheGameDoes() => EditorHost.Run(services =>
    {
        var (_, _, clips) = Open(services);
        clips.SelectClip("Run");

        clips.Tick(0.1);
        Assert.Equal(1, clips.Frame);
        Assert.Equal(3, clips.SheetFrame);
        Assert.Equal("Frame 2 of 4 (sheet frame 3)", clips.FrameText);
        Assert.True(clips.Frames[1].IsCurrent);
        clips.Tick(0.25);
        Assert.Equal(0, clips.Frame);

        // One that plays once stops on its last frame; playing it again starts over.
        clips.SelectClip("Die");
        clips.Tick(5);
        Assert.False(clips.IsPlaying);
        Assert.Equal(1, clips.Frame);
        clips.Play();
        Assert.True(clips.IsPlaying);
        Assert.Equal(0, clips.Frame);
    });

    [Fact]
    public void SteppingPausesAndGoesRound() => EditorHost.Run(services =>
    {
        var (_, _, clips) = Open(services);
        clips.SelectClip("Run");

        clips.Step(-1);
        Assert.False(clips.IsPlaying);
        Assert.Equal(3, clips.Frame);
        clips.Step(1);
        Assert.Equal(0, clips.Frame);
        clips.ShowFrameCommand.Execute(clips.Frames[2]);
        Assert.Equal(2, clips.Frame);
        Assert.Equal(4, clips.SheetFrame);

        // Paused stays paused, on that frame.
        clips.Tick(1);
        Assert.Equal(2, clips.Frame);
    });

    [Fact]
    public void FollowsTheSelection() => EditorHost.Run(services =>
    {
        var (document, selection, clips) = Open(services);

        selection.Select(document.Scene.Entities.Single(e => e.Name == "Crate"));
        Assert.Empty(clips.Clips);
        Assert.True(clips.CanAddAnimation);
        Assert.Equal("Crate has no Animation. Add one to play frames of its sprite sheet.", clips.Notice);

        // Adding one plays every frame of the sheet, as one undo step.
        clips.AddAnimation();
        var crate = document.Scene.Entities.Single(e => e.Name == "Crate");
        Assert.Equal(2, crate.GetComponent<Animation>()!.FrameCount);
        Assert.Equal([ClipEditor.OwnFrames], clips.Clips.Select(c => c.Name));
        Assert.False(clips.CanAddAnimation);
        document.History.Undo();
        Assert.Null(crate.GetComponent<Animation>());
        Assert.True(clips.CanAddAnimation);

        selection.Select(null);
        Assert.Equal("Select something with an Animation to see its clips.", clips.Notice);
        Assert.Null(clips.Selected);
    });

    [Fact]
    public void AddsRenamesAndRemovesClips_EachAsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, _, clips) = Open(services);
        var animation = AnimationOf(document);

        clips.AddClip("Jump");
        Assert.Equal("Idle 0-1, Run 2-5 @12, Die 6-7 once, Jump 8", animation.Clips);
        Assert.Equal("Jump", clips.Selected!.Name);
        Assert.Equal("Add Clip Jump", document.History.UndoDescription);

        // Renaming the one the game starts with renames that too.
        clips.SelectClip("Idle");
        clips.RenameClip("Stand");
        Assert.Equal("Stand 0-1, Run 2-5 @12, Die 6-7 once, Jump 8", animation.Clips);
        Assert.Equal("Stand", animation.Clip);
        Assert.Equal("Stand", clips.Selected!.Name);

        clips.RemoveClip();
        Assert.Equal("Run 2-5 @12, Die 6-7 once, Jump 8", animation.Clips);
        Assert.Null(animation.Clip);
        Assert.Equal(ClipEditor.OwnFrames, clips.Clips[0].Name);

        clips.SelectClip("Die");
        clips.StartWithClip();
        Assert.Equal("Die", animation.Clip);
        Assert.Equal("Start With Die", document.History.UndoDescription);

        // Each undoes in one step, and the list follows.
        document.History.Undo();
        document.History.Undo();
        Assert.Equal("Stand", animation.Clip);
        Assert.Equal(["Stand", "Run", "Die", "Jump"], clips.Clips.Select(c => c.Name));
        document.History.Undo();
        document.History.Undo();
        Assert.Equal("Idle 0-1, Run 2-5 @12, Die 6-7 once", animation.Clips);
        Assert.Equal("Idle", animation.Clip);
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void TheFirstClipTakesTheAnimationsOwnFrames_AndStartsTheGame() => EditorHost.Run(services =>
    {
        var (document, _, clips) = Open(services, start: null);
        var animation = AnimationOf(document);
        animation.Entity!.GetComponent<Sprite>()!.Rows = 2;
        document.Execute(new SetPropertyEdit(animation, services.GetRequiredService<IWorkspace>().Components.Get(typeof(Animation)).Properties
            .Single(p => p.Name == nameof(Animation.Clips)), null));
        animation.FirstFrame = 4;
        animation.FrameCount = 3;

        clips.AddClip("Idle");
        Assert.Equal("Idle 4-6", animation.Clips);
        Assert.Equal("Idle", animation.Clip);
        Assert.Equal(["Idle"], clips.Clips.Select(c => c.Name));

        // A clip past the end of the sheet goes back to its first frame.
        clips.AddClip("Run");
        clips.AddClip("Jump");
        Assert.Equal("Idle 4-6, Run 7, Jump 0", animation.Clips);
    });

    [Fact]
    public void ClipNamesAreOneWordAndNotTaken_AndAgentsAnswerThePrompt() => EditorHost.Run(services =>
    {
        var (document, _, clips) = Open(services);

        Assert.Equal("There's already a clip called run.", clips.NameProblem("run", null));
        Assert.Null(clips.NameProblem("run", "Run"));
        Assert.Equal("A clip's name is one word, with no spaces, commas or colons.", clips.NameProblem("Big Jump", null));
        Assert.Equal("A clip needs a name.", clips.NameProblem(" ", null));
        Assert.Throws<InvalidOperationException>(() => clips.AddClip("Idle"));
        Assert.False(document.History.CanUndo);

        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Clip name": "Hurt"}""").RootElement))
            clips.AddClipCommand.Execute(null);
        Assert.Equal("Hurt", clips.Selected!.Name);
    });

    [Fact]
    public void ThePreviewShowsFramesCutFromTheSpriteSheet() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));

        // Four 16-pixel frames, each a different shade of red.
        var sheet = new PixelSheet(SheetLayout.Sprite(16, 4));
        for (var frame = 0; frame < 4; frame++)
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                    sheet.Set(frame, x, y, PixelSheet.Pack((byte)(60 * (frame + 1)), 0, 0, 255));
        services.GetRequiredService<EditorArt>().Save(sheet, "Sprites/hero.png");

        var hero = new Entity("Hero");
        hero.AddComponent(new Sprite { Image = "Sprites/hero.png", Columns = 4 });
        hero.AddComponent(new Animation { Clips = "Idle 0-1, Run 2-3" });
        workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, hero));
        services.GetRequiredService<ISelection>().Select(hero);
        var clips = services.GetRequiredService<ClipEditor>();

        Assert.Null(clips.Notice);
        clips.SelectClip("Run");
        clips.ShowFrame(1);
        var image = (BitmapSource)clips.FrameImage!;
        Assert.Equal(16, image.PixelWidth);
        var pixel = new byte[4];
        image.CopyPixels(new System.Windows.Int32Rect(8, 8, 1, 1), pixel, 4, 0);
        Assert.Equal(240, pixel[2]);
        Assert.Equal(2, clips.Frames.Count);
        Assert.NotNull(clips.Frames[0].Image);
    });

    [Fact]
    public void TheArtMenuOpensIt_AndAgentsReachIt() => EditorHost.Run(services =>
    {
        var command = services.GetRequiredService<CommandRegistry>().Get("Art.Clips");
        Assert.Equal("Animation _Clips...", command.Text);
        Assert.Contains(services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single().All(), root => root.Name == "ClipEditor");
    });

    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
