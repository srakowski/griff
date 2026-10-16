#if DEBUG
using System.IO;
using System.Numerics;
using System.Windows;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Modules.FirstPerson;
using Griff.Rendering;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Diagnostics;

// GRIFF_SNAPSHOT_ONLY=shooter: plays the Shooter template in first person and captures the game view on the GPU from a
// few places: the start room, down the hall with enemies in it, firing, and the shotgun in hand. Also the editor, showing
// the level as the top-down map it's painted as.
internal static partial class SnapshotRunner
{
    private static async Task ShooterSnapshots(IServiceProvider services, Window main, Workspace workspace, string projectDir, string outputDir)
    {
        var checks = new List<string>();
        await workspace.CreateProjectAsync(projectDir, "Snapshot Shooter", Griff.Projects.ProjectTemplates.Get("Shooter"));
        Render(main, Path.Combine(outputDir, "shooter-editor.png"));
        var play = services.GetRequiredService<PlayMode>();
        play.Play();
        await Task.Delay(700);
        var game = play.Game!;
        var player = game.Find("Player")!;

        void Capture(string name)
        {
            if (play.Surface.Capture(SceneRenderer.Build(game, play.Surface.Aspect)) is var (pixels, width, height))
                SaveRgba(pixels, width, height, Path.Combine(outputDir, name));
            else
                checks.Add($"gpu capture {name}: FAILED");
        }

        void Place(Vector2 at, float facing)
        {
            player.Transform.Position = new Vector3(at, 0);
            player.Transform.Rotation = new Vector3(0, 0, facing);
        }

        foreach (var chase in game.FindAll<Chase>())
            chase.Speed = 0;
        foreach (var shooter in game.FindAll<Shooter>())
            shooter.Running = false;

        Capture("shooter-start.png");
        Place(new Vector2(4.5f, -2.5f), -45);
        await Task.Delay(300);
        Capture("shooter-corner.png");

        game.Find("Hall Door")!.Transform.Position = new Vector3(7.5f, -2.5f, 0);
        Place(new Vector2(9, -3.5f), 0);
        await Task.Delay(300);
        Capture("shooter-hall.png");

        game.Fire(game.CurrentWeapon!);
        await Task.Delay(30);
        Capture("shooter-firing.png");

        Place(new Vector2(8.5f, -8.5f), 180);
        await Task.Delay(500);
        checks.Add($"shotgun taken out: {(game.CurrentWeapon?.Name == "Shotgun" ? "ok" : "FAILED " + game.CurrentWeapon?.Name)}");
        Place(new Vector2(14, -9.5f), 0);
        await Task.Delay(400);
        Capture("shooter-cave.png");

        play.Stop();
        File.WriteAllLines(Path.Combine(outputDir, "checks.txt"), checks);
        await workspace.SaveSceneAsync();
    }
}
#endif
