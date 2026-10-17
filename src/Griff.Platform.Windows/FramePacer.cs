using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Griff.Hosting;

namespace Griff.Platform.Windows;

/// <summary>
/// Calls back on the UI thread once for every frame the screen shows, timed by the desktop's compositor (DWM) rather
/// than by WPF's render loop, which on some machines only gets every other frame of the screen's (a game drawn at 30 fps
/// on a 60 Hz screen). Only one frame waits at a time, so a frame that takes too long makes the next one wait for the
/// screen's frame after, as a game in step with the screen would. Each frame is told when the compositor let it go (a
/// <see cref="Stopwatch"/> timestamp): the vertical blank the compositor is working towards, as it reports it, so frames
/// are timed by the screen's steady beat rather than by when this thread woke up (a millisecond or two either way).
/// With <see cref="ToScreen"/> off (the game's VSync off), a frame is called for as soon as the last is done, at input's
/// priority so the keyboard and mouse still get through; with a <see cref="Cap"/>, frames come no more often than it
/// allows (see <see cref="FrameSchedule"/>), on the screen's beat or, off it, after a precise wait.
/// </summary>
internal sealed class FramePacer : IDisposable
{
    // The compositor can let a wait go early (when nothing was waiting to be shown); frames never come closer than this,
    // which still leaves room for a 144 Hz screen.
    private const double MinMilliseconds = 6;

    private readonly Dispatcher _dispatcher;
    private readonly Action<long> _frame;
    private readonly FrameSchedule _schedule = new();
    // Set as each frame is done, for frames that don't wait for the screen.
    private readonly AutoResetEvent _done = new(false);
    private volatile bool _running = true, _toScreen = true;
    private volatile int _cap;
    private int _waiting;
    private long _shownAt;

    public FramePacer(Dispatcher dispatcher, Action<long> frame)
    {
        _dispatcher = dispatcher;
        _frame = frame;
        new Thread(Run) { IsBackground = true, Name = "Griff frame pacer" }.Start();
    }

    /// <summary>Whether frames come one for each the screen shows (VSync); off, as soon as the last is done.</summary>
    public bool ToScreen
    {
        get => _toScreen;
        set
        {
            if (_toScreen == value)
                return;
            _toScreen = value;
            if (_running)
                _done.Set();
        }
    }

    /// <summary>The most frames a second, 0 for no cap.</summary>
    public int Cap
    {
        get => _cap;
        set => _cap = value;
    }

    // The thread sees it within a frame (or the tenth of a second it waits for one at most), and ends.
    public void Dispose() => _running = false;

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        using var timer = new PreciseTimer();
        while (_running)
        {
            _schedule.Cap = _cap;
            long shownAt;
            if (_toScreen)
            {
                if (DwmFlush() != 0)
                    Thread.Sleep(15);
                if (clock.Elapsed.TotalMilliseconds < MinMilliseconds)
                {
                    Thread.Sleep(1);
                    continue;
                }

                clock.Restart();
                // A frame still waiting skips the screen's frame, as it always has; the cap counts only frames drawn.
                if (Volatile.Read(ref _waiting) != 0)
                    continue;
                shownAt = ShownAt();
                if (!_schedule.Take(Seconds(shownAt)))
                    continue;
            }
            else
            {
                if (Volatile.Read(ref _waiting) != 0)
                {
                    _done.WaitOne(100);
                    continue;
                }

                var wait = _schedule.Wait(Seconds(Stopwatch.GetTimestamp()));
                if (wait > 0)
                    timer.Wait(wait);
                shownAt = Stopwatch.GetTimestamp();
                if (!_schedule.Take(Seconds(shownAt)))
                    continue;
            }

            if (Interlocked.Exchange(ref _waiting, 1) == 0)
            {
                Volatile.Write(ref _shownAt, shownAt);
                // Off the screen's beat, frames could come back to back: at input's priority, so the keyboard and mouse
                // get through.
                _dispatcher.BeginInvoke(_toScreen ? DispatcherPriority.Render : DispatcherPriority.Input, OnFrame);
            }
        }

        _done.Dispose();
    }

    private static double Seconds(long timestamp) => (double)timestamp / Stopwatch.Frequency;

    private void OnFrame()
    {
        try
        {
            if (_running)
                _frame(Volatile.Read(ref _shownAt));
        }
        finally
        {
            Volatile.Write(ref _waiting, 0);
            if (_running)
                _done.Set();
        }
    }

    // The vertical blank the compositor is working towards, if it says (and it's within a few frames of now, and after the
    // last one); otherwise now.
    private long _lastBlank;

    private long ShownAt()
    {
        var now = Stopwatch.GetTimestamp();
        var timing = new DwmTimingInfo { Size = (uint)Marshal.SizeOf<DwmTimingInfo>() };
        if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref timing) != 0)
            return now;
        var blank = (long)timing.QpcVBlank;
        if (blank <= _lastBlank || Math.Abs(blank - now) > Stopwatch.Frequency / 20)
            return now;
        _lastBlank = blank;
        return blank;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    // Waits a fraction of a millisecond late at most: Windows' high-resolution waitable timer for most of the wait
    // (Thread.Sleep can be 15 ms late), then a few spins for the rest.
    private sealed class PreciseTimer : IDisposable
    {
        private const uint HighResolution = 0x2, AllAccess = 0x1F0003;
        private readonly IntPtr _handle = CreateWaitableTimerExW(IntPtr.Zero, null, HighResolution, AllAccess);

        public void Wait(double seconds)
        {
            var until = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
            var coarse = seconds - 0.0005;
            if (coarse > 0)
            {
                // Relative, in 100-nanosecond units.
                var due = -(long)(coarse * 10_000_000);
                if (_handle != IntPtr.Zero && SetWaitableTimer(_handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                    _ = WaitForSingleObject(_handle, 1000);
                else if (coarse > 0.002)
                    Thread.Sleep((int)(coarse * 1000) - 1);
            }

            while (Stopwatch.GetTimestamp() < until)
                Thread.Yield();
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
                CloseHandle(_handle);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

        [DllImport("kernel32.dll")]
        private static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr argument, bool resume);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DwmTimingInfo timing);

    // DWM_TIMING_INFO, packed to 1 byte as dwmapi.h declares it. Only the vertical blank is read.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DwmTimingInfo
    {
        public uint Size;
        public uint RateRefreshNumerator, RateRefreshDenominator;
        public ulong QpcRefreshPeriod;
        public uint RateComposeNumerator, RateComposeDenominator;
        public ulong QpcVBlank, CRefresh;
        public uint CDxRefresh;
        public ulong QpcCompose, CFrame;
        public uint CDxPresent;
        public ulong CRefreshFrame, CFrameSubmitted;
        public uint CDxPresentSubmitted;
        public ulong CFrameConfirmed;
        public uint CDxPresentConfirmed;
        public ulong CRefreshConfirmed;
        public uint CDxRefreshConfirmed;
        public ulong CFramesLate;
        public uint CFramesOutstanding;
        public ulong CFrameDisplayed, QpcFrameDisplayed, CRefreshFrameDisplayed, CFrameComplete, QpcFrameComplete, CFramePending, QpcFramePending;
        public ulong CFramesDisplayed, CFramesComplete, CFramesPending, CFramesAvailable, CFramesDropped, CFramesMissed, CRefreshNextDisplayed,
            CRefreshNextPresented, CRefreshesDisplayed, CRefreshesPresented, CRefreshStarted, CPixelsReceived, CPixelsDrawn, CBuffersEmpty;
    }
}
