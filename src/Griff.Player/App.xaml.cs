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

        MainWindow = window;
        window.Show();

        _runner = new GameRunner(game.Scene, surface, window);
        _runner.Faulted += (_, ex) =>
        {
            MessageBox.Show(window, $"The game hit an error and paused.\n\n{ex.Message}", game.Project.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            _runner.IsPaused = false;
        };
        _runner.Start();
        Keyboard.Focus(window);
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
