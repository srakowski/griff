#if DEBUG
using System.IO;
using System.Numerics;
using System.Windows;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Modules.BlockWorlds;
using Griff.Rendering;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Griff.Editor.Diagnostics;
using static Griff.Editor.Diagnostics.SnapshotRunner;

namespace Griff.Editor.Genres.BlockWorlds;

// GRIFF_SNAPSHOT_ONLY=sandbox: the Sandbox template in the editor and playing: the world, the hotbar and crafting list,
// and a cave lit by torches.
internal sealed class SandboxSnapshots : SnapshotGenre
{
    public override string Name => "sandbox";

    public override async Task Run(IServiceProvider services, Window main, Workspace workspace, string projectDir, string outputDir)
    {
        var checks = new List<string>();
        await workspace.CreateProjectAsync(projectDir, "Snapshot Sandbox", Griff.Templates.ProjectTemplates.Get("Sandbox"));
        await Task.Delay(500);
        Render(main, Path.Combine(outputDir, "sandbox-editor.png"));
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

        var player = game.Find("Player")!;
        checks.Add($"player on the ground: {(player.GetComponent<Body>()!.IsGrounded ? "ok" : "FAILED")}");
        Capture("sandbox-start.png");

        var inventory = player.GetComponent<Inventory>()!;
        foreach (var (item, count) in new[] { ("Dirt", 24), ("Wood", 6), ("Coal", 3), ("Stone", 12), ("Sand", 4) })
            inventory.Add(item, count);
        player.GetComponent<Crafting>()!.IsOpen = true;
        await Task.Delay(400);
        Capture("sandbox-crafting.png");
        player.GetComponent<Crafting>()!.IsOpen = false;

        // Down into the biggest cave near the middle, with torches around.
        var map = game.Find("World")!.GetComponent<Tilemap>()!;
        var cave = Enumerable.Range(40, 25).SelectMany(y => Enumerable.Range(50, 60).Select(x => (X: x, Y: y)))
            .Where(c => map.GetTile(c.X, c.Y) < 0 && map.GetTile(c.X, c.Y - 1) < 0 && map.IsBlocked(c.X, c.Y + 1))
            .OrderBy(c => Math.Abs(c.X - 80)).FirstOrDefault();
        checks.Add($"a cave underground: {(cave != default ? $"ok ({cave.X}, {cave.Y})" : "FAILED")}");
        var floor = Vector2.Transform(map.CellCenter(cave.X, cave.Y), map.Entity!.Transform.WorldMatrix);
        player.Transform.Position = new Vector3(floor.X, floor.Y + 0.45f, 0);
        foreach (var dx in new[] { -4, 3 })
        {
            var (x, y) = (cave.X + dx, cave.Y - 1);
            if (map.GetTile(x, y) < 0)
                map.SetTile(x, y, map.Entity!.GetComponent<Blocks>()!.TileOf("Torch"));
        }

        await Task.Delay(1500);
        Capture("sandbox-cave.png");

        play.Stop();
        File.WriteAllLines(Path.Combine(outputDir, "checks.txt"), checks);
        await workspace.SaveSceneAsync();
    }
}
#endif
