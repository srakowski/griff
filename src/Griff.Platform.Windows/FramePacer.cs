using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Griff.Platform.Windows;

/// <summary>
/// Calls back on the UI thread once for every frame the screen shows, timed by the desktop's compositor (DWM) rather
/// than by WPF's render loop, which on some machines only gets every other frame of the screen's (a game drawn at 30 fps
/// on a 60 Hz screen). Only one frame waits at a time, so a frame that takes too long makes the next one wait for the
/// screen's frame after, as a game in step with the screen would.
/// </summary>
internal sealed class FramePacer : IDisposable
{
    // The compositor can let a wait go early (when nothing was waiting to be shown); frames never come closer than this,
    // which still leaves room for a 144 Hz screen.
    private const double MinMilliseconds = 6;

    private readonly Dispatcher _dispatcher;
    private readonly Action _frame;
    private volatile bool _running = true;
    private int _waiting;

    public FramePacer(Dispatcher dispatcher, Action frame)
    {
        _dispatcher = dispatcher;
        _frame = frame;
        new Thread(Run) { IsBackground = true, Name = "Griff frame pacer" }.Start();
    }

    public void Dispose() => _running = false;

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        while (_running)
        {
            if (DwmFlush() != 0)
                Thread.Sleep(15);
            if (clock.Elapsed.TotalMilliseconds < MinMilliseconds)
            {
                Thread.Sleep(1);
                continue;
            }

            clock.Restart();
            if (Interlocked.Exchange(ref _waiting, 1) == 0)
                _dispatcher.BeginInvoke(DispatcherPriority.Render, OnFrame);
        }
    }

    private void OnFrame()
    {
        try
        {
            if (_running)
                _frame();
        }
        finally
        {
            Volatile.Write(ref _waiting, 0);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
