#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Griff.Editor.Core.Panels;
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
/// A bar at the top pauses, changes speed, jumps between chapters and exits (Ctrl+Alt+Space, Up/Down, Left/Right, X),
/// and mutes the narration (kept for the next demo: see <see cref="DemoPreferences"/>). GRIFF_MUTE_NARRATION=1 mutes it
/// for one run; GRIFF_MUTE=1 mutes the editor's own sound, as anywhere.
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

        // Demos start with the panels where they start out and every Inspector section open, and leave the user's own
        // layout and sections alone.
        services.GetRequiredService<PanelLayout>().StartOver();
        services.GetRequiredService<Features.Inspector.InspectorPanel>().StartOver();

        // GRIFF_DEMO=path\to\change.demo plays a demo script; GRIFF_DEMO=1 (or fast) the guided tour.
        var script = mode.EndsWith(".demo", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(mode) : null;
        var fast = mode.Equals("fast", StringComparison.OrdinalIgnoreCase) || Environment.GetEnvironmentVariable("GRIFF_DEMO_FAST") == "1";
        _ = RunAsync(services, shell, fast, script);
        return true;
    }

    private static async Task RunAsync(IServiceProvider services, MainWindow main, bool fast, string? scriptPath)
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
        DemoPreferences.Attach(control, DemoPreferences.DefaultPath, DemoPreferences.MutedForRun);
        var overlay = new DemoOverlay();
        var bar = new DemoControlBar(control);
        overlay.Above.Add(bar);
        var play = services.GetRequiredService<PlayMode>();
        var restart = false;
        var failed = false;

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
            ui.Scripts = services.GetRequiredService<Features.Scripts.ScriptEditors>();
            ui.ScriptTabs = services.GetRequiredService<Features.Scripts.ScriptTabs>();
            ui.Debugger = services.GetRequiredService<Features.Debugging.Debugger>();
            ui.Panels = services.GetRequiredService<PanelHost>();
            var options = new DemoOptions(fast, Log, control);
            var workspace = services.GetRequiredService<IWorkspace>();
            var selection = services.GetRequiredService<ISelection>();
            var sceneView = services.GetRequiredService<SceneViewControl>();

            Log("demo started" + (scriptPath is null ? "" : ": " + scriptPath));
            if (scriptPath is null)
            {
                using var tour = new DemoScript(ui, workspace, selection, play, sceneView, options);
                restart = await tour.Run();
            }
            else
            {
                restart = await new DemoFile(scriptPath, ui, workspace, selection, play, sceneView, options).Run();
            }

            Log(restart ? "starting over" : "demo finished");
        }
        catch (Exception ex)
        {
            failed = true;
            Log("demo failed: " + ex);
            narrator?.Stop();
            overlay.ClearSpotlight();
            overlay.Caption("The demo stopped early: " + ex.Message);
            if (Environment.GetEnvironmentVariable("GRIFF_DEMO_EXIT") != "1")
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
            control.Dispose();
        }

        // Checking demos (GRIFF_DEMO_EXIT=1): close the editor when done, with an exit code that says how it went.
        if (Environment.GetEnvironmentVariable("GRIFF_DEMO_EXIT") == "1" && !restart)
        {
            services.GetRequiredService<Features.Scripts.ScriptEditors>().DiscardAll();
            services.GetRequiredService<IWorkspace>().Scene?.MarkSaved();
            Environment.Exit(failed ? 1 : 0);
        }

        // Back to before the project existed: a fresh editor runs the demo from the top.
        if (restart && Environment.ProcessPath is { } exe)
        {
            services.GetRequiredService<Features.Scripts.ScriptEditors>().DiscardAll();
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
