#if DEBUG
using System.IO;
using System.Windows;
using Griff.Editor.Diagnostics.Demo;

namespace Griff.Editor.Tests;

/// <summary>A demo never waits for good: a step that hangs fails with its line, and a prompt a demo can't answer fails it.</summary>
public sealed class DemoSafetyTests
{
    [Fact]
    public async Task A_step_that_never_finishes_fails_instead_of_waiting()
    {
        var forever = new TaskCompletionSource();
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            DemoStepGuard.Run(() => forever.Task, TimeSpan.FromMilliseconds(300), () => false, pollMs: 20));
        Assert.Contains("without finishing", error.Message);
        Assert.Null(DemoStepGuard.Heartbeat);
    }

    [Fact]
    public async Task Time_spent_paused_does_not_count_against_a_step()
    {
        var paused = true;
        var done = new TaskCompletionSource();
        var run = DemoStepGuard.Run(() => done.Task, TimeSpan.FromMilliseconds(300), () => paused, pollMs: 20);
        await Task.Delay(800);
        Assert.False(run.IsCompleted);
        paused = false;
        done.SetResult();
        await run;
    }

    [Fact]
    public async Task A_failing_step_reports_its_own_error()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DemoStepGuard.Run(() => Task.FromException(new InvalidOperationException("no")), TimeSpan.FromSeconds(5), () => false));
    }

    [Fact]
    public void A_failed_runs_project_folders_are_cleared_before_the_next()
    {
        var root = Path.Combine(Path.GetTempPath(), "griff-demo-clear-" + Guid.NewGuid().ToString("N"));
        try
        {
            // One without the marker (a run that failed while making the project), one with it.
            Directory.CreateDirectory(Path.Combine(root, "Half Made", "Assets"));
            File.WriteAllText(Path.Combine(root, "Half Made", "Assets", "a.txt"), "x");
            Directory.CreateDirectory(Path.Combine(root, "Made"));
            File.WriteAllText(Path.Combine(root, "Made", ".griff-demo"), "x");
            File.WriteAllText(Path.Combine(root, "settings.json"), "{}");

            DemoFile.ClearProjects(root);

            Assert.Empty(Directory.EnumerateDirectories(root));
            Assert.True(File.Exists(Path.Combine(root, "settings.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_fallback_command_that_opens_a_prompt_fails_with_a_message_instead_of_hanging()
    {
        EditorHost.Run(_ => { }, _ =>
        {
            // Awaits resume on this thread, as in the editor.
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
            {
                // An ordinary command finishes.
                var ran = false;
                EditorHost.Pump(DemoUi.RunFallback(() =>
                {
                    ran = true;
                    return Task.CompletedTask;
                }, "menu File > Save"));
                Assert.True(ran);

                // One that asks something: ShowDialog doesn't return until it's answered.
                var error = Assert.Throws<InvalidOperationException>(() => EditorHost.Pump(DemoUi.RunFallback(() =>
                {
                    new Window { Title = "Name the scene", Width = 200, Height = 100, ShowActivated = false }.ShowDialog();
                    return Task.CompletedTask;
                }, "menu File > New Scene...")));
                Assert.Contains("menu File > New Scene...", error.Message);
                Assert.Contains("opened a prompt (Name the scene)", error.Message);
                Assert.DoesNotContain(System.Windows.PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>(), x => x.RootVisual is Window);
            }
        }, timeout: TimeSpan.FromMinutes(1));
    }
}
#endif
