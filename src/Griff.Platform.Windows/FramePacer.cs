using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Griff.Platform.Windows;

/// <summary>
/// Calls back on the UI thread once for every frame the screen shows, timed by the desktop's compositor (DWM) rather
/// than by WPF's render loop, which on some machines only gets every other frame of the screen's (a game drawn at 30 fps
/// on a 60 Hz screen). Only one frame waits at a time, so a frame that takes too long makes the next one wait for the
/// screen's frame after, as a game in step with the screen would. Each frame is told when the compositor let it go (a
/// <see cref="Stopwatch"/> timestamp): the vertical blank the compositor is working towards, as it reports it, so frames
/// are timed by the screen's steady beat rather than by when this thread woke up (a millisecond or two either way).
/// </summary>
internal sealed class FramePacer : IDisposable
{
    // The compositor can let a wait go early (when nothing was waiting to be shown); frames never come closer than this,
    // which still leaves room for a 144 Hz screen.
    private const double MinMilliseconds = 6;

    private readonly Dispatcher _dispatcher;
    private readonly Action<long> _frame;
    private volatile bool _running = true;
    private int _waiting;
    private long _shownAt;

    public FramePacer(Dispatcher dispatcher, Action<long> frame)
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
            {
                Volatile.Write(ref _shownAt, ShownAt());
                _dispatcher.BeginInvoke(DispatcherPriority.Render, OnFrame);
            }
        }
    }

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
