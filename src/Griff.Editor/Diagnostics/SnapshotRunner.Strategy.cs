#if DEBUG
using System.IO;
using System.Numerics;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Modules.Strategy;
using Griff.Rendering;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Diagnostics;

// GRIFF_SNAPSHOT_ONLY=strategy: plays the Strategy template for a while, ordering units about through the game's own
// API, and captures the game view on the GPU at a few points: the HUD and selection, the economy going, a battle.
internal static partial class SnapshotRunner
{
    private static async Task StrategySnapshots(IServiceProvider services, Workspace workspace, string projectDir, string outputDir)
    {
        var checks = new List<string>();
        await workspace.CreateProjectAsync(projectDir, "Snapshot Strategy", Griff.Projects.ProjectTemplates.Get("Strategy"));
        var play = services.GetRequiredService<PlayMode>();
        play.Play();
        await Task.Delay(700);
        var game = play.Game!;

        void Capture(string name)
        {
            if (play.Surface.Capture(SceneRenderer.Build(game, play.Surface.Aspect)) is var (pixels, width, height))
                SaveRgba(pixels, width, height, Path.Combine(outputDir, name));
            else
                checks.Add($"gpu capture {name}: FAILED");
        }

        List<Entity> Mine<T>() where T : Component =>
            game.FindAll<T>().Select(c => c.Entity!).Where(e => e.GetComponent<Unit>()?.Team == "Player").ToList();

        var workers = Mine<Worker>();
        game.Select(workers);
        await Task.Delay(300);
        Capture("strategy-start.png");

        var mine = game.FindAll("Gold Mine").OrderBy(m => m.Transform.Position.X).First();
        foreach (var worker in workers.Take(4))
            worker.GetComponent<Worker>()!.Gather(mine);
        var tree = game.FindAll("Tree").OrderBy(t => Vector2.Distance(new Vector2(t.Transform.Position.X, t.Transform.Position.Y), new Vector2(-18, -8))).First();
        workers[4].GetComponent<Worker>()!.Gather(tree);
        var hall = game.FindAll("Town Hall").First(h => h.GetComponent<Building>()!.Team == "Player");
        game.Train(hall.GetComponent<Producer>()!, "Worker");
        game.Train(hall.GetComponent<Producer>()!, "Worker");
        var gold = game.FactionOf("Player")!.Amount("Gold");
        game.Select([hall]);
        await Task.Delay(12000);
        Capture("strategy-economy.png");
        checks.Add($"workers bring gold back: {(game.FactionOf("Player")!.Amount("Gold") > gold ? "ok" : "FAILED")} ({gold} -> {game.FactionOf("Player")!.Amount("Gold")})");

        var army = Mine<Fighter>();
        game.Select(army);
        foreach (var unit in army)
            unit.GetComponent<Unit>()!.AttackMoveTo(new Vector2(12, 6));
        await Task.Delay(9000);
        game.LookAt(new Vector2(10, 5));
        await Task.Delay(1500);
        Capture("strategy-battle.png");
        checks.Add($"red trained or fought: {(game.FindAll<Unit>().Count(u => u.Team == "Enemy") != 7 ? "ok" : "unchanged")}");

        play.Stop();
        File.WriteAllLines(Path.Combine(outputDir, "checks.txt"), checks);
        await workspace.SaveSceneAsync();
    }
}
#endif
