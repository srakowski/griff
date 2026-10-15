#if DEBUG
using System.Diagnostics;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>Gives each step of a demo a time limit, so no demo can wait for good.</summary>
internal static class DemoStepGuard
{
    /// <summary>When the running step last showed signs of life (a <see cref="Stopwatch"/> timestamp), for a watchdog that catches a blocked editor; null between steps.</summary>
    public static long? Heartbeat { get; private set; }

    /// <summary>
    /// Runs <paramref name="step"/> and fails with a <see cref="TimeoutException"/> if it takes longer than
    /// <paramref name="limit"/>, not counting the time <paramref name="paused"/> says the viewer held it. A stopped step is
    /// left behind (it can't be cancelled from outside), which is fine when the demo is ending anyway.
    /// </summary>
    public static async Task Run(Func<Task> step, TimeSpan limit, Func<bool> paused, int pollMs = 100)
    {
        var task = step();
        var ran = TimeSpan.Zero;
        var last = Stopwatch.GetTimestamp();
        Heartbeat = last;
        try
        {
            while (!task.IsCompleted)
            {
                await Task.WhenAny(task, Task.Delay(pollMs));
                var now = Stopwatch.GetTimestamp();
                if (!paused())
                    ran += Stopwatch.GetElapsedTime(last, now);
                last = now;
                Heartbeat = now;
                if (!task.IsCompleted && ran > limit)
                {
                    _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException($"The step ran for over {limit.TotalSeconds:0.##} s without finishing, so it was stopped.");
                }
            }

            await task;
        }
        finally
        {
            Heartbeat = null;
        }
    }
}
#endif
