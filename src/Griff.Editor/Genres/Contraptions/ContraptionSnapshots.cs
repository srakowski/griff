#if DEBUG
using System.IO;
using System.Numerics;
using System.Windows;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Modules.Contraptions;
using Griff.Rendering;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Griff.Editor.Diagnostics;
using static Griff.Editor.Diagnostics.SnapshotRunner;

namespace Griff.Editor.Genres.Contraptions;

// GRIFF_SNAPSHOT_ONLY=contraption: the Contraption template: the room, the lift going up, the spawn list, and the
// machine past the door.
internal sealed class ContraptionSnapshots : SnapshotGenre
{
    public override string Name => "contraption";

    public override async Task Run(IServiceProvider services, Window main, Workspace workspace, string projectDir, string outputDir)
    {
        var checks = new List<string>();
        await workspace.CreateProjectAsync(projectDir, "Snapshot Contraption", Griff.Templates.ProjectTemplates.Get("Contraption"));
        await Task.Delay(500);
        Render(main, Path.Combine(outputDir, "contraption-editor.png"));
        var play = services.GetRequiredService<PlayMode>();
        play.Play();
        await Task.Delay(900);
        var game = play.Game!;
        void Capture(string name)
        {
            if (play.Surface.Capture(SceneRenderer.Build(game, play.Surface.Aspect)) is var (pixels, width, height))
                SaveRgba(pixels, width, height, Path.Combine(outputDir, name));
            else
                checks.Add($"gpu capture {name}: FAILED");
        }

        Capture("contraption-start.png");
        game.Find("Button")!.GetComponent<WireButton>()!.IsOn = true;
        var plank = game.Find("Lift Plank")!;
        var start = plank.Transform.WorldPosition.Y;
        game.Find("Lift Button")!.GetComponent<WireButton>()!.IsOn = true;
        await Task.Delay(1200);
        checks.Add($"lift rises: {(plank.Transform.WorldPosition.Y > start + 2 ? "ok" : "FAILED")} ({start:0.0} -> {plank.Transform.WorldPosition.Y:0.0})");
        game.Tools!.Selected = 1;
        await Task.Delay(300);
        Capture("contraption-lift.png");

        // Over the plate goes the crate, and past the open door goes the player.
        game.Find("Crate")!.Transform.Position = new Vector3(3, -6.9f, 0);
        await Task.Delay(600);
        checks.Add($"door opens: {(!game.Find("Gate Wall")!.IsEnabled ? "ok" : "FAILED")}");
        game.Find("Player")!.Transform.Position = new Vector3(12, -6.6f, 0);
        game.Tools!.Selected = 5;
        await Task.Delay(1500);
        Capture("contraption-machine.png");

        play.Stop();
        File.WriteAllLines(Path.Combine(outputDir, "checks.txt"), checks);
        await workspace.SaveSceneAsync();
    }
}
#endif
