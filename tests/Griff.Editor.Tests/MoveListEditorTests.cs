using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Data;
using Griff.Editor.Features.Fighting;
using Griff.Modules.Fighting;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Move List and Create Training Pair: a pair made with its sheet in Assets, the fighter's moves listed with their
/// inputs as arrows and their frame data, a move's motion, button, clip, frames, height and cancels picked or typed, each an
/// undo step, moves added, moved and removed, and moved into a table.
/// </summary>
public sealed class MoveListEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A new project with a training pair, its fighter selected.
    private (IWorkspace Workspace, MoveListEditor Editor, Entity Fighter) Training(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        EditorHost.Pump(services.GetRequiredService<CommandRegistry>().Get("Entity.CreateTrainingPair").ExecuteAsync());
        var fighter = workspace.Scene!.Scene.Entities.Single(e => e.Name == "Fighter");
        services.GetRequiredService<ISelection>().Select(fighter);
        return (workspace, services.GetRequiredService<MoveListEditor>(), fighter);
    }

    [Fact]
    public void CreateTrainingPair_PutsItsSheetInAssets_AndMakesTwoFighters_InOneUndoStep() => EditorHost.Run(services =>
    {
        var (workspace, _, fighter) = Training(services);
        Assert.True(File.Exists(Path.Combine(workspace.Project!.AssetsPath, "Fighting", "fighter.png")));
        Assert.Equal(TrainingPreset.FighterMoves, fighter.GetComponent<FightingCharacter>()!.Moves);
        Assert.False(workspace.Scene!.Scene.FindNamed("Dummy")!.GetComponent<FightingCharacter>()!.Controllable);

        workspace.Scene.History.Undo();
        Assert.DoesNotContain(workspace.Scene.Scene.Entities, e => e.GetComponent<FightingCharacter>() is not null);
    });

    [Fact]
    public void ListsTheMoves_WithTheirInputsAsArrows_AndTheirFrameData() => EditorHost.Run(services =>
    {
        var (_, editor, _) = Training(services);
        Assert.Equal("Fighter", editor.EntityName);
        Assert.Null(editor.Notice);
        Assert.Equal(["Jab", "Kick", "Low Kick", "Air Kick", "Uppercut", "Palm", "Rush"], editor.Moves.Select(m => m.Name));
        Assert.Equal("↓↘→ P", editor.Moves[5].Arrows);
        Assert.Equal("quarter circle forward + P", editor.Moves[5].Description);
        Assert.Equal("5/3/14 · low", editor.Moves[2].Frames);
        Assert.Equal(["P", "K"], editor.Buttons);

        editor.SelectMoveNamed("Jab");
        Assert.Equal("• P", editor.InputArrows);
        Assert.Equal("Startup 3 · Active 2 · Recovery 7 · 12 in all", editor.FrameSummary);
        // Its 14 steps of stun outlast the 8 it takes to recover after hitting.
        Assert.Equal("+6 on hit, +1 on block · 2 damage", editor.Advantage);
        Assert.Equal(12, editor.FrameCells.Count);
        Assert.Equal(["Startup", "Startup", "Startup", "Active", "Active"], editor.FrameCells.Take(5).Select(c => c.Phase));
        Assert.False(editor.HasProblems);

        services.GetRequiredService<ISelection>().Select(null);
        Assert.NotNull(editor.Notice);
    });

    [Fact]
    public void AMovesMotionButtonClipAndFrames_ArePickedOrTyped_EachAnUndoStep() => EditorHost.Run(services =>
    {
        var (workspace, editor, fighter) = Training(services);
        var moves = fighter.GetComponent<FightingCharacter>()!;
        editor.SelectMoveNamed("Kick");

        editor.MoveMotion = "214";
        Assert.Equal("Kick: 214K frames 6/3/14 cancels special", moves.Moves[1]);
        Assert.Equal("↓↙← K", editor.InputArrows);
        Assert.Equal("quarter circle back + K", editor.InputDescription);
        editor.MoveButton = "P";
        editor.MoveAir = true;
        Assert.Equal("j.214P", editor.MoveInput);
        editor.MoveInput = "[2]8K";
        Assert.Equal("charge down, up + K", editor.InputDescription);
        Assert.Throws<ArgumentException>(() => editor.MoveInput = "28");

        editor.MoveClip = "Uppercut";
        (editor.MoveStartup, editor.MoveActive, editor.MoveRecovery) = (4, 6, 20);
        editor.MoveHits = "High";
        editor.MoveChip = "0.3";
        editor.MovePush = "1, 7";
        editor.MoveCancels = "Palm, Rush";
        editor.MoveName = "Flash Kick";
        Assert.Equal("Flash Kick: [2]8K clip Uppercut frames 4/6/20 high chip 0.3 push 1,7 cancels Palm, Rush", moves.Moves[1]);
        Assert.Equal("Startup 4 · Active 6 · Recovery 20 · 30 in all", editor.FrameSummary);

        editor.MoveCancels = "Teleport";
        Assert.Contains("Teleport, which isn't one of its moves", editor.Problems);
        editor.MoveClip = "";
        Assert.Equal("FlashKick", editor.MoveClip);
        Assert.Contains("no clip called FlashKick", editor.Problems);

        for (var i = 0; i < 15; i++)
            workspace.Scene!.History.Undo();
        Assert.Equal(TrainingPreset.FighterMoves, moves.Moves);
    });

    [Fact]
    public void MovesAreAddedMovedRemoved_AndMadeIntoATable() => EditorHost.Run(services =>
    {
        var (workspace, editor, fighter) = Training(services);
        var moves = fighter.GetComponent<FightingCharacter>()!;
        editor.SelectMove(0);
        Assert.Equal(1, editor.AddMove());
        Assert.Equal("New Move: 5P", moves.Moves[1]);
        Assert.Equal(2, editor.AddMove("Fireball: 236P frames 12/4/30"));
        editor.MoveMove(2, -1);
        Assert.Equal("Fireball: 236P frames 12/4/30", moves.Moves[1]);
        editor.RemoveMove(2);
        Assert.Equal(8, moves.Moves.Count);
        Assert.Throws<ArgumentException>(() => editor.AddMove("Oops 5P"));

        var table = editor.MakeTable();
        Assert.Equal("Fighter Moves", table);
        Assert.Empty(moves.Moves);
        Assert.Equal("Fighter Moves", moves.MovesTable);
        Assert.Equal(8, editor.Moves.Count);
        Assert.True(editor.Moves.All(m => m.FromTable));
        editor.SelectMoveNamed("Uppercut");
        Assert.True(editor.IsTableMove);
        Assert.Equal("→↓↘ P", editor.InputArrows);
        var stored = services.GetRequiredService<TableStore>().Find(table)!;
        Assert.Equal("623P", stored["Uppercut"]["Input"].ToString());
        Assert.True(FighterMove.TryFromRow(stored["Low Kick"], out var lowKick, out _));
        Assert.Equal(MoveHeight.Low, lowKick.Hits);

        workspace.Scene!.History.Undo();
        Assert.Equal(8, moves.Moves.Count);
        Assert.Null(moves.MovesTable);
    });
}
