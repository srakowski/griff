using System.IO;
using System.Runtime.InteropServices;
using Griff.Runtime;

namespace Griff.Player;

/// <summary>
/// Runs an exported Griff game: loads the game packed into the executable (see <see cref="GamePackage"/>), else the
/// <c>game</c> folder next to it, or a project folder passed on the command line, and plays its start scene in a window
/// (<see cref="App"/>), or with <c>--server</c> as a dedicated server with no window at all (<see cref="Server"/>).
/// </summary>
/// <remarks>
/// For network games: <c>--host [port]</c> hosts straight away, <c>--join &lt;address&gt;</c> joins, <c>--name &lt;name&gt;</c>
/// sets the player's name, and <c>--server [port]</c> runs a dedicated server (the game's start scene, hosted for everyone,
/// played by nobody) until it's closed. <c>--mute</c> (or <c>GRIFF_MUTE=1</c>) plays it without opening the sound device:
/// nothing is heard, and the game runs just as it would with sound. <c>GRIFF_PLAYER_SMOKE</c> runs it for a second as a
/// check (windowed, it saves a frame there).
/// </remarks>
public static class Program
{
    /// <summary>The smoke test's variable: the path of the frame a windowed game saves, or anything for a server.</summary>
    public const string SmokeVariable = "GRIFF_PLAYER_SMOKE";

    [STAThread]
    public static int Main(string[] args)
    {
        var options = PlayerOptions.Parse(args);
        // A server never touches WPF: it's a console program, drawing nothing.
        return options.Server is { } port ? Server.Run(options, port) : RunWindowed(options);
    }

    // Kept apart, so a server never loads the types (and with them WPF) it names.
    private static int RunWindowed(PlayerOptions options)
    {
        var app = new App(options);
        return app.Run();
    }

    /// <summary>
    /// The game folder to play: the one on the command line, else the game packed into this executable (unpacked once into
    /// the user's local app data), else the <c>game</c> folder beside it.
    /// </summary>
    public static string GameFolder(PlayerOptions options)
    {
        if (options.Folder is { } folder)
            return folder;
        if (Environment.ProcessPath is { } exe && GamePackage.IsPackaged(exe))
        {
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff", "Games");
            return GamePackage.Extract(exe, cache, Path.GetFileNameWithoutExtension(exe))!;
        }

        return Path.Combine(AppContext.BaseDirectory, ExportLayout.GameFolder);
    }

    /// <summary>Whether this run is the smoke test's.</summary>
    public static string? Smoke => Environment.GetEnvironmentVariable(SmokeVariable);

    [DllImport("kernel32.dll")]
    internal static extern bool AttachConsole(int processId);
}

/// <summary>What the command line asked for, besides the game folder.</summary>
public sealed record PlayerOptions(string? Folder, int? Host, string? Join, int? Server, string? Name, bool Mute)
{
    public static PlayerOptions Parse(string[] args)
    {
        string? folder = null, join = null, name = null;
        int? host = null, server = null;
        var mute = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? Next() => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
            int? Port() => Next() is { } text && int.TryParse(text, out var port) ? port : 0;
            switch (arg.ToLowerInvariant())
            {
                case "--host": host = Port(); break;
                case "--server": server = Port(); break;
                case "--join": join = Next() ?? ""; break;
                case "--name": name = Next(); break;
                case "--mute": mute = true; break;
                default:
                    if (!arg.StartsWith("--", StringComparison.Ordinal))
                        folder ??= arg;
                    break;
            }
        }

        return new PlayerOptions(folder, host, join, server, name, mute);
    }
}
