using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Platform.Windows;
using Griff.Runtime;

namespace Griff.Player;

/// <summary>
/// The player's window: the game (see <see cref="Program"/>) drawn on a <see cref="GameSurface"/> and played by a
/// <see cref="GameRunner"/>, its sound through the player's own <see cref="SoundOutput"/>. F11 toggles fullscreen, as
/// does the game itself (<see cref="Game.Fullscreen"/>), and the choice is remembered. The window opens at the size the
/// player picked (<see cref="Game.WindowSize"/>; 1280x720 until they do) and takes a new one as they pick it;
/// fullscreen is borderless at the screen's own resolution.
/// </summary>
public sealed class App(PlayerOptions options) : Application, IDisposable
{
    private GameRunner? _runner;
    private SoundOutput? _sound;
    private WindowState _windowedState;
    // A window size picked while fullscreen, for the window once it's windowed again.
    private bool _resizeWindowed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var smokeOutput = Program.Smoke;
        var folder = "";
        LoadedGame loaded;
        try
        {
            folder = Program.GameFolder(options);
            loaded = await GameLoader.LoadAsync(folder);
        }
        catch (Exception ex) when (smokeOutput is not null)
        {
            Console.Error.WriteLine(ex);
            Shutdown(2);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't load the game from \"{folder}\".\n\n{ex.Message}", "Griff", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var project = loaded.Project!;
        // An exported game's files don't change while it plays: they're read once, never watched.
        var assets = new Griff.Projects.AssetFolder(project.AssetsPath);
        var surface = new GameSurface { Assets = assets };
            // A smoke test's game stays on this machine and keeps its saves to itself.
        var saves = smokeOutput is null ? new SaveData(SaveData.PlayerPath(project.Name)) : null;
        var window = new Window
        {
            Title = project.Name,
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
        if (smokeOutput is not null)
        {
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -30000;
        }

        // The size the player picked last time, before the window shows (the game reads it from the same saves as it
        // starts).
        if (WindowSize.TryParse(saves?.GetString(Game.WindowSizeSaveKey), out var picked))
            window.SourceInitialized += (_, _) => GameWindowSizing.Resize(window, picked);

        MainWindow = window;
        window.Show();

        _sound = AudioOutput.Open(options.Mute);
        var game = loaded.NewGame(new GameHostOptions
        {
            Audio = WindowsAudio.Create(_sound, WindowsAudio.Library(assets)),
            SaveData = saves,
            OverNetwork = smokeOutput is null,
            Problems = p => Trace.TraceWarning(p.Message),
        });
        game.QuitRequested += (_, _) => Dispatcher.BeginInvoke(window.Close);
        game.FullscreenChanged += (_, _) => SetFullscreen(window, game);
        game.WindowSizeChanged += (_, _) => SetWindowSize(window, game);
        // The window sizes a player can pick are those that fit the screen the window's on.
        game.DisplayRoom = GameWindowSizing.Room(window);
        window.LocationChanged += (_, _) => game.DisplayRoom = GameWindowSizing.Room(window);
        window.DpiChanged += (_, _) => game.DisplayRoom = GameWindowSizing.Room(window);
        // Fullscreen as the player left it last time (read as the game was made, before it could say so).
        SetFullscreen(window, game);
        var runner = _runner = new GameRunner(game, surface, window);
        runner.Loop.Faulted += (_, ex) =>
        {
            if (smokeOutput is not null)
                return;
            MessageBox.Show(window, $"The game hit an error and paused.\n\n{ex.Message}", project.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            runner.IsPaused = false;
        };
        runner.Start();
        StartNetwork(game, options);

        if (smokeOutput is not null)
        {
            await Task.Delay(1000);
            var ok = !runner.IsPaused && runner.Loop.LastFrame is { } shown && SaveFrame(surface, shown, smokeOutput);
            Console.WriteLine(_sound.IsDeviceOpen ? "Sound device: open" : "Sound device: not open");
            Shutdown(ok ? 0 : 1);
            return;
        }

        Keyboard.Focus(window);
    }

    /// <summary>Stops the game and closes the sound device as the app exits (stopping twice is harmless: closing the window stops it too).</summary>
    public void Dispose()
    {
        _runner?.Dispose();
        _sound?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    private static void StartNetwork(Game game, PlayerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Name))
            game.Network.PlayerName = options.Name;
        if (options.Host is { } port)
            game.Network.Host(port);
        else if (options.Join is { } address)
            game.Network.Join(address);
    }

    // The frame last shown, drawn again and saved as a PNG.
    private static bool SaveFrame(GameSurface surface, Griff.Rendering.RenderFrame frame, string path)
    {
        if (surface.Capture(frame) is not var (rgba, width, height))
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

    // Fullscreen is the window without its frame, maximized: borderless at the screen's own resolution, never changing
    // the display's mode.
    private void SetFullscreen(Window window, Game game)
    {
        var fullscreen = game.Fullscreen;
        if (fullscreen == (window.WindowStyle == WindowStyle.None))
            return;

        if (!fullscreen)
        {
            window.WindowStyle = WindowStyle.SingleBorderWindow;
            window.WindowState = _windowedState;
            if (_resizeWindowed && game.WindowSize is { } size)
                GameWindowSizing.Resize(window, size);
            _resizeWindowed = false;
        }
        else
        {
            _windowedState = window.WindowState;
            window.WindowStyle = WindowStyle.None;
            window.WindowState = WindowState.Normal;
            window.WindowState = WindowState.Maximized;
        }
    }

    // The player picked a window size: the window takes it now, or once it's windowed again.
    private void SetWindowSize(Window window, Game game)
    {
        if (game.WindowSize is not { } size)
            return;
        if (window.WindowStyle == WindowStyle.None)
            _resizeWindowed = true;
        else
            GameWindowSizing.Resize(window, size);
    }
}
