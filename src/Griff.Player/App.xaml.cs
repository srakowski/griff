using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Platform.Windows;
using Griff.Runtime;

namespace Griff.Player;

/// <summary>
/// Runs an exported Griff game: loads the <c>game</c> folder next to the executable (or a project folder
/// passed on the command line) and plays its start scene. F11 toggles fullscreen.
/// </summary>
public partial class App : Application
{
    private GameRunner? _runner;
    private WindowState _windowedState;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var folder = e.Args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, ExportLayout.GameFolder);
        LoadedGame game;
        try
        {
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
            MessageBox.Show($"Couldn't load the game from \"{folder}\".\n\n{ex.Message}", "Griff", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
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
            if (args.Key == System.Windows.Input.Key.F11)
                ToggleFullscreen(window);
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
        _runner.Faulted += (_, ex) =>
        {
            if (smokeOutput is not null)
                return;
            MessageBox.Show(window, $"The game hit an error and paused.\n\n{ex.Message}", game.Project.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            _runner.IsPaused = false;
        };
        _runner.Start();

        if (smokeOutput is not null)
        {
            await Task.Delay(1000);
            var ok = !_runner.IsPaused && SaveFrame(surface, game, smokeOutput);
            Shutdown(ok ? 0 : 1);
            return;
        }

        Keyboard.Focus(window);
    }

    private static bool SaveFrame(GameSurface surface, LoadedGame game, string path)
    {
        if (surface.Capture(Griff.Rendering.SceneRenderer.Build(game.Scene, surface.Aspect)) is not var (rgba, width, height))
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

    private void ToggleFullscreen(Window window)
    {
        if (window.WindowStyle == WindowStyle.None)
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
