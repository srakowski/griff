using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Griff.Platform.Windows;
using Griff.Runtime;

namespace Griff.Player;

/// <summary>
/// Runs an exported Griff game: loads the game packed into the executable (see <see cref="GamePackage"/>), else the
/// <c>game</c> folder next to it, or a project folder passed on the command line, and plays its start scene. F11 toggles fullscreen, as does the game itself
/// (<see cref="Game.Fullscreen"/>), and the choice is remembered.
/// </summary>
/// <remarks>
/// For network games: <c>--host [port]</c> hosts straight away, <c>--join &lt;address&gt;</c> joins, <c>--name &lt;name&gt;</c>
/// sets the player's name, and <c>--server [port]</c> runs a dedicated server with no window (the game's start scene,
/// hosted for everyone, played by nobody) until it's closed. <c>--mute</c> (or <c>GRIFF_MUTE=1</c>) plays it without
/// opening the sound device: nothing is heard, and the game runs just as it would with sound.
/// </remarks>
public partial class App : Application, IDisposable
{
    private GameRunner? _runner;
    private WindowState _windowedState;

    // What the command line asked for, besides the game folder.
    private sealed record Options(string? Folder, int? Host, string? Join, int? Server, string? Name, bool Mute);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = Parse(e.Args);
        if (options.Mute)
            AudioOutput.Muted = true;
        var folder = options.Folder ?? Path.Combine(AppContext.BaseDirectory, ExportLayout.GameFolder);
        LoadedGame game;
        try
        {
            // An exported game carries its content inside the exe; unpack it (once) and play from there.
            if (options.Folder is null && Environment.ProcessPath is { } exe && GamePackage.IsPackaged(exe))
            {
                var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff", "Games");
                folder = GamePackage.Extract(exe, cache, Path.GetFileNameWithoutExtension(exe))!;
            }

            game = await GameLoader.LoadAsync(folder);
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("GRIFF_PLAYER_SMOKE") is not null)
        {
            Console.Error.WriteLine(ex);
            Shutdown(2);
            return;
        }
        catch (Exception ex)
        {
            if (options.Server is not null)
            {
                Console.Error.WriteLine($"Couldn't load the game from \"{folder}\": {ex.Message}");
                Shutdown(1);
                return;
            }

            MessageBox.Show($"Couldn't load the game from \"{folder}\".\n\n{ex.Message}", "Griff", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (options.Server is { } serverPort)
        {
            RunServer(game, serverPort);
            return;
        }

        var surface = new GameSurface { AssetRoot = game.Project.AssetsPath };
        var window = new Window
        {
            Title = game.Project.Name,
            Width = 1280,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.Black,
            Content = surface,
        };

        window.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.F11 && _runner is not null)
                _runner.Game.Fullscreen = !_runner.Game.Fullscreen;
        };
        window.Closed += (_, _) => _runner?.Dispose();

        // Smoke test (GRIFF_PLAYER_SMOKE=<png path>): run off-screen for a second, save a frame, exit 0 on success.
        var smokeOutput = Environment.GetEnvironmentVariable("GRIFF_PLAYER_SMOKE");
        if (smokeOutput is not null)
        {
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -30000;
        }

        MainWindow = window;
        window.Show();

        _runner = new GameRunner(game.Scene, surface, window);
        _runner.Game.SceneLoader = game.LoadScene;
        _runner.Game.PrefabLoader = game.LoadPrefab;
        _runner.Game.SceneName = game.SceneName;
        _runner.Game.Title = game.Project.Name;
        _runner.Game.Components = game.Components;
        _runner.Game.UseProjectSettings(game.Project);
        _runner.Game.Problems.Reported += p => Trace.TraceWarning(p.Message);
        _runner.Game.QuitRequested += (_, _) => Dispatcher.BeginInvoke(window.Close);
        _runner.Game.FullscreenChanged += (_, _) => SetFullscreen(window, _runner.Game.Fullscreen);
        if (smokeOutput is null)
            _runner.Game.SaveData = new SaveData(SaveData.PlayerPath(game.Project.Name));
        _runner.Faulted += (_, ex) =>
        {
            if (smokeOutput is not null)
                return;
            MessageBox.Show(window, $"The game hit an error and paused.\n\n{ex.Message}", game.Project.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            _runner.IsPaused = false;
        };
        _runner.Start();
        StartNetwork(_runner.Game, options);

        if (smokeOutput is not null)
        {
            await Task.Delay(1000);
            var ok = !_runner.IsPaused && SaveFrame(surface, _runner.Game.Scene, smokeOutput);
            Console.WriteLine(AudioOutput.IsAvailable ? "Sound device: open" : "Sound device: not open");
            Shutdown(ok ? 0 : 1);
            return;
        }

        Keyboard.Focus(window);
    }

    private static Options Parse(string[] args)
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

        return new Options(folder, host, join, server, name, mute);
    }

    /// <summary>Stops the game when the app exits (stopping twice is harmless: the window's closing stops it too).</summary>
    public void Dispose()
    {
        _runner?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    private static void StartNetwork(Game game, Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.Name))
            game.Net.PlayerName = options.Name;
        if (options.Host is { } port)
            game.Net.Host(port);
        else if (options.Join is { } address)
            game.Net.Join(address);
    }

    // A dedicated server: the start scene, run and hosted with no window, until the process is ended.
    private void RunServer(LoadedGame loaded, int port)
    {
        AttachConsole(-1);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var game = new Game(loaded.Scene, new InputState())
        {
            SceneLoader = loaded.LoadScene,
            PrefabLoader = loaded.LoadPrefab,
            SceneName = loaded.SceneName,
            Title = loaded.Project.Name,
            Components = loaded.Components,
            SaveData = new SaveData(SaveData.PlayerPath(loaded.Project.Name + " Server")),
        };
        game.UseProjectSettings(loaded.Project);
        game.Problems.Reported += p => Console.Error.WriteLine(p.Message);
        if (!game.Net.HostServer(port))
        {
            Console.Error.WriteLine(game.Net.Status);
            Shutdown(1);
            return;
        }

        Console.WriteLine($"{loaded.Project.Name}: {game.Net.Status}. Close this window (or press Ctrl+C) to stop.");
        game.Net.PlayerJoined += p => Console.WriteLine($"{p.Name} joined ({game.Net.Players.Count} playing)");
        game.Net.PlayerLeft += p => Console.WriteLine($"{p.Name} left");
        var clock = Stopwatch.StartNew();
        // The game takes its fixed steps as real time passes, however unevenly the timer fires.
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(1000.0 / 120) };
        timer.Tick += (_, _) =>
        {
            var seconds = clock.Elapsed.TotalSeconds;
            clock.Restart();
            try
            {
                game.Advance(seconds);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Game error: {ex.Message}");
            }
        };
        timer.Start();
        Console.CancelKeyPress += (_, _) => Dispatcher.Invoke(() =>
        {
            timer.Stop();
            game.Stop();
            Shutdown(0);
        });
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private static bool SaveFrame(GameSurface surface, Griff.Scenes.Scene scene, string path)
    {
        if (surface.Capture(Griff.Rendering.SceneRenderer.Build(scene, surface.Aspect)) is not var (rgba, width, height))
            return false;

        for (var i = 0; i < rgba.Length; i += 4)
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rgba, width * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        return true;
    }

    private void SetFullscreen(Window window, bool fullscreen)
    {
        if (fullscreen == (window.WindowStyle == WindowStyle.None))
            return;

        if (!fullscreen)
        {
            window.WindowStyle = WindowStyle.SingleBorderWindow;
            window.WindowState = _windowedState;
        }
        else
        {
            _windowedState = window.WindowState;
            window.WindowStyle = WindowStyle.None;
            window.WindowState = WindowState.Normal;
            window.WindowState = WindowState.Maximized;
        }
    }
}
