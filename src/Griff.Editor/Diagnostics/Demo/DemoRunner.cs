#if DEBUG
using System.IO;
using System.Windows;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// Debug-only guided demo: set GRIFF_DEMO=1 and the editor narrates and drives itself through building a small game,
/// with real mouse and keyboard input (hands off while it runs). GRIFF_DEMO=fast runs silently at speed, for checking
/// the script. Pair with GRIFF_SETTINGS_PATH so it starts from a clean editor. GRIFF_DEMO_LOG names a log file.
/// Press Ctrl+Shift+Esc... or just close the editor, to stop it.
/// </summary>
internal static class DemoRunner
{
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
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Log(string message)
        {
            if (logPath is not null)
                File.AppendAllText(logPath, $"{clock.Elapsed:mm\\:ss\\.f} {message}{Environment.NewLine}");
        }

        var overlay = new DemoOverlay();
        DemoNarrator? narrator = null;
        try
        {
            main.WindowState = WindowState.Maximized;
            main.Show();
            main.Activate();
            await DemoUi.Settle(600);
            overlay.Cover(WindowRect(main));
            await DemoUi.Settle(200);

            var input = new DemoInput(overlay, fast ? 4 : 1);
            narrator = new DemoNarrator(overlay, speak: !fast, voiceName: Environment.GetEnvironmentVariable("GRIFF_DEMO_VOICE") ?? "Microsoft Zira Desktop", rate: 1, silentSpeed: fast ? 6 : 1);
            var ui = new DemoUi(main, services.GetRequiredService<SceneViewControl>(), input, overlay, narrator);
            var script = new DemoScript(ui, services.GetRequiredService<IWorkspace>(), services.GetRequiredService<ISelection>(),
                services.GetRequiredService<PlayMode>(), services.GetRequiredService<SceneViewControl>(), new DemoOptions(fast, Log));

            Log("demo started");
            await script.Run();
            Log("demo finished");
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
            overlay.Caption(null);
            await Task.Delay(400);
            overlay.Close();
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
