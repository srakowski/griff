#if DEBUG
using System.IO;
using System.Numerics;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Diagnostics;

// GRIFF_SNAPSHOT_ONLY=multiplayer: plays the Multiplayer template's arena with two players (Play with Players: the Game
// tab hosts, a second window joins), moves and shoots as each, and captures both players' screens on the GPU.
internal static partial class SnapshotRunner
{
    private static async Task MultiplayerSnapshots(IServiceProvider services, IWorkspace workspace, string projectDir, string outputDir)
    {
        var checks = new List<string>();
        await workspace.CreateProjectAsync(projectDir, "Snapshot Arena", Griff.Projects.ProjectTemplate.Multiplayer);
        Render(System.Windows.Application.Current.MainWindow!, Path.Combine(outputDir, "multiplayer-menu-editor.png"));
        await workspace.OpenSceneAsync(Path.Combine(workspace.Project!.AssetsPath, "Scenes", "Arena.scene"));
        var play = services.GetRequiredService<PlayMode>();
        play.Play(2);
        await Task.Delay(1500);
        var host = play.Game!;
        var guest = play.PlayerGame(2)!;
        checks.Add($"two players: {(host.Net.Players.Count == 2 && guest.Net.Players.Count == 2 ? "ok" : "FAILED")} ({host.Net.Players.Count} on the host, {guest.Net.Players.Count} on the guest)");

        void Capture(string name, int player)
        {
            var game = play.PlayerGame(player)!;
            var surface = play.PlayerSurface(player)!;
            if (surface.Capture(SceneRenderer.Build(game, surface.Aspect)) is var (pixels, width, height))
                SaveRgba(pixels, width, height, Path.Combine(outputDir, name));
            else
                checks.Add($"gpu capture {name}: FAILED");
        }

        Capture("multiplayer-host.png", 1);
        Capture("multiplayer-guest.png", 2);

        // The guest walks right and fires at the host; the host fires back.
        var guestKeys = (InputState)guest.Input;
        var hostKeys = (InputState)host.Input;
        var guestAvatar = guest.Net.LocalPlayer!.Avatar!;
        var start = guestAvatar.Transform.Position;
        guestKeys.SetKey(Key.A, true);
        await Task.Delay(700);
        guestKeys.SetKey(Key.A, false);
        var hostCopy = host.Scene.Find(guestAvatar.Id)!;
        await Task.Delay(300);
        checks.Add($"the guest moves on both screens: {(Vector3.Distance(start, guestAvatar.Transform.Position) > 2 && Vector3.Distance(hostCopy.Transform.Position, guestAvatar.Transform.Position) < 0.3f ? "ok" : "FAILED")}");

        var hostAvatar = host.Net.LocalPlayer!.Avatar!;
        var hostStart = hostAvatar.Transform.Position;
        hostKeys.SetKey(Key.S, true);
        await Task.Delay(700);
        hostKeys.SetKey(Key.S, false);
        await Task.Delay(400);
        var seenHost = guest.Net.Players.First(p => p.IsHost).Avatar;
        checks.Add($"the host moves on both screens: {(seenHost is not null && Vector3.Distance(hostStart, hostAvatar.Transform.Position) > 2 && Vector3.Distance(seenHost.Transform.Position, hostAvatar.Transform.Position) < 0.3f ? "ok" : "FAILED")} " +
                   $"(host {hostStart} -> {hostAvatar.Transform.Position}, seen {seenHost?.Transform.Position}, seen id {seenHost?.Id == hostAvatar.Id}, " +
                   $"guest scene has {guest.Scene.Entities.Count(e => e.Name == "Player")} players, traffic {guest.Net.Traffic})");
        guestKeys.MouseWorld = new Vector2(hostAvatar.Transform.Position.X, hostAvatar.Transform.Position.Y);
        guestKeys.IsMouseInView = true;
        guestKeys.SetMouseButton(MouseButton.Left, true);
        hostKeys.MouseWorld = new Vector2(hostCopy.Transform.Position.X, hostCopy.Transform.Position.Y);
        hostKeys.IsMouseInView = true;
        hostKeys.SetMouseButton(MouseButton.Left, true);
        await Task.Delay(260);
        Capture("multiplayer-host-firing.png", 1);
        Capture("multiplayer-guest-firing.png", 2);
        guestKeys.SetMouseButton(MouseButton.Left, false);
        hostKeys.SetMouseButton(MouseButton.Left, false);
        checks.Add($"shots on the guest's screen: {guest.FindAll("Shot").Count}");

        await Task.Delay(5000);
        Capture("multiplayer-slimes.png", 2);
        checks.Add($"slimes out: {host.FindAll("Slime").Count} on the host, {guest.FindAll("Slime").Count} on the guest");
        checks.Add($"scores: {string.Join(", ", host.Net.Players.Select(p => $"{p.Name} {p.Score}"))}");

        play.Stop();
        File.WriteAllLines(Path.Combine(outputDir, "checks.txt"), checks);
        await workspace.SaveSceneAsync();
    }
}
#endif
