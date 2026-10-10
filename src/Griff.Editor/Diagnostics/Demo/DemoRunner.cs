#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.Projects;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// Debug-only guided demo: set GRIFF_DEMO=1 and the editor narrates and drives itself through building a small game,
/// with real mouse and keyboard input (hands off while it runs). GRIFF_DEMO=fast runs silently at speed, for checking
/// the script. Pair with GRIFF_SETTINGS_PATH so it starts from a clean editor. GRIFF_DEMO_LOG names a log file.
/// A bar at the top pauses, changes speed, jumps between chapters and exits (Ctrl+Alt+Space, Up/Down, Left/Right, X).
/// </summary>
internal static class DemoRunner
{
    private const string ProjectName = "Ember Hollow";

    /// <summary>Where the demo makes its project.</summary>
    public static string ProjectFolder { get; private set; } = "";

    public static bool TryRun(IServiceProvider services, Window main)
    {
        var mode = Environment.GetEnvironmentVariable("GRIFF_DEMO");
        if (string.IsNullOrEmpty(mode) || main is not MainWindow shell)
            return false;

        _ = RunAsync(services, shell, fast: mode.Equals("fast", StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static async Task RunAsync(IServiceProvider services, MainWindow main, bool fast)
    {
        var logPath = Environment.GetEnvironmentVariable("GRIFF_DEMO_LOG");
        var clock = Stopwatch.StartNew();
        void Log(string message)
        {
            if (logPath is not null)
                File.AppendAllText(logPath, $"{clock.Elapsed:mm\\:ss\\.f} {message}{Environment.NewLine}");
        }

        var settings = services.GetRequiredService<ISettingsStore>().Settings;
        ProjectFolder = Path.Combine(settings.LastProjectLocation ?? NewProjectViewModel.DefaultLocation, ProjectName);

        // Starting over: the project an earlier run made goes (only ever one the demo made).
        if (File.Exists(Path.Combine(ProjectFolder, ".griff-demo")))
            Directory.Delete(ProjectFolder, recursive: true);

        var control = new DemoControl();
        DemoControl.Active = control;
        var overlay = new DemoOverlay();
        var bar = new DemoControlBar(control);
        overlay.Above.Add(bar);
        var play = services.GetRequiredService<PlayMode>();
        var restart = false;

        // Reaching for the mouse pauses the demo, so the viewer can use the bar (or anything else).
        var takeover = new DemoTakeover(control);
        takeover.TookOver += (_, _) => overlay.Badge("⏸  Paused: the mouse is yours · ▶ or Ctrl+Alt+Space to resume");
        control.PauseChanged += (_, paused) =>
        {
            if (!paused)
                overlay.Badge(null);
        };

        // Pausing the demo pauses a game it's playing, too; skipping ahead hurries the game along.
        var pausedGame = false;
        control.PauseChanged += (_, paused) =>
        {
            if (paused && play is { IsPlaying: true, IsPaused: false })
            {
                play.TogglePause();
                pausedGame = true;
            }
            else if (!paused && pausedGame)
            {
                if (play is { IsPlaying: true, IsPaused: true })
                    play.TogglePause();
                pausedGame = false;
            }
        };
        var gameSpeed = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        gameSpeed.Tick += (_, _) =>
        {
            if (play.Game is { } game && game.Time.Scale is 1 or 2)
                game.Time.Scale = control.Skipping ? 2 : 1;
        };
        gameSpeed.Start();

        DemoNarrator? narrator = null;
        try
        {
            main.WindowState = WindowState.Maximized;
            main.Show();
            main.Activate();
            await DemoUi.Settle(600);
            overlay.Cover(WindowRect(main));
            bar.PlaceAt(WindowRect(main));
            await DemoUi.Settle(200);

            var input = new DemoInput(overlay, fast ? 4 : 1);
            narrator = new DemoNarrator(overlay, speak: !fast, voiceName: Environment.GetEnvironmentVariable("GRIFF_DEMO_VOICE") ?? "Microsoft Zira Desktop", rate: 1, silentSpeed: fast ? 6 : 1);
            var ui = new DemoUi(main, services.GetRequiredService<SceneViewControl>(), input, overlay, narrator);
            var script = new DemoScript(ui, services.GetRequiredService<IWorkspace>(), services.GetRequiredService<ISelection>(),
                play, services.GetRequiredService<SceneViewControl>(), new DemoOptions(fast, Log, control));

            Log("demo started");
            restart = await script.Run();
            Log(restart ? "starting over" : "demo finished");
        }
        catch (Exception ex)
        {
            Log("demo failed: " + ex);
            narrator?.Stop();
            overlay.ClearSpotlight();
            overlay.Caption("The demo stopped early: " + ex.Message);
            await Task.Delay(6000);
        }
        finally
        {
            gameSpeed.Stop();
            takeover.Dispose();
            DemoControl.Active = null;
            overlay.Caption(null);
            await Task.Delay(300);
            bar.Close();
            overlay.Close();
        }

        // Back to before the project existed: a fresh editor runs the demo from the top.
        if (restart && Environment.ProcessPath is { } exe)
        {
            services.GetRequiredService<IWorkspace>().Scene?.MarkSaved();
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
            Environment.Exit(0);
        }
    }

    // The window's whole frame on screen, title bar included, in device pixels.
    private static Rect WindowRect(Window window)
    {
        GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var r);
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
#endif
