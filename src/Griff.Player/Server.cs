using Griff.Hosting;
using Griff.Runtime;

namespace Griff.Player;

/// <summary>
/// A dedicated server (<c>--server [port]</c>): the game's start scene, hosted for everyone and played by nobody, in a
/// console with no window and no WPF, until it's closed (or Ctrl+C). The core's <see cref="GameLoop"/> plays it with nothing
/// to draw on, a frame about every 120th of a second. With <c>GRIFF_PLAYER_SMOKE</c> set it plays for a second on this
/// machine only and says what it did.
/// </summary>
internal static class Server
{
    private static readonly TimeSpan FrameEvery = TimeSpan.FromSeconds(1.0 / 120);

    public static int Run(PlayerOptions options, int port)
    {
        // Started from a console, it writes there (a test reading its output has it already).
        if (!Console.IsOutputRedirected)
            Program.AttachConsole(-1);
        var smoke = Program.Smoke is not null;
        var folder = "";
        LoadedGame loaded;
        try
        {
            folder = Program.GameFolder(options);
            loaded = GameLoader.LoadAsync(folder).GetAwaiter().GetResult();
        }
#pragma warning disable CA1031 // Whatever stops the game loading is said, and the server ends.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine($"Couldn't load the game from \"{folder}\": {ex.Message}");
            return smoke ? 2 : 1;
        }

        var title = loaded.Project!.Name;
        var game = loaded.NewGame(new GameHostOptions
        {
            // A smoke test's server stays on this machine and keeps its saves to itself.
            SaveData = smoke ? null : new SaveData(SaveData.PlayerPath(title + " Server")),
            OverNetwork = !smoke,
            Problems = p => Console.Error.WriteLine(p.Message),
        });
        if (!game.Network.HostServer(port))
        {
            Console.Error.WriteLine(game.Network.Status);
            game.Stop();
            return 1;
        }

        Console.WriteLine($"{title}: {game.Network.Status}. Close this window (or press Ctrl+C) to stop.");
        game.Network.PlayerJoined += p => Console.WriteLine($"{p.Name} joined ({game.Network.Players.Count} playing)");
        game.Network.PlayerLeft += p => Console.WriteLine($"{p.Name} left");

        var loop = new GameLoop(game);
        // An error ends its frame and is said; the server plays on.
        loop.Faulted += (_, ex) =>
        {
            Console.Error.WriteLine($"Game error: {ex.Message}");
            loop.IsPaused = false;
        };
        using var stop = smoke ? new CancellationTokenSource(TimeSpan.FromSeconds(1)) : new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        loop.Run(FrameEvery, stop.Token);
        var steps = game.Time.Frame;
        loop.Stop();

        if (smoke)
        {
            var wpf = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "PresentationFramework" or "PresentationCore");
            Console.WriteLine($"Server steps: {steps}");
            Console.WriteLine(wpf ? "WPF: loaded" : "WPF: not loaded");
        }

        return 0;
    }
}
